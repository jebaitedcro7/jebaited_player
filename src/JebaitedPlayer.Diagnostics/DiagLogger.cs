using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Threading.Channels;

namespace JebaitedPlayer.Diagnostics;

/// <summary>
/// Lokalni logger u procesu playera. Pozivatelj samo stavlja zapis u ograničeni red (bez I/O), a jedna pozadinska dretva
/// ih skuplja i upisuje u logs\GGGG\MM\GGGG-MM-DD\{session,errors,diagnostics}.log (JSON Lines).
/// ERROR i CRITICAL imaju vlastiti red i ne odbacuju se; pri punom redu odbacuju se DEBUG i INFO (broji se).
/// Nijedna greška loggera ne izlazi iz njega (vidi logger_status.log).
/// </summary>
public sealed class DiagLogger : IDisposable
{
    private const int PriorityCapacity = 512;
    private const string MarkerPrefix = "active-";
    private const string MarkerExt = ".session";

    private static readonly JavaScriptEncoder Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;

    private readonly string _root;
    private readonly string _version;
    private readonly LoggerOptions _options;
    private readonly Channel<Item> _normal;
    private readonly Channel<Item> _priority;
    private readonly ManualResetEventSlim _signal = new(false);
    private readonly Queue<byte[]> _ring = new();
    private readonly MemoryStream _buffer = new();
    private readonly object _lifecycle = new();

    private Thread? _thread;
    private volatile bool _stopping;
    private long _seq;
    private long _dropped;
    private long _droppedReported;
    private int _afterRemaining;
    private int _writeFailures;
    private volatile bool _writeDisabled;
    private volatile bool _diskCritical;
    private volatile bool _errorsOnly;
    private Timer? _janitorTimer;
    private string _sessionId = string.Empty;
    private DateTimeOffset _startedAt;

    public DiagLogger(string logsRoot, string version, LoggerOptions? options = null)
    {
        _root = logsRoot;
        _version = version;
        _options = options ?? LoggerOptions.Load(Path.Combine(logsRoot, "logger_config.json"));
        _normal = Channel.CreateBounded<Item>(new BoundedChannelOptions(_options.QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait, // TryWrite vraća false kad je pun (DropWrite bi tiho odbacio bez brojanja)
            SingleReader = true,
        });
        _priority = Channel.CreateBounded<Item>(new BoundedChannelOptions(PriorityCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });
    }

    public string SessionId => _sessionId;

    public bool IsRunning => _thread is { IsAlive: true } && !_stopping;

    /// <summary>Koliko je zapisa odbačeno zbog punog reda (DEBUG/INFO).</summary>
    public long DroppedCount => Interlocked.Read(ref _dropped);

    public string RootDir => _root;

    public string StatusFile => Path.Combine(_root, "logger_status.log");

    public string MarkerFile => Path.Combine(_root, MarkerPrefix + _sessionId + MarkerExt);

    /// <summary>Putanja dnevne mape za zadani dan.</summary>
    public string DayDir(DateTime day) =>
        Path.Combine(_root, day.ToString("yyyy", CultureInfo.InvariantCulture), day.ToString("MM", CultureInfo.InvariantCulture),
            day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

    /// <summary>Pokreće logger i sesiju. Ako je prethodna sesija ostala s markerom, bilježi njezin neočekivani kraj.</summary>
    public void Start()
    {
        lock (_lifecycle)
        {
            if (_thread != null || !_options.Enabled)
            {
                return;
            }

            _startedAt = DateTimeOffset.Now;
            _sessionId = $"s-{_startedAt:yyyyMMdd-HHmmss}-{Random.Shared.Next(0x1000):x3}";
            var crashed = new List<string>();
            try
            {
                Directory.CreateDirectory(_root);
                crashed = FindAbandonedSessions();
                using Process me = Process.GetCurrentProcess();
                File.WriteAllText(MarkerFile, $"{me.Id}|{me.StartTime.ToUniversalTime().Ticks}|{_startedAt:O}");
                if (!File.Exists(Path.Combine(_root, "logger_config.json")))
                {
                    _options.Save(Path.Combine(_root, "logger_config.json"));
                }
            }
            catch (Exception ex)
            {
                Status($"Start: preparing the folder failed: {ex.GetType().Name}: {ex.Message}");
            }

            _janitorTimer = new Timer(_ => RunJanitor(), null, TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(_options.JanitorMinutes));
            _thread = new Thread(Run) { IsBackground = true, Name = "jebaited-logger", Priority = ThreadPriority.BelowNormal };
            _thread.Start();
            Status($"Logger started, session {_sessionId}, version {_version}.");

            Log(LogLevel.Info, LogCat.App, "session.start", "DiagLogger", "Session started", ("version", _version), ("os", Environment.OSVersion.Version.ToString()));
            foreach (string prevId in crashed)
            {
                Log(LogLevel.Critical, LogCat.Stability, "session.crashed", "DiagLogger",
                    "Previous session did not end cleanly (process was terminated without a clean exit)", ("previousSession", prevId));
            }
        }
    }

    /// <summary>
    /// Markeri sesija čiji proces više ne postoji (ili je pid preuzeo drugi proces): ta sesija je završila nasilno.
    /// Marker živog procesa (drugi otvoreni player) se ne dira. Pronađeni markeri se brišu.
    /// </summary>
    internal List<string> FindAbandonedSessions()
    {
        var result = new List<string>();
        foreach (string file in Directory.EnumerateFiles(_root, MarkerPrefix + "*" + MarkerExt))
        {
            try
            {
                string[] parts = File.ReadAllText(file).Split('|');
                bool alive = false;
                if (parts.Length >= 2 && int.TryParse(parts[0], out int pid) && long.TryParse(parts[1], out long ticks))
                {
                    try
                    {
                        using Process p = Process.GetProcessById(pid);
                        alive = Math.Abs(p.StartTime.ToUniversalTime().Ticks - ticks) < TimeSpan.TicksPerSecond;
                    }
                    catch (Exception)
                    {
                        alive = false;
                    }
                }

                if (!alive)
                {
                    string name = Path.GetFileNameWithoutExtension(file);
                    result.Add(name[MarkerPrefix.Length..]);
                    File.Delete(file);
                }
            }
            catch (Exception)
            {
                // Marker koji se ne može pročitati ostaje do sljedećeg pokušaja.
            }
        }

        return result;
    }

    /// <summary>Zabilježi događaj. Nikad ne baca iznimku i ne blokira.</summary>
    public void Log(LogLevel level, string category, string type, string component, string message,
        params (string Key, object? Value)[] data) =>
        Enqueue(level, category, type, component, message, null, data);

    public void Log(LogLevel level, string category, string type, string component, string message, Exception? ex,
        params (string Key, object? Value)[] data) =>
        Enqueue(level, category, type, component, message, ex, data);

    private void Enqueue(LogLevel level, string category, string type, string component, string message, Exception? ex,
        (string Key, object? Value)[] data)
    {
        try
        {
            if (_thread is null || _stopping)
            {
                return;
            }

            var item = new Item(DateTimeOffset.Now, level, category, type, component, message,
                Interlocked.Increment(ref _seq), ex?.ToString(), data);
            ChannelWriter<Item> writer = level >= LogLevel.Error ? _priority.Writer : _normal.Writer;
            if (!writer.TryWrite(item))
            {
                Interlocked.Increment(ref _dropped);
            }

            _signal.Set();
        }
        catch (Exception)
        {
            // Logger nikad ne smije ugroziti player.
        }
    }

    /// <summary>Zaustavlja logger: ispraznjuje red (rok), bilježi kraj sesije i briše marker (čisti izlaz).</summary>
    public void Stop(TimeSpan? timeout = null)
    {
        lock (_lifecycle)
        {
            if (_thread is null || _stopping)
            {
                return;
            }

            try
            {
                Log(LogLevel.Info, LogCat.App, "session.end", "DiagLogger", "Session ended",
                    ("durationSec", (long)(DateTimeOffset.Now - _startedAt).TotalSeconds), ("clean", true));
            }
            catch (Exception)
            {
            }

            _janitorTimer?.Dispose();
            _stopping = true;
            _signal.Set();
            if (!_thread.Join(timeout ?? TimeSpan.FromSeconds(2)))
            {
                Status("Stop: writer did not finish in time, marker is kept.");
                return;
            }

            try
            {
                File.Delete(MarkerFile);
            }
            catch (Exception ex)
            {
                Status($"Stop: marker was not deleted: {ex.Message}");
            }

            Status($"Logger stopped, dropped events: {DroppedCount}.");
        }
    }

    /// <summary>
    /// Zaustavljanje pri padu procesa: ispraznjuje red (kratak rok), ali NE briše marker ni ne bilježi čist kraj,
    /// pa se sljedeći start prijavi kao neuredan završetak prethodne sesije.
    /// </summary>
    public void Abort(TimeSpan? timeout = null)
    {
        lock (_lifecycle)
        {
            if (_thread is null || _stopping)
            {
                return;
            }

            _janitorTimer?.Dispose();
            _stopping = true;
            _signal.Set();
            _thread.Join(timeout ?? TimeSpan.FromMilliseconds(1500));
            Status($"Logger aborted because of a process crash, dropped events: {DroppedCount}.");
        }
    }

    public void Dispose() => Stop();

    // --- Writer dretva -----------------------------------------------------------------------------------------

    private void Run()
    {
        try
        {
            while (true)
            {
                _signal.Wait();
                if (!_stopping)
                {
                    Thread.Sleep(_options.FlushMs); // skupljanje u paket
                }

                _signal.Reset();
                Drain();
                if (_stopping)
                {
                    Drain();
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            Status($"Writer thread failed: {ex}");
        }
    }

    private void Drain()
    {
        var batch = new List<Item>(128);
        while (true)
        {
            batch.Clear();
            while (batch.Count < 256 && (_priority.Reader.TryRead(out Item? p) || _normal.Reader.TryRead(out p)))
            {
                batch.Add(p);
            }

            if (batch.Count == 0)
            {
                break;
            }

            // Redoslijed po seq (dva reda se miješaju samo po prioritetu čitanja).
            batch.Sort(static (a, b) => a.Seq.CompareTo(b.Seq));
            foreach (Item item in batch)
            {
                WriteItem(item);
            }
        }

        long dropped = Interlocked.Read(ref _dropped);
        if (dropped > _droppedReported)
        {
            long delta = dropped - _droppedReported;
            _droppedReported = dropped;
            Status($"Events dropped because the queue was full: {delta} (total {dropped}).");
            WriteItem(new Item(DateTimeOffset.Now, LogLevel.Warning, LogCat.Logger, "logger.dropped", "DiagLogger",
                "Events dropped because the queue was full", Interlocked.Increment(ref _seq), null, [("count", delta), ("total", dropped)]));
        }
    }

    private void WriteItem(Item item)
    {
        byte[] line = Serialize(item);
        string dir = DayDir(item.Time.LocalDateTime);

        if (item.Level >= LogLevel.Warning)
        {
            // Kontekst prije greške: sve iz prstena što još nije u errors.log (označeno).
            if (_afterRemaining == 0)
            {
                foreach (byte[] ctx in _ring)
                {
                    Append(dir, "errors.log", ctx);
                }

                _ring.Clear();
            }

            _afterRemaining = item.Level >= LogLevel.Error ? _options.ContextAfter : Math.Max(_afterRemaining, 0);
            Append(dir, "errors.log", line);
        }
        else if (_afterRemaining > 0)
        {
            _afterRemaining--;
            Append(dir, "errors.log", line);
        }
        else if (_options.ContextBefore > 0)
        {
            _ring.Enqueue(line);
            while (_ring.Count > _options.ContextBefore)
            {
                _ring.Dequeue();
            }
        }

        if (item.Level >= (_errorsOnly ? LogLevel.Warning : _options.MinLevel) && item.Level > LogLevel.Debug)
        {
            Append(dir, "session.log", line);
        }
        else if (item.Level == LogLevel.Debug && _options.DebugFile)
        {
            Append(dir, "diagnostics.log", line);
        }

    }

    private byte[] Serialize(Item item)
    {
        _buffer.SetLength(0);
        using (var w = new Utf8JsonWriter(_buffer, new JsonWriterOptions { Encoder = Encoder }))
        {
            w.WriteStartObject();
            w.WriteString("t", item.Time.ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", CultureInfo.InvariantCulture));
            w.WriteString("lvl", item.Level.ToString().ToUpperInvariant());
            w.WriteString("cat", item.Category);
            w.WriteString("type", item.Type);
            w.WriteString("comp", item.Component);
            w.WriteString("msg", Redactor.Clean(item.Message));
            w.WriteString("sid", _sessionId);
            w.WriteNumber("seq", item.Seq);
            w.WriteString("ver", _version);
            if (item.Data is { Length: > 0 })
            {
                w.WriteStartObject("d");
                foreach ((string key, object? value) in item.Data)
                {
                    WriteValue(w, key, value);
                }

                w.WriteEndObject();
            }

            if (item.Exception != null)
            {
                w.WriteString("ex", Redactor.Clean(item.Exception));
            }

            w.WriteEndObject();
        }

        _buffer.WriteByte((byte)'\n');
        return _buffer.ToArray();
    }

    private static void WriteValue(Utf8JsonWriter w, string key, object? value)
    {
        switch (value)
        {
            case null:
                w.WriteNull(key);
                break;
            case bool b:
                w.WriteBoolean(key, b);
                break;
            case int i:
                w.WriteNumber(key, i);
                break;
            case long l:
                w.WriteNumber(key, l);
                break;
            case double d when double.IsFinite(d):
                w.WriteNumber(key, d);
                break;
            case float f when float.IsFinite(f):
                w.WriteNumber(key, f);
                break;
            case Enum e:
                w.WriteString(key, e.ToString());
                break;
            default:
                w.WriteString(key, Redactor.Clean(Convert.ToString(value, CultureInfo.InvariantCulture)));
                break;
        }
    }

    // --- Datoteke ----------------------------------------------------------------------------------------------

    private void Append(string dir, string file, byte[] data)
    {
        if (_writeDisabled || _diskCritical)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, file);
            RotateIfNeeded(path, data.Length);
            using var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            fs.Write(data);
            _writeFailures = 0;
        }
        catch (Exception ex)
        {
            if (++_writeFailures == 1)
            {
                Status($"Write to {file} failed: {ex.GetType().Name}: {ex.Message}");
            }

            if (_writeFailures >= 20)
            {
                _writeDisabled = true;
                Status("Writing is disabled until the next start (repeated write failures, disk may be full).");
            }
        }
    }

    private void RotateIfNeeded(string path, int incoming)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length + incoming <= _options.MaxFileBytes)
        {
            return;
        }

        string stem = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path));
        string ext = Path.GetExtension(path);
        File.Delete($"{stem}.{_options.MaxRotations}{ext}");
        for (int i = _options.MaxRotations - 1; i >= 1; i--)
        {
            if (File.Exists($"{stem}.{i}{ext}"))
            {
                File.Move($"{stem}.{i}{ext}", $"{stem}.{i + 1}{ext}");
            }
        }

        File.Move(path, $"{stem}.1{ext}");
    }

    /// <summary>Jedno čišćenje (na timeru, nikad na UI dretvi). Provjerava i disk: malo mjesta = samo upozorenja i greške, kritično = bez zapisa.</summary>
    internal CleanupResult? RunJanitor()
    {
        try
        {
            CleanupResult r = new LogJanitor(_root, _options).Run();
            _errorsOnly = r.FreeDiskBytes >= 0 && r.FreeDiskBytes < _options.LowDiskBytes;
            bool critical = r.FreeDiskBytes >= 0 && r.FreeDiskBytes < _options.CriticalDiskBytes;
            if (critical != _diskCritical)
            {
                _diskCritical = critical;
                Status(critical ? $"Disk almost full ({r.FreeDiskBytes >> 20} MB free): logging paused." : "Disk space recovered: logging resumed.");
            }

            if (r.Zipped + r.DeletedDays + r.ShrunkDays > 0)
            {
                Log(LogLevel.Info, LogCat.Logger, "logger.cleanup", "LogJanitor", "Log cleanup done",
                    ("zipped", r.Zipped), ("deletedDays", r.DeletedDays), ("shrunkDays", r.ShrunkDays), ("freedKB", r.FreedBytes >> 10), ("totalKB", r.TotalBytes >> 10));
            }

            if (_errorsOnly && !critical)
            {
                Status($"Low disk space ({r.FreeDiskBytes >> 20} MB free): only WARNING and above are written.");
            }

            return r;
        }
        catch (Exception ex)
        {
            Status($"Cleanup failed: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Vlastite poruke loggera (start, stop, odbačeno, greške zapisa). Ograničeno na 256 KB.</summary>
    private void Status(string message)
    {
        try
        {
            Directory.CreateDirectory(_root);
            var info = new FileInfo(StatusFile);
            if (info.Exists && info.Length > 256 * 1024)
            {
                File.Move(StatusFile, StatusFile + ".old", true);
            }

            File.AppendAllText(StatusFile,
                $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)} {Redactor.Clean(message)}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // Ni to ne smije ugroziti player.
        }
    }

    private sealed record Item(DateTimeOffset Time, LogLevel Level, string Category, string Type, string Component,
        string Message, long Seq, string? Exception, (string Key, object? Value)[]? Data);
}

using System.Globalization;
using System.Runtime.InteropServices;
using JebaitedPlayer.Mpv.Native;

namespace JebaitedPlayer.Mpv;

public sealed class MpvPropertyChangedEventArgs(string name, object? value) : EventArgs
{
    public string Name { get; } = name;
    public object? Value { get; } = value;
}

public sealed class MpvFileLoadedEventArgs(bool hasVideo, string? path, string? trackListJson, double? frameRate) : EventArgs
{
    public bool HasVideo { get; } = hasVideo;

    /// <summary>Putanja učitane datoteke.</summary>
    public string? Path { get; } = path;

    /// <summary>mpv "track-list" (JSON) u trenutku učitavanja.</summary>
    public string? TrackListJson { get; } = trackListJson;

    /// <summary>Broj sličica u sekundi iz kontejnera (ako je poznat).</summary>
    public double? FrameRate { get; } = frameRate;
}

public enum MpvEndFileReason
{
    Eof = 0,
    Stop = 2,
    Quit = 3,
    Error = 4,
    Redirect = 5,
}

public sealed class MpvEndFileEventArgs(MpvEndFileReason reason, int error, string? errorText) : EventArgs
{
    public MpvEndFileReason Reason { get; } = reason;
    public int Error { get; } = error;
    public string? ErrorText { get; } = errorText;
}

public sealed class MpvException(int errorCode, string message) : Exception(message)
{
    public int ErrorCode { get; } = errorCode;
}

/// <summary>
/// Tanki omotač oko libmpv klijentskog API-ja.
/// <para>
/// Pravilo: UI dretva nikad ne poziva sinkrone mpv funkcije tijekom reprodukcije, nego samo
/// <see cref="CommandAsync"/>. Tako UI ne može zapeti dok mpv jezgra radi (npr. dok stvara video prozor).
/// </para>
/// <para>
/// Događaji se čitaju u zasebnoj dretvi i prosljeđuju u zadani <see cref="SynchronizationContext"/>.
/// Iznimka je <c>time-pos</c>: on se mijenja na svakom kadru, pa se samo sprema u polje
/// (<see cref="TimePosition"/>), a UI ga čita svojim tempom.
/// </para>
/// </summary>
public sealed class MpvPlayer : IDisposable
{
    private readonly SynchronizationContext? _context;
    private nint _handle;
    private Thread? _eventThread;
    private volatile bool _stopping;
    private long _timePosBits = BitConverter.DoubleToInt64Bits(double.NaN);
    private ulong _nextObserveId;

    public MpvPlayer(SynchronizationContext? context)
    {
        _context = context;
        _handle = LibMpv.mpv_create();
        if (_handle == 0)
        {
            throw new MpvException(-1, Loc.T("error.mpv.create"));
        }
    }

    public static Version ClientApiVersion
    {
        get
        {
            ulong v = LibMpv.mpv_client_api_version();
            return new Version((int)(v >> 16), (int)(v & 0xFFFF));
        }
    }

    public event EventHandler<MpvPropertyChangedEventArgs>? PropertyChanged;
    public event EventHandler? StartFile;
    public event EventHandler<MpvFileLoadedEventArgs>? FileLoaded;
    public event EventHandler<MpvEndFileEventArgs>? EndFile;
    public event EventHandler? PlaybackRestart;
    public event EventHandler? VideoReconfig;
    public event EventHandler? Shutdown;

    /// <summary>Poruke iz mpv loga (prefix, razina, tekst). Poziva se u dretvi događaja, ne u UI dretvi.</summary>
    public event Action<string, string, string>? LogMessage;

    public bool IsInitialized { get; private set; }

    /// <summary>Zadnja poznata pozicija reprodukcije u sekundama (NaN ako nije poznata). Sigurno za čitanje iz bilo koje dretve.</summary>
    public double TimePosition => BitConverter.Int64BitsToDouble(Interlocked.Read(ref _timePosBits));

    /// <summary>Postavlja opciju prije inicijalizacije. Vraća false ako je mpv odbije (npr. ne postoji u ovoj verziji).</summary>
    public bool TrySetOption(string name, string value)
    {
        EnsureHandle();
        return LibMpv.mpv_set_option_string(_handle, name, value) >= 0;
    }

    public void SetOption(string name, string value)
    {
        EnsureHandle();
        int r = LibMpv.mpv_set_option_string(_handle, name, value);
        if (r < 0)
        {
            throw new MpvException(r, Loc.T("error.mpv.option", name, ErrorString(r)));
        }
    }

    public void Initialize()
    {
        EnsureHandle();
        if (IsInitialized)
        {
            return;
        }

        int r = LibMpv.mpv_initialize(_handle);
        if (r < 0)
        {
            throw new MpvException(r, $"mpv_initialize: {ErrorString(r)}");
        }

        IsInitialized = true;
        _eventThread = new Thread(EventLoop) { IsBackground = true, Name = "mpv-events" };
        _eventThread.Start();
    }

    public void ObserveProperty(string name, MpvFormat format)
    {
        EnsureHandle();
        int r = LibMpv.mpv_observe_property(_handle, ++_nextObserveId, name, format);
        if (r < 0)
        {
            throw new MpvException(r, Loc.T("error.mpv.observe", name, ErrorString(r)));
        }
    }

    public void RequestLogMessages(string minLevel)
    {
        EnsureHandle();
        LibMpv.mpv_request_log_messages(_handle, minLevel);
    }

    /// <summary>Asinkrona naredba: ne čeka mpv jezgru. Vraća false ako naredba nije prihvaćena.</summary>
    public bool CommandAsync(params string[] args) => RunCommand(args, async: true);

    /// <summary>Sinkrona naredba. Ne koristiti iz UI dretve tijekom reprodukcije.</summary>
    public void Command(params string[] args)
    {
        if (!RunCommand(args, async: false))
        {
            throw new MpvException(-1, Loc.T("error.mpv.command", args[0]));
        }
    }

    public bool SetPropertyAsync(string name, string value) => CommandAsync("set", name, value);

    public bool SetPropertyAsync(string name, bool value) => CommandAsync("set", name, value ? "yes" : "no");

    // Puna preciznost ("R"): npr. brzina titlova 1,0427083 ne smije se zaokružiti (2 h filma = sekunde razlike).
    public bool SetPropertyAsync(string name, double value) =>
        CommandAsync("set", name, value.ToString("R", CultureInfo.InvariantCulture));

    /// <summary>Sinkrono čitanje svojstva. Ne koristiti iz UI dretve tijekom reprodukcije.</summary>
    public string? GetPropertyString(string name)
    {
        nint h = _handle;
        if (h == 0 || _stopping)
        {
            return null;
        }

        nint p = LibMpv.mpv_get_property_string(h, name);
        if (p == 0)
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringUTF8(p);
        }
        finally
        {
            LibMpv.mpv_free(p);
        }
    }

    public double? GetPropertyDouble(string name)
    {
        nint h = _handle;
        if (h == 0 || _stopping)
        {
            return null;
        }

        return LibMpv.mpv_get_property_double(h, name, MpvFormat.Double, out double v) >= 0 ? v : null;
    }

    public long? GetPropertyLong(string name)
    {
        nint h = _handle;
        if (h == 0 || _stopping)
        {
            return null;
        }

        return LibMpv.mpv_get_property_long(h, name, MpvFormat.Int64, out long v) >= 0 ? v : null;
    }

    public static string ErrorString(int error) =>
        Marshal.PtrToStringUTF8(LibMpv.mpv_error_string(error)) ?? error.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Gasi mpv. Poziva se iz pozadinske dretve dok UI dretva i dalje obrađuje poruke,
    /// jer mpv pri gašenju uništava svoj prozor, koji je dijete našeg prozora.
    /// </summary>
    public void Dispose()
    {
        nint h = _handle;
        if (h == 0)
        {
            return;
        }

        _stopping = true;
        if (_eventThread != null)
        {
            LibMpv.mpv_wakeup(h);
            if (!_eventThread.Join(TimeSpan.FromSeconds(5)))
            {
                // Dretva događaja nije stala; radije ne uništavamo handle nego da riskiramo pad.
                return;
            }
        }

        _handle = 0;
        LibMpv.mpv_terminate_destroy(h);
    }

    private bool RunCommand(string[] args, bool async)
    {
        nint h = _handle;
        if (h == 0 || _stopping || args.Length == 0)
        {
            return false;
        }

        var ptrs = new nint[args.Length + 1];
        try
        {
            for (int i = 0; i < args.Length; i++)
            {
                ptrs[i] = Marshal.StringToCoTaskMemUTF8(args[i]);
            }

            int r = async ? LibMpv.mpv_command_async(h, 0, ptrs) : LibMpv.mpv_command(h, ptrs);
            if (r < 0)
            {
                LogMessage?.Invoke("client", "warn", $"Naredba '{args[0]}' odbijena: {ErrorString(r)}");
                return false;
            }

            return true;
        }
        finally
        {
            foreach (nint p in ptrs)
            {
                if (p != 0)
                {
                    Marshal.FreeCoTaskMem(p);
                }
            }
        }
    }

    private void EventLoop()
    {
        while (!_stopping)
        {
            nint ptr = LibMpv.mpv_wait_event(_handle, -1);
            if (_stopping || ptr == 0)
            {
                break;
            }

            MpvEvent ev = Marshal.PtrToStructure<MpvEvent>(ptr);
            try
            {
                HandleEvent(ev);
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke("client", "error", "Greška u obradi mpv događaja: " + ex);
            }

            if (ev.EventId == MpvEventId.Shutdown)
            {
                break;
            }
        }
    }

    private void HandleEvent(MpvEvent ev)
    {
        switch (ev.EventId)
        {
            case MpvEventId.PropertyChange:
                HandlePropertyChange(ev.Data);
                break;

            case MpvEventId.LogMessage:
            {
                var m = Marshal.PtrToStructure<MpvEventLogMessage>(ev.Data);
                LogMessage?.Invoke(Utf8(m.Prefix), Utf8(m.Level), Utf8(m.Text).TrimEnd());
                break;
            }

            case MpvEventId.StartFile:
                Post(() => StartFile?.Invoke(this, EventArgs.Empty));
                break;

            case MpvEventId.FileLoaded:
            {
                // Sinkroni upiti su ovdje sigurni jer smo u dretvi događaja, a ne u UI dretvi.
                var args = new MpvFileLoadedEventArgs(
                    GetPropertyString("current-tracks/video/id") != null,
                    GetPropertyString("path"),
                    GetPropertyString("track-list"),
                    GetPropertyDouble("container-fps"));
                Post(() => FileLoaded?.Invoke(this, args));
                break;
            }

            case MpvEventId.EndFile:
            {
                var e = Marshal.PtrToStructure<MpvEventEndFile>(ev.Data);
                var args = new MpvEndFileEventArgs(
                    (MpvEndFileReason)e.Reason,
                    e.Error,
                    e.Error < 0 ? ErrorString(e.Error) : null);
                Post(() => EndFile?.Invoke(this, args));
                break;
            }

            case MpvEventId.PlaybackRestart:
                Post(() => PlaybackRestart?.Invoke(this, EventArgs.Empty));
                break;

            case MpvEventId.VideoReconfig:
                Post(() => VideoReconfig?.Invoke(this, EventArgs.Empty));
                break;

            case MpvEventId.CommandReply:
                if (ev.Error < 0)
                {
                    LogMessage?.Invoke("client", "warn", "Asinkrona naredba nije uspjela: " + ErrorString(ev.Error));
                }

                break;

            case MpvEventId.Shutdown:
                Post(() => Shutdown?.Invoke(this, EventArgs.Empty));
                break;
        }
    }

    private void HandlePropertyChange(nint data)
    {
        var p = Marshal.PtrToStructure<MpvEventProperty>(data);
        string name = Utf8(p.Name);
        object? value = p.Data == 0
            ? null
            : p.Format switch
            {
                MpvFormat.Flag => Marshal.ReadInt32(p.Data) != 0,
                MpvFormat.Int64 => Marshal.ReadInt64(p.Data),
                MpvFormat.Double => BitConverter.Int64BitsToDouble(Marshal.ReadInt64(p.Data)),
                MpvFormat.String or MpvFormat.OsdString => Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(p.Data)),
                _ => null,
            };

        if (name == "time-pos")
        {
            double t = value as double? ?? double.NaN;
            Interlocked.Exchange(ref _timePosBits, BitConverter.DoubleToInt64Bits(t));
            return;
        }

        Post(() => PropertyChanged?.Invoke(this, new MpvPropertyChangedEventArgs(name, value)));
    }

    private void Post(Action action)
    {
        if (_stopping)
        {
            return;
        }

        if (_context is null)
        {
            action();
        }
        else
        {
            _context.Post(static state => ((Action)state!)(), action);
        }
    }

    private void EnsureHandle()
    {
        ObjectDisposedException.ThrowIf(_handle == 0, this);
    }

    private static string Utf8(nint p) => p == 0 ? string.Empty : Marshal.PtrToStringUTF8(p) ?? string.Empty;
}

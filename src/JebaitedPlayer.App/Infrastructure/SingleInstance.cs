using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace JebaitedPlayer.Infrastructure;

/// <summary>
/// Jedna instanca playera po korisniku i Windows sesiji. Svako drugo pokretanje (dvoklik na video, "Otvori pomoću",
/// više označenih datoteka u Exploreru) preda svoje datoteke već otvorenom playeru preko imenovane cijevi i odmah se zatvori.
/// <para>Cijev prihvaća samo procese istog korisnika (<see cref="PipeOptions.CurrentUserOnly"/>), a poruka sadrži samo
/// apsolutne putanje (nikad zastavice naredbenog retka).</para>
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    public const string DefaultName = "jebaited_player";
    private const int MaxMessageBytes = 1 << 20;

    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _gate = new();
    private readonly List<IReadOnlyList<string>> _pending = [];
    private Action<IReadOnlyList<string>>? _handler;
    private bool _owned = true;

    private SingleInstance(Mutex mutex, string pipeName)
    {
        _mutex = mutex;
        _pipeName = pipeName;
        _ = Task.Run(ServeAsync);
    }

    /// <summary>Koliko je zahtjeva primljeno od drugih pokretanja (za dijagnostiku).</summary>
    public int ReceivedBatches { get; private set; }

    /// <summary>
    /// Postani glavna instanca ili predaj datoteke već otvorenom playeru.
    /// Vraća instancu kad je ovaj proces glavni; <c>null</c> kad su datoteke predane (proces se treba zatvoriti).
    /// Ako glavni player ne odgovara (npr. upravo se zatvara), nakon <paramref name="timeout"/> vraća samostalnu instancu
    /// bez cijevi, da se video ipak otvori.
    /// </summary>
    public static SingleInstance? Acquire(string name, IReadOnlyList<string> files, TimeSpan timeout, out bool standalone)
    {
        standalone = false;
        string scope = Scope(name);
        var mutex = new Mutex(false, @"Local\" + scope);
        var deadline = Stopwatch.StartNew();
        while (true)
        {
            if (TryOwn(mutex))
            {
                return new SingleInstance(mutex, scope);
            }

            if (TrySend(scope, files, TimeSpan.FromMilliseconds(800)))
            {
                mutex.Dispose();
                return null;
            }

            if (deadline.Elapsed > timeout)
            {
                mutex.Dispose();
                standalone = true;
                return null;
            }

            Thread.Sleep(100);
        }
    }

    /// <summary>Naziv mutexa i cijevi: odvojen po korisniku (SID) i Windows sesiji.</summary>
    internal static string Scope(string name)
    {
        string sid = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        int session = Process.GetCurrentProcess().SessionId;
        return $"{name}-{sid}-{session}";
    }

    /// <summary>
    /// Primatelj datoteka iz drugih pokretanja (poziva se na dretvi cijevi). Zahtjevi stigli prije postavljanja
    /// primatelja (dok se prozor još otvara) predaju se odmah, redom kojim su stigli.
    /// </summary>
    public void SetHandler(Action<IReadOnlyList<string>> handler)
    {
        List<IReadOnlyList<string>> pending;
        lock (_gate)
        {
            _handler = handler;
            pending = [.. _pending];
            _pending.Clear();
        }

        foreach (IReadOnlyList<string> files in pending)
        {
            handler(files);
        }
    }

    /// <summary>
    /// Pusti ulogu glavne instance (pri zatvaranju prozora), da se player pokrenut u tom trenutku otvori normalno.
    /// Mora se pozvati na dretvi koja je instancu stvorila.
    /// </summary>
    public void Release()
    {
        _cts.Cancel();
        if (_owned)
        {
            _owned = false;
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Pozvano s druge dretve: mutex se oslobodi sam kad proces završi.
            }
        }
    }

    public void Dispose()
    {
        Release();
        _mutex.Dispose();
        _cts.Dispose();
    }

    /// <summary>Poruka u cijevi: jedan redak JSON-a s putanjama.</summary>
    internal static string Serialize(IReadOnlyList<string> files) => JsonSerializer.Serialize(new Message { Files = [.. files] });

    /// <summary>Prihvaća samo apsolutne lokalne ili mrežne putanje; zastavice i relativne putanje se odbacuju.</summary>
    internal static IReadOnlyList<string> Deserialize(string line)
    {
        Message? message;
        try
        {
            message = JsonSerializer.Deserialize<Message>(line);
        }
        catch (JsonException)
        {
            return [];
        }

        return (message?.Files ?? [])
            .Where(f => !string.IsNullOrWhiteSpace(f) && f.Length < 32_000 && !f.StartsWith("--", StringComparison.Ordinal))
            .Where(f => Path.IsPathFullyQualified(f) && !f.StartsWith(@"\\?\", StringComparison.Ordinal) && !f.StartsWith(@"\\.\", StringComparison.Ordinal))
            .Take(10_000)
            .ToList();
    }

    private static bool TryOwn(Mutex mutex)
    {
        try
        {
            return mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            // Prethodni player se srušio dok je bio glavni: sada je ovaj glavni.
            return true;
        }
    }

    internal static bool TrySend(string pipeName, IReadOnlyList<string> files, TimeSpan connectTimeout) =>
        TrySendAsync(pipeName, files, connectTimeout).GetAwaiter().GetResult();

    private static async Task<bool> TrySendAsync(string pipeName, IReadOnlyList<string> files, TimeSpan connectTimeout)
    {
        try
        {
            await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await client.ConnectAsync((int)connectTimeout.TotalMilliseconds).ConfigureAwait(false);

            // Pokrenuo ga je korisnik, pa smije dopustiti glavnom playeru da dođe u prvi plan.
            if (GetNamedPipeServerProcessId(client.SafePipeHandle.DangerousGetHandle(), out uint serverPid))
            {
                AllowSetForegroundWindow(serverPid);
            }

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            byte[] payload = Encoding.UTF8.GetBytes(Serialize(files) + "\n");
            await client.WriteAsync(payload, timeout.Token).ConfigureAwait(false);
            await client.FlushAsync(timeout.Token).ConfigureAwait(false);

            // Potvrda da je glavni player primio datoteke.
            var ack = new byte[1];
            int n = await client.ReadAsync(ack, timeout.Token).ConfigureAwait(false);
            return n == 1 && ack[0] == 'K';
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            return false;
        }
    }

    private async Task ServeAsync()
    {
        CancellationToken ct = _cts.Token;
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream server;
            try
            {
                server = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            }
            catch (IOException ex)
            {
                Log.Warn($"Jedna instanca: cijev nije otvorena ({ex.Message})");
                return;
            }

            try
            {
                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException)
            {
                await server.DisposeAsync().ConfigureAwait(false);
                if (ct.IsCancellationRequested)
                {
                    return;
                }

                continue;
            }

            // Svaka veza se obrađuje zasebno, a odmah se otvara nova cijev (Explorer pokrene po proces za svaku datoteku).
            _ = Task.Run(() => HandleAsync(server, ct), CancellationToken.None);
        }
    }

    private async Task HandleAsync(NamedPipeServerStream server, CancellationToken ct)
    {
        await using (server.ConfigureAwait(false))
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                var buffer = new MemoryStream();
                var chunk = new byte[8192];
                while (buffer.Length < MaxMessageBytes)
                {
                    int n = await server.ReadAsync(chunk, timeout.Token).ConfigureAwait(false);
                    if (n == 0)
                    {
                        break;
                    }

                    buffer.Write(chunk, 0, n);
                    if (Array.IndexOf(chunk, (byte)'\n', 0, n) >= 0)
                    {
                        break;
                    }
                }

                IReadOnlyList<string> files = Deserialize(Encoding.UTF8.GetString(buffer.ToArray()).TrimEnd('\n', '\r'));
                await server.WriteAsync(new[] { (byte)'K' }, timeout.Token).ConfigureAwait(false);
                await server.FlushAsync(timeout.Token).ConfigureAwait(false);
                Deliver(files);
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException)
            {
                // Drugi proces je odustao ili se player zatvara.
            }
        }
    }

    private void Deliver(IReadOnlyList<string> files)
    {
        Action<IReadOnlyList<string>>? handler;
        lock (_gate)
        {
            ReceivedBatches++;
            handler = _handler;
            if (handler is null)
            {
                _pending.Add(files);
                return;
            }
        }

        handler(files);
    }

    private sealed class Message
    {
        public int V { get; set; } = 1;

        public List<string> Files { get; set; } = [];
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeServerProcessId(nint pipe, out uint serverProcessId);

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(uint processId);
}

/// <summary>
/// Spaja datoteke koje stignu u kratkom razmaku u jedan popis. Explorer za više označenih datoteka pokrene player
/// jednom po datoteci (gotovo istodobno), a korisnik očekuje jedan popis za reprodukciju, ne samo zadnju datoteku.
/// </summary>
internal sealed class OpenRequestMerger(TimeSpan window)
{
    private readonly List<string> _current = [];
    private DateTime _lastAt = DateTime.MinValue;

    /// <summary>Vraća cijeli popis za otvaranje: unutar <c>window</c> od prethodnog zahtjeva to je unija oba.</summary>
    public IReadOnlyList<string> Add(IEnumerable<string> files, DateTime now)
    {
        if (now - _lastAt > window)
        {
            _current.Clear();
        }

        foreach (string file in files)
        {
            if (!_current.Contains(file, StringComparer.OrdinalIgnoreCase))
            {
                _current.Add(file);
            }
        }

        _lastAt = now;
        return [.. _current];
    }
}

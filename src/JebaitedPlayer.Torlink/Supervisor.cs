using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace JebaitedPlayer.Torlink;

public enum SupervisorState
{
    Stopped,
    Starting,
    Running,
    Failed,
}

/// <summary>Ograničenje ponovnih pokretanja nakon pada: najviše <see cref="Max"/> u prozoru <see cref="Window"/>.</summary>
public sealed class RestartPolicy
{
    public int Max { get; init; } = 3;
    public TimeSpan Window { get; init; } = TimeSpan.FromMinutes(5);

    private readonly Queue<DateTimeOffset> _times = new();

    /// <summary>Bilježi pad; vraća false kad je prekoračeno ograničenje (tada se odustaje).</summary>
    public bool TryRegister(DateTimeOffset now)
    {
        while (_times.Count > 0 && now - _times.Peek() > Window)
        {
            _times.Dequeue();
        }

        if (_times.Count >= Max)
        {
            return false;
        }

        _times.Enqueue(now);
        return true;
    }

    public void Reset() => _times.Clear();
}

/// <summary>
/// Nadzornik <c>torlnk serve</c>: jedan proces, slobodan port na 127.0.0.1, slučajan token samo u okolini procesa,
/// zdravstvena provjera, uklanjanje procesa-siročeta (pidfile), ponovno pokretanje nakon pada (najviše 3 u 5 min).
/// Proces ne radi dok ga nitko ne traži: pokreće ga <see cref="TorlinkHost"/>.
/// </summary>
public sealed class Supervisor : IDisposable
{
    private readonly TorlinkPaths _paths;
    private readonly IProcessLauncher _launcher;
    private readonly TimeProvider _clock;
    private readonly Func<int, string, ApiClient> _apiFactory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly RestartPolicy _policy = new();
    private IServeProcess? _process;
    private TorlinkOptions _options = new();
    private bool _stopping;
    private bool _healthy;

    public Supervisor(TorlinkPaths paths, IProcessLauncher launcher, TimeProvider? clock = null, Func<int, string, ApiClient>? apiFactory = null)
    {
        _paths = paths;
        _launcher = launcher;
        _clock = clock ?? TimeProvider.System;
        _apiFactory = apiFactory ?? ((port, token) => new ApiClient(port, token));
    }

    public SupervisorState State { get; private set; } = SupervisorState.Stopped;
    public ApiClient? Api { get; private set; }
    public string? LastError { get; private set; }

    /// <summary>Id procesa <c>serve</c> (dijagnostika i testovi); null dok ne radi.</summary>
    public int? ProcessId => _process?.Id;
    public IReadOnlyList<string> RecentOutput => _process?.RecentOutput ?? [];

    /// <summary>Zamjena za provjeru zdravlja (testovi); zadano zove <c>GET /health</c>.</summary>
    public Func<ApiClient, CancellationToken, Task<bool>>? HealthProbe { get; set; }

    public TimeSpan HealthTimeout { get; set; } = TimeSpan.FromSeconds(20);

    public event Action? StateChanged;

    // Cijeli rad (pokretanje procesa, ubijanje, datoteke) ide na dretvu iz skupa: UI dretva se nikad ne blokira, čak ni kad je
    // semafor slobodan (tada bi await s ConfigureAwait(false) nastavio sinkrono na pozivateljevoj dretvi).
    public Task<bool> StartAsync(TorlinkOptions options, CancellationToken ct = default) =>
        Task.Run(async () =>
        {
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                _options = options;
                if (State == SupervisorState.Running && _process is { HasExited: false })
                {
                    return true;
                }

                return await LaunchAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }, CancellationToken.None);

    public Task StopAsync() =>
        Task.Run(async () =>
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                StopCore();
            }
            finally
            {
                _gate.Release();
            }
        });

    private void StopCore()
    {
        _stopping = true;
        IServeProcess? p = _process;
        _process = null;
        Api?.Dispose();
        Api = null;
        if (p is not null)
        {
            try
            {
                p.Kill();
            }
            finally
            {
                p.Dispose();
            }
        }

        DeletePidFile();
        if (_healthy)
        {
            // Namjerno gašenje zdravog procesa nije pad pri podizanju. Torlink bi inače (ako ugasimo unutar 4 s od starta, npr. odmah nakon
            // zdravstvene provjere) pri sljedećem pokretanju vratio sva preuzimanja PAUZIRANA. Pad koji nismo mi izazvali ostavlja marker.
            DeleteBootMarker();
        }

        _healthy = false;
        _stopping = false;
        Set(SupervisorState.Stopped);
    }

    private void DeleteBootMarker()
    {
        try
        {
            File.Delete(_paths.BootMarkerFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private async Task<bool> LaunchAsync(CancellationToken ct)
    {
        if (!File.Exists(_paths.NodeExe) || !File.Exists(_paths.CliScript))
        {
            LastError = "runtime-missing";
            Set(SupervisorState.Failed);
            return false;
        }

        Set(SupervisorState.Starting);
        KillOrphan();
        TorlinkCommand.WriteConfig(_paths, _options);

        int port = FindFreePort();
        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var psi = TorlinkCommand.StartInfo(_paths, TorlinkCommand.ServeArgs(_paths, _options, port), TorlinkCommand.Environment(_paths, _options, token));

        IServeProcess proc;
        try
        {
            proc = _launcher.Start(psi);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            LastError = "start-failed";
            Set(SupervisorState.Failed);
            return false;
        }

        _process = proc;
        proc.Exited += OnExited;
        WritePidFile(proc.Id);
        ApiClient api = _apiFactory(port, token);
        Api = api;

        bool healthy = await WaitHealthyAsync(api, proc, ct).ConfigureAwait(false);
        if (!healthy)
        {
            LastError = proc.HasExited ? "exited" : "health-timeout";
            StopCore();
            Set(SupervisorState.Failed);
            return false;
        }

        LastError = null;
        _healthy = true;
        Set(SupervisorState.Running);
        return true;
    }

    private async Task<bool> WaitHealthyAsync(ApiClient api, IServeProcess proc, CancellationToken ct)
    {
        // Stvarno vrijeme (ne umetnuti sat): čekanje na proces je uvijek stvarno čekanje.
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < HealthTimeout && !ct.IsCancellationRequested)
        {
            if (proc.HasExited)
            {
                return false;
            }

            bool ok = HealthProbe is not null ? await HealthProbe(api, ct).ConfigureAwait(false) : await api.HealthAsync(ct).ConfigureAwait(false) is not null;
            if (ok)
            {
                return true;
            }

            await Task.Delay(200, ct).ConfigureAwait(false);
        }

        return false;
    }

    private void OnExited(int code)
    {
        if (_stopping)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_stopping || _process is null || !_process.HasExited)
                {
                    return;
                }

                _process.Dispose();
                _process = null;
                Api?.Dispose();
                Api = null;
                DeletePidFile();
                _healthy = false;

                if (!_policy.TryRegister(_clock.GetUtcNow()))
                {
                    LastError = "crash-loop";
                    Set(SupervisorState.Failed);
                    return;
                }

                LastError = "restarted";
                await LaunchAsync(CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        });
    }

    /// <summary>Nakon ručnog "Pokušaj ponovno": briše brojač padova i pokušava iznova.</summary>
    public Task<bool> RetryAsync(TorlinkOptions options, CancellationToken ct = default)
    {
        _policy.Reset();
        return StartAsync(options, ct);
    }

    private void Set(SupervisorState s)
    {
        if (State == s)
        {
            return;
        }

        State = s;
        StateChanged?.Invoke();
    }

    public static int FindFreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        try
        {
            return ((IPEndPoint)l.LocalEndpoint).Port;
        }
        finally
        {
            l.Stop();
        }
    }

    private void WritePidFile(int pid)
    {
        try
        {
            Directory.CreateDirectory(_paths.StateDir);
            File.WriteAllText(_paths.PidFile, pid.ToString());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // dijagnostika; bez pidfilea samo nema čišćenja siročeta
        }
    }

    private void DeletePidFile()
    {
        try
        {
            File.Delete(_paths.PidFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Ako je prošli player stradao bez čišćenja, ubija preostali Torlinkov node.exe (samo naš node.exe iz runtimea).</summary>
    private void KillOrphan()
    {
        try
        {
            if (!File.Exists(_paths.PidFile) || !int.TryParse(File.ReadAllText(_paths.PidFile).Trim(), out int pid))
            {
                return;
            }

            using Process p = Process.GetProcessById(pid);
            string? exe = null;
            try
            {
                exe = p.MainModule?.FileName;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
            }

            if (exe is not null && string.Equals(Path.GetFullPath(exe), Path.GetFullPath(_paths.NodeExe), StringComparison.OrdinalIgnoreCase))
            {
                p.Kill(entireProcessTree: true);
                p.WaitForExit(3000);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            // proces više ne postoji
        }
        finally
        {
            DeletePidFile();
        }
    }

    public void Dispose()
    {
        StopCore();
        _gate.Dispose();
    }
}

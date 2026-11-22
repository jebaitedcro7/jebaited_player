using System.Diagnostics;
using System.Text;

namespace JebaitedPlayer.Torlink;

public sealed record ProcessResult(int ExitCode, string Stdout, string Stderr, bool TimedOut);

/// <summary>Dugotrajan proces (<c>serve</c>). Odvojeno sučelje da se nadzornik može testirati bez pravog Nodea.</summary>
public interface IServeProcess : IDisposable
{
    int Id { get; }
    bool HasExited { get; }
    event Action<int>? Exited;
    void Kill();

    /// <summary>Zadnjih nekoliko redaka izlaza (za dijagnostiku; token se nikad ne ispisuje jer ide samo u okolinu).</summary>
    IReadOnlyList<string> RecentOutput { get; }
}

public interface IProcessLauncher
{
    IServeProcess Start(ProcessStartInfo startInfo);

    /// <summary>Jednokratan proces (<c>search</c>): čeka završetak, najviše <paramref name="timeout"/>; pri otkazivanju ubija proces.</summary>
    Task<ProcessResult> RunAsync(ProcessStartInfo startInfo, TimeSpan timeout, CancellationToken ct);
}

/// <summary>Pravi pokretač: bez prozora, svi procesi u zajedničkom Job Objectu (nestaju s playerom).</summary>
public sealed class ProcessLauncher : IProcessLauncher
{
    private const int MaxCapturedBytes = 10 * 1024 * 1024;

    /// <summary>
    /// Prije pokretanja provjerava da datoteka stvarno izgleda kao program (zaglavlje "MZ"): Windows za tekst preimenovan u .exe može dugo
    /// provjeravati (zaštita, SmartScreen) prije greške, a pokvaren paket treba odmah odbiti i vratiti prethodnu verziju.
    /// </summary>
    internal static void EnsureExecutable(string file)
    {
        byte[] head = new byte[2];
        using (FileStream fs = File.OpenRead(file))
        {
            if (fs.Read(head, 0, 2) < 2 || head[0] != (byte)'M' || head[1] != (byte)'Z')
            {
                throw new System.ComponentModel.Win32Exception("Not a valid executable: " + Path.GetFileName(file));
            }
        }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SetThreadErrorMode(uint newMode, out uint oldMode);

    /// <summary>
    /// Pokretanje programa bez Windowsovih dijaloga o grešci ("Unsupported 16-Bit Application" i sl.) za pokvaren .exe: samo za ovu dretvu i samo
    /// dok traje pokretanje. Greška se tada javlja izuzetkom, a o njoj odlučuje player.
    /// </summary>
    private static void StartQuietly(Process p)
    {
        uint old = 0;
        bool set = OperatingSystem.IsWindows() && SetThreadErrorMode(0x0001 | 0x8000, out old); // SEM_FAILCRITICALERRORS | SEM_NOOPENFILEERRORBOX
        try
        {
            p.Start();
        }
        finally
        {
            if (set)
            {
                SetThreadErrorMode(old, out _);
            }
        }
    }

    public IServeProcess Start(ProcessStartInfo startInfo)
    {
        EnsureExecutable(startInfo.FileName);
        var p = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var proc = new RealServeProcess(p);
        StartQuietly(p);
        JobObject.Shared?.Assign(p);
        proc.BeginReading();
        return proc;
    }

    public async Task<ProcessResult> RunAsync(ProcessStartInfo startInfo, TimeSpan timeout, CancellationToken ct)
    {
        EnsureExecutable(startInfo.FileName);
        using var p = new Process { StartInfo = startInfo };
        StartQuietly(p);
        JobObject.Shared?.Assign(p);

        Task<string> stdout = ReadLimitedAsync(p.StandardOutput);
        Task<string> stderr = ReadLimitedAsync(p.StandardError);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        bool timedOut = false;
        try
        {
            await p.WaitForExitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            timedOut = !ct.IsCancellationRequested;
            TryKill(p);
            if (ct.IsCancellationRequested)
            {
                throw;
            }
        }

        string o = await stdout.ConfigureAwait(false);
        string e = await stderr.ConfigureAwait(false);
        return new ProcessResult(timedOut ? -1 : p.ExitCode, o, e, timedOut);
    }

    internal static void TryKill(Process p)
    {
        try
        {
            if (!p.HasExited)
            {
                p.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            // već završio ili nedostupan
        }
    }

    private static async Task<string> ReadLimitedAsync(StreamReader reader)
    {
        var sb = new StringBuilder();
        char[] buf = new char[8192];
        int read;
        while ((read = await reader.ReadAsync(buf, 0, buf.Length).ConfigureAwait(false)) > 0)
        {
            if (sb.Length + read > MaxCapturedBytes)
            {
                break;
            }

            sb.Append(buf, 0, read);
        }

        return sb.ToString();
    }

    private sealed class RealServeProcess : IServeProcess
    {
        private const int KeepLines = 60;
        private readonly Process _p;
        private readonly Queue<string> _lines = new();
        private readonly object _lock = new();

        public RealServeProcess(Process p)
        {
            _p = p;
            p.Exited += (_, _) => Exited?.Invoke(SafeExitCode());
        }

        public int Id => _p.Id;
        public bool HasExited => SafeHasExited();
        public event Action<int>? Exited;

        public IReadOnlyList<string> RecentOutput
        {
            get
            {
                lock (_lock)
                {
                    return _lines.ToArray();
                }
            }
        }

        public void BeginReading()
        {
            _p.OutputDataReceived += (_, e) => Keep(e.Data);
            _p.ErrorDataReceived += (_, e) => Keep(e.Data);
            _p.BeginOutputReadLine();
            _p.BeginErrorReadLine();
        }

        public void Kill()
        {
            TryKill(_p);
            try
            {
                // Pričekaj da proces stvarno nestane: dok se gasi drži datoteke runtimea pa ih ažuriranje ne može premjestiti.
                _p.WaitForExit(5000);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
            }
        }

        public void Dispose()
        {
            TryKill(_p);
            _p.Dispose();
        }

        private void Keep(string? line)
        {
            if (line is null)
            {
                return;
            }

            lock (_lock)
            {
                _lines.Enqueue(line.Length > 300 ? line[..300] : line);
                while (_lines.Count > KeepLines)
                {
                    _lines.Dequeue();
                }
            }
        }

        private bool SafeHasExited()
        {
            try
            {
                return _p.HasExited;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }

        private int SafeExitCode()
        {
            try
            {
                return _p.ExitCode;
            }
            catch (InvalidOperationException)
            {
                return -1;
            }
        }
    }
}

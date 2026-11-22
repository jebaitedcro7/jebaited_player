using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using JebaitedPlayer.Diagnostics;
using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.Interop;

namespace JebaitedPlayer;

/// <summary>
/// 2.1.12: čuvar pokretanja. Player nikad ne smije raditi bez prozora koji korisnik vidi (2.1.7: proces bez prozora je
/// držao „jednu instancu”, pa se player nije mogao ponovno pokrenuti). Provjera se radi samo dvaput (nakon pokretanja i
/// pri drugom pokretanju), bez stalnog tajmera.
/// </summary>
public partial class MainWindow
{
    /// <summary>Koliko nakon pokretanja čuvar provjerava ima li prozora (dovoljno i za sporo otvaranje mini playera).</summary>
    internal static readonly TimeSpan StartupWatchDelay = TimeSpan.FromSeconds(6);

    private bool _startupGuardActive;
    private DispatcherTimer? _startupWatch;

    /// <summary>Koliko je puta čuvar morao vratiti prozor (za provjeru i dnevnik).</summary>
    internal int WindowRecoveries { get; private set; }

    /// <summary>Pokreće provjeru nakon pokretanja. <paramref name="guardActive"/>: oznaka pokretanja postoji i treba je obrisati.</summary>
    internal void StartStartupWatch(bool guardActive)
    {
        _startupGuardActive = guardActive;
        _startupWatch = new DispatcherTimer(StartupWatchDelay, DispatcherPriority.Background, (_, _) =>
        {
            _startupWatch?.Stop();
            _startupWatch = null;
            EnsurePlayerWindowVisible("startup");
        }, Dispatcher);
        _startupWatch.Start();
    }

    /// <summary>Prvi trenutak kad je prikaz gotov: ako je prozor na zaslonu, oznaka pokretanja se odmah briše.</summary>
    private void CompleteStartupGuardIfVisible()
    {
        if (_startupGuardActive && AnyPlayerWindowUsable())
        {
            CompleteStartupGuard();
        }
    }

    private void CompleteStartupGuard()
    {
        if (!_startupGuardActive)
        {
            return;
        }

        _startupGuardActive = false;
        if (StartupGuard.Complete(AppPaths.DataDir))
        {
            Log.Info("Pokretanje završeno: prozor playera je na zaslonu.");
        }
    }

    /// <summary>
    /// Ima li korisnik barem jedan prozor playera koji vidi i može uhvatiti; ako nema, vraća glavni prozor (normalan, na
    /// sredini radnog područja). Vraća true ako je prozor već bio u redu.
    /// </summary>
    internal bool EnsurePlayerWindowVisible(string reason)
    {
        if (_closing)
        {
            return true;
        }

        if (AnyPlayerWindowUsable())
        {
            CompleteStartupGuard();
            return true;
        }

        WindowRecoveries++;
        DiagLog.Error(LogCat.Stability, "boot.no_window", "MainWindow", "No player window is visible; restoring the main window", null,
            ("reason", reason), ("mainVisible", IsVisible), ("mainState", WindowState.ToString()), ("miniVisible", _mini?.IsVisible));
        RestoreMainWindow();
        if (AnyPlayerWindowUsable())
        {
            CompleteStartupGuard();
        }

        return false;
    }

    /// <summary>Glavni prozor u sigurnom stanju: normalan, na sredini radnog područja, s gumbom na taskbaru, vidljiv i aktivan.</summary>
    internal void RestoreMainWindow()
    {
        _hiddenStart = false;
        if (_mini is { IsVisible: true } mini)
        {
            // Mini nije na zaslonu (inače ne bi trebalo vraćati): ne smiju ostati dva prikaza.
            mini.Hide();
        }

        ShowActivated = true;
        ShowInTaskbar = true;
        Opacity = 1;
        if (_vm.IsFullscreen)
        {
            SetFullscreen(false);
        }

        if (WindowState != WindowState.Normal)
        {
            WindowState = WindowState.Normal;
        }

        Rect work = SystemParameters.WorkArea;
        Width = Math.Min(double.IsNaN(Width) || Width < 400 ? 1100 : Width, work.Width);
        Height = Math.Min(double.IsNaN(Height) || Height < 300 ? 680 : Height, work.Height);
        Left = work.Left + (work.Width - Width) / 2;
        Top = work.Top + (work.Height - Height) / 2;
        if (!IsVisible)
        {
            Show();
        }

        Activate();
        UpdateOverlayVisibility();
        Log.Warn("Glavni prozor vraćen na zaslon (čuvar pokretanja).");
    }

    /// <summary>Glavni prozor ili mini player: vidljiv, nije proziran i barem malo na nekom zaslonu (ili minimiziran na taskbar).</summary>
    internal bool AnyPlayerWindowUsable()
    {
        Rect screen = VirtualScreenPixels();
        return IsUsable(this, screen) || (_mini is { } mini && IsUsable(mini, screen));
    }

    private static bool IsUsable(Window window, Rect screen)
    {
        nint hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == 0 || !GetWindowRect(hwnd, out NativeMethods.RECT r))
        {
            return false;
        }

        var bounds = new Rect(r.Left, r.Top, Math.Max(0, r.Right - r.Left), Math.Max(0, r.Bottom - r.Top));
        return StartupGuard.IsUsable(bounds, screen, window.IsVisible && IsWindowVisible(hwnd), IsIconic(hwnd), window.ShowInTaskbar, window.Opacity);
    }

    private static Rect VirtualScreenPixels() =>
        new(GetSystemMetrics(SmXVirtualScreen), GetSystemMetrics(SmYVirtualScreen),
            Math.Max(0, GetSystemMetrics(SmCxVirtualScreen)), Math.Max(0, GetSystemMetrics(SmCyVirtualScreen)));

    /// <summary>
    /// Samo za testove (uz JEBAITED_DATA_DIR): JEBAITED_TEST_FAULT=hide-windows sakrije sve prozore nakon pokretanja,
    /// kao u 2.1.7, da se provjeri čuvar.
    /// </summary>
    internal static string? TestFault =>
        Environment.GetEnvironmentVariable("JEBAITED_DATA_DIR") is { Length: > 0 } ? Environment.GetEnvironmentVariable("JEBAITED_TEST_FAULT") : null;

    private bool _testFaultApplied;

    private void ApplyTestFaultAfterStartup()
    {
        if (TestFault != "hide-windows" || _testFaultApplied)
        {
            return;
        }

        _testFaultApplied = true;
        Log.Warn("Test: svi prozori playera su sakriveni (JEBAITED_TEST_FAULT=hide-windows).");
        _mini?.Hide();
        Hide();
    }

    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hwnd, out NativeMethods.RECT rect);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint hwnd);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}

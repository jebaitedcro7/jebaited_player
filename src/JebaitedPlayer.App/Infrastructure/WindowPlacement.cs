using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace JebaitedPlayer.Infrastructure;

/// <summary>
/// 2.1.7: prozori (mini player, Equalizer i ostali) uvijek ostaju cijeli unutar radnog područja zaslona na kojem se nalaze
/// (bez taskbara), uz DPI tog zaslona. Radi se na pravom monitoru, pa vrijedi i s više zaslona.
/// </summary>
internal static class WindowPlacement
{
    /// <summary>Prozor pomakne najmanje potrebno da stane u <paramref name="work"/>; veći od područja poravna uz gornji lijevi kut.</summary>
    internal static Rect Clamp(Rect window, Rect work)
    {
        double x = window.Width >= work.Width ? work.Left : Math.Clamp(window.X, work.Left, work.Right - window.Width);
        double y = window.Height >= work.Height ? work.Top : Math.Clamp(window.Y, work.Top, work.Bottom - window.Height);
        return new Rect(x, y, window.Width, window.Height);
    }

    /// <summary>Radno područje (bez taskbara) zaslona koji je najbliži prozoru, u DIP-ovima prozora.</summary>
    internal static Rect WorkAreaFor(Window window)
    {
        try
        {
            double scale = VisualTreeHelper.GetDpi(window).DpiScaleX;
            var px = new NativeRect
            {
                Left = (int)Math.Round(window.Left * scale), Top = (int)Math.Round(window.Top * scale),
                Right = (int)Math.Round((window.Left + window.ActualWidth) * scale),
                Bottom = (int)Math.Round((window.Top + window.ActualHeight) * scale),
            };
            nint monitor = MonitorFromRect(ref px, MonitorDefaultToNearest);
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (monitor != 0 && GetMonitorInfo(monitor, ref info))
            {
                return new Rect(info.Work.Left / scale, info.Work.Top / scale,
                    (info.Work.Right - info.Work.Left) / scale, (info.Work.Bottom - info.Work.Top) / scale);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException)
        {
            // Padamo na primarni zaslon.
        }

        return SystemParameters.WorkArea;
    }

    /// <summary>Vrati prozor u dozvoljene granice. Vraća true ako je pomaknut.</summary>
    internal static bool Ensure(Window window)
    {
        if (!window.IsLoaded || !window.IsVisible || window.WindowState != WindowState.Normal ||
            window.ActualWidth <= 0 || window.ActualHeight <= 0 || double.IsNaN(window.Left) || double.IsNaN(window.Top))
        {
            return false;
        }

        Rect current = new(window.Left, window.Top, window.ActualWidth, window.ActualHeight);
        Rect fitted = Clamp(current, WorkAreaFor(window));
        if (Math.Abs(fitted.X - current.X) < 0.5 && Math.Abs(fitted.Y - current.Y) < 0.5)
        {
            return false;
        }

        window.Left = fitted.X;
        window.Top = fitted.Y;
        Log.Info($"Prozor {window.GetType().Name} vraćen u vidljivo područje ({current.X:0},{current.Y:0}) → ({fitted.X:0},{fitted.Y:0}).");
        return true;
    }

    /// <summary>Prozori koji se ne smiju pomicati (prozirni slojevi preko videa i glavni prozor).</summary>
    private static bool IsExcluded(Window window) =>
        window is MainWindow or JebaitedPlayer.Views.ControlsOverlay or JebaitedPlayer.Views.TopBarOverlay or JebaitedPlayer.Views.CardOverlay;

    /// <summary>Jednom pri pokretanju: svaki novi prozor (osim slojeva i glavnog) prati vlastiti položaj i veličinu.</summary>
    internal static void RegisterForAllWindows() =>
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, _) =>
        {
            if (sender is Window w && !IsExcluded(w))
            {
                Attach(w);
            }
        }));

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Window, object> Attached = [];

    internal static void Attach(Window window)
    {
        if (!Attached.TryAdd(window, new object()))
        {
            return;
        }

        var timer = new DispatcherTimer(DispatcherPriority.Background, window.Dispatcher) { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) =>
        {
            // Dok korisnik još drži prozor mišem, ne diramo ga.
            if (Mouse.LeftButton == MouseButtonState.Pressed)
            {
                return;
            }

            timer.Stop();
            Ensure(window);
        };
        timer.Stop();

        void Kick()
        {
            timer.Stop();
            timer.Start();
        }

        // Veličina se promijeni (npr. otvoren Equalizer): odmah u granice, bez čekanja.
        window.SizeChanged += (_, _) => window.Dispatcher.BeginInvoke(() => Ensure(window), DispatcherPriority.Loaded);
        window.LocationChanged += (_, _) => Kick();
        window.IsVisibleChanged += (_, _) => Kick();
        EventHandler displayChanged = (_, _) => window.Dispatcher.BeginInvoke(Kick);
        SystemEvents.DisplaySettingsChanged += displayChanged;
        window.Closed += (_, _) =>
        {
            timer.Stop();
            SystemEvents.DisplaySettingsChanged -= displayChanged;
        };
        Kick();
    }

    private const uint MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern nint MonitorFromRect(ref NativeRect rect, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
}

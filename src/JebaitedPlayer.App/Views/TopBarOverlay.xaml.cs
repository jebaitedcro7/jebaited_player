using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using static JebaitedPlayer.Interop.NativeMethods;

namespace JebaitedPlayer.Views;

/// <summary>
/// Gornja traka preko videa s tipkom Home (UPGRADE 3). Kao i donja traka, to je zaseban prozor koji se nikad ne aktivira
/// (WS_EX_NOACTIVATE), jer WPF ne može crtati preko prozora u koji crta mpv. Prikazuje se i skriva zajedno s donjim
/// kontrolama. Klik na prazan dio trake ponaša se kao klik na video (pauza, dvoklik = cijeli zaslon, desni klik = izbornik).
/// </summary>
public partial class TopBarOverlay : Window
{
    public TopBarOverlay()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            nint h = new WindowInteropHelper(this).Handle;
            nint ex = GetWindowLongPtr(h, GWL_EXSTYLE);
            SetWindowLongPtr(h, GWL_EXSTYLE, ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        };

        PreviewMouseMove += (_, _) => UserActivity?.Invoke(this, EventArgs.Empty);
        PreviewMouseWheel += (_, e) =>
        {
            MouseWheel?.Invoke(this, e.Delta);
            e.Handled = true;
        };
        Strip.MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource == Strip)
            {
                if (e.ClickCount == 2)
                {
                    BackgroundDoubleClick?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    BackgroundClick?.Invoke(this, EventArgs.Empty);
                }

                e.Handled = true;
            }
        };
        Strip.MouseRightButtonUp += (_, e) =>
        {
            BackgroundRightClick?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        };
    }

    /// <summary>Klik na Home: povratak na početni ekran (pozicija se sprema).</summary>
    public event EventHandler? HomeRequested;

    public event EventHandler? UserActivity;
    public event EventHandler? BackgroundClick;
    public event EventHandler? BackgroundDoubleClick;
    public event EventHandler? BackgroundRightClick;
    public new event EventHandler<int>? MouseWheel;
    public event EventHandler<string[]>? FilesDropped;

    public nint Handle => new WindowInteropHelper(this).EnsureHandle();

    /// <summary>Središte tipke Home na zaslonu (fizički pikseli), za dijagnostiku stvarnim klikom.</summary>
    internal Point HomeButtonScreenCenter() =>
        HomeButton.PointToScreen(new Point(HomeButton.ActualWidth / 2, HomeButton.ActualHeight / 2));

    /// <summary>Pravokutnik tipke Home na zaslonu (fizički pikseli).</summary>
    internal Rect HomeButtonScreenRect()
    {
        Point a = HomeButton.PointToScreen(new Point(0, 0));
        Point b = HomeButton.PointToScreen(new Point(HomeButton.ActualWidth, HomeButton.ActualHeight));
        return new Rect(a, b);
    }

    private void OnHomeClick(object sender, RoutedEventArgs e) => HomeRequested?.Invoke(this, EventArgs.Empty);

    protected override void OnDragOver(DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    protected override void OnDrop(DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            FilesDropped?.Invoke(this, files);
        }

        e.Handled = true;
    }
}

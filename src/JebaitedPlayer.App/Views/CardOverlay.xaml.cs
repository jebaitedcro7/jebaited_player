using System.Windows;
using System.Windows.Interop;
using JebaitedPlayer.ViewModels;
using static JebaitedPlayer.Interop.NativeMethods;

namespace JebaitedPlayer.Views;

/// <summary>
/// Prozirni sloj preko cijelog videa za kartice "Nastaviti gledanje?" i "Sljedeća epizoda".
/// Kao i sloj s kontrolama, nikad se ne aktivira, pa tipkovnica ostaje u glavnom prozoru.
/// </summary>
public partial class CardOverlay : Window
{
    public CardOverlay(PlayerViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        SourceInitialized += (_, _) =>
        {
            nint h = new WindowInteropHelper(this).Handle;
            nint ex = GetWindowLongPtr(h, GWL_EXSTYLE);
            SetWindowLongPtr(h, GWL_EXSTYLE, ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        };
    }

    public nint Handle => new WindowInteropHelper(this).EnsureHandle();
}

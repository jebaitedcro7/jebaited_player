#if TORLINK
using System.Windows;

namespace JebaitedPlayer.Views.Torlink;

public partial class TorlinkConsentWindow : Window
{
    public TorlinkConsentWindow()
    {
        InitializeComponent();
        WindowTheme.UseDarkTitleBar(this);
    }

    /// <summary>Odgovor za testove (bez prozora): true = prihvaćeno.</summary>
    internal static Func<bool>? AskOverride { get; set; }

    /// <summary>Prikaži upozorenje; true samo ako je korisnik potvrdio kvačicu i "Uključi Torlink".</summary>
    internal static bool Ask(Window? owner)
    {
        if (AskOverride is { } o)
        {
            return o();
        }

        var w = new TorlinkConsentWindow { Owner = owner };
        return w.ShowDialog() == true;
    }

    private void OnAcceptChanged(object sender, RoutedEventArgs e) => TurnOnButton.IsEnabled = AcceptBox.IsChecked == true;

    private void OnTurnOn(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
#endif

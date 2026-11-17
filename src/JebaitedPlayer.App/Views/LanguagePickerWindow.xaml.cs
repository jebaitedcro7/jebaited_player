using System.Windows;
using System.Windows.Input;

namespace JebaitedPlayer.Views;

/// <summary>
/// Odabir jezika pri prvom pokretanju (odmah nakon instalacije, jer Velopack sam pokrene player) i jednom nakon
/// nadogradnje s verzije bez jezika. Velopackov instaler nema vlastitih stranica, pa je ovo prvi korak instalacije.
/// Zatvaranje ili Enter bez odabira ostavlja engleski.
/// </summary>
public partial class LanguagePickerWindow : Window
{
    public LanguagePickerWindow()
    {
        InitializeComponent();
        WindowTheme.UseDarkTitleBar(this);
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        };
    }

    private void OnContinue(object sender, RoutedEventArgs e) => Close();
}

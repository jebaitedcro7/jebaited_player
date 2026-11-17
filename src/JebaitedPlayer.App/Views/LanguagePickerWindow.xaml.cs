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

    private void OnPrivacy(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Services.UpdateHistory.Repository + "/blob/main/PRIVACY.md")
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Infrastructure.Log.Warn($"Poveznica se nije otvorila: {ex.Message}");
        }
    }
}

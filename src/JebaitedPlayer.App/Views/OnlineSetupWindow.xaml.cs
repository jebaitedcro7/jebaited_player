using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.Online;
using JebaitedPlayer.ViewModels;

namespace JebaitedPlayer.Views;

/// <summary>
/// Vodič "Titlovi s interneta": otvori stranicu izvora, objasni korake, korisnik zalijepi podatke,
/// player ih provjeri (prava prijava ili pretraga) i tek tada spremi (kriptirano).
/// </summary>
public partial class OnlineSetupWindow : Window
{
    private readonly AppSettings _settings;
    private readonly OnlineSubtitleController _online;
    private readonly StackPanel[] _pages;
    private int _page;

    public OnlineSetupWindow(AppSettings settings, OnlineSubtitleController online, int startPage = 0)
    {
        _settings = settings;
        _online = online;
        InitializeComponent();
        WindowTheme.UseDarkTitleBar(this);
        MaxHeight = Math.Max(420, SystemParameters.WorkArea.Height - 24);
        _pages = [IntroPage, OsPage, SubDlPage, TitloviPage, DonePage];

        OnlineSettings o = settings.Online;
        OsUser.Text = o.OpenSubtitlesUsername ?? string.Empty;
        OsPassword.Password = Secret.Unprotect(o.OpenSubtitlesPassword) ?? string.Empty;
        OsKey.Password = Secret.Unprotect(o.OpenSubtitlesApiKey) ?? string.Empty;
        SubDlKey.Password = Secret.Unprotect(o.SubDlApiKey) ?? string.Empty;
        TitloviUser.Text = o.TitloviUsername ?? string.Empty;
        TitloviPassword.Password = Secret.Unprotect(o.TitloviPassword) ?? string.Empty;

        // Izdanje ima ugrađen ključ aplikacije: korisnik upisuje samo korisničko ime i lozinku.
        bool needsKey = !AppSecrets.HasOpenSubtitlesAppKey;
        Visibility keyVisibility = needsKey ? Visibility.Visible : Visibility.Collapsed;
        OsKeyLabel.Visibility = keyVisibility;
        OsKeyRow.Visibility = keyVisibility;
        OsKeyHelp.Visibility = keyVisibility;
        if (needsKey)
        {
            OsIntroText.Text = "Najviše titlova i jedini izvor koji prepoznaje točno izdanje tvog videa. Trebaju korisničko ime, lozinka i besplatni API ključ.";
        }

        ShowPage(Math.Clamp(startPage, 0, _pages.Length - 1));
    }

    /// <summary>Što je vodič spremio (za dijagnostiku i izvještaj).</summary>
    public bool Saved { get; private set; }

    private void ShowPage(int page)
    {
        _page = page;
        for (int i = 0; i < _pages.Length; i++)
        {
            _pages[i].Visibility = i == page ? Visibility.Visible : Visibility.Collapsed;
        }

        bool last = page == _pages.Length - 1;
        BackButton.Visibility = page is > 0 and < 4 ? Visibility.Visible : Visibility.Collapsed;
        SkipButton.Visibility = page is 1 or 2 or 3 ? Visibility.Visible : Visibility.Collapsed;
        SkipButton.Content = page == 1 ? "Preskoči" : "Preskoči (neobavezno)";
        NextText.Text = last ? "Zatvori" : page == 0 ? "Započni" : "Dalje";
        PageCounter.Text = page is > 0 and < 4 ? $"Korak {page} od 3" : string.Empty;
        if (last)
        {
            DoneSummary.Text = OnlineSetup.Summary(_settings.Online);
        }
    }

    private void OnNext(object sender, RoutedEventArgs e)
    {
        if (_page == _pages.Length - 1)
        {
            Close();
            return;
        }

        ShowPage(_page + 1);
    }

    private void OnSkip(object sender, RoutedEventArgs e) => ShowPage(_page + 1);

    private void OnBack(object sender, RoutedEventArgs e) => ShowPage(Math.Max(0, _page - 1));

    private async void OnTestOpenSubtitles(object sender, RoutedEventArgs e)
    {
        string user = OsUser.Text.Trim();
        string pass = OsPassword.Password;
        string personalKey = OnlineSetup.CleanPastedKey(OsKey.Password);
        string? key = personalKey.Length > 0 ? personalKey : AppSecrets.OpenSubtitlesAppKey;
        if (user.Length == 0 || pass.Length == 0)
        {
            OsStatus.Text = "Upiši korisničko ime i lozinku.";
            return;
        }

        if (key is null)
        {
            OsStatus.Text = "Upiši i API ključ (vidi upute ispod polja).";
            return;
        }

        var provider = new OpenSubtitlesProvider(OnlineSubtitleController.SharedHttp, () => new OpenSubtitlesCredentials(key, user, pass));
        if (await RunTest(OsStatus, OsTestButton, provider.TestAsync))
        {
            OnlineSetup.SaveOpenSubtitles(_settings.Online, user, pass, personalKey.Length > 0 ? personalKey : null);
            SaveAll("OpenSubtitles");
        }
    }

    private async void OnTestSubDl(object sender, RoutedEventArgs e)
    {
        string key = OnlineSetup.CleanPastedKey(SubDlKey.Password);
        if (key.Length == 0)
        {
            SubDlStatus.Text = "Zalijepi API ključ sa stranice SubDL.";
            return;
        }

        var provider = new SubDlProvider(OnlineSubtitleController.SharedHttp, () => key);
        if (await RunTest(SubDlStatus, SubDlTestButton, provider.TestAsync))
        {
            OnlineSetup.SaveSubDl(_settings.Online, key);
            SaveAll("SubDL");
        }
    }

    private async void OnTestTitlovi(object sender, RoutedEventArgs e)
    {
        string user = TitloviUser.Text.Trim();
        string pass = TitloviPassword.Password;
        if (user.Length == 0 || pass.Length == 0)
        {
            TitloviStatus.Text = "Upiši korisničko ime i lozinku.";
            return;
        }

        var provider = new TitloviProvider(OnlineSubtitleController.SharedHttp, () => new TitloviCredentials(user, pass));
        if (await RunTest(TitloviStatus, TitloviTestButton, provider.TestAsync))
        {
            OnlineSetup.SaveTitlovi(_settings.Online, user, pass);
            SaveAll("Titlovi.com");
        }
    }

    private void SaveAll(string source)
    {
        _settings.Save();
        _online.RebuildProviders();
        Saved = true;
        Log.Info($"Vodič za titlove: {source} provjeren i spremljen.");
    }

    private static async Task<bool> RunTest(TextBlock status, Button button, Func<CancellationToken, Task<string>> test)
    {
        button.IsEnabled = false;
        status.Text = "Provjeravam…";
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            status.Text = "✓ " + await test(timeout.Token) + " Spremljeno.";
            return true;
        }
        catch (Exception ex) when (ex is ProviderException or HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            status.Text = "✗ " + (ex switch
            {
                TaskCanceledException => "Izvor ne odgovara. Provjeri vezu s internetom i pokušaj ponovno.",
                HttpRequestException => "Nema veze s izvorom. Provjeri vezu s internetom.",
                _ => ex.Message,
            });
            return false;
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private void OnPasteOsKey(object sender, RoutedEventArgs e) => OsKey.Password = OnlineSetup.CleanPastedKey(ClipboardText());

    private void OnPasteSubDlKey(object sender, RoutedEventArgs e) => SubDlKey.Password = OnlineSetup.CleanPastedKey(ClipboardText());

    private static string ClipboardText()
    {
        try
        {
            return Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty;
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            return string.Empty;
        }
    }

    private void OnLinkClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string url } && url.StartsWith("https://", StringComparison.Ordinal))
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
    }
}

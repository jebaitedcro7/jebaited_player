using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.Online;
using JebaitedPlayer.ViewModels;

namespace JebaitedPlayer.Views;

/// <summary>Izvori titlova: uključivanje izvora, API ključevi i računi (kriptirano), provjera veze.</summary>
public partial class SubtitleSourcesWindow : Window
{
    private readonly AppSettings _settings;
    private readonly OnlineSubtitleController _online;

    public SubtitleSourcesWindow(AppSettings settings, OnlineSubtitleController online)
    {
        _settings = settings;
        _online = online;
        InitializeComponent();
        WindowTheme.UseDarkTitleBar(this);
        MaxHeight = Math.Max(400, SystemParameters.WorkArea.Height - 24);

        OnlineSettings o = settings.Online;
        AutoSearchBox.IsChecked = o.AutoSearch;
        SaveNextBox.IsChecked = o.SaveNextToVideo;
        OsEnabled.IsChecked = o.OpenSubtitlesEnabled;
        OsKey.Password = Secret.Unprotect(o.OpenSubtitlesApiKey) ?? string.Empty;
        OsUser.Text = o.OpenSubtitlesUsername ?? string.Empty;
        OsPassword.Password = Secret.Unprotect(o.OpenSubtitlesPassword) ?? string.Empty;
        SubDlEnabled.IsChecked = o.SubDlEnabled;
        SubDlKey.Password = Secret.Unprotect(o.SubDlApiKey) ?? string.Empty;
        TitloviEnabled.IsChecked = o.TitloviEnabled;
        TitloviUser.Text = o.TitloviUsername ?? string.Empty;
        TitloviPassword.Password = Secret.Unprotect(o.TitloviPassword) ?? string.Empty;
        if (AppSecrets.HasOpenSubtitlesAppKey)
        {
            OsKeyHint.Bind(TextBlock.TextProperty, "sources.keyOptional");
        }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        OnlineSettings o = _settings.Online;
        o.AutoSearch = AutoSearchBox.IsChecked == true;
        o.SaveNextToVideo = SaveNextBox.IsChecked == true;
        o.OpenSubtitlesEnabled = OsEnabled.IsChecked == true;
        o.OpenSubtitlesApiKey = Secret.Protect(OsKey.Password.Trim());
        o.OpenSubtitlesUsername = Empty(OsUser.Text);
        o.OpenSubtitlesPassword = Secret.Protect(OsPassword.Password);
        o.SubDlEnabled = SubDlEnabled.IsChecked == true;
        o.SubDlApiKey = Secret.Protect(SubDlKey.Password.Trim());
        o.TitloviEnabled = TitloviEnabled.IsChecked == true;
        o.TitloviUsername = Empty(TitloviUser.Text);
        o.TitloviPassword = Secret.Protect(TitloviPassword.Password);
        _settings.Save();
        _online.RebuildProviders();
        Log.Info($"Izvori titlova spremljeni (OpenSubtitles {(o.OpenSubtitlesEnabled ? "uključen" : "isključen")}, " +
                 $"SubDL {(o.SubDlEnabled ? "uključen" : "isključen")}, Titlovi {(o.TitloviEnabled ? "uključen" : "isključen")})");
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => Close();

    private async void OnTestOpenSubtitles(object sender, RoutedEventArgs e)
    {
        // Prazno polje = ključ aplikacije iz izdanja (ako ga ima).
        string? key = OsKey.Password.Trim() is { Length: > 0 } personal ? personal : AppSecrets.OpenSubtitlesAppKey;
        var provider = new OpenSubtitlesProvider(OnlineSubtitleController.SharedHttp, () => key is null
            ? null
            : new OpenSubtitlesCredentials(key, Empty(OsUser.Text), Empty(OsPassword.Password)));
        await RunTest(OsStatus, sender, provider.TestAsync);
    }

    private async void OnTestSubDl(object sender, RoutedEventArgs e)
    {
        string key = SubDlKey.Password.Trim();
        var provider = new SubDlProvider(OnlineSubtitleController.SharedHttp, () => key.Length == 0 ? null : key);
        await RunTest(SubDlStatus, sender, provider.TestAsync);
    }

    private async void OnTestTitlovi(object sender, RoutedEventArgs e)
    {
        string user = TitloviUser.Text.Trim();
        string pass = TitloviPassword.Password;
        var provider = new TitloviProvider(OnlineSubtitleController.SharedHttp, () => user.Length > 0 && pass.Length > 0
            ? new TitloviCredentials(user, pass)
            : null);
        await RunTest(TitloviStatus, sender, provider.TestAsync);
    }

    private static async Task RunTest(TextBlock status, object sender, Func<CancellationToken, Task<string>> test)
    {
        var button = (Button)sender;
        button.IsEnabled = false;
        status.Text = Loc.T("wizard.checking");
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            status.Text = "✓ " + await test(timeout.Token);
        }
        catch (Exception ex) when (ex is ProviderException or HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            status.Text = "✗ " + (ex is TaskCanceledException ? Loc.T("sources.timeout") : ex.Message);
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private void OnLinkClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string url } && url.StartsWith("https://", StringComparison.Ordinal))
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
    }

    private static string? Empty(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}

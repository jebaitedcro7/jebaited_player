using System.Windows;
using System.Windows.Threading;
using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.Online;
using JebaitedPlayer.Services;

namespace JebaitedPlayer;

/// <summary>
/// Ciklus 6, N3: datum, vrijeme i prognoza gore lijevo na početnom ekranu. Sat tiče samo dok je početni ekran
/// vidljiv; prognoza se osvježava najviše svakih 30 minuta (Open-Meteo, grad bira korisnik).
/// </summary>
public partial class MainWindow
{
    private HomeInfoService? _homeInfo;
    private DispatcherTimer? _homeClock;
    private DateTime _lastWeatherCheck;

    internal HomeInfoService HomeInfo => _homeInfo ??= new HomeInfoService(_settings, AppPaths.DataDir);

    // --- N1 (ciklus 6): izvještaji o greškama --------------------------------------------------------------------------

    private ErrorReporting? _reports;

    internal ErrorReporting Reports => _reports ??= new ErrorReporting(_settings, AppPaths.LogsDir);

    /// <summary>Jednom: pitanje smiju li se slati redigirani izvještaji (zadano ne). U dijagnostici se ne pita.</summary>
    private void AskErrorReportConsent()
    {
        // Bez prijemne točke ništa se ne može poslati, pa se korisnika ništa ni ne pita.
        if (_options.IsDiagnostics || _settings.ErrorReports.ConsentAsked || !Reports.HasEndpoint)
        {
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            MessageBoxResult answer = MessageBox.Show(this, Loc.T("consent.reports"), "jebaited_player", MessageBoxButton.YesNo, MessageBoxImage.Question);
            _settings.ErrorReports.SendReports = answer == MessageBoxResult.Yes;
            _settings.ErrorReports.ConsentAsked = true;
            _settings.Save();
            Log.Info($"Dijagnostika: slanje izvještaja {(_settings.ErrorReports.SendReports ? "dopušteno" : "nije dopušteno")}.");
            _ = Reports.SendAsync();
        }, DispatcherPriority.ApplicationIdle);
    }

    private void InitializeHomeInfo()
    {
        _homeClock = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => TickHomeInfo(), Dispatcher);
        WelcomePanel.IsVisibleChanged += (_, _) => UpdateHomeClockTimer();
        UpdateHomeClockTimer();
        ApplyHomeInfo();
    }

    private void UpdateHomeClockTimer()
    {
        bool run = WelcomePanel.IsVisible && (_settings.HomeInfo.ShowTime || _settings.HomeInfo.ShowDate || _settings.HomeInfo.ShowWeather);
        if (run)
        {
            TickHomeInfo();
            _homeClock?.Start();
        }
        else
        {
            _homeClock?.Stop();
        }
    }

    /// <summary>Promjena u postavkama: odmah novi prikaz (i nova prognoza ako se promijenio grad ili jedinica).</summary>
    internal void ApplyHomeInfo(bool weatherChanged = false)
    {
        if (weatherChanged)
        {
            HomeInfo.Invalidate();
            _lastWeatherCheck = DateTime.MinValue;
        }

        UpdateHomeClockTimer();
        TickHomeInfo();
    }

    private void TickHomeInfo()
    {
        HomeInfoSettings s = _settings.HomeInfo;
        DateTime now = DateTime.Now;
        HomeTimeText.Text = s.ShowTime ? now.ToString("t", Loc.Culture) : string.Empty;
        HomeDateText.Text = s.ShowDate ? now.ToString("D", Loc.Culture) : string.Empty;
        HomeTimeText.Visibility = s.ShowTime ? Visibility.Visible : Visibility.Collapsed;
        HomeDateText.Visibility = s.ShowDate ? Visibility.Visible : Visibility.Collapsed;

        string weather = _homeInfo?.WeatherText() ?? string.Empty;
        HomeWeatherText.Text = weather;
        HomeWeatherText.Visibility = HomeWeatherSource.Visibility = weather.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        if (s.ShowWeather && s.HasPlace && DateTime.UtcNow - _lastWeatherCheck > TimeSpan.FromMinutes(1))
        {
            _lastWeatherCheck = DateTime.UtcNow;
            _ = RefreshWeatherAsync();
        }
    }

    private async Task RefreshWeatherAsync()
    {
        await HomeInfo.RefreshAsync();
        HomeWeatherText.Text = HomeInfo.WeatherText();
        HomeWeatherText.Visibility = HomeWeatherSource.Visibility = HomeWeatherText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    internal Task<IReadOnlyList<WeatherPlace>> SearchWeatherPlacesAsync(string name) => HomeInfo.SearchPlacesAsync(name);
}

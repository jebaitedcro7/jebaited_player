using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.Online;
using JebaitedPlayer.Services;
using JebaitedPlayer.Views;

namespace JebaitedPlayer;

/// <summary>
/// Dijagnostički testovi ciklusa 6 u pravom playeru: --diag-ui-test homeinfo (N3, uz JEBAITED_DATA_DIR).
/// </summary>
public partial class MainWindow
{
    /// <summary>Mali lokalni HTTP poslužitelj s promjenjivom rutom (simulirani vanjski servisi); bilježi zahtjeve.</summary>
    internal sealed class FakeHttpServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _cts = new();

        public FakeHttpServer(Func<string, string, string, (int Status, string Body)> route)
        {
            Route = route;
            _listener.Start();
            _ = Task.Run(LoopAsync);
        }

        public Func<string, string, string, (int Status, string Body)> Route { get; set; }

        public Uri Base => new($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/");

        public System.Collections.Concurrent.ConcurrentQueue<string> Requests { get; } = new();

        private async Task LoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(_cts.Token);
                }
                catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
                {
                    return;
                }

                _ = Task.Run(() => ServeAsync(client));
            }
        }

        private async Task ServeAsync(TcpClient client)
        {
            using (client)
            {
                try
                {
                    NetworkStream s = client.GetStream();
                    var raw = new StringBuilder();
                    var buf = new byte[16384];
                    while (true)
                    {
                        int n = await s.ReadAsync(buf, _cts.Token);
                        if (n == 0)
                        {
                            return;
                        }

                        raw.Append(Encoding.UTF8.GetString(buf, 0, n));
                        string text = raw.ToString();
                        int end = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                        if (end < 0)
                        {
                            continue;
                        }

                        int length = text[..end].Split("\r\n").Where(l => l.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                            .Select(l => int.Parse(l[15..].Trim(), System.Globalization.CultureInfo.InvariantCulture)).FirstOrDefault();
                        if (text.Length - end - 4 < length)
                        {
                            continue;
                        }

                        string[] first = text.Split("\r\n")[0].Split(' ');
                        string body = text[(end + 4)..];
                        Requests.Enqueue($"{first[0]} {first[1]} {body}".Trim());
                        (int status, string json) = Route(first[0], first[1], body);
                        byte[] payload = Encoding.UTF8.GetBytes(json);
                        await s.WriteAsync(Encoding.ASCII.GetBytes(
                            $"HTTP/1.1 {status} X\r\nContent-Type: application/json\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n"), _cts.Token);
                        await s.WriteAsync(payload, _cts.Token);
                        return;
                    }
                }
                catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
                {
                }
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Stop();
        }
    }

    /// <summary>N1: greška → redigirani izvještaj u redu; bez pristanka ništa ne ide van; s pristankom na (simuliranu) prijemnu točku.</summary>
    private async Task<Dictionary<string, object?>> ReportsUiTestAsync()
    {
        var r = new Dictionary<string, object?>();
        if (Environment.GetEnvironmentVariable("JEBAITED_DATA_DIR") is not { Length: > 0 })
        {
            r["error"] = "JEBAITED_DATA_DIR is required (never your real data)";
            return r;
        }

        await Task.Delay(800);
        ErrorReporting reports = Reports;
        r["consentAsked"] = _settings.ErrorReports.ConsentAsked;
        JebaitedPlayer.Diagnostics.DiagLog.Error(JebaitedPlayer.Diagnostics.LogCat.Playback, "uitest.problem", "UiTest", @"Test error C:\Users\Someone Private\Videos\Holiday.mkv api_key=SECRET42", new IOException("disk"));
        var pending = reports.Queue.Pending();
        r["queued"] = new Dictionary<string, object?> { ["count"] = pending.Count, ["message"] = pending.FirstOrDefault().Report?.Message };
        r["github"] = reports.GitHubIssueUrl();
        r["releaseHasEndpoint"] = reports.HasEndpoint;

        using var fake = new FakeHttpServer((_, _, _) => (200, "{}"));
        reports.UseTestEndpoint(new Uri(fake.Base, "report"));
        r["sentWithoutConsent"] = await reports.SendAsync();

        ShowPlayerSettings(SettingsTab.About);
        await Task.Delay(700);
        PlayerSettingsWindow w = _playerSettingsWindow!;
        w.UpdateReportsStatus();
        r["before"] = new Dictionary<string, object?> { ["status"] = w.ReportsStatusText.Text, ["github"] = w.ReportGitHubButton.IsEnabled, ["checked"] = w.SendReportsBox.IsChecked };
        w.SendReportsBox.IsChecked = true;
        await WaitForAsync(() => reports.Queue.Pending().Count == 0, 5000);
        w.UpdateReportsStatus();
        r["after"] = new Dictionary<string, object?> { ["status"] = w.ReportsStatusText.Text, ["pending"] = reports.Queue.Pending().Count, ["requests"] = fake.Requests.ToArray(), ["consent"] = _settings.ErrorReports.SendReports };
        r["screenshot"] = SaveWindowPng("settings_reports", w);
        w.Close();
        return r;
    }

    /// <summary>N3: vrijeme i datum na početnom ekranu, grad iz postavki, prognoza, °F, bez interneta, jedan stvarni poziv.</summary>
    private async Task<Dictionary<string, object?>> HomeInfoUiTestAsync()
    {
        var r = new Dictionary<string, object?>();
        if (Environment.GetEnvironmentVariable("JEBAITED_DATA_DIR") is not { Length: > 0 })
        {
            r["error"] = "JEBAITED_DATA_DIR is required (never your real data)";
            return r;
        }

        await Task.Delay(1500);
        r["start"] = new Dictionary<string, object?>
        {
            ["time"] = HomeTimeText.Text, ["date"] = HomeDateText.Text, ["weatherVisible"] = HomeWeatherText.IsVisible, ["homeVisible"] = WelcomePanel.IsVisible,
        };

        int temp = 8;
        using var fake = new FakeHttpServer((_, path, _) => path.StartsWith("/v1/search", StringComparison.Ordinal)
            ? (200, """{ "results": [ { "name": "Zagreb", "admin1": "Grad Zagreb", "country": "Hrvatska", "latitude": 45.8144, "longitude": 15.978 } ] }""")
            : (200, $$"""{ "current": { "temperature_2m": {{temp}}.4, "weather_code": 3, "is_day": 1 } }"""));
        HomeInfo.UseTestServer(fake.Base);

        // Postavke → Izgled: uključi prognozu, nađi i odaberi grad.
        ShowPlayerSettings(SettingsTab.Appearance);
        await Task.Delay(700);
        PlayerSettingsWindow w = _playerSettingsWindow!;
        w.HomeWeatherBox.IsChecked = true;
        await Task.Delay(300);
        r["beforeCity"] = HomeWeatherText.IsVisible;
        r["found"] = await w.FindWeatherCityAsync("Zagreb");
        w.SetWeatherPlace(new WeatherPlace("Zagreb", "Grad Zagreb", "Hrvatska", 45.8144, 15.978));
        await WaitForAsync(() => HomeWeatherText.Text.Contains("Zagreb", StringComparison.Ordinal), 5000);
        r["weather"] = new Dictionary<string, object?>
        {
            ["text"] = HomeWeatherText.Text, ["visible"] = HomeWeatherText.IsVisible, ["source"] = HomeWeatherSource.IsVisible, ["cityText"] = w.WeatherCityText.Text,
        };
        r["settingsScreenshot"] = SaveWindowPng("settings_home_info", w);

        // °F: nova prognoza u Fahrenheitima.
        temp = 47;
        w.WeatherFahrenheit.IsChecked = true;
        await WaitForAsync(() => HomeWeatherText.Text.Contains("°F", StringComparison.Ordinal), 5000);
        r["fahrenheit"] = HomeWeatherText.Text;
        w.Close();
        await Task.Delay(500);
        r["homeScreenshot"] = SaveWindowPng("home_info");

        // Bez interneta (poslužitelj vraća grešku): zadnja prognoza ostaje, uz vrijeme ažuriranja.
        fake.Route = (_, _, _) => (500, "{}");
        await HomeInfo.RefreshAsync(force: true);
        TickHomeInfo();
        r["offline"] = new Dictionary<string, object?> { ["text"] = HomeWeatherText.Text, ["stale"] = HomeInfo.IsStale };
        r["requests"] = fake.Requests.ToArray();

        // Isključeno: ništa se ne prikazuje.
        _settings.HomeInfo.ShowTime = false;
        _settings.HomeInfo.ShowWeather = false;
        ApplyHomeInfo();
        r["hidden"] = new Dictionary<string, object?> { ["time"] = HomeTimeText.IsVisible, ["weather"] = HomeWeatherText.IsVisible, ["date"] = HomeDateText.IsVisible };

        // Jedan stvarni poziv prema Open-Meteou (internet).
        try
        {
            using var real = new OpenMeteoClient($"jebaited_player/{AppPaths.Version} (+https://github.com/jebaitedcro7/jebaited_player)");
            IReadOnlyList<WeatherPlace> places = await real.SearchPlacesAsync("Zagreb", "hr", CancellationToken.None);
            WeatherNow now = await real.CurrentAsync(places[0].Latitude, places[0].Longitude, false, CancellationToken.None);
            r["real"] = new Dictionary<string, object?> { ["place"] = places[0].Label, ["temperature"] = now.Temperature, ["code"] = now.Code };
        }
        catch (Exception ex) when (ex is ProviderException or ArgumentOutOfRangeException)
        {
            r["real"] = new Dictionary<string, object?> { ["error"] = ex.Message };
        }

        return r;
    }
}

using System.Windows;

namespace JebaitedPlayer;

/// <summary>
/// 2.4.18: vrijeme, datum i prognoza na početnom ekranu prilagođavaju se veličini prozora (--diag-ui-test homefit).
/// Veliki prozor: veličine iz postavki; manji: razmjerno manje; premali: bez dodatnih redaka prognoze.
/// </summary>
public partial class MainWindow
{
    private async Task<Dictionary<string, object?>> HomeFitUiTestAsync()
    {
        var r = new Dictionary<string, object?>();
        JebaitedPlayer.Services.HomeInfoSettings h = _settings.HomeInfo;
        h.ShowTime = h.ShowDate = true;
        h.TimeStyle = new JebaitedPlayer.Services.HomeElementStyle { Size = 96, Position = "tl" };
        h.DateStyle = new JebaitedPlayer.Services.HomeElementStyle { Size = 60, Position = "tl" };
        h.WeatherStyle = new JebaitedPlayer.Services.HomeElementStyle { Size = 40, Position = "tl" };
        ApplyHomeInfo();
        _homeClock?.Stop(); // sat inače svake sekunde prepisuje isprobani tekst prognoze
        HomeWeatherText.Text = "-1 °C · Partly cloudy";
        HomeWeatherText.Visibility = Visibility.Visible;
        HomeLayer.SetDetails([("🌡", "Feels like -3 °C"), ("↕", "Min -2 °C · Max 3 °C"), ("🌧", "Chance of rain 0 %"), ("🌬", "Wind 1 km/h"), ("💧", "Humidity 80 %")]);

        WindowState = WindowState.Normal;
        double timeSize96 = 0;
        foreach ((string name, double w, double hgt) in new[] { ("big", 1300d, 800d), ("mid", 900d, 560d), ("small", 520d, 320d) })
        {
            Width = w;
            Height = hgt;
            await Task.Delay(900);
            if (name == "big")
            {
                timeSize96 = HomeTimeText.FontSize;
            }

            r[name] = new Dictionary<string, object?>
            {
                ["layer"] = $"{HomeLayer.ActualWidth:0}x{HomeLayer.ActualHeight:0}",
                ["scale"] = Math.Round(HomeLayer.CurrentScale, 3),
                ["timeSize"] = Math.Round(HomeTimeText.FontSize, 1),
                ["dateSize"] = Math.Round(HomeDateText.FontSize, 1),
                ["weatherSize"] = Math.Round(HomeWeatherText.FontSize, 1),
                ["detailsShown"] = HomeLayer.DetailsShown,
                ["screenshot"] = SaveWindowPng("home_fit_" + name),
            };
        }

        r["bigTimeIsConfigured"] = Math.Abs(timeSize96 - 96) < 0.5;
        r["smallIsSmaller"] = ((Dictionary<string, object?>)r["small"]!)["timeSize"] is double s && s < timeSize96 * 0.5;
        h.ResetStyle();
        ApplyHomeInfo();
        UpdateHomeClockTimer();
        return r;
    }
}

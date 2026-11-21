using System.Net;
using System.Net.Sockets;
using System.Text;
using JebaitedPlayer.Online;

namespace JebaitedPlayer.Online.Tests;

/// <summary>Ciklus 6, N3: Open-Meteo klijent prema lokalnom simuliranom poslužitelju.</summary>
[TestClass]
public sealed class OpenMeteoTests
{
    private static (Uri Base, Task Server, List<string> Paths) Serve(Func<string, (int, string)> route, int requests)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var paths = new List<string>();
        Task server = Task.Run(async () =>
        {
            for (int i = 0; i < requests; i++)
            {
                using TcpClient c = await listener.AcceptTcpClientAsync();
                NetworkStream s = c.GetStream();
                var buf = new byte[8192];
                int n = await s.ReadAsync(buf);
                string path = Encoding.ASCII.GetString(buf, 0, n).Split(' ')[1];
                lock (paths)
                {
                    paths.Add(path);
                }

                (int status, string body) = route(path);
                byte[] payload = Encoding.UTF8.GetBytes(body);
                await s.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {status} X\r\nContent-Type: application/json\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n"));
                await s.WriteAsync(payload);
            }

            listener.Stop();
        });
        return (new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/"), server, paths);
    }

    [TestMethod]
    public async Task City_search_and_current_weather_are_parsed()
    {
        (Uri baseUri, Task server, List<string> paths) = Serve(path => path.StartsWith("/v1/search", StringComparison.Ordinal)
            ? (200, """{ "results": [ { "name": "Zagreb", "admin1": "Grad Zagreb", "country": "Hrvatska", "latitude": 45.8144, "longitude": 15.978 }, { "name": "Zagreb", "country": "Hrvatska", "latitude": 45.1, "longitude": 15.2 } ] }""")
            : (200, """{ "current": { "time": "2026-11-21T09:00", "temperature_2m": 7.6, "weather_code": 3, "is_day": 1 } }"""), 2);
        using var client = new OpenMeteoClient("test", baseUri);

        IReadOnlyList<WeatherPlace> places = await client.SearchPlacesAsync("Zagreb", "hr", CancellationToken.None);
        Assert.AreEqual(2, places.Count);
        Assert.AreEqual("Zagreb, Grad Zagreb, Hrvatska", places[0].Label);
        Assert.AreEqual(45.8144, places[0].Latitude, 0.0001);

        WeatherNow now = await client.CurrentAsync(45.8144, 15.978, fahrenheit: true, CancellationToken.None);
        Assert.AreEqual(7.6, now.Temperature, 0.001);
        Assert.AreEqual(3, now.Code);
        Assert.IsTrue(now.IsDay);
        await server;
        StringAssert.Contains(paths[0], "name=Zagreb");
        StringAssert.Contains(paths[0], "language=hr");
        StringAssert.Contains(paths[1], "latitude=45.8144&longitude=15.978&current=temperature_2m,apparent_temperature,");
        StringAssert.Contains(paths[1], "temperature_unit=fahrenheit&wind_speed_unit=mph&precipitation_unit=inch");
        Assert.IsNull(now.Feels, "izvor ne šalje osjećaj temperature, pa ga nema");
        Assert.AreEqual(0, now.Hourly.Count);
        Assert.AreEqual(0, now.Daily.Count);
    }

    [TestMethod]
    public async Task Detailed_forecast_is_parsed_and_missing_values_stay_empty()
    {
        const string body = """
            { "current": { "temperature_2m": 14.2, "apparent_temperature": 12.1, "relative_humidity_2m": 71, "precipitation": 0.4, "weather_code": 61, "wind_speed_10m": 18.5, "wind_direction_10m": 225, "is_day": 1 },
              "hourly": { "time": ["2026-11-21T10:00","2026-11-21T11:00","2026-11-21T12:00"], "temperature_2m": [13.1, 14.2, 15.0], "weather_code": [3, 61, 61], "precipitation_probability": [10, 60, 80] },
              "daily": { "time": ["2026-11-21","2026-11-22"], "weather_code": [61, 3], "temperature_2m_max": [15.0, 11.0], "temperature_2m_min": [8.5, 6.0],
                         "precipitation_probability_max": [80, 20], "uv_index_max": [1.5, 1.0], "sunrise": ["2026-11-21T07:05","2026-11-22T07:06"], "sunset": ["2026-11-21T16:01","2026-11-22T16:00"] } }
            """;
        (Uri baseUri, Task server, _) = Serve(_ => (200, body), 1);
        using var client = new OpenMeteoClient("test", baseUri);
        WeatherNow w = await client.CurrentAsync(45.8, 15.9, fahrenheit: false, CancellationToken.None);
        await server;

        Assert.AreEqual(12.1, w.Feels!.Value, 0.001);
        Assert.AreEqual(71, w.Humidity!.Value, 0.001);
        Assert.AreEqual(0.4, w.Precipitation!.Value, 0.001);
        Assert.AreEqual(18.5, w.WindSpeed!.Value, 0.001);
        Assert.AreEqual(225, w.WindDirection!.Value, 0.001);
        Assert.AreEqual(8.5, w.TempMin!.Value, 0.001);
        Assert.AreEqual(15.0, w.TempMax!.Value, 0.001);
        Assert.AreEqual(80, w.PrecipProbability!.Value, 0.001);
        Assert.AreEqual(1.5, w.UvIndex!.Value, 0.001);
        Assert.AreEqual(new DateTime(2026, 11, 21, 7, 5, 0), w.Sunrise);
        Assert.AreEqual(new DateTime(2026, 11, 21, 16, 1, 0), w.Sunset);
        Assert.AreEqual(3, w.Hourly.Count);
        Assert.AreEqual(60, w.Hourly[1].PrecipProbability);
        Assert.AreEqual(2, w.Daily.Count);
        Assert.AreEqual(11.0, w.Daily[1].Max, 0.001);
    }

    [TestMethod]
    public async Task Errors_become_provider_exceptions()
    {
        (Uri baseUri, Task server, _) = Serve(_ => (500, "{}"), 1);
        using var client = new OpenMeteoClient("test", baseUri);
        await Assert.ThrowsExceptionAsync<ProviderException>(() => client.CurrentAsync(1, 2, false, CancellationToken.None));
        await server;

        using var nowhere = new OpenMeteoClient("test", new Uri("http://127.0.0.1:1/"));
        await Assert.ThrowsExceptionAsync<ProviderException>(() => nowhere.SearchPlacesAsync("x", "en", CancellationToken.None));
    }

    [TestMethod]
    public void Weather_codes_have_texts_and_symbols()
    {
        Assert.AreEqual(("weather.clear", "☀"), OpenMeteoClient.Describe(0, true));
        Assert.AreEqual(("weather.clear", "☾"), OpenMeteoClient.Describe(0, false));
        Assert.AreEqual("weather.rain", OpenMeteoClient.Describe(63, true).Key);
        Assert.AreEqual("weather.snow", OpenMeteoClient.Describe(86, true).Key);
        Assert.AreEqual("weather.thunder", OpenMeteoClient.Describe(99, true).Key);
        Assert.AreEqual("weather.unknown", OpenMeteoClient.Describe(42, true).Key);
    }
}

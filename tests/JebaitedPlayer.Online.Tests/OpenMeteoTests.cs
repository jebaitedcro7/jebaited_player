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
        StringAssert.Contains(paths[1], "latitude=45.8144&longitude=15.978&current=temperature_2m,weather_code,is_day");
        StringAssert.Contains(paths[1], "temperature_unit=fahrenheit");
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

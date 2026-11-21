using System.Globalization;
using System.Net;
using System.Text.Json;

namespace JebaitedPlayer.Online;

/// <summary>Mjesto iz geokodera (ručni unos grada; automatska lokacija se ne koristi).</summary>
public sealed record WeatherPlace(string Name, string Region, string Country, double Latitude, double Longitude)
{
    public string Label => string.Join(", ", new[] { Name, Region, Country }.Where(x => x.Length > 0).Distinct());
}

/// <summary>Trenutno vrijeme na mjestu: temperatura, WMO kôd vremena, dan/noć.</summary>
public sealed record WeatherNow(double Temperature, int Code, bool IsDay, bool Fahrenheit, DateTime FetchedUtc);

/// <summary>
/// Ciklus 6, N3: Open-Meteo (besplatno, bez ključa; izvor se navodi: „Weather data by Open-Meteo.com”).
/// Samo HTTPS prema api.open-meteo.com i geocoding-api.open-meteo.com; šalju se samo koordinate grada koji je korisnik odabrao.
/// </summary>
public sealed class OpenMeteoClient : IDisposable
{
    private static readonly string[] AllowedHosts = ["api.open-meteo.com", "geocoding-api.open-meteo.com"];

    private readonly HttpClient _http;
    private readonly Uri _forecast;
    private readonly Uri _geocoding;
    private readonly bool _testServer;

    /// <param name="testBase">Samo za testove: lokalni simulirani poslužitelj (http://127.0.0.1:port/).</param>
    public OpenMeteoClient(string userAgent, Uri? testBase = null)
    {
        _http = OnlineHttp.CreateClient(userAgent);
        _testServer = testBase is not null && IPAddress.TryParse(testBase.Host, out IPAddress? ip) && IPAddress.IsLoopback(ip);
        _forecast = _testServer ? testBase! : new Uri("https://api.open-meteo.com/");
        _geocoding = _testServer ? testBase! : new Uri("https://geocoding-api.open-meteo.com/");
    }

    public async Task<IReadOnlyList<WeatherPlace>> SearchPlacesAsync(string name, string language, CancellationToken ct)
    {
        string query = OnlineHttp.QueryString(new Dictionary<string, string>
        {
            ["name"] = name.Trim(), ["count"] = "8", ["language"] = language, ["format"] = "json",
        });
        using JsonDocument doc = await GetAsync(new Uri(_geocoding, "v1/search?" + query), ct);
        return doc.RootElement.TryGetProperty("results", out JsonElement results) && results.ValueKind == JsonValueKind.Array
            ? results.EnumerateArray().Select(r => new WeatherPlace(Str(r, "name"), Str(r, "admin1"), Str(r, "country"), Num(r, "latitude"), Num(r, "longitude")))
                .Where(p => p.Name.Length > 0).ToList()
            : [];
    }

    public async Task<WeatherNow> CurrentAsync(double latitude, double longitude, bool fahrenheit, CancellationToken ct)
    {
        string query = string.Create(CultureInfo.InvariantCulture,
            $"latitude={latitude:0.####}&longitude={longitude:0.####}&current=temperature_2m,weather_code,is_day&timezone=auto") +
            (fahrenheit ? "&temperature_unit=fahrenheit" : string.Empty);
        using JsonDocument doc = await GetAsync(new Uri(_forecast, "v1/forecast?" + query), ct);
        if (!doc.RootElement.TryGetProperty("current", out JsonElement current) || current.ValueKind != JsonValueKind.Object)
        {
            throw new ProviderException("Open-Meteo: no current weather");
        }

        return new WeatherNow(Num(current, "temperature_2m"), (int)Num(current, "weather_code"),
            current.TryGetProperty("is_day", out JsonElement d) && d.ValueKind == JsonValueKind.Number && d.GetInt32() == 1, fahrenheit, DateTime.UtcNow);
    }

    /// <summary>WMO kôd vremena → ključ opisa (prijevod) i simbol.</summary>
    public static (string Key, string Symbol) Describe(int code, bool isDay) => code switch
    {
        0 => ("weather.clear", isDay ? "☀" : "☾"),
        1 or 2 => ("weather.partlyCloudy", isDay ? "⛅" : "☁"),
        3 => ("weather.cloudy", "☁"),
        45 or 48 => ("weather.fog", "🌫"),
        >= 51 and <= 57 => ("weather.drizzle", "🌦"),
        >= 61 and <= 67 => ("weather.rain", "🌧"),
        >= 71 and <= 77 or 85 or 86 => ("weather.snow", "❄"),
        >= 80 and <= 82 => ("weather.showers", "🌦"),
        >= 95 => ("weather.thunder", "⛈"),
        _ => ("weather.unknown", "·"),
    };

    public void Dispose() => _http.Dispose();

    private async Task<JsonDocument> GetAsync(Uri uri, CancellationToken ct)
    {
        if (!_testServer)
        {
            OnlineHttp.EnsureAllowed(uri, AllowedHosts);
        }

        HttpResponseMessage response;
        try
        {
            response = await _http.GetAsync(uri, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new ProviderException("Open-Meteo: " + ex.Message);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new ProviderException("Open-Meteo: " + ex.Message);
        }

        using (response)
        {
            string text = await OnlineHttp.ReadTextAsync(response, ct);
            if (!response.IsSuccessStatusCode)
            {
                throw new ProviderException($"Open-Meteo: {(int)response.StatusCode}");
            }

            try
            {
                return JsonDocument.Parse(text);
            }
            catch (JsonException ex)
            {
                throw new ProviderException("Open-Meteo: " + ex.Message);
            }
        }
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty;

    private static double Num(JsonElement e, string name) =>
        e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
}

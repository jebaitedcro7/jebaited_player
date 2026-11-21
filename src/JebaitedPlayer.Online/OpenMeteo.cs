using System.Globalization;
using System.Net;
using System.Text.Json;

namespace JebaitedPlayer.Online;

/// <summary>Mjesto iz geokodera (ručni unos grada; automatska lokacija se ne koristi).</summary>
public sealed record WeatherPlace(string Name, string Region, string Country, double Latitude, double Longitude)
{
    public string Label => string.Join(", ", new[] { Name, Region, Country }.Where(x => x.Length > 0).Distinct());
}

/// <summary>Sat prognoze (2.1.7).</summary>
public sealed record HourlyForecast(DateTime Time, double Temperature, int Code, double? PrecipProbability);

/// <summary>Dan prognoze (2.1.7).</summary>
public sealed record DailyForecast(DateTime Date, double Min, double Max, int Code, double? PrecipProbability);

/// <summary>
/// Trenutno vrijeme na mjestu: temperatura, WMO kôd vremena, dan/noć. Od 2.1.7 i dodatni podaci; svaki je null (ili prazan popis)
/// ako ga izvor ne šalje, pa se takav podatak ne prikazuje.
/// </summary>
public sealed record WeatherNow(double Temperature, int Code, bool IsDay, bool Fahrenheit, DateTime FetchedUtc)
{
    public double? Feels { get; init; }
    public double? Humidity { get; init; }
    public double? Precipitation { get; init; }
    public double? PrecipProbability { get; init; }
    public double? WindSpeed { get; init; }
    public double? WindDirection { get; init; }
    public double? TempMin { get; init; }
    public double? TempMax { get; init; }
    public double? UvIndex { get; init; }
    public DateTime? Sunrise { get; init; }
    public DateTime? Sunset { get; init; }
    public IReadOnlyList<HourlyForecast> Hourly { get; init; } = [];
    public IReadOnlyList<DailyForecast> Daily { get; init; } = [];
}

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
        // Jedan upit daje sve (trenutno, po satima, po danima). Imperijalne jedinice samo uz °F.
        string query = string.Create(CultureInfo.InvariantCulture, $"latitude={latitude:0.####}&longitude={longitude:0.####}") +
            "&current=temperature_2m,apparent_temperature,relative_humidity_2m,precipitation,weather_code,wind_speed_10m,wind_direction_10m,is_day" +
            "&hourly=temperature_2m,weather_code,precipitation_probability" +
            "&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max,uv_index_max,sunrise,sunset" +
            "&forecast_days=6&timezone=auto" +
            (fahrenheit ? "&temperature_unit=fahrenheit&wind_speed_unit=mph&precipitation_unit=inch" : string.Empty);
        using JsonDocument doc = await GetAsync(new Uri(_forecast, "v1/forecast?" + query), ct);
        if (!doc.RootElement.TryGetProperty("current", out JsonElement current) || current.ValueKind != JsonValueKind.Object)
        {
            throw new ProviderException("Open-Meteo: no current weather");
        }

        double? minToday = null, maxToday = null, uv = null, probToday = null;
        DateTime? sunrise = null, sunset = null;
        var daily = new List<DailyForecast>();
        if (doc.RootElement.TryGetProperty("daily", out JsonElement d) && d.ValueKind == JsonValueKind.Object)
        {
            JsonElement[] days = Items(d, "time");
            for (int i = 0; i < days.Length; i++)
            {
                DateTime? date = Time(days[i]);
                double? min = At(d, "temperature_2m_min", i), max = At(d, "temperature_2m_max", i);
                if (date is null || min is null || max is null)
                {
                    continue;
                }

                daily.Add(new DailyForecast(date.Value, min.Value, max.Value, (int)(At(d, "weather_code", i) ?? 0), At(d, "precipitation_probability_max", i)));
            }

            minToday = At(d, "temperature_2m_min", 0);
            maxToday = At(d, "temperature_2m_max", 0);
            uv = At(d, "uv_index_max", 0);
            probToday = At(d, "precipitation_probability_max", 0);
            JsonElement[] rise = Items(d, "sunrise"), set = Items(d, "sunset");
            sunrise = rise.Length > 0 ? Time(rise[0]) : null;
            sunset = set.Length > 0 ? Time(set[0]) : null;
        }

        var hourly = new List<HourlyForecast>();
        if (doc.RootElement.TryGetProperty("hourly", out JsonElement h) && h.ValueKind == JsonValueKind.Object)
        {
            JsonElement[] hours = Items(h, "time");
            for (int i = 0; i < hours.Length; i++)
            {
                DateTime? time = Time(hours[i]);
                double? t = At(h, "temperature_2m", i);
                if (time is not null && t is not null)
                {
                    hourly.Add(new HourlyForecast(time.Value, t.Value, (int)(At(h, "weather_code", i) ?? 0), At(h, "precipitation_probability", i)));
                }
            }
        }

        return new WeatherNow(Num(current, "temperature_2m"), (int)Num(current, "weather_code"),
            current.TryGetProperty("is_day", out JsonElement day) && day.ValueKind == JsonValueKind.Number && day.GetInt32() == 1, fahrenheit, DateTime.UtcNow)
        {
            Feels = NumOrNull(current, "apparent_temperature"),
            Humidity = NumOrNull(current, "relative_humidity_2m"),
            Precipitation = NumOrNull(current, "precipitation"),
            WindSpeed = NumOrNull(current, "wind_speed_10m"),
            WindDirection = NumOrNull(current, "wind_direction_10m"),
            PrecipProbability = probToday,
            TempMin = minToday,
            TempMax = maxToday,
            UvIndex = uv,
            Sunrise = sunrise,
            Sunset = sunset,
            Hourly = hourly,
            Daily = daily,
        };
    }

    private static JsonElement[] Items(JsonElement e, string name) =>
        e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().ToArray() : [];

    private static double? At(JsonElement e, string name, int index)
    {
        JsonElement[] values = Items(e, name);
        return index < values.Length && values[index].ValueKind == JsonValueKind.Number ? values[index].GetDouble() : null;
    }

    /// <summary>Lokalno vrijeme mjesta iz odgovora („2026-11-21T14:00”).</summary>
    private static DateTime? Time(JsonElement v) =>
        v.ValueKind == JsonValueKind.String &&
        DateTime.TryParse(v.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime t) ? t : null;

    private static double? NumOrNull(JsonElement e, string name) =>
        e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

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

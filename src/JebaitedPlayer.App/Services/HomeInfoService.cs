using System.Text.Json;
using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.Online;

namespace JebaitedPlayer.Services;

/// <summary>N3 (ciklus 6): što početni ekran prikazuje gore lijevo. Grad bira korisnik (automatske lokacije nema).</summary>
public sealed class HomeInfoSettings
{
    public bool ShowTime { get; set; } = true;
    public bool ShowDate { get; set; } = true;

    /// <summary>Prognoza je zadano isključena dok korisnik ne odabere grad (nijedan podatak ne ide na mrežu bez toga).</summary>
    public bool ShowWeather { get; set; }

    public string? City { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public bool Fahrenheit { get; set; }

    public bool HasPlace => Latitude is not null && Longitude is not null;
}

/// <summary>
/// N3: prognoza s Open-Meteoa. Zadnji rezultat se čuva u weather-cache.json (podatkovna mapa), pa bez interneta
/// početni ekran pokazuje zadnje stanje s vremenom. Osvježava se najviše svakih 30 minuta i samo kad se traži.
/// </summary>
internal sealed class HomeInfoService : IDisposable
{
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(30);

    private readonly AppSettings _settings;
    private readonly string _cacheFile;
    private OpenMeteoClient _client = new($"jebaited_player/{AppPaths.Version} (+https://github.com/jebaitedcro7/jebaited_player)");

    public HomeInfoService(AppSettings settings, string dataDir)
    {
        _settings = settings;
        _cacheFile = Path.Combine(dataDir, "weather-cache.json");
        Weather = ReadCache();
    }

    private sealed record CacheEntry(double Latitude, double Longitude, bool Fahrenheit, double Temperature, int Code, bool IsDay, DateTime FetchedUtc);

    /// <summary>Zadnja prognoza za odabrani grad i jedinicu; null = nema podataka.</summary>
    public WeatherNow? Weather { get; private set; }

    /// <summary>Zadnje osvježavanje nije uspjelo (prikazuje se staro stanje s vremenom).</summary>
    public bool IsStale { get; private set; }

    internal void UseTestServer(Uri baseUri)
    {
        _client.Dispose();
        _client = new OpenMeteoClient("test", baseUri);
    }

    public Task<IReadOnlyList<WeatherPlace>> SearchPlacesAsync(string name) =>
        _client.SearchPlacesAsync(name, Loc.Current, CancellationToken.None);

    /// <summary>Osvježi ako je prognoza uključena, grad odabran i podaci stariji od 30 min (ili <paramref name="force"/>).</summary>
    public async Task RefreshAsync(bool force = false)
    {
        HomeInfoSettings s = _settings.HomeInfo;
        if (!s.ShowWeather || !s.HasPlace)
        {
            return;
        }

        if (Weather is { } w && Matches(w) && !force && DateTime.UtcNow - w.FetchedUtc < RefreshInterval && !IsStale)
        {
            return;
        }

        try
        {
            Weather = await _client.CurrentAsync(s.Latitude!.Value, s.Longitude!.Value, s.Fahrenheit, CancellationToken.None);
            IsStale = false;
            WriteCache(Weather);
        }
        catch (ProviderException ex)
        {
            Log.Warn($"Prognoza nije osvježena: {ex.Message}");
            IsStale = true;
            if (Weather is { } old && !Matches(old))
            {
                Weather = null; // stari podaci za drugi grad ili jedinicu ne vrijede
            }
        }
    }

    /// <summary>Promijenjen grad ili jedinica: stari podaci više ne vrijede.</summary>
    public void Invalidate()
    {
        if (Weather is { } w && !Matches(w))
        {
            Weather = null;
        }

        IsStale = false;
    }

    private bool Matches(WeatherNow w)
    {
        CacheEntry? c = _cached;
        HomeInfoSettings s = _settings.HomeInfo;
        return c is not null && w.Fahrenheit == s.Fahrenheit && s.HasPlace &&
               Math.Abs(c.Latitude - s.Latitude!.Value) < 0.001 && Math.Abs(c.Longitude - s.Longitude!.Value) < 0.001;
    }

    private CacheEntry? _cached;

    private WeatherNow? ReadCache()
    {
        try
        {
            if (File.Exists(_cacheFile))
            {
                _cached = JsonSerializer.Deserialize<CacheEntry>(File.ReadAllText(_cacheFile));
                if (_cached is { } c)
                {
                    return new WeatherNow(c.Temperature, c.Code, c.IsDay, c.Fahrenheit, c.FetchedUtc);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Log.Warn($"Spremljena prognoza nije pročitana: {ex.Message}");
        }

        return null;
    }

    private void WriteCache(WeatherNow w)
    {
        HomeInfoSettings s = _settings.HomeInfo;
        _cached = new CacheEntry(s.Latitude!.Value, s.Longitude!.Value, w.Fahrenheit, w.Temperature, w.Code, w.IsDay, w.FetchedUtc);
        try
        {
            File.WriteAllText(_cacheFile, JsonSerializer.Serialize(_cached));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Prognoza nije spremljena: {ex.Message}");
        }
    }

    /// <summary>Tekst za početni ekran, npr. „☀ 21 °C · Vedro · Zagreb” (uz „ažurirano 14:05” kad je zastarjelo).</summary>
    public string WeatherText()
    {
        HomeInfoSettings s = _settings.HomeInfo;
        if (!s.ShowWeather || Weather is not { } w)
        {
            return string.Empty;
        }

        (string key, string symbol) = OpenMeteoClient.Describe(w.Code, w.IsDay);
        string text = $"{symbol} {Math.Round(w.Temperature):0} {(w.Fahrenheit ? "°F" : "°C")} · {Loc.T(key)}" + (s.City is { Length: > 0 } c ? $" · {c}" : string.Empty);
        return IsStale ? text + " · " + Loc.T("weather.updatedAt", w.FetchedUtc.ToLocalTime().ToString("t", Loc.Culture)) : text;
    }

    public void Dispose() => _client.Dispose();
}

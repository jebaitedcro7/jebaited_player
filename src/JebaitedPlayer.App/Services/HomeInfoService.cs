using System.Text.Json;
using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.Online;

namespace JebaitedPlayer.Services;

/// <summary>2.1.7: izgled jednog elementa početnog ekrana (vrijeme, datum ili prognoza).</summary>
public sealed class HomeElementStyle
{
    /// <summary>Naziv fonta; prazno = zadani font sučelja.</summary>
    public string Font { get; set; } = string.Empty;

    /// <summary>Veličina fonta; 0 = zadana veličina elementa.</summary>
    public double Size { get; set; }

    /// <summary>Mjesto na zaslonu (id iz <see cref="HomeInfoSettings.Positions"/>); zadano gore lijevo.</summary>
    public string Position { get; set; } = "tl";
}

/// <summary>N3 (ciklus 6): što početni ekran prikazuje gore lijevo. Grad bira korisnik (automatske lokacije nema).</summary>
public sealed class HomeInfoSettings
{
    public const double MinSize = 10;
    public const double MaxSize = 96;

    /// <summary>Mjesta: gore/sredina/dolje × lijevo/sredina/desno, bez sredine (tamo je logo).</summary>
    public static readonly IReadOnlyList<string> Positions = ["tl", "tc", "tr", "ml", "mr", "bl", "bc", "br"];

    /// <summary>Fontovi koji dolaze s Windowsima 10/11.</summary>
    public static readonly IReadOnlyList<string> Fonts =
        ["Segoe UI", "Segoe UI Light", "Segoe UI Semibold", "Bahnschrift", "Calibri", "Arial", "Verdana", "Trebuchet MS", "Georgia", "Times New Roman", "Consolas", "Cascadia Mono"];

    /// <summary>Podaci prognoze koje korisnik može uključiti (ključ prijevoda "weatherDetail.&lt;id&gt;").</summary>
    public static readonly IReadOnlyList<string> AllDetails = ["feels", "minmax", "precip", "wind", "humidity", "uv", "sun", "hourly", "daily"];

    public static readonly IReadOnlyList<string> DefaultDetails = ["feels", "minmax", "precip", "wind", "humidity"];

    public const double DefaultTimeSize = 30;
    public const double DefaultDateSize = 13;
    public const double DefaultWeatherSize = 13;

    public bool ShowTime { get; set; } = true;
    public bool ShowDate { get; set; } = true;

    /// <summary>Prognoza je zadano isključena dok korisnik ne odabere grad (nijedan podatak ne ide na mrežu bez toga).</summary>
    public bool ShowWeather { get; set; }

    public string? City { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public bool Fahrenheit { get; set; }

    public HomeElementStyle TimeStyle { get; set; } = new();
    public HomeElementStyle DateStyle { get; set; } = new();
    public HomeElementStyle WeatherStyle { get; set; } = new();

    /// <summary>Koji dodatni podaci prognoze se prikazuju (samo oni koje izvor stvarno šalje).</summary>
    public List<string> WeatherDetails { get; set; } = [.. DefaultDetails];

    public bool HasPlace => Latitude is not null && Longitude is not null;

    /// <summary>Vrati font, veličinu, mjesto i podatke prognoze na zadano. Prekidači, grad i jedinice ostaju.</summary>
    public void ResetStyle()
    {
        TimeStyle = new HomeElementStyle();
        DateStyle = new HomeElementStyle();
        WeatherStyle = new HomeElementStyle();
        WeatherDetails = [.. DefaultDetails];
    }

    public void Normalize()
    {
        foreach (HomeElementStyle style in new[] { TimeStyle ??= new(), DateStyle ??= new(), WeatherStyle ??= new() })
        {
            style.Size = double.IsFinite(style.Size) && style.Size > 0 ? Math.Clamp(Math.Round(style.Size), MinSize, MaxSize) : 0;
            style.Font = Fonts.Contains(style.Font ?? string.Empty) ? style.Font! : string.Empty;
            style.Position = Positions.Contains(style.Position ?? string.Empty) ? style.Position! : "tl";
        }

        HashSet<string> wanted = (WeatherDetails ?? [.. DefaultDetails]).ToHashSet(StringComparer.Ordinal);
        WeatherDetails = AllDetails.Where(wanted.Contains).ToList();
    }
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

    private sealed record CacheEntry(double Latitude, double Longitude, bool Fahrenheit, double Temperature, int Code, bool IsDay, DateTime FetchedUtc, WeatherNow? Full = null);

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
                    return c.Full ?? new WeatherNow(c.Temperature, c.Code, c.IsDay, c.Fahrenheit, c.FetchedUtc);
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
        _cached = new CacheEntry(s.Latitude!.Value, s.Longitude!.Value, w.Fahrenheit, w.Temperature, w.Code, w.IsDay, w.FetchedUtc, w);
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

    /// <summary>Dodatni podaci prognoze koje je korisnik uključio i koje izvor šalje (ostali se ne prikazuju).</summary>
    public IReadOnlyList<string> DetailLines()
    {
        HomeInfoSettings s = _settings.HomeInfo;
        if (!s.ShowWeather || Weather is not { } w)
        {
            return [];
        }

        string deg = w.Fahrenheit ? "°F" : "°C";
        string T(double v) => $"{Math.Round(v):0}{deg}";
        var lines = new List<string>();
        foreach (string id in s.WeatherDetails)
        {
            switch (id)
            {
                case "feels" when w.Feels is { } feels:
                    lines.Add(Loc.T("weather.feels", T(feels)));
                    break;
                case "minmax" when w.TempMin is { } min && w.TempMax is { } max:
                    lines.Add(Loc.T("weather.minmax", T(min), T(max)));
                    break;
                case "precip" when w.PrecipProbability is not null || w.Precipitation is not null:
                    string chance = w.PrecipProbability is { } p ? Loc.T("weather.precipChance", $"{Math.Round(p):0}") : Loc.T("weather.precip");
                    string amount = w.Precipitation is > 0 ? $" · {w.Precipitation.Value.ToString("0.#", Loc.Culture)} {(w.Fahrenheit ? "in" : "mm")}" : string.Empty;
                    lines.Add(chance + amount);
                    break;
                case "wind" when w.WindSpeed is { } wind:
                    lines.Add(Loc.T("weather.wind", $"{Math.Round(wind):0}", w.Fahrenheit ? "mph" : "km/h", WindArrow(w.WindDirection)));
                    break;
                case "humidity" when w.Humidity is { } humidity:
                    lines.Add(Loc.T("weather.humidity", $"{Math.Round(humidity):0}"));
                    break;
                case "uv" when w.UvIndex is { } uv:
                    lines.Add(Loc.T("weather.uv", uv.ToString("0.#", Loc.Culture)));
                    break;
                case "sun" when w.Sunrise is { } rise && w.Sunset is { } set:
                    lines.Add(Loc.T("weather.sun", rise.ToString("t", Loc.Culture), set.ToString("t", Loc.Culture)));
                    break;
                case "hourly" when HourlyText() is { Length: > 0 } hourly:
                    lines.Add(hourly);
                    break;
                case "daily":
                    lines.AddRange(DailyLines());
                    break;
            }
        }

        return lines;
    }

    /// <summary>Smjer u koji vjetar puše (strelica), iz meteorološkog smjera „odakle” puše.</summary>
    internal static string WindArrow(double? from)
    {
        if (from is not { } deg)
        {
            return string.Empty;
        }

        string[] arrows = ["↓", "↙", "←", "↖", "↑", "↗", "→", "↘"];
        int index = (int)Math.Round(((deg % 360) + 360) % 360 / 45.0) % 8;
        return arrows[index];
    }

    /// <summary>Sljedećih nekoliko sati (svaki drugi sat): „15:00 12° · 17:00 11° …”.</summary>
    public string HourlyText()
    {
        if (Weather is not { } w || w.Hourly.Count == 0)
        {
            return string.Empty;
        }

        DateTime now = DateTime.Now;
        var next = w.Hourly.Where(h => h.Time >= now.AddMinutes(-30)).Where((_, i) => i % 2 == 0).Take(4).ToList();
        if (next.Count == 0)
        {
            return string.Empty;
        }

        return Loc.T("weather.hourly") + ": " + string.Join(" · ", next.Select(h =>
            $"{h.Time.ToString("t", Loc.Culture)} {OpenMeteoClient.Describe(h.Code, h.Time.Hour is >= 6 and < 20).Symbol} {Math.Round(h.Temperature):0}°"));
    }

    /// <summary>Sljedećih nekoliko dana, jedan red po danu: „uto ☁ 5°/11°”.</summary>
    public IReadOnlyList<string> DailyLines()
    {
        if (Weather is not { } w || w.Daily.Count == 0)
        {
            return [];
        }

        return w.Daily.Where(d => d.Date.Date > DateTime.Today).Take(4)
            .Select(d => $"{d.Date.ToString("ddd", Loc.Culture)} {OpenMeteoClient.Describe(d.Code, true).Symbol} {Math.Round(d.Min):0}°/{Math.Round(d.Max):0}°"
                         + (d.PrecipProbability is { } p and >= 20 ? $" · {Math.Round(p):0} %" : string.Empty)).ToList();
    }

    public void Dispose() => _client.Dispose();
}


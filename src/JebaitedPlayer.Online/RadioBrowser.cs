using System.Text.Json;

namespace JebaitedPlayer.Online;

/// <summary>Radio postaja iz Radio Browsera ili ručno upisana.</summary>
public sealed record RadioStation(string Uuid, string Name, string Url, string Country, string CountryCode, string Tags, string Codec, int Bitrate);

/// <summary>Država s brojem postaja.</summary>
public sealed record RadioCountry(string Code, string Name, int Stations);

/// <summary>
/// Ciklus 3, V3: pretraga radio postaja preko besplatnog javnog Radio Browsera (radio-browser.info, bez ključa i prijave).
/// Šalje se samo ono što korisnik traži (naziv, žanr, država); klikovi se ne prijavljuju. Ako glavni poslužitelj ne odgovori,
/// pokušavaju se zrcalni.
/// </summary>
public sealed class RadioBrowserClient : IDisposable
{
    private static readonly string[] Servers =
    [
        "https://all.api.radio-browser.info", "https://de1.api.radio-browser.info",
        "https://nl1.api.radio-browser.info", "https://at1.api.radio-browser.info",
    ];

    private readonly HttpClient _http;

    public RadioBrowserClient(string userAgent) => _http = OnlineHttp.CreateClient(userAgent);

    public Task<IReadOnlyList<RadioStation>> SearchAsync(string? name, string? tag, string? countryCode, int limit, CancellationToken ct) =>
        GetAsync(SearchQuery(name, tag, countryCode, limit), ParseStations, ct);

    public Task<IReadOnlyList<RadioCountry>> CountriesAsync(CancellationToken ct) =>
        GetAsync("/json/countries?hidebroken=true&order=stationcount&reverse=true", ParseCountries, ct);

    public Task<IReadOnlyList<string>> TagsAsync(CancellationToken ct) =>
        GetAsync("/json/tags?hidebroken=true&order=stationcount&reverse=true&limit=60", ParseTags, ct);

    /// <summary>Najslušanije postaje prvo, bez neispravnih; prazni uvjeti se ne šalju.</summary>
    public static string SearchQuery(string? name, string? tag, string? countryCode, int limit)
    {
        var p = new Dictionary<string, string>
        {
            ["hidebroken"] = "true", ["order"] = "clickcount", ["reverse"] = "true", ["limit"] = Math.Clamp(limit, 1, 200).ToString(),
        };
        if (name?.Trim() is { Length: > 0 } n)
        {
            p["name"] = n;
        }

        if (tag?.Trim() is { Length: > 0 } t)
        {
            p["tag"] = t;
        }

        if (countryCode?.Trim() is { Length: 2 } c)
        {
            p["countrycode"] = c.ToUpperInvariant();
        }

        return "/json/stations/search?" + OnlineHttp.QueryString(p);
    }

    public static bool IsStreamUrl(string? url) =>
        Uri.TryCreate(url?.Trim(), UriKind.Absolute, out Uri? u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps);

    public static IReadOnlyList<RadioStation> ParseStations(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        var list = new List<RadioStation>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement e in doc.RootElement.EnumerateArray())
        {
            string url = Str(e, "url_resolved") is { Length: > 0 } resolved ? resolved : Str(e, "url");
            string name = Str(e, "name").Trim();
            if (!IsStreamUrl(url) || name.Length == 0 || !seen.Add(url))
            {
                continue;
            }

            int bitrate = e.TryGetProperty("bitrate", out JsonElement b) && b.ValueKind == JsonValueKind.Number ? b.GetInt32() : 0;
            list.Add(new RadioStation(Str(e, "stationuuid"), name, url.Trim(), Str(e, "country").Trim(), Str(e, "countrycode").Trim().ToUpperInvariant(),
                Str(e, "tags").Trim(), Str(e, "codec").Trim().ToUpperInvariant(), bitrate));
        }

        return list;
    }

    public static IReadOnlyList<RadioCountry> ParseCountries(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        return doc.RootElement.EnumerateArray()
            .Select(e => new RadioCountry(Str(e, "iso_3166_1").ToUpperInvariant(), Str(e, "name"),
                e.TryGetProperty("stationcount", out JsonElement c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : 0))
            .Where(c => c.Code.Length == 2 && c.Name.Length > 0)
            .ToList();
    }

    public static IReadOnlyList<string> ParseTags(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        return doc.RootElement.EnumerateArray().Select(e => Str(e, "name").Trim()).Where(t => t.Length is > 1 and < 30).Distinct().ToList();
    }

    public void Dispose() => _http.Dispose();

    private async Task<T> GetAsync<T>(string pathAndQuery, Func<string, T> parse, CancellationToken ct)
    {
        Exception? last = null;
        foreach (string server in Servers)
        {
            try
            {
                using HttpResponseMessage response = await _http.GetAsync(server + pathAndQuery, ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                return parse(await OnlineHttp.ReadTextAsync(response, ct).ConfigureAwait(false));
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException ||
                                       (ex is TaskCanceledException && !ct.IsCancellationRequested))
            {
                last = ex;
            }
        }

        throw new HttpRequestException("Radio Browser is not reachable.", last);
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty;
}

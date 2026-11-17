using System.Globalization;
using System.Net;
using System.Text.Json;
using JebaitedPlayer.Library;
using JebaitedPlayer.Subtitles;

namespace JebaitedPlayer.Online;

/// <summary>SubDL (api.subdl.com): traži po nazivu, sezoni i epizodi; titlovi dolaze u ZIP-u.</summary>
public sealed class SubDlProvider(HttpClient http, Func<string?> apiKey) : ISubtitleProvider
{
    private static readonly string[] AllowedHosts = ["subdl.com"];
    private readonly RequestThrottle _throttle = new(TimeSpan.FromMilliseconds(500));

    public string Name => "SubDL";

    public bool IsAvailable => !string.IsNullOrWhiteSpace(apiKey());

    public async Task<IReadOnlyList<SubtitleCandidate>> SearchAsync(SubtitleQuery query, CancellationToken ct)
    {
        string key = apiKey() ?? throw new ProviderException(Loc.T("online.subdl.noKey"));
        var p = new Dictionary<string, string>
        {
            ["api_key"] = key,
            ["film_name"] = query.Title,
            ["languages"] = string.Join(",", query.Languages.Select(l => l.ToUpperInvariant()).Distinct()),
            ["subs_per_page"] = "30",
            ["releases"] = "1",
            ["type"] = query.IsEpisode ? "tv" : "movie",
        };
        if (query.IsEpisode)
        {
            p["episode_number"] = query.Episode!.Value.ToString(CultureInfo.InvariantCulture);
            if (query.Season is int s)
            {
                p["season_number"] = s.ToString(CultureInfo.InvariantCulture);
            }
        }
        else if (query.Year is int y)
        {
            p["year"] = y.ToString(CultureInfo.InvariantCulture);
        }

        var uri = new Uri($"https://api.subdl.com/api/v1/subtitles?{OnlineHttp.QueryString(p)}");
        OnlineHttp.EnsureAllowed(uri, AllowedHosts);
        await _throttle.WaitAsync(ct);
        using HttpResponseMessage response = await http.GetAsync(uri, ct);
        string json = await OnlineHttp.ReadTextAsync(response, ct);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new ProviderException(Loc.T("online.subdl.keyRejected"));
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            throw new ProviderException(Loc.T("online.subdl.tooMany"));
        }

        return ParseSearch(json);
    }

    public async Task<DownloadedSubtitle> DownloadAsync(SubtitleCandidate candidate, SubtitleQuery query, CancellationToken ct)
    {
        // Poveznica se sprema bez ključa (vidi WithoutApiKey); ključ se dodaje samo ako ga SubDL za preuzimanje traži.
        var uri = new Uri(new Uri("https://dl.subdl.com"), WithoutApiKey(candidate.DownloadReference));
        OnlineHttp.EnsureAllowed(uri, AllowedHosts);
        await _throttle.WaitAsync(ct);
        HttpResponseMessage response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden && apiKey() is { Length: > 0 } key)
        {
            response.Dispose();
            var withKey = new Uri($"{uri}{(uri.Query.Length > 0 ? "&" : "?")}api_key={Uri.EscapeDataString(key)}");
            await _throttle.WaitAsync(ct);
            response = await http.GetAsync(withKey, HttpCompletionOption.ResponseHeadersRead, ct);
        }

        using (response)
        {
            OnlineHttp.EnsureAllowed(response.RequestMessage?.RequestUri ?? uri, AllowedHosts);
            if (!response.IsSuccessStatusCode)
            {
                throw new ProviderException(Loc.T("online.subdl.downloadFailed", (int)response.StatusCode));
            }

            byte[] bytes = await OnlineHttp.ReadLimitedAsync(response, ct);
            return SafeSubtitleArchive.ExtractBest(bytes, (candidate.ReleaseName ?? "titlovi") + ".srt", query.Info);
        }
    }

    /// <summary>
    /// SubDL u rezultatima vraća poveznicu za preuzimanje s API ključem ("?api_key=…"). Ključ se ne smije spremati u
    /// predmemoriju ni pisati u log, pa se iz poveznice uklanja.
    /// </summary>
    internal static string WithoutApiKey(string url)
    {
        int q = url.IndexOf('?');
        if (q < 0)
        {
            return url;
        }

        string[] kept = url[(q + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => !p.StartsWith("api_key=", StringComparison.OrdinalIgnoreCase)).ToArray();
        return kept.Length == 0 ? url[..q] : url[..q] + "?" + string.Join("&", kept);
    }

    public async Task<string> TestAsync(CancellationToken ct)
    {
        var info = MediaNameParser.Parse("Inception.2010.mkv");
        var q = new SubtitleQuery("test", info, null, 0, null, ["en"]);
        IReadOnlyList<SubtitleCandidate> results = await SearchAsync(q, ct);
        return Loc.T("online.subdl.keyOk", results.Count);
    }

    internal static IReadOnlyList<SubtitleCandidate> ParseSearch(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        var list = new List<SubtitleCandidate>();
        if (root.TryGetProperty("status", out JsonElement status) && status.ValueKind == JsonValueKind.False)
        {
            string message = Json.Str(root, "error") ?? Json.Str(root, "message") ?? Loc.T("error.unknown");
            if (message.Contains("can't find", StringComparison.OrdinalIgnoreCase) || message.Contains("not found", StringComparison.OrdinalIgnoreCase))
            {
                return list;
            }

            throw new ProviderException($"SubDL: {message}");
        }

        JsonElement results = Json.Prop(root, "results");
        JsonElement first = results.ValueKind == JsonValueKind.Array && results.GetArrayLength() > 0 ? results[0] : default;
        string? title = Json.Str(first, "name");
        int? year = (int?)Json.Long(first, "year");

        if (!root.TryGetProperty("subtitles", out JsonElement subs) || subs.ValueKind != JsonValueKind.Array)
        {
            return list;
        }

        foreach (JsonElement s in subs.EnumerateArray())
        {
            string? url = Json.Str(s, "url");
            string? language = SubtitleLanguage.Normalize(Json.Str(s, "language") ?? Json.Str(s, "lang"));
            if (url is null || language is null)
            {
                continue;
            }

            var releases = new List<string>();
            if (s.TryGetProperty("releases", out JsonElement rel) && rel.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement r in rel.EnumerateArray())
                {
                    if (r.ValueKind == JsonValueKind.String && r.GetString() is { Length: > 0 } name)
                    {
                        releases.Add(name);
                    }
                    else if (Json.Str(r, "release_name") is { Length: > 0 } rn)
                    {
                        releases.Add(rn);
                    }
                }
            }

            list.Add(new SubtitleCandidate
            {
                Provider = "SubDL",
                DownloadReference = WithoutApiKey(url),
                Language = language,
                ReleaseName = Json.Str(s, "release_name") ?? releases.FirstOrDefault(),
                Releases = releases,
                FileName = Json.Str(s, "name"),
                HearingImpaired = Json.Bool(s, "hi"),
                Uploader = Json.Str(s, "author"),
                Season = (int?)Json.Long(s, "season"),
                Episode = (int?)Json.Long(s, "episode"),
                Title = title,
                Year = year,
                PageUrl = Json.Str(s, "subtitlePage") is { } page ? "https://subdl.com" + page : null,
            });
        }

        return list;
    }
}

public sealed record TitloviCredentials(string Username, string Password);

/// <summary>
/// Titlovi.com (najveća baza titlova na hrvatskom i susjednim jezicima). Koristi API službenog Kodi dodatka
/// uz korisnički račun. Uvjeti korištenja za vanjske aplikacije nisu javno potvrđeni, zato je izvor isključen
/// dok ga korisnik izričito ne uključi.
/// </summary>
public sealed class TitloviProvider(HttpClient http, Func<TitloviCredentials?> credentials) : ISubtitleProvider
{
    private const string Api = "https://kodi.titlovi.com/api/subtitles";
    private static readonly string[] AllowedHosts = ["titlovi.com"];
    private readonly RequestThrottle _throttle = new(TimeSpan.FromMilliseconds(500));
    private string? _token;
    private string? _userId;
    private DateTime _tokenExpires;

    public string Name => "Titlovi.com";

    public bool IsAvailable => credentials() is { Username.Length: > 0, Password.Length: > 0 };

    public async Task<IReadOnlyList<SubtitleCandidate>> SearchAsync(SubtitleQuery query, CancellationToken ct)
    {
        await EnsureTokenAsync(ct);
        var p = new Dictionary<string, string>
        {
            ["token"] = _token!,
            ["userid"] = _userId!,
            ["query"] = query.Title,
            ["lang"] = string.Join("|", query.Languages.Select(ToTitloviLanguage).Where(l => l != null).Distinct()!),
            ["json"] = "true",
        };
        if (query.IsEpisode && query.Season is int s)
        {
            p["season"] = s.ToString(CultureInfo.InvariantCulture);
        }

        var uri = new Uri($"{Api}/search?{OnlineHttp.QueryString(p)}");
        OnlineHttp.EnsureAllowed(uri, AllowedHosts);
        await _throttle.WaitAsync(ct);
        using HttpResponseMessage response = await http.GetAsync(uri, ct);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            _token = null;
            throw new ProviderException(Loc.T("online.titlovi.rejected"));
        }

        response.EnsureSuccessStatusCode();
        IReadOnlyList<SubtitleCandidate> all = ParseSearch(await OnlineHttp.ReadTextAsync(response, ct));
        return query.IsEpisode
            ? all.Where(c => c.Episode is null or 0 || c.Episode == query.Episode).ToList()
            : all;
    }

    public async Task<DownloadedSubtitle> DownloadAsync(SubtitleCandidate candidate, SubtitleQuery query, CancellationToken ct)
    {
        var uri = new Uri(candidate.DownloadReference);
        OnlineHttp.EnsureAllowed(uri, AllowedHosts);
        await _throttle.WaitAsync(ct);
        using HttpResponseMessage response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
        OnlineHttp.EnsureAllowed(response.RequestMessage?.RequestUri ?? uri, AllowedHosts);
        response.EnsureSuccessStatusCode();
        byte[] bytes = await OnlineHttp.ReadLimitedAsync(response, ct);
        return SafeSubtitleArchive.ExtractBest(bytes, (candidate.ReleaseName ?? "titlovi") + ".srt", query.Info);
    }

    public async Task<string> TestAsync(CancellationToken ct)
    {
        _token = null;
        await EnsureTokenAsync(ct);
        return Loc.T("online.titlovi.loginOk");
    }

    internal static IReadOnlyList<SubtitleCandidate> ParseSearch(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        var list = new List<SubtitleCandidate>();
        JsonElement results = Json.Prop(doc.RootElement, "SubtitleResults");
        if (results.ValueKind != JsonValueKind.Array)
        {
            return list;
        }

        foreach (JsonElement r in results.EnumerateArray())
        {
            string? link = Json.Str(r, "Link");
            string? language = FromTitloviLanguage(Json.Str(r, "Lang"));
            if (link is null || language is null)
            {
                continue;
            }

            list.Add(new SubtitleCandidate
            {
                Provider = "Titlovi.com",
                DownloadReference = link,
                Language = language,
                ReleaseName = Json.Str(r, "Release") is { Length: > 0 } rel ? rel : null,
                Title = Json.Str(r, "Title"),
                Year = (int?)Json.Long(r, "Year"),
                Season = Json.Long(r, "Season") is long s and > 0 ? (int)s : null,
                Episode = Json.Long(r, "Episode") is long e and > 0 ? (int)e : null,
                Downloads = (int)(Json.Long(r, "DownloadCount") ?? 0),
                PageUrl = Json.Str(r, "Link"),
            });
        }

        return list;
    }

    private async Task EnsureTokenAsync(CancellationToken ct)
    {
        if (_token != null && DateTime.UtcNow < _tokenExpires)
        {
            return;
        }

        TitloviCredentials c = credentials() ?? throw new ProviderException(Loc.T("online.titlovi.noAccount"));
        var p = new Dictionary<string, string> { ["username"] = c.Username, ["password"] = c.Password, ["json"] = "true" };
        var uri = new Uri($"{Api}/gettoken?{OnlineHttp.QueryString(p)}");
        OnlineHttp.EnsureAllowed(uri, AllowedHosts);
        await _throttle.WaitAsync(ct);
        using HttpResponseMessage response = await http.PostAsync(uri, content: null, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new ProviderException(response.StatusCode == HttpStatusCode.Forbidden
                ? Loc.T("online.titlovi.loginFailed")
                : Loc.T("online.titlovi.loginFailedCode", (int)response.StatusCode));
        }

        using JsonDocument doc = JsonDocument.Parse(await OnlineHttp.ReadTextAsync(response, ct));
        _token = Json.Str(doc.RootElement, "Token") ?? throw new ProviderException(Loc.T("online.titlovi.noToken"));
        _userId = Json.Str(doc.RootElement, "UserId") ?? string.Empty;
        _tokenExpires = Json.Str(doc.RootElement, "ExpirationDate") is { } exp &&
                        DateTime.TryParse(exp, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out DateTime e)
            ? e.AddMinutes(-5)
            : DateTime.UtcNow.AddHours(6);
    }

    private static string? ToTitloviLanguage(string code) => code switch
    {
        "hr" => "hr",
        "sr" => "sr",
        "bs" => "ba",
        "en" => "en",
        "sl" => "si",
        "mk" => "mk",
        _ => null,
    };

    private static string? FromTitloviLanguage(string? value) => value?.ToLowerInvariant() switch
    {
        "hr" or "hrvatski" => "hr",
        "sr" or "srpski" or "cirilica" => "sr",
        "ba" or "bs" or "bosanski" => "bs",
        "en" or "english" or "engleski" => "en",
        "si" or "sl" or "slovenski" => "sl",
        "mk" or "makedonski" => "mk",
        null => null,
        var other => SubtitleLanguage.Normalize(other),
    };
}

/// <summary>Izvor s unaprijed zadanim rezultatima: za testove i dijagnostiku (bez mreže i ključeva).</summary>
public sealed class CannedSubtitleProvider(string name, IReadOnlyList<SubtitleCandidate> candidates, Func<SubtitleCandidate, DownloadedSubtitle> download)
    : ISubtitleProvider
{
    public string Name => name;

    public bool IsAvailable => true;

    public int Searches { get; private set; }

    public int Downloads { get; private set; }

    public Task<IReadOnlyList<SubtitleCandidate>> SearchAsync(SubtitleQuery query, CancellationToken ct)
    {
        Searches++;
        return Task.FromResult(candidates);
    }

    public Task<DownloadedSubtitle> DownloadAsync(SubtitleCandidate candidate, SubtitleQuery query, CancellationToken ct)
    {
        Downloads++;
        return Task.FromResult(download(candidate));
    }
}

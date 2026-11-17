using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using JebaitedPlayer.Subtitles;

namespace JebaitedPlayer.Online;

public sealed record OpenSubtitlesCredentials(string ApiKey, string? Username, string? Password);

/// <summary>
/// OpenSubtitles.com REST API (v1). Jedini izvor koji uspoređuje otisak datoteke (moviehash),
/// pa daje "isto izdanje" s najvećom sigurnošću.
/// <para>Bez prijave: 5 preuzimanja dnevno po IP adresi; s besplatnim računom više; VIP 1000.</para>
/// </summary>
public sealed class OpenSubtitlesProvider(HttpClient http, Func<OpenSubtitlesCredentials?> credentials) : ISubtitleProvider
{
    private static readonly string[] AllowedHosts = ["opensubtitles.com", "opensubtitles.org"];
    private readonly RequestThrottle _throttle = new(TimeSpan.FromMilliseconds(300));
    private string _host = "api.opensubtitles.com";
    private string? _token;
    private DateTime _tokenExpires;

    public string Name => "OpenSubtitles";

    public bool IsAvailable => credentials() is { ApiKey.Length: > 0 };

    public async Task<IReadOnlyList<SubtitleCandidate>> SearchAsync(SubtitleQuery query, CancellationToken ct)
    {
        OpenSubtitlesCredentials c = credentials() ?? throw new ProviderException(Loc.T("online.noApiKey"));
        var p = new Dictionary<string, string>
        {
            ["languages"] = string.Join(",", query.Languages.Select(ToApiLanguage).Distinct().OrderBy(l => l, StringComparer.Ordinal)),
            ["query"] = query.Title.ToLowerInvariant(),
        };
        if (query.MovieHash is { Length: 16 } hash)
        {
            p["moviehash"] = hash.ToLowerInvariant();
        }

        if (query.IsEpisode)
        {
            p["type"] = "episode";
            p["episode_number"] = query.Episode!.Value.ToString(CultureInfo.InvariantCulture);
            if (query.Season is int s)
            {
                p["season_number"] = s.ToString(CultureInfo.InvariantCulture);
            }
        }
        else
        {
            p["type"] = "movie";
            if (query.Year is int y)
            {
                p["year"] = y.ToString(CultureInfo.InvariantCulture);
            }
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://{_host}/api/v1/subtitles?{OnlineHttp.QueryString(p)}");
        AddHeaders(request, c);
        using HttpResponseMessage response = await SendAsync(request, ct);
        string json = await OnlineHttp.ReadTextAsync(response, ct);
        return ParseSearch(json);
    }

    public async Task<DownloadedSubtitle> DownloadAsync(SubtitleCandidate candidate, SubtitleQuery query, CancellationToken ct)
    {
        OpenSubtitlesCredentials c = credentials() ?? throw new ProviderException(Loc.T("online.noApiKey"));
        await EnsureLoginAsync(c, ct);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://{_host}/api/v1/download")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { file_id = long.Parse(candidate.DownloadReference, CultureInfo.InvariantCulture), sub_format = "srt" }),
                Encoding.UTF8, "application/json"),
        };
        AddHeaders(request, c);
        using HttpResponseMessage response = await SendAsync(request, ct);
        using JsonDocument doc = JsonDocument.Parse(await OnlineHttp.ReadTextAsync(response, ct));
        string link = Json.Str(doc.RootElement, "link") ?? throw new ProviderException(Loc.T("online.os.noLink"));
        string fileName = Json.Str(doc.RootElement, "file_name") ?? candidate.FileName ?? "titlovi.srt";

        var uri = new Uri(link);
        OnlineHttp.EnsureAllowed(uri, AllowedHosts);
        await _throttle.WaitAsync(ct);
        using HttpResponseMessage file = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
        OnlineHttp.EnsureAllowed(file.RequestMessage?.RequestUri ?? uri, AllowedHosts);
        file.EnsureSuccessStatusCode();
        byte[] bytes = await OnlineHttp.ReadLimitedAsync(file, ct);
        return SafeSubtitleArchive.ExtractBest(bytes, fileName, query.Info);
    }

    /// <summary>Provjera ključa i (ako je upisan) računa, za prozor "Izvori titlova".</summary>
    public async Task<string> TestAsync(CancellationToken ct)
    {
        OpenSubtitlesCredentials c = credentials() ?? throw new ProviderException(Loc.T("online.noApiKey"));
        _token = null;
        if (!string.IsNullOrWhiteSpace(c.Username))
        {
            int allowed = await EnsureLoginAsync(c, ct);
            return Loc.T("online.os.loginOk", allowed);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://{_host}/api/v1/infos/formats");
        AddHeaders(request, c);
        using HttpResponseMessage response = await SendAsync(request, ct);
        return Loc.T("online.os.keyOk");
    }

    internal static IReadOnlyList<SubtitleCandidate> ParseSearch(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        var list = new List<SubtitleCandidate>();
        if (!doc.RootElement.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.Array)
        {
            return list;
        }

        foreach (JsonElement item in data.EnumerateArray())
        {
            if (!item.TryGetProperty("attributes", out JsonElement a))
            {
                continue;
            }

            JsonElement files = a.TryGetProperty("files", out JsonElement f) ? f : default;
            if (files.ValueKind != JsonValueKind.Array || files.GetArrayLength() == 0)
            {
                continue;
            }

            JsonElement file = files[0];
            JsonElement feature = a.TryGetProperty("feature_details", out JsonElement fd) ? fd : default;
            string? language = SubtitleLanguage.Normalize(Json.Str(a, "language"));
            long? fileId = Json.Long(file, "file_id");
            if (language is null || fileId is null)
            {
                continue;
            }

            list.Add(new SubtitleCandidate
            {
                Provider = "OpenSubtitles",
                DownloadReference = fileId.Value.ToString(CultureInfo.InvariantCulture),
                Language = language,
                ReleaseName = Json.Str(a, "release"),
                FileName = Json.Str(file, "file_name"),
                HashMatch = Json.Bool(a, "moviehash_match"),
                HearingImpaired = Json.Bool(a, "hearing_impaired"),
                MachineTranslated = Json.Bool(a, "machine_translated") || Json.Bool(a, "ai_translated"),
                FrameRate = Json.Double(a, "fps") is > 0 and var fps ? fps : null,
                Downloads = (int)(Json.Long(a, "download_count") ?? 0),
                Trusted = Json.Bool(a, "from_trusted"),
                Uploader = a.TryGetProperty("uploader", out JsonElement u) ? Json.Str(u, "name") : null,
                Season = feature.ValueKind == JsonValueKind.Object ? (int?)Json.Long(feature, "season_number") : null,
                Episode = feature.ValueKind == JsonValueKind.Object ? (int?)Json.Long(feature, "episode_number") : null,
                Year = feature.ValueKind == JsonValueKind.Object ? (int?)Json.Long(feature, "year") : null,
                Title = feature.ValueKind == JsonValueKind.Object ? Json.Str(feature, "parent_title") ?? Json.Str(feature, "movie_name") ?? Json.Str(feature, "title") : null,
                PageUrl = Json.Str(a, "url"),
            });
        }

        return list;
    }

    private async Task<int> EnsureLoginAsync(OpenSubtitlesCredentials c, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(c.Username) || string.IsNullOrEmpty(c.Password) || (_token != null && DateTime.UtcNow < _tokenExpires))
        {
            return -1;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.opensubtitles.com/api/v1/login")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { username = c.Username, password = c.Password }), Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("Api-Key", c.ApiKey);
        using HttpResponseMessage response = await SendAsync(request, ct, isLogin: true);
        using JsonDocument doc = JsonDocument.Parse(await OnlineHttp.ReadTextAsync(response, ct));
        _token = Json.Str(doc.RootElement, "token") ?? throw new ProviderException(Loc.T("online.os.noToken"));
        _tokenExpires = DateTime.UtcNow.AddHours(12);
        if (Json.Str(doc.RootElement, "base_url") is { Length: > 0 } baseUrl)
        {
            string host = baseUrl.Replace("https://", string.Empty).Replace("http://", string.Empty).TrimEnd('/');
            if (host.EndsWith("opensubtitles.com", StringComparison.OrdinalIgnoreCase))
            {
                _host = host;
            }
        }

        return doc.RootElement.TryGetProperty("user", out JsonElement user) ? (int)(Json.Long(user, "allowed_downloads") ?? -1) : -1;
    }

    private void AddHeaders(HttpRequestMessage request, OpenSubtitlesCredentials c)
    {
        request.Headers.TryAddWithoutValidation("Api-Key", c.ApiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (_token != null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct, bool isLogin = false)
    {
        OnlineHttp.EnsureAllowed(request.RequestUri!, AllowedHosts);
        await _throttle.WaitAsync(ct);
        HttpResponseMessage response = await http.SendAsync(request, ct);
        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        string body = string.Empty;
        try
        {
            body = await OnlineHttp.ReadTextAsync(response, ct);
        }
        catch (ProviderException)
        {
        }

        string? message = TryMessage(body);
        response.Dispose();
        throw response.StatusCode switch
        {
            HttpStatusCode.Unauthorized when isLogin => new ProviderException(Loc.T("online.os.loginFailed")),
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new ProviderException(Loc.T("online.os.keyRejected")),
            HttpStatusCode.NotAcceptable => new ProviderException(Loc.T("online.os.limit", ResetText(body)).Trim()),
            HttpStatusCode.TooManyRequests => new ProviderException(Loc.T("online.os.tooMany")),
            HttpStatusCode.Gone => new ProviderException(Loc.T("online.os.linkExpired")),
            _ => new ProviderException(Loc.T("online.os.error", (int)response.StatusCode) + (message is null ? string.Empty : ": " + message)),
        };
    }

    private static string? TryMessage(string body)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(body);
            return Json.Str(doc.RootElement, "message");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string ResetText(string body)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(body);
            return Json.Str(doc.RootElement, "reset_time") is { } reset ? Loc.T("online.os.resetsIn", reset) : string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    private static string ToApiLanguage(string code) => code switch
    {
        "pt" => "pt-PT",
        _ => code,
    };
}

/// <summary>Pomoćne funkcije za čitanje JSON-a bez rušenja na neočekivane tipove.</summary>
internal static class Json
{
    public static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement v)
            ? v.ValueKind switch
            {
                JsonValueKind.String => v.GetString(),
                JsonValueKind.Number => v.GetRawText(),
                _ => null,
            }
            : null;

    public static long? Long(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out JsonElement v))
        {
            return null;
        }

        return v.ValueKind switch
        {
            JsonValueKind.Number when v.TryGetInt64(out long l) => l,
            JsonValueKind.Number => (long)v.GetDouble(),
            JsonValueKind.String when long.TryParse(v.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long l) => l,
            _ => null,
        };
    }

    public static double? Double(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out JsonElement v))
        {
            return null;
        }

        return v.ValueKind switch
        {
            JsonValueKind.Number => v.GetDouble(),
            JsonValueKind.String when double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double d) => d,
            _ => null,
        };
    }

    public static bool Bool(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement v) &&
        (v.ValueKind == JsonValueKind.True || (v.ValueKind == JsonValueKind.Number && v.GetRawText() != "0") ||
         (v.ValueKind == JsonValueKind.String && (v.GetString() is "1" or "true" or "True")));

    public static JsonElement Prop(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement v) ? v : default;
}

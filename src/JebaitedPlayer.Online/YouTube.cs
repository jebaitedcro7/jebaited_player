using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace JebaitedPlayer.Online;

/// <summary>Što se pušta u ugrađenom YouTube playeru: video, playlista ili video unutar playliste.</summary>
public sealed record YouTubeTarget(string? VideoId, string? PlaylistId);

/// <summary>Rezultat pretrage ili stavka playliste.</summary>
public sealed record YouTubeItem(string VideoId, string Title, string Channel, string? ThumbnailUrl);

/// <summary>
/// Ciklus 6, V9: YouTube Music samo službenim putem. Reprodukcija ide kroz ugrađeni YouTube player (IFrame), a podaci kroz
/// YouTube Data API v3 s korisnikovim vlastitim API ključem. Neslužbene biblioteke (ytmusicapi, yt-dlp) se ne koriste.
/// </summary>
public static partial class YouTubeLinks
{
    [GeneratedRegex(@"^[A-Za-z0-9_-]{11}$")]
    private static partial Regex VideoIdPattern();

    [GeneratedRegex(@"^[A-Za-z0-9_-]{10,64}$")]
    private static partial Regex PlaylistIdPattern();

    /// <summary>Link (youtube.com, music.youtube.com, youtu.be, m.youtube.com; watch, shorts, embed, playlist) ili sam ID videa.</summary>
    public static YouTubeTarget? Parse(string? text)
    {
        string s = (text ?? string.Empty).Trim();
        if (VideoIdPattern().IsMatch(s))
        {
            return new YouTubeTarget(s, null);
        }

        if (!s.Contains("://", StringComparison.Ordinal))
        {
            s = "https://" + s;
        }

        if (!Uri.TryCreate(s, UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("https" or "http"))
        {
            return null;
        }

        string host = uri.Host.ToLowerInvariant();
        Dictionary<string, string> query = SpotifyLoopback.ParseQuery(uri.PathAndQuery);
        string? list = query.TryGetValue("list", out string? l) && PlaylistIdPattern().IsMatch(l) ? l : null;
        string? video = null;
        if (host == "youtu.be")
        {
            video = uri.AbsolutePath.Trim('/');
        }
        else if (("." + host).EndsWith(".youtube.com", StringComparison.Ordinal) || host == "www.youtube-nocookie.com")
        {
            string[] parts = uri.AbsolutePath.Trim('/').Split('/');
            video = parts[0] switch
            {
                "watch" => query.GetValueOrDefault("v"),
                "shorts" or "embed" or "live" when parts.Length > 1 => parts[1],
                _ => null,
            };
        }
        else
        {
            return null;
        }

        video = video is not null && VideoIdPattern().IsMatch(video) ? video : null;
        return video is null && list is null ? null : new YouTubeTarget(video, list);
    }
}

public enum YouTubeError
{
    BadKey,
    Quota,
    Forbidden,
    Network,
    Other,
}

public sealed class YouTubeException(YouTubeError kind, string detail) : Exception(detail)
{
    public YouTubeError Kind { get; } = kind;
}

/// <summary>
/// YouTube Data API v3 s korisnikovim ključem (kvota projekta: zadano 10.000 jedinica dnevno; pretraga troši 100, stavke
/// playliste 1). Samo HTTPS prema www.googleapis.com.
/// </summary>
public sealed class YouTubeClient : IDisposable
{
    private static readonly string[] AllowedHosts = ["www.googleapis.com"];

    private readonly HttpClient _http;
    private readonly Uri _api;
    private readonly bool _testServer;

    public YouTubeClient(string userAgent, Uri? testBase = null)
    {
        _http = OnlineHttp.CreateClient(userAgent);
        _testServer = testBase is not null && IPAddress.TryParse(testBase.Host, out IPAddress? ip) && IPAddress.IsLoopback(ip);
        _api = _testServer ? new Uri(testBase!, "youtube/v3/") : new Uri("https://www.googleapis.com/youtube/v3/");
    }

    /// <summary>Pretraga glazbenih videa (kategorija 10 = Music), najviše 10 rezultata.</summary>
    public async Task<IReadOnlyList<YouTubeItem>> SearchMusicAsync(string apiKey, string query, CancellationToken ct)
    {
        string q = OnlineHttp.QueryString(new Dictionary<string, string>
        {
            ["part"] = "snippet", ["type"] = "video", ["videoCategoryId"] = "10", ["maxResults"] = "10", ["q"] = query.Trim(), ["key"] = apiKey,
        });
        using JsonDocument doc = await GetAsync("search?" + q, ct);
        return Items(doc, item => item.TryGetProperty("id", out JsonElement id) ? Str(id, "videoId") : string.Empty);
    }

    /// <summary>Stavke javne ili nenavedene (unlisted) playliste, najviše 50.</summary>
    public async Task<IReadOnlyList<YouTubeItem>> PlaylistItemsAsync(string apiKey, string playlistId, CancellationToken ct)
    {
        string q = OnlineHttp.QueryString(new Dictionary<string, string>
        {
            ["part"] = "snippet", ["maxResults"] = "50", ["playlistId"] = playlistId, ["key"] = apiKey,
        });
        using JsonDocument doc = await GetAsync("playlistItems?" + q, ct);
        return Items(doc, item => item.TryGetProperty("snippet", out JsonElement sn) && sn.TryGetProperty("resourceId", out JsonElement rid)
            ? Str(rid, "videoId")
            : string.Empty);
    }

    public void Dispose() => _http.Dispose();

    private async Task<JsonDocument> GetAsync(string pathAndQuery, CancellationToken ct)
    {
        var uri = new Uri(_api, pathAndQuery);
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
            throw new YouTubeException(YouTubeError.Network, ex.Message);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new YouTubeException(YouTubeError.Network, ex.Message);
        }

        using (response)
        {
            string text = await OnlineHttp.ReadTextAsync(response, ct);
            if (response.IsSuccessStatusCode)
            {
                return JsonDocument.Parse(text);
            }

            string reason = Reason(text);
            throw new YouTubeException(reason switch
            {
                "quotaExceeded" or "dailyLimitExceeded" or "rateLimitExceeded" => YouTubeError.Quota,
                "keyInvalid" or "badRequest" when text.Contains("API key", StringComparison.OrdinalIgnoreCase) => YouTubeError.BadKey,
                "keyInvalid" => YouTubeError.BadKey,
                _ when response.StatusCode == HttpStatusCode.Forbidden => YouTubeError.Forbidden,
                _ when response.StatusCode == HttpStatusCode.BadRequest && text.Contains("API key", StringComparison.OrdinalIgnoreCase) => YouTubeError.BadKey,
                _ => YouTubeError.Other,
            }, $"{(int)response.StatusCode} {reason}");
        }
    }

    private static IReadOnlyList<YouTubeItem> Items(JsonDocument doc, Func<JsonElement, string> videoId) =>
        doc.RootElement.TryGetProperty("items", out JsonElement items) && items.ValueKind == JsonValueKind.Array
            ? items.EnumerateArray().Select(item =>
            {
                JsonElement sn = item.TryGetProperty("snippet", out JsonElement s) ? s : default;
                string thumb = sn.ValueKind == JsonValueKind.Object && sn.TryGetProperty("thumbnails", out JsonElement t) &&
                               (t.TryGetProperty("medium", out JsonElement m) || t.TryGetProperty("default", out m))
                    ? Str(m, "url")
                    : string.Empty;
                return new YouTubeItem(videoId(item), sn.ValueKind == JsonValueKind.Object ? WebUtility.HtmlDecode(Str(sn, "title")) : string.Empty,
                    sn.ValueKind == JsonValueKind.Object ? Str(sn, sn.TryGetProperty("videoOwnerChannelTitle", out _) ? "videoOwnerChannelTitle" : "channelTitle") : string.Empty,
                    thumb.Length > 0 ? thumb : null);
            }).Where(i => i.VideoId.Length == 11).ToList()
            : [];

    private static string Reason(string text)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(text);
            return doc.RootElement.TryGetProperty("error", out JsonElement e) && e.TryGetProperty("errors", out JsonElement errs) &&
                   errs.ValueKind == JsonValueKind.Array && errs.GetArrayLength() > 0
                ? Str(errs[0], "reason")
                : string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    private static string Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty;
}

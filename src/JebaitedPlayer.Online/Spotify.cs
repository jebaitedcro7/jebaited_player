using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace JebaitedPlayer.Online;

/// <summary>Ciklus 5, V8: tokeni Spotifyja (pristupni traje oko sat vremena, osvježavajući se sprema kriptiran).</summary>
public sealed record SpotifyTokens(string AccessToken, string RefreshToken, DateTime ExpiresAtUtc);

/// <summary>Stanje reprodukcije na korisnikovom Spotify uređaju (GET /me/player).</summary>
public sealed record SpotifyPlayback(bool IsPlaying, long ProgressMs, long DurationMs, string Track, string Artists, string Album,
    string? ImageUrl, string? TrackUri, string? TrackUrl, string? DeviceId, string? DeviceName, int? Volume);

public sealed record SpotifyDevice(string Id, string Name, string Type, bool IsActive, int? Volume);

/// <summary>Playlista, album ili pjesma za popis (Uri pokreće reprodukciju).</summary>
public sealed record SpotifyItem(string Name, string Detail, string Uri, string? ImageUrl, bool IsTrack);

public enum SpotifyError
{
    Unauthorized,
    Forbidden,
    PremiumRequired,
    NoActiveDevice,
    RateLimited,
    Network,
    Other,
}

/// <summary>Greška Spotifyja s vrstom (poruku na jeziku sučelja slaže aplikacija).</summary>
public sealed class SpotifyException(SpotifyError kind, string detail) : Exception(detail)
{
    public SpotifyError Kind { get; } = kind;
}

/// <summary>OAuth 2.0 s PKCE (RFC 7636): bez tajnog ključa, prikladno za desktop aplikaciju.</summary>
public static class SpotifyPkce
{
    /// <summary>Samo ono što daljinski upravljač treba: stanje i upravljanje reprodukcijom, playliste i spremljene pjesme.</summary>
    public const string Scopes = "user-read-playback-state user-modify-playback-state user-read-currently-playing playlist-read-private user-library-read";

    public static string CreateVerifier() => Base64Url(RandomNumberGenerator.GetBytes(48));

    public static string CreateState() => Base64Url(RandomNumberGenerator.GetBytes(16));

    public static string Challenge(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    public static string AuthorizeUrl(Uri accounts, string clientId, string redirectUri, string challenge, string state) =>
        new Uri(accounts, "authorize") + "?" + OnlineHttp.QueryString(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["response_type"] = "code",
            ["redirect_uri"] = redirectUri,
            ["code_challenge_method"] = "S256",
            ["code_challenge"] = challenge,
            ["state"] = state,
            ["scope"] = Scopes,
        });

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>
/// Prijem povratnog poziva prijave na lokalnoj adresi (http://127.0.0.1:port/callback). Samo jedan zahtjev, samo
/// s ovog računala; provjerava se „state”, da tuđa stranica ne može podmetnuti kod.
/// </summary>
public static class SpotifyLoopback
{
    public static async Task<string> WaitForCodeAsync(int port, string expectedState, string pageHtml, CancellationToken ct)
    {
        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        try
        {
            while (true)
            {
                using TcpClient client = await listener.AcceptTcpClientAsync(ct);
                NetworkStream stream = client.GetStream();
                var request = new StringBuilder();
                var buffer = new byte[4096];
                while (!request.ToString().Contains("\r\n\r\n", StringComparison.Ordinal) && request.Length < 16384)
                {
                    int n = await stream.ReadAsync(buffer, ct);
                    if (n == 0)
                    {
                        break;
                    }

                    request.Append(Encoding.ASCII.GetString(buffer, 0, n));
                }

                string line = request.ToString().Split("\r\n")[0];
                string[] parts = line.Split(' ');
                if (parts.Length < 2 || !parts[1].StartsWith("/callback", StringComparison.Ordinal))
                {
                    await RespondAsync(stream, "404 Not Found", string.Empty, ct);
                    continue; // npr. favicon
                }

                Dictionary<string, string> query = ParseQuery(parts[1]);
                await RespondAsync(stream, "200 OK", pageHtml, ct);
                if (!query.TryGetValue("state", out string? state) || state != expectedState)
                {
                    throw new SpotifyException(SpotifyError.Unauthorized, "state mismatch");
                }

                if (query.TryGetValue("error", out string? error))
                {
                    throw new SpotifyException(SpotifyError.Unauthorized, error);
                }

                return query.TryGetValue("code", out string? code) && code.Length > 0
                    ? code
                    : throw new SpotifyException(SpotifyError.Unauthorized, "no code");
            }
        }
        finally
        {
            listener.Stop();
        }
    }

    public static Dictionary<string, string> ParseQuery(string pathAndQuery)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        int q = pathAndQuery.IndexOf('?', StringComparison.Ordinal);
        if (q < 0)
        {
            return result;
        }

        foreach (string pair in pathAndQuery[(q + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = pair.IndexOf('=', StringComparison.Ordinal);
            string key = Uri.UnescapeDataString(eq < 0 ? pair : pair[..eq]);
            string value = eq < 0 ? string.Empty : Uri.UnescapeDataString(pair[(eq + 1)..].Replace('+', ' '));
            result[key] = value;
        }

        return result;
    }

    private static async Task RespondAsync(NetworkStream stream, string status, string html, CancellationToken ct)
    {
        byte[] body = Encoding.UTF8.GetBytes(html);
        byte[] head = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(head, ct);
        await stream.WriteAsync(body, ct);
    }
}

/// <summary>
/// Spotify Web API (službeno; dostupno i u Development modu, 2026). Zvuk svira u Spotify aplikaciji korisnika
/// (Spotify Connect), a player samo prikazuje i upravlja. Za upravljanje Spotify traži Premium.
/// </summary>
public sealed class SpotifyClient : IDisposable
{
    public static readonly Uri DefaultApi = new("https://api.spotify.com/v1/");
    public static readonly Uri DefaultAccounts = new("https://accounts.spotify.com/");

    private static readonly string[] AllowedHosts = ["api.spotify.com", "accounts.spotify.com"];

    private readonly HttpClient _http;
    private readonly Uri _api;
    private readonly Uri _accounts;
    private readonly bool _testServer;

    /// <param name="testBase">Samo za testove: lokalni simulirani poslužitelj (http://127.0.0.1:port/) za API i prijavu.</param>
    public SpotifyClient(string userAgent, Uri? testBase = null)
    {
        _http = OnlineHttp.CreateClient(userAgent);
        _testServer = testBase is not null && IPAddress.TryParse(testBase.Host, out IPAddress? ip) && IPAddress.IsLoopback(ip);
        _api = _testServer ? new Uri(testBase!, "v1/") : DefaultApi;
        _accounts = _testServer ? testBase! : DefaultAccounts;
    }

    public Uri Accounts => _accounts;

    public Task<SpotifyTokens> ExchangeCodeAsync(string clientId, string code, string redirectUri, string verifier, CancellationToken ct) =>
        TokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code", ["code"] = code, ["redirect_uri"] = redirectUri, ["client_id"] = clientId, ["code_verifier"] = verifier,
        }, null, ct);

    public Task<SpotifyTokens> RefreshAsync(string clientId, string refreshToken, CancellationToken ct) =>
        TokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token", ["refresh_token"] = refreshToken, ["client_id"] = clientId,
        }, refreshToken, ct);

    /// <summary>Trenutna reprodukcija; null kad nijedan uređaj ništa ne svira (204).</summary>
    public async Task<SpotifyPlayback?> PlaybackAsync(string token, CancellationToken ct)
    {
        using JsonDocument? doc = await SendAsync(HttpMethod.Get, "me/player?additional_types=track,episode", token, null, ct);
        if (doc is null || doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        JsonElement root = doc.RootElement;
        JsonElement item = root.TryGetProperty("item", out JsonElement i) && i.ValueKind == JsonValueKind.Object ? i : default;
        JsonElement device = root.TryGetProperty("device", out JsonElement d) && d.ValueKind == JsonValueKind.Object ? d : default;
        string artists = item.ValueKind == JsonValueKind.Object && item.TryGetProperty("artists", out JsonElement a) && a.ValueKind == JsonValueKind.Array
            ? string.Join(", ", a.EnumerateArray().Select(x => Str(x, "name")).Where(x => x.Length > 0))
            : item.ValueKind == JsonValueKind.Object && item.TryGetProperty("show", out JsonElement show) ? Str(show, "name") : string.Empty;
        JsonElement album = item.ValueKind == JsonValueKind.Object && item.TryGetProperty("album", out JsonElement al) ? al : default;
        return new SpotifyPlayback(
            root.TryGetProperty("is_playing", out JsonElement p) && p.ValueKind == JsonValueKind.True,
            Long(root, "progress_ms"),
            item.ValueKind == JsonValueKind.Object ? Long(item, "duration_ms") : 0,
            item.ValueKind == JsonValueKind.Object ? Str(item, "name") : string.Empty,
            artists,
            album.ValueKind == JsonValueKind.Object ? Str(album, "name") : string.Empty,
            FirstImage(album.ValueKind == JsonValueKind.Object ? album : item),
            item.ValueKind == JsonValueKind.Object ? Str(item, "uri") : null,
            item.ValueKind == JsonValueKind.Object && item.TryGetProperty("external_urls", out JsonElement ext) ? Str(ext, "spotify") : null,
            device.ValueKind == JsonValueKind.Object ? Str(device, "id") : null,
            device.ValueKind == JsonValueKind.Object ? Str(device, "name") : null,
            device.ValueKind == JsonValueKind.Object && device.TryGetProperty("volume_percent", out JsonElement v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null);
    }

    public async Task<IReadOnlyList<SpotifyDevice>> DevicesAsync(string token, CancellationToken ct)
    {
        using JsonDocument? doc = await SendAsync(HttpMethod.Get, "me/player/devices", token, null, ct);
        return doc is not null && doc.RootElement.TryGetProperty("devices", out JsonElement list)
            ? list.EnumerateArray().Select(x => new SpotifyDevice(Str(x, "id"), Str(x, "name"), Str(x, "type"),
                x.TryGetProperty("is_active", out JsonElement act) && act.ValueKind == JsonValueKind.True,
                x.TryGetProperty("volume_percent", out JsonElement v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null)).ToList()
            : [];
    }

    public async Task<IReadOnlyList<SpotifyItem>> PlaylistsAsync(string token, CancellationToken ct)
    {
        using JsonDocument? doc = await SendAsync(HttpMethod.Get, "me/playlists?limit=50", token, null, ct);
        return Items(doc, x => new SpotifyItem(Str(x, "name"), x.TryGetProperty("owner", out JsonElement o) ? Str(o, "display_name") : string.Empty,
            Str(x, "uri"), FirstImage(x), IsTrack: false));
    }

    public async Task<IReadOnlyList<SpotifyItem>> SavedTracksAsync(string token, CancellationToken ct)
    {
        using JsonDocument? doc = await SendAsync(HttpMethod.Get, "me/tracks?limit=50", token, null, ct);
        return Items(doc, x => x.TryGetProperty("track", out JsonElement t) ? Track(t) : null);
    }

    /// <summary>Pretraga pjesama (u Development modu Spotify vraća najviše 10).</summary>
    public async Task<IReadOnlyList<SpotifyItem>> SearchTracksAsync(string token, string query, CancellationToken ct)
    {
        using JsonDocument? doc = await SendAsync(HttpMethod.Get, "search?type=track&limit=10&q=" + Uri.EscapeDataString(query), token, null, ct);
        return doc is not null && doc.RootElement.TryGetProperty("tracks", out JsonElement tracks) && tracks.TryGetProperty("items", out JsonElement items)
            ? items.EnumerateArray().Select(Track).OfType<SpotifyItem>().ToList()
            : [];
    }

    public Task PlayAsync(string token, string? contextUri, string? trackUri, string? deviceId, CancellationToken ct)
    {
        object? body = contextUri is not null ? new { context_uri = contextUri } : trackUri is not null ? new { uris = new[] { trackUri } } : null;
        return CommandAsync(HttpMethod.Put, "me/player/play" + DeviceQuery(deviceId, "?"), token, body, ct);
    }

    public Task PauseAsync(string token, CancellationToken ct) => CommandAsync(HttpMethod.Put, "me/player/pause", token, null, ct);

    public Task NextAsync(string token, CancellationToken ct) => CommandAsync(HttpMethod.Post, "me/player/next", token, null, ct);

    public Task PreviousAsync(string token, CancellationToken ct) => CommandAsync(HttpMethod.Post, "me/player/previous", token, null, ct);

    public Task SeekAsync(string token, long positionMs, CancellationToken ct) =>
        CommandAsync(HttpMethod.Put, $"me/player/seek?position_ms={Math.Max(0, positionMs)}", token, null, ct);

    public Task VolumeAsync(string token, int percent, CancellationToken ct) =>
        CommandAsync(HttpMethod.Put, $"me/player/volume?volume_percent={Math.Clamp(percent, 0, 100)}", token, null, ct);

    public Task TransferAsync(string token, string deviceId, bool play, CancellationToken ct) =>
        CommandAsync(HttpMethod.Put, "me/player", token, new { device_ids = new[] { deviceId }, play }, ct);

    public void Dispose() => _http.Dispose();

    // --- Pomoćno -----------------------------------------------------------------------------------------------------

    private async Task<SpotifyTokens> TokenAsync(Dictionary<string, string> form, string? oldRefresh, CancellationToken ct)
    {
        var uri = new Uri(_accounts, "api/token");
        Check(uri);
        using var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = new FormUrlEncodedContent(form) };
        using HttpResponseMessage response = await SendRawAsync(request, ct);
        string text = await OnlineHttp.ReadTextAsync(response, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new SpotifyException(SpotifyError.Unauthorized, $"{(int)response.StatusCode}: {Short(text)}");
        }

        using JsonDocument doc = JsonDocument.Parse(text);
        string access = Str(doc.RootElement, "access_token");
        string refresh = Str(doc.RootElement, "refresh_token") is { Length: > 0 } r ? r : oldRefresh ?? string.Empty;
        long seconds = Long(doc.RootElement, "expires_in");
        return access.Length == 0
            ? throw new SpotifyException(SpotifyError.Unauthorized, "no access token")
            : new SpotifyTokens(access, refresh, DateTime.UtcNow.AddSeconds(Math.Max(60, seconds) - 30));
    }

    private async Task CommandAsync(HttpMethod method, string path, string token, object? body, CancellationToken ct)
    {
        using JsonDocument? _ = await SendAsync(method, path, token, body, ct);
    }

    private async Task<JsonDocument?> SendAsync(HttpMethod method, string path, string token, object? body, CancellationToken ct)
    {
        var uri = new Uri(_api, path);
        Check(uri);
        using var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        }
        else if (method != HttpMethod.Get)
        {
            request.Content = new ByteArrayContent([]);
        }

        using HttpResponseMessage response = await SendRawAsync(request, ct);
        string text = response.StatusCode == HttpStatusCode.NoContent ? string.Empty : await OnlineHttp.ReadTextAsync(response, ct);
        if (response.IsSuccessStatusCode)
        {
            return text.Length == 0 ? null : JsonDocument.Parse(text);
        }

        string reason = text.Length > 0 ? ReasonOf(text) : string.Empty;
        throw new SpotifyException(response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => SpotifyError.Unauthorized,
            HttpStatusCode.Forbidden when reason == "PREMIUM_REQUIRED" => SpotifyError.PremiumRequired,
            HttpStatusCode.Forbidden => SpotifyError.Forbidden,
            HttpStatusCode.NotFound when reason == "NO_ACTIVE_DEVICE" || path.StartsWith("me/player", StringComparison.Ordinal) => SpotifyError.NoActiveDevice,
            HttpStatusCode.TooManyRequests => SpotifyError.RateLimited,
            _ => SpotifyError.Other,
        }, $"{(int)response.StatusCode} {reason}: {Short(text)}");
    }

    private async Task<HttpResponseMessage> SendRawAsync(HttpRequestMessage request, CancellationToken ct)
    {
        try
        {
            return await _http.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new SpotifyException(SpotifyError.Network, ex.Message);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new SpotifyException(SpotifyError.Network, ex.Message);
        }
    }

    private void Check(Uri uri)
    {
        if (!_testServer)
        {
            OnlineHttp.EnsureAllowed(uri, AllowedHosts);
        }
    }

    private static string DeviceQuery(string? deviceId, string prefix) => deviceId is { Length: > 0 } ? $"{prefix}device_id={Uri.EscapeDataString(deviceId)}" : string.Empty;

    private static IReadOnlyList<SpotifyItem> Items(JsonDocument? doc, Func<JsonElement, SpotifyItem?> map) =>
        doc is not null && doc.RootElement.TryGetProperty("items", out JsonElement items) && items.ValueKind == JsonValueKind.Array
            ? items.EnumerateArray().Select(map).OfType<SpotifyItem>().ToList()
            : [];

    private static SpotifyItem? Track(JsonElement t)
    {
        if (t.ValueKind != JsonValueKind.Object || Str(t, "uri") is not { Length: > 0 } uri)
        {
            return null;
        }

        string artists = t.TryGetProperty("artists", out JsonElement a) && a.ValueKind == JsonValueKind.Array
            ? string.Join(", ", a.EnumerateArray().Select(x => Str(x, "name")))
            : string.Empty;
        JsonElement album = t.TryGetProperty("album", out JsonElement al) ? al : default;
        return new SpotifyItem(Str(t, "name"), artists, uri, FirstImage(album.ValueKind == JsonValueKind.Object ? album : t), IsTrack: true);
    }

    private static string? FirstImage(JsonElement e) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty("images", out JsonElement images) && images.ValueKind == JsonValueKind.Array
            ? images.EnumerateArray().Select(x => Str(x, "url")).FirstOrDefault(x => x.Length > 0)
            : null;

    private static string ReasonOf(string text)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(text);
            return doc.RootElement.TryGetProperty("error", out JsonElement e) && e.ValueKind == JsonValueKind.Object ? Str(e, "reason") : string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    private static string Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty;

    private static long Long(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;

    private static string Short(string text) => text.Length <= 200 ? text : text[..200];
}

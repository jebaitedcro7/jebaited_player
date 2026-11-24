using System.Net;
using System.Text;
using System.Text.Json;

namespace JebaitedPlayer.Iptv;

/// <summary>
/// Klijent Xtream Codes API-ja (player_api.php): prijava, kategorije, uživo kanali i kratki EPG.
/// Lozinka se nikad ne upisuje u poruke grešaka. Prijava se šalje jednom po povezivanju (bez ponavljanja pri odbijanju).
/// </summary>
public sealed class XtreamClient
{
    private readonly HttpClient _http;
    private readonly string _user;
    private readonly string _pass;

    public XtreamClient(HttpClient http, string serverUrl, string user, string pass)
    {
        _http = http;
        _user = user;
        _pass = pass;
        BaseUrl = NormalizeBase(serverUrl);
    }

    public string BaseUrl { get; }

    /// <summary>Veza nije šifrirana (http). Sučelje to mora jasno reći.</summary>
    public bool IsEncrypted => BaseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    public static string NormalizeBase(string url)
    {
        url = url.Trim();
        if (!url.Contains("://", StringComparison.Ordinal))
        {
            url = "http://" + url;
        }

        // Korisnik često zalijepi cijeli get.php/player_api.php URL: zadrži samo izvor.
        if (Uri.TryCreate(url, UriKind.Absolute, out Uri? u))
        {
            return u.GetLeftPart(UriPartial.Authority);
        }

        throw new IptvException(IptvErrorKind.InvalidResponse, "The server address is not valid.");
    }

    private string ApiUrl(string? action, string? extra = null)
    {
        var sb = new StringBuilder(BaseUrl).Append("/player_api.php?username=").Append(Uri.EscapeDataString(_user))
            .Append("&password=").Append(Uri.EscapeDataString(_pass));
        if (action is not null)
        {
            sb.Append("&action=").Append(action);
        }

        if (extra is not null)
        {
            sb.Append('&').Append(extra);
        }

        return sb.ToString();
    }

    public string XmltvUrl => $"{BaseUrl}/xmltv.php?username={Uri.EscapeDataString(_user)}&password={Uri.EscapeDataString(_pass)}";

    /// <summary>Adresa toka. Sadrži vjerodajnice: ne zapisivati u log.</summary>
    public string BuildStreamUrl(string streamId, string extension = "m3u8") =>
        $"{BaseUrl}/live/{Uri.EscapeDataString(_user)}/{Uri.EscapeDataString(_pass)}/{streamId}.{extension}";

    private async Task<JsonElement> GetJsonAsync(string url, CancellationToken ct)
    {
        try
        {
            using HttpResponseMessage resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new IptvException(IptvErrorKind.BadCredentials, "The server rejected the login (HTTP " + (int)resp.StatusCode + ").");
            }

            if (!resp.IsSuccessStatusCode)
            {
                throw new IptvException(IptvErrorKind.Http, "The server answered with HTTP " + (int)resp.StatusCode + ".");
            }

            await using Stream s = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using JsonDocument doc = await JsonDocument.ParseAsync(s, cancellationToken: ct).ConfigureAwait(false);
            return doc.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new IptvException(IptvErrorKind.InvalidResponse, "The response is not valid JSON (this may not be an Xtream server).", ex);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw new IptvException(IptvErrorKind.Cancelled, "Cancelled.");
        }
        catch (OperationCanceledException ex)
        {
            throw new IptvException(IptvErrorKind.Timeout, "The server did not answer in time.", ex);
        }
        catch (HttpRequestException ex)
        {
            // Poruka izvorne greške može sadržavati URL s lozinkom: ne prosljeđujemo je.
            throw new IptvException(IptvErrorKind.Unreachable, "The server is not reachable (" + (ex.StatusCode?.ToString() ?? "network error") + ").");
        }
    }

    /// <summary>Provjera računa. Vraća opis (status, istek) ili baca <see cref="IptvException"/>.</summary>
    public async Task<XtreamAccount> AuthenticateAsync(CancellationToken ct = default)
    {
        JsonElement root = await GetJsonAsync(ApiUrl(null), ct).ConfigureAwait(false);
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("user_info", out JsonElement ui) || ui.ValueKind != JsonValueKind.Object)
        {
            throw new IptvException(IptvErrorKind.BadCredentials, "The server did not accept the username or password.");
        }

        if (AsInt(ui, "auth") != 1)
        {
            throw new IptvException(IptvErrorKind.BadCredentials, "The server did not accept the username or password.");
        }

        string status = AsString(ui, "status") ?? "";
        DateTimeOffset? exp = long.TryParse(AsString(ui, "exp_date"), out long e) && e > 0 ? DateTimeOffset.FromUnixTimeSeconds(e) : null;
        if (status.Equals("Expired", StringComparison.OrdinalIgnoreCase) || (exp is not null && exp < DateTimeOffset.UtcNow))
        {
            throw new IptvException(IptvErrorKind.Expired, "The subscription has expired.");
        }

        if (status.Equals("Banned", StringComparison.OrdinalIgnoreCase) || status.Equals("Disabled", StringComparison.OrdinalIgnoreCase))
        {
            throw new IptvException(IptvErrorKind.BadCredentials, "The account is disabled (" + status + ").");
        }

        return new XtreamAccount(status, exp, AsInt(ui, "max_connections"), AsInt(ui, "active_cons"));
    }

    public async Task<IReadOnlyDictionary<string, string>> GetLiveCategoriesAsync(CancellationToken ct = default)
    {
        JsonElement root = await GetJsonAsync(ApiUrl("get_live_categories"), ct).ConfigureAwait(false);
        var map = new Dictionary<string, string>();
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement c in root.EnumerateArray())
            {
                string? id = AsString(c, "category_id");
                string? name = AsString(c, "category_name");
                if (id is not null && name is not null)
                {
                    map[id] = name;
                }
            }
        }

        return map;
    }

    public async Task<IptvCatalog> GetLiveCatalogAsync(CancellationToken ct = default)
    {
        IReadOnlyDictionary<string, string> cats = await GetLiveCategoriesAsync(ct).ConfigureAwait(false);
        JsonElement root = await GetJsonAsync(ApiUrl("get_live_streams"), ct).ConfigureAwait(false);
        return ParseLiveStreams(root, cats);
    }

    internal IptvCatalog ParseLiveStreams(JsonElement root, IReadOnlyDictionary<string, string> cats)
    {
        if (root.ValueKind != JsonValueKind.Array)
        {
            throw new IptvException(IptvErrorKind.InvalidResponse, "The channel list is not in the expected format.");
        }

        var list = new List<IptvChannel>();
        foreach (JsonElement s in root.EnumerateArray())
        {
            string? id = AsString(s, "stream_id");
            string? name = AsString(s, "name");
            if (string.IsNullOrEmpty(id) || string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            string cat = AsString(s, "category_id") is { } cid && cats.TryGetValue(cid, out string? cn) ? cn : "—";
            string? logo = AsString(s, "stream_icon");
            string? epg = AsString(s, "epg_channel_id");
            list.Add(new IptvChannel(id, name.Trim(), BuildStreamUrl(id), cat,
                string.IsNullOrWhiteSpace(logo) ? null : logo,
                string.IsNullOrWhiteSpace(epg) ? null : epg,
                AsInt(s, "num"), AsInt(s, "tv_archive") == 1));
        }

        return new IptvCatalog(list, XmltvUrl, IsEncrypted ? null : "unencrypted");
    }

    /// <summary>Kratki EPG jednog kanala (sljedećih nekoliko programa).</summary>
    public async Task<IReadOnlyList<EpgProgram>> GetShortEpgAsync(IptvChannel channel, int limit = 8, CancellationToken ct = default)
    {
        JsonElement root = await GetJsonAsync(ApiUrl("get_short_epg", $"stream_id={Uri.EscapeDataString(channel.Id)}&limit={limit}"), ct).ConfigureAwait(false);
        var list = new List<EpgProgram>();
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("epg_listings", out JsonElement arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement p in arr.EnumerateArray())
            {
                if (long.TryParse(AsString(p, "start_timestamp"), out long a) && long.TryParse(AsString(p, "stop_timestamp"), out long b))
                {
                    string title = DecodeB64(AsString(p, "title"));
                    if (title.Length > 0)
                    {
                        string d = DecodeB64(AsString(p, "description"));
                        list.Add(new EpgProgram(channel.EpgId ?? channel.Id, DateTimeOffset.FromUnixTimeSeconds(a),
                            DateTimeOffset.FromUnixTimeSeconds(b), title, d.Length == 0 ? null : d));
                    }
                }
            }
        }

        return list;
    }

    internal static string DecodeB64(string? s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return "";
        }

        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(s)).Trim();
        }
        catch (FormatException)
        {
            return s.Trim();
        }
    }

    internal static string? AsString(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out JsonElement v))
        {
            return null;
        }

        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            JsonValueKind.True => "1",
            JsonValueKind.False => "0",
            _ => null,
        };
    }

    internal static int AsInt(JsonElement e, string name) => int.TryParse(AsString(e, name), out int i) ? i : 0;
}

public sealed record XtreamAccount(string Status, DateTimeOffset? Expires, int MaxConnections, int ActiveConnections);

using System.Net;
using System.Web;

namespace JebaitedPlayer.Iptv;

/// <summary>Rezultat prepoznavanja unosa u čarobnjaku za povezivanje.</summary>
public sealed record DetectedSource(IptvSourceKind Kind, string? Server, string? Username, string? Password, string? Url, string? FilePath);

public static class IptvSourceDetector
{
    /// <summary>Pogodi vrstu izvora iz jednog unesenog teksta (putanja, M3U adresa ili Xtream adresa, i ona s get.php?username=...).</summary>
    public static DetectedSource Detect(string input)
    {
        input = input.Trim().Trim('"');
        if (input.Length > 1 && input[1] == ':' && !input.Contains("://", StringComparison.Ordinal) || input.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return new DetectedSource(IptvSourceKind.M3uFile, null, null, null, null, input);
        }

        string withScheme = input.Contains("://", StringComparison.Ordinal) ? input : "http://" + input;
        if (Uri.TryCreate(withScheme, UriKind.Absolute, out Uri? u))
        {
            var q = HttpUtility.ParseQueryString(u.Query);
            string? user = q["username"], pass = q["password"];
            if (!string.IsNullOrEmpty(user) && !string.IsNullOrEmpty(pass)
                && (u.AbsolutePath.Contains("get.php", StringComparison.OrdinalIgnoreCase) || u.AbsolutePath.Contains("player_api", StringComparison.OrdinalIgnoreCase)
                    || u.AbsolutePath.Contains("xmltv", StringComparison.OrdinalIgnoreCase)))
            {
                return new DetectedSource(IptvSourceKind.Xtream, u.GetLeftPart(UriPartial.Authority), user, pass, null, null);
            }

            string path = u.AbsolutePath;
            if (path.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase)
                || path.Contains("get.php", StringComparison.OrdinalIgnoreCase) || path.Contains("playlist", StringComparison.OrdinalIgnoreCase))
            {
                return new DetectedSource(IptvSourceKind.M3uUrl, null, null, null, withScheme, null);
            }

            return new DetectedSource(IptvSourceKind.Xtream, u.GetLeftPart(UriPartial.Authority), null, null, null, null);
        }

        return new DetectedSource(IptvSourceKind.M3uUrl, null, null, null, input, null);
    }
}

/// <summary>
/// Jedna povezana usluga: učitava kanale i EPG. Vjerodajnice (već otključane) prima u konstruktoru i nikad ih ne zapisuje.
/// Svaki <c>Connect</c> šalje jednu prijavu, bez automatskog ponavljanja.
/// </summary>
public sealed class IptvSession
{
    private readonly HttpClient _http;
    private readonly IptvSourceConfig _config;
    private readonly string? _password;
    private readonly string? _url;
    private XtreamClient? _xtream;

    public IptvSession(HttpClient http, IptvSourceConfig config, string? password, string? url)
    {
        _http = http;
        _config = config;
        _password = password;
        _url = url;
    }

    public EpgIndex Epg { get; } = new();

    public string? XmltvUrl { get; private set; }

    public bool IsEncrypted => _config.Kind switch
    {
        IptvSourceKind.Xtream => _xtream?.IsEncrypted ?? (_config.Server?.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ?? false),
        IptvSourceKind.M3uUrl => _url?.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ?? false,
        _ => true,
    };

    public bool HasShortEpg => _xtream is not null;

    public async Task<(IptvCatalog Catalog, XtreamAccount? Account)> ConnectAsync(CancellationToken ct = default)
    {
        switch (_config.Kind)
        {
            case IptvSourceKind.Xtream:
                if (string.IsNullOrWhiteSpace(_config.Server) || string.IsNullOrWhiteSpace(_config.Username) || string.IsNullOrEmpty(_password))
                {
                    throw new IptvException(IptvErrorKind.BadCredentials, "Server address, username or password is missing.");
                }

                _xtream = new XtreamClient(_http, _config.Server, _config.Username, _password);
                XtreamAccount acc = await _xtream.AuthenticateAsync(ct).ConfigureAwait(false);
                IptvCatalog cat = await _xtream.GetLiveCatalogAsync(ct).ConfigureAwait(false);
                XmltvUrl = string.IsNullOrWhiteSpace(_config.XmltvUrl) ? cat.XmltvUrl : _config.XmltvUrl;
                return (cat, acc);

            case IptvSourceKind.M3uUrl:
                if (string.IsNullOrWhiteSpace(_url))
                {
                    throw new IptvException(IptvErrorKind.InvalidResponse, "The playlist address is missing.");
                }

                string text = await GetTextAsync(_url, ct).ConfigureAwait(false);
                IptvCatalog m = M3uParser.Parse(text);
                if (m.Channels.Count == 0)
                {
                    throw new IptvException(IptvErrorKind.InvalidResponse, "The playlist has no channels (not a valid M3U list).");
                }

                XmltvUrl = string.IsNullOrWhiteSpace(_config.XmltvUrl) ? m.XmltvUrl : _config.XmltvUrl;
                return (m, null);

            default:
                if (string.IsNullOrWhiteSpace(_config.FilePath) || !File.Exists(_config.FilePath))
                {
                    throw new IptvException(IptvErrorKind.InvalidResponse, "The playlist file does not exist.");
                }

                IptvCatalog f = await Task.Run(() =>
                {
                    using StreamReader r = File.OpenText(_config.FilePath);
                    return M3uParser.Parse(r);
                }, ct).ConfigureAwait(false);
                if (f.Channels.Count == 0)
                {
                    throw new IptvException(IptvErrorKind.InvalidResponse, "The file has no channels.");
                }

                XmltvUrl = string.IsNullOrWhiteSpace(_config.XmltvUrl) ? f.XmltvUrl : _config.XmltvUrl;
                return (f, null);
        }
    }

    private async Task<string> GetTextAsync(string url, CancellationToken ct)
    {
        try
        {
            using HttpResponseMessage resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
            if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new IptvException(IptvErrorKind.BadCredentials, "The server refused access (HTTP " + (int)resp.StatusCode + ").");
            }

            if (!resp.IsSuccessStatusCode)
            {
                throw new IptvException(IptvErrorKind.Http, "The server answered with HTTP " + (int)resp.StatusCode + ".");
            }

            return await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw new IptvException(IptvErrorKind.Cancelled, "Cancelled.");
        }
        catch (OperationCanceledException ex)
        {
            throw new IptvException(IptvErrorKind.Timeout, "The server did not answer in time.", ex);
        }
        catch (HttpRequestException)
        {
            throw new IptvException(IptvErrorKind.Unreachable, "The server is not reachable.");
        }
    }

    /// <summary>Preuzmi XMLTV i ubaci programe za zadane kanale (streaming, bez učitavanja cijele datoteke u memoriju).</summary>
    public async Task<int> LoadEpgAsync(IReadOnlyList<IptvChannel> channels, DateTimeOffset now, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(XmltvUrl))
        {
            return 0;
        }

        var ids = new HashSet<string>(channels.Where(c => c.EpgId is not null).Select(c => c.EpgId!), StringComparer.OrdinalIgnoreCase);
        if (ids.Count == 0)
        {
            return 0;
        }

        try
        {
            using HttpResponseMessage resp = await _http.GetAsync(XmltvUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                throw new IptvException(IptvErrorKind.Http, "Guide: HTTP " + (int)resp.StatusCode + ".");
            }

            await using Stream raw = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            Stream s = raw;
            // .xml.gz bez Content-Encoding zaglavlja.
            var buffered = new BufferedStream(raw, 64 * 1024);
            s = buffered;
            if (XmltvUrl.Contains(".gz", StringComparison.OrdinalIgnoreCase))
            {
                s = new System.IO.Compression.GZipStream(buffered, System.IO.Compression.CompressionMode.Decompress);
            }

            List<EpgProgram> programs = await Task.Run(
                () => XmltvParser.Parse(s, ids, now.AddHours(-6), now.AddHours(48), ct), ct).ConfigureAwait(false);
            Epg.Load(programs, now);
            return programs.Count;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw new IptvException(IptvErrorKind.Cancelled, "Cancelled.");
        }
        catch (OperationCanceledException ex)
        {
            throw new IptvException(IptvErrorKind.Timeout, "The guide server did not answer in time.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Xml.XmlException or InvalidDataException)
        {
            throw new IptvException(IptvErrorKind.InvalidResponse, "The guide could not be loaded.");
        }
    }

    /// <summary>Kratki EPG izabranog kanala (Xtream); koristi se kad XMLTV nema podataka za kanal.</summary>
    public async Task LoadShortEpgAsync(IptvChannel channel, CancellationToken ct = default)
    {
        if (_xtream is null)
        {
            return;
        }

        IReadOnlyList<EpgProgram> p = await _xtream.GetShortEpgAsync(channel, 10, ct).ConfigureAwait(false);
        if (p.Count > 0)
        {
            Epg.Merge(channel.EpgId ?? channel.Id, p);
        }
    }
}

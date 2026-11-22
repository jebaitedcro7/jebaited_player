using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace JebaitedPlayer.Torlink;

/// <summary>Izdanje izvornog Torlinka (samo informacija: verzija i tekst promjena; binarnih datoteka tamo nema).</summary>
public sealed record UpstreamRelease(string Tag, string Version, string Notes, string Url, DateTimeOffset? Published);

/// <summary>Naš runtime paket (pre-release <c>torlink-runtime-&lt;torlink&gt;-r&lt;N&gt;</c> na repozitoriju playera).</summary>
public sealed record RuntimeRelease(string Tag, TorlinkVersion Version, string ZipName, string ZipUrl, long ZipSize, string SumsUrl, string Notes);

public interface IReleaseFeed
{
    Task<UpstreamRelease?> GetUpstreamLatestAsync(CancellationToken ct);

    Task<RuntimeRelease?> GetRuntimeLatestAsync(CancellationToken ct);

    Task<string> GetTextAsync(string url, CancellationToken ct);

    Task DownloadAsync(string url, string destinationFile, long maxBytes, IProgress<double>? progress, CancellationToken ct);
}

/// <summary>
/// GitHub kao izvor: samo HTTPS i samo poslužitelji GitHuba (api.github.com, github.com, *.githubusercontent.com).
/// Za testove (JEBAITED_DATA_DIR) dopušten je i lokalni poslužitelj preko <see cref="TestEndpoints"/>.
/// </summary>
public sealed class GitHubReleaseFeed : IReleaseFeed
{
    public const string UpstreamApi = "https://api.github.com/repos/baairon/torlink/releases/latest";
    public const string RuntimeApi = "https://api.github.com/repos/jebaitedcro7/jebaited_player/releases?per_page=40";
    public const string RuntimeTagPrefix = "torlink-runtime-";

    private static readonly string[] AllowedHostSuffixes = ["github.com", "githubusercontent.com"];
    private static readonly Regex TagPattern = new(@"^torlink-runtime-(?<v>\d+\.\d+\.\d+)-r(?<r>\d+)$", RegexOptions.Compiled);

    private readonly HttpClient _http;
    private readonly string _upstreamApi;
    private readonly string _runtimeApi;
    private readonly bool _allowLocal;

    public GitHubReleaseFeed(HttpMessageHandler? handler = null, string? upstreamApi = null, string? runtimeApi = null, bool allowLocalHttp = false)
    {
        _http = handler is null
            ? new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 5, ConnectTimeout = TimeSpan.FromSeconds(10) })
            : new HttpClient(handler, disposeHandler: false);
        _http.Timeout = TimeSpan.FromMinutes(10);
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("jebaited_player", "2"));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _upstreamApi = upstreamApi ?? UpstreamApi;
        _runtimeApi = runtimeApi ?? RuntimeApi;
        _allowLocal = allowLocalHttp;
    }

    public async Task<UpstreamRelease?> GetUpstreamLatestAsync(CancellationToken ct)
    {
        using JsonDocument doc = JsonDocument.Parse(await GetTextAsync(_upstreamApi, ct).ConfigureAwait(false));
        JsonElement r = doc.RootElement;
        string tag = ApiStatus.Str(r, "tag_name");
        if (tag.Length == 0 || !TorlinkVersion.TryParse(tag, 1, out _))
        {
            return null;
        }

        DateTimeOffset? published = DateTimeOffset.TryParse(ApiStatus.Str(r, "published_at"), out DateTimeOffset p) ? p : null;
        return new UpstreamRelease(tag, tag.TrimStart('v', 'V'), ApiStatus.Str(r, "body"), ApiStatus.Str(r, "html_url"), published);
    }

    public async Task<RuntimeRelease?> GetRuntimeLatestAsync(CancellationToken ct)
    {
        using JsonDocument doc = JsonDocument.Parse(await GetTextAsync(_runtimeApi, ct).ConfigureAwait(false));
        return PickLatestRuntime(doc.RootElement);
    }

    /// <summary>Iz popisa izdanja uzima najviše <c>torlink-runtime-x.y.z-rN</c> koje ima zip i SHA256SUMS.txt (čista funkcija, testirana).</summary>
    public static RuntimeRelease? PickLatestRuntime(JsonElement releases)
    {
        RuntimeRelease? best = null;
        if (releases.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (JsonElement rel in releases.EnumerateArray())
        {
            string tag = ApiStatus.Str(rel, "tag_name");
            Match m = TagPattern.Match(tag);
            if (!m.Success || !TorlinkVersion.TryParse(m.Groups["v"].Value, int.Parse(m.Groups["r"].Value), out TorlinkVersion ver))
            {
                continue;
            }

            string? zipName = null, zipUrl = null, sumsUrl = null;
            long size = 0;
            if (rel.TryGetProperty("assets", out JsonElement assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement a in assets.EnumerateArray())
                {
                    string name = ApiStatus.Str(a, "name");
                    string url = ApiStatus.Str(a, "browser_download_url");
                    if (name.EndsWith("-win-x64.zip", StringComparison.OrdinalIgnoreCase) && name.StartsWith(tag, StringComparison.OrdinalIgnoreCase))
                    {
                        zipName = name;
                        zipUrl = url;
                        size = (long)ApiStatus.Num(a, "size");
                    }
                    else if (name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        sumsUrl = url;
                    }
                }
            }

            if (zipName is null || zipUrl is null || sumsUrl is null)
            {
                continue;
            }

            if (best is null || ver > best.Version)
            {
                best = new RuntimeRelease(tag, ver, zipName, zipUrl, size, sumsUrl, ApiStatus.Str(rel, "body"));
            }
        }

        return best;
    }

    public async Task<string> GetTextAsync(string url, CancellationToken ct)
    {
        EnsureAllowed(url);
        using HttpResponseMessage r = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        r.EnsureSuccessStatusCode();
        EnsureAllowed(r.RequestMessage?.RequestUri?.ToString() ?? url);
        if (r.Content.Headers.ContentLength is > 4 * 1024 * 1024)
        {
            throw new InvalidDataException("response too large");
        }

        return await r.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    public async Task DownloadAsync(string url, string destinationFile, long maxBytes, IProgress<double>? progress, CancellationToken ct)
    {
        EnsureAllowed(url);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
        using HttpResponseMessage r = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        r.EnsureSuccessStatusCode();
        EnsureAllowed(r.RequestMessage?.RequestUri?.ToString() ?? url);
        long? total = r.Content.Headers.ContentLength;
        if (total > maxBytes)
        {
            throw new InvalidDataException("download too large");
        }

        string tmp = destinationFile + ".part";
        await using (Stream src = await r.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
        await using (var dst = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            byte[] buf = new byte[81920];
            long done = 0;
            int n;
            while ((n = await src.ReadAsync(buf, ct).ConfigureAwait(false)) > 0)
            {
                done += n;
                if (done > maxBytes)
                {
                    throw new InvalidDataException("download too large");
                }

                await dst.WriteAsync(buf.AsMemory(0, n), ct).ConfigureAwait(false);
                if (total is > 0)
                {
                    progress?.Report(done / (double)total.Value);
                }
            }
        }

        File.Move(tmp, destinationFile, overwrite: true);
    }

    private void EnsureAllowed(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            throw new InvalidDataException("bad url");
        }

        if (_allowLocal && uri.IsLoopback && uri.Scheme is "http" or "https")
        {
            return;
        }

        bool hostOk = AllowedHostSuffixes.Any(s => uri.Host.Equals(s, StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith("." + s, StringComparison.OrdinalIgnoreCase));
        if (uri.Scheme != Uri.UriSchemeHttps || !hostOk)
        {
            throw new InvalidDataException($"address not allowed: {uri.Scheme}://{uri.Host}");
        }
    }
}

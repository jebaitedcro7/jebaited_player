using System.Net;
using System.Text;

namespace JebaitedPlayer.Online;

/// <summary>
/// Zajednička pravila za mrežu: samo HTTPS, samo dopušteni poslužitelji, ograničena veličina odgovora,
/// vremenska ograničenja i razmak između zahtjeva (da ne preopteretimo izvore).
/// </summary>
public static class OnlineHttp
{
    public const long MaxResponseBytes = 10L * 1024 * 1024;

    public static HttpClient CreateClient(string userAgent)
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
            UseCookies = false,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };

        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(25) };
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", userAgent);
        return client;
    }

    /// <summary>Provjera adrese prije zahtjeva i nakon preusmjeravanja.</summary>
    public static void EnsureAllowed(Uri uri, IReadOnlyCollection<string> allowedHostSuffixes)
    {
        bool hostOk = allowedHostSuffixes.Any(s =>
            uri.Host.Equals(s, StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith("." + s, StringComparison.OrdinalIgnoreCase));
        if (uri.Scheme != Uri.UriSchemeHttps || !hostOk)
        {
            throw new ProviderException($"Odbijena nesigurna ili nepoznata adresa: {uri.Scheme}://{uri.Host}");
        }
    }

    public static async Task<byte[]> ReadLimitedAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.Content.Headers.ContentLength is long declared && declared > MaxResponseBytes)
        {
            throw new ProviderException("Odgovor izvora je prevelik.");
        }

        await using Stream stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > MaxResponseBytes)
            {
                throw new ProviderException("Odgovor izvora je prevelik.");
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    public static async Task<string> ReadTextAsync(HttpResponseMessage response, CancellationToken ct) =>
        Encoding.UTF8.GetString(await ReadLimitedAsync(response, ct));

    /// <summary>Upitni niz: abecedno, mala slova za vrijednosti po želji, razmak kao "+".</summary>
    public static string QueryString(IEnumerable<KeyValuePair<string, string>> parameters) =>
        string.Join("&", parameters
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value).Replace("%20", "+")}"));
}

/// <summary>Najmanji razmak između zahtjeva prema jednom izvoru.</summary>
public sealed class RequestThrottle(TimeSpan minInterval)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private long _last;

    public async Task WaitAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            long wait = _last + (long)minInterval.TotalMilliseconds - Environment.TickCount64;
            if (wait > 0)
            {
                await Task.Delay((int)wait, ct);
            }

            _last = Environment.TickCount64;
        }
        finally
        {
            _gate.Release();
        }
    }
}

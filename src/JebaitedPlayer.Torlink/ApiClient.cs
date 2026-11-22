using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace JebaitedPlayer.Torlink;

public sealed class TorlinkApiException : Exception
{
    public TorlinkApiException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>Tanki klijent za <c>torlnk serve</c> (127.0.0.1, bearer token). Tijelo odgovora je ograničeno, nema ponavljanja POST-a.</summary>
public sealed class ApiClient : IDisposable
{
    public const int MaxTorrentBytes = 700 * 1024; // JSON omot sa base64 mora stati u Torlinkov limit od 1 MB

    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    public ApiClient(int port, string token, HttpMessageHandler? handler = null, TimeSpan? timeout = null)
    {
        _http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        _ownsHttp = true;
        _http.BaseAddress = new Uri($"http://127.0.0.1:{port}/");
        _http.Timeout = timeout ?? TimeSpan.FromSeconds(8);
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public void Dispose()
    {
        if (_ownsHttp)
        {
            _http.Dispose();
        }
    }

    /// <summary>Verzija Torlinka ako proces odgovara (bez tokena), inače null.</summary>
    public async Task<string?> HealthAsync(CancellationToken ct = default)
    {
        try
        {
            using HttpResponseMessage r = await _http.GetAsync("health", ct).ConfigureAwait(false);
            if (!r.IsSuccessStatusCode)
            {
                return null;
            }

            using JsonDocument doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            return doc.RootElement.TryGetProperty("version", out JsonElement v) ? v.GetString() : string.Empty;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            if (ct.IsCancellationRequested)
            {
                throw;
            }

            return null;
        }
    }

    public async Task<ApiStatus> GetStatusAsync(CancellationToken ct = default)
    {
        try
        {
            using HttpResponseMessage r = await _http.GetAsync("downloads", ct).ConfigureAwait(false);
            if (r.StatusCode == HttpStatusCode.Unauthorized)
            {
                throw new TorlinkApiException("unauthorized");
            }

            r.EnsureSuccessStatusCode();
            return ApiStatus.Parse(await r.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new TorlinkApiException("status", ex);
        }
    }

    public Task<AddOutcome> AddMagnetAsync(string magnetOrInfoHash, CancellationToken ct = default) =>
        PostAddAsync(JsonSerializer.Serialize(new { magnet = magnetOrInfoHash.Trim() }), ct);

    public Task<AddOutcome> AddTorrentFileAsync(byte[] torrentBytes, CancellationToken ct = default)
    {
        if (torrentBytes.Length > MaxTorrentBytes)
        {
            return Task.FromResult(AddOutcome.TooLarge);
        }

        return PostAddAsync(JsonSerializer.Serialize(new { torrent = Convert.ToBase64String(torrentBytes) }), ct);
    }

    /// <summary>Radnje: pause, resume, start-seed, stop-seed, remove (zadrži datoteke), delete (obriši i datoteke).</summary>
    public async Task<bool> ControlAsync(string id, string action, CancellationToken ct = default)
    {
        using var body = new StringContent(JsonSerializer.Serialize(new { id, action }), Encoding.UTF8, "application/json");
        try
        {
            using HttpResponseMessage r = await _http.PostAsync("control", body, ct).ConfigureAwait(false);
            return r.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new TorlinkApiException("control", ex);
        }
    }

    private async Task<AddOutcome> PostAddAsync(string json, CancellationToken ct)
    {
        using var body = new StringContent(json, Encoding.UTF8, "application/json");
        try
        {
            using HttpResponseMessage r = await _http.PostAsync("add", body, ct).ConfigureAwait(false);
            return r.StatusCode switch
            {
                HttpStatusCode.OK => AddOutcome.Added,
                HttpStatusCode.BadRequest => AddOutcome.Invalid,
                HttpStatusCode.RequestEntityTooLarge => AddOutcome.TooLarge,
                HttpStatusCode.Unauthorized => AddOutcome.Unauthorized,
                _ => AddOutcome.Failed,
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new TorlinkApiException("add", ex);
        }
    }
}

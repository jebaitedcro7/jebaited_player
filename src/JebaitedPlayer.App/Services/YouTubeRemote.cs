using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.Online;

namespace JebaitedPlayer.Services;

/// <summary>Stanje ugrađenog YouTube playera (iz IFrame API-ja).</summary>
public sealed record YouTubeState(int PlayerState, double CurrentTime, double Duration, string Title, string VideoId)
{
    /// <summary>IFrame API: 1 = svira, 2 = pauza, 3 = učitava, 0 = kraj, 5 = spreman, -1 = nije pokrenut.</summary>
    public bool IsPlaying => PlayerState == 1;
}

/// <summary>
/// Ciklus 6, V9: YouTube Music samo službenim putem: ugrađeni YouTube player u vidljivom prozoru (YouTubeWindow) i
/// Data API s korisnikovim vlastitim ključem (kriptiran u youtube-key.dat). Zvuk ne prolazi kroz player (ni EQ ni Discord).
/// Dok je YouTube izvor, mini player prikazuje YOUTUBE i tipke idu ugrađenom playeru.
/// </summary>
internal sealed class YouTubeRemote : IDisposable
{
    public const string ApiKeyGuide = "https://console.cloud.google.com/apis/library/youtube.googleapis.com";

    private readonly string _keyFile;
    private readonly bool _persist;
    private YouTubeClient _client = new($"jebaited_player/{AppPaths.Version} (+https://github.com/jebaitedcro7/jebaited_player)");
    private string? _apiKey;
    private bool _isActiveSource;

    public YouTubeRemote(string dataDir, bool persist = true)
    {
        _keyFile = Path.Combine(dataDir, "youtube-key.dat");
        _persist = persist;
        if (File.Exists(_keyFile))
        {
            try
            {
                _apiKey = Secret.Unprotect(File.ReadAllText(_keyFile));
            }
            catch (IOException ex)
            {
                Log.Warn($"YouTube ključ nije pročitan: {ex.Message}");
            }
        }
    }

    public event EventHandler? Changed;

    /// <summary>Naredbe ugrađenom playeru (postavlja YouTubeWindow dok je otvoren).</summary>
    public Func<string, Task>? Command { get; set; }

    public YouTubeState? State { get; private set; }

    public string Status { get; private set; } = string.Empty;

    public bool HasApiKey => _apiKey is { Length: > 0 };

    public bool IsWindowOpen => Command is not null;

    public bool IsActiveSource
    {
        get => _isActiveSource;
        set
        {
            if (_isActiveSource != value)
            {
                _isActiveSource = value;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    internal void UseTestServer(Uri baseUri, string apiKey)
    {
        _client.Dispose();
        _client = new YouTubeClient("test", baseUri);
        _apiKey = apiKey;
    }

    public void SetApiKey(string? key)
    {
        _apiKey = string.IsNullOrWhiteSpace(key) ? null : key.Trim();
        if (_persist)
        {
            try
            {
                if (_apiKey is null)
                {
                    File.Delete(_keyFile);
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_keyFile)!);
                    File.WriteAllText(_keyFile, Secret.Protect(_apiKey) ?? string.Empty);
                }
            }
            catch (IOException ex)
            {
                Log.Warn($"YouTube ključ nije spremljen: {ex.Message}");
            }
        }

        // Stara poruka (npr. kvota ili kriv ključ) ne vrijedi za novi ključ.
        Status = _apiKey is null ? Loc.T("youtube.keyRemoved") : string.Empty;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void ReportState(YouTubeState state)
    {
        State = state;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetStatus(string status)
    {
        Status = status;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task<IReadOnlyList<YouTubeItem>> SearchAsync(string query) =>
        await CallAsync(c => c.SearchMusicAsync(_apiKey!, query, CancellationToken.None));

    public async Task<IReadOnlyList<YouTubeItem>> PlaylistAsync(string playlistId) =>
        await CallAsync(c => c.PlaylistItemsAsync(_apiKey!, playlistId, CancellationToken.None));

    private async Task<IReadOnlyList<YouTubeItem>> CallAsync(Func<YouTubeClient, Task<IReadOnlyList<YouTubeItem>>> call)
    {
        if (!HasApiKey)
        {
            SetStatus(Loc.T("youtube.keyFirst"));
            return [];
        }

        try
        {
            IReadOnlyList<YouTubeItem> result = await call(_client);
            SetStatus(string.Empty);
            return result;
        }
        catch (YouTubeException ex)
        {
            Log.Warn($"YouTube: {ex.Kind} {ex.Message}");
            SetStatus(ex.Kind switch
            {
                YouTubeError.BadKey => Loc.T("youtube.errKey"),
                YouTubeError.Quota => Loc.T("youtube.errQuota"),
                YouTubeError.Network => Loc.T("youtube.errNetwork"),
                _ => Loc.T("youtube.errOther"),
            });
            return [];
        }
        catch (ProviderException ex)
        {
            SetStatus(ex.Message);
            return [];
        }
    }

    /// <summary>Naredba playeru (playVideo, pauseVideo, nextVideo…); ništa ako prozor nije otvoren.</summary>
    public Task SendAsync(string script) => Command?.Invoke(script) ?? Task.CompletedTask;

    public Task TogglePlayAsync() => SendAsync(State is { IsPlaying: true } ? "player.pauseVideo()" : "player.playVideo()");

    public Task PauseAsync() => SendAsync("player.pauseVideo()");

    public Task NextAsync() => SendAsync("player.nextVideo()");

    public Task PreviousAsync() => SendAsync("player.previousVideo()");

    public void Dispose() => _client.Dispose();
}

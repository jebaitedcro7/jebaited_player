using System.Diagnostics;
using System.Net.Sockets;
using System.Windows.Threading;
using JebaitedPlayer.Diagnostics;
using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.Online;

namespace JebaitedPlayer.Services;

/// <summary>
/// Ciklus 5, V8 (S1): „Spotify daljinski”. Player prikazuje i upravlja reprodukcijom u korisnikovoj Spotify aplikaciji
/// (Spotify Connect, službeni Web API). Zvuk ne prolazi kroz player (ni EQ ni Discord). Upravljanje traži Premium.
/// Stanje se osvježava svake 2 s samo dok je nešto otvoreno što ga prikazuje (prozor Spotify ili mini player u načinu Spotify).
/// </summary>
internal sealed class SpotifyRemote : IDisposable
{
    public const int CallbackPort = 43821;

    public static string RedirectUri => $"http://127.0.0.1:{CallbackPort}/callback";

    public const string DeveloperDashboard = "https://developer.spotify.com/dashboard";

    private readonly AppSettings _settings;
    private readonly bool _persist;
    private readonly string _tokenFile;
    private readonly DispatcherTimer _poll;
    private SpotifyClient _client;
    private SpotifyTokens? _tokens;
    private int _consumers;
    private bool _busy;
    private bool _isActiveSource;

    /// <param name="persist">Spremanje postavki (u dijagnostici samo uz testnu mapu podataka, nikad tvoje postavke).</param>
    public SpotifyRemote(AppSettings settings, string dataDir, bool persist = true)
    {
        _settings = settings;
        _persist = persist;
        _tokenFile = Path.Combine(dataDir, "spotify-auth.dat");
        _client = new SpotifyClient(UserAgent);
        _poll = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, async (_, _) => await RefreshStateAsync(), Dispatcher.CurrentDispatcher);
        _poll.Stop();
    }

    private static string UserAgent => $"jebaited_player/{AppPaths.Version} (+https://github.com/jebaitedcro7/jebaited_player)";

    /// <summary>Stanje ili poruka promijenjeni (prozor i mini player osvježavaju prikaz).</summary>
    public event EventHandler? Changed;

    public SpotifyPlayback? State { get; private set; }

    /// <summary>Zadnja poruka za korisnika (greška ili uputa); prazno = sve u redu.</summary>
    public string Status { get; private set; } = string.Empty;

    public bool HasClientId => _settings.SpotifyClientId is { Length: > 0 };

    public bool IsSignedIn => _tokens is not null || File.Exists(_tokenFile);

    /// <summary>Mini player prikazuje i upravlja Spotifyjem (dok se ne pusti lokalna datoteka ili radio).</summary>
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

    /// <summary>Zahtjevi poslani simuliranom poslužitelju (dijagnostika).</summary>
    internal bool IsTestServer { get; private set; }

    /// <summary>Dijagnostika: simulirani Spotify (lokalni poslužitelj) i gotov token, bez prave prijave.</summary>
    internal void UseTestServer(Uri baseUri, string accessToken)
    {
        _client.Dispose();
        _client = new SpotifyClient(UserAgent, baseUri);
        _tokens = new SpotifyTokens(accessToken, "test-refresh", DateTime.UtcNow.AddHours(1));
        IsTestServer = true;
    }

    public void SetClientId(string? clientId)
    {
        _settings.SpotifyClientId = string.IsNullOrWhiteSpace(clientId) ? null : clientId.Trim();
        if (_persist)
        {
            _settings.Save();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // --- Prijava -----------------------------------------------------------------------------------------------------

    /// <summary>Prijava u pregledniku (PKCE). Povratni poziv stiže na lokalnu adresu; čeka se najviše 3 minute.</summary>
    public async Task<bool> SignInAsync()
    {
        if (_settings.SpotifyClientId is not { Length: > 0 } clientId)
        {
            return false;
        }

        string verifier = SpotifyPkce.CreateVerifier();
        string state = SpotifyPkce.CreateState();
        string url = SpotifyPkce.AuthorizeUrl(_client.Accounts, clientId, RedirectUri, SpotifyPkce.Challenge(verifier), state);
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try
        {
            string page = $"<!doctype html><meta charset=\"utf-8\"><title>jebaited_player</title><body style=\"font-family:Segoe UI;padding:40px\">" +
                          $"<h2>{System.Net.WebUtility.HtmlEncode(Loc.T("spotify.signedInPage"))}</h2></body>";
            Task<string> wait = SpotifyLoopback.WaitForCodeAsync(CallbackPort, state, page, cts.Token);
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            SetStatus(Loc.T("spotify.waitingForBrowser"));
            string code = await wait;
            SpotifyTokens tokens = await _client.ExchangeCodeAsync(clientId, code, RedirectUri, verifier, cts.Token);
            SaveTokens(tokens);
            SetStatus(string.Empty);
            DiagLog.Info(LogCat.Usage, "spotifyapi.signin", nameof(SpotifyRemote), "Spotify signed in");
            await RefreshStateAsync();
            return true;
        }
        catch (SocketException)
        {
            SetStatus(Loc.T("spotify.portBusy", CallbackPort));
        }
        catch (OperationCanceledException)
        {
            SetStatus(Loc.T("spotify.signInTimeout"));
        }
        catch (SpotifyException ex)
        {
            Log.Warn($"Spotify prijava: {ex.Message}");
            SetStatus(Loc.T("spotify.signInFailed"));
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Warn($"Spotify prijava: {ex.Message}");
            SetStatus(Loc.T("spotify.signInFailed"));
        }

        return false;
    }

    public void SignOut()
    {
        _tokens = null;
        State = null;
        IsActiveSource = false;
        try
        {
            File.Delete(_tokenFile);
        }
        catch (IOException ex)
        {
            Log.Warn($"Spotify odjava: {ex.Message}");
        }

        SetStatus(string.Empty);
        DiagLog.Info(LogCat.Usage, "spotifyapi.signout", nameof(SpotifyRemote), "Spotify signed out");
    }

    private void SaveTokens(SpotifyTokens tokens)
    {
        _tokens = tokens;
        if (IsTestServer)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_tokenFile)!);
            File.WriteAllText(_tokenFile, Secret.Protect(tokens.RefreshToken) ?? string.Empty);
        }
        catch (IOException ex)
        {
            Log.Warn($"Spotify token nije spremljen: {ex.Message}");
        }
    }

    private async Task<string?> AccessTokenAsync(bool forceRefresh)
    {
        if (_tokens is { } t && !forceRefresh && t.ExpiresAtUtc > DateTime.UtcNow)
        {
            return t.AccessToken;
        }

        if (IsTestServer)
        {
            return _tokens?.AccessToken;
        }

        string? refresh = _tokens?.RefreshToken;
        if (refresh is null && File.Exists(_tokenFile))
        {
            refresh = Secret.Unprotect(File.ReadAllText(_tokenFile));
        }

        if (refresh is null || _settings.SpotifyClientId is not { Length: > 0 } clientId)
        {
            return null;
        }

        SaveTokens(await _client.RefreshAsync(clientId, refresh, CancellationToken.None));
        return _tokens!.AccessToken;
    }

    // --- Pozivi -------------------------------------------------------------------------------------------------------

    /// <summary>Poziv s jednim ponovnim pokušajem nakon isteka tokena; greška postaje poruka korisniku.</summary>
    /// <param name="clearStatus">Uspjeh briše zadnju poruku (naredbe); osvježavanje stanja je ne briše, da greška ostane vidljiva.</param>
    public async Task<T?> CallAsync<T>(Func<SpotifyClient, string, Task<T>> call, bool clearStatus = true)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                string? token = await AccessTokenAsync(forceRefresh: attempt > 0);
                if (token is null)
                {
                    SetStatus(HasClientId ? Loc.T("spotify.signInFirst") : Loc.T("spotify.setupFirst"));
                    return default;
                }

                T result = await call(_client, token);
                if (clearStatus && Status.Length > 0 && Status != Loc.T("spotify.waitingForBrowser"))
                {
                    SetStatus(string.Empty);
                }

                return result;
            }
            catch (SpotifyException ex) when (ex.Kind == SpotifyError.Unauthorized && attempt == 0)
            {
                // Istekao token: osvježi i pokušaj još jednom.
            }
            catch (SpotifyException ex)
            {
                Log.Warn($"Spotify: {ex.Kind} {ex.Message}");
                SetStatus(Message(ex.Kind));
                return default;
            }
            catch (ProviderException ex)
            {
                SetStatus(ex.Message);
                return default;
            }
        }

        return default;
    }

    public Task RunAsync(Func<SpotifyClient, string, Task> call) => CallAsync<bool>(async (c, t) =>
    {
        await call(c, t);
        return true;
    });

    public static string Message(SpotifyError kind) => kind switch
    {
        SpotifyError.Unauthorized => Loc.T("spotify.errUnauthorized"),
        SpotifyError.Forbidden => Loc.T("spotify.errForbidden"),
        SpotifyError.PremiumRequired => Loc.T("spotify.errPremium"),
        SpotifyError.NoActiveDevice => Loc.T("spotify.errNoDevice"),
        SpotifyError.RateLimited => Loc.T("spotify.errRateLimited"),
        SpotifyError.Network => Loc.T("spotify.errNetwork"),
        _ => Loc.T("spotify.errOther"),
    };

    public async Task RefreshStateAsync()
    {
        if (_busy || !IsSignedIn)
        {
            return;
        }

        _busy = true;
        try
        {
            State = await CallAsync((c, t) => c.PlaybackAsync(t, CancellationToken.None), clearStatus: false);
            Changed?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>Naredba, pa novo stanje (Spotify treba trenutak da primijeni promjenu).</summary>
    private async Task CommandAsync(Func<SpotifyClient, string, Task> call, bool makeActive = true)
    {
        await RunAsync(call);
        if (makeActive && Status.Length == 0)
        {
            IsActiveSource = true;
        }

        await Task.Delay(IsTestServer ? 50 : 350);
        await RefreshStateAsync();
    }

    public Task TogglePlayAsync() => State is { IsPlaying: true }
        ? CommandAsync((c, t) => c.PauseAsync(t, CancellationToken.None))
        : CommandAsync((c, t) => c.PlayAsync(t, null, null, null, CancellationToken.None));

    public Task PauseAsync() => CommandAsync((c, t) => c.PauseAsync(t, CancellationToken.None));

    public Task NextAsync() => CommandAsync((c, t) => c.NextAsync(t, CancellationToken.None));

    public Task PreviousAsync() => CommandAsync((c, t) => c.PreviousAsync(t, CancellationToken.None));

    public Task SeekAsync(long positionMs) => CommandAsync((c, t) => c.SeekAsync(t, positionMs, CancellationToken.None));

    public Task VolumeAsync(int percent) => CommandAsync((c, t) => c.VolumeAsync(t, percent, CancellationToken.None), makeActive: false);

    public Task PlayAsync(SpotifyItem item) => CommandAsync((c, t) => c.PlayAsync(t, item.IsTrack ? null : item.Uri, item.IsTrack ? item.Uri : null, null, CancellationToken.None));

    public Task TransferAsync(SpotifyDevice device) => CommandAsync((c, t) => c.TransferAsync(t, device.Id, play: true, CancellationToken.None));

    public async Task<IReadOnlyList<SpotifyItem>> PlaylistsAsync() => await CallAsync((c, t) => c.PlaylistsAsync(t, CancellationToken.None)) ?? [];

    public async Task<IReadOnlyList<SpotifyItem>> SavedTracksAsync() => await CallAsync((c, t) => c.SavedTracksAsync(t, CancellationToken.None)) ?? [];

    public async Task<IReadOnlyList<SpotifyItem>> SearchAsync(string query) =>
        await CallAsync((c, t) => c.SearchTracksAsync(t, query, CancellationToken.None)) ?? [];

    public async Task<IReadOnlyList<SpotifyDevice>> DevicesAsync() => await CallAsync((c, t) => c.DevicesAsync(t, CancellationToken.None)) ?? [];

    // --- Osvježavanje samo dok je potrebno ----------------------------------------------------------------------------

    public void AddConsumer()
    {
        if (++_consumers == 1)
        {
            _poll.Start();
            _ = RefreshStateAsync();
        }
    }

    public void RemoveConsumer()
    {
        if (_consumers > 0 && --_consumers == 0)
        {
            _poll.Stop();
        }
    }

    private void SetStatus(string status)
    {
        Status = status;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _poll.Stop();
        _client.Dispose();
    }
}

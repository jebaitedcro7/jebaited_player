using System.Windows;
using System.Windows.Input;
using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.Library;
using JebaitedPlayer.Services;
using JebaitedPlayer.ViewModels;
using JebaitedPlayer.Views;

namespace JebaitedPlayer;

/// <summary>
/// Mini Music Player (ciklus 2): glavni prozor se sakrije, a mini prozor upravlja istim mpv-om i bibliotekom,
/// pa se glazba ne prekida. Ctrl+M (ili izbornik) prebacuje naprijed-natrag.
/// </summary>
public partial class MainWindow : IMiniPlayerHost
{
    private MiniPlayerWindow? _mini;

    /// <summary>Korisnik je sam otvorio datoteku: prvo učitavanje odlučuje mini ili puni player (glazba / film).</summary>
    private bool _userOpenPending;

    /// <summary>Dijagnostika ne prebacuje sama (testovi drugih dijelova), osim scenarija koji to testira.</summary>
    private bool _diagAutoSwitch;

    /// <summary>Glazba (datoteka ili internetski stream) ili video.</summary>
    internal static bool IsMusic(string path) => MediaScanner.IsAudio(path) || PlayerViewModel.IsStreamPath(path);

    /// <summary>
    /// Prije svakog učitavanja: equalizer za glazbu ili za filmove (1.4.1) i, ako je korisnik sam otvorio datoteku,
    /// automatski mini player za glazbu i puni player za film.
    /// </summary>
    private void OnLoadStarting(string path)
    {
        bool music = IsMusic(path);
        if (music != MpvConfig.MusicMode)
        {
            MpvConfig.MusicMode = music;
            ((ISettingsHost)this).ApplyPlaybackSettings();
            _mini?.BindEqualizer();
            _playerSettingsWindow?.OnMediaKindChanged();
        }

        if (_userOpenPending)
        {
            _userOpenPending = false;
            if (_settings.MiniPlayer.AutoSwitch && (!_options.IsDiagnostics || _diagAutoSwitch))
            {
                if (music && !IsMiniPlayerActive)
                {
                    ShowMiniPlayer();
                }
                else if (!music && IsMiniPlayerActive)
                {
                    ((IMiniPlayerHost)this).ShowFullPlayer();
                }
            }
        }
    }

    /// <summary>VU filtar samo dok je mini player otvoren i VU nije isključen.</summary>
    private void RefreshVuFilter()
    {
        bool vu = IsMiniPlayerActive && _settings.MiniPlayer.VuStyle != "off";
        if (vu != MpvConfig.VuMeter)
        {
            MpvConfig.VuMeter = vu;
            ((ISettingsHost)this).ApplyPlaybackSettings();
        }
    }

    internal bool IsMiniPlayerActive => _mini is { IsVisible: true };

    private RadioWindow? _radio;

    internal RadioWindow? RadioWindowForTests => _radio;

    /// <summary>Radio (ciklus 3): prozor s postajama, uz mini player ili puni player.</summary>
    internal void ShowRadio(Window? owner = null)
    {
        if (_radio is { IsLoaded: true })
        {
            _radio.Activate();
            return;
        }

        _radio = new RadioWindow(PlayStream, _store, () => new RadioPlayback(_vm.CurrentPath, _vm.IsLoading, _vm.Position, _vm.ErrorMessage))
        {
            Owner = owner ?? (IsMiniPlayerActive ? _mini : this),
        };
        _radio.Closed += (_, _) => _radio = null;
        _radio.Show();
    }

    /// <summary>Pokreni internetski stream (radio): mimo popisa i povijesti gledanja, kao glazba (mini player, glazbeni EQ).</summary>
    internal void PlayStream(string url, string name)
    {
        // 1.5.2: mpv se stvara tek pri prvom otvaranju; radio može biti prvo što se pusti (npr. odmah iz mini playera).
        if (!TryEnsurePlayer())
        {
            return;
        }

        _userOpenPending = true;
        _vm.Library.DetachForStream();
        _vm.Load(url, startPaused: false, displayTitle: name);
        Log.Info("Radio: postaja pokrenuta.");
    }

    internal MiniPlayerWindow? MiniPlayer => _mini;

    internal void ToggleMiniPlayer()
    {
        if (IsMiniPlayerActive)
        {
            ((IMiniPlayerHost)this).ShowFullPlayer();
        }
        else
        {
            ShowMiniPlayer();
        }
    }

    internal void ShowMiniPlayer()
    {
        if (_vm.IsFullscreen)
        {
            SetFullscreen(false);
        }

        // Dok je glavni prozor još vidljiv: pripremi mpv, da mini player (i radio iz njega) može odmah svirati.
        TryEnsurePlayer();
        _mini ??= new MiniPlayerWindow(_vm, _settings, this);
        _mini.Show();
        _mini.Activate();
        if (_radio is { IsLoaded: true } radio && ReferenceEquals(radio.Owner, this))
        {
            // Prozor Radio ostaje vidljiv kad se glavni prozor sakrije (1.5.1).
            radio.Owner = _mini;
            radio.Activate();
        }
        RefreshVuFilter();
        Hide();
        UpdateOverlayVisibility();
        Log.Info("Mini player otvoren.");
    }

    void IMiniPlayerHost.ShowFullPlayer()
    {
        if (_radio is { IsLoaded: true } radio && ReferenceEquals(radio.Owner, _mini))
        {
            radio.Owner = this;
        }

        _mini?.Hide();
        RefreshVuFilter();
        Show();
        Activate();
        UpdateOverlayVisibility();
        Log.Info("Mini player zatvoren, natrag na puni player.");
    }

    void IMiniPlayerHost.ClosePlayer() => Close();

    void IMiniPlayerHost.ShowOpenDialog(Window owner) => ShowOpenDialog(owner);

    void IMiniPlayerHost.OpenFiles(IReadOnlyList<string> paths) => OpenFiles(paths);

    void IMiniPlayerHost.ApplyPlaybackSettings() => ((ISettingsHost)this).ApplyPlaybackSettings();

    bool IMiniPlayerHost.HandleShortcut(Key key, ModifierKeys mods) => HandleShortcut(key, mods);

    string? IMiniPlayerHost.GetMpvProperty(string name) => _player?.GetPropertyString(name);

    void IMiniPlayerHost.RefreshAudioFilters() => RefreshVuFilter();

    void IMiniPlayerHost.ShowSettings() => ShowPlayerSettings(SettingsTab.Appearance);

    void IMiniPlayerHost.ShowRadio(Window owner) => ShowRadio(owner);

    void IMiniPlayerHost.ShowSpotify(Window owner) => ShowSpotify(owner);

    SpotifyRemote IMiniPlayerHost.Spotify => Spotify;

    // --- Ciklus 5, V8: Spotify daljinski --------------------------------------------------------------------------------

    private SpotifyRemote? _spotify;
    private SpotifyWindow? _spotifyWindow;
    private bool _spotifyWasActive;

    internal SpotifyWindow? SpotifyWindowForTests => _spotifyWindow;

    /// <summary>Jedan za cijeli player; stvara se tek kad zatreba (bez Spotifyja nema ni mreže ni timera).</summary>
    internal SpotifyRemote Spotify
    {
        get
        {
            if (_spotify is null)
            {
                _spotify = new SpotifyRemote(_settings, AppPaths.DataDir,
                    persist: !_options.IsDiagnostics || Environment.GetEnvironmentVariable("JEBAITED_DATA_DIR") is { Length: > 0 });
                _spotify.Changed += (_, _) => OnSpotifyChanged();
                _vm.LoadStarting += (_, _) => _spotify.IsActiveSource = false;

                // Nastavak lokalne reprodukcije (npr. razmak u punom playeru) vraća lokalni izvor.
                _vm.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(PlayerViewModel.IsPaused) && !_vm.IsPaused && _vm.HasMedia)
                    {
                        _spotify.IsActiveSource = false;
                    }
                };
            }

            return _spotify;
        }
    }

    /// <summary>Spotify je postao izvor: lokalni video ili radio se zaustavlja, da ne sviraju dva zvuka.</summary>
    private void OnSpotifyChanged()
    {
        bool active = _spotify?.IsActiveSource == true;
        if (active && !_spotifyWasActive && _vm.HasMedia)
        {
            if (_vm.IsLiveStream)
            {
                _vm.StopPlayback();
            }
            else
            {
                _vm.SetPaused(true);
            }
        }

        _spotifyWasActive = active;
        _mini?.Tick();
    }

    internal void ShowSpotify(Window? owner = null)
    {
        if (_spotifyWindow is { IsLoaded: true })
        {
            _spotifyWindow.Activate();
            return;
        }

        _spotifyWindow = new SpotifyWindow(Spotify) { Owner = owner ?? (IsMiniPlayerActive ? _mini : this) };
        _spotifyWindow.Closed += (_, _) => _spotifyWindow = null;
        _spotifyWindow.Show();
    }

    /// <summary>Pokreni mpv ako još ne radi (inače bi učitavanje bilo tiho preskočeno). Greška se zapiše i javi.</summary>
    private bool TryEnsurePlayer()
    {
        try
        {
            EnsurePlayer();
            return true;
        }
        catch (Exception ex)
        {
            Log.Stability.Error("Pokretanje mpv-a", ex);
            Notify(Loc.T("error.engineStart", ex.Message));
            return false;
        }
    }

    /// <summary>Zatvaranje playera: mini prozor se zatvara s njim.</summary>
    private void CloseMiniPlayer()
    {
        if (_mini is { } mini)
        {
            // Sljedeće pokretanje bez datoteke opet otvara mini player, na istom mjestu (1.4.1).
            _settings.MiniPlayer.WasOpen = mini.IsVisible;
            mini.AllowClose = true;
            mini.Close();
            _mini = null;
        }

        MpvConfig.VuMeter = false;
    }
}

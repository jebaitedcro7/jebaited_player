using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.Mpv;
using JebaitedPlayer.Services;
using JebaitedPlayer.Views;

namespace JebaitedPlayer;

/// <summary>
/// Postavke u karticama i povijest nadogradnji (UPGRADE 3): otvaranje prozora, primjena postavki zvuka, slike i prozora
/// odmah na player, popis audio uređaja i izbornik broja verzije na početnom ekranu.
/// </summary>
public partial class MainWindow : ISettingsHost
{
    private PlayerSettingsWindow? _playerSettingsWindow;
    private UpdateHistoryWindow? _historyWindow;
    private DiagnosticsLogWindow? _logsWindow;
    private bool _fullscreenOnLoad;

    /// <summary>Postavke na zadanoj kartici; ako su već otvorene, samo prijeđe na tu karticu.</summary>
    private void ShowPlayerSettings() => ShowPlayerSettings(SettingsTab.General);

    private void ShowPlayerSettings(SettingsTab tab, string? savedSnapshot = null)
    {
        if (_playerSettingsWindow is { IsLoaded: true })
        {
            _playerSettingsWindow.SelectTab(tab);
            _playerSettingsWindow.Activate();
            return;
        }

        _playerSettingsWindow = new PlayerSettingsWindow(_settings, _vm, Updates, this, tab, savedSnapshot)
        {
            Owner = IsVisible ? this : _mini ?? (Window)this,
        };
        _playerSettingsWindow.Closed += (_, _) =>
        {
            _playerSettingsWindow = null;
            UpdateOnlineSetupButton();
            Activate();
        };
        _playerSettingsWindow.Show();
    }

    /// <summary>Postavke titlova (izbornik titlova) = kartica "Titlovi" u postavkama.</summary>
    private void ShowSubtitleSettings() => ShowPlayerSettings(SettingsTab.Subtitles);

    private void ShowUpdateHistory()
    {
        if (_historyWindow is { IsLoaded: true })
        {
            _historyWindow.Activate();
            return;
        }

        Window owner = _playerSettingsWindow is { IsLoaded: true } ? _playerSettingsWindow : this;
        _historyWindow = new UpdateHistoryWindow(Updates.CurrentVersion, checkOnline: !_options.IsDiagnostics || _options.DiagOnlineLive) { Owner = owner };
        _historyWindow.Closed += (_, _) => _historyWindow = null;
        _historyWindow.Show();
    }

    /// <summary>Klik na broj verzije (dolje desno na početnom ekranu): povijest nadogradnji, provjera, o aplikaciji.</summary>
    private void OnVersionClick(object sender, RoutedEventArgs e) => ShowVersionMenu();

    private void ShowVersionMenu()
    {
        // Iznad broja verzije, desnim rubom uz desni rub (broj je u donjem desnom kutu prozora).
        var menu = new ContextMenu
        {
            PlacementTarget = VersionButton,
            Placement = PlacementMode.Custom,
            CustomPopupPlacementCallback = (popup, target, _) =>
                [new CustomPopupPlacement(new Point(target.Width - popup.Width, -popup.Height - 4), PopupPrimaryAxis.Horizontal)],
        };
        menu.Items.Add(Item(Loc.T("history.menuView"), null, ShowUpdateHistory));
        menu.Items.Add(Item(Loc.T("history.menuCheck"), null, () => ShowPlayerSettings(SettingsTab.Updates)));
        menu.Items.Add(Item(Loc.T("history.menuAbout"), null, () => ShowPlayerSettings(SettingsTab.About)));
        OpenMenu(menu);
    }

    /// <summary>Video je učitan: cijeli zaslon ako je tako postavljeno (samo za video koji je korisnik otvorio).</summary>
    private void OnMediaLoadedForWindow()
    {
        if (_fullscreenOnLoad && !_vm.IsAudioOnly && !_vm.IsFullscreen)
        {
            SetFullscreen(true);
        }

        _fullscreenOnLoad = false;
    }

    /// <summary>Player iznad ostalih prozora dok se video reproducira (postavka); dijagnostika ima svoj redoslijed.</summary>
    private void ApplyWindowSettings()
    {
        if (_options.IsDiagnostics)
        {
            return;
        }

        Topmost = _settings.OnTopWhilePlaying && _vm.HasMedia;
    }

    void ISettingsHost.OpenSources() => ShowSourcesWindow();

    void ISettingsHost.OpenOnlineSetup() => ShowOnlineSetup();

    void ISettingsHost.RestartForUpdate() => RestartForUpdate();

    void ISettingsHost.ShowUpdateHistory() => ShowUpdateHistory();

    void ISettingsHost.ShowLogs()
    {
        if (_logsWindow is { IsLoaded: true })
        {
            _logsWindow.Activate();
            return;
        }

        _logsWindow = new DiagnosticsLogWindow { Owner = _playerSettingsWindow ?? (Window)this };
        _logsWindow.Closed += (_, _) => _logsWindow = null;
        _logsWindow.Show();
    }

    void ISettingsHost.ApplyPlaybackSettings()
    {
        if (_player is { } player)
        {
            MpvConfig.ApplyPlaybackLive(player, _settings);
            Log.Info($"Postavke zvuka i slike primijenjene: hwdec={_settings.HardwareDecoding}, kvaliteta={_settings.VideoQuality}, " +
                     $"alang={_settings.AudioLanguage ?? "-"}, uređaj={_settings.OutputDevice(MpvConfig.MusicMode)}, kašnjenje={_settings.AudioDelayMs} ms, " +
                     $"ujednačavanje={_settings.NormalizeAudio}, HDR={_settings.HdrPassthrough}");
        }
    }

    void ISettingsHost.ApplyWindowSettings() => ApplyWindowSettings();

    void ISettingsHost.ApplyMiniPlayerSettings()
    {
        _mini?.ApplyDisplayStyle();
        RefreshVuFilter();
    }

    void ISettingsHost.ApplyHomeBackground() => _ = ApplyHomeBackgroundAsync();

    void ISettingsHost.ApplyHomeInfo(bool weatherChanged) => ApplyHomeInfo(weatherChanged);

    ErrorReporting ISettingsHost.Reports => Reports;

    Task<IReadOnlyList<JebaitedPlayer.Online.WeatherPlace>> ISettingsHost.SearchWeatherPlacesAsync(string name) => SearchWeatherPlacesAsync(name);

    private int _homeBackgroundVersion;

    /// <summary>Zadnje učitavanje pozadine (dijagnostika): putanja, trajanje i je li slika prikazana.</summary>
    internal (string? Path, long Milliseconds, bool Shown) LastHomeBackground { get; private set; }

    /// <summary>
    /// N4: vlastita slika pozadine početnog ekrana ili svemir. Slika se dekodira u širini zaslona i izvan UI dretve.
    /// Ako slike više nema (obrisana, premještena, isključen disk), vrijedi svemir uz obavijest, a postavka ostaje
    /// (disk se možda opet spoji).
    /// </summary>
    internal async Task ApplyHomeBackgroundAsync()
    {
        int version = ++_homeBackgroundVersion;
        string? path = _settings.HomeBackgroundImage;
        System.Windows.Media.Imaging.BitmapSource? image = null;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        if (path is { Length: > 0 })
        {
            double scale = System.Windows.Media.VisualTreeHelper.GetDpi(this).DpiScaleX;
            int width = (int)Math.Ceiling(Math.Max(SystemParameters.PrimaryScreenWidth, ActualWidth) * scale);
            image = await HomeBackground.LoadAsync(path, width);
            if (version != _homeBackgroundVersion)
            {
                return;
            }

            if (image is null)
            {
                _vm.Notice = Loc.T("home.backgroundMissing");
            }
        }

        HomeBackgroundImage.Source = image;
        HomeBackgroundImage.Stretch = HomeBackground.ToStretch(_settings.HomeBackgroundMode);
        bool custom = image is not null;

        // Sredina: piksel slike = piksel zaslona (inače WPF fotografiju od 72 dpi prikaže uvećano).
        bool center = custom && _settings.HomeBackgroundMode == "center";
        double dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this).DpiScaleX;
        HomeBackgroundImage.Width = center ? image!.PixelWidth / dpi : double.NaN;
        HomeBackgroundImage.Height = center ? image!.PixelHeight / dpi : double.NaN;
        if (center)
        {
            HomeBackgroundImage.Stretch = System.Windows.Media.Stretch.Uniform;
        }

        HomeBackgroundImage.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
        HomeBackgroundDim.Visibility = HomeBackgroundImage.Visibility;
        HomeSpace.Visibility = custom ? Visibility.Collapsed : Visibility.Visible;
        LastHomeBackground = (path, watch.ElapsedMilliseconds, custom);
        if (path is { Length: > 0 })
        {
            Log.Info($"Pozadina početnog ekrana: {(custom ? $"{image!.PixelWidth}×{image.PixelHeight}, {_settings.HomeBackgroundMode}" : "slika nije dostupna, svemir")} ({watch.ElapsedMilliseconds} ms)");
        }
    }

    /// <summary>M1b: uvezene postavke odmah na sve dijelove playera (isto kao promjene u prozoru postavki).</summary>
    void ISettingsHost.ApplyImportedSettings()
    {
        WindowTheme.Apply(_settings.Theme);
        WindowTheme.ApplyAccent(_settings.AccentColor);
        _vm.RefreshShortcuts();
        if (!_options.IsDiagnostics && _settings.Language is { Length: > 0 } language)
        {
            Loc.SetLanguage(language);
        }

        _vm.Volume = _settings.Volume;
        _vm.IsMuted = _settings.Muted;
        _vm.Library.AutoPlayNext = _settings.AutoPlayNext;
        _vm.Library.UpNextDelaySeconds = _settings.UpNextSeconds;
        _vm.Library.UpNextLeadSeconds = Math.Clamp(_settings.UpNextLeadSeconds, 0, 120);
        _vm.Library.ResumeMode = _settings.ResumeMode;
        _vm.Subtitles.ApplyLanguagePriority();
        _vm.Subtitles.ApplyStyle();
        ((ISettingsHost)this).ApplyPlaybackSettings();
        ApplyWindowSettings();
        ((ISettingsHost)this).ApplyMiniPlayerSettings();
        _mini?.BindEqualizer();
        _ = ApplyHomeBackgroundAsync();
        ApplyHomeInfo(weatherChanged: true);
        Log.Info("Uvezene postavke primijenjene.");
    }

    /// <summary>Prozor postavki se nakon uvoza ponovno otvara, da pokaže uvezene vrijednosti.</summary>
    void ISettingsHost.ReopenSettings(SettingsTab tab, string? snapshot)
    {
        if (_playerSettingsWindow is { } w)
        {
            w.Closed += (_, _) => Dispatcher.BeginInvoke(() => ShowPlayerSettings(tab, snapshot));
            w.Close();
        }
        else
        {
            ShowPlayerSettings(tab, snapshot);
        }
    }

    bool ISettingsHost.PersistSettings => !_options.IsDiagnostics;

    bool ISettingsHost.IsClosingPlayer => _closing;

    /// <summary>Izlazni uređaji zvuka: iz otvorenog mpv-a ili iz kratkotrajnog (bez videa, odmah se zatvara).</summary>
    async Task<IReadOnlyList<AudioDeviceInfo>> ISettingsHost.GetAudioDevicesAsync()
    {
        MpvPlayer? player = _player;
        string? json = await Task.Run(() =>
        {
            if (player != null)
            {
                return player.GetPropertyString("audio-device-list");
            }

            using var temp = new MpvPlayer(null);
            foreach (var (name, value) in new[]
                     {
                         ("vo", "null"), ("idle", "yes"), ("config", "no"), ("terminal", "no"), ("load-scripts", "no"),
                         ("ytdl", "no"), ("osc", "no"), ("input-default-bindings", "no"),
                     })
            {
                temp.TrySetOption(name, value);
            }

            temp.Initialize();
            return temp.GetPropertyString("audio-device-list");
        });
        IReadOnlyList<AudioDeviceInfo> devices = ParseAudioDevices(json);
        Log.Info($"Audio uređaji: {devices.Count}");
        return devices;
    }

    /// <summary>mpv "audio-device-list" (JSON) → uređaji; neispravan ili prazan odgovor = prazan popis.</summary>
    internal static IReadOnlyList<AudioDeviceInfo> ParseAudioDevices(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            return doc.RootElement.EnumerateArray()
                .Select(e => new AudioDeviceInfo(
                    e.TryGetProperty("name", out JsonElement n) ? n.GetString() ?? string.Empty : string.Empty,
                    e.TryGetProperty("description", out JsonElement d) ? d.GetString() ?? string.Empty : string.Empty))
                .Where(d => d.Name.Length > 0)
                .Select(d => d.Description.Length > 0 ? d : d with { Description = d.Name })
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }
}

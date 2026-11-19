using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.Services;
using JebaitedPlayer.Subtitles;
using JebaitedPlayer.ViewModels;

namespace JebaitedPlayer.Views;

/// <summary>Kartice postavki (UPGRADE 3), redom kako su u prozoru.</summary>
public enum SettingsTab
{
    General,
    Language,
    Subtitles,
    Audio,
    Video,
    Appearance,
    Updates,
    About,
}

/// <summary>Izlazni uređaj zvuka (mpv "audio-device-list").</summary>
public sealed record AudioDeviceInfo(string Name, string Description);

/// <summary>Što prozor postavki traži od glavnog prozora (otvaranje drugih prozora, primjena na mpv).</summary>
internal interface ISettingsHost
{
    void OpenSources();

    void OpenOnlineSetup();

    void RestartForUpdate();

    void ShowUpdateHistory();

    void ShowLogs();

    /// <summary>Postavke zvuka i slike odmah na video (ako je otvoren).</summary>
    void ApplyPlaybackSettings();

    /// <summary>Postavke prozora (npr. iznad ostalih prozora) odmah.</summary>
    void ApplyWindowSettings();

    /// <summary>Font i VU mini playera odmah (1.4.1).</summary>
    void ApplyMiniPlayerSettings();

    /// <summary>Uvezene postavke (M1b) odmah na cijeli player.</summary>
    void ApplyImportedSettings();

    /// <summary>Zatvori i ponovno otvori prozor postavki (prikaz uvezenih vrijednosti).</summary>
    /// <param name="snapshot">Stanje zadnjeg spremanja (radna kopija se nastavlja u novom prozoru).</param>
    void ReopenSettings(SettingsTab tab, string? snapshot = null);

    Task<IReadOnlyList<AudioDeviceInfo>> GetAudioDevicesAsync();

    /// <summary>Spremaju li se postavke pri zatvaranju (u dijagnostici ne: testovi nikad ne mijenjaju tvoje postavke).</summary>
    bool PersistSettings { get; }
}

/// <summary>
/// Postavke playera u karticama (UPGRADE 3): Općenito, Jezik, Titlovi, Zvuk, Slika, Izgled, Nadogradnje i O aplikaciji.
/// Svaka promjena vrijedi odmah; datoteka postavki se sprema pri zatvaranju prozora.
/// </summary>
public partial class PlayerSettingsWindow : Window
{
    /// <summary>Jezici zvuka koji se nude (kod jezika; naziv iz Languages/*.json).</summary>
    private static readonly string[] AudioLanguages = ["en", "de", "hr", "sr", "bs", "sl", "fr", "it", "es", "pt", "ru", "pl", "cs", "hu", "nl", "tr", "ja", "ko", "zh"];

    /// <summary>Tehnologije koje player koristi (vlastita imena, ne prevode se); licence su u THIRD_PARTY_NOTICES.md.</summary>
    private static readonly (string Name, string License)[] Technologies =
    [
        ("mpv / libmpv", "GPL-2.0-or-later"),
        ("FFmpeg, libass, libplacebo", "GPL / LGPL / ISC"),
        (".NET / WPF", "MIT"),
        ("Velopack", "MIT"),
        ("Microsoft.Data.Sqlite", "MIT"),
        ("SQLitePCLRaw", "Apache-2.0"),
        ("SQLite", "Public domain"),
        ("WebRtcVadSharp", "MIT"),
        ("WebRTC VAD", "BSD-3-Clause"),
    ];

    private readonly AppSettings _settings;
    private readonly PlayerViewModel _vm;
    private readonly UpdateService _updates;
    private readonly ISettingsHost _host;
    private readonly SubtitleSettingsPanel _subtitlesPanel;
    private IReadOnlyList<AudioDeviceInfo> _audioDevices = [];
    private bool _loading = true;

    internal PlayerSettingsWindow(AppSettings settings, PlayerViewModel vm, UpdateService updates, ISettingsHost host, SettingsTab tab,
        string? savedSnapshot = null)
    {
        _settings = settings;
        _vm = vm;
        _updates = updates;
        _host = host;
        InitializeComponent();
        WindowTheme.UseDarkTitleBar(this);
        MaxHeight = Math.Max(420, SystemParameters.WorkArea.Height - 24);
        Height = Math.Min(Height, MaxHeight);
        Width = Math.Min(Width, Math.Max(MinWidth, SystemParameters.WorkArea.Width - 24));

        _subtitlesPanel = new SubtitleSettingsPanel(settings, vm.Subtitles, host.OpenSources, host.OpenOnlineSetup);
        SubtitlesHost.Content = _subtitlesPanel;

        LoadGeneral();
        LoadAudio();
        LoadVideo();
        LoadAppearance();
        LoadUpdates();
        LoadAbout();
        SelectTab(tab);
        _loading = false;

        this.OnLanguageChanged(RefreshTexts);
        _savedSnapshot = savedSnapshot ?? SettingsWorkingCopy.Snapshot(_settings);
        _savedComparable = SettingsWorkingCopy.ComparableSnapshot(_savedSnapshot);
        _dirtyTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(400), DispatcherPriority.Background, (_, _) => UpdateSaveState(), Dispatcher);
        UpdateSaveState();
        Closed += (_, _) =>
        {
            _dirtyTimer.Stop();
            // Zatvaranje i dalje sprema (pitanje Spremi / Odbaci / Odustani dolazi s M3b); ponovno otvaranje ne sprema.
            if (_host.PersistSettings && !_reopening)
            {
                _settings.Save();
            }
        };
        Loaded += async (_, _) => await LoadAudioDevicesAsync();
    }

    /// <summary>Otvori zadanu karticu (npr. "Titlovi" iz izbornika titlova, "Nadogradnje" iz izbornika verzije).</summary>
    public void SelectTab(SettingsTab tab) => Tabs.SelectedIndex = (int)tab;

    /// <summary>Dijagnostika: naziv i broj otvorene kartice.</summary>
    internal SettingsTab SelectedTab => (SettingsTab)Math.Max(0, Tabs.SelectedIndex);

    private void RefreshTexts()
    {
        UpdateSeekStepTexts();
        UpdateAudioTexts();
        EqualizerPanel.Refresh();
        UpdateMiniTexts();
        UpdateAspectText();
        UpdateHideControlsText();
        UpdateUpdateStatus();
        UpdateShellStatus();
        UpdateBackupStatus();
        UpdateWhatsNew();
        AboutVersionText.Text = Loc.T("settings.aboutVersion", _updates.CurrentVersion);
        _subtitlesPanel.RefreshTexts();
    }

    // --- 1. Općenito ---------------------------------------------------------------------------------------------

    private void LoadGeneral()
    {
        (_settings.ResumeMode switch { "resume" => ResumeAuto, "start" => ResumeStart, _ => ResumeAsk }).IsChecked = true;
        ResumeAsk.Checked += (_, _) => SetResumeMode("ask");
        ResumeAuto.Checked += (_, _) => SetResumeMode("resume");
        ResumeStart.Checked += (_, _) => SetResumeMode("start");
        UpdateSeekStepTexts();

        OpenFullscreenBox.IsChecked = _settings.OpenFullscreen;
        OpenFullscreenBox.Checked += (_, _) => _settings.OpenFullscreen = true;
        OpenFullscreenBox.Unchecked += (_, _) => _settings.OpenFullscreen = false;
        OnTopBox.IsChecked = _settings.OnTopWhilePlaying;
        OnTopBox.Checked += (_, _) => SetOnTop(true);
        OnTopBox.Unchecked += (_, _) => SetOnTop(false);

        AutoNextBox.IsChecked = _settings.AutoPlayNext;
        UpNextSlider.Value = Math.Clamp(_settings.UpNextSeconds, 5, 30);
        UpNextSlider.IsEnabled = _settings.AutoPlayNext;
        AutoNextBox.Checked += (_, _) => SetAutoNext(true);
        AutoNextBox.Unchecked += (_, _) => SetAutoNext(false);
        UpNextSlider.ValueChanged += (_, e) =>
        {
            _settings.UpNextSeconds = (int)Math.Round(e.NewValue);
            _vm.Library.UpNextDelaySeconds = _settings.UpNextSeconds;
        };

        UpdateFolderText();
        UpdateShellStatus();
        UpdateBackupStatus();
    }

    private void SetResumeMode(string mode)
    {
        if (_loading)
        {
            return;
        }

        _settings.ResumeMode = mode;
        _vm.Library.ResumeMode = mode;
    }

    private void UpdateSeekStepTexts()
    {
        SeekStepButton.Content = Loc.T("settings.seconds", _settings.SeekStepSeconds);
        SeekStepHint.Text = Loc.T("settings.seekStepHint", _settings.SeekStepSeconds * 6);
    }

    private void OnSeekStepClick(object sender, RoutedEventArgs e) =>
        ShowChoices(SeekStepButton, AppSettings.SeekSteps.Select(s => (Loc.T("settings.seconds", s), s == _settings.SeekStepSeconds, (Action)(() =>
        {
            _settings.SeekStepSeconds = s;
            UpdateSeekStepTexts();
        }))));

    private void SetOnTop(bool on)
    {
        _settings.OnTopWhilePlaying = on;
        _host.ApplyWindowSettings();
    }

    private void SetAutoNext(bool on)
    {
        _settings.AutoPlayNext = on;
        _vm.Library.AutoPlayNext = on;
        UpNextSlider.IsEnabled = on;
    }

    private void UpdateFolderText() => ShotFolderText.Text = _vm.Extras.ScreenshotFolder;

    private void OnChooseFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = Loc.T("dialog.screenshotFolder"),
            InitialDirectory = Directory.Exists(_vm.Extras.ScreenshotFolder) ? _vm.Extras.ScreenshotFolder : null,
        };
        if (dialog.ShowDialog(this) == true)
        {
            _settings.ScreenshotFolder = dialog.FolderName;
            UpdateFolderText();
        }
    }

    private void OnDefaultFolder(object sender, RoutedEventArgs e)
    {
        _settings.ScreenshotFolder = null;
        UpdateFolderText();
    }

    private void OnOpenFolder(object sender, RoutedEventArgs e) => _vm.Extras.OpenScreenshotFolder();

    private void UpdateShellStatus()
    {
        if (AppPackage.IsStore)
        {
            // Store inačica: vrste datoteka pridružuje manifest paketa; zadani player bira se u Zadanim aplikacijama.
            ShellStatusText.Text = Loc.T("settings.shellStore");
            UnregisterButton.Visibility = Visibility.Collapsed;
            return;
        }

        string? exe = Environment.ProcessPath;
        string? registered = ShellIntegration.RegisteredExe();
        bool current = exe != null && ShellIntegration.IsRegisteredFor(exe);
        ShellStatusText.Text = registered is null
            ? Loc.T("settings.shellNotRegistered")
            : current
                ? Loc.T("settings.shellRegistered")
                : Loc.T("settings.shellOther", registered);
        UnregisterButton.Visibility = registered is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnSetDefaultPlayer(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!AppPackage.IsStore && Environment.ProcessPath is { } exe && !ShellIntegration.IsRegisteredFor(exe))
            {
                int types = ShellIntegration.Register(exe);
                Log.Info($"Registracija u Windowsu iz postavki: {types} vrsta datoteka ({exe})");
            }

            ShellIntegration.OpenDefaultAppsSettings();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            Log.Error("Registracija u Windowsu", ex);
            MessageBox.Show(this, Loc.T("error.registrationFailed", ex.Message), "jebaited_player", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        UpdateShellStatus();
    }

    private void OnUnregister(object sender, RoutedEventArgs e)
    {
        try
        {
            ShellIntegration.Unregister();
            Log.Info("Registracija u Windowsu uklonjena iz postavki.");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            Log.Error("Uklanjanje registracije", ex);
        }

        UpdateShellStatus();
    }

    // --- Ciklus 3: Spremi, Odbaci promjene (M2a) i Zadano (M2b) ----------------------------------------------------

    private string _savedSnapshot = string.Empty;
    private DispatcherTimer _dirtyTimer = null!;
    private bool _reopening;

    private string _savedComparable = string.Empty;

    internal bool IsDirty => SettingsWorkingCopy.Comparable(_settings) != _savedComparable;

    private void UpdateSaveState()
    {
        bool dirty = IsDirty;
        SaveButton.IsEnabled = dirty;
        DiscardButton.IsEnabled = dirty;
        SaveStatusText.Text = Loc.T(dirty ? "settings.unsaved" : "settings.allSaved");
    }

    /// <summary>Spremi: trajno na disk (u dijagnostici samo s testnom mapom podataka) i nova točka za „Odbaci promjene”.</summary>
    internal void SaveNow()
    {
        EqualizerPanel.Flush();
        if (_host.PersistSettings || Environment.GetEnvironmentVariable("JEBAITED_DATA_DIR") is { Length: > 0 })
        {
            _settings.Save();
        }

        _savedSnapshot = SettingsWorkingCopy.Snapshot(_settings);
        _savedComparable = SettingsWorkingCopy.ComparableSnapshot(_savedSnapshot);
        UpdateSaveState();
        Log.Info("Postavke spremljene (gumb Spremi).");
    }

    /// <summary>Odbaci promjene: vrati stanje zadnjeg spremanja i primijeni ga odmah.</summary>
    internal void DiscardChanges()
    {
        EqualizerPanel.Flush();
        SettingsWorkingCopy.Revert(_settings, _savedSnapshot);
        _host.ApplyImportedSettings();
        Log.Info("Promjene postavki odbačene.");
        Reopen(SelectedTab);
    }

    /// <summary>Zadano za karticu (ključ iz <see cref="SettingsDefaults.Tabs"/>) ili za sve (null).</summary>
    internal void ResetDefaults(string? tab, bool confirm)
    {
        string question = tab is null ? Loc.T("settings.defaultsConfirmAll") : Loc.T("settings.defaultsConfirmTab", TabTitle(SelectedTab));
        if (confirm && MessageBox.Show(this, question, Loc.T("settings.defaults"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        EqualizerPanel.Flush();
        SettingsDefaults.Reset(_settings, tab);
        _host.ApplyImportedSettings();
        Log.Info($"Zadane postavke: {tab ?? "sve"}.");
        Reopen(SelectedTab);
    }

    private void Reopen(SettingsTab tab)
    {
        _reopening = true;
        _host.ReopenSettings(tab, _savedSnapshot);
    }

    internal static string? DefaultsKey(SettingsTab tab) => tab switch
    {
        SettingsTab.General => "general",
        SettingsTab.Subtitles => "subtitles",
        SettingsTab.Audio => "audio",
        SettingsTab.Video => "video",
        SettingsTab.Appearance => "appearance",
        SettingsTab.Updates => "updates",
        _ => null,
    };

    private string TabTitle(SettingsTab tab) => (Tabs.Items[(int)tab] as TabItem)?.Header?.ToString() ?? tab.ToString();

    private void OnSave(object sender, RoutedEventArgs e) => SaveNow();

    private void OnDiscard(object sender, RoutedEventArgs e) => DiscardChanges();

    private void OnDefaults(object sender, RoutedEventArgs e)
    {
        string? key = DefaultsKey(SelectedTab);
        var menu = new ContextMenu { PlacementTarget = DefaultsButton, Placement = PlacementMode.Top };
        var thisTab = new MenuItem { Header = Loc.T("settings.defaultsThisTab", TabTitle(SelectedTab)), IsEnabled = key is not null };
        thisTab.Click += (_, _) => ResetDefaults(key, confirm: true);
        var all = new MenuItem { Header = Loc.T("settings.defaultsAll") };
        all.Click += (_, _) => ResetDefaults(null, confirm: true);
        menu.Items.Add(thisTab);
        menu.Items.Add(all);
        menu.IsOpen = true;
    }

    // --- 1a. Kopije, izvoz i uvoz postavki (ciklus 2, M1a i M1b) --------------------------------------------------

    private void UpdateBackupStatus()
    {
        BackupInfo? last = SettingsBackup.List(AppPaths.DataDir).FirstOrDefault();
        BackupStatusText.Text = last is null
            ? Loc.T("settings.backupNone", SettingsBackup.Keep)
            : Loc.T("settings.backupStatus", SettingsBackup.Keep, last.Created.ToLocalTime().ToString("g", Loc.Culture));
    }

    private void OnOpenBackups(object sender, RoutedEventArgs e)
    {
        string dir = SettingsBackup.BackupsDir(AppPaths.DataDir);
        try
        {
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Log.Warn($"Mapa s kopijama nije otvorena: {ex.Message}");
        }
    }

    private void OnExportSettings(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = SettingsTransfer.DefaultFileName(DateTime.Now),
            DefaultExt = ".json",
            Filter = Loc.T("settings.settingsFileFilter") + "|*.json",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dialog.ShowDialog(this) == true)
        {
            ExportTo(dialog.FileName);
        }
    }

    /// <summary>Izvoz u datoteku (gumb, dijagnostika). Vraća je li uspio.</summary>
    internal bool ExportTo(string path)
    {
        try
        {
            File.WriteAllText(path, SettingsTransfer.Export(_settings, _updates.CurrentVersion, DateTime.UtcNow));
            Log.Info("Postavke izvezene.");
            ShowTransferMessage(Loc.T("settings.exportDone", Path.GetFileName(path)), MessageBoxImage.Information);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Izvoz postavki nije uspio: {ex.Message}");
            ShowTransferMessage(Loc.T("settings.transferFailed", ex.Message), MessageBoxImage.Warning);
            return false;
        }
    }

    private void OnImportSettings(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = Loc.T("settings.settingsFileFilter") + "|*.json",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dialog.ShowDialog(this) == true)
        {
            ImportFrom(dialog.FileName, confirm: true);
        }
    }

    /// <summary>
    /// Uvoz: provjeri datoteku, pitaj, napravi kopiju trenutnih postavki, prepiši ih, primijeni odmah i ponovno
    /// otvori prozor. Neispravna datoteka ne mijenja ništa. Vraća je li uvezeno.
    /// </summary>
    internal bool ImportFrom(string path, bool confirm)
    {
        AppSettings imported;
        try
        {
            imported = SettingsTransfer.Read(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Uvoz postavki odbijen: {ex.Message}");
            ShowTransferMessage(Loc.T("settings.importInvalid"), MessageBoxImage.Warning);
            return false;
        }

        if (confirm && MessageBox.Show(this, Loc.T("settings.importConfirm", Path.GetFileName(path)), Loc.T("settings.importSettings"),
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return false;
        }

        if (_host.PersistSettings || Environment.GetEnvironmentVariable("JEBAITED_DATA_DIR") is { Length: > 0 })
        {
            try
            {
                _settings.Save();
                SettingsBackup.Create(AppPaths.DataDir, store: null, "import", DateTime.UtcNow);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warn($"Kopija prije uvoza nije napravljena: {ex.Message}");
            }
        }

        SettingsTransfer.ApplyTo(_settings, imported);
        _host.ApplyImportedSettings();
        Log.Info("Postavke uvezene.");
        Reopen(SettingsTab.General);
        return true;
    }

    /// <summary>Poruka o izvozu ili uvozu (u dijagnostici samo u log, bez prozora koji čeka klik).</summary>
    private void ShowTransferMessage(string text, MessageBoxImage icon)
    {
        if (_host.PersistSettings)
        {
            MessageBox.Show(this, text, Loc.T("settings.backupSection"), MessageBoxButton.OK, icon);
        }
        else
        {
            Log.Info("Dijagnostika, poruka: " + text);
        }
    }

    /// <summary>Pokrenuta je glazba umjesto filma ili obrnuto: ploča prikazuje equalizer koji sada svira.</summary>
    internal void OnMediaKindChanged() => (MpvConfig.MusicMode ? EqMusicRadio : EqMoviesRadio).IsChecked = true;

    // --- 6a. Mini player (1.4.1) ----------------------------------------------------------------------------------

    private void UpdateMiniTexts()
    {
        MiniFontButton.Content = Loc.T("miniFont." + _settings.MiniPlayer.Font);
        MiniVuButton.Content = Loc.T("vu." + _settings.MiniPlayer.VuStyle);
    }

    private void OnMiniFontClick(object sender, RoutedEventArgs e) =>
        ShowChoices(MiniFontButton, MiniPlayerSettings.Fonts.Select(f => (Loc.T("miniFont." + f.Id), f.Id == _settings.MiniPlayer.Font, (Action)(() =>
        {
            _settings.MiniPlayer.Font = f.Id;
            UpdateMiniTexts();
            _host.ApplyMiniPlayerSettings();
        }))));

    private void OnMiniVuClick(object sender, RoutedEventArgs e) =>
        ShowChoices(MiniVuButton, MiniPlayerSettings.VuStyles.Select(v => (Loc.T("vu." + v), v == _settings.MiniPlayer.VuStyle, (Action)(() =>
        {
            _settings.MiniPlayer.VuStyle = v;
            UpdateMiniTexts();
            _host.ApplyMiniPlayerSettings();
        }))));

    // --- 4. Zvuk -------------------------------------------------------------------------------------------------

    private void LoadAudio()
    {
        VolumeSlider.Value = _vm.Volume;
        VolumeSlider.ValueChanged += (_, e) =>
        {
            _vm.Volume = e.NewValue;
            _settings.Volume = _vm.Volume;
        };
        NormalizeBox.IsChecked = _settings.NormalizeAudio;
        NormalizeBox.Checked += (_, _) => ChangePlayback(() => _settings.NormalizeAudio = true);
        NormalizeBox.Unchecked += (_, _) => ChangePlayback(() => _settings.NormalizeAudio = false);
        AudioDelaySlider.Value = _settings.AudioDelayMs;
        AudioDelaySlider.ValueChanged += (_, e) => ChangePlayback(() =>
        {
            _settings.AudioDelayMs = Math.Round(e.NewValue / 50) * 50;
            UpdateAudioTexts();
        });
        UpdateAudioTexts();
        EqualizerPanel.Initialize(MpvConfig.MusicMode ? _settings.MusicEqualizer : _settings.Equalizer, _host.ApplyPlaybackSettings);
        (MpvConfig.MusicMode ? EqMusicRadio : EqMoviesRadio).IsChecked = true;
        EqMoviesRadio.Checked += (_, _) => EqualizerPanel.Bind(_settings.Equalizer);
        EqMusicRadio.Checked += (_, _) => EqualizerPanel.Bind(_settings.MusicEqualizer);
        Closed += (_, _) => EqualizerPanel.Flush();
    }

    private void UpdateAudioTexts()
    {
        AudioLanguageButton.Content = _settings.AudioLanguage is { Length: > 0 } lang
            ? SubtitleLanguage.DisplayName(lang)
            : Loc.T("settings.audioLanguageDefault");
        AudioDelayText.Text = Loc.T("settings.milliseconds", _settings.AudioDelayMs.ToString("+0;−0;0", Loc.Culture));
        AudioDeviceButton.Content = _settings.AudioDevice == "auto"
            ? Loc.T("settings.audioDeviceDefault")
            : _audioDevices.FirstOrDefault(d => d.Name == _settings.AudioDevice)?.Description ?? _settings.AudioDevice;
    }

    private async Task LoadAudioDevicesAsync()
    {
        try
        {
            _audioDevices = await _host.GetAudioDevicesAsync();
        }
        catch (Exception ex)
        {
            Log.Warn($"Popis audio uređaja nije dostupan: {ex.Message}");
        }

        UpdateAudioTexts();
    }

    private void OnAudioLanguageClick(object sender, RoutedEventArgs e)
    {
        var choices = new List<(string, bool, Action)>
        {
            (Loc.T("settings.audioLanguageDefault"), string.IsNullOrEmpty(_settings.AudioLanguage), () => SetAudioLanguage(null)),
        };
        choices.AddRange(AudioLanguages.Select(code => (SubtitleLanguage.DisplayName(code), code == _settings.AudioLanguage, (Action)(() => SetAudioLanguage(code)))));
        ShowChoices(AudioLanguageButton, choices);
    }

    private void SetAudioLanguage(string? code) => ChangePlayback(() =>
    {
        _settings.AudioLanguage = code;
        UpdateAudioTexts();
    });

    private void OnAudioDeviceClick(object sender, RoutedEventArgs e)
    {
        var choices = new List<(string, bool, Action)>
        {
            (Loc.T("settings.audioDeviceDefault"), _settings.AudioDevice == "auto", () => SetAudioDevice("auto")),
        };
        choices.AddRange(_audioDevices.Where(d => d.Name != "auto")
            .Select(d => (d.Description, d.Name == _settings.AudioDevice, (Action)(() => SetAudioDevice(d.Name)))));
        ShowChoices(AudioDeviceButton, choices);
    }

    private void SetAudioDevice(string name) => ChangePlayback(() =>
    {
        _settings.AudioDevice = name;
        UpdateAudioTexts();
    });

    private void OnResetAudioDelay(object sender, RoutedEventArgs e) => AudioDelaySlider.Value = 0;

    // --- 5. Slika ------------------------------------------------------------------------------------------------

    private void LoadVideo()
    {
        (_settings.VideoQuality switch { "high" => QualityHigh, "fast" => QualityFast, _ => QualityBalanced }).IsChecked = true;
        QualityBalanced.Checked += (_, _) => ChangePlayback(() => _settings.VideoQuality = "balanced");
        QualityHigh.Checked += (_, _) => ChangePlayback(() => _settings.VideoQuality = "high");
        QualityFast.Checked += (_, _) => ChangePlayback(() => _settings.VideoQuality = "fast");
        HwdecBox.IsChecked = _settings.HardwareDecoding;
        HwdecBox.Checked += (_, _) => ChangePlayback(() => _settings.HardwareDecoding = true);
        HwdecBox.Unchecked += (_, _) => ChangePlayback(() => _settings.HardwareDecoding = false);
        HdrBox.IsChecked = _settings.HdrPassthrough;
        HdrBox.Checked += (_, _) => ChangePlayback(() => _settings.HdrPassthrough = true);
        HdrBox.Unchecked += (_, _) => ChangePlayback(() => _settings.HdrPassthrough = false);
        UpdateAspectText();
    }

    private void UpdateAspectText() =>
        AspectButton.Content = PlaybackExtrasController.AspectModes.FirstOrDefault(m => m.Value == _settings.DefaultAspect).NameKey is { } key
            ? Loc.T(key)
            : _settings.DefaultAspect;

    private void OnAspectClick(object sender, RoutedEventArgs e) =>
        ShowChoices(AspectButton, PlaybackExtrasController.AspectModes.Select(m => (Loc.T(m.NameKey), m.Value == _settings.DefaultAspect, (Action)(() =>
        {
            _settings.DefaultAspect = m.Value;
            UpdateAspectText();
            _vm.Extras.ApplyAspect(m.Value, announce: false);
        }))));

    /// <summary>Promjena zvuka ili slike: zapamti i odmah primijeni na otvoreni video.</summary>
    private void ChangePlayback(Action change)
    {
        if (_loading)
        {
            return;
        }

        change();
        _host.ApplyPlaybackSettings();
    }

    // --- 6. Izgled -----------------------------------------------------------------------------------------------

    private void LoadAppearance()
    {
        (WindowTheme.IsDark ? ThemeDark : ThemeLight).IsChecked = true;
        ThemeDark.Checked += (_, _) => SetTheme("dark");
        ThemeLight.Checked += (_, _) => SetTheme("light");
        Action syncTheme = () => (WindowTheme.IsDark ? ThemeDark : ThemeLight).IsChecked = true;
        WindowTheme.Changed += syncTheme;
        Closed += (_, _) => WindowTheme.Changed -= syncTheme;
        UpdateHideControlsText();

        MiniAutoSwitchBox.IsChecked = _settings.MiniPlayer.AutoSwitch;
        MiniAutoSwitchBox.Checked += (_, _) => _settings.MiniPlayer.AutoSwitch = true;
        MiniAutoSwitchBox.Unchecked += (_, _) => _settings.MiniPlayer.AutoSwitch = false;
        UpdateMiniTexts();

        GpuUiBox.IsChecked = _settings.UiGpuRendering;
        GpuUiBox.Checked += (_, _) => _settings.UiGpuRendering = true;
        GpuUiBox.Unchecked += (_, _) => _settings.UiGpuRendering = false;
    }

    private void SetTheme(string theme)
    {
        if (_loading || theme == WindowTheme.Current)
        {
            return;
        }

        _settings.Theme = theme;
        WindowTheme.Apply(theme);
    }

    private void UpdateHideControlsText() => HideControlsButton.Content = Loc.T("settings.seconds", _settings.ControlsHideSeconds);

    private void OnHideControlsClick(object sender, RoutedEventArgs e) =>
        ShowChoices(HideControlsButton, AppSettings.HideDelays.Select(d => (Loc.T("settings.seconds", d), Math.Abs(d - _settings.ControlsHideSeconds) < 0.01, (Action)(() =>
        {
            _settings.ControlsHideSeconds = d;
            UpdateHideControlsText();
        }))));

    // --- 7. Nadogradnje ------------------------------------------------------------------------------------------

    private void LoadUpdates()
    {
        AutoUpdateBox.IsChecked = _settings.AutoUpdate;
        AutoUpdateBox.Checked += (_, _) => _settings.AutoUpdate = true;
        AutoUpdateBox.Unchecked += (_, _) => _settings.AutoUpdate = false;
        EventHandler onUpdate = (_, _) => Dispatcher.BeginInvoke(UpdateUpdateStatus);
        _updates.Changed += onUpdate;
        Closed += (_, _) => _updates.Changed -= onUpdate;
        UpdateUpdateStatus();
        UpdateWhatsNew();
    }

    private void UpdateUpdateStatus()
    {
        VersionText.Text = $"jebaited_player {_updates.CurrentVersion}{(_updates.IsPortable ? " " + Loc.T("settings.portable") : string.Empty)}";
        UpdateStatusText.Text = _updates.Status;
        bool installed = _updates.IsInstalled;
        UpdatesHintText.Visibility = _updates.IsStore ? Visibility.Collapsed : Visibility.Visible;
        UpdatesStoreHintText.Visibility = _updates.IsStore ? Visibility.Visible : Visibility.Collapsed;
        AutoUpdateBox.Visibility = installed ? Visibility.Visible : Visibility.Collapsed;
        UpdateButtons.Visibility = installed ? Visibility.Visible : Visibility.Collapsed;
        CheckUpdateButton.IsEnabled = _updates.State is not (UpdateState.Checking or UpdateState.Downloading);
        RestartUpdateButton.Visibility = _updates.State == UpdateState.Ready ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>"Što je novo": bilješke instalirane verzije iz ugrađene povijesti nadogradnji.</summary>
    private void UpdateWhatsNew()
    {
        ReleaseEntry? entry = UpdateHistory.Find(_updates.CurrentVersion) ?? UpdateHistory.Versions.FirstOrDefault();
        WhatsNewList.Children.Clear();
        if (entry is null)
        {
            WhatsNewTitle.Visibility = WhatsNewDate.Visibility = Visibility.Collapsed;
            return;
        }

        WhatsNewTitle.Text = Loc.T("settings.whatsNew", entry.Version);
        WhatsNewDate.Text = UpdateHistory.FormatDate(entry);
        ReleaseNotesText notes = entry.NotesFor(Loc.Current);
        foreach (string item in notes.Features.Concat(notes.Improvements))
        {
            var row = new DockPanel { Margin = new Thickness(2, 3, 0, 3) };
            var bullet = new TextBlock { Text = "•", Margin = new Thickness(0, 0, 8, 0), Foreground = (Brush)FindResource("AccentBrush") };
            DockPanel.SetDock(bullet, Dock.Left);
            row.Children.Add(bullet);
            row.Children.Add(new TextBlock { Text = item, TextWrapping = TextWrapping.Wrap });
            WhatsNewList.Children.Add(row);
        }
    }

    private async void OnCheckUpdate(object sender, RoutedEventArgs e)
    {
        await _updates.CheckAndDownloadAsync();
        UpdateUpdateStatus();
    }

    private void OnRestartUpdate(object sender, RoutedEventArgs e)
    {
        Close();
        _host.RestartForUpdate();
    }

    private void OnViewHistory(object sender, RoutedEventArgs e) => _host.ShowUpdateHistory();

    private void OnViewLogs(object sender, RoutedEventArgs e) => _host.ShowLogs();

    private void OnGitHubReleases(object sender, RoutedEventArgs e) => OpenUrl(UpdateHistory.Repository + "/releases");

    // --- 8. O aplikaciji -----------------------------------------------------------------------------------------

    private void LoadAbout()
    {
        AboutVersionText.Text = Loc.T("settings.aboutVersion", _updates.CurrentVersion);
        foreach (var (name, license) in Technologies)
        {
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            var text = new TextBlock { Text = name, MinWidth = 260 };
            DockPanel.SetDock(text, Dock.Left);
            row.Children.Add(text);
            row.Children.Add(new TextBlock { Text = license, Foreground = (Brush)FindResource("TextSecondaryBrush") });
            TechnologyList.Children.Add(row);
        }
    }

    private void OnLicense(object sender, RoutedEventArgs e) => OpenUrl(UpdateHistory.Repository + "/blob/main/LICENSE");

    private void OnSourceCode(object sender, RoutedEventArgs e) => OpenUrl(UpdateHistory.Repository);

    private void OnThirdParty(object sender, RoutedEventArgs e) => OpenUrl(UpdateHistory.Repository + "/blob/main/THIRD_PARTY_NOTICES.md");

    private void OnPrivacy(object sender, RoutedEventArgs e) => OpenUrl(UpdateHistory.Repository + "/blob/main/PRIVACY.md");

    private void OnCodeSigning(object sender, RoutedEventArgs e) => OpenUrl(UpdateHistory.Repository + "/blob/main/CODE_SIGNING.md");

    /// <summary>Priručnik na jeziku sučelja (njemački, hrvatski za hr/bs/sr, inače engleski).</summary>
    private void OnManual(object sender, RoutedEventArgs e)
    {
        string file = Loc.Current switch
        {
            "de" => "README_DE.md",
            "hr" or "bs" or "sr" => "README_HR.md",
            _ => "README_EN.md",
        };
        OpenUrl($"{UpdateHistory.Repository}/blob/main/{file}");
    }

    // --- Zajedničko ----------------------------------------------------------------------------------------------

    /// <summary>Popis mogućnosti ispod gumba (umjesto ComboBoxa, u boji teme); kvačica uz odabranu.</summary>
    private static void ShowChoices(Button button, IEnumerable<(string Text, bool IsChecked, Action Choose)> choices)
    {
        var menu = new ContextMenu { PlacementTarget = button, Placement = PlacementMode.Bottom };
        foreach (var (text, isChecked, choose) in choices)
        {
            var item = new MenuItem { Header = text.Replace("_", "__", StringComparison.Ordinal), IsChecked = isChecked };
            item.Click += (_, _) => choose();
            menu.Items.Add(item);
        }

        menu.IsOpen = true;
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Warn($"Poveznica se nije otvorila: {ex.Message}");
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}

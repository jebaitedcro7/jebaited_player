using System.Windows;
using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.Services;
using JebaitedPlayer.ViewModels;

namespace JebaitedPlayer.Views;

/// <summary>Opće postavke playera: tema, sljedeća epizoda, mapa snimki, poveznice na postavke titlova.</summary>
public partial class PlayerSettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly PlayerViewModel _vm;
    private readonly Action _openSubtitleSettings;
    private readonly Action _openSources;
    private readonly Action _openOnlineSetup;
    private readonly UpdateService _updates;
    private readonly Action _restartForUpdate;

    public PlayerSettingsWindow(AppSettings settings, PlayerViewModel vm, Action openSubtitleSettings, Action openSources,
        Action openOnlineSetup, UpdateService updates, Action restartForUpdate)
    {
        _settings = settings;
        _vm = vm;
        _openSubtitleSettings = openSubtitleSettings;
        _openSources = openSources;
        _openOnlineSetup = openOnlineSetup;
        _updates = updates;
        _restartForUpdate = restartForUpdate;
        InitializeComponent();
        WindowTheme.UseDarkTitleBar(this);
        MaxHeight = Math.Max(400, SystemParameters.WorkArea.Height - 24);

        LightThemeBox.IsChecked = !WindowTheme.IsDark;
        AutoNextBox.IsChecked = settings.AutoPlayNext;
        UpNextSlider.Value = Math.Clamp(settings.UpNextSeconds, 5, 30);
        UpNextSlider.IsEnabled = settings.AutoPlayNext;
        GpuUiBox.IsChecked = settings.UiGpuRendering;
        UpdateFolderText();
        UpdateShellStatus();
        AutoUpdateBox.IsChecked = settings.AutoUpdate;
        AutoUpdateBox.Checked += (_, _) => _settings.AutoUpdate = true;
        AutoUpdateBox.Unchecked += (_, _) => _settings.AutoUpdate = false;
        EventHandler onUpdate = (_, _) => Dispatcher.BeginInvoke(UpdateUpdateStatus);
        updates.Changed += onUpdate;
        Closed += (_, _) => updates.Changed -= onUpdate;
        UpdateUpdateStatus();
        this.OnLanguageChanged(() =>
        {
            UpdateUpdateStatus();
            UpdateShellStatus();
        });

        LightThemeBox.Checked += (_, _) => SetTheme("light");
        LightThemeBox.Unchecked += (_, _) => SetTheme("dark");
        Action syncTheme = () => LightThemeBox.IsChecked = !WindowTheme.IsDark;
        WindowTheme.Changed += syncTheme;
        Closed += (_, _) => WindowTheme.Changed -= syncTheme;
        AutoNextBox.Checked += (_, _) => SetAutoNext(true);
        AutoNextBox.Unchecked += (_, _) => SetAutoNext(false);
        UpNextSlider.ValueChanged += (_, e) =>
        {
            _settings.UpNextSeconds = (int)Math.Round(e.NewValue);
            _vm.Library.UpNextDelaySeconds = _settings.UpNextSeconds;
        };
        GpuUiBox.Checked += (_, _) => _settings.UiGpuRendering = true;
        GpuUiBox.Unchecked += (_, _) => _settings.UiGpuRendering = false;
        Closed += (_, _) => _settings.Save();
    }

    private void SetTheme(string theme)
    {
        if (theme == WindowTheme.Current)
        {
            return;
        }

        _settings.Theme = theme;
        WindowTheme.Apply(theme);
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

    private void UpdateUpdateStatus()
    {
        VersionText.Text = $"jebaited_player {_updates.CurrentVersion}{(_updates.IsPortable ? " " + Loc.T("settings.portable") : string.Empty)}";
        UpdateStatusText.Text = _updates.Status;
        bool installed = _updates.IsInstalled;
        AutoUpdateBox.Visibility = installed ? Visibility.Visible : Visibility.Collapsed;
        UpdateButtons.Visibility = installed ? Visibility.Visible : Visibility.Collapsed;
        CheckUpdateButton.IsEnabled = _updates.State is not (UpdateState.Checking or UpdateState.Downloading);
        RestartUpdateButton.Visibility = _updates.State == UpdateState.Ready ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void OnCheckUpdate(object sender, RoutedEventArgs e)
    {
        await _updates.CheckAndDownloadAsync();
        UpdateUpdateStatus();
    }

    private void OnRestartUpdate(object sender, RoutedEventArgs e)
    {
        Close();
        _restartForUpdate();
    }

    private void UpdateShellStatus()
    {
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
            if (Environment.ProcessPath is { } exe && !ShellIntegration.IsRegisteredFor(exe))
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

    private void OnSubtitleSettings(object sender, RoutedEventArgs e) => _openSubtitleSettings();

    private void OnSources(object sender, RoutedEventArgs e) => _openSources();

    private void OnOnlineSetup(object sender, RoutedEventArgs e) => _openOnlineSetup();

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}

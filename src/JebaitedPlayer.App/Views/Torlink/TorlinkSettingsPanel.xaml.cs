#if TORLINK
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.Services;
using JebaitedPlayer.Torlink;

namespace JebaitedPlayer.Views.Torlink;

/// <summary>
/// Postavke → Torlink. "Uključi" je radnja (prihvat upozorenja, preuzimanje i provjera runtimea) i sprema se odmah; ostale opcije
/// su obične postavke (Spremi / Odbaci kao drugdje) i primjenjuju se pri sljedećem pokretanju Torlinka (po zatvaranju postavki).
/// </summary>
public partial class TorlinkSettingsPanel : UserControl
{
    private readonly AppSettings _settings;
    private readonly TorlinkService _svc;
    private readonly Func<bool> _saveNow;
    private bool _loading = true;
    private bool _busy;
    private UpdateInfo? _info;
    private Func<string>? _result;

    internal TorlinkSettingsPanel(AppSettings settings, TorlinkService svc, Func<bool> saveNow)
    {
        _settings = settings;
        _svc = svc;
        _saveNow = saveNow;
        InitializeComponent();
        LoadValues();
        Refresh();
        _loading = false;

        Action onLang = () => Dispatcher.BeginInvoke(Refresh);
        Loc.LanguageChanged += onLang;
        Action onHost = () => Dispatcher.BeginInvoke(Refresh);
        _svc.Host.Changed += onHost;
        Unloaded += (_, _) =>
        {
            Loc.LanguageChanged -= onLang;
            _svc.Host.Changed -= onHost;
        };
    }

    /// <summary>Odgovor za testove (bez prozora): true = isključi iako su preuzimanja aktivna.</summary>
    internal static Func<bool>? ConfirmDisableOverride { get; set; }

    private TorlinkSettings T => _settings.Torlink;

    // ---- pregled za testove ----
    internal bool EnabledForTests => EnableSwitch.IsChecked == true;
    internal string StateTextForTests => StateText.Text;
    internal string EnableMessageForTests => EnableMessage.Visibility == Visibility.Visible ? EnableMessage.Text : string.Empty;
    internal string UpdateStatusForTests => UpdateStatus.Text;
    internal bool UpdateButtonVisibleForTests => UpdateButton.Visibility == Visibility.Visible;
    internal bool RollbackButtonVisibleForTests => RollbackButton.Visibility == Visibility.Visible;
    internal bool BodyEnabledForTests => Body.IsEnabled;
    internal Task ClickEnableForTestsAsync() => EnableToggleAsync();
    internal Task ClickCheckForTestsAsync() => CheckAsync();
    internal Task ClickUpdateForTestsAsync() => UpdateAsync();
    internal Task ClickRollbackForTestsAsync() => RollbackAsync();

    private void LoadValues()
    {
        FolderBox.Text = T.DownloadDir;
        MaxBox.Text = T.MaxDownloads.ToString(CultureInfo.InvariantCulture);
        PlaylistBox.IsChecked = T.Playlist;
        TrackersBox.Text = string.Join(Environment.NewLine, T.Trackers);
        SeedBox.IsChecked = T.SeedAfter;
        SeedHoursBox.Text = T.SeedHours.ToString(CultureInfo.InvariantCulture);
        InterfaceBox.Text = T.Interface;
        PortBox.Text = T.PeerPort.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Tekstovi i stanja koji ovise o uključenosti, instalaciji, jeziku i zadnjoj provjeri.</summary>
    private void Refresh()
    {
        bool on = T.Enabled && _svc.Host.Installed is not null;
        EnableSwitch.IsChecked = T.Enabled;
        Body.IsEnabled = T.Enabled && !_busy;
        Body.Opacity = T.Enabled ? 1 : 0.45;
        StateText.Text = Loc.T(_svc.Host.Status switch
        {
            TorlinkStatus.Disabled => "torlink.set.stateOff",
            TorlinkStatus.NotInstalled => "torlink.set.stateNotInstalled",
            TorlinkStatus.Starting => "torlink.set.stateStarting",
            TorlinkStatus.Running => "torlink.set.stateRunning",
            TorlinkStatus.Failed => "torlink.set.stateFailed",
            _ => "torlink.set.stateReady",
        });

        RuntimeManifest? m = _svc.Host.Installed;
        IntegratedText.Text = m is null ? Loc.T("torlink.set.notInstalled") : m.Display;
        LatestText.Text = _info?.Upstream is { } u ? u.Version : Loc.T("torlink.set.unknown");
        IntegrationText.Text = Loc.T(_svc.Host.Status switch
        {
            TorlinkStatus.Failed => "torlink.set.integrationProblem",
            TorlinkStatus.NotInstalled => "torlink.set.integrationNotInstalled",
            TorlinkStatus.Disabled => "torlink.set.integrationOff",
            _ => "torlink.set.integrationOk",
        });
        LastCheckText.Text = Format(T.LastUpdateCheck);
        LastUpdateText.Text = Format(T.LastUpdate);
        ServicesText.Text = _svc.Host.LastSources.Count == 0
            ? Loc.T("torlink.set.servicesNone")
            : string.Join(", ", _svc.Host.LastSources.GroupBy(s => s.Label).Select(g => g.Any(s => s.Ok) ? $"{g.Key} ✔" : $"{g.Key} ✖"));
        SeedHoursPanel.Visibility = T.SeedAfter ? Visibility.Visible : Visibility.Collapsed;
        RollbackButton.Visibility = on && _svc.Host.Store.HasPrevious ? Visibility.Visible : Visibility.Collapsed;
        if (_svc.Host.Store.PreviousManifest() is { } prev)
        {
            RollbackButton.Content = Loc.T("torlink.set.rollback", prev.TorlinkVersion);
        }

        UpdateStatus.Text = DescribeUpdate();
    }

    private static string Format(string iso) =>
        DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime t)
            ? t.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)
            : Loc.T("torlink.set.never");

    private string DescribeUpdate()
    {
        if (_info is null)
        {
            if (_result is not null)
            {
                return _result();
            }

            return _svc.Host.Installed is null ? Loc.T("torlink.set.notInstalled") : Loc.T("torlink.set.checkPrompt");
        }

        if (_info.Error == "both")
        {
            return Loc.T("torlink.set.checkFailed");
        }

        if (_info.Available && _info.Runtime is { } r)
        {
            return Loc.T("torlink.set.available", r.Version, _info.Installed?.Display ?? Loc.T("torlink.set.notInstalled"));
        }

        if (_info.UpstreamAheadOfRuntime && _info.Upstream is { } up)
        {
            return Loc.T("torlink.set.upstreamAhead", up.Version);
        }

        return Loc.T("torlink.set.upToDate", _info.Installed?.TorlinkVersion ?? "-");
    }

    // ---- uključi / isključi ----

    private async void OnEnableClick(object sender, RoutedEventArgs e) => await EnableToggleAsync();

    private async Task EnableToggleAsync()
    {
        if (_busy)
        {
            return;
        }

        bool wantOn = !T.Enabled;
        EnableSwitch.IsChecked = T.Enabled; // vraća se dok radnja ne uspije
        if (wantOn)
        {
            await TurnOnAsync();
        }
        else
        {
            await TurnOffAsync();
        }

        Refresh();
    }

    private async Task TurnOnAsync()
    {
        if (!T.ConsentAccepted)
        {
            if (!TorlinkConsentWindow.Ask(Window.GetWindow(this)))
            {
                return;
            }

            T.ConsentAccepted = true;
        }

        _busy = true;
        EnableSwitch.IsEnabled = false;
        EnableMessage.Visibility = Visibility.Collapsed;
        InstallPanel.Visibility = Visibility.Visible;
        InstallText.Text = Loc.T("torlink.set.installing");
        InstallBar.Percent = 0;
        try
        {
            var progress = Throttled(p =>
            {
                InstallText.Text = StepText(p.Step);
                if (p.Step is UpdateStep.Download or UpdateStep.Verify)
                {
                    InstallBar.Percent = p.Fraction * 100;
                }
            });
            TorlinkEnableResult r = await _svc.EnableAsync(progress);
            if (r == TorlinkEnableResult.Ok)
            {
                _saveNow();
                _info = null;
                return;
            }

            ShowEnableMessage(Loc.T(r switch
            {
                TorlinkEnableResult.NoRuntimePublished => "torlink.set.enableNoRuntime",
                TorlinkEnableResult.NetworkError => "torlink.set.enableNetwork",
                _ => "torlink.set.enableFailed",
            }));
        }
        finally
        {
            _busy = false;
            EnableSwitch.IsEnabled = true;
            InstallPanel.Visibility = Visibility.Collapsed;
        }
    }

    private async Task TurnOffAsync()
    {
        int active = _svc.Host.Snapshot.DownloadingCount;
        if (active > 0)
        {
            bool confirmed = ConfirmDisableOverride?.Invoke() ?? MessageBox.Show(Window.GetWindow(this), Loc.T("torlink.disableText", active),
                Loc.T("torlink.disableTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
            if (!confirmed)
            {
                return;
            }
        }

        _busy = true;
        try
        {
            await _svc.DisableAsync();
            _saveNow();
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>Napredak stiže po datoteci (tisuće poruka): prosljeđuje se samo promjena koraka ili pomak od bar 1 %.</summary>
    private IProgress<UpdateProgress> Throttled(Action<UpdateProgress> apply)
    {
        UpdateStep? lastStep = null;
        double lastFraction = -1;
        return new Progress<UpdateProgress>(p =>
        {
            if (p.Step == lastStep && Math.Abs(p.Fraction - lastFraction) < 0.01 && p.Fraction < 1)
            {
                return;
            }

            (lastStep, lastFraction) = (p.Step, p.Fraction);
            apply(p);
        });
    }

    private void ShowEnableMessage(string text)
    {
        EnableMessage.Text = text;
        EnableMessage.Visibility = Visibility.Visible;
        EnableMessage.SetResourceReference(TextBlock.ForegroundProperty, "ErrorBrush");
    }

    private static string StepText(UpdateStep s) => Loc.T(s switch
    {
        UpdateStep.Download => "torlink.step.download",
        UpdateStep.Verify => "torlink.step.verify",
        UpdateStep.Backup => "torlink.step.backup",
        UpdateStep.Replace => "torlink.step.replace",
        UpdateStep.HealthCheck => "torlink.step.health",
        UpdateStep.RolledBack => "torlink.step.rolledBack",
        UpdateStep.Done => "torlink.step.done",
        _ => "torlink.step.check",
    });

    // ---- ažuriranje ----

    private async void OnCheckClick(object sender, RoutedEventArgs e) => await CheckAsync();

    private async Task CheckAsync()
    {
        CheckButton.IsEnabled = false;
        _result = null;
        UpdateStatus.Text = Loc.T("torlink.step.check");
        try
        {
            _info = await _svc.CheckForUpdateAsync();
            _saveNow();
            UpdateButton.Visibility = _info.Available && _info.Runtime is not null ? Visibility.Visible : Visibility.Collapsed;
            if (_info.Runtime is { } r)
            {
                UpdateButton.Content = Loc.T("torlink.set.install", r.Version);
            }

            string notes = _info.Runtime?.Notes is { Length: > 0 } n ? n : _info.Upstream?.Notes ?? string.Empty;
            UpdateNotes.Text = notes.Length > 0 ? Loc.T("torlink.set.notes") + " " + (notes.Length > 500 ? notes[..500] + "…" : notes) : string.Empty;
            UpdateNotes.Visibility = notes.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        finally
        {
            CheckButton.IsEnabled = true;
            Refresh();
        }
    }

    private async void OnUpdateClick(object sender, RoutedEventArgs e) => await UpdateAsync();

    private async Task UpdateAsync()
    {
        if (_info?.Runtime is not { } release || _busy)
        {
            return;
        }

        _busy = true;
        CheckButton.IsEnabled = UpdateButton.IsEnabled = RollbackButton.IsEnabled = false;
        UpdateBar.Visibility = UpdateStepText.Visibility = Visibility.Visible;
        UpdateBar.Percent = 0;
        try
        {
            var progress = Throttled(p =>
            {
                UpdateStepText.Text = StepText(p.Step);
                if (p.Step is UpdateStep.Download or UpdateStep.Verify)
                {
                    UpdateBar.Percent = p.Fraction * 100;
                }
            });
            UpdateResult r = await _svc.InstallUpdateAsync(release, progress);
            _saveNow();
            _result = () => r.Success
                ? Loc.T("torlink.set.result.ok", release.Version)
                : Loc.T(r.RolledBack ? "torlink.set.result.rolledBack" : "torlink.set.result.failed", r.Detail ?? r.Step.ToString());
            UpdateStatus.Text = _result();
            _info = null;
            UpdateButton.Visibility = Visibility.Collapsed;
        }
        finally
        {
            _busy = false;
            CheckButton.IsEnabled = UpdateButton.IsEnabled = RollbackButton.IsEnabled = true;
            UpdateBar.Visibility = UpdateStepText.Visibility = Visibility.Collapsed;
            string keep = UpdateStatus.Text;
            Refresh();
            if (_info is null)
            {
                UpdateStatus.Text = keep;
            }
        }
    }

    private async void OnRollbackClick(object sender, RoutedEventArgs e) => await RollbackAsync();

    private async Task RollbackAsync()
    {
        RollbackButton.IsEnabled = false;
        try
        {
            string? prev = _svc.Host.Store.PreviousManifest()?.TorlinkVersion;
            bool ok = await _svc.Host.RollbackAsync();
            _result = () => ok ? Loc.T("torlink.set.result.rolledBackManual", prev ?? "-") : Loc.T("torlink.set.result.failed", "rollback");
            UpdateStatus.Text = _result();
            _info = null;
        }
        finally
        {
            RollbackButton.IsEnabled = true;
            string keep = UpdateStatus.Text;
            Refresh();
            UpdateStatus.Text = keep;
        }
    }

    private void OnOpenLicenses(object sender, RoutedEventArgs e)
    {
        string dir = Path.Combine(new TorlinkPaths(AppPaths.DataDir).RuntimeDir, "licenses");
        if (Directory.Exists(dir))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = false });
        }
    }

    // ---- opcije (sprema Spremi / Odbaci kao i ostale postavke) ----

    private void OnChangeFolder(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { InitialDirectory = T.ToOptions().EffectiveDownloadDir };
        if (dlg.ShowDialog(Window.GetWindow(this)) == true)
        {
            FolderBox.Text = dlg.FolderName;
            OnFolderLostFocus(sender, e);
        }
    }

    private void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        string dir = T.ToOptions().EffectiveDownloadDir;
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = false });
    }

    private void OnFolderLostFocus(object sender, RoutedEventArgs e)
    {
        if (!_loading)
        {
            T.DownloadDir = FolderBox.Text.Trim();
        }
    }

    private void OnMaxLostFocus(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        T.MaxDownloads = int.TryParse(MaxBox.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? Math.Clamp(n, 0, 20) : T.MaxDownloads;
        MaxBox.Text = T.MaxDownloads.ToString(CultureInfo.InvariantCulture);
    }

    private void OnPlaylistChanged(object sender, RoutedEventArgs e)
    {
        if (!_loading)
        {
            T.Playlist = PlaylistBox.IsChecked == true;
        }
    }

    private void OnTrackersLostFocus(object sender, RoutedEventArgs e)
    {
        if (!_loading)
        {
            T.Trackers = TrackersBox.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        }
    }

    private void OnSeedChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        T.SeedAfter = SeedBox.IsChecked == true;
        SeedHoursPanel.Visibility = T.SeedAfter ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnSeedHoursLostFocus(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        T.SeedHours = int.TryParse(SeedHoursBox.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? Math.Clamp(n, 0, 24 * 365) : T.SeedHours;
        SeedHoursBox.Text = T.SeedHours.ToString(CultureInfo.InvariantCulture);
    }

    private void OnInterfaceLostFocus(object sender, RoutedEventArgs e)
    {
        if (!_loading)
        {
            T.Interface = InterfaceBox.Text.Trim();
        }
    }

    private void OnPortLostFocus(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        T.PeerPort = int.TryParse(PortBox.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n is >= 0 and <= 65535 ? n : T.PeerPort;
        PortBox.Text = T.PeerPort.ToString(CultureInfo.InvariantCulture);
    }

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        T.ResetOptions();
        _loading = true;
        LoadValues();
        _loading = false;
        Refresh();
    }

    /// <summary>Nakon spremanja/zatvaranja postavki: ponovno pokretanje Torlinka ako se promijenilo nešto što čita pri pokretanju.</summary>
    internal Task ApplyOptionsAsync() => _svc.ApplyOptionsAsync();
}
#endif

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.Subtitles;
using JebaitedPlayer.ViewModels;
using JebaitedPlayer.Views;

namespace JebaitedPlayer;

/// <summary>Izbornici (desni klik, gumb titlova) i prozori za titlove.</summary>
public partial class MainWindow
{
    private SubtitleSyncWindow? _syncWindow;

    private void ShowContextMenu()
    {
        var menu = new ContextMenu { PlacementTarget = this, Placement = PlacementMode.MousePoint };
        menu.Items.Add(Item(Loc.T("menu.openFile"), "Ctrl+O", ShowOpenDialog));
        menu.Items.Add(Item(Loc.T("menu.openFolder"), "Ctrl+Shift+O", ShowOpenFolderDialog));
        menu.Items.Add(Item(Loc.T("menu.playPause"), Loc.T("key.space"), _vm.TogglePause, _vm.HasMedia));
        menu.Items.Add(Item(Loc.T("menu.fullscreen"), "F", () => SetFullscreen(!_vm.IsFullscreen)));
        if (_vm.HasMedia)
        {
            menu.Items.Add(Item(Loc.T("menu.goHome"), null, GoHome));
        }

        menu.Items.Add(new Separator());
        if (_vm.HasMedia && _vm.Library.HasPlaylist)
        {
            menu.Items.Add(Item(Loc.T("menu.playlist"), "L", () => SetPlaylistPanel(PlaylistPanel.Visibility != Visibility.Visible)));
            menu.Items.Add(Item(Loc.T("menu.nextEpisode"), "N", _vm.Library.PlayNext));
        }

        menu.Items.Add(Item(_vm.Library.IsCurrentFavorite ? Loc.T("home.removeFavorite") : Loc.T("menu.addFavorite"), "Ctrl+D",
            _vm.Library.ToggleFavorite, _vm.HasMedia));
        menu.Items.Add(new Separator());

        var audio = new HoverIntentMenuItem { Header = Loc.T("menu.audio"), IsEnabled = _vm.HasMedia && _vm.Extras.AudioTracks.Count > 0 };
        AddAudioItems(audio.Items);
        menu.Items.Add(audio);

        var subtitles = new HoverIntentMenuItem { Header = Loc.T("controls.subtitles"), IsEnabled = _vm.HasMedia };
        AddSubtitleItems(subtitles.Items);
        menu.Items.Add(subtitles);

        var speed = new HoverIntentMenuItem { Header = Loc.T("menu.speed", _vm.Extras.SpeedText), IsEnabled = _vm.HasMedia };
        AddSpeedItems(speed.Items);
        menu.Items.Add(speed);

        var aspect = new HoverIntentMenuItem { Header = Loc.T("menu.aspect", _vm.Extras.AspectName), IsEnabled = _vm.HasMedia && !_vm.IsAudioOnly };
        AddAspectItems(aspect.Items);
        menu.Items.Add(aspect);

        bool canShoot = _vm.HasMedia && !_vm.IsAudioOnly;
        var shot = new HoverIntentMenuItem { Header = Loc.T("menu.screenshot") };
        shot.Items.Add(Item(Loc.T("menu.screenshotWithSubs"), "S", () => _ = _vm.Extras.TakeScreenshotAsync(withSubtitles: true), canShoot));
        shot.Items.Add(Item(Loc.T("menu.screenshotPictureOnly"), "Shift+S", () => _ = _vm.Extras.TakeScreenshotAsync(withSubtitles: false), canShoot));
        shot.Items.Add(new Separator());
        shot.Items.Add(Item(Loc.T("menu.openScreenshotFolder"), null, _vm.Extras.OpenScreenshotFolder));
        menu.Items.Add(shot);

        menu.Items.Add(new Separator());
        menu.Items.Add(Item(Loc.T("menu.settings"), null, ShowPlayerSettings));
        menu.Items.Add(Item(Loc.T("menu.shortcuts"), "F1", ShowShortcuts, _vm.HasMedia));
        menu.Items.Add(Item(Loc.T("menu.exit"), "Alt+F4", Close));
        OpenMenu(menu);
    }

    private void ShowItemsMenu(FrameworkElement placementTarget, Action<ItemCollection> fill)
    {
        var menu = new ContextMenu { PlacementTarget = placementTarget, Placement = PlacementMode.Top };
        fill(menu.Items);
        OpenMenu(menu);
    }

    private void AddAudioItems(ItemCollection items)
    {
        PlaybackExtrasController extras = _vm.Extras;
        foreach (AudioTrackInfo track in extras.AudioTracks)
        {
            AudioTrackInfo t = track;
            items.Add(Item(t.DisplayName, null, () => extras.SelectAudio(t), isChecked: t.IsSelected));
        }

        if (extras.AudioTracks.Count > 1)
        {
            items.Add(new Separator());
            items.Add(Item(Loc.T("menu.nextAudio"), "B", extras.CycleAudio));
        }

        items.Add(new Separator());
        items.Add(Item(Loc.T("menu.equalizer"), null, () => ShowPlayerSettings(SettingsTab.Audio), isChecked: _settings.Equalizer.Enabled));
    }

    private void AddSpeedItems(ItemCollection items)
    {
        PlaybackExtrasController extras = _vm.Extras;
        foreach (double s in PlaybackExtrasController.SpeedSteps)
        {
            double speed = s;
            string name = Math.Abs(speed - 1) < 1e-9 ? Loc.T("menu.speedNormal") : $"{speed.ToString("0.##", Loc.Culture)}×";
            items.Add(Item(name, Math.Abs(speed - 1) < 1e-9 ? "Backspace" : null, () => extras.SetSpeed(speed),
                isChecked: Math.Abs(extras.Speed - speed) < 1e-6));
        }

        items.Add(new Separator());
        items.Add(Item(Loc.T("menu.faster"), "+", () => extras.StepSpeed(+1), extras.Speed < 2 - 1e-6));
        items.Add(Item(Loc.T("menu.slower"), "−", () => extras.StepSpeed(-1), extras.Speed > 0.5 + 1e-6));
    }

    private void AddAspectItems(ItemCollection items)
    {
        PlaybackExtrasController extras = _vm.Extras;
        foreach (var (value, nameKey) in PlaybackExtrasController.AspectModes)
        {
            string v = value;
            items.Add(Item(Loc.T(nameKey), null, () => extras.SetAspect(v), isChecked: extras.AspectMode == v));
        }

        items.Add(new Separator());
        items.Add(Item(Loc.T("menu.nextAspect"), "O", extras.CycleAspect));
    }

    private void ShowSubtitleMenu(FrameworkElement placementTarget)
    {
        var menu = new ContextMenu { PlacementTarget = placementTarget, Placement = PlacementMode.Top };
        AddSubtitleItems(menu.Items);
        OpenMenu(menu);
    }

    private void AddSubtitleItems(ItemCollection items)
    {
        SubtitleController subs = _vm.Subtitles;
        SubtitleTrackInfo? selected = subs.SelectedTrack;

        items.Add(Item(Loc.T("menu.noSubtitles"), null, () => subs.SelectTrack(null), isChecked: selected is null));
        foreach (SubtitleTrackInfo track in subs.Tracks)
        {
            SubtitleTrackInfo t = track;
            items.Add(Item(t.MenuName(subs.VideoPath), null, () => subs.SelectTrack(t), isChecked: t.IsSelected));
        }

        items.Add(new Separator());
        items.Add(Item(Loc.T("menu.searchOnline"), "Ctrl+F", ShowSearchWindow));
        items.Add(Item(Loc.T("menu.loadFromFile"), "Ctrl+T", ShowLoadSubtitlesDialog));

        if (selected?.Prepared is { IsBitmap: false } prepared)
        {
            var encoding = new HoverIntentMenuItem { Header = Loc.T("menu.encoding", prepared.EncodingName) };
            encoding.Items.Add(Item(Loc.T("menu.encodingAuto"), null, () => subs.ChangeEncoding(selected, null)));
            foreach (var (codePage, name) in SubtitleEncoding.ManualChoices)
            {
                int cp = codePage;
                encoding.Items.Add(Item(name, null, () => subs.ChangeEncoding(selected, cp), isChecked: prepared.CodePage == cp));
            }

            items.Add(encoding);
        }

        items.Add(Item(_autoSync.IsBusy ? Loc.T("menu.syncBusy") : Loc.T("menu.sync"), "A", _autoSync.SyncSelected,
            selected?.Prepared is { IsBitmap: false } && !_autoSync.IsBusy));
        items.Add(Item(Loc.T("menu.syncWindow"), null, ShowSyncWindow, selected != null));
        items.Add(Item(Loc.T("menu.resetSync"), "Ctrl+Backspace", subs.ResetSync, subs.HasCorrection));
        items.Add(Item(subs.IsVisible ? Loc.T("menu.hideSubtitles") : Loc.T("menu.showSubtitles"), "V", subs.ToggleVisibility));
        items.Add(new Separator());
        items.Add(Item(Loc.T("settings.subtitleSettings"), null, ShowSubtitleSettings));
        items.Add(Item(Loc.T("menu.onlineSetup"), null, () => ShowOnlineSetup()));
        items.Add(Item(Loc.T("settings.sources"), null, ShowSourcesWindow));
    }

    private OnlineSetupWindow? _onlineSetupWindow;

    /// <summary>Vodič za titlove s interneta (registracija, prijava, provjera, spremanje).</summary>
    private void ShowOnlineSetup(int startPage = 0)
    {
        if (_onlineSetupWindow is { IsLoaded: true })
        {
            _onlineSetupWindow.Activate();
            return;
        }

        Window owner = _searchWindow is { IsLoaded: true } ? _searchWindow : this;
        _onlineSetupWindow = new OnlineSetupWindow(_settings, _online, startPage) { Owner = owner };
        _onlineSetupWindow.Closed += (_, _) =>
        {
            _onlineSetupWindow = null;
            UpdateOnlineSetupButton();
        };
        _onlineSetupWindow.Show();
    }

    /// <summary>Gumb na početnom ekranu dok korisnik nema nijedan svoj račun ili ključ.</summary>
    private void UpdateOnlineSetupButton() =>
        OnlineSetupButton.Visibility = OnlineSetup.HasPersonalSetup(_settings.Online) ? Visibility.Collapsed : Visibility.Visible;

    private void OnOnlineSetupClick(object sender, RoutedEventArgs e) => ShowOnlineSetup();

    /// <summary>
    /// Prvo pokretanje bez datoteke (npr. odmah nakon instalacije): vodič se ponudi jednom.
    /// Ako je player otvoren dvoklikom na video, ne prekida se gledanje (ostaje gumb i izbornik).
    /// </summary>
    private void OfferOnlineSetupOnFirstRun()
    {
        if (_options.IsDiagnostics || _settings.OnlineWizardOffered || OnlineSetup.HasPersonalSetup(_settings.Online))
        {
            return;
        }

        _settings.OnlineWizardOffered = true;
        _settings.Save();
        if (_options.Files.Count == 0 && !_vm.HasMedia)
        {
            Dispatcher.BeginInvoke(() => ShowOnlineSetup(), DispatcherPriority.ApplicationIdle);
        }
    }

    private SubtitleSearchWindow? _searchWindow;
    private SubtitleSourcesWindow? _sourcesWindow;

    private void ShowSearchWindow()
    {
        if (!_vm.HasMedia)
        {
            Notify(Loc.T("notice.openVideoBeforeSearch"));
            return;
        }

        if (_searchWindow is { IsLoaded: true })
        {
            _searchWindow.Activate();
            return;
        }

        _searchWindow = new SubtitleSearchWindow(_online, ShowSourcesWindow) { Owner = this };
        _searchWindow.Closed += (_, _) =>
        {
            _searchWindow = null;
            Activate();
        };
        _searchWindow.Show();
    }

    private void ShowSourcesWindow()
    {
        if (_sourcesWindow is { IsLoaded: true })
        {
            _sourcesWindow.Activate();
            return;
        }

        Window owner = _searchWindow is { IsLoaded: true } ? _searchWindow : this;
        _sourcesWindow = new SubtitleSourcesWindow(_settings, _online) { Owner = owner };
        _sourcesWindow.Closed += (_, _) => _sourcesWindow = null;
        _sourcesWindow.ShowDialog();
    }

    /// <summary>Dijagnostika: lažni izvor s gotovim rezultatima (bez mreže i ključeva).</summary>
    private IReadOnlyList<JebaitedPlayer.Online.ISubtitleProvider>? FakeOnlineProviders(string? mode)
    {
        if (mode is null)
        {
            return null;
        }

        if (mode == "m5")
        {
            // M5: prvi (više preuzimanja) je za drugi film, drugi je pravi ali kasni 2,5 s. Datoteke su uz testni video.
            JebaitedPlayer.Online.SubtitleCandidate[] m5 =
            [
                new() { Provider = "Test", DownloadReference = "sync_krivi_film.srt", Language = "hr", ReleaseName = "Sync.Film.2026.WEB", Downloads = 5000 },
                new() { Provider = "Test", DownloadReference = "sync_kasni_2500.srt", Language = "hr", ReleaseName = "Sync.Film.2026.WEB", Downloads = 10 },
            ];
            return
            [
                new JebaitedPlayer.Online.CannedSubtitleProvider("Test", m5, c =>
                {
                    string dir = Path.GetDirectoryName(_vm.CurrentPath) ?? ".";
                    return new JebaitedPlayer.Online.DownloadedSubtitle(c.DownloadReference, File.ReadAllBytes(Path.Combine(dir, c.DownloadReference)));
                }),
            ];
        }

        const string text = "1\r\n00:00:01,000 --> 00:00:20,000\r\nPreuzeto s interneta: čćžšđ ČĆŽŠĐ\r\n";
        JebaitedPlayer.Online.SubtitleCandidate[] candidates = mode switch
        {
            "sure" =>
            [
                new() { Provider = "Test", DownloadReference = "1", Language = "hr", ReleaseName = "Online.Test.2026.1080p.WEB-DL.x264-GRP", HashMatch = true, Downloads = 10 },
                new() { Provider = "Test", DownloadReference = "3", Language = "en", ReleaseName = "Online.Test.2026.1080p.WEB-DL.x264-GRP", HashMatch = true },
            ],
            "unsure" =>
            [
                new() { Provider = "Test", DownloadReference = "2", Language = "hr", ReleaseName = "Online.Test.2026.720p.HDTV.x264-OTHER" },
            ],
            _ => [],
        };
        return
        [
            new JebaitedPlayer.Online.CannedSubtitleProvider("Test", candidates,
                _ => new JebaitedPlayer.Online.DownloadedSubtitle("Online.Test.hr.srt", SubtitleEncoding.Get(1250).GetBytes(text))),
        ];
    }

    private void OpenMenu(ContextMenu menu)
    {
        menu.Closed += (_, _) =>
        {
            _contextMenuOpen = false;
            RestoreKeyboardFocus();
        };
        _contextMenuOpen = true;
        menu.IsOpen = true;
        UpdateOverlayVisibility();
    }

    private static MenuItem Item(string header, string? gesture, Action action, bool enabled = true, bool isChecked = false)
    {
        // Dugi nazivi (npr. puno ime datoteke titla) skraćuju se s "…", a puni naziv je u oblačiću: preširok izbornik ne stane
        // pokraj glavnog, pa ga Windows prebaci preko njega i "desno" više nema ničega.
        object content = header.Length <= 60
            ? header.Replace("_", "__", StringComparison.Ordinal) // "_" bi inače bio pristupna tipka (nestao bi iz naziva)
            : new TextBlock { Text = header, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 420 };
        var item = new HoverIntentMenuItem
        {
            Header = content,
            InputGestureText = gesture ?? string.Empty,
            IsEnabled = enabled,
            IsChecked = isChecked,
            ToolTip = header.Length > 60 ? header : null,
        };
        item.Click += (_, _) => action();
        return item;
    }

    private void ShowLoadSubtitlesDialog()
    {
        if (!_vm.HasMedia)
        {
            Notify(Loc.T("notice.openVideoBeforeLoad"));
            return;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Loc.T("dialog.loadSubtitles"),
            Multiselect = true,
            Filter = MediaFormats.SubtitleDialogFilter,
            InitialDirectory = _vm.Subtitles.VideoPath is { } video ? Path.GetDirectoryName(video) : null,
        };

        if (dialog.ShowDialog(this) == true)
        {
            _vm.Subtitles.LoadFiles(dialog.FileNames);
        }
    }

    private void ShowSyncWindow()
    {
        if (_syncWindow is { IsLoaded: true })
        {
            _syncWindow.Activate();
            return;
        }

        _syncWindow = new SubtitleSyncWindow(_vm.Subtitles, _autoSync, HandleKey) { Owner = this };
        _syncWindow.Closed += (_, _) =>
        {
            _syncWindow = null;
            Activate();
        };
        _syncWindow.Show();
    }
}

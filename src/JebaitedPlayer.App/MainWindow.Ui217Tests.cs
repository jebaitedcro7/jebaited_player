using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using JebaitedPlayer.Controls;
using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.Views;

namespace JebaitedPlayer;

/// <summary>
/// Dijagnostički test 2.1.7 u pravom playeru (<c>--diag-ui-test ui217</c>, testni album od tri pjesme):
/// kotačić glasnoće, položaj prozora uz taskbar, Minimize, LIVE, svih pet izgleda mini playera, beskonačni naslov i podaci o pjesmi,
/// Discord u izborniku izvora, Home bez prekida glazbe s kontrolama reprodukcije, izgled vremena i prognoze.
/// </summary>
public partial class MainWindow
{
    [DllImport("user32.dll")]
    private static extern void mouse_event(uint flags, int dx, int dy, int data, nuint extra);

    [DllImport("user32.dll")]
    private static extern nint WindowFromPoint(Interop.NativeMethods.POINT p);

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint hwnd, uint flags);

    private static void RaiseWheel(UIElement target, int delta) =>
        target.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta)
        {
            RoutedEvent = UIElement.PreviewMouseWheelEvent,
            Source = target,
        });

    private static bool Inside(Window w)
    {
        Rect work = WindowPlacement.WorkAreaFor(w);
        return w.Left >= work.Left - 0.5 && w.Top >= work.Top - 0.5 &&
               w.Left + w.ActualWidth <= work.Right + 0.5 && w.Top + w.ActualHeight <= work.Bottom + 0.5;
    }

    private async Task<Dictionary<string, object?>> Ui217UiTestAsync()
    {
        var r = new Dictionary<string, object?>();
        await WaitForAsync(() => _vm.HasMedia && !_vm.IsLoading && _vm.Position > 1 && _vm.IsAudioOnly, 12000);
        r["aoVolumeStart"] = MpvString("ao-volume");
        _player?.SetPropertyAsync("ao-volume", "100"); // Windows pamti glasnoću sesije po aplikaciji; test je ne smije ostaviti niskom
        _player?.SetPropertyAsync("mute", "yes");
        var loads = new List<string>();
        _vm.LoadStarting += (_, path) => loads.Add(path);

        // --- 9) Home ne prekida glazbu, kontrole reprodukcije na početnom ekranu ------------------------------------
        double p0 = _vm.Position;
        GoHome();
        await Task.Delay(1800);
        r["home"] = new Dictionary<string, object?>
        {
            ["homeView"] = _homeView, ["welcomeVisible"] = WelcomePanel.IsVisible, ["hasMedia"] = _vm.HasMedia, ["paused"] = _vm.IsPaused,
            ["advanced"] = Math.Round(_vm.Position - p0, 2), ["bar"] = HomePlaybackBar.IsVisible, ["track"] = HomeTrackText.Text,
            ["source"] = HomeSourceText.Text, ["idle"] = MpvString("idle-active"), ["loads"] = loads.Count,
            ["overlayVisible"] = _overlayVisible, ["audioPanel"] = AudioPanel.IsVisible, ["volumeVisible"] = HomeVolumeArea.IsVisible,
            ["screenshot"] = SaveWindowPng("home_playing"),
        };

        ClickButton(HomePlayPauseButton);
        await Task.Delay(500);
        bool pausedByHome = _vm.IsPaused;
        string glyphPaused = (string)HomePlayPauseButton.Content;
        ClickButton(HomePlayPauseButton);
        await Task.Delay(700);
        _vm.Volume = 50;
        double before = _vm.Volume;
        RaiseWheel(HomeVolumeArea, 120);
        double up = _vm.Volume;
        RaiseWheel(HomeVolumeArea, -120);
        r["homeControls"] = new Dictionary<string, object?>
        {
            ["pausedByHome"] = pausedByHome, ["resumed"] = !_vm.IsPaused, ["glyphPaused"] = glyphPaused, ["glyphPlaying"] = (string)HomePlayPauseButton.Content,
            ["volumeStep"] = Math.Round(up - before, 1), ["volumeBack"] = _vm.Volume == before,
        };

        ClickButton(HomeNextButton);
        await WaitForAsync(() => _vm.Library.Current is { } c && _vm.Library.Playlist.IndexOf(c) == 1 && _vm.Duration > 0 && !_vm.IsLoading, 8000);
        await Task.Delay(1500);
        r["homeNext"] = new Dictionary<string, object?>
        {
            ["current"] = _vm.Library.Current?.Label, ["homeView"] = _homeView, ["track"] = HomeTrackText.Text, ["welcomeVisible"] = WelcomePanel.IsVisible,
            ["paused"] = _vm.IsPaused,
        };
        ClickButton(HomePrevButton);
        await WaitForAsync(() => _vm.Library.Current is { } c && _vm.Library.Playlist.IndexOf(c) == 0 && _vm.Duration > 0 && !_vm.IsLoading, 8000);
        await Task.Delay(800);

        // Prijelazi Home → mini → puni player: isti mpv, glazba ne staje, nema novog učitavanja.
        int loadsBeforeSwitch = loads.Count;
        double s0 = _vm.Position;
        ShowMiniPlayer();
        await Task.Delay(1500);
        MiniPlayerWindow mini = _mini!;
        double s1 = _vm.Position;
        ((IMiniPlayerHost)this).ShowFullPlayer();
        await Task.Delay(1500);
        double s2 = _vm.Position;
        r["switches"] = new Dictionary<string, object?>
        {
            ["toMiniAdvanced"] = Math.Round(s1 - s0, 2), ["backAdvanced"] = Math.Round(s2 - s1, 2), ["extraLoads"] = loads.Count - loadsBeforeSwitch,
            ["homeViewAfter"] = _homeView, ["paused"] = _vm.IsPaused,
        };

        // Nova pjesma otvara prikaz playera (Home je bio samo prekrivač).
        OpenFiles([System.IO.Path.Combine(System.IO.Path.GetDirectoryName(_vm.CurrentPath!)!, "02 Druga.flac")]);
        await WaitForAsync(() => _vm.HasMedia && !_vm.IsLoading && _vm.Duration > 0 && _vm.Library.Current is { } c && _vm.Library.Playlist.IndexOf(c) == 1 && !_homeView, 8000);
        await Task.Delay(500);
        r["afterLoad"] = new Dictionary<string, object?> { ["homeView"] = _homeView, ["welcomeVisible"] = WelcomePanel.IsVisible, ["audioPanel"] = AudioPanel.IsVisible };

        // --- 1) Kotačić glasnoće ------------------------------------------------------------------------------------------
        ShowMiniPlayer();
        await Task.Delay(800);
        _vm.Volume = 50;
        RaiseWheel(mini.VolumeSlider, 120);
        double miniUp = _vm.Volume;
        RaiseWheel(mini.VolumeSlider, -120);
        RaiseWheel(mini.VolumeSlider, -120);
        double miniDown = _vm.Volume;
        RaiseWheel(mini.Marquee, 120);
        RaiseWheel(mini.TimeText, 120);
        double wheelElsewhere = _vm.Volume;
        // 2.3.15: traka glasnoće prati kotačić ODMAH (bez čekanja na sat od 250 ms), i kad kotačić nije iznad same trake.
        double sliderAfterWheel = mini.VolumeSlider.Value;
        _vm.Volume = 99;
        for (int i = 0; i < 4; i++)
        {
            RaiseWheel(mini.VolumeSlider, 120);
        }

        double clampedTop = _vm.Volume;
        _vm.Volume = 1;
        for (int i = 0; i < 4; i++)
        {
            RaiseWheel(mini.VolumeSlider, -120);
        }

        double clampedBottom = _vm.Volume;

        // Pravi kotačić miša iznad trake glasnoće mini playera.
        _vm.Volume = 40;
        await Task.Delay(300);
        Point center = mini.VolumeSlider.PointToScreen(new Point(mini.VolumeSlider.ActualWidth / 2, mini.VolumeSlider.ActualHeight / 2));
        double realUp = _vm.Volume;
        int attempts = 0;
        for (int attempt = 0; attempt < 4 && realUp == 40; attempt++)
        {
            // Pravi miš: kursor dođe na traku, pa tek onda kotačić (Windows ga šalje prozoru ispod kursora).
            Interop.NativeMethods.SetCursorPos((int)center.X, (int)center.Y);
            await Task.Delay(400);
            mouse_event(0x0800, 0, 0, 120, 0);
            await Task.Delay(400);
            realUp = _vm.Volume;
            attempts++;
        }

        nint under = WindowFromPoint(new Interop.NativeMethods.POINT { X = (int)center.X, Y = (int)center.Y });
        r["realWheelDiag"] = new Dictionary<string, object?>
        {
            ["attempts"] = attempts, ["sliderMouseOver"] = mini.VolumeSlider.IsMouseOver, ["miniActive"] = mini.IsActive,
            ["underIsMini"] = GetAncestor(under, 2) == new System.Windows.Interop.WindowInteropHelper(mini).Handle, ["miniTopmost"] = mini.Topmost,
        };
        Point elsewhere = mini.Marquee.PointToScreen(new Point(20, 5));
        Interop.NativeMethods.SetCursorPos((int)elsewhere.X, (int)elsewhere.Y);
        await Task.Delay(250);
        mouse_event(0x0800, 0, 0, 120, 0);
        await Task.Delay(300);
        double realElsewhere = _vm.Volume;

        // Puni player: samo traka glasnoće u kontrolama; video, naslov i vremenska traka ne mijenjaju glasnoću.
        ((IMiniPlayerHost)this).ShowFullPlayer();
        await Task.Delay(800);
        _vm.Volume = 50;
        RegisterActivity();
        await Task.Delay(300);
        double overlayBefore = _vm.Volume;
        RaiseWheel(_overlay!.VolumeArea, 120);
        double overlayUp = _vm.Volume;
        RaiseWheel(_overlay.Timeline, 120);
        double overlayTimeline = _vm.Volume;
        r["wheel"] = new Dictionary<string, object?>
        {
            ["step"] = Math.Round(miniUp - 50, 1), ["miniDown"] = miniDown, ["elsewhere"] = wheelElsewhere, ["clampedTop"] = clampedTop,
            ["clampedBottom"] = clampedBottom, ["realUp"] = realUp, ["realElsewhere"] = realElsewhere,
            ["overlayStep"] = Math.Round(overlayUp - overlayBefore, 1), ["overlayTimeline"] = overlayTimeline,
            ["sliderAfterWheel"] = Math.Round(sliderAfterWheel, 1),
        };

        // --- 6) Svih pet izgleda mini playera ----------------------------------------------------------------------------
        ShowMiniPlayer();
        await Task.Delay(600);
        var designs = new Dictionary<string, object?>();
        foreach (string id in MiniPlayerSettings.Designs)
        {
            mini.SetDesign(id);
            await Task.Delay(900);
            MiniSkinState st = mini.LastSkinState;
            bool playingBefore = !_vm.IsPaused;
            string title = id is "classic" or "strip" ? mini.MarqueeTextValue : st.Title;
            var entry = new Dictionary<string, object?>
            {
                ["active"] = mini.ActiveDesign, ["saved"] = _settings.MiniPlayer.ActiveDesign, ["skin"] = mini.CurrentSkin?.GetType().Name,
                ["size"] = new[] { Math.Round(mini.ActualWidth), Math.Round(mini.ActualHeight) }, ["title"] = title, ["time"] = id is "classic" ? mini.TimeText.Text : st.Time,
                ["source"] = id is "classic" ? mini.SourceText.Text : st.Source, ["playing"] = playingBefore, ["inside"] = Inside(mini),
                ["screenshot"] = SaveWindowPng("mini_design_" + id, mini),
            };

            // Iste radnje u svakom izgledu: Play/Pause, Sljedeća/Prethodna i glasnoća.
            if (mini.CurrentSkin is { } skin)
            {
                ClickButton(skin.PlayPauseButton);
                await Task.Delay(400);
                entry["pausedByButton"] = _vm.IsPaused;
                ClickButton(skin.PlayPauseButton);
                await Task.Delay(500);
                entry["resumedByButton"] = !_vm.IsPaused;
                _vm.Volume = 30;
                await Task.Delay(400);
                mini.Tick();
                entry["volumeSliderFollows"] = Math.Abs(skin.VolumeBar.Value - 30) < 1;
                RaiseWheel(skin.VolumeBar, 120);
                entry["wheelStep"] = Math.Round(_vm.Volume - 30, 1);
                entry["hasLive"] = skin.LiveMark is not null;
                entry["hasEq"] = skin.EqButton is not null;
                entry["hasDiscord"] = skin.DiscordButton is not null;
            }
            else if (id == "classic")
            {
                ClickButton(mini.PauseButton);
                await Task.Delay(400);
                entry["pausedByButton"] = _vm.IsPaused;
                ClickButton(mini.PlayButton);
                await Task.Delay(500);
                entry["resumedByButton"] = !_vm.IsPaused;
                _vm.Volume = 30;
                await Task.Delay(400);
                mini.Tick();
                entry["volumeSliderFollows"] = Math.Abs(mini.VolumeSlider.Value - 30) < 1;
                RaiseWheel(mini.VolumeSlider, 120);
                entry["wheelStep"] = Math.Round(_vm.Volume - 30, 1);
            }
            else
            {
                _vm.Volume = 30;
                await Task.Delay(400);
                mini.Tick();
                entry["volumeSliderFollows"] = Math.Abs(mini.CompactVolume.Value - 30) < 1;
                RaiseWheel(mini.CompactVolume, 120);
                entry["wheelStep"] = Math.Round(_vm.Volume - 30, 1);
            }

            designs[id] = entry;
        }

        r["designs"] = designs;

        // Izgled se pamti: spremi postavke u JSON i učitaj.
        mini.SetDesign("side");
        string json = System.Text.Json.JsonSerializer.Serialize(_settings.MiniPlayer);
        MiniPlayerSettings reloaded = System.Text.Json.JsonSerializer.Deserialize<MiniPlayerSettings>(json)!;
        r["designPersist"] = new Dictionary<string, object?> { ["design"] = reloaded.ActiveDesign, ["playlistShown"] = reloaded.ShowPlaylist };
        mini.SetPanels(false, false);
        mini.SetDesign("classic");
        await Task.Delay(500);

        // --- 2.3.15: odabir izgleda u Postavkama (svih pet, odmah na mini playeru) i poravnanje vremena -------------------
        ((IMiniPlayerHost)this).ShowSettings();
        await WaitForAsync(() => _playerSettingsWindow is { IsLoaded: true }, 5000);
        PlayerSettingsWindow sw = _playerSettingsWindow!;
        await Task.Delay(400);
        var settingsDesigns = new Dictionary<string, object?>();
        foreach (string id in MiniPlayerSettings.Designs)
        {
            sw.SelectMiniDesignForTests(id);
            await Task.Delay(500);
            var cards = sw.MiniDesignCards;
            settingsDesigns[id] = new Dictionary<string, object?>
            {
                ["miniActive"] = mini.ActiveDesign, ["saved"] = _settings.MiniPlayer.ActiveDesign,
                ["cardSelected"] = cards.Count(c => c.Selected) == 1 && cards.First(c => c.Selected).Id == id, ["cards"] = cards.Count,
                ["skin"] = mini.CurrentSkin?.GetType().Name, ["inside"] = Inside(mini),
            };
        }

        r["settingsDesigns"] = settingsDesigns;
        sw.SelectMiniDesignForTests("classic");
        await Task.Delay(500);
        sw.ShowMiniDesignCardsForTests();
        await Task.Delay(400);
        r["settingsDesignSnapshot"] = SaveWindowPng("settings_mini_design", sw);
        sw.Close();
        await Task.Delay(300);

        var timeAlign = new Dictionary<string, object?>();
        var timeParent = (FrameworkElement)mini.TimeText.Parent;
        foreach (string text in new[] { "00:09", "01:25", "12:45", "1:02:03", "-12:45", "-1:02:03" })
        {
            mini.TimeText.Text = text;
            timeParent.UpdateLayout();
            double centre = mini.TimeText.TranslatePoint(new Point(0, 0), timeParent).X + (mini.TimeText.ActualWidth / 2);
            timeAlign[text] = new Dictionary<string, object?>
            {
                ["offset"] = Math.Round(centre - (timeParent.ActualWidth / 2), 2), ["fits"] = mini.TimeText.ActualWidth <= timeParent.ActualWidth,
                ["fontSize"] = mini.TimeText.FontSize,
            };
        }

        r["timeAlign"] = timeAlign;

        // --- 7) Naslov: zadano samo izvođač i pjesma, beskonačno kruženje, odabir podataka -----------------------------
        mini.SetInfoFields(JebaitedPlayer.Services.TrackInfo.DefaultFields);
        await Task.Delay(1200);
        string defaultTitle = mini.MarqueeTextValue;
        mini.ToggleInfoField("quality");
        mini.ToggleInfoField("album");
        await Task.Delay(1500);
        string extraTitle = mini.MarqueeTextValue;
        mini.SetInfoFields(JebaitedPlayer.Services.TrackInfo.DefaultFields);
        await Task.Delay(600);
        string longText = string.Join("  ·  ", Enumerable.Repeat("A very long song title that has to scroll round and round", 4));
        mini.Marquee.Text = longText;
        await Task.Delay(400);
        bool longScrolls = mini.Marquee.IsScrolling;
        double cycle = mini.Marquee.CycleSeconds;
        double textWidth = mini.Marquee.TextWidth;
        double boxWidth = mini.Marquee.ActualWidth;
        mini.Marquee.Text = "Short";
        await Task.Delay(300);
        bool shortScrolls = mini.Marquee.IsScrolling;
        r["title"] = new Dictionary<string, object?>
        {
            ["default"] = defaultTitle, ["withExtras"] = extraTitle, ["longScrolls"] = longScrolls, ["cycleSeconds"] = Math.Round(cycle, 1),
            ["textWidth"] = Math.Round(textWidth), ["boxWidth"] = Math.Round(boxWidth), ["shortScrolls"] = shortScrolls,
            ["infoFields"] = _settings.MiniPlayer.InfoFields.ToArray(),
        };

        // --- 8) Discord Bot u izborniku izvora -------------------------------------------------------------------------------
        r["sourceMenu"] = mini.BuildSourceMenu().Items.OfType<MenuItem>().Select(m => m.Header?.ToString()).ToArray();

        // --- 2) Položaj prozora uz taskbar -----------------------------------------------------------------------------------
        Rect work = WindowPlacement.WorkAreaFor(mini);
        mini.Left = work.Right - mini.ActualWidth - 10;
        mini.Top = work.Bottom - mini.ActualHeight - 4;
        await Task.Delay(400);
        mini.SetPanels(equalizer: true, playlist: true);
        await Task.Delay(1200);
        bool afterEqInside = Inside(mini);
        double eqBottom = mini.Top + mini.ActualHeight;
        mini.Left = work.Right + 400;
        mini.Top = work.Bottom + 400;
        await Task.Delay(900);
        bool afterFarInside = Inside(mini);
        mini.SetPanels(false, false);
        mini.SetDesign("strip");
        mini.Top = work.Bottom - 10;
        await Task.Delay(900);
        bool stripInside = Inside(mini);
        mini.SetDesign("classic");
        mini.Left = work.Right - mini.ActualWidth - 10;
        mini.Top = work.Bottom - mini.ActualHeight - 4;
        await Task.Delay(500);
        ShowDiscord(mini);
        await Task.Delay(1200);
        bool discordInside = _discordWindow is not null && Inside(_discordWindow);
        _discordWindow?.Close();
        r["placement"] = new Dictionary<string, object?>
        {
            ["workBottom"] = Math.Round(work.Bottom), ["miniBottomWithPanels"] = Math.Round(eqBottom), ["afterEqInside"] = afterEqInside,
            ["afterFarInside"] = afterFarInside, ["stripInside"] = stripInside, ["discordInside"] = discordInside,
            ["clampedBottomEdge"] = WindowPlacement.Clamp(new Rect(100, work.Bottom - 20, 400, 300), work).Bottom <= work.Bottom + 0.5,
        };
        mini.Left = Math.Max(work.Left + 100, 100);
        mini.Top = Math.Max(work.Top + 100, 100);
        await Task.Delay(500);

        // --- 3) Minimize ------------------------------------------------------------------------------------------------------
        double m0 = _vm.Position;
        mini.OnMinimize(mini, new RoutedEventArgs());
        await Task.Delay(1500);
        bool miniMinimized = mini.WindowState == WindowState.Minimized;
        double m1 = _vm.Position;
        bool pausedWhileMinimized = _vm.IsPaused;
        mini.WindowState = WindowState.Normal;
        await Task.Delay(500);
        ((IMiniPlayerHost)this).ShowFullPlayer();
        await Task.Delay(600);
        double f0 = _vm.Position;
        MinimizePlayer();
        await Task.Delay(1500);
        bool fullMinimized = WindowState == WindowState.Minimized;
        double f1 = _vm.Position;
        bool fullPaused = _vm.IsPaused;
        WindowState = WindowState.Normal;
        await Task.Delay(500);
        r["minimize"] = new Dictionary<string, object?>
        {
            ["miniMinimized"] = miniMinimized, ["miniAdvanced"] = Math.Round(m1 - m0, 2), ["miniPaused"] = pausedWhileMinimized,
            ["fullMinimized"] = fullMinimized, ["fullAdvanced"] = Math.Round(f1 - f0, 2), ["fullPaused"] = fullPaused,
            ["mainVisibleAfter"] = IsVisible && WindowState == WindowState.Normal,
        };

        // --- 5) LIVE ----------------------------------------------------------------------------------------------------------
        string file = _vm.CurrentPath!;
        byte[] audio = TestStreamServer.AudioWithoutTag(System.IO.File.ReadAllBytes(file), 4_000_000);
        using var server = new TestStreamServer(audio);
        _player?.SetPropertyAsync("loop-file", "no");
        ShowMiniPlayer();
        await Task.Delay(500);
        double notLiveVisible = mini.LiveClassic.IsVisible ? 1 : 0;
        PlayStream(server.Url, "Test radio");
        await WaitForAsync(() => _vm.HasMedia && !_vm.IsLoading && _vm.Position > 1.5 && _vm.IsLiveStream, 15000);
        await Task.Delay(800);
        var live = new Dictionary<string, object?>
        {
            ["notLiveBefore"] = notLiveVisible == 0, ["classicVisible"] = mini.LiveClassic.IsVisible, ["classicPulsing"] = mini.LiveClassic.IsPulsing,
            ["screenshotClassic"] = SaveWindowPng("live_classic", mini),
        };
        mini.SetDesign("strip");
        await Task.Delay(500);
        live["stripVisible"] = mini.LiveCompact.IsVisible;
        mini.SetDesign("card");
        await Task.Delay(700);
        live["cardVisible"] = mini.CurrentSkin?.LiveMark.IsVisible;
        live["screenshotCard"] = SaveWindowPng("live_card", mini);
        mini.SetDesign("ufo");
        await Task.Delay(700);
        live["ufoVisible"] = mini.CurrentSkin?.LiveMark.IsVisible;
        mini.SetDesign("side");
        await Task.Delay(700);
        live["sideVisible"] = mini.CurrentSkin?.LiveMark.IsVisible;
        mini.SetDesign("classic");
        ((IMiniPlayerHost)this).ShowFullPlayer();
        await Task.Delay(1200);
        _overlayVisible = false;
        RegisterActivity();
        await Task.Delay(500);
        live["overlayVisible"] = _overlay!.LiveMark.IsVisible;
        live["audioPanelVisible"] = AudioLive.IsVisible;
        GoHome();
        await Task.Delay(1500);
        live["homeVisible"] = HomeLive.IsVisible;
        live["homeStillPlaying"] = _vm.HasMedia && !_vm.IsPaused && _vm.IsLiveStream;
        live["homeSource"] = HomeSourceText.Text;
        live["homeScreenshot"] = SaveWindowPng("home_radio");
        LeaveHomeView();
        ShowMiniPlayer();
        await Task.Delay(500);
        _vm.TogglePause();
        await Task.Delay(1200);
        live["afterPause"] = mini.LiveClassic.IsVisible;
        _vm.TogglePause();
        await Task.Delay(2500);
        live["afterResume"] = mini.LiveClassic.IsVisible;
        mini.OnStop(mini, new RoutedEventArgs());
        await Task.Delay(1200);
        live["afterStop"] = mini.LiveClassic.IsVisible;
        r["live"] = live;

        // --- 10) Izgled vremena, datuma i prognoze --------------------------------------------------------------------------
        ((IMiniPlayerHost)this).ShowFullPlayer();
        _vm.Volume = 100; // dok audio izlaz još postoji (Windows pamti glasnoću sesije po aplikaciji)
        await Task.Delay(600);
        _vm.Stop();
        await Task.Delay(800);
        JebaitedPlayer.Services.HomeInfoSettings h = _settings.HomeInfo;
        h.ShowTime = h.ShowDate = true;
        h.TimeStyle = new JebaitedPlayer.Services.HomeElementStyle { Font = "Georgia", Size = 48, Position = "tr" };
        h.DateStyle = new JebaitedPlayer.Services.HomeElementStyle { Font = "Consolas", Size = 20, Position = "bl" };
        h.WeatherStyle = new JebaitedPlayer.Services.HomeElementStyle { Position = "br" };
        ApplyHomeInfo();
        await Task.Delay(700);
        r["homeLook"] = new Dictionary<string, object?>
        {
            ["timeFont"] = HomeTimeText.FontFamily.Source, ["timeSize"] = HomeTimeText.FontSize, ["timePos"] = HomeLayer.PositionOf("time"),
            ["dateFont"] = HomeDateText.FontFamily.Source, ["dateSize"] = HomeDateText.FontSize, ["datePos"] = HomeLayer.PositionOf("date"),
            ["weatherPos"] = HomeLayer.PositionOf("weather"), ["screenshot"] = SaveWindowPng("home_look"),
        };
        h.ResetStyle();
        ApplyHomeInfo();
        await Task.Delay(400);
        r["homeLookReset"] = new Dictionary<string, object?>
        {
            ["timeSize"] = HomeTimeText.FontSize, ["timePos"] = HomeLayer.PositionOf("time"), ["datePos"] = HomeLayer.PositionOf("date"),
            ["weatherPos"] = HomeLayer.PositionOf("weather"), ["dateSize"] = HomeDateText.FontSize,
        };

        // Postavke: pregled prije spremanja i kontrole izgleda (pravi prozor postavki).
        ShowPlayerSettings(SettingsTab.Appearance);
        await Task.Delay(1500);
        PlayerSettingsWindow? settings = _playerSettingsWindow;
        settings?.ShowHomeLookForTests();
        await Task.Delay(600);
        r["settingsLook"] = new Dictionary<string, object?>
        {
            ["open"] = settings is not null, ["hasPreview"] = settings?.HomePreviewForTests is not null,
            ["previewTime"] = settings?.HomePreviewForTests?.TimeText.Text, ["screenshot"] = SaveWindowPng("settings_homelook", settings),
        };
        settings?.Close();

        // --- 9, završno) video se i dalje zatvara tipkom Home ---------------------------------------------------------------
        string? clip = Environment.GetEnvironmentVariable("JEBAITED_UI217_CLIP");
        if (!string.IsNullOrEmpty(clip) && System.IO.File.Exists(clip))
        {
            OpenFiles([clip]);
            await WaitForAsync(() => _vm.HasMedia && !_vm.IsLoading && !_vm.IsAudioOnly && _vm.Position > 0.5, 12000);
            bool wasVideo = _vm.HasMedia && !_vm.IsAudioOnly;
            _vm.Volume = 100;
            _player?.SetPropertyAsync("ao-volume", "100");
            await Task.Delay(600);
            GoHome();
            await Task.Delay(800);
            r["videoHome"] = new Dictionary<string, object?> { ["wasVideo"] = wasVideo, ["hasMediaAfter"] = _vm.HasMedia, ["homeView"] = _homeView, ["welcome"] = WelcomePanel.IsVisible };
        }

        r["aoVolumeBeforeReset"] = MpvString("ao-volume");
        _vm.Volume = 100; // Windows pamti glasnoću sesije po aplikaciji: ne ostavljaj testnu vrijednost
        await Task.Delay(300);
        r["aoVolumeEnd"] = MpvString("ao-volume");
        r["loadsTotal"] = loads.Count;
        return r;
    }
}

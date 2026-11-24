using System.IO;
using System.Text.Json;
using System.Windows;
using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.Iptv;
using JebaitedPlayer.Views;

namespace JebaitedPlayer;

/// <summary>
/// Dijagnostika IPTV-a (<c>--diag-ui-test iptv</c>): stvarni prozor, stvarna prijava (jedna) i stvarna reprodukcija.
/// Vjerodajnice se čitaju iz lokalne datoteke koja je u .gitignore (JEBAITED_IPTV_TEST_FILE) i nikad se ne zapisuju u rezultat.
/// </summary>
public partial class MainWindow
{
    private async Task<Dictionary<string, object?>> IptvUiTestAsync()
    {
        var r = new Dictionary<string, object?>();
        string? file = Environment.GetEnvironmentVariable("JEBAITED_IPTV_TEST_FILE");
        if (file is null || !File.Exists(file))
        {
            r["error"] = "test account file missing (JEBAITED_IPTV_TEST_FILE)";
            return r;
        }

        JsonElement j = JsonDocument.Parse(File.ReadAllText(file)).RootElement;
        var store = new IptvStore(AppPaths.DataDir);
        var settings = new IptvSettings();
        settings.Sources.Add(new IptvSourceConfig
        {
            Id = "test", Name = "Test", Kind = IptvSourceKind.Xtream, Server = j.GetProperty("server").GetString(),
            Username = j.GetProperty("username").GetString(), PasswordProtected = Secret.Protect(j.GetProperty("password").GetString()),
        });
        settings.DefaultSourceId = "test";
        store.SaveSettings(settings);
        r["enabledBefore"] = IptvEnabled;

        // 1) Povezivanje (jedna prijava) i popis.
        ShowIptv();
        IptvPanel w = _iptv!;
        await WaitForAsync(() => w.IsConnected && w.VisibleCount > 0, 60000);
        r["connected"] = w.IsConnected;
        r["channels"] = w.VisibleCount;
        r["categories"] = w.CategoryCount;
        r["status"] = w.Status;
        await Task.Delay(1500);
        r["realizedRows"] = w.RealizedRowCount;
        r["privateMB"] = System.Diagnostics.Process.GetCurrentProcess().PrivateMemorySize64 / 1024 / 1024;
        r["shot1"] = SaveWindowPng("iptv_start", this);
        if (!w.IsConnected)
        {
            return r;
        }

        // 2) Pretraga i kategorija.
        w.SearchTextBox.Text = "sport";
        await Task.Delay(700);
        r["searchCount"] = w.VisibleCount;
        r["searchAllMatch"] = w.VisibleChannels.All(c => c.Name.Contains("sport", StringComparison.OrdinalIgnoreCase) || c.Group.Contains("sport", StringComparison.OrdinalIgnoreCase) || (c.EpgId?.Contains("sport", StringComparison.OrdinalIgnoreCase) ?? false));
        w.SearchTextBox.Text = string.Empty;
        await Task.Delay(500);
        w.SelectCategoryForTests("Sport");
        await Task.Delay(500);
        r["sportCount"] = w.VisibleCount;
        r["sportAll"] = w.VisibleChannels.All(c => c.Group == "Sport");

        // 3) Sortiranje A–Z i Z–A.
        w.SetSortForTests(ChannelSort.NameAsc);
        await Task.Delay(300);
        var az = w.VisibleChannels.Take(20).Select(c => c.Name).ToList();
        r["azSorted"] = az.SequenceEqual(az.OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase));
        w.SetSortForTests(ChannelSort.NameDesc);
        await Task.Delay(300);
        var za = w.VisibleChannels.Take(20).Select(c => c.Name).ToList();
        r["zaSorted"] = za.SequenceEqual(za.OrderByDescending(x => x, StringComparer.CurrentCultureIgnoreCase));

        // 4) Ručni redoslijed: drugi kanal na prvo mjesto.
        w.SetSortForTests(ChannelSort.Manual);
        await Task.Delay(300);
        string second = w.VisibleChannels[1].Id;
        w.MoveForTests(w.Rows[1], -1);
        await Task.Delay(300);
        r["manualMoved"] = w.VisibleChannels[0].Id == second;

        // 5) Favorit.
        IptvChannelRow fav = w.Rows[2];
        string favId = fav.Channel.Id;
        w.ToggleFavoriteForTests(fav);
        await Task.Delay(300);
        r["favoriteAdded"] = w.FavoriteIds.Contains(favId) && w.FavoriteIds.Count(x => x == favId) == 1;

        // 6) EPG: vodič i traka za izabrani kanal.
        await WaitForAsync(() => w.EpgProgramCount > 0, 30000);
        r["epgPrograms"] = w.EpgProgramCount;
        r["rowsWithEpg"] = w.RowsWithEpg;
        IptvChannelRow? withEpg = w.Rows.FirstOrDefault(x => x.HasProgress);
        if (withEpg is not null)
        {
            w.SelectForTests(withEpg);
            await Task.Delay(800);
            r["epgCards"] = w.EpgCardCount;
            r["infoNow"] = w.InfoNowText;
        }

        r["shot2"] = SaveWindowPng("iptv_list", this);

        _diagAutoSwitch = true; // kao u pravom radu: automatski prelazak na mini player za glazbu/radio ne smije zahvatiti IPTV
        // 7) Reprodukcija (UŽIVO, mpv u glavnom playeru).
        IptvChannelRow first = w.Rows[Math.Min(3, w.Rows.Count - 1)];
        _player?.SetPropertyAsync("mute", "yes");
        w.Play(first);
        await WaitForAsync(() => _vm.HasMedia && !_vm.IsLoading && _vm.Position > 0.5, 30000);
        r["playing"] = _vm.HasMedia && !_vm.IsLoading;
        r["position"] = Math.Round(_vm.Position, 1);
        r["error1"] = _vm.ErrorMessage;
        r["playingFlag"] = first.IsPlaying;
        r["notMini"] = !IsMiniPlayerActive;
        r["musicModeOff"] = !MpvConfig.MusicMode;
        r["shot3"] = SaveWindowPng("iptv_playing", this);
        r["shotMain"] = SaveWindowPng("iptv_main", this);

        // 7b) Cijeli zaslon tipkom F dok je IPTV panel prikazan: panel se sakrije, a nakon izlaska vrati; klik na sliku ne pauzira.
        r["clickDoesNotPause"] = _currentIsIptv;
        // Traka vremena za TV je uvijek puna (uživo nema trajanja): nema djelomične zelene trake ni treperenja.
        r["seekBarFull"] = _vm.TimelinePosition >= _vm.TimelineMaximum && _vm.DurationText.Length == 0;
        r["panelVisibleBeforeFs"] = IsIptvVisible;
        HandleShortcut(System.Windows.Input.Key.F, System.Windows.Input.ModifierKeys.None);
        await Task.Delay(1500);
        r["fullscreenOn"] = _vm.IsFullscreen;
        r["panelHiddenInFs"] = !IsIptvVisible;
        r["notPausedInFs"] = !_vm.IsPaused;
        HandleShortcut(System.Windows.Input.Key.F, System.Windows.Input.ModifierKeys.None);
        await Task.Delay(1500);
        r["fullscreenOff"] = !_vm.IsFullscreen;
        r["panelBackAfterFs"] = IsIptvVisible;
        r["notPausedAfterFs"] = !_vm.IsPaused;
        r["noSubtitleNotice"] = !((_vm.Notice ?? string.Empty) + (_vm.ErrorMessage ?? string.Empty)).Contains("titl", StringComparison.OrdinalIgnoreCase)
            && !((_vm.Notice ?? string.Empty) + (_vm.ErrorMessage ?? string.Empty)).Contains("subtit", StringComparison.OrdinalIgnoreCase);
        r["noticeText"] = _vm.Notice;

        // 7c) Favorit iz „Nedavno gledano” ostaje u toj kategoriji; trake napretka su 0..1 (ne puna traka).
        w.SelectCategoryKindForTests(2);
        await Task.Delay(500);
        IptvChannelRow? recent = w.Rows.FirstOrDefault();
        if (recent is not null)
        {
            w.ToggleFavoriteForTests(recent);
            await Task.Delay(400);
            r["recentKeptAfterFavorite"] = w.SelectedCategoryKind == 2;
            w.ToggleFavoriteForTests(recent);
        }

        r["maxRowProgress"] = w.Rows.Concat(new[] { first }).Where(x => x.HasProgress).Select(x => x.Progress).DefaultIfEmpty(0).Max();
        w.SelectCategoryKindForTests(3);
        await Task.Delay(300);

        // 7d) Izgled: font, veličina, boje (samo IPTV panel).
        w.SetAppearanceForTests("Georgia", 1.15, "#5AA9FF", "#0F1B33");
        await Task.Delay(500);
        r["appearanceBrushes"] = w.HasBrushOverride("AccentBrush") && w.HasBrushOverride("PanelBackgroundBrush");
        r["appearanceFont"] = w.FontFamily.Source;
        r["shotAppearance"] = SaveWindowPng("iptv_appearance", this);
        w.SetAppearanceForTests(null, 1.0, null, null);
        r["appearanceReset"] = !w.HasBrushOverride("AccentBrush");

        // 7e) Zastave pored država i skrivanje kategorije (desni klik → Sakrij; ovdje isti poziv).
        r["flaggedCategories"] = w.FlaggedCategoryCount;
        string? hideName = w.CategoryNames.FirstOrDefault(n => n.Contains("Norw", StringComparison.OrdinalIgnoreCase)) ?? w.CategoryNames.LastOrDefault();
        int namesBefore = w.CategoryNames.Count;
        if (hideName is not null)
        {
            w.SetCategoryHiddenForTests(hideName, true);
            await Task.Delay(400);
            r["hiddenCategory"] = hideName;
            r["categoryRemovedFromList"] = !w.CategoryNames.Contains(hideName) && w.CategoryNames.Count == namesBefore - 1;
            r["hiddenChannelsExcluded"] = w.VisibleChannels.All(c => !c.Group.Equals(hideName, StringComparison.OrdinalIgnoreCase));
            w.SetCategoryHiddenForTests(hideName, false);
            await Task.Delay(400);
            r["categoryRestored"] = w.CategoryNames.Contains(hideName);
        }

        // 7f) Home dok svira IPTV: player na Home ekranu ima Stop; Stop prekida TV i uklanja player.
        GoHome();
        await Task.Delay(1500);
        r["homeBarVisible"] = HomePlaybackBar.IsVisible;
        r["homeStopVisible"] = HomeStopButton.IsVisible;
        r["homeSource"] = HomeSourceText.Text;
        r["railFullHeight"] = Math.Abs(HomeRail.ActualHeight - WelcomePanel.ActualHeight) < 2;
        Point rc = HomeOpenButtons.TranslatePoint(new Point(0, HomeOpenButtons.ActualHeight / 2), HomeRail);
        r["railIconsCentered"] = Math.Abs(rc.Y - HomeRail.ActualHeight / 2) < 3;
        r["bottomRowRightOfRail"] = HomeBottomRow.TranslatePoint(new Point(0, 0), WelcomePanel).X >= 56;
        // Bočna traka se pod mišem proširi i pokaže nazive, a ostatak ekrana se ne pomiče.
        double contentLeft = HomeLayer.TranslatePoint(new Point(0, 0), WelcomePanel).X;
        SetRailExpanded(true);
        await Task.Delay(500);
        r["railExpandedWidth"] = Math.Round(HomeRailWidthForTests);
        r["railLabelsVisible"] = HomeOpenButtons.Children.OfType<System.Windows.Controls.Button>().Where(b => b.Visibility == Visibility.Visible).All(b => b.Content is System.Windows.Controls.StackPanel { Children.Count: > 1 } sp && sp.Children[1].IsVisible);
        r["railDidNotShiftContent"] = Math.Abs(HomeLayer.TranslatePoint(new Point(0, 0), WelcomePanel).X - contentLeft) < 1;
        r["shotRail"] = SaveWindowPng("iptv_rail_expanded", this);
        SetRailExpanded(false);
        await Task.Delay(500);
        r["railCollapsedWidth"] = Math.Round(HomeRailWidthForTests);
        r["railLabelsHidden"] = HomeOpenButtons.Children.OfType<System.Windows.Controls.Button>().All(b => b.Content is System.Windows.Controls.StackPanel { Children.Count: > 1 } sp && !sp.Children[1].IsVisible);
        HomeStopButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        await Task.Delay(1500);
        r["stoppedNoMedia"] = !_vm.HasMedia;
        r["homeBarGoneAfterStop"] = !HomePlaybackBar.IsVisible;
        r["iptvPanelClosedAfterStop"] = !IsIptvVisible;
        r["notIptvAfterStop"] = !_currentIsIptv;
        r["shotHome"] = SaveWindowPng("iptv_home_stop", this);
        LeaveHomeView();
        await Task.Delay(500);
        w.Play(first);
        await WaitForAsync(() => _vm.HasMedia && !_vm.IsLoading && _vm.Position > 0.5, 30000);

        // 8) Brzo mijenjanje kanala (6 puta).
        int last = Math.Min(9, w.Rows.Count - 1);
        for (int i = 4; i <= last; i++)
        {
            w.Play(w.Rows[i]);
            await Task.Delay(250);
        }

        string lastName = w.Rows[last].Channel.Name;
        await WaitForAsync(() => _vm.HasMedia && !_vm.IsLoading && _vm.Position > 0.5, 30000);
        r["rapidOk"] = _vm.HasMedia && string.IsNullOrEmpty(_vm.ErrorMessage);
        r["rapidTitle"] = _vm.Title == lastName;

        if (!IsIptvVisible)
        {
            ShowIptv();
            await Task.Delay(500);
        }

        // 9) Neispravan tok: greška, Pokušaj ponovno, aplikacija ostaje stabilna; zatim ispravan kanal.
        var bad = new IptvChannelRow(new IptvChannel("bad", "Neispravan", "http://127.0.0.1:9/nema.ts", "x", null, null, 0, false), false);
        w.Play(bad);
        await WaitForAsync(() => !string.IsNullOrEmpty(_vm.ErrorMessage), 20000);
        await Task.Delay(1200);
        r["badError"] = !string.IsNullOrEmpty(_vm.ErrorMessage);
        r["badRetryShown"] = w.RetryVisible;
        r["badState"] = w.PlayStateText;
        w.Play(first);
        await WaitForAsync(() => _vm.HasMedia && !_vm.IsLoading && _vm.Position > 0.5 && string.IsNullOrEmpty(_vm.ErrorMessage), 30000);
        r["recovered"] = _vm.HasMedia && string.IsNullOrEmpty(_vm.ErrorMessage);

        // 10) Zatvaranje prozora tijekom reprodukcije ne prekida reprodukciju; ponovno otvaranje čuva favorite i sortiranje.
        HideIptv();
        await Task.Delay(800);
        r["stillPlayingAfterClose"] = _vm.HasMedia;
        string userFile = Directory.GetFiles(AppPaths.DataDir, "iptv-user-test.json").FirstOrDefault() ?? "";
        r["favoriteSaved"] = userFile.Length > 0 && File.ReadAllText(userFile).Contains(favId);
        // Novi panel (kao nakon ponovnog pokretanja): stanje se mora učitati s diska.
        IptvDock.Child = null;
        _iptv = null;
        ShowIptv();
        w = _iptv!;
        await WaitForAsync(() => w.IsConnected && w.VisibleCount > 0, 60000);
        r["reopenFavoriteKept"] = w.FavoriteIds.Contains(favId);
        r["reopenSortKept"] = w.Settings.DefaultSort == ChannelSort.Manual;

        // 11) Isključivanje modula.
        HideIptv();
        await Task.Delay(300);
        IptvSettings s2 = store.LoadSettings();
        s2.Enabled = false;
        store.SaveSettings(s2);
        r["enabledAfterDisable"] = IptvEnabled;
        s2.Enabled = true;
        store.SaveSettings(s2);

        // 12) Lozinka nigdje nije u datoteci postavki ni u dnevniku.
        string pass = j.GetProperty("password").GetString()!;
        string user = j.GetProperty("username").GetString()!;
        string cfgText = File.ReadAllText(Path.Combine(AppPaths.DataDir, "iptv.json"));
        r["settingsHasNoPlainPassword"] = !cfgText.Contains(pass);
        string logs = string.Join("\n", Directory.EnumerateFiles(AppPaths.LogsDir).Select(f =>
        {
            try
            {
                using var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                return new StreamReader(fs).ReadToEnd();
            }
            catch (IOException)
            {
                return string.Empty;
            }
        }));
        r["logsHaveNoPassword"] = !logs.Contains(pass);
        r["logsHaveNoUsername"] = !logs.Contains(user);
        return r;
    }
}

public partial class MainWindow
{
    /// <summary>
    /// Dugotrajna reprodukcija jednog IPTV kanala (<c>--diag-ui-test iptvlong</c>, trajanje u minutama iz JEBAITED_IPTV_MINUTES):
    /// uzorak svakih 5 s (pauza, položaj, međuspremnik), da se vidi pauzira li se program sam ili zastaje.
    /// </summary>
    private async Task<Dictionary<string, object?>> IptvLongUiTestAsync()
    {
        var r = new Dictionary<string, object?>();
        string? file = Environment.GetEnvironmentVariable("JEBAITED_IPTV_TEST_FILE");
        if (file is null || !File.Exists(file))
        {
            r["error"] = "test account file missing";
            return r;
        }

        int minutes = int.TryParse(Environment.GetEnvironmentVariable("JEBAITED_IPTV_MINUTES"), out int m) ? m : 8;
        JsonElement j = JsonDocument.Parse(File.ReadAllText(file)).RootElement;
        var store = new IptvStore(AppPaths.DataDir);
        var settings = new IptvSettings();
        settings.Sources.Add(new IptvSourceConfig
        {
            Id = "test", Name = "Test", Kind = IptvSourceKind.Xtream, Server = j.GetProperty("server").GetString(),
            Username = j.GetProperty("username").GetString(), PasswordProtected = Secret.Protect(j.GetProperty("password").GetString()),
        });
        settings.DefaultSourceId = "test";
        store.SaveSettings(settings);
        ShowIptv();
        IptvPanel w = _iptv!;
        await WaitForAsync(() => w.IsConnected && w.VisibleCount > 0, 60000);
        _player?.SetPropertyAsync("mute", "yes");
        w.Play(w.Rows[3]);
        await WaitForAsync(() => _vm.HasMedia && !_vm.IsLoading && _vm.Position > 0.5, 30000);

        long end = Environment.TickCount64 + minutes * 60_000L;
        int samples = 0, paused = 0, cachePaused = 0, stalls = 0, errors = 0;
        double lastPos = _vm.Position;
        int stallRun = 0, maxStall = 0;
        var events = new List<string>();
        while (Environment.TickCount64 < end)
        {
            await Task.Delay(5000);
            samples++;
            bool isPaused = _vm.IsPaused;
            string? pfc = _player?.GetPropertyString("paused-for-cache");
            double pos = _vm.Position;
            if (isPaused) { paused++; events.Add($"{samples * 5}s: paused"); }
            if (pfc == "yes") { cachePaused++; events.Add($"{samples * 5}s: paused-for-cache"); }
            if (pos - lastPos < 3.0 && !isPaused) { stalls++; stallRun++; maxStall = Math.Max(maxStall, stallRun); events.Add($"{samples * 5}s: no progress"); }
            else { stallRun = 0; }
            if (!string.IsNullOrEmpty(_vm.ErrorMessage)) { errors++; events.Add($"{samples * 5}s: error"); }
            lastPos = pos;
        }

        r["minutes"] = minutes;
        r["samples"] = samples;
        r["pausedSamples"] = paused;
        r["cachePausedSamples"] = cachePaused;
        r["noProgressSamples"] = stalls;
        r["longestNoProgressRun"] = maxStall;
        r["errorSamples"] = errors;
        r["finalPosition"] = Math.Round(_vm.Position, 1);
        r["events"] = events.Take(40).ToArray();
        return r;
    }
}

public partial class MainWindow
{
    /// <summary>Bočna traka početnog ekrana (<c>--diag-ui-test homerail</c>): glatko širenje, nazivi, širina, bez pomicanja sadržaja.</summary>
    private async Task<Dictionary<string, object?>> HomeRailUiTestAsync()
    {
        var r = new Dictionary<string, object?>();
        await WaitForAsync(() => _vm.HasMedia && !_vm.IsLoading && _vm.Position > 0.5, 12000);
        _player?.SetPropertyAsync("mute", "yes");
        string trackPath = _vm.CurrentPath!;
        // Pretraga titlova uz otvoren video otvara prozor pretrage (ne izvore titlova).
        OnHomeSearchClick(this, new RoutedEventArgs());
        await Task.Delay(1200);
        r["searchOpensSearchWindow"] = _searchWindow is { IsLoaded: true };
        r["searchOpenedSourcesWindow"] = _sourcesWindow is { IsLoaded: true };
        _searchWindow?.Close();
        await Task.Delay(300);
        _vm.Stop();
        await WaitForAsync(() => !_vm.HasMedia && WelcomePanel.IsVisible, 5000);
        await Task.Delay(1200);
        // Glatkoća: broj iscrtanih sličica i najduži razmak tijekom širenja (sama traka; zatim uz pokrenut video i otvoren IPTV panel).
        (int fAlone, double maxAlone, double avgAlone) = await MeasureRailFramesAsync(true);
        r["alone_frames"] = fAlone; r["alone_maxGapMs"] = Math.Round(maxAlone, 1); r["alone_avgGapMs"] = Math.Round(avgAlone, 1);
        await MeasureRailFramesAsync(false);
        await Task.Delay(300);
        double contentLeft = HomeLayer.TranslatePoint(new Point(0, 0), WelcomePanel).X;
        r["collapsedWidth"] = Math.Round(HomeRailWidthForTests);
        r["labelsHiddenAtStart"] = HomeOpenButtons.Children.OfType<System.Windows.Controls.Button>().All(b => b.Content is System.Windows.Controls.StackPanel { Children.Count: > 1 } sp && !sp.Children[1].IsVisible);

        // Pravi miš: prelazak preko trake je širi, a odlazak sa trake vraća (hover stil: zelena crta + sjaj na retku pod mišem).
        if (Interop.NativeMethods.NoMouse)
        {
            r["realHoverSkipped"] = "no-mouse mode";
        }
        else
        {
            Point target = HomeSearchButton.PointToScreen(new Point(24, HomeSearchButton.ActualHeight / 2));
            Interop.NativeMethods.SetCursorPos((int)target.X, (int)target.Y);
            await Task.Delay(700);
            r["realHoverExpands"] = IsHomeRailExpanded && HomeRailWidthForTests >= 199;
            r["hoverRowIsMouseOver"] = HomeSearchButton.IsMouseOver;
            r["shotHover"] = SaveWindowPng("homerail_hover", this);
            Point away = WelcomePanel.PointToScreen(new Point(WelcomePanel.ActualWidth * 0.8, WelcomePanel.ActualHeight * 0.5));
            Interop.NativeMethods.SetCursorPos((int)away.X, (int)away.Y);
            await Task.Delay(700);
            r["realLeaveCollapses"] = !IsHomeRailExpanded && Math.Round(HomeRailWidthForTests) == 56;
        }

        // Širenje: uzorkuj širinu tijekom animacije (mora proći kroz međuvrijednosti = glatko, ne skok).
        SetRailExpanded(true);
        var samples = new List<double>();
        for (int i = 0; i < 14; i++)
        {
            await Task.Delay(20);
            samples.Add(Math.Round(HomeRailWidthForTests));
        }

        await Task.Delay(500);
        double expanded = HomeRailWidthForTests;
        r["openSamples"] = samples.ToArray();
        r["openIntermediate"] = samples.Count(w => w > 60 && w < expanded - 4);
        r["expandedWidth"] = Math.Round(expanded);
        r["labelsVisible"] = HomeOpenButtons.Children.OfType<System.Windows.Controls.Button>().Where(b => b.Visibility == Visibility.Visible).All(b => b.Content is System.Windows.Controls.StackPanel { Children.Count: > 1 } sp && sp.Children[1].IsVisible && sp.Children[1].Opacity > 0.95);
        // Najduži naziv mora imati razmak do desnog ruba trake (prije je tekst dolazio tik do ruba).
        double maxRight = 0;
        foreach (System.Windows.Controls.Button b in HomeOpenButtons.Children.OfType<System.Windows.Controls.Button>().Where(b => b.Visibility == Visibility.Visible))
        {
            if (b.Content is System.Windows.Controls.StackPanel { Children.Count: > 1 } sp)
            {
                var label = (FrameworkElement)sp.Children[1];
                double right = label.TranslatePoint(new Point(label.ActualWidth, 0), HomeRail).X;
                maxRight = Math.Max(maxRight, right);
            }
        }

        r["longestLabelRight"] = Math.Round(maxRight);
        r["marginToRailEdge"] = Math.Round(expanded - maxRight);
        r["contentNotShifted"] = Math.Abs(HomeLayer.TranslatePoint(new Point(0, 0), WelcomePanel).X - contentLeft) < 1;
        r["shotExpanded"] = SaveWindowPng("homerail_expanded", this);

        // Zatvaranje: glatko natrag, nazivi nestaju.
        SetRailExpanded(false);
        var closing = new List<double>();
        for (int i = 0; i < 12; i++)
        {
            await Task.Delay(20);
            closing.Add(Math.Round(HomeRailWidthForTests));
        }

        await Task.Delay(500);
        r["closeSamples"] = closing.ToArray();
        r["closeIntermediate"] = closing.Count(w => w > 60 && w < expanded - 4);
        r["collapsedAgain"] = Math.Round(HomeRailWidthForTests) == 56;
        r["labelsHiddenAgain"] = HomeOpenButtons.Children.OfType<System.Windows.Controls.Button>().All(b => b.Content is System.Windows.Controls.StackPanel { Children.Count: > 1 } sp && !sp.Children[1].IsVisible);

        // Brzo gore-dolje (miš ulazi i izlazi): na kraju mora biti uredno zatvorena.
        for (int i = 0; i < 6; i++)
        {
            SetRailExpanded(i % 2 == 0);
            await Task.Delay(60);
        }

        SetRailExpanded(false);
        await Task.Delay(600);
        // Isto uz pokrenut video i otvoren IPTV panel (traka je tada desno od panela).
        OpenFiles([trackPath]);
        await WaitForAsync(() => _vm.HasMedia && !_vm.IsLoading && _vm.Position > 0.5, 12000);
        _player?.SetPropertyAsync("mute", "yes");
        GoHome();
        ShowIptv();
        await Task.Delay(1500);
        r["busy_railLeft"] = Math.Round(HomeRail.TranslatePoint(new Point(0, 0), this).X);
        (int fBusy, double maxBusy, double avgBusy) = await MeasureRailFramesAsync(true);
        r["busy_frames"] = fBusy; r["busy_maxGapMs"] = Math.Round(maxBusy, 1); r["busy_avgGapMs"] = Math.Round(avgBusy, 1);
        await MeasureRailFramesAsync(false);
        r["rapidEndsCollapsed"] = Math.Round(HomeRailWidthForTests) == 56
            && HomeOpenButtons.Children.OfType<System.Windows.Controls.Button>().All(b => b.Content is System.Windows.Controls.StackPanel { Children.Count: > 1 } sp && !sp.Children[1].IsVisible);
        return r;
    }
}

public partial class MainWindow
{
    /// <summary>Širina IPTV panela i popisa kategorija (<c>--diag-ui-test iptvresize</c>): povlačenje, ograničenja, pamćenje; bez računa i mreže.</summary>
    private async Task<Dictionary<string, object?>> IptvResizeUiTestAsync()
    {
        var r = new Dictionary<string, object?>();
        await WaitForAsync(() => _vm.HasMedia && !_vm.IsLoading && _vm.Position > 0.5, 12000);
        _vm.Stop();
        await WaitForAsync(() => !_vm.HasMedia && WelcomePanel.IsVisible, 5000);
        await Task.Delay(800);

        // Naziv stavke za titlove na traci je novi, kratki naziv.
        var label = (FrameworkElement)((System.Windows.Controls.StackPanel)HomeSearchButton.Content).Children[1];
        r["railSearchLabel"] = ((System.Windows.Controls.TextBlock)label).Text;
        r["railLabelIsNewName"] = ((System.Windows.Controls.TextBlock)label).Text == Loc.T("home.railSearch");

        ShowIptv();
        await Task.Delay(800);
        IptvPanel w = _iptv!;
        r["defaultPanelWidth"] = Math.Round(IptvDock.ActualWidth);
        r["defaultCategoryWidth"] = Math.Round(w.CategoryColumnWidth);
        double videoBefore = VideoArea.ActualWidth;

        w.DragEdgeForTests(+120);
        await Task.Delay(400);
        r["widerPanelWidth"] = Math.Round(IptvDock.ActualWidth);
        r["videoShrank"] = Math.Round(videoBefore - VideoArea.ActualWidth);
        w.DragEdgeForTests(-60);
        await Task.Delay(300);
        r["narrowerPanelWidth"] = Math.Round(IptvDock.ActualWidth);

        // Granice: ne uže od 380 i ne šire nego da video ostane upotrebljiv.
        w.DragEdgeForTests(-2000);
        await Task.Delay(300);
        r["minPanelWidth"] = Math.Round(IptvDock.ActualWidth);
        w.DragEdgeForTests(+5000);
        await Task.Delay(300);
        r["maxPanelWidth"] = Math.Round(IptvDock.ActualWidth);
        r["videoStillUsable"] = VideoArea.ActualWidth >= 300;
        w.DragEdgeForTests(-5000);
        await Task.Delay(300);
        w.DragEdgeForTests(+170);
        await Task.Delay(300);
        double panelSet = Math.Round(IptvDock.ActualWidth);
        r["panelSet"] = panelSet;

        if (Interop.NativeMethods.NoMouse)
        {
            r["realDragSkipped"] = "no-mouse mode";
        }
        else
        {
            // Pravi miš: povuci razdjelnik kategorija udesno (+50) i hvatište na rubu panela ulijevo (-40).
            double cat0 = w.CategoryColumnWidth;
            Point sp = w.CategorySplitterScreenPoint;
            Interop.NativeMethods.SetCursorPos((int)sp.X, (int)sp.Y);
            await Task.Delay(200);
            Interop.NativeMethods.MouseButton(true);
            for (int i = 1; i <= 10; i++)
            {
                Interop.NativeMethods.SetCursorPos((int)sp.X + i * 5, (int)sp.Y);
                await Task.Delay(30);
            }

            Interop.NativeMethods.MouseButton(false);
            await Task.Delay(300);
            r["realDragCategories"] = Math.Round(w.CategoryColumnWidth - cat0);

            double dock0 = IptvDock.ActualWidth;
            Point gp = w.EdgeGripScreenPoint;
            Interop.NativeMethods.SetCursorPos((int)gp.X, (int)gp.Y);
            await Task.Delay(200);
            Interop.NativeMethods.MouseButton(true);
            for (int i = 1; i <= 8; i++)
            {
                Interop.NativeMethods.SetCursorPos((int)gp.X - i * 5, (int)gp.Y);
                await Task.Delay(30);
            }

            Interop.NativeMethods.MouseButton(false);
            await Task.Delay(300);
            r["realDragPanel"] = Math.Round(IptvDock.ActualWidth - dock0);

            // Glatkoća: lijevo/desno u 40 sitnih koraka od po 3 px; širina panela mora pratiti miš bez kašnjenja i bez trzaja.
            double start = IptvDock.ActualWidth;
            Point gp2 = w.EdgeGripScreenPoint;
            Interop.NativeMethods.SetCursorPos((int)gp2.X, (int)gp2.Y);
            await Task.Delay(200);
            Interop.NativeMethods.MouseButton(true);
            var errors = new List<double>();
            var steps = new List<double>();
            double prev = start;
            for (int i = 1; i <= 40; i++)
            {
                Interop.NativeMethods.SetCursorPos((int)gp2.X + i * 3, (int)gp2.Y);
                await Task.Delay(24);
                double now = IptvDock.ActualWidth;
                errors.Add(Math.Abs(now - Math.Min(start + i * 3, 940)));
                steps.Add(now - prev);
                prev = now;
            }

            Interop.NativeMethods.MouseButton(false);
            await Task.Delay(300);
            r["smoothMaxLag"] = Math.Round(errors.Max(), 1);
            r["smoothAvgLag"] = Math.Round(errors.Average(), 1);
            r["smoothMaxStep"] = Math.Round(steps.Max(), 1);
            r["smoothMinStep"] = Math.Round(steps.Min(), 1);
            r["smoothFinal"] = Math.Round(IptvDock.ActualWidth - start);
            // Vrati širinu da ostatak testa ostane isti.
            w.DragEdgeForTests(start - IptvDock.ActualWidth);
            await Task.Delay(300);
        }

        panelSet = Math.Round(IptvDock.ActualWidth);
        r["panelSet"] = panelSet;

        w.ResizeCategoriesForTests(210);
        await Task.Delay(300);
        r["categoryWidthSet"] = Math.Round(w.CategoryColumnWidth);
        r["shotResized"] = SaveWindowPng("iptv_resized", this);

        // Pamti se: novi panel (kao nakon ponovnog pokretanja) čita širine iz iptv.json.
        HideIptv();
        IptvDock.Child = null;
        _iptv = null;
        ShowIptv();
        await Task.Delay(800);
        r["restoredPanelWidth"] = Math.Round(IptvDock.ActualWidth);
        r["restoredCategoryWidth"] = Math.Round(_iptv!.CategoryColumnWidth);
        r["panelWidthRestored"] = Math.Abs(IptvDock.ActualWidth - panelSet) < 3;
        r["categoryWidthRestored"] = Math.Abs(_iptv.CategoryColumnWidth - 210) < 3;
        return r;
    }
}

public partial class MainWindow
{
    /// <summary>Dijagnostika: koliko se sličica iscrta dok se traka širi/sužava i koliki je najduži razmak među njima (ms).</summary>
    private async Task<(int Frames, double MaxGapMs, double AvgGapMs)> MeasureRailFramesAsync(bool expand)
    {
        var stamps = new List<double>();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        EventHandler handler = (_, _) => stamps.Add(sw.Elapsed.TotalMilliseconds);
        System.Windows.Media.CompositionTarget.Rendering += handler;
        SetRailExpanded(expand);
        await Task.Delay(450);
        System.Windows.Media.CompositionTarget.Rendering -= handler;
        // Samo prvih 300 ms (trajanje animacije).
        var inAnim = stamps.Where(t => t <= 300).ToList();
        var gaps = new List<double>();
        for (int i = 1; i < inAnim.Count; i++)
        {
            gaps.Add(inAnim[i] - inAnim[i - 1]);
        }

        return (inAnim.Count, gaps.Count == 0 ? 0 : gaps.Max(), gaps.Count == 0 ? 0 : gaps.Average());
    }
}

public partial class MainWindow
{
    /// <summary>
    /// Glatkoća povlačenja desnog ruba IPTV panela na velikom prozoru uz video (<c>--diag-ui-test iptvdrag</c>, kao na korisnikovom zaslonu):
    /// razmaci među iscrtanim sličicama i kašnjenje širine za mišem, na početnom ekranu i uz sam video.
    /// </summary>
    private async Task<Dictionary<string, object?>> IptvDragUiTestAsync()
    {
        var r = new Dictionary<string, object?>();
        if (Interop.NativeMethods.NoMouse)
        {
            r["skipped"] = "needs the mouse (no-mouse mode)";
            return r;
        }

        await WaitForAsync(() => _vm.HasMedia && !_vm.IsLoading && _vm.Position > 0.5, 20000);
        _player?.SetPropertyAsync("mute", "yes");
        WindowState = WindowState.Maximized;
        await Task.Delay(1500);
        r["window"] = $"{Math.Round(ActualWidth)}x{Math.Round(ActualHeight)}";
        r["video"] = _vm.CurrentPath is { } cp ? System.IO.Path.GetFileName(cp) : null;

        var store = new IptvStore(AppPaths.DataDir);
        store.SaveSettings(new IptvSettings { PanelWidth = 520 });

        foreach (bool home in new[] { true, false })
        {
            if (home) { GoHome(); } else { LeaveHomeView(); }
            if (!IsIptvVisible) { ShowIptv(); }
            await Task.Delay(1200);
            IptvPanel w = _iptv!;
            w.DragEdgeForTests(520 - IptvDock.ActualWidth);
            await Task.Delay(500);
            double start = IptvDock.ActualWidth;
            Point gp = w.EdgeGripScreenPoint;
            Interop.NativeMethods.SetCursorPos((int)gp.X, (int)gp.Y);
            await Task.Delay(300);
            var stamps = new List<double>();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            EventHandler handler = (_, _) => stamps.Add(sw.Elapsed.TotalMilliseconds);
            System.Windows.Media.CompositionTarget.Rendering += handler;
            Interop.NativeMethods.MouseButton(true);
            var lag = new List<double>();
            double dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this).DpiScaleX;
            for (int i = 1; i <= 70; i++)
            {
                int dx = (int)Math.Round(i * 6 * dpi);
                Interop.NativeMethods.SetCursorPos((int)gp.X + dx, (int)gp.Y);
                await Task.Delay(16);
                lag.Add(Math.Abs(IptvDock.ActualWidth - Math.Min(start + i * 6, 1000)));
            }

            Interop.NativeMethods.MouseButton(false);
            await Task.Delay(300);
            System.Windows.Media.CompositionTarget.Rendering -= handler;
            var gaps = new List<double>();
            for (int i = 1; i < stamps.Count; i++) { gaps.Add(stamps[i] - stamps[i - 1]); }
            gaps.Sort();
            string k = home ? "home" : "video";
            r[k + "_frames"] = stamps.Count;
            r[k + "_avgGapMs"] = Math.Round(gaps.Average(), 1);
            r[k + "_p95GapMs"] = Math.Round(gaps[(int)(gaps.Count * 0.95)], 1);
            r[k + "_maxGapMs"] = Math.Round(gaps.Max(), 1);
            r[k + "_gapsOver33ms"] = gaps.Count(g => g > 33);
            r[k + "_maxLagPx"] = Math.Round(lag.Max(), 1);
            r[k + "_avgLagPx"] = Math.Round(lag.Average(), 1);
            r[k + "_widened"] = Math.Round(IptvDock.ActualWidth - start);
            w.DragEdgeForTests(520 - IptvDock.ActualWidth);
            await Task.Delay(500);
        }

        return r;
    }
}

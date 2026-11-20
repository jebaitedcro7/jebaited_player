using System.IO;
using System.Windows;
using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.Views;

namespace JebaitedPlayer;

/// <summary>
/// Dijagnostički testovi ciklusa 5 u pravom playeru:
/// --diag-ui-test homebg (N4, uz JEBAITED_TEST_IMAGES = mapa testnih slika i JEBAITED_DATA_DIR).
/// </summary>
public partial class MainWindow
{
    /// <summary>Gumb iz predloška stavke kartice „Sljedeće za gledanje” (klik kao mišem).</summary>
    private bool ClickWatchNext(int index, string button)
    {
        WatchNextList.UpdateLayout();
        if (WatchNextList.ItemContainerGenerator.ContainerFromIndex(index) is System.Windows.Controls.ContentPresenter cp &&
            cp.ContentTemplate.FindName(button, cp) is System.Windows.Controls.Button b && b.IsEnabled)
        {
            // Gumbi kartice rade preko naredbi (Command): RaiseEvent(Click) ih ne izvršava, a „Invoke” iz UI Automationa
            // ide istim putem kao klik mišem (ButtonBase.OnClick).
            var peer = new System.Windows.Automation.Peers.ButtonAutomationPeer(b);
            (peer.GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke) as System.Windows.Automation.Provider.IInvokeProvider)!.Invoke();
            return true;
        }

        return false;
    }

    private string[] WatchNextTitles() => _vm.Library.WatchNext.Select(w => w.Title + (w.IsAuto ? " [auto]" : " [manual]")).ToArray();

    /// <summary>
    /// N2 u pravom playeru (testna baza): pogledana epizoda → sljedeća na Home kartici; klik je pokreće; nakon gledanja
    /// lista ide dalje; ručno dodan film; ▲ ▼; × uklanja i stavka se ne vraća.
    /// </summary>
    private async Task<Dictionary<string, object?>> WatchNextUiTestAsync()
    {
        var r = new Dictionary<string, object?>();
        await WaitForAsync(() => _vm.HasMedia && !_vm.IsLoading && _vm.Position > 1, 10000);
        string e1 = _vm.CurrentPath!;
        string season1 = Path.GetDirectoryName(e1)!;
        string film = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(season1)!)!, "Filmovi", "Film.Za.Test.2026.1080p.BluRay.x264-GRP.mp4");

        // 1) E01 pogledana → Home: sljedeća je E02.
        _store!.SetWatched(e1, true);
        GoHome();
        await WaitForAsync(() => _vm.Library.HasWatchNext, 8000);
        await Task.Delay(400);
        r["afterE01"] = new Dictionary<string, object?>
        {
            ["items"] = WatchNextTitles(), ["cardVisible"] = WatchNextCard.IsVisible, ["first"] = _vm.Library.WatchNext.FirstOrDefault()?.Path,
        };
        r["homeScreenshot"] = SaveWindowPng("home_watchnext");

        // 2) Klik na stavku pokreće E02; pogledana E02 → lista ide na E03.
        string? e2 = _vm.Library.WatchNext.FirstOrDefault()?.Path;
        r["clickedOpen"] = ClickWatchNext(0, "OpenButton");
        await WaitForAsync(() => _vm.HasMedia && !_vm.IsLoading && _vm.CurrentPath == e2 && _vm.Position > 0.5, 10000);
        r["opened"] = _vm.CurrentPath == e2;
        _store.SetWatched(e2!, true);
        GoHome();
        await WaitForAsync(() => _vm.Library.WatchNext.Count > 0 && _vm.Library.WatchNext[0].Path != e2, 8000);
        await Task.Delay(300);
        r["afterE02"] = WatchNextTitles();

        // 3) Ručno: film iz izbornika (trenutni video) → na kraju liste, oznaka „Dodano ručno”.
        OpenFiles([film]);
        await WaitForAsync(() => _vm.HasMedia && !_vm.IsLoading && _vm.CurrentPath == film && _vm.Position > 0.5, 10000);
        _vm.Library.AddToWatchNext(film);
        _vm.Library.AddToWatchNext(film);
        GoHome();
        await WaitForAsync(() => _vm.Library.WatchNext.Any(w => w.Path == film), 8000);
        await Task.Delay(300);
        r["withFilm"] = WatchNextTitles();

        // 4) ▲ na filmu (drugi) → prvi; ▼ na prvom → natrag.
        r["clickedUp"] = ClickWatchNext(1, "UpButton");
        await WaitForAsync(() => _vm.Library.WatchNext.FirstOrDefault()?.Path == film, 4000);
        r["afterUp"] = WatchNextTitles();
        r["upDisabledOnFirst"] = !ClickWatchNext(0, "UpButton");
        r["clickedDown"] = ClickWatchNext(0, "DownButton");
        await WaitForAsync(() => _vm.Library.WatchNext.LastOrDefault()?.Path == film, 4000);
        r["afterDown"] = WatchNextTitles();

        // 5) × na automatskoj stavci: nestaje i ne vraća se nakon osvježavanja.
        string? autoPath = _vm.Library.WatchNext.FirstOrDefault(w => w.IsAuto)?.Path;
        int autoIndex = _vm.Library.WatchNext.ToList().FindIndex(w => w.IsAuto);
        r["clickedRemove"] = autoIndex >= 0 && ClickWatchNext(autoIndex, "RemoveButton");
        await WaitForAsync(() => _vm.Library.WatchNext.All(w => w.Path != autoPath), 4000);
        _vm.Library.RefreshRecent();
        await Task.Delay(1500);
        r["afterRemove"] = WatchNextTitles();
        r["removedStaysRemoved"] = _vm.Library.WatchNext.All(w => w.Path != autoPath);
        r["finalScreenshot"] = SaveWindowPng("home_watchnext_final");
        return r;
    }

    /// <summary>Simulirani Spotify Web API (lokalno, sa stanjem: play/pause, uređaj); bilježi naredbe.</summary>
    private sealed class FakeSpotifyServer : IDisposable
    {
        private readonly System.Net.Sockets.TcpListener _listener = new(System.Net.IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _cts = new();
        private volatile bool _playing = true;

        public FakeSpotifyServer()
        {
            _listener.Start();
            _ = Task.Run(LoopAsync);
        }

        public Uri Base => new($"http://127.0.0.1:{((System.Net.IPEndPoint)_listener.LocalEndpoint).Port}/");

        public System.Collections.Concurrent.ConcurrentQueue<string> Commands { get; } = new();

        public bool NoDeviceForNext { get; set; }

        private (int, string) Route(string method, string path, string body)
        {
            if (method != "GET")
            {
                Commands.Enqueue($"{method} {path} {body}".Trim());
            }

            return (method, path) switch
            {
                ("GET", _) when path.StartsWith("/v1/me/player?", StringComparison.Ordinal) => (200,
                    "{\"is_playing\":" + (_playing ? "true" : "false") + ",\"progress_ms\":61000,\"device\":{\"id\":\"d1\",\"name\":\"Test PC\",\"volume_percent\":40}," +
                    "\"item\":{\"name\":\"Testna pjesma\",\"uri\":\"spotify:track:t1\",\"duration_ms\":200000,\"external_urls\":{\"spotify\":\"https://open.spotify.com/track/t1\"}," +
                    "\"artists\":[{\"name\":\"Test artist\"}],\"album\":{\"name\":\"A\",\"images\":[]}}}"),
                ("GET", "/v1/me/playlists?limit=50") => (200,
                    "{\"items\":[{\"name\":\"Lista jedan\",\"uri\":\"spotify:playlist:p1\",\"owner\":{\"display_name\":\"ja\"}},{\"name\":\"Lista dva\",\"uri\":\"spotify:playlist:p2\",\"owner\":{\"display_name\":\"ja\"}}]}"),
                ("PUT", _) when path.StartsWith("/v1/me/player/pause", StringComparison.Ordinal) => Set(false),
                ("PUT", _) when path.StartsWith("/v1/me/player/play", StringComparison.Ordinal) => Set(true),
                ("POST", "/v1/me/player/next") when NoDeviceForNext => (404, "{\"error\":{\"status\":404,\"reason\":\"NO_ACTIVE_DEVICE\"}}"),
                _ => (204, string.Empty),
            };
        }

        private (int, string) Set(bool playing)
        {
            _playing = playing;
            return (204, string.Empty);
        }

        private async Task LoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                System.Net.Sockets.TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(_cts.Token);
                }
                catch (Exception ex) when (ex is OperationCanceledException or System.Net.Sockets.SocketException or ObjectDisposedException)
                {
                    return;
                }

                _ = Task.Run(() => ServeAsync(client));
            }
        }

        private async Task ServeAsync(System.Net.Sockets.TcpClient client)
        {
            using (client)
            {
                try
                {
                    System.Net.Sockets.NetworkStream s = client.GetStream();
                    var raw = new System.Text.StringBuilder();
                    var buf = new byte[8192];
                    while (true)
                    {
                        int n = await s.ReadAsync(buf, _cts.Token);
                        if (n == 0)
                        {
                            return;
                        }

                        raw.Append(System.Text.Encoding.UTF8.GetString(buf, 0, n));
                        string text = raw.ToString();
                        int end = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                        if (end < 0)
                        {
                            continue;
                        }

                        int length = text[..end].Split("\r\n").Where(l => l.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                            .Select(l => int.Parse(l[15..].Trim(), System.Globalization.CultureInfo.InvariantCulture)).FirstOrDefault();
                        if (text.Length - end - 4 < length)
                        {
                            continue;
                        }

                        string[] first = text.Split("\r\n")[0].Split(' ');
                        (int status, string json) = Route(first[0], first[1], text[(end + 4)..]);
                        byte[] payload = System.Text.Encoding.UTF8.GetBytes(json);
                        await s.WriteAsync(System.Text.Encoding.ASCII.GetBytes(
                            $"HTTP/1.1 {status} X\r\nContent-Type: application/json\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n"), _cts.Token);
                        await s.WriteAsync(payload, _cts.Token);
                        return;
                    }
                }
                catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
                {
                }
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Stop();
        }
    }

    /// <summary>
    /// V8 u pravom playeru (uz JEBAITED_DATA_DIR): vodič za Client ID, pa simulirani Spotify: trenutna pjesma, playliste,
    /// pauza i pokretanje liste, mini player kao Spotify daljinski, greška „nema uređaja”, lokalna datoteka vraća mini player.
    /// </summary>
    private async Task<Dictionary<string, object?>> SpotifyUiTestAsync()
    {
        var r = new Dictionary<string, object?>();
        if (Environment.GetEnvironmentVariable("JEBAITED_DATA_DIR") is not { Length: > 0 })
        {
            r["error"] = "JEBAITED_DATA_DIR is required (never your real data)";
            return r;
        }

        await WaitForAsync(() => _vm.HasMedia && !_vm.IsLoading && _vm.Position > 1, 10000);
        string music = _vm.CurrentPath!;
        _player?.SetPropertyAsync("mute", "yes");
        _diagAutoSwitch = true;
        ShowMiniPlayer();
        await Task.Delay(600);
        MiniPlayerWindow mini = _mini!;

        // 1) Bez Client ID-a: vodič; kriv ID se odbija, ispravan se sprema.
        ShowSpotify(mini);
        await Task.Delay(700);
        SpotifyWindow w = _spotifyWindow!;
        r["setup"] = new Dictionary<string, object?>
        {
            ["setupVisible"] = w.SetupPanel.IsVisible, ["redirect"] = w.RedirectBox.Text, ["nowVisible"] = w.NowPanel.IsVisible,
        };
        r["setupScreenshot"] = SaveWindowPng("spotify_setup", w);
        w.SaveClientId("kriv id");
        r["badIdStatus"] = w.StatusText.Text;
        w.SaveClientId("0123456789abcdef0123456789abcdef");
        await Task.Delay(300);
        r["afterId"] = new Dictionary<string, object?>
        {
            ["setupVisible"] = w.SetupPanel.IsVisible, ["signInVisible"] = w.SignInButton.IsVisible, ["account"] = w.AccountText.Text,
            ["saved"] = _settings.SpotifyClientId,
        };

        // 2) Simulirani Spotify: trenutna pjesma i playliste.
        using var fake = new FakeSpotifyServer();
        Spotify.UseTestServer(fake.Base, "test-token");
        w.Render();
        await Spotify.RefreshStateAsync();
        await w.ShowListAsync("playlists");
        await Task.Delay(300);
        r["now"] = new Dictionary<string, object?>
        {
            ["track"] = w.TrackText.Text, ["artist"] = w.ArtistText.Text, ["device"] = w.DeviceText.Text, ["playGlyph"] = w.PlayPauseButton.Content,
            ["items"] = w.Items.Select(i => i.Name).ToArray(), ["nowVisible"] = w.NowPanel.IsVisible,
        };
        r["windowScreenshot"] = SaveWindowPng("spotify_window", w);

        // 3) Pauza iz prozora → naredba Spotifyju; mini player postaje Spotify daljinski.
        ClickButton(w.PlayPauseButton);
        await WaitForAsync(() => fake.Commands.Any(c => c.Contains("/me/player/pause", StringComparison.Ordinal)) && Spotify.State is { IsPlaying: false }, 5000);
        mini.Tick();
        r["paused"] = new Dictionary<string, object?>
        {
            ["commands"] = fake.Commands.ToArray(), ["active"] = Spotify.IsActiveSource, ["source"] = mini.SourceText.Text,
            ["marquee"] = mini.MarqueeTextValue, ["localPaused"] = _vm.IsPaused,
        };
        r["miniScreenshot"] = SaveWindowPng("mini_spotify", mini);

        // 4) Play u mini playeru ide Spotifyju; ▶ na playlisti pokreće listu.
        ClickButton(mini.PlayButton);
        await WaitForAsync(() => Spotify.State is { IsPlaying: true }, 5000);
        w.ItemList.UpdateLayout();
        if (w.ItemList.ItemContainerGenerator.ContainerFromIndex(1) is System.Windows.Controls.ContentPresenter cp &&
            cp.ContentTemplate.FindName("PlayButton", cp) is System.Windows.Controls.Button play)
        {
            ClickButton(play);
        }

        await WaitForAsync(() => fake.Commands.Any(c => c.Contains("spotify:playlist:p2", StringComparison.Ordinal)), 5000);
        r["played"] = new Dictionary<string, object?> { ["commands"] = fake.Commands.ToArray(), ["playing"] = Spotify.State?.IsPlaying };

        // 5) Greška: nema aktivnog uređaja → poruka u prozoru.
        fake.NoDeviceForNext = true;
        ClickButton(w.NextButton);
        await WaitForAsync(() => Spotify.Status.Length > 0, 5000);
        w.Render();
        r["noDevice"] = w.StatusText.Text;

        // 6) Druga lokalna datoteka: Spotify više nije izvor, mini player opet svira datoteku.
        string other = Path.Combine(Path.GetDirectoryName(music)!, "audio_only.flac");
        OpenFiles([other]);
        await WaitForAsync(() => _vm.HasMedia && !_vm.IsLoading && _vm.CurrentPath == other && _vm.Position > 0.5, 10000);
        mini.Tick();
        r["local"] = new Dictionary<string, object?> { ["active"] = Spotify.IsActiveSource, ["source"] = mini.SourceText.Text };
        w.Close();
        return r;
    }

    /// <summary>N4: slika pozadine na raznim veličinama i omjerima, sva tri načina, nepostojeća slika, postavke.</summary>
    private async Task<Dictionary<string, object?>> HomeBackgroundUiTestAsync()
    {
        var r = new Dictionary<string, object?>();
        if (Environment.GetEnvironmentVariable("JEBAITED_TEST_IMAGES") is not { Length: > 0 } dir ||
            Environment.GetEnvironmentVariable("JEBAITED_DATA_DIR") is not { Length: > 0 })
        {
            r["error"] = "JEBAITED_TEST_IMAGES and JEBAITED_DATA_DIR are required";
            return r;
        }

        // Pokretanje s pozadinom iz settings.json (skripta je upisala veliku sliku).
        await WaitForAsync(() => LastHomeBackground.Path is not null, 8000);
        r["start"] = new Dictionary<string, object?>
        {
            ["shown"] = HomeBackgroundImage.Visibility == Visibility.Visible, ["space"] = HomeSpace.Visibility == Visibility.Visible,
            ["ms"] = LastHomeBackground.Milliseconds, ["pixelWidth"] = (HomeBackgroundImage.Source as System.Windows.Media.Imaging.BitmapSource)?.PixelWidth,
        };
        double dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this).DpiScaleX;
        r["screenPx"] = (int)Math.Ceiling(Math.Max(SystemParameters.PrimaryScreenWidth, ActualWidth) * dpi);

        var cases = new List<Dictionary<string, object?>>();
        foreach (string name in new[] { "wide-3840x2160.jpg", "tall-800x1200.png", "small-300x200.png" })
        {
            foreach (string mode in AppSettings.HomeBackgroundModes)
            {
                _settings.HomeBackgroundImage = Path.Combine(dir, name);
                _settings.HomeBackgroundMode = mode;
                await ApplyHomeBackgroundAsync();
                await Task.Delay(150);
                var source = HomeBackgroundImage.Source as System.Windows.Media.Imaging.BitmapSource;
                cases.Add(new Dictionary<string, object?>
                {
                    ["image"] = name, ["mode"] = mode, ["shown"] = HomeBackgroundImage.Visibility == Visibility.Visible,
                    ["space"] = HomeSpace.Visibility == Visibility.Visible, ["pixelWidth"] = source?.PixelWidth, ["ms"] = LastHomeBackground.Milliseconds,
                    ["stretch"] = HomeBackgroundImage.Stretch.ToString(), ["width"] = Math.Round(HomeBackgroundImage.ActualWidth),
                    ["height"] = Math.Round(HomeBackgroundImage.ActualHeight), ["panelWidth"] = Math.Round(WelcomePanel.ActualWidth),
                    ["panelHeight"] = Math.Round(WelcomePanel.ActualHeight),
                });
                if ((name, mode) is ("wide-3840x2160.jpg", "fill") or ("tall-800x1200.png", "fit") or ("small-300x200.png", "center"))
                {
                    r["shot-" + Path.GetFileNameWithoutExtension(name) + "-" + mode] = SaveWindowPng($"home_bg_{Path.GetFileNameWithoutExtension(name)}_{mode}");
                }
            }
        }

        r["cases"] = cases;

        // Nepostojeća slika i datoteka koja nije slika: svemir i obavijest.
        _vm.Notice = string.Empty;
        _settings.HomeBackgroundImage = Path.Combine(dir, "nema-je.jpg");
        await ApplyHomeBackgroundAsync();
        r["missing"] = new Dictionary<string, object?>
        {
            ["space"] = HomeSpace.Visibility == Visibility.Visible, ["shown"] = HomeBackgroundImage.Visibility == Visibility.Visible,
            ["notice"] = _vm.Notice, ["settingKept"] = _settings.HomeBackgroundImage is not null,
        };
        _settings.HomeBackgroundImage = Path.Combine(dir, "not-an-image.jpg");
        await ApplyHomeBackgroundAsync();
        r["notImage"] = new Dictionary<string, object?> { ["space"] = HomeSpace.Visibility == Visibility.Visible };

        // Postavke → Izgled: odabir, pregled, Zadana, Odbaci vraća sliku.
        _settings.HomeBackgroundImage = null;
        _settings.HomeBackgroundMode = "fill";
        await ApplyHomeBackgroundAsync();
        ShowPlayerSettings(SettingsTab.Appearance);
        await Task.Delay(700);
        PlayerSettingsWindow w = _playerSettingsWindow!;
        r["settingsDefaultChecked"] = w.HomeBgDefault.IsChecked;
        w.SetHomeBackground(Path.Combine(dir, "wide-3840x2160.jpg"));
        await WaitForAsync(() => w.HomeBgPreview.Source is not null && LastHomeBackground.Shown, 5000);
        r["settings"] = new Dictionary<string, object?>
        {
            ["customChecked"] = w.HomeBgCustom.IsChecked, ["preview"] = w.HomeBgPreview.Source is not null, ["file"] = w.HomeBgFileText.Text,
            ["homeShown"] = HomeBackgroundImage.Visibility == Visibility.Visible, ["dirty"] = w.IsDirty, ["modesEnabled"] = w.HomeBgFit.IsEnabled,
        };
        w.Tabs.UpdateLayout();
        r["settingsScreenshot"] = SaveWindowPng("settings_home_bg", w);
        w.SetHomeBackgroundMode("fit");
        await Task.Delay(300);
        r["modeFromSettings"] = HomeBackgroundImage.Stretch.ToString();
        w.DiscardChanges();
        await Task.Delay(1200);
        r["discard"] = new Dictionary<string, object?>
        {
            ["image"] = _settings.HomeBackgroundImage, ["mode"] = _settings.HomeBackgroundMode,
            ["space"] = HomeSpace.Visibility == Visibility.Visible,
        };
        _playerSettingsWindow?.Close();
        return r;
    }
}

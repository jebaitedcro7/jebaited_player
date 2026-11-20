using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.ViewModels;
using JebaitedPlayer.Views;

namespace JebaitedPlayer;

/// <summary>
/// Dijagnostički testovi ciklusa 4 u pravom playeru:
/// --diag-ui-test radiostop (R1, stvarna mreža: Stop / Pauza / Play / razmak na pravoj postaji, bez premotavanja) i
/// --diag-ui-test radiodrop (R1, bez mreže: lokalni testni stream koji prekine vezu; Stop kod obične datoteke).
/// </summary>
public partial class MainWindow
{
    private static void ClickButton(ButtonBase button) => button.RaiseEvent(new System.Windows.RoutedEventArgs(ButtonBase.ClickEvent));

    private string? MpvString(string name) => _player?.GetPropertyString(name);

    /// <summary>R1 na pravoj postaji: Stop zatvara vezu, a Play / razmak je ponovno spaja uživo (nova veza, ne cache).</summary>
    private async Task<Dictionary<string, object?>> RadioStopUiTestAsync()
    {
        var r = new Dictionary<string, object?>();
        int loads = 0;
        _vm.LoadStarting += (_, path) =>
        {
            if (PlayerViewModel.IsStreamPath(path))
            {
                loads++;
            }
        };

        // Bez datoteke, odmah mini player, radio kao prvo što se pusti (kao kod vlasnika).
        _diagAutoSwitch = true;
        ShowMiniPlayer();
        await Task.Delay(800);
        _player?.SetPropertyAsync("mute", "yes");
        ShowRadio(_mini);
        await Task.Delay(600);
        RadioWindow radio = _radio!;
        r["count"] = await radio.SearchAsync(null, "jazz", null);
        RadioRow? playing = null;
        int index = 0;
        foreach (RadioRow row in radio.Results.Take(6).ToList())
        {
            ClickRadioPlay(radio, index++);
            await WaitForAsync(() => _vm.HasMedia && !_vm.IsLoading && _vm.Position > 2 && _vm.CurrentPath == row.Url, 15000);
            if (_vm.HasMedia && _vm.Position > 2 && _vm.CurrentPath == row.Url)
            {
                playing = row;
                break;
            }
        }

        radio.Close();
        r["station"] = playing?.Name;
        MiniPlayerWindow? mini = _mini;
        if (playing is null || mini is null)
        {
            return r;
        }

        await WaitForAsync(() => _vm.Position > 8, 15000);
        r["cache"] = new Dictionary<string, object?> { ["maxBytes"] = MpvString("demuxer-max-bytes"), ["maxBackBytes"] = MpvString("demuxer-max-back-bytes") };

        // 1) Stop: veza se zatvara (mpv u mirovanju), postaja ostaje prikazana.
        double before = _vm.Position;
        int loadsBefore = loads;
        ClickButton(mini.StopButton);
        await Task.Delay(1500);
        mini.Tick();
        r["stop"] = new Dictionary<string, object?>
        {
            ["positionBefore"] = Math.Round(before, 1), ["hasMedia"] = _vm.HasMedia, ["stopped"] = _vm.StoppedStream?.Title,
            ["idle"] = MpvString("idle-active"), ["remaining"] = mini.RemainingText.Text, ["marquee"] = mini.MarqueeTextValue,
            ["source"] = mini.SourceText.Text, ["compact"] = mini.CompactTime.Text,
        };
        r["stopScreenshot"] = SaveWindowPng("mini_radio_stopped", mini);

        // 2) Nakon pauze od nekoliko sekundi Play spaja postaju ponovno (nova veza).
        await Task.Delay(6000);
        ClickButton(mini.PlayButton);
        await WaitForAsync(() => _vm.HasMedia && !_vm.IsLoading && _vm.Position > 1, 15000);
        mini.Tick();
        r["play"] = new Dictionary<string, object?>
        {
            ["loads"] = loads - loadsBefore, ["hasMedia"] = _vm.HasMedia, ["samePath"] = _vm.CurrentPath == playing.Url,
            ["position"] = Math.Round(_vm.Position, 1), ["paused"] = _vm.IsPaused, ["live"] = mini.RemainingText.Text,
            ["stoppedCleared"] = _vm.StoppedStream is null,
        };

        // 3) Pauza pa Play: ne nastavlja iz cachea, nego ponovno spaja.
        await WaitForAsync(() => _vm.Position > 4, 10000);
        loadsBefore = loads;
        ClickButton(mini.PauseButton);
        await Task.Delay(1000);
        r["pause"] = new Dictionary<string, object?> { ["paused"] = _vm.IsPaused, ["hasMedia"] = _vm.HasMedia };
        await Task.Delay(4000);
        ClickButton(mini.PlayButton);
        await WaitForAsync(() => !_vm.IsPaused && !_vm.IsLoading && _vm.Position > 1, 15000);
        r["pausePlay"] = new Dictionary<string, object?>
        {
            ["loads"] = loads - loadsBefore, ["paused"] = _vm.IsPaused, ["position"] = Math.Round(_vm.Position, 1),
        };

        // 4) Premotavanje se za radio ignorira (Home, strelica lijevo, izravni poziv).
        await WaitForAsync(() => _vm.Position > 5, 10000);
        double p0 = _vm.Position;
        ((IMiniPlayerHost)this).HandleShortcut(Key.Home, ModifierKeys.None);
        ((IMiniPlayerHost)this).HandleShortcut(Key.Left, ModifierKeys.None);
        _vm.SeekAbsolute(0);
        await Task.Delay(1500);
        r["seek"] = new Dictionary<string, object?> { ["before"] = Math.Round(p0, 1), ["after"] = Math.Round(_vm.Position, 1) };

        // 5) Razmak nakon Stopa ponovno spaja postaju.
        ClickButton(mini.StopButton);
        await Task.Delay(1000);
        loadsBefore = loads;
        bool handled = ((IMiniPlayerHost)this).HandleShortcut(Key.Space, ModifierKeys.None);
        await WaitForAsync(() => _vm.HasMedia && !_vm.IsLoading && _vm.Position > 1, 15000);
        r["space"] = new Dictionary<string, object?>
        {
            ["handled"] = handled, ["loads"] = loads - loadsBefore, ["hasMedia"] = _vm.HasMedia, ["position"] = Math.Round(_vm.Position, 1),
        };

        // 6) Home (puni Stop) briše zapamćenu postaju: razmak tada ništa ne pokreće.
        ClickButton(mini.StopButton);
        await Task.Delay(800);
        bool hadStopped = _vm.StoppedStream is not null;
        _vm.Stop();
        loadsBefore = loads;
        bool handledAfterHome = ((IMiniPlayerHost)this).HandleShortcut(Key.Space, ModifierKeys.None);
        await Task.Delay(1500);
        r["home"] = new Dictionary<string, object?>
        {
            ["hadStopped"] = hadStopped, ["cleared"] = _vm.StoppedStream is null, ["spaceHandled"] = handledAfterHome,
            ["loads"] = loads - loadsBefore, ["hasMedia"] = _vm.HasMedia,
        };
        return r;
    }

    /// <summary>
    /// R1 bez mreže. Najprije obična datoteka: Stop = pauza na početku, zadani cache. Zatim lokalni testni stream koji
    /// pošalje nekoliko sekundi zvuka i prekine vezu: na kraju streama Play spaja ponovno (prije: povratak na početak cachea).
    /// </summary>
    private async Task<Dictionary<string, object?>> RadioDropUiTestAsync()
    {
        var r = new Dictionary<string, object?>();
        await WaitForAsync(() => _vm.HasMedia && _vm.Duration > 0 && _vm.Position > 2, 10000);
        string file = _vm.CurrentPath!;
        _player?.SetPropertyAsync("mute", "yes");
        _diagAutoSwitch = true;
        ShowMiniPlayer();
        await Task.Delay(800);
        MiniPlayerWindow mini = _mini!;

        // 1) Regresija: Stop kod datoteke ostaje Winamp stop.
        double before = _vm.Position;
        ClickButton(mini.StopButton);
        await Task.Delay(1000);
        r["fileStop"] = new Dictionary<string, object?>
        {
            ["before"] = Math.Round(before, 1), ["position"] = Math.Round(_vm.Position, 2), ["paused"] = _vm.IsPaused,
            ["hasMedia"] = _vm.HasMedia, ["stopped"] = _vm.StoppedStream is not null, ["maxBytes"] = MpvString("demuxer-max-bytes"),
            ["maxBackBytes"] = MpvString("demuxer-max-back-bytes"),
        };

        // 2) Lokalni stream: nekoliko sekundi MP3 zvuka, pa prekid veze.
        byte[] audio = TestStreamServer.AudioWithoutTag(File.ReadAllBytes(file), 120_000);
        using var server = new TestStreamServer(audio);
        int loads = 0;
        _vm.LoadStarting += (_, path) =>
        {
            if (PlayerViewModel.IsStreamPath(path))
            {
                loads++;
            }
        };

        // Dijagnostika inače vrti datoteku u krug; stream mora stvarno završiti.
        _player?.SetPropertyAsync("loop-file", "no");
        PlayStream(server.Url, "Test radio");
        await WaitForAsync(() => _vm.HasMedia && !_vm.IsLoading && _vm.Position > 1, 15000);
        r["stream"] = new Dictionary<string, object?>
        {
            ["live"] = _vm.IsLiveStream, ["position"] = Math.Round(_vm.Position, 1), ["maxBytes"] = MpvString("demuxer-max-bytes"),
            ["maxBackBytes"] = MpvString("demuxer-max-back-bytes"), ["connections"] = server.Connections,
        };
        await WaitForAsync(() => _vm.EofReached, 20000);
        mini.Tick();
        r["ended"] = new Dictionary<string, object?>
        {
            ["eof"] = _vm.EofReached, ["paused"] = _vm.IsPaused, ["hasMedia"] = _vm.HasMedia, ["position"] = Math.Round(_vm.Position, 1),
        };

        // 3) Play na kraju streama: nova veza, ne povratak na početak cachea.
        int loadsBefore = loads;
        int connectionsBefore = server.Connections;
        ClickButton(mini.PlayButton);
        await WaitForAsync(() => server.Connections > connectionsBefore && _vm.HasMedia && !_vm.IsLoading && !_vm.EofReached && _vm.Position > 0.5, 15000);
        r["replay"] = new Dictionary<string, object?>
        {
            ["loads"] = loads - loadsBefore, ["connections"] = server.Connections - connectionsBefore, ["paused"] = _vm.IsPaused,
            ["eof"] = _vm.EofReached, ["position"] = Math.Round(_vm.Position, 1),
        };

        // 4) Natrag na datoteku: zadani cache.
        _vm.Library.PlayItem(_vm.Library.Playlist.First(p => string.Equals(p.Path, file, StringComparison.OrdinalIgnoreCase)));
        await WaitForAsync(() => _vm.HasMedia && !_vm.IsLoading && !_vm.IsLiveStream && _vm.Position > 0.5, 10000);
        r["backToFile"] = new Dictionary<string, object?>
        {
            ["path"] = _vm.CurrentPath == file, ["maxBytes"] = MpvString("demuxer-max-bytes"), ["maxBackBytes"] = MpvString("demuxer-max-back-bytes"),
        };
        return r;
    }

    /// <summary>
    /// M3a u pravom playeru (samo uz JEBAITED_DATA_DIR): skripta ostavi oštećen settings.json i automatsku kopiju.
    /// Player mora vratiti postavke iz kopije, sačuvati oštećenu datoteku i pri spremanju zapisati oznaku oblika.
    /// </summary>
    private async Task<Dictionary<string, object?>> Settings4UiTestAsync()
    {
        var r = new Dictionary<string, object?>();
        if (Environment.GetEnvironmentVariable("JEBAITED_DATA_DIR") is not { Length: > 0 })
        {
            r["error"] = "JEBAITED_DATA_DIR is required (never your real data)";
            return r;
        }

        await Task.Delay(500);
        SettingsLoadReport? report = _settings.LoadReport;
        string file = Path.Combine(AppPaths.DataDir, "settings.json");
        r["outcome"] = report?.Outcome.ToString();
        r["backupCreated"] = report?.BackupCreatedUtc?.ToString("o", System.Globalization.CultureInfo.InvariantCulture);
        r["corruptFile"] = report?.CorruptFile is { } corrupt ? Path.GetFileName(corrupt) : null;
        r["corruptContent"] = report?.CorruptFile is { } c && File.Exists(c) ? File.ReadAllText(c) : null;
        r["message"] = report?.Describe();
        r["volume"] = _settings.Volume;
        r["theme"] = _settings.Theme;
        r["appliedTheme"] = WindowTheme.Current;
        r["seek"] = _settings.SeekStepSeconds;
        r["fileRestored"] = File.Exists(file) ? File.ReadAllText(file) : null;

        _settings.Save();
        r["savedVersion"] = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file))?["SettingsVersion"]?.GetValue<int>();
        r["corruptFiles"] = Directory.GetFiles(AppPaths.DataDir, "settings.corrupt-*.json").Length;
        return r;
    }

    /// <summary>
    /// M3b u pravom playeru (samo uz JEBAITED_DATA_DIR): gumb Zatvori s nespremljenim promjenama pita
    /// Spremi / Odbaci / Odustani. Odgovor daje test (bez modalnog prozora), a pravi prozor se snimi zasebno.
    /// </summary>
    private async Task<Dictionary<string, object?>> UnsavedUiTestAsync()
    {
        var r = new Dictionary<string, object?>();
        if (Environment.GetEnvironmentVariable("JEBAITED_DATA_DIR") is not { Length: > 0 })
        {
            r["error"] = "JEBAITED_DATA_DIR is required (never your real data)";
            return r;
        }

        string file = Path.Combine(AppPaths.DataDir, "settings.json");
        string FileResume() => System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file))?["ResumeMode"]?.GetValue<string>() ?? string.Empty;
        string original = _settings.ResumeMode;

        // 1) Odustani: prozor ostaje otvoren, promjena ostaje nespremljena.
        ShowPlayerSettings(SettingsTab.General);
        await Task.Delay(700);
        PlayerSettingsWindow w = _playerSettingsWindow!;
        _settings.ResumeMode = "start";
        await Task.Delay(600);
        int asked = 0;
        w.AskUnsavedOverride = () =>
        {
            asked++;
            return UnsavedChoice.Cancel;
        };
        ClickButton(w.CloseButton);
        await Task.Delay(800);
        r["cancel"] = new Dictionary<string, object?>
        {
            ["asked"] = asked, ["open"] = w.IsVisible && ReferenceEquals(_playerSettingsWindow, w), ["dirty"] = w.IsDirty, ["resume"] = _settings.ResumeMode,
        };

        // 2) Odbaci: prozor se zatvara, postavka se vraća, datoteka ostaje kakva je bila.
        w.AskUnsavedOverride = () =>
        {
            asked++;
            return UnsavedChoice.Discard;
        };
        ClickButton(w.CloseButton);
        await Task.Delay(1000);
        r["discard"] = new Dictionary<string, object?>
        {
            ["asked"] = asked, ["closed"] = _playerSettingsWindow is null, ["resume"] = _settings.ResumeMode, ["original"] = original, ["file"] = FileResume(),
        };

        // 3) Spremi: prozor se zatvara, promjena je na disku.
        ShowPlayerSettings(SettingsTab.General);
        await Task.Delay(700);
        PlayerSettingsWindow w2 = _playerSettingsWindow!;
        _settings.ResumeMode = "resume";
        await Task.Delay(600);
        w2.AskUnsavedOverride = () =>
        {
            asked++;
            return UnsavedChoice.Save;
        };
        ClickButton(w2.CloseButton);
        await Task.Delay(1000);
        r["save"] = new Dictionary<string, object?>
        {
            ["asked"] = asked, ["closed"] = _playerSettingsWindow is null, ["resume"] = _settings.ResumeMode, ["file"] = FileResume(),
        };

        // 4) Bez promjena: zatvaranje ne pita.
        ShowPlayerSettings(SettingsTab.General);
        await Task.Delay(700);
        PlayerSettingsWindow w3 = _playerSettingsWindow!;
        int before = asked;
        w3.AskUnsavedOverride = () =>
        {
            asked++;
            return UnsavedChoice.Cancel;
        };
        ClickButton(w3.CloseButton);
        await Task.Delay(800);
        r["clean"] = new Dictionary<string, object?> { ["asked"] = asked - before, ["closed"] = _playerSettingsWindow is null };

        // 5) Pravi prozor s pitanjem (tekstovi, gumbi): snimka i klikovi kroz gumbe.
        ShowPlayerSettings(SettingsTab.General);
        await Task.Delay(700);
        PlayerSettingsWindow w4 = _playerSettingsWindow!;
        var dialog = new UnsavedChangesWindow { Owner = w4 };
        dialog.Show();
        await Task.Delay(700);
        r["dialogScreenshot"] = SaveWindowPng("unsaved_dialog", dialog);
        r["dialog"] = new Dictionary<string, object?>
        {
            ["title"] = dialog.Title, ["save"] = dialog.SaveChoice.Content, ["discard"] = dialog.DiscardChoice.Content,
            ["cancel"] = dialog.CancelChoice.Content, ["saveIsDefault"] = dialog.SaveChoice.IsDefault, ["cancelIsCancel"] = dialog.CancelChoice.IsCancel,
        };
        ClickButton(dialog.DiscardChoice);
        await Task.Delay(300);
        r["dialogChoice"] = dialog.Choice.ToString();
        r["dialogClosed"] = !dialog.IsVisible;
        w4.Close();
        return r;
    }

    /// <summary>
    /// V4 (priprema za Discord): glazba na zasebnom izlazu, film na glavnom. Uređaj je stvarni izlaz s ovog računala
    /// (prvi s popisa mpv-a); dijagnostika je utišana, a postavke se ne spremaju.
    /// </summary>
    private async Task<Dictionary<string, object?>> MusicDeviceUiTestAsync()
    {
        var r = new Dictionary<string, object?>();
        await WaitForAsync(() => _vm.HasMedia && !_vm.IsLoading && _vm.Position > 1, 10000);
        string music = _vm.CurrentPath!;
        IReadOnlyList<AudioDeviceInfo> devices = await ((Views.ISettingsHost)this).GetAudioDevicesAsync();
        AudioDeviceInfo? device = devices.FirstOrDefault(d => d.Name != "auto");
        r["devices"] = devices.Count;
        if (device is null)
        {
            return r;
        }

        r["device"] = device.Description;
        ShowPlayerSettings(SettingsTab.Audio);
        await Task.Delay(800);
        PlayerSettingsWindow w = _playerSettingsWindow!;
        r["buttonBefore"] = w.MusicAudioDeviceButton.Content?.ToString();
        w.SetMusicAudioDevice(device.Name);
        await Task.Delay(800);
        r["buttonAfter"] = w.MusicAudioDeviceButton.Content?.ToString();
        r["settingsScreenshot"] = SaveWindowPng("settings_music_output", w);
        r["musicPlaying"] = new Dictionary<string, object?> { ["musicMode"] = MpvConfig.MusicMode, ["mpv"] = MpvString("audio-device") };
        w.Close();

        // Film: glavni izlaz (zadani uređaj sustava).
        string video = Path.Combine(Path.GetDirectoryName(music)!, "1080p_h264_aac.mp4");
        OpenFiles([video]);
        await WaitForAsync(() => _vm.HasMedia && !_vm.IsLoading && _vm.CurrentPath == video && _vm.Position > 0.5, 10000);
        await Task.Delay(500);
        r["video"] = new Dictionary<string, object?> { ["musicMode"] = MpvConfig.MusicMode, ["mpv"] = MpvString("audio-device"), ["main"] = _settings.AudioDevice };

        // Natrag na glazbu: opet zaseban izlaz.
        OpenFiles([music]);
        await WaitForAsync(() => _vm.HasMedia && !_vm.IsLoading && _vm.CurrentPath == music && _vm.Position > 0.5, 10000);
        await Task.Delay(500);
        r["musicAgain"] = new Dictionary<string, object?> { ["musicMode"] = MpvConfig.MusicMode, ["mpv"] = MpvString("audio-device"), ["expected"] = device.Name };
        return r;
    }

    /// <summary>Najjednostavniji HTTP „radio”: na svaki zahtjev pošalje isti MP3 komad i zatvori vezu.</summary>
    private sealed class TestStreamServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _cts = new();
        private readonly byte[] _data;
        private int _connections;

        internal TestStreamServer(byte[] data)
        {
            _data = data;
            _listener.Start();
            _ = Task.Run(AcceptLoopAsync);
        }

        internal string Url => $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/radio.mp3";

        internal int Connections => Volatile.Read(ref _connections);

        /// <summary>MP3 bez ID3v2 oznake na početku, najviše <paramref name="max"/> bajtova zvuka.</summary>
        internal static byte[] AudioWithoutTag(byte[] mp3, int max)
        {
            int start = 0;
            if (mp3.Length > 10 && mp3[0] == 'I' && mp3[1] == 'D' && mp3[2] == '3')
            {
                start = 10 + ((mp3[6] & 0x7F) << 21 | (mp3[7] & 0x7F) << 14 | (mp3[8] & 0x7F) << 7 | (mp3[9] & 0x7F));
            }

            return mp3.AsSpan(start, Math.Min(max, mp3.Length - start)).ToArray();
        }

        private async Task AcceptLoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(_cts.Token);
                }
                catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
                {
                    return;
                }

                Interlocked.Increment(ref _connections);
                _ = Task.Run(() => ServeAsync(client));
            }
        }

        private async Task ServeAsync(TcpClient client)
        {
            using (client)
            {
                try
                {
                    NetworkStream stream = client.GetStream();
                    var request = new StringBuilder();
                    var buffer = new byte[4096];
                    while (!request.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
                    {
                        int n = await stream.ReadAsync(buffer, _cts.Token);
                        if (n == 0)
                        {
                            return;
                        }

                        request.Append(Encoding.ASCII.GetString(buffer, 0, n));
                    }

                    byte[] head = Encoding.ASCII.GetBytes("HTTP/1.0 200 OK\r\nContent-Type: audio/mpeg\r\nicy-name: Test radio\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(head, _cts.Token);
                    await stream.WriteAsync(_data, _cts.Token);
                }
                catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
                {
                    // Klijent je prekinuo vezu: u redu za test.
                }
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Stop();
            _cts.Dispose();
        }
    }
}

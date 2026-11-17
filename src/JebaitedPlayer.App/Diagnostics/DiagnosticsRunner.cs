using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using JebaitedPlayer.Controls;
using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.Mpv;
using JebaitedPlayer.ViewModels;

namespace JebaitedPlayer.Diagnostics;

/// <summary>
/// Dijagnostički način rada (<c>--diag N</c>): pokrene datoteku, pričeka da se reprodukcija stabilizira,
/// N sekundi mjeri CPU i RAM procesa, zapiše stanje mpv-a i aplikacije u JSON i zatvori aplikaciju.
/// Služi za ponovljiva mjerenja u svakom koraku razvoja.
/// </summary>
public sealed class DiagnosticsRunner(
    StartupOptions options,
    PlayerViewModel vm,
    VideoHost video,
    Func<MpvPlayer?> player,
    Action close,
    Func<System.Windows.Input.Key, System.Windows.Input.ModifierKeys, bool>? pressKey = null)
{
    private static readonly string[] MpvProperties =
    [
        "mpv-version", "ffmpeg-version", "current-vo", "current-gpu-context", "hwdec-current",
        "video-codec", "width", "height", "video-params/pixelformat", "video-params/hw-pixelformat",
        "video-params/gamma", "video-params/primaries", "video-params/sig-peak",
        "video-target-params/gamma", "video-target-params/primaries",
        "container-fps", "estimated-vf-fps", "display-fps",
        "frame-drop-count", "decoder-frame-drop-count", "vo-delayed-frame-count",
        "audio-codec-name", "audio-params/channel-count", "osd-dimensions/w", "osd-dimensions/h",
        "sid", "current-tracks/sub/lang", "current-tracks/sub/title", "track-list/count",
    ];

    /// <summary>Datoteke primljene iz drugih pokretanja (test jedne instance), po zahtjevu.</summary>
    public List<string[]> ExternalOpens { get; } = [];

    /// <summary>Test nadogradnje: provjeri i preuzmi iz zadane mape, vrati stanje za JSON.</summary>
    public Func<Task<Dictionary<string, object?>>>? UpdateProbe { get; set; }

    private readonly Stopwatch _loadWatch = new();
    private double? _startupMs;
    private double? _mpvInitMs;
    private double? _firstFrameMs;
    private bool _started;

    public void OnPlayerCreated(double elapsedMs) => _mpvInitMs = Math.Round(elapsedMs);

    public void OnContentRendered()
    {
        _startupMs = Math.Round((DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds);
        if (options.Files.Count == 0)
        {
            Start(TimeSpan.FromSeconds(3), options.DiagSeconds ?? 10);
        }
    }

    public void OnLoadRequested() => _loadWatch.Restart();

    public void OnPlaybackRestart()
    {
        if (_firstFrameMs is null && _loadWatch.IsRunning)
        {
            _firstFrameMs = Math.Round(_loadWatch.Elapsed.TotalMilliseconds);
            Start(options.DiagWarmupMs is int ms ? TimeSpan.FromMilliseconds(ms) : TimeSpan.FromSeconds(5), options.DiagSeconds ?? 10);
        }
    }

    public void OnLoadFailed() => Start(TimeSpan.FromMilliseconds(500), 0);

    /// <summary>
    /// Test titlova: pričeka da se titlovi učitaju, primijeni zadanu sinkronizaciju, skoči na zadanu sekundu,
    /// pauzira i pročita prikazani titl (mpv "sub-text") te stanje sinkronizacije.
    /// </summary>
    private async Task<Dictionary<string, object?>> ProbeSubtitlesAsync(double seconds)
    {
        SubtitleController subs = vm.Subtitles;
        for (int i = 0; i < 50 && subs.SelectedTrack is null; i++)
        {
            await Task.Delay(100);
        }

        // M5: pričekaj automatsku sinkronizaciju prema govoru (i provjeru titlova s interneta).
        var syncWait = Stopwatch.StartNew();
        if (options.DiagWaitSync && vm.AutoSync is { } auto)
        {
            while (syncWait.Elapsed < TimeSpan.FromSeconds(90) && (auto.CompletedRuns < options.DiagWaitSyncRuns || auto.IsBusy))
            {
                await Task.Delay(100);
            }
        }

        double syncWaitMs = syncWait.Elapsed.TotalMilliseconds;

        // Poravnanje po replici kao korisnik: premotaj na trenutak kad se replika čuje, pauziraj, "ova replika počinje sada".
        if (options.DiagAnchors is { } anchors && player() is { } ap)
        {
            foreach (string pair in anchors.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                double[] v = pair.Split(':').Select(x => double.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                ap.CommandAsync("seek", v[1].ToString("R", System.Globalization.CultureInfo.InvariantCulture), "absolute+exact");
                ap.SetPropertyAsync("pause", true);
                await Task.Delay(1200);
                subs.AddAnchor(new CueRow(v[0] * 1000, v[1], string.Empty, "dijagnostika", false));
                await Task.Delay(1200);
            }
        }

        if (options.DiagSyncPoints is { } points)
        {
            double[] v = points.Split(';', ':').Select(s => double.Parse(s, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            subs.ApplyTwoPoints(v[0], v[1], v[2], v[3]);
            subs.PersistState();
        }
        else if (options.DiagSubDelayMs.HasValue || options.DiagSubSpeed.HasValue)
        {
            subs.SetTiming(new JebaitedPlayer.Subtitles.SubtitleTiming(options.DiagSubSpeed ?? 1.0, options.DiagSubDelayMs ?? 0), null);
            subs.PersistState();
        }

        MpvPlayer? p = player();
        var probe = new Dictionary<string, object?>();
        if (p is null)
        {
            return probe;
        }

        p.CommandAsync("seek", seconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture), "absolute+exact");
        p.SetPropertyAsync("pause", true);
        await Task.Delay(1500);

        Dictionary<string, string?> values = await Task.Run(() => new[]
            {
                "sub-text", "sub-start", "sub-end", "time-pos", "sub-delay", "sub-speed", "sid",
                "current-tracks/sub/lang", "current-tracks/sub/codec", "current-tracks/sub/external-filename",
            }
            .ToDictionary(name => name, name => p.GetPropertyString(name)));

        foreach (var (key, value) in values)
        {
            probe[key] = value;
        }

        probe["selectedTrack"] = subs.SelectedTrack?.DisplayName;
        probe["selectedLanguage"] = subs.SelectedTrack?.Language;
        probe["selectedEncoding"] = subs.SelectedTrack?.Prepared?.EncodingName;
        probe["selectedFormat"] = subs.SelectedTrack?.Prepared?.Format.ToString();
        probe["selectedCueCount"] = subs.SelectedTrack?.Prepared?.CueCount;
        probe["syncWaitMs"] = Math.Round(syncWaitMs);
        probe["timingSpeed"] = subs.Timing.Speed;
        probe["timingSummary"] = subs.TimingSummary;
        probe["anchorStatus"] = subs.AnchorStatus;
        probe["timeMap"] = subs.TimeMap?.Serialize();
        probe["timingDelayMs"] = subs.Timing.DelayMs;
        probe["tracks"] = subs.Tracks.Select(t => $"{(t.IsSelected ? "* " : "  ")}{t.DisplayName}").ToArray();

        if (options.DiagSaveSubtitles is { } savePath && subs.SelectedTrack is { } selected)
        {
            try
            {
                subs.SaveSyncedCopy(selected, savePath);
                probe["savedTo"] = savePath;
            }
            catch (Exception ex)
            {
                probe["saveError"] = ex.Message;
            }
        }

        return probe;
    }

    /// <summary>Test nastavka gledanja: čeka pitanje, odgovara ("continue"/"start") i bilježi poziciju nakon toga.</summary>
    private async Task<Dictionary<string, object?>> ResumeScenarioAsync(string choice)
    {
        LibraryController library = vm.Library;
        for (int i = 0; i < 50 && !library.HasResumeOffer; i++)
        {
            await Task.Delay(100);
        }

        var info = new Dictionary<string, object?>
        {
            ["offered"] = library.ResumeOffer,
            ["offerText"] = library.ResumeText,
            ["pausedWhileAsking"] = vm.IsPaused,
            ["positionWhileAsking"] = Math.Round(player()?.TimePosition ?? double.NaN, 1),
        };

        if (library.HasResumeOffer)
        {
            if (choice == "start")
            {
                library.ResumeFromStart();
            }
            else
            {
                library.ResumeContinue();
            }
        }

        await Task.Delay(2000);
        info["choice"] = choice;
        info["positionAfter"] = Math.Round(player()?.TimePosition ?? double.NaN, 1);
        info["pausedAfter"] = vm.IsPaused;
        info["promptStillVisible"] = library.HasResumeOffer;
        return info;
    }

    /// <summary>Test sljedeće epizode: čeka kraj datoteke, karticu i automatski prelazak.</summary>
    private async Task<Dictionary<string, object?>> UpNextScenarioAsync()
    {
        LibraryController library = vm.Library;
        string? first = vm.CurrentPath;
        bool sawCard = false;
        string? cardTitle = null;
        int afterCard = 0;
        for (int i = 0; i < 900 && string.Equals(vm.CurrentPath, first, StringComparison.OrdinalIgnoreCase); i++)
        {
            if (library.HasUpNext)
            {
                sawCard = true;
                cardTitle ??= library.UpNext?.Info.DisplayTitle;
            }

            // Bez automatskog pokretanja: kartica mora ostati i nakon isteka odbrojavanja (+3 s).
            if (sawCard && !library.AutoPlayNext && ++afterCard > (library.UpNextDelaySeconds + 3) * 10)
            {
                break;
            }

            await Task.Delay(100);
        }

        await Task.Delay(2000);
        return new Dictionary<string, object?>
        {
            ["from"] = first is null ? null : Path.GetFileName(first),
            ["sawCard"] = sawCard,
            ["cardTitle"] = cardTitle,
            ["to"] = vm.CurrentPath is null ? null : Path.GetFileName(vm.CurrentPath),
            ["title"] = vm.Title,
            ["isPaused"] = vm.IsPaused,
            ["upNextText"] = library.UpNextText,
            ["upNextStillShown"] = library.HasUpNext,
        };
    }

    /// <summary>
    /// "Pritisne" tipke redom (npr. "B;OemPlus;O;S;Ctrl+D;Shift+S"), istim putem kao prava tipkovnica,
    /// i nakon svake zapiše stanje (audio traka, brzina, omjer, snimka, omiljeni).
    /// </summary>
    private async Task<List<Dictionary<string, object?>>> PressKeysAsync(string keys)
    {
        var steps = new List<Dictionary<string, object?>>();
        foreach (string token in keys.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var mods = System.Windows.Input.ModifierKeys.None;
            string name = token;
            while (name.Contains('+') && name.Length > 1)
            {
                string prefix = name[..name.IndexOf('+')];
                mods |= prefix switch
                {
                    "Ctrl" => System.Windows.Input.ModifierKeys.Control,
                    "Shift" => System.Windows.Input.ModifierKeys.Shift,
                    "Alt" => System.Windows.Input.ModifierKeys.Alt,
                    _ => throw new ArgumentException($"Nepoznat modifikator: {prefix}"),
                };
                name = name[(name.IndexOf('+') + 1)..];
            }

            // "H*10": deset pritisaka bez čekanja (kao držanje tipke ili brzo klikanje).
            int repeat = 1;
            if (name.LastIndexOf('*') is int star and > 0)
            {
                repeat = int.Parse(name[(star + 1)..], System.Globalization.CultureInfo.InvariantCulture);
                name = name[..star];
            }

            var key = Enum.Parse<System.Windows.Input.Key>(name);
            bool handled = false;
            for (int r = 0; r < repeat; r++)
            {
                handled |= pressKey?.Invoke(key, mods) ?? false;
                await Task.Yield();
            }

            await Task.Delay(name == "S" ? 2500 : 700);
            steps.Add(new Dictionary<string, object?>
            {
                ["key"] = token,
                ["handled"] = handled,
                ["ms"] = Environment.TickCount64,
                ["state"] = await ExtrasSnapshotAsync(),
            });
        }

        return steps;
    }

    private async Task<Dictionary<string, object?>> ExtrasSnapshotAsync()
    {
        PlaybackExtrasController x = vm.Extras;
        MpvPlayer? p = player();
        Dictionary<string, string?> mpv = p is null
            ? []
            : await Task.Run(() => new[]
                {
                    "aid", "speed", "video-aspect-override", "panscan", "current-tracks/audio/lang", "time-pos", "pause",
                    "sub-delay", "sub-speed", "sub-start", "sub-text",
                }
                .ToDictionary(n => n, n => p.GetPropertyString(n)));
        return new Dictionary<string, object?>
        {
            ["audioTracks"] = x.AudioTracks.Select(t => $"{(t.IsSelected ? "* " : "  ")}{t.DisplayName}").ToArray(),
            ["speed"] = x.Speed,
            ["aspect"] = x.AspectMode,
            ["lastScreenshot"] = x.LastScreenshot,
            ["lastScreenshotBytes"] = x.LastScreenshot is { } s && File.Exists(s) ? new FileInfo(s).Length : null,
            ["subTimingDelayMs"] = vm.Subtitles.Timing.DelayMs,
            ["subTimingSpeed"] = vm.Subtitles.Timing.Speed,
            ["subSyncStatus"] = vm.Subtitles.SyncStatus,
            ["isFavorite"] = vm.Library.IsCurrentFavorite,
            ["favorites"] = vm.Library.Favorites.Select(f => $"{(f.IsSeries ? "serija" : "film")}: {f.Title} | {f.Path}").ToArray(),
            ["mpv"] = mpv,
        };
    }

    private Dictionary<string, object?> LibrarySnapshot()
    {
        LibraryController library = vm.Library;
        return new Dictionary<string, object?>
        {
            ["playlistTitle"] = library.PlaylistTitle,
            ["playlist"] = library.Playlist
                .Select(p => $"{(p.IsCurrent ? "*" : " ")}{(p.Watched ? "✓" : " ")} {p.Label} [{p.Group}] {p.ProgressText}".TrimEnd())
                .ToArray(),
            ["currentIndex"] = library.Current is null ? -1 : library.Playlist.IndexOf(library.Current),
            ["recent"] = library.Recent.Select(r => $"{r.Title} | {r.Detail}").ToArray(),
            ["favorites"] = library.Favorites.Select(f => $"{(f.IsSeries ? "serija" : "film")}: {f.Title} | {f.Path}").ToArray(),
            ["theme"] = JebaitedPlayer.Views.WindowTheme.Current,
            ["themePanelColor"] = (Application.Current.TryFindResource("PanelBackgroundBrush") as System.Windows.Media.SolidColorBrush)?.Color.ToString(),
            ["themeDictionaries"] = Application.Current.Resources.MergedDictionaries.Select(d => d.Source?.OriginalString).ToArray(),
            ["windowTitle"] = vm.Title,
        };
    }

    private void Start(TimeSpan warmup, int seconds)
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _ = RunAsync(warmup, seconds);
    }

    private async Task RunAsync(TimeSpan warmup, int seconds)
    {
        var result = new Dictionary<string, object?>
        {
            ["file"] = options.Files.FirstOrDefault(),
            ["appVersion"] = AppPaths.Version,
            ["startupMs"] = _startupMs,
            ["mpvInitMs"] = _mpvInitMs,
            ["firstFrameMs"] = _firstFrameMs,
            ["logicalCores"] = Environment.ProcessorCount,
            ["fullscreen"] = vm.IsFullscreen,
            ["controlsForced"] = options.DiagShowControls,
        };

        try
        {
            await Task.Delay(warmup);

            if (UpdateProbe is { } probeUpdate)
            {
                result["update"] = await probeUpdate();
            }

            if (options.DiagResume is { } choice)
            {
                result["resume"] = await ResumeScenarioAsync(choice);
            }

            if (options.DiagUpNextSeconds.HasValue)
            {
                result["upNext"] = await UpNextScenarioAsync();
            }

            if (options.DiagSelectSubtitle is { } language)
            {
                SubtitleController subs = vm.Subtitles;
                SubtitleTrackInfo? track = language == "none" ? null : subs.Tracks.FirstOrDefault(t => t.Language == language);
                subs.SelectTrack(track);
                await Task.Delay(1000);
            }

            if (options.DiagProbeSeconds is double probeAt)
            {
                result["subtitleProbe"] = await ProbeSubtitlesAsync(probeAt);
            }

            if (options.DiagKeys is { } keys)
            {
                result["keys"] = await PressKeysAsync(keys);
            }

            result["extras"] = await ExtrasSnapshotAsync();

            using var proc = Process.GetCurrentProcess();
            var cpu = new List<double>();
            var workingSet = new List<double>();
            var privateBytes = new List<double>();

            proc.Refresh();
            TimeSpan lastCpu = proc.TotalProcessorTime;
            var wall = Stopwatch.StartNew();
            TimeSpan lastWall = wall.Elapsed;

            for (int i = 0; i < seconds; i++)
            {
                await Task.Delay(1000);
                proc.Refresh();
                TimeSpan nowCpu = proc.TotalProcessorTime;
                TimeSpan nowWall = wall.Elapsed;
                cpu.Add((nowCpu - lastCpu).TotalMilliseconds / (nowWall - lastWall).TotalMilliseconds
                        / Environment.ProcessorCount * 100.0);
                workingSet.Add(proc.WorkingSet64 / 1048576.0);
                privateBytes.Add(proc.PrivateMemorySize64 / 1048576.0);
                lastCpu = nowCpu;
                lastWall = nowWall;
            }

            result["sampleSeconds"] = seconds;
            if (cpu.Count > 0)
            {
                result["cpuAvgPercent"] = Math.Round(cpu.Average(), 2);
                result["cpuMaxPercent"] = Math.Round(cpu.Max(), 2);
                result["cpuAvgPercentOfOneCore"] = Math.Round(cpu.Average() * Environment.ProcessorCount, 1);
                result["workingSetAvgMB"] = Math.Round(workingSet.Average(), 1);
                result["workingSetMaxMB"] = Math.Round(workingSet.Max(), 1);
                result["privateAvgMB"] = Math.Round(privateBytes.Average(), 1);
                result["privateMaxMB"] = Math.Round(privateBytes.Max(), 1);
                result["cpuSamples"] = cpu.Select(v => Math.Round(v, 2)).ToArray();
                result["workingSetSamplesMB"] = workingSet.Select(v => Math.Round(v, 1)).ToArray();
                result["privateSamplesMB"] = privateBytes.Select(v => Math.Round(v, 1)).ToArray();
            }

            result["library"] = LibrarySnapshot();
            if (vm.Online is { } online)
            {
                result["online"] = new Dictionary<string, object?>
                {
                    ["lastAutoResult"] = online.LastAutoResult,
                    ["anyProvider"] = online.AnyProviderConfigured,
                    ["status"] = online.Status,
                    ["providers"] = online.ProviderStatus,
                    ["results"] = online.Results.Select(r => $"{r.Level} {r.Candidate.Language} {r.Title} ({r.Candidate.Score})").ToArray(),
                    ["selectedTrack"] = vm.Subtitles.SelectedTrack?.DisplayName,
                };
            }

            if (vm.AutoSync is { } auto)
            {
                JebaitedPlayer.Sync.SyncResult? r = auto.LastResult;
                result["autoSync"] = new Dictionary<string, object?>
                {
                    ["verdict"] = r?.Verdict.ToString(),
                    ["speed"] = r?.Speed,
                    ["offset"] = r?.Offset,
                    ["globalScore"] = r?.GlobalScore,
                    ["finalScore"] = r?.FinalScore,
                    ["windows"] = r?.Windows,
                    ["agreeing"] = r?.AgreeingWindows,
                    ["matchRatio"] = r?.MatchRatio,
                    ["detail"] = r?.Detail,
                    ["track"] = auto.LastTrackKey is { } k ? Path.GetFileName(k) : null,
                    ["status"] = auto.Status,
                    ["runs"] = auto.CompletedRuns,
                    ["analysisMs"] = auto.LastAnalysisMs,
                    ["alignMs"] = auto.LastAlignMs,
                    ["appliedDelayMs"] = vm.Subtitles.Timing.DelayMs,
                    ["appliedSpeed"] = vm.Subtitles.Timing.Speed,
                    ["evidence"] = r?.Evidence,
                    ["segments"] = r?.Segments.Select(s => new { s.From, s.Speed, s.Offset, s.Cues, s.Evidence, s.Confirmed }).ToArray(),
                    ["appliedTimeMap"] = vm.Subtitles.TimeMap?.Serialize(),
                    ["timingSummary"] = vm.Subtitles.TimingSummary,
                };
            }

            result["externalOpens"] = ExternalOpens.ToArray();
            result["osAppKeyPresent"] = AppSecrets.HasOpenSubtitlesAppKey;
            result["processId"] = Environment.ProcessId;
            result["currentFile"] = vm.CurrentPath is null ? null : Path.GetFileName(vm.CurrentPath);
            result["hasMedia"] = vm.HasMedia;
            result["isAudioOnly"] = vm.IsAudioOnly;
            result["isPaused"] = vm.IsPaused;
            result["title"] = vm.Title;
            result["errorMessage"] = vm.ErrorMessage;
            result["positionSeconds"] = Math.Round(vm.Position, 2);

            var (children, disabled) = video.GetChildWindowInfo();
            result["videoChildWindows"] = children;
            result["videoChildWindowsDisabled"] = disabled;
            result["mouseRoutedToHost"] = video.IsCenterRoutedToHost();

            MpvPlayer? p = player();
            if (p != null)
            {
                result["mpv"] = await Task.Run(() =>
                    MpvProperties.ToDictionary(name => name, name => p.GetPropertyString(name)));
            }
        }
        catch (Exception ex)
        {
            result["exception"] = ex.ToString();
            Log.Error("Dijagnostika", ex);
        }
        finally
        {
            try
            {
                string path = options.DiagOut
                    ?? Path.Combine(AppPaths.LogsDir, $"diag-{DateTime.Now:yyyyMMdd-HHmmss}.json");
                File.WriteAllText(path, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                Log.Error("Dijagnostika: zapis rezultata", ex);
            }

            _ = Application.Current.Dispatcher.BeginInvoke(close);
        }
    }
}

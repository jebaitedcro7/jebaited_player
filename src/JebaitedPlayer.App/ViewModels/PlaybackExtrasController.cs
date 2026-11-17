using System.Globalization;
using System.Text.Json;
using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.Library;
using JebaitedPlayer.Mpv;
using JebaitedPlayer.Subtitles;

namespace JebaitedPlayer.ViewModels;

/// <summary>Jedna audio traka iz mpv-ova track-list.</summary>
public sealed record AudioTrackInfo(long Id, string? Title, string? Language, string? Codec, int? Channels, bool IsDefault,
    bool IsExternal, bool IsSelected)
{
    public string DisplayName
    {
        get
        {
            var parts = new List<string> { Language is null ? Loc.T("audio.unknownLanguage") : SubtitleLanguage.DisplayName(Language) };
            string format = string.Join(' ', new[] { CodecName(Codec), ChannelName(Channels) }.Where(s => s.Length > 0));
            if (format.Length > 0)
            {
                parts.Add(format);
            }

            if (!string.IsNullOrWhiteSpace(Title))
            {
                parts.Add(Title.Trim());
            }

            return $"{Id}. {string.Join(" · ", parts)}";
        }
    }

    private static string CodecName(string? codec) => codec?.ToLowerInvariant() switch
    {
        null or "" => string.Empty,
        "eac3" => "E-AC3",
        "truehd" => "TrueHD",
        "opus" => "Opus",
        "vorbis" => "Vorbis",
        "flac" => "FLAC",
        var c when c.StartsWith("pcm", StringComparison.Ordinal) => "PCM",
        var c when c.StartsWith("dts", StringComparison.Ordinal) => "DTS",
        var c => c.ToUpperInvariant(),
    };

    private static string ChannelName(int? channels) => channels switch
    {
        null or <= 0 => string.Empty,
        1 => "mono",
        2 => "stereo",
        6 => "5.1",
        8 => "7.1",
        int n => Loc.T("audio.channels", n),
    };
}

/// <summary>
/// Dodaci reprodukcije (M6): izbor audio trake (pamti se po videu), brzina 0,5–2×, omjer slike i snimka zaslona.
/// Sve ide kroz isti mpv; ništa od ovoga ne mijenja datoteke osim snimke koja se sprema u mapu slika.
/// </summary>
public sealed class PlaybackExtrasController(AppSettings settings, PlayerViewModel vm, WatchStore? store) : ObservableObject
{
    public static readonly double[] SpeedSteps = [0.5, 0.75, 1.0, 1.25, 1.5, 1.75, 2.0];

    /// <summary>Omjeri slike: vrijednost za mpv i ključ naziva (Languages/*.json).</summary>
    public static readonly IReadOnlyList<(string Value, string NameKey)> AspectModes =
    [
        ("auto", "aspect.original"),
        ("16:9", "aspect.16x9"),
        ("4:3", "aspect.4x3"),
        ("1.85:1", "aspect.185"),
        ("2.35:1", "aspect.235"),
        ("fill", "aspect.fill"),
    ];

    private MpvPlayer? _player;
    private IReadOnlyList<AudioTrackInfo> _audioTracks = [];
    private double _speed = 1;
    private string _aspect = "auto";

    public event Action<string>? Message;

    public IReadOnlyList<AudioTrackInfo> AudioTracks
    {
        get => _audioTracks;
        private set
        {
            _audioTracks = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasMultipleAudio));
            OnPropertyChanged(nameof(SelectedAudio));
        }
    }

    public bool HasMultipleAudio => _audioTracks.Count > 1;

    public AudioTrackInfo? SelectedAudio => _audioTracks.FirstOrDefault(t => t.IsSelected);

    public double Speed
    {
        get => _speed;
        private set
        {
            if (SetField(ref _speed, value))
            {
                OnPropertyChanged(nameof(SpeedText));
                OnPropertyChanged(nameof(IsSpeedChanged));
            }
        }
    }

    public string SpeedText => $"{Speed.ToString("0.##", Loc.Culture)}×";

    public bool IsSpeedChanged => Math.Abs(Speed - 1) > 1e-6;

    public string AspectMode
    {
        get => _aspect;
        private set
        {
            if (SetField(ref _aspect, value))
            {
                OnPropertyChanged(nameof(AspectName));
            }
        }
    }

    public string AspectName => AspectModes.FirstOrDefault(m => m.Value == _aspect).NameKey is { } key ? Loc.T(key) : _aspect;

    /// <summary>Zadnja spremljena snimka (za dijagnostiku).</summary>
    public string? LastScreenshot { get; private set; }

    public static string DefaultScreenshotFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "jebaited_player");

    public string ScreenshotFolder => FolderOverride
                                      ?? (string.IsNullOrWhiteSpace(settings.ScreenshotFolder) ? DefaultScreenshotFolder : settings.ScreenshotFolder);

    /// <summary>Dijagnostika: snimke idu u privremenu mapu.</summary>
    public string? FolderOverride { get; set; }

    public void Attach(MpvPlayer player)
    {
        _player = player;
        ApplyAspect(settings.DefaultAspect, announce: false);
        player.PropertyChanged += (_, e) =>
        {
            switch (e.Name)
            {
                case "track-list" when e.Value is string json:
                    UpdateAudioTracks(json);
                    break;
                case "aid":
                    UpdateSelection(e.Value as string);
                    break;
                case "speed" when e.Value is double s && s > 0:
                    Speed = s;
                    break;
            }
        };
        player.FileLoaded += (_, e) =>
        {
            if (e.TrackListJson != null)
            {
                UpdateAudioTracks(e.TrackListJson);
            }

            RestoreAudio(e.Path);
        };
    }

    // --- Audio ---------------------------------------------------------------------------------------------------

    public void SelectAudio(AudioTrackInfo track)
    {
        if (_player is null)
        {
            return;
        }

        _player.SetPropertyAsync("aid", track.Id.ToString(CultureInfo.InvariantCulture));
        vm.ShowOsd(Loc.T("osd.audio", track.DisplayName), 2000);
        if (vm.CurrentPath is { } path)
        {
            Safe(() => store?.SaveAudioState(path, new AudioState(track.Id, track.Language)));
        }
    }

    /// <summary>Tipka B: sljedeća audio traka.</summary>
    public void CycleAudio()
    {
        if (_audioTracks.Count == 0)
        {
            vm.ShowOsd(Loc.T("osd.noAudio"));
            return;
        }

        if (_audioTracks.Count == 1)
        {
            vm.ShowOsd(Loc.T("osd.singleAudio", _audioTracks[0].DisplayName), 2000);
            return;
        }

        int index = _audioTracks.ToList().FindIndex(t => t.IsSelected);
        SelectAudio(_audioTracks[(index + 1) % _audioTracks.Count]);
    }

    private void RestoreAudio(string? path)
    {
        if (path is null || store is null || _player is null)
        {
            return;
        }

        AudioState? state = Safe(() => store.GetAudioState(path));
        if (state is null)
        {
            return;
        }

        // Ista traka po ID-u ako jezik odgovara; inače prva traka zapamćenog jezika.
        AudioTrackInfo? track = _audioTracks.FirstOrDefault(t => t.Id == state.TrackId && (state.Language is null || t.Language == state.Language))
                                ?? _audioTracks.FirstOrDefault(t => state.Language != null && t.Language == state.Language);
        if (track != null && !track.IsSelected)
        {
            _player.SetPropertyAsync("aid", track.Id.ToString(CultureInfo.InvariantCulture));
            Log.Info($"Vraćena zapamćena audio traka: {track.DisplayName}");
        }
    }

    private void UpdateAudioTracks(string json)
    {
        var list = new List<AudioTrackInfo>();
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            foreach (JsonElement t in doc.RootElement.EnumerateArray())
            {
                if (Str(t, "type") != "audio")
                {
                    continue;
                }

                list.Add(new AudioTrackInfo(
                    t.TryGetProperty("id", out JsonElement id) && id.TryGetInt64(out long v) ? v : 0,
                    Str(t, "title"),
                    SubtitleLanguage.Normalize(Str(t, "lang")),
                    Str(t, "codec"),
                    t.TryGetProperty("demux-channel-count", out JsonElement ch) && ch.TryGetInt32(out int c) ? c : null,
                    Bool(t, "default"),
                    Bool(t, "external"),
                    Bool(t, "selected")));
            }
        }
        catch (JsonException ex)
        {
            Log.Warn($"Audio trake nije moguće pročitati: {ex.Message}");
            return;
        }

        AudioTracks = list;
    }

    private void UpdateSelection(string? aid)
    {
        long? id = long.TryParse(aid, NumberStyles.Integer, CultureInfo.InvariantCulture, out long v) ? v : null;
        AudioTracks = _audioTracks.Select(t => t with { IsSelected = t.Id == id }).ToList();
    }

    // --- Brzina --------------------------------------------------------------------------------------------------

    public void SetSpeed(double speed)
    {
        speed = Math.Clamp(Math.Round(speed, 2), 0.5, 2.0);
        _player?.SetPropertyAsync("speed", speed);
        Speed = speed;
        vm.ShowOsd(Loc.T("osd.speed", SpeedText));
    }

    /// <summary>Tipke [ i ]: sljedeća manja ili veća brzina iz popisa.</summary>
    public void StepSpeed(int direction)
    {
        double next = direction > 0
            ? SpeedSteps.FirstOrDefault(s => s > Speed + 1e-6, SpeedSteps[^1])
            : SpeedSteps.LastOrDefault(s => s < Speed - 1e-6, SpeedSteps[0]);
        SetSpeed(next);
    }

    // --- Omjer slike ---------------------------------------------------------------------------------------------

    public void SetAspect(string mode) => ApplyAspect(mode, announce: true);

    /// <summary>Omjer slike; bez poruke na videu kad dolazi iz postavki (zadani omjer, UPGRADE 3).</summary>
    public void ApplyAspect(string mode, bool announce)
    {
        if (_player is null || AspectModes.All(m => m.Value != mode))
        {
            return;
        }

        _player.SetPropertyAsync("video-aspect-override", mode is "auto" or "fill" ? "-1" : mode);
        _player.SetPropertyAsync("panscan", mode == "fill" ? 1.0 : 0.0);
        AspectMode = mode;
        if (announce)
        {
            vm.ShowOsd(Loc.T("osd.aspect", AspectName));
        }
    }

    /// <summary>Tipka O: sljedeći omjer slike.</summary>
    public void CycleAspect()
    {
        int index = AspectModes.ToList().FindIndex(m => m.Value == _aspect);
        SetAspect(AspectModes[(index + 1) % AspectModes.Count].Value);
    }

    // --- Snimka zaslona ------------------------------------------------------------------------------------------

    /// <summary>Tipka S (s titlovima) ili Shift+S (samo slika). Sprema JPG u mapu slika.</summary>
    public async Task<string?> TakeScreenshotAsync(bool withSubtitles)
    {
        if (_player is null || !vm.HasMedia || vm.IsAudioOnly)
        {
            Message?.Invoke(Loc.T("notice.screenshotNeedsVideo"));
            return null;
        }

        string folder = ScreenshotFolder;
        string path;
        try
        {
            Directory.CreateDirectory(folder);
            path = UniquePath(folder, ScreenshotName(vm.Title, vm.Position));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Message?.Invoke(Loc.T("notice.screenshotFolderUnavailable", ex.Message));
            return null;
        }

        _player.CommandAsync("screenshot-to-file", path, withSubtitles ? "subtitles" : "video");

        // mpv sprema u pozadini; pričekaj da se datoteka pojavi.
        for (int i = 0; i < 50; i++)
        {
            await Task.Delay(100);
            if (File.Exists(path) && new FileInfo(path).Length > 0)
            {
                LastScreenshot = path;
                Message?.Invoke(Loc.T("notice.screenshotSaved", Path.GetFileName(path)));
                Log.Info($"Snimka zaslona: {path}");
                return path;
            }
        }

        Message?.Invoke(Loc.T("notice.screenshotFailed"));
        return null;
    }

    public void OpenScreenshotFolder()
    {
        try
        {
            Directory.CreateDirectory(ScreenshotFolder);
            string args = LastScreenshot is { } last && File.Exists(last) ? $"/select,\"{last}\"" : $"\"{ScreenshotFolder}\"";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", args) { UseShellExecute = false });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Message?.Invoke(Loc.T("notice.screenshotFolderUnavailable", ex.Message));
        }
    }

    /// <summary>"Naslov 00-42-13.jpg" (bez znakova koje Windows ne dopušta u nazivu).</summary>
    public static string ScreenshotName(string title, double seconds)
    {
        string safe = new string((string.IsNullOrWhiteSpace(title) ? "snimka" : title)
            .Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray()).Trim();
        if (safe.Length > 80)
        {
            safe = safe[..80].Trim();
        }

        TimeSpan t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return $"{safe} {(int)t.TotalHours:00}-{t.Minutes:00}-{t.Seconds:00}.jpg";
    }

    private static string UniquePath(string folder, string name)
    {
        string path = Path.Combine(folder, name);
        for (int i = 2; File.Exists(path); i++)
        {
            path = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(name)} ({i}){Path.GetExtension(name)}");
        }

        return path;
    }

    private static T? Safe<T>(Func<T?> action)
    {
        try
        {
            return action();
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or InvalidOperationException)
        {
            Log.Warn($"Baza: {ex.Message}");
            return default;
        }
    }

    private static void Safe(Action action) => Safe<object?>(() =>
    {
        action();
        return null;
    });

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.True;
}

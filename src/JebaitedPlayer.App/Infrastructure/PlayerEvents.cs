using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using JebaitedPlayer.Diagnostics;
using JebaitedPlayer.Mpv;
using JebaitedPlayer.ViewModels;

namespace JebaitedPlayer.Infrastructure;

/// <summary>
/// Bilježi događaje reprodukcije u lokalni dnevnik (samo promjene, ne svaki kadar): učitavanje i kraj datoteke, pauza,
/// glasnoća, zvuk i titlovi, zastajkivanje (buffer), neočekivano gašenje mpv-a. Sluša postojeće događaje, ne dodaje pozadinske procese.
/// Nikad ne zapisuje putanju ni sadržaj, samo naziv datoteke.
/// </summary>
internal sealed class PlayerEventTracker : IDisposable
{
    private static readonly HashSet<string> Coalesced = ["volume", "sub-delay", "sub-speed", "speed"];

    private readonly MpvPlayer _player;
    private readonly PlayerViewModel _vm;
    private readonly object _gate = new();
    private readonly Dictionary<string, object?> _pending = [];
    private Timer? _flush;
    private bool _armed;
    private bool _detached;
    private long _startTick;
    private long _stallTick;
    private int _stalls;

    public PlayerEventTracker(MpvPlayer player, PlayerViewModel vm)
    {
        _player = player;
        _vm = vm;
        player.StartFile += OnStartFile;
        player.FileLoaded += OnFileLoaded;
        player.EndFile += OnEndFile;
        player.PlaybackRestart += OnRestart;
        player.VideoReconfig += OnVideoReconfig;
        player.Shutdown += OnShutdown;
        player.PropertyChanged += OnProperty;
        vm.PropertyChanged += OnVmProperty;
    }

    /// <summary>Poziva se prije gašenja mpv-a: tada Shutdown nije neočekivan.</summary>
    public void Detach()
    {
        _detached = true;
        FlushPending();
    }

    public void Dispose()
    {
        _detached = true;
        _player.StartFile -= OnStartFile;
        _player.FileLoaded -= OnFileLoaded;
        _player.EndFile -= OnEndFile;
        _player.PlaybackRestart -= OnRestart;
        _player.VideoReconfig -= OnVideoReconfig;
        _player.Shutdown -= OnShutdown;
        _player.PropertyChanged -= OnProperty;
        _vm.PropertyChanged -= OnVmProperty;
        lock (_gate)
        {
            _flush?.Dispose();
            _flush = null;
        }
    }

    private string CurrentFile => Redactor.FileName(_vm.CurrentPath);

    private void OnStartFile(object? sender, EventArgs e)
    {
        _startTick = Environment.TickCount64;
        _stalls = 0;
        DiagLog.Info(LogCat.Playback, "file.open", nameof(PlayerEventTracker), "Loading file", ("file", CurrentFile));
    }

    private void OnFileLoaded(object? sender, MpvFileLoadedEventArgs e)
    {
        _armed = true;
        long ms = _startTick == 0 ? -1 : Environment.TickCount64 - _startTick;
        var (video, audio, subs) = Summarize(e.TrackListJson);
        DiagLog.Info(LogCat.Playback, "file.loaded", nameof(PlayerEventTracker), "File loaded",
            ("file", CurrentFile), ("loadMs", ms), ("hasVideo", e.HasVideo), ("fps", e.FrameRate),
            ("video", video), ("audio", audio), ("subtitleTracks", subs));
    }

    private void OnEndFile(object? sender, MpvEndFileEventArgs e)
    {
        double pos = _player.TimePosition;
        double dur = _vm.Duration;
        if (e.Reason == MpvEndFileReason.Error)
        {
            DiagLog.Error(LogCat.Playback, "file.error", nameof(PlayerEventTracker), "Playback failed",
                null, ("file", CurrentFile), ("reason", e.Reason), ("error", e.ErrorText), ("code", e.Error), ("position", Round(pos)));
            return;
        }

        if (e.Reason == MpvEndFileReason.Eof && dur > 30 && !double.IsNaN(pos) && pos < dur - 10)
        {
            DiagLog.Warn(LogCat.Playback, "file.premature_end", nameof(PlayerEventTracker),
                "File ended well before its duration", ("file", CurrentFile), ("position", Round(pos)), ("duration", Round(dur)));
            return;
        }

        DiagLog.Info(LogCat.Playback, "file.end", nameof(PlayerEventTracker), "File ended",
            ("file", CurrentFile), ("reason", e.Reason), ("position", Round(pos)), ("duration", Round(dur)), ("stalls", _stalls));
    }

    private static void OnRestart(object? sender, EventArgs e) =>
        DiagLog.Debug(LogCat.Playback, "playback.restart", nameof(PlayerEventTracker), "Playback restarted");

    private static void OnVideoReconfig(object? sender, EventArgs e) =>
        DiagLog.Debug(LogCat.Video, "video.reconfig", nameof(PlayerEventTracker), "Video output reconfigured");

    private void OnShutdown(object? sender, EventArgs e)
    {
        if (!_detached)
        {
            DiagLog.Critical(LogCat.Stability, "mpv.shutdown.unexpected", nameof(PlayerEventTracker),
                "mpv core shut down unexpectedly", null, ("file", CurrentFile), ("position", Round(_player.TimePosition)));
        }
    }

    private void OnProperty(object? sender, MpvPropertyChangedEventArgs e)
    {
        if (!_armed)
        {
            return;
        }

        if (Coalesced.Contains(e.Name))
        {
            lock (_gate)
            {
                _pending[e.Name] = e.Value;
                _flush ??= new Timer(_ => FlushPending(), null, 600, Timeout.Infinite);
            }

            return;
        }

        switch (e.Name)
        {
            case "pause":
                DiagLog.Info(LogCat.Usage, e.Value is true ? "play.pause" : "play.resume", nameof(PlayerEventTracker),
                    e.Value is true ? "Paused" : "Resumed", ("position", Round(_player.TimePosition)));
                break;
            case "mute":
                DiagLog.Info(LogCat.Usage, "mute.changed", nameof(PlayerEventTracker), "Mute changed", ("muted", e.Value));
                break;
            case "aid":
                DiagLog.Info(LogCat.Audio, "audiotrack.changed", nameof(PlayerEventTracker), "Audio track changed", ("aid", e.Value));
                break;
            case "sid":
                DiagLog.Info(LogCat.Subtitle, "subtitle.track", nameof(PlayerEventTracker), "Subtitle track changed", ("sid", e.Value));
                break;
            case "sub-visibility":
                DiagLog.Info(LogCat.Subtitle, "subtitle.visibility", nameof(PlayerEventTracker), "Subtitle visibility changed", ("visible", e.Value));
                break;
            case "paused-for-cache":
                if (e.Value is true)
                {
                    _stallTick = Environment.TickCount64;
                }
                else if (_stallTick != 0)
                {
                    long ms = Environment.TickCount64 - _stallTick;
                    _stallTick = 0;
                    _stalls++;
                    DiagLog.Warn(LogCat.Playback, "playback.stall", nameof(PlayerEventTracker), "Playback stalled waiting for data",
                        ("stallMs", ms), ("position", Round(_player.TimePosition)), ("file", CurrentFile));
                }

                break;
        }
    }

    private void FlushPending()
    {
        KeyValuePair<string, object?>[] items;
        lock (_gate)
        {
            items = [.. _pending];
            _pending.Clear();
            _flush?.Dispose();
            _flush = null;
        }

        foreach ((string name, object? value) in items)
        {
            string type = name switch
            {
                "volume" => "volume.changed",
                "speed" => "play.speed",
                _ => "subtitle." + name.Replace("sub-", string.Empty),
            };
            string cat = name == "volume" ? LogCat.Audio : name == "speed" ? LogCat.Playback : LogCat.Subtitle;
            DiagLog.Info(cat, type, nameof(PlayerEventTracker), "Setting changed", ("name", name), ("value", value is double d ? Round(d) : value));
        }
    }

    private void OnVmProperty(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlayerViewModel.IsFullscreen))
        {
            DiagLog.Info(LogCat.Ui, "ui.fullscreen", nameof(PlayerEventTracker), "Fullscreen changed", ("fullscreen", _vm.IsFullscreen));
        }
    }

    private static double Round(double v) => double.IsNaN(v) ? -1 : Math.Round(v, 1);

    /// <summary>Codec i dimenzije iz mpv track-list JSON-a (bez jezika naslova i putanja).</summary>
    internal static (string Video, string Audio, int Subtitles) Summarize(string? json)
    {
        string video = string.Empty;
        string audio = string.Empty;
        int subs = 0;
        if (string.IsNullOrEmpty(json))
        {
            return (video, audio, subs);
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (JsonElement t in doc.RootElement.EnumerateArray())
            {
                string type = t.TryGetProperty("type", out JsonElement ty) ? ty.GetString() ?? string.Empty : string.Empty;
                string codec = t.TryGetProperty("codec", out JsonElement c) ? c.GetString() ?? "?" : "?";
                if (type == "video" && video.Length == 0)
                {
                    int w = t.TryGetProperty("demux-w", out JsonElement we) && we.TryGetInt32(out int wi) ? wi : 0;
                    int h = t.TryGetProperty("demux-h", out JsonElement he) && he.TryGetInt32(out int hi) ? hi : 0;
                    video = string.Create(CultureInfo.InvariantCulture, $"{codec} {w}x{h}");
                }
                else if (type == "audio" && audio.Length == 0)
                {
                    int ch = t.TryGetProperty("demux-channel-count", out JsonElement ce) && ce.TryGetInt32(out int ci) ? ci : 0;
                    audio = string.Create(CultureInfo.InvariantCulture, $"{codec} {ch}ch");
                }
                else if (type == "sub")
                {
                    subs++;
                }
            }
        }
        catch (Exception)
        {
            // Nepoznat oblik: samo bez sažetka.
        }

        return (video, audio, subs);
    }
}

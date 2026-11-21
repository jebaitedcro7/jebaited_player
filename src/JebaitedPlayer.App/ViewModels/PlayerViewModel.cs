using JebaitedPlayer.Diagnostics;
using System.Globalization;
using System.Windows.Input;
using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.Library;
using JebaitedPlayer.Mpv;

namespace JebaitedPlayer.ViewModels;

/// <summary>Radio postaja (URL streama i naziv za prikaz).</summary>
public sealed record StreamInfo(string Url, string Title);

/// <summary>Stanje reprodukcije za glavni prozor i sloj s kontrolama.</summary>
public sealed class PlayerViewModel : ObservableObject
{
    // R1: radio uživo ne treba velik cache. Zadano mpv čuva do 150 MiB unaprijed i 50 MiB unatrag, pa je pauziran
    // stream skupljao zvuk u RAM, a „početak” streama bio je trenutak uključenja postaje.
    private const string StreamMaxBytes = "4MiB";
    private const string StreamMaxBackBytes = "1MiB";
    private const string DefaultMaxBytes = "150MiB";
    private const string DefaultMaxBackBytes = "50MiB";

    private MpvPlayer? _player;
    private bool _streamCache;
    private StreamInfo? _stoppedStream;
    private bool _hasMedia;
    private bool _isAudioOnly;
    private bool _isLoading;
    private bool _isPaused = true;
    private bool _isMuted;
    private bool _isFullscreen;
    private bool _isScrubbing;
    private bool _eofReached;
    private double _position;
    private double _duration;
    private double _volume = 100;
    private string _title = string.Empty;
    private string _errorMessage = string.Empty;
    private string _notice = string.Empty;
    private bool _applyingFromMpv;
    private long _lastScrubSeek;
    private long _ignorePositionUntil;

    private string? _titleOverride;
    private bool _showWelcomeShortcuts;

    public PlayerViewModel(AppSettings settings, WatchStore? store)
    {
        Subtitles = new SubtitleController(settings)
        {
            LoadState = path => store?.GetSubtitleState(path),
            SaveState = (path, state) => store?.SaveSubtitleState(path, state),
        };
        Library = new LibraryController(this, store)
        {
            AutoPlayNext = settings.AutoPlayNext,
            UpNextDelaySeconds = Math.Clamp(settings.UpNextSeconds, 3, 60),
            UpNextLeadSeconds = Math.Clamp(settings.UpNextLeadSeconds, 0, 120),
            ResumeMode = settings.ResumeMode,
        };
        Extras = new PlaybackExtrasController(settings, this, store);
        PlayPauseCommand = new RelayCommand(TogglePause, () => HasMedia);
        MuteCommand = new RelayCommand(ToggleMute);
        OpenCommand = new RelayCommand(() => OpenRequested?.Invoke(this, EventArgs.Empty));
        FullscreenCommand = new RelayCommand(() => FullscreenToggleRequested?.Invoke(this, EventArgs.Empty));
    }

    public event EventHandler? OpenRequested;
    public event EventHandler? FullscreenToggleRequested;
    public event EventHandler? PlaybackRestarted;
    public event EventHandler? LoadFailed;

    /// <summary>Datoteka je učitana (argument: putanja).</summary>
    public event EventHandler<string>? MediaLoaded;

    /// <summary>Učitavanje počinje (prije mpv naredbe): npr. odabir equalizera za glazbu ili film.</summary>
    public event EventHandler<string>? LoadStarting;

    /// <summary>Reprodukcija je stigla do kraja datoteke.</summary>
    public event EventHandler? EndReached;

    /// <summary>Korisnik se nakon kraja vratio unatrag (kraj više nije dosegnut).</summary>
    public event EventHandler? EndCleared;

    public LibraryController Library { get; }

    /// <summary>Titlovi s interneta (postavlja glavni prozor).</summary>
    public OnlineSubtitleController? Online { get; set; }

    /// <summary>Audio trake, brzina, omjer slike, snimka zaslona (M6).</summary>
    public PlaybackExtrasController Extras { get; }

    /// <summary>Automatska sinkronizacija titlova prema govoru (M5).</summary>
    public AutoSyncController? AutoSync { get; set; }

    /// <summary>Putanja trenutno otvorene datoteke.</summary>
    public string? CurrentPath { get; private set; }

    public bool EofReached => _eofReached;

    /// <summary>Internetski stream (radio): http ili https adresa.</summary>
    public static bool IsStreamPath(string? path) =>
        path is not null && (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                             path.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

    /// <summary>Svira (ili se spaja) radio uživo: bez premotavanja, a Play nakon pauze ponovno spaja stream.</summary>
    public bool IsLiveStream => HasMedia && IsStreamPath(CurrentPath);

    /// <summary>Postaja zaustavljena tipkom Stop (R1). Play je ponovno spaja uživo; brišu je novo učitavanje i Home.</summary>
    public StreamInfo? StoppedStream
    {
        get => _stoppedStream;
        private set => SetField(ref _stoppedStream, value);
    }

    /// <summary>Na početnom ekranu: prikaži popis prečaca (inače "Nastavi gledati").</summary>
    public bool ShowWelcomeShortcuts
    {
        get => _showWelcomeShortcuts;
        set => SetField(ref _showWelcomeShortcuts, value);
    }

    public ICommand PlayPauseCommand { get; }
    public ICommand MuteCommand { get; }
    public ICommand OpenCommand { get; }
    public ICommand FullscreenCommand { get; }

    public SubtitleController Subtitles { get; }

    public IReadOnlyList<ShortcutInfo> Shortcuts => KeyboardShortcuts.All;

    /// <summary>2.0: tipke su promijenjene u Postavkama (popis na početnom ekranu i F1 slijede stvarne tipke).</summary>
    public void RefreshShortcuts() => OnPropertyChanged(nameof(Shortcuts));

    /// <summary>Promjena jezika: tekstovi koje slažu kontroleri (popis, povijest, titlovi, prečaci) na novom jeziku.</summary>
    public void RefreshTexts()
    {
        Library.RefreshTexts();
        Subtitles.RefreshTexts();
        Extras.NotifyAllChanged();
        OnPropertyChanged(nameof(Shortcuts));
    }

    public string VersionText => "v" + AppPaths.Version;

    public bool HasMedia
    {
        get => _hasMedia;
        set => SetField(ref _hasMedia, value);
    }

    public bool IsAudioOnly
    {
        get => _isAudioOnly;
        private set => SetField(ref _isAudioOnly, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetField(ref _isLoading, value);
    }

    public bool IsPaused
    {
        get => _isPaused;
        private set
        {
            if (SetField(ref _isPaused, value))
            {
                OnPropertyChanged(nameof(PlayPauseGlyph));
            }
        }
    }

    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            if (SetField(ref _isMuted, value))
            {
                OnPropertyChanged(nameof(VolumeGlyph));
                if (!_applyingFromMpv)
                {
                    _player?.SetPropertyAsync("mute", value);
                }
            }
        }
    }

    public double Volume
    {
        get => _volume;
        set
        {
            value = Math.Clamp(Math.Round(value), 0, 100);
            if (SetField(ref _volume, value))
            {
                OnPropertyChanged(nameof(VolumeGlyph));
                OnPropertyChanged(nameof(VolumeText));
                if (!_applyingFromMpv)
                {
                    _player?.SetPropertyAsync("volume", value);
                }
            }
        }
    }

    public bool IsFullscreen
    {
        get => _isFullscreen;
        set
        {
            if (SetField(ref _isFullscreen, value))
            {
                OnPropertyChanged(nameof(FullscreenGlyph));
            }
        }
    }

    public bool IsScrubbing
    {
        get => _isScrubbing;
        set => SetField(ref _isScrubbing, value);
    }

    public double Position
    {
        get => _position;
        private set
        {
            if (SetField(ref _position, value))
            {
                OnPropertyChanged(nameof(PositionText));
            }
        }
    }

    public double Duration
    {
        get => _duration;
        private set
        {
            if (SetField(ref _duration, value))
            {
                OnPropertyChanged(nameof(DurationText));
                OnPropertyChanged(nameof(PositionText));
                OnPropertyChanged(nameof(TimelineMaximum));
            }
        }
    }

    public double TimelineMaximum => Duration > 0 ? Duration : 1;

    public string Title
    {
        get => _title;
        private set => SetField(ref _title, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        set
        {
            if (SetField(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => ErrorMessage.Length > 0;

    public string Notice
    {
        get => _notice;
        set
        {
            if (SetField(ref _notice, value))
            {
                OnPropertyChanged(nameof(HasNotice));
            }
        }
    }

    public bool HasNotice => Notice.Length > 0;

    public string PositionText => TimeFormat.Format(Position, Duration >= 3600);

    public string DurationText => TimeFormat.Format(Duration, Duration >= 3600);

    public string PlayPauseGlyph => IsPaused ? "" : "";

    public string VolumeGlyph => IsMuted || Volume <= 0 ? ""
        : Volume < 34 ? ""
        : Volume < 67 ? ""
        : "";

    public string VolumeText => $"{Volume:0} %";

    public string FullscreenGlyph => IsFullscreen ? "" : "";

    public void Attach(MpvPlayer player)
    {
        _player = player;
        _streamCache = false; // novi mpv ima zadani cache (R1)
        player.PropertyChanged += OnMpvPropertyChanged;
        player.FileLoaded += OnFileLoaded;
        player.EndFile += OnEndFile;
        player.PlaybackRestart += (_, _) => PlaybackRestarted?.Invoke(this, EventArgs.Empty);
        Subtitles.Attach(player);
        Extras.Attach(player);
        AutoSync?.Attach(player);
    }

    /// <summary>Učitava jednu datoteku. Popis i redoslijed vodi <see cref="LibraryController"/>.</summary>
    public void Load(string path, bool startPaused, string? displayTitle)
    {
        if (_player is null)
        {
            return;
        }

        LoadStarting?.Invoke(this, path);
        StoppedStream = null;
        ApplyStreamCache(IsStreamPath(path));
        ErrorMessage = string.Empty;
        Notice = string.Empty;
        IsLoading = true;
        IsAudioOnly = false;
        HasMedia = true;
        CurrentPath = path;
        _titleOverride = displayTitle;
        Title = displayTitle ?? Path.GetFileName(path);
        Position = 0;
        Duration = 0;
        _eofReached = false;

        // Pauza se postavlja prije učitavanja, da video ne krene dok korisnik ne odgovori na pitanje o nastavku.
        _player.SetPropertyAsync("pause", startPaused);
        _player.CommandAsync("loadfile", path, "replace");
    }

    public void SetPaused(bool paused) => _player?.SetPropertyAsync("pause", paused);

    /// <summary>
    /// R1: za stream samo mali cache (štedi RAM, a pauziran stream ne skuplja zvuk). Obične datoteke dobivaju
    /// natrag zadane vrijednosti mpv-a (aplikacija ih inače ne mijenja). Postavlja se prije <c>loadfile</c>.
    /// </summary>
    private void ApplyStreamCache(bool stream)
    {
        if (_player is null || stream == _streamCache)
        {
            return;
        }

        _streamCache = stream;
        _player.SetPropertyAsync("demuxer-max-bytes", stream ? StreamMaxBytes : DefaultMaxBytes);
        _player.SetPropertyAsync("demuxer-max-back-bytes", stream ? StreamMaxBackBytes : DefaultMaxBackBytes);
    }

    /// <summary>
    /// Tipka Stop (mini player). Datoteka ostaje učitana, na početku i pauzirana (kao u Winampu).
    /// Radio (R1) se stvarno zaustavlja: veza se zatvara, a postaja se pamti da je Play ponovno spoji uživo.
    /// Prije je i radio išao „na početak”, a to je u mpv cacheu trenutak uključenja postaje, pa je svirala ista pjesma.
    /// </summary>
    public void StopPlayback()
    {
        if (_player is null || !HasMedia)
        {
            return;
        }

        if (IsLiveStream)
        {
            var station = new StreamInfo(CurrentPath!, _titleOverride ?? Title);
            DiagLog.Info(LogCat.Usage, "stream.stop", nameof(PlayerViewModel), "Stream stopped", ("position", Math.Round(Position, 1)));
            StopCore();
            StoppedStream = station;
            return;
        }

        SetPaused(true);
        SeekAbsolute(0, exact: true);
    }

    /// <summary>Tipka Play: nastavi. Radio (R1) se nakon Stopa, pauze ili prekida veze ponovno spaja uživo.</summary>
    public void Play()
    {
        if (!HasMedia)
        {
            ResumeStoppedStream();
            return;
        }

        if (IsLiveStream)
        {
            if (IsPaused || _eofReached)
            {
                ReconnectStream();
            }

            return;
        }

        SetPaused(false);
    }

    /// <summary>Ponovno spoji postaju zaustavljenu tipkom Stop. Vraća false ako takve postaje nema.</summary>
    public bool ResumeStoppedStream()
    {
        if (_player is null || StoppedStream is not { } station)
        {
            return false;
        }

        DiagLog.Info(LogCat.Usage, "stream.resume", nameof(PlayerViewModel), "Stream reconnected after stop");
        Load(station.Url, startPaused: false, station.Title);
        return true;
    }

    /// <summary>Pauziran ili prekinut radio: nova veza (uživo) umjesto nastavka iz cachea.</summary>
    private void ReconnectStream()
    {
        DiagLog.Info(LogCat.Usage, "stream.reconnect", nameof(PlayerViewModel), "Stream reconnected", ("paused", IsPaused), ("ended", _eofReached));
        Load(CurrentPath!, startPaused: false, _titleOverride ?? Title);
    }

    /// <summary>
    /// Zatvori trenutni video i vrati se na početni ekran (tipka Home). mpv ostaje pokrenut (sljedeće otvaranje je odmah).
    /// Poziciju prije toga sprema <see cref="LibraryController.LeaveToHome"/>.
    /// </summary>
    public void Stop()
    {
        StoppedStream = null;
        StopCore();
    }

    private void StopCore()
    {
        if (_player is null || !HasMedia)
        {
            return;
        }

        _player.CommandAsync("stop");
        CurrentPath = null;
        _titleOverride = null;
        _eofReached = false;
        IsLoading = false;
        IsScrubbing = false;
        IsAudioOnly = false;
        HasMedia = false;
        Position = 0;
        Duration = 0;
    }

    public void TogglePause()
    {
        if (_player is null)
        {
            return;
        }

        if (!HasMedia)
        {
            ResumeStoppedStream();
            return;
        }

        if (IsLiveStream && (IsPaused || _eofReached))
        {
            // R1: radio se ne nastavlja iz cachea (zakašnjelo ili ispočetka), nego se ponovno spaja uživo.
            ReconnectStream();
            return;
        }

        if (_eofReached && IsPaused)
        {
            // Kraj datoteke: ponovno od početka.
            _player.CommandAsync("seek", "0", "absolute");
            _player.SetPropertyAsync("pause", false);
            return;
        }

        _player.CommandAsync("cycle", "pause");
    }

    public void SeekRelative(double seconds)
    {
        // R1: radio uživo se ne premotava (inače strelice skaču po cacheu, a Home na trenutak uključenja).
        if (_player is null || !HasMedia || IsLiveStream)
        {
            return;
        }

        DiagLog.Info(LogCat.Usage, "seek.relative", nameof(PlayerViewModel), "Seek", ("delta", seconds), ("from", Math.Round(Position, 1)));
        _player.CommandAsync("seek", F(seconds), "relative");
        ShowOsd(seconds > 0 ? $"+{seconds:0} s" : $"−{-seconds:0} s", 700);
    }

    public void SeekAbsolute(double seconds, bool exact = false)
    {
        if (_player is null || !HasMedia || IsLiveStream)
        {
            return;
        }

        DiagLog.Info(LogCat.Usage, "seek.absolute", nameof(PlayerViewModel), "Seek", ("to", Math.Round(seconds, 1)), ("from", Math.Round(Position, 1)));
        _player.CommandAsync("seek", F(seconds), exact ? "absolute+exact" : "absolute");
        Position = seconds;
        _ignorePositionUntil = Environment.TickCount64 + 500;
    }

    /// <summary>Povlačenje po vremenskoj traci: tijekom povlačenja brzo (ključni kadrovi), na kraju precizno.</summary>
    public void ScrubTo(double seconds, bool final)
    {
        if (_player is null || !HasMedia || IsLiveStream)
        {
            return;
        }

        Position = seconds;
        long now = Environment.TickCount64;
        if (final)
        {
            DiagLog.Info(LogCat.Usage, "seek.scrub", nameof(PlayerViewModel), "Seek by dragging", ("to", Math.Round(seconds, 1)));
            _player.CommandAsync("seek", F(seconds), "absolute+exact");
            _ignorePositionUntil = now + 700;
        }
        else if (now - _lastScrubSeek >= 80)
        {
            _lastScrubSeek = now;
            _player.CommandAsync("seek", F(seconds), "absolute+keyframes");
        }
    }

    /// <summary>Poziva UI timer; čita zadnju poznatu poziciju bez čekanja na mpv.</summary>
    public void RefreshPosition()
    {
        if (_player is null || !HasMedia || IsScrubbing || Environment.TickCount64 < _ignorePositionUntil)
        {
            return;
        }

        double t = _player.TimePosition;
        if (!double.IsNaN(t))
        {
            Position = t;
        }
    }

    /// <summary>Jedan zarez kotačića miša iznad trake glasnoće (%): 50 zareza za cijeli raspon, dovoljno precizno.</summary>
    public const double VolumeWheelStep = 5;

    private double _wheelVolumeRemainder;

    /// <summary>
    /// Kotačić miša: jedan zarez (Delta 120) = <see cref="VolumeWheelStep"/> %. Razmjerno Deltau, pa miševi s finim kotačićem
    /// i touchpadi mijenjaju glasnoću glatko; ostatak manji od 1 % se pamti, da se ne gubi zaokruživanjem.
    /// Vraća novu glasnoću.
    /// </summary>
    public double ChangeVolumeByWheel(int wheelDelta)
    {
        _wheelVolumeRemainder += wheelDelta / 120.0 * VolumeWheelStep;
        double whole = Math.Truncate(_wheelVolumeRemainder);
        _wheelVolumeRemainder -= whole;
        if (whole != 0)
        {
            Volume += whole;
        }

        return Volume;
    }

    public void ChangeVolume(double delta)
    {
        Volume += delta;
        ShowOsd(Loc.T("osd.volume", Volume));
    }

    public void ToggleMute()
    {
        IsMuted = !IsMuted;
        ShowOsd(IsMuted ? Loc.T("osd.muted") : Loc.T("osd.unmuted"));
    }

    public void FrameStep(bool back)
    {
        if (HasMedia)
        {
            _player?.CommandAsync(back ? "frame-back-step" : "frame-step");
        }
    }

    /// <summary>Kratka poruka preko videa (crta je mpv, pa radi i u cijelom zaslonu).</summary>
    public void ShowOsd(string text, int durationMs = 1200)
    {
        if (HasMedia && !IsAudioOnly)
        {
            _player?.CommandAsync("show-text", text, durationMs.ToString(CultureInfo.InvariantCulture));
        }
    }

    private void OnMpvPropertyChanged(object? sender, MpvPropertyChangedEventArgs e)
    {
        _applyingFromMpv = true;
        try
        {
            switch (e.Name)
            {
                case "pause":
                    IsPaused = e.Value as bool? ?? true;
                    break;
                case "duration":
                    Duration = e.Value as double? ?? 0;
                    break;
                case "volume":
                    if (e.Value is double v)
                    {
                        Volume = v;
                    }

                    break;
                case "mute":
                    if (e.Value is bool m)
                    {
                        IsMuted = m;
                    }

                    break;
                case "media-title":
                    if (e.Value is string t && t.Length > 0 && _titleOverride is null)
                    {
                        Title = t;
                    }

                    break;
                case "eof-reached":
                {
                    bool eof = e.Value as bool? ?? false;
                    bool was = _eofReached;
                    _eofReached = eof;
                    if (eof && !was && HasMedia && !IsLoading)
                    {
                        EndReached?.Invoke(this, EventArgs.Empty);
                    }
                    else if (!eof && was)
                    {
                        EndCleared?.Invoke(this, EventArgs.Empty);
                    }

                    break;
                }
                case "idle-active":
                    if (e.Value is true && !IsLoading)
                    {
                        HasMedia = false;
                        IsAudioOnly = false;
                        Position = 0;
                        Duration = 0;
                    }

                    break;
            }
        }
        finally
        {
            _applyingFromMpv = false;
        }
    }

    private void OnFileLoaded(object? sender, MpvFileLoadedEventArgs e)
    {
        IsLoading = false;
        ErrorMessage = string.Empty;
        IsAudioOnly = !e.HasVideo;
        HasMedia = true;
        MediaLoaded?.Invoke(this, e.Path ?? CurrentPath ?? string.Empty);
    }

    private void OnEndFile(object? sender, MpvEndFileEventArgs e)
    {
        if (e.Reason != MpvEndFileReason.Error)
        {
            return;
        }

        IsLoading = false;
        ErrorMessage = Loc.T("error.cannotPlay", e.ErrorText ?? Loc.T("error.unknown"));
        Log.Playback.Error($"Reprodukcija nije uspjela: {e.ErrorText}");
        LoadFailed?.Invoke(this, EventArgs.Empty);
    }

    private static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}

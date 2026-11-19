using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.Library;
using JebaitedPlayer.Mpv;
using JebaitedPlayer.Services;
using JebaitedPlayer.Subtitles;

namespace JebaitedPlayer.ViewModels;

/// <param name="Language">Jezik odabranih titlova (null = nema titlova).</param>
/// <param name="Restored">Za ovaj video postoji zapamćeni izbor korisnika.</param>
public sealed record SubtitlesSettledEventArgs(string? VideoPath, string? Language, bool Restored);

/// <summary>Odabran je vanjski tekstualni titl. TimingKnown: sinkronizacija je već poznata (zapamćena, ručna ili ranije provjerena).</summary>
public sealed record ExternalSubtitleSelectedEventArgs(SubtitleTrackInfo Track, bool TimingKnown);

/// <summary>Replika u popisu za poravnanje po replici: izvorno vrijeme, kada se sada prikazuje i tekst.</summary>
public sealed record CueRow(double OriginalMs, double DisplaySeconds, string Time, string Text, bool IsCurrent);

/// <summary>Točka ručnog poravnanja u prozoru sinkronizacije.</summary>
public sealed record AnchorRow(SyncAnchor Anchor, string ShortText, double PositionSeconds)
{
    /// <summary>Opis točke na trenutnom jeziku.</summary>
    public string Description => Loc.T("anchors.row", ShortText, SubtitleController.ClockText(PositionSeconds));
}

/// <summary>Jedan titl kako ga vidi aplikacija (ugrađeni u video ili vanjski koji smo mi učitali).</summary>
public sealed record SubtitleTrackInfo
{
    public long Id { get; init; }
    public string? Title { get; init; }
    public string? Language { get; init; }
    public bool IsExternal { get; init; }
    public string? ExternalFile { get; init; }
    public string? Codec { get; init; }
    public bool IsDefault { get; init; }
    public bool IsForced { get; init; }
    public bool IsImage { get; init; }
    public bool IsSelected { get; init; }
    public PreparedSubtitle? Prepared { get; init; }

    /// <summary>Ključ za pamćenje sinkronizacije po titlu.</summary>
    public string Key => Prepared?.OriginalPath ?? ExternalFile ?? $"embedded:{Id}";

    public string DisplayName
    {
        get
        {
            string language = SubtitleLanguage.DisplayName(Language);
            if (Prepared is { } p)
            {
                string details = p.IsBitmap
                    ? SubtitleFormats.DisplayName(p.Format)
                    : $"{SubtitleFormats.DisplayName(p.Format)}, {p.EncodingName}";
                return $"{language} · {Path.GetFileName(p.OriginalPath)} ({details})";
            }

            string title = string.IsNullOrWhiteSpace(Title) ? string.Empty : $" · {Title}";
            string kind = IsExternal ? Loc.T("track.external") : Loc.T("track.embedded");
            string codec = Codec is null ? string.Empty : ", " + CodecName(Codec);
            string flags = (IsForced ? ", " + Loc.T("track.forced") : string.Empty) + (IsDefault ? ", " + Loc.T("track.default") : string.Empty);
            return $"{language}{title} ({kind}{codec}{flags})";
        }
    }

    /// <summary>
    /// Kraći naziv za izbornik: kod titlova uz video istog imena prikazuje se samo nastavak (npr. "hr.opensubtitles2.srt"),
    /// a ne cijelo ime epizode koje je isto za sve titlove.
    /// </summary>
    public string MenuName(string? videoPath)
    {
        if (Prepared is { } p && videoPath != null)
        {
            string stem = Path.GetFileNameWithoutExtension(videoPath);
            string file = Path.GetFileName(p.OriginalPath);
            if (file.Length > stem.Length + 1 && file.StartsWith(stem + ".", StringComparison.OrdinalIgnoreCase))
            {
                string details = p.IsBitmap
                    ? SubtitleFormats.DisplayName(p.Format)
                    : $"{SubtitleFormats.DisplayName(p.Format)}, {p.EncodingName}";
                return $"{SubtitleLanguage.DisplayName(Language)} · {file[(stem.Length + 1)..]} ({details})";
            }
        }

        return DisplayName;
    }

    private static string CodecName(string codec) => codec switch
    {
        "subrip" => "SRT",
        "ass" or "ssa" => "ASS",
        "webvtt" => "WebVTT",
        "hdmv_pgs_subtitle" => "PGS",
        "dvd_subtitle" => "VobSub",
        "dvb_subtitle" => "DVB",
        "mov_text" => Loc.T("track.mp4Text"),
        _ => codec,
    };
}

/// <summary>
/// Titlovi u playeru: učitavanje lokalnih datoteka, odabir po prioritetu jezika, izbornik,
/// ručna sinkronizacija (pomak, brzina, dvije točke) i spremanje sinkronizirane kopije.
/// </summary>
public sealed class SubtitleController(AppSettings settings) : ObservableObject
{
    private readonly Dictionary<string, PreparedSubtitle> _prepared = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SubtitleTiming> _timingByTrack = new(StringComparer.OrdinalIgnoreCase);

    // Korekcija po dijelovima (titlovi za drugo izdanje) po titlu (ključ: originalna datoteka). mpv tada prikazuje
    // ispravljenu kopiju iz predmemorije, a pomak i brzina (sub-delay, sub-speed) dolaze dodatno, preko nje.
    private readonly Dictionary<string, SubtitleTimeMap> _mapByTrack = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _queued = [];

    // Izvorni dokument po titlu (za popis replika u poravnanju po replici).
    private readonly Dictionary<string, SubtitleDocument> _documents = new(StringComparer.OrdinalIgnoreCase);
    private Func<string> _anchorStatus = () => Loc.T("anchors.noneStatus");

    // Zadnji odabrani titl (ključ), i dok je nakratko "bez odabira" tijekom zamjene datoteke.
    private string? _activeKey;

    private MpvPlayer? _player;
    private IReadOnlyList<SubtitleTrackInfo> _tracks = [];
    private string? _videoPath;
    private double? _videoFps;
    private int _generation;
    private SubtitleTiming _timing = SubtitleTiming.Identity;
    private bool _visible = true;
    private double? _lastSubStartSeconds;
    private long _lastSubStartTicks;
    private double? _audioMarkMs;
    private double? _subtitleMarkOriginalMs;
    private (double Original, double Target)? _firstPoint;
    private Func<string> _syncStatus = () => Loc.T("zx.none");
    private bool _persistAfterSelection;
    private string? _lastSelectedLanguage;

    // Vrijednosti koje smo nedavno poslali mpv-u. mpv potvrđuje svaku promjenu događajem, ali s kašnjenjem: kod brzih
    // promjena (držanje tipke H, brzo klikanje) stigne potvrda starije vrijednosti nakon što smo već poslali noviju.
    // Takve potvrde ne smiju prepisati trenutni pomak, inače se dio koraka izgubi.
    private readonly List<double> _sentDelays = [];
    private readonly List<double> _sentSpeeds = [];

    // Ručne promjene u log idu sažeto: jedan zapis kad se niz pritisaka smiri.
    private Timer? _logTimer;
    private string? _pendingLog;

    /// <summary>Lokalni i ugrađeni titlovi su obrađeni nakon otvaranja videa (temelj za pretragu na internetu).</summary>
    public event EventHandler<SubtitlesSettledEventArgs>? Settled;

    /// <summary>Kratke poruke za prikaz preko videa.</summary>
    public event Action<string>? Message;

    /// <summary>Odabran je vanjski tekstualni titl (temelj za automatsku sinkronizaciju prema govoru).</summary>
    public event EventHandler<ExternalSubtitleSelectedEventArgs>? ExternalSelected;

    /// <summary>Čitanje zapamćenih titlova za video (iz baze).</summary>
    public Func<string, SubtitleState?>? LoadState { get; init; }

    /// <summary>Spremanje odabranih titlova i sinkronizacije za video (u bazu).</summary>
    public Action<string, SubtitleState>? SaveState { get; init; }

    /// <summary>Ručni encoding za titlove dodane uz video (koristi dijagnostika).</summary>
    public int? ForcedCodePageForQueued { get; set; }

    public IReadOnlyList<SubtitleTrackInfo> Tracks
    {
        get => _tracks;
        private set
        {
            _tracks = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedTrack));
            OnPropertyChanged(nameof(SelectedTrackText));
            OnPropertyChanged(nameof(CanSaveSynced));
        }
    }

    public SubtitleTrackInfo? SelectedTrack => _tracks.FirstOrDefault(t => t.IsSelected);

    public string SelectedTrackText => SelectedTrack?.DisplayName ?? Loc.T("subs.noneSelected");

    public bool CanSaveSynced => SelectedTrack?.Prepared is { IsBitmap: false };

    public string? VideoPath => _videoPath;

    public double? VideoFrameRate => _videoFps;

    public SubtitleTiming Timing
    {
        get => _timing;
        private set
        {
            if (_timing == value)
            {
                return;
            }

            _timing = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DelayText));
            OnPropertyChanged(nameof(DelayMsInput));
            OnPropertyChanged(nameof(SpeedInput));
            OnPropertyChanged(nameof(TimingSummary));
            OnPropertyChanged(nameof(HasCorrection));
            OnPropertyChanged(nameof(DelaySecondsInput));
            OnPropertyChanged(nameof(GradualText));
        }
    }

    /// <summary>Pomak u sekundama za unos u prozoru sinkronizacije (npr. "−2,5").</summary>
    public string DelaySecondsInput
    {
        get => (Timing.DelayMs / 1000).ToString("0.00", Loc.Culture);
        set
        {
            if (TryParseNumber(value, out double seconds) && Math.Abs(seconds) < 36_000)
            {
                SetTiming(Timing with { DelayMs = seconds * 1000 }, Loc.T("osd.subDelay"));
            }
            else
            {
                OnPropertyChanged();
            }
        }
    }

    /// <summary>Opis postupne korekcije (brzine) običnim riječima.</summary>
    public string GradualText
    {
        get
        {
            double perTen = Timing.DriftSecondsPerTenMinutes;
            if (Math.Abs(perTen) < 0.005)
            {
                return Loc.T("gradual.none");
            }

            return Loc.T(perTen < 0 ? "gradual.earlier" : "gradual.later", Math.Abs(perTen).ToString("0.0", Loc.Culture));
        }
    }

    /// <summary>Replike oko trenutnog mjesta u filmu (za poravnanje po replici).</summary>
    public ObservableCollection<CueRow> NearbyCues { get; } = [];

    /// <summary>Točke ručnog poravnanja (replika → trenutak kad se čuje).</summary>
    public ObservableCollection<AnchorRow> Anchors { get; } = [];

    public string AnchorStatus => _anchorStatus();

    /// <summary>Promjena jezika: opisi točaka, statusi i sažeci na novom jeziku.</summary>
    public void RefreshTexts()
    {
        for (int i = 0; i < Anchors.Count; i++)
        {
            Anchors[i] = Anchors[i] with { };
        }

        OnPropertyChanged(string.Empty);
    }

    private void SetAnchorStatus(Func<string> status)
    {
        _anchorStatus = status;
        OnPropertyChanged(nameof(AnchorStatus));
    }

    private void SetSyncStatus(Func<string> status)
    {
        _syncStatus = status;
        OnPropertyChanged(nameof(SyncStatus));
    }

    public string DelayText => FormatDelay(Timing.DelayMs);

    /// <summary>Korekcija po dijelovima za odabrani titl (null = nema).</summary>
    public SubtitleTimeMap? TimeMap => SelectedTrack?.Key is { } key && _mapByTrack.TryGetValue(key, out SubtitleTimeMap? map) ? map : null;

    /// <summary>Postoji neka korekcija (pomak, brzina ili po dijelovima) koju "Vrati original" poništava.</summary>
    public bool HasCorrection => !Timing.IsIdentity || TimeMap != null;

    public string TimingSummary
    {
        get
        {
            string delay = FormatDelay(Timing.DelayMs);
            string speed = Timing.Speed.ToString("0.0000", Loc.Culture);
            if (TimeMap is not { } map)
            {
                return Timing.IsIdentity ? Loc.T("timing.original") : Loc.T("timing.summaryLinear", delay, speed);
            }

            string parts = Loc.T("timing.summaryParts", map.Segments.Count, FormatDelay(map.MinOffsetMs), FormatDelay(map.MaxOffsetMs));
            return Timing.IsIdentity ? parts : $"{parts} + {Loc.T("timing.summaryLinearInline", delay, speed)}";
        }
    }

    /// <summary>Pomak u milisekundama za unos u prozoru sinkronizacije.</summary>
    public string DelayMsInput
    {
        get => Math.Round(Timing.DelayMs).ToString("0", CultureInfo.InvariantCulture);
        set
        {
            if (TryParseNumber(value, out double ms) && Math.Abs(ms) < 36_000_000)
            {
                SetTiming(Timing with { DelayMs = ms }, Loc.T("osd.subDelay"));
            }
            else
            {
                OnPropertyChanged();
            }
        }
    }

    /// <summary>Faktor brzine za unos (1 = bez korekcije).</summary>
    public string SpeedInput
    {
        get => Timing.Speed.ToString("0.0000", Loc.Culture);
        set
        {
            if (TryParseNumber(value, out double speed) && speed is > 0.5 and < 2)
            {
                SetTiming(Timing with { Speed = speed }, Loc.T("osd.subSpeed"));
            }
            else
            {
                OnPropertyChanged();
            }
        }
    }

    public bool IsVisible
    {
        get => _visible;
        set
        {
            if (_visible == value)
            {
                return;
            }

            _visible = value;
            OnPropertyChanged();
            _player?.SetPropertyAsync("sub-visibility", value);
        }
    }

    public string SyncStatus => _syncStatus();

    public void Attach(MpvPlayer player)
    {
        _player = player;
        player.PropertyChanged += OnMpvPropertyChanged;
        player.FileLoaded += OnFileLoaded;
    }

    /// <summary>Titlovi koji su stigli zajedno s videom (npr. drag &amp; drop); učitaju se čim se video učita.</summary>
    public void QueueForNextFile(IEnumerable<string> files) => _queued.AddRange(files);

    /// <summary>Ručno učitavanje titlova (dijalog ili drag &amp; drop) za video koji se već reproducira.</summary>
    public void LoadFiles(IReadOnlyList<string> files, int? forcedCodePage = null)
    {
        if (_player is null || _videoPath is null)
        {
            QueueForNextFile(files);
            return;
        }

        _ = LoadAsync(_generation, files, includeAutoFound: false, forcedCodePage);
    }

    public void SelectTrack(SubtitleTrackInfo? track)
    {
        _persistAfterSelection = true;
        _player?.SetPropertyAsync("sid", track is null ? "no" : track.Id.ToString(CultureInfo.InvariantCulture));
        if (track != null && !IsVisible)
        {
            IsVisible = true;
        }
    }

    public void CycleTrack()
    {
        if (_tracks.Count == 0)
        {
            Message?.Invoke(Loc.T("subs.noneForVideo"));
            return;
        }

        int index = _tracks.ToList().FindIndex(t => t.IsSelected);
        SubtitleTrackInfo? next = index + 1 < _tracks.Count ? _tracks[index + 1] : null;
        SelectTrack(next);
        Message?.Invoke(next is null ? Loc.T("subs.off") : Loc.T("subs.selected", next.DisplayName));
    }

    public void ToggleVisibility()
    {
        IsVisible = !IsVisible;
        Message?.Invoke(IsVisible ? Loc.T("subs.shown") : Loc.T("subs.hidden"));
    }

    public void AdjustDelay(double deltaMs) => SetTiming(Timing with { DelayMs = Timing.DelayMs + deltaMs }, Loc.T("osd.subDelay"));

    /// <summary>Korak postupne korekcije: 0,5 s na svakih 10 minuta filma.</summary>
    public const double GradualStep = 0.5 / 600;

    /// <summary>
    /// "Titlovi postupno kasne" (<paramref name="direction"/> = −1: kasnije replike dolaze ranije) ili "postupno žure" (+1).
    /// Replika koja je sad na ekranu ostaje gdje jest; mijenja se samo ono dalje u filmu.
    /// </summary>
    public void AdjustGradual(int direction) => SetGradual(Timing.Speed + Math.Sign(direction) * GradualStep);

    public void ResetGradual() => SetGradual(1);

    private void SetGradual(double speed)
    {
        double position = _player?.TimePosition ?? double.NaN;
        double anchorMs = double.IsNaN(position) ? 0 : position * 1000;
        SetTiming(Timing.WithSpeedAnchored(Math.Clamp(speed, 0.9, 1.1), anchorMs), null);
        PersistState();
        Message?.Invoke(Math.Abs(Timing.Speed - 1) < 1e-9 ? Loc.T("gradual.noneShort") : Loc.T("gradual.message", GradualText));
        LogManual($"postupna korekcija: {GradualText} {TimingSummary}");
    }

    /// <summary>Popis replika oko trenutnog mjesta (25 s unatrag, 8 s naprijed), s vremenom kad se sad prikazuju.</summary>
    public void RefreshNearbyCues()
    {
        NearbyCues.Clear();
        double position = _player?.TimePosition ?? double.NaN;
        if (SelectedTrack?.Prepared is not { IsBitmap: false } prepared || double.IsNaN(position))
        {
            return;
        }

        SubtitleDocument doc;
        try
        {
            doc = OriginalDocument(prepared);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Message?.Invoke(Loc.T("anchors.readFailed", ex.Message));
            return;
        }

        SubtitleTimeMap? map = TimeMap;
        var rows = doc.Cues
            .Select(c => (Cue: c, Display: Timing.Apply(map?.Apply(c.StartMs) ?? c.StartMs) / 1000))
            .Where(x => x.Display >= position - 25 && x.Display <= position + 8)
            .ToList();
        int current = rows.FindLastIndex(x => x.Display <= position);
        for (int i = 0; i < rows.Count; i++)
        {
            // Bez oznaka (<i>, {\an8}); novi redak (i ASS \N) kao " / ".
            string text = Regex.Replace(rows[i].Cue.Text, @"<[^>]+>|\{\\[^}]*\}", string.Empty)
                .Replace(@"\N", " / ").Replace("\r", string.Empty).Replace("\n", " / ").Trim();
            NearbyCues.Add(new CueRow(rows[i].Cue.StartMs, rows[i].Display, ClockText(rows[i].Display), text, i == current));
        }
    }

    /// <summary>
    /// Odabrana replika počinje sada (film je pauziran na početku te replike). Jedna točka pomakne titlove (brzina ostaje);
    /// više točaka daje pomak i brzinu ili ispravak po dijelovima (titlovi za drugo izdanje).
    /// </summary>
    public void AddAnchor(CueRow row)
    {
        double position = _player?.TimePosition ?? double.NaN;
        if (double.IsNaN(position))
        {
            return;
        }

        var anchor = new SyncAnchor(row.OriginalMs, position * 1000);
        AnchorRow? same = Anchors.FirstOrDefault(a => Math.Abs(a.Anchor.OriginalMs - row.OriginalMs) < 1);
        if (same != null)
        {
            Anchors.Remove(same);
        }

        string shortText = row.Text.Length > 40 ? row.Text[..40] + "…" : row.Text;
        Anchors.Add(new AnchorRow(anchor, shortText, position));
        ApplyAnchors();
    }

    public void RemoveAnchor(AnchorRow row)
    {
        Anchors.Remove(row);
        ApplyAnchors();
    }

    public void ClearAnchors()
    {
        Anchors.Clear();
        SetAnchorStatus(() => Loc.T("anchors.noneStatus"));
    }

    private void ApplyAnchors()
    {
        if (SelectedTrack is not { Prepared: { IsBitmap: false } prepared } track)
        {
            return;
        }

        List<SyncAnchor> anchors = [.. Anchors.Select(a => a.Anchor)];
        if (anchors.Count == 0)
        {
            SetAnchorStatus(() => Loc.T("anchors.noneKept"));
            return;
        }

        Func<string> result;
        if (anchors.Count == 1)
        {
            // Jedna točka: cijeli titl (i postojeći ispravak po dijelovima) pomakne se da ova replika bude točna.
            SyncAnchor a = anchors[0];
            double mapped = TimeMap?.Apply(a.OriginalMs) ?? a.OriginalMs;
            SetTiming(Timing.AlignPoint(mapped, a.TargetMs), null);
            PersistState();
            double delayMs = Timing.DelayMs;
            result = () => Loc.T("anchors.offset", FormatDelay(delayMs));
        }
        else
        {
            List<(long, long)> cues = [.. OriginalDocument(prepared).Cues.Select(c => (c.StartMs, c.EndMs))];
            AnchorSolution solution = SyncAnchors.Solve(anchors, cues);
            ApplyAutoSync(track, solution.Timing, solution.Map);
            // Opis se pri promjeni jezika ponovno složi (rješenje je brzo i ne mijenja se).
            result = () => SyncAnchors.Solve(anchors, cues).Description;
        }

        int count = anchors.Count;
        string Points() => Loc.Plural("anchors.points", count);
        SetAnchorStatus(() => Loc.T("anchors.status", Points(), result()) + (count == 1 ? Loc.T("anchors.addMoreHint") : string.Empty));
        Message?.Invoke(Loc.T("anchors.message", Points(), result()));
        LogManual($"poravnanje po replici ({Points()}): {result()}; {TimingSummary}");
        RefreshNearbyCues();
    }

    private SubtitleDocument OriginalDocument(PreparedSubtitle prepared)
    {
        string key = $"{prepared.OriginalPath}|{prepared.CodePage}";
        if (!_documents.TryGetValue(key, out SubtitleDocument? doc))
        {
            byte[] bytes = File.ReadAllBytes(prepared.OriginalPath);
            System.Text.Encoding? encoding = prepared.CodePage is int cp ? SubtitleEncoding.Get(cp) : null;
            doc = SubtitleParser.Parse(SubtitleEncoding.Decode(bytes, encoding, out _), prepared.OriginalPath, _videoFps);
            _documents[key] = doc;
        }

        return doc;
    }

    internal static string ClockText(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        string tenths = ((int)(t.Milliseconds / 100)).ToString(CultureInfo.InvariantCulture);
        string separator = Loc.Culture.NumberFormat.NumberDecimalSeparator;
        return t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}{separator}{tenths}"
            : $"{t.Minutes:00}:{t.Seconds:00}{separator}{tenths}";
    }

    public void ApplySpeedPreset(double speed) => SetTiming(Timing with { Speed = speed }, Loc.T("osd.subSpeed"));

    public void ResetSync()
    {
        ResetSyncPoints();
        ClearAnchors();
        if (SelectedTrack is { } track && _mapByTrack.Remove(track.Key))
        {
            ReplacePlayback(track, null);
            OnTimeMapChanged();
        }

        SetTiming(SubtitleTiming.Identity, null);
        PersistState();
        Message?.Invoke(Loc.T("subs.syncReset"));
        LogManual("vraćena originalna sinkronizacija");
    }

    /// <summary>Pamti odabrane titlove i sinkronizaciju za ovaj video (vraćaju se pri sljedećem otvaranju).</summary>
    public void PersistState()
    {
        if (_videoPath is null || SaveState is null)
        {
            return;
        }

        SubtitleTrackInfo? track = SelectedTrack;
        SubtitleState state = track switch
        {
            null => new SubtitleState("none", null, null, null, 0, 1),
            { Prepared: { } p } => new SubtitleState("external", p.OriginalPath, null, p.Language, Timing.DelayMs, Timing.Speed,
                _mapByTrack.TryGetValue(p.OriginalPath, out SubtitleTimeMap? map) ? map.Serialize() : null),
            _ => new SubtitleState("embedded", null, track.Id, track.Language, Timing.DelayMs, Timing.Speed),
        };

        try
        {
            SaveState(_videoPath, state);
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or InvalidOperationException)
        {
            Log.Subtitles.Warn($"Stanje titlova nije spremljeno: {ex.Message}");
        }
    }

    /// <summary>Postavlja pomak i brzinu titlova (odmah, bez prepisivanja datoteke).</summary>
    public void SetTiming(SubtitleTiming timing, string? osdLabel)
    {
        if (_player != null)
        {
            double delaySeconds = timing.DelayMs / 1000.0;
            Remember(_sentDelays, delaySeconds);
            Remember(_sentSpeeds, timing.Speed);
            _player.SetPropertyAsync("sub-delay", delaySeconds);
            _player.SetPropertyAsync("sub-speed", timing.Speed);
        }

        Timing = timing;
        if (SelectedTrack is { } track)
        {
            _timingByTrack[track.Key] = timing;
        }

        if (osdLabel != null)
        {
            // Oznaka znači da je promjenu napravio korisnik, pa je pamtimo za ovaj video.
            PersistState();
            string speed = Math.Abs(timing.Speed - 1) > 1e-9 ? Loc.T("timing.speed", timing.Speed.ToString("0.0000", Loc.Culture)) : string.Empty;
            Message?.Invoke($"{osdLabel}: {FormatDelay(timing.DelayMs)}{speed}");
            LogManual(TimingSummary);
        }
    }

    private static void Remember(List<double> sent, double value)
    {
        sent.Add(value);
        if (sent.Count > 32)
        {
            sent.RemoveAt(0);
        }
    }

    /// <summary>Potvrda vrijednosti koju smo sami poslali (možda zastarjela) ili stvarna promjena izvana.</summary>
    private static bool IsEcho(List<double> sent, double value) => sent.Any(v => Math.Abs(v - value) < 1e-6);

    /// <summary>Ručna sinkronizacija u log, sažeto: zapis tek kad se niz pritisaka smiri (~1 s).</summary>
    private void LogManual(string text)
    {
        string track = SelectedTrack?.Prepared is { } p ? Path.GetFileName(p.OriginalPath) : SelectedTrack?.DisplayName ?? "bez titlova";
        string position = _player?.TimePosition is double t && !double.IsNaN(t) ? TimeFormat.Format(t, forceHours: true) : "?";
        Interlocked.Exchange(ref _pendingLog, $"Ručna sinkronizacija ({track}, na {position}): {text}");
        _logTimer ??= new Timer(_ =>
        {
            if (Interlocked.Exchange(ref _pendingLog, null) is { } line)
            {
                Log.Subtitles.Info(line);
            }
        });
        _logTimer.Change(1000, Timeout.Infinite);
    }

    /// <summary>Sinkronizacija po replici, 1. korak: korisnik je upravo čuo repliku.</summary>
    public void MarkAudio()
    {
        double t = _player?.TimePosition ?? double.NaN;
        if (double.IsNaN(t))
        {
            return;
        }

        _audioMarkMs = t * 1000;
        if (_subtitleMarkOriginalMs is null)
        {
            Message?.Invoke(Loc.T("zx.audioMarked"));
        }

        TryCompletePoint();
    }

    /// <summary>Sinkronizacija po replici, 2. korak: titl te replike je na ekranu (ili je upravo bio).</summary>
    public void MarkSubtitle()
    {
        if (_lastSubStartSeconds is not double start || Environment.TickCount64 - _lastSubStartTicks > 10_000)
        {
            Message?.Invoke(Loc.T("zx.noSubtitle"));
            return;
        }

        // mpv "sub-start" je vrijeme iz datoteke titla (prije sub-delay i sub-speed), a ne vrijeme na ekranu.
        // Ranije se ovdje još jednom oduzimao trenutni pomak, pa je uz već postavljen pomak svaka točka bila kriva upravo
        // za taj pomak (npr. +3 s i Z/X na točnoj replici → +6 s).
        _subtitleMarkOriginalMs = start * 1000;
        if (_audioMarkMs is null)
        {
            Message?.Invoke(Loc.T("zx.subtitleMarked"));
        }

        TryCompletePoint();
    }

    public void ResetSyncPoints()
    {
        _audioMarkMs = null;
        _subtitleMarkOriginalMs = null;
        _firstPoint = null;
        SetSyncStatus(() => Loc.T("zx.none"));
    }

    /// <summary>Dvije točke izravno (dijagnostika i testovi).</summary>
    public void ApplyTwoPoints(double original1, double target1, double original2, double target2)
    {
        _firstPoint = (original1, target1);
        SetTiming(SubtitleTiming.FromTwoPoints(original1, target1, original2, target2), null);
        double speed = Timing.Speed, delay = Timing.DelayMs;
        SetSyncStatus(() => Loc.T("zx.twoPoints", speed.ToString("0.0000", Loc.Culture), FormatDelay(delay)));
    }

    public void ChangeEncoding(SubtitleTrackInfo track, int? codePage)
    {
        if (track.Prepared is not { IsBitmap: false } prepared || _player is null)
        {
            return;
        }

        int generation = _generation;
        string? video = _videoPath;
        double? fps = _videoFps;
        SubtitleTimeMap? map = _mapByTrack.TryGetValue(prepared.OriginalPath, out SubtitleTimeMap? m) ? m : null;
        _ = Task.Run(() =>
        {
            PreparedSubtitle p = SubtitleService.Prepare(prepared.OriginalPath, video, fps, codePage);
            return map is null ? p : SubtitleService.PrepareMapped(p, map, fps);
        }).ContinueWith(task =>
        {
            if (generation != _generation || _player is null)
            {
                return;
            }

            if (task.Exception is { } ex)
            {
                Message?.Invoke(Loc.T("subs.encodingFailed", ex.InnerException?.Message));
                return;
            }

            PreparedSubtitle updated = task.Result;
            _player.CommandAsync("sub-remove", track.Id.ToString(CultureInfo.InvariantCulture));
            AddToPlayer(updated, select: true);
            Message?.Invoke(Loc.T("subs.encodingChanged", updated.EncodingName));
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    public string SuggestedSavePath(SubtitleTrackInfo track)
    {
        string ext = track.Prepared?.Format is SubtitleFormat.Ass ? ".ass" : track.Prepared?.Format is SubtitleFormat.Ssa ? ".ssa" : ".srt";
        string language = track.Language ?? "hr";
        if (_videoPath is { } video && Path.GetDirectoryName(video) is { } dir)
        {
            return Path.Combine(dir, $"{Path.GetFileNameWithoutExtension(video)}.{language}.sync{ext}");
        }

        return Path.ChangeExtension(track.Prepared?.OriginalPath ?? "titlovi", $".sync{ext}");
    }

    public void SaveSyncedCopy(SubtitleTrackInfo track, string targetPath)
    {
        if (track.Prepared is not { } prepared)
        {
            return;
        }

        SubtitleService.SaveSynced(prepared, Timing, targetPath, _videoFps, TimeMap);
        Message?.Invoke(Loc.T("subs.saved", Path.GetFileName(targetPath)));
        Log.Subtitles.Info($"Spremljeni sinkronizirani titlovi: {targetPath} ({TimingSummary})");
    }

    public void ApplyLanguagePriority() => _player?.SetPropertyAsync("slang", MpvConfig.LanguagePriority(settings));

    public void ApplyStyle()
    {
        if (_player != null)
        {
            MpvConfig.ApplyStyle(_player, settings.SubtitleStyle);
        }
    }

    private void OnFileLoaded(object? sender, MpvFileLoadedEventArgs e)
    {
        int generation = ++_generation;
        _videoPath = e.Path;
        _videoFps = e.FrameRate is > 1 and < 300 ? e.FrameRate : null;
        _prepared.Clear();
        _timingByTrack.Clear();
        _mapByTrack.Clear();
        _documents.Clear();
        ClearAnchors();
        NearbyCues.Clear();
        _activeKey = null;
        _persistAfterSelection = false;
        Tracks = [];
        ResetSyncPoints();
        SetTiming(SubtitleTiming.Identity, null);

        List<string> queued = [.. _queued];
        _queued.Clear();

        // Zapamćeni izbor za ovaj video ima prednost pred automatskim odabirom.
        SubtitleState? state = null;
        if (_videoPath != null && LoadState != null)
        {
            try
            {
                state = LoadState(_videoPath);
            }
            catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or InvalidOperationException)
            {
                Log.Subtitles.Warn($"Stanje titlova nije pročitano: {ex.Message}");
            }
        }

        if (state != null)
        {
            var timing = new SubtitleTiming(state.Speed, state.DelayMs);
            switch (state.Choice)
            {
                case "external" when state.ExternalPath != null && File.Exists(state.ExternalPath):
                    _timingByTrack[state.ExternalPath] = timing;
                    if (SubtitleTimeMap.Parse(state.TimeMap) is { } map)
                    {
                        _mapByTrack[Path.GetFullPath(state.ExternalPath)] = map;
                    }

                    queued.Add(state.ExternalPath);
                    break;
                case "embedded" when state.EmbeddedId is long id:
                    _timingByTrack[$"embedded:{id}"] = timing;
                    _player?.SetPropertyAsync("sid", id.ToString(CultureInfo.InvariantCulture));
                    break;
                case "none":
                    _player?.SetPropertyAsync("sid", "no");
                    break;
            }

            string parts = SubtitleTimeMap.Parse(state.TimeMap) is { } restoredMap ? $", po dijelovima: {restoredMap.Segments.Count}" : string.Empty;
            Log.Subtitles.Info($"Vraćeni zapamćeni titlovi ({state.Choice}, pomak {state.DelayMs} ms, brzina {state.Speed}{parts})");
        }

        if (e.TrackListJson != null)
        {
            UpdateTracks(e.TrackListJson);
        }

        _ = LoadThenSettleAsync(generation, queued, state, restoredExternalPath: state?.Choice == "external" ? state.ExternalPath : null);
    }

    private async Task LoadThenSettleAsync(int generation, List<string> queued, SubtitleState? restored, string? restoredExternalPath)
    {
        _lastSelectedLanguage = null;
        await LoadAsync(generation, queued, includeAutoFound: true, ForcedCodePageForQueued,
            keepRestoredChoice: restored != null, restoredExternalPath: restoredExternalPath);
        if (generation != _generation)
        {
            return;
        }

        string? language = restored?.Choice == "none" ? null : _lastSelectedLanguage ?? restored?.Language ?? SelectedTrack?.Language;
        Settled?.Invoke(this, new SubtitlesSettledEventArgs(_videoPath, language, restored != null));
    }

    private async Task LoadAsync(int generation, IReadOnlyList<string> explicitFiles, bool includeAutoFound, int? forcedCodePage,
        bool keepRestoredChoice = false, string? restoredExternalPath = null)
    {
        string? video = _videoPath;
        double? fps = _videoFps;
        bool auto = includeAutoFound && settings.AutoLoadSubtitles;

        List<(string Path, PreparedSubtitle? Prepared, string? Error)> results;
        var maps = new Dictionary<string, SubtitleTimeMap>(_mapByTrack, StringComparer.OrdinalIgnoreCase);
        try
        {
            results = await Task.Run(() =>
            {
                var files = new List<string>();
                if (auto && video != null && File.Exists(video))
                {
                    files.AddRange(SubtitleFileFinder.FindForVideo(video));
                }

                files.AddRange(explicitFiles);
                return files.Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select(f => TryPrepare(f, video, fps, explicitFiles.Contains(f, StringComparer.OrdinalIgnoreCase) ? forcedCodePage : null,
                        maps.TryGetValue(Path.GetFullPath(f), out SubtitleTimeMap? map) ? map : null))
                    .ToList();
            });
        }
        catch (Exception ex)
        {
            Log.Subtitles.Error("Učitavanje titlova", ex);
            return;
        }

        if (generation != _generation || _player is null || results.Count == 0)
        {
            return;
        }

        var ok = results.Where(r => r.Prepared != null).Select(r => r.Prepared!).ToList();

        // Ručno dodani titlovi uvijek se odabiru (redom kojim su dodani); inače biramo najbolji jezik
        // (vanjski ima prednost pred ugrađenim istog jezika), osim ako za ovaj video postoji zapamćeni izbor.
        PreparedSubtitle? select = explicitFiles
            .Select(f => ok.FirstOrDefault(p => string.Equals(p.OriginalPath, Path.GetFullPath(f), StringComparison.OrdinalIgnoreCase)))
            .FirstOrDefault(p => p != null);
        if (select != null)
        {
            _lastSelectedLanguage = select.Language;
        }

        if (select != null && !string.Equals(select.OriginalPath, restoredExternalPath, StringComparison.OrdinalIgnoreCase))
        {
            // Novi titlovi koje je korisnik sam dodao: zapamti izbor.
            _persistAfterSelection = true;
        }

        if (select is null && !keepRestoredChoice)
        {
            int embeddedBest = _tracks.Where(t => !t.IsExternal && !t.IsForced).Select(t => Priority(t.Language))
                .DefaultIfEmpty(int.MaxValue).Min();
            PreparedSubtitle? bestExternal = ok.Where(p => Priority(p.Language) < int.MaxValue)
                .OrderBy(p => Priority(p.Language)).FirstOrDefault();
            if (bestExternal != null && Priority(bestExternal.Language) <= embeddedBest)
            {
                select = bestExternal;
                // mpv još nije javio novi odabir (sub-add je asinkron), pa jezik pamtimo odmah.
                _lastSelectedLanguage = select.Language;
            }
        }

        foreach (PreparedSubtitle p in ok)
        {
            AddToPlayer(p, ReferenceEquals(p, select));
        }

        foreach (var failed in results.Where(r => r.Error != null))
        {
            Log.Subtitles.Warn($"Titlovi nisu učitani: {failed.Path}: {failed.Error}");
            Message?.Invoke(Loc.T("subs.loadFailed", Path.GetFileName(failed.Path), failed.Error));
        }

        if (select != null)
        {
            string details = select.IsBitmap ? SubtitleFormats.DisplayName(select.Format) : $"{SubtitleFormats.DisplayName(select.Format)}, {select.EncodingName}";
            Message?.Invoke(Loc.T("subs.loaded", SubtitleLanguage.DisplayName(select.Language), Path.GetFileName(select.OriginalPath), details));
            Log.Subtitles.Info($"Titlovi odabrani: {select.OriginalPath} ({details}, jezik {select.Language ?? "?"}, {select.CueCount} redaka)");
        }
    }

    private static (string Path, PreparedSubtitle? Prepared, string? Error) TryPrepare(string path, string? video, double? fps, int? codePage,
        SubtitleTimeMap? map)
    {
        try
        {
            PreparedSubtitle prepared = SubtitleService.Prepare(path, video, fps, codePage);
            return (path, map is null || prepared.IsBitmap ? prepared : SubtitleService.PrepareMapped(prepared, map, fps), null);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            return (path, null, ex.Message);
        }
    }

    private void AddToPlayer(PreparedSubtitle p, bool select)
    {
        if (_player is null)
        {
            return;
        }

        _prepared[p.PlaybackPath] = p;
        SubtitleTrackInfo? existing = _tracks.FirstOrDefault(t =>
            string.Equals(t.ExternalFile, p.PlaybackPath, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            if (select)
            {
                SelectTrack(existing);
            }

            return;
        }

        _player.CommandAsync("sub-add", p.PlaybackPath, select ? "select" : "auto",
            Path.GetFileName(p.OriginalPath), p.Language ?? string.Empty);
    }

    private int Priority(string? language)
    {
        if (language is null)
        {
            return int.MaxValue;
        }

        List<string> list = settings.SubtitleLanguages;
        int index = list.IndexOf(language);
        if (index < 0 && language == "sh")
        {
            int[] southSlavic = [list.IndexOf("hr"), list.IndexOf("sr"), list.IndexOf("bs")];
            index = southSlavic.Where(i => i >= 0).DefaultIfEmpty(-1).Min();
        }

        return index < 0 ? int.MaxValue : index;
    }

    private void OnMpvPropertyChanged(object? sender, MpvPropertyChangedEventArgs e)
    {
        switch (e.Name)
        {
            case "track-list":
                if (e.Value is string json)
                {
                    UpdateTracks(json);
                }

                break;
            case "sid":
                UpdateSelection(e.Value as string);
                break;
            case "sub-delay":
                // Potvrde naših vrijednosti se zanemaruju (vidi _sentDelays); prihvaća se samo promjena izvana.
                if (e.Value is double delay && !IsEcho(_sentDelays, delay))
                {
                    Timing = Timing with { DelayMs = Math.Round(delay * 1000, 3) };
                }

                break;
            case "sub-speed":
                if (e.Value is double speed && speed > 0 && !IsEcho(_sentSpeeds, speed))
                {
                    Timing = Timing with { Speed = speed };
                }

                break;
            case "sub-visibility":
                if (e.Value is bool visible)
                {
                    _visible = visible;
                    OnPropertyChanged(nameof(IsVisible));
                }

                break;
            case "sub-start":
                if (e.Value is double start)
                {
                    _lastSubStartSeconds = start;
                    _lastSubStartTicks = Environment.TickCount64;
                }

                break;
        }
    }

    private void UpdateTracks(string json)
    {
        var list = new List<SubtitleTrackInfo>();
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            foreach (JsonElement t in doc.RootElement.EnumerateArray())
            {
                if (Str(t, "type") != "sub")
                {
                    continue;
                }

                string? external = Str(t, "external-filename");
                PreparedSubtitle? prepared = external != null && _prepared.TryGetValue(external, out var p) ? p : null;
                list.Add(new SubtitleTrackInfo
                {
                    Id = t.TryGetProperty("id", out JsonElement id) && id.TryGetInt64(out long v) ? v : 0,
                    Title = Str(t, "title"),
                    Language = prepared?.Language ?? SubtitleLanguage.Normalize(Str(t, "lang")),
                    IsExternal = Bool(t, "external"),
                    ExternalFile = external,
                    Codec = Str(t, "codec"),
                    IsDefault = Bool(t, "default"),
                    IsForced = Bool(t, "forced"),
                    IsImage = Bool(t, "image"),
                    IsSelected = Bool(t, "selected"),
                    Prepared = prepared,
                });
            }
        }
        catch (JsonException ex)
        {
            Log.Subtitles.Warn($"track-list nije moguće pročitati: {ex.Message}");
            return;
        }

        SetTracks(list);
    }

    private void UpdateSelection(string? sid)
    {
        long? id = long.TryParse(sid, NumberStyles.Integer, CultureInfo.InvariantCulture, out long v) ? v : null;
        SetTracks(_tracks.Select(t => t with { IsSelected = id == t.Id }).ToList());
    }

    private void SetTracks(IReadOnlyList<SubtitleTrackInfo> list)
    {
        string? previousKey = SelectedTrack?.Key;
        Tracks = list;
        string? currentKey = SelectedTrack?.Key;
        bool known = false;

        // Promjena titla je tek kad se odabere drugi titl. Zamjena datoteke istog titla (ispravljena kopija za korekciju
        // po dijelovima, drugi encoding) nakratko nema odabira, ali nije promjena: točke poravnanja i pomak ostaju.
        bool switched = currentKey != null && !string.Equals(currentKey, _activeKey, StringComparison.OrdinalIgnoreCase);
        if (currentKey != null)
        {
            _activeKey = currentKey;
        }

        if (switched)
        {
            // Svaki titl pamti svoju sinkronizaciju (npr. hrvatski −2 s, engleski bez pomaka).
            ResetSyncPoints();
            ClearAnchors();
            NearbyCues.Clear();
            known = _timingByTrack.TryGetValue(currentKey!, out SubtitleTiming t);
            SetTiming(known ? t : SubtitleTiming.Identity, null);
            known |= _mapByTrack.ContainsKey(currentKey!);
            OnTimeMapChanged();
        }

        if (_persistAfterSelection && previousKey != currentKey)
        {
            _persistAfterSelection = false;
            PersistState();
        }

        if (switched && SelectedTrack is { Prepared: { IsBitmap: false } } external)
        {
            ExternalSelected?.Invoke(this, new ExternalSubtitleSelectedEventArgs(external, known));
        }
    }

    /// <summary>
    /// Rezultat automatske sinkronizacije: primijeni i zapamti za ovaj video (samo ako je taj titl još odabran).
    /// Uz <paramref name="map"/> (titlovi za drugo izdanje) prikazuje se ispravljena kopija, a <paramref name="timing"/> je tada
    /// obično bez korekcije. Bez karte se ranija korekcija po dijelovima uklanja (rezultat se uvijek računa od originala).
    /// </summary>
    public void ApplyAutoSync(SubtitleTrackInfo track, SubtitleTiming timing, SubtitleTimeMap? map = null)
    {
        if (SelectedTrack?.Key != track.Key)
        {
            return;
        }

        ResetSyncPoints();
        bool hadMap = _mapByTrack.TryGetValue(track.Key, out SubtitleTimeMap? previous);
        if (map != null)
        {
            _mapByTrack[track.Key] = map;
        }
        else
        {
            _mapByTrack.Remove(track.Key);
        }

        if (map != null ? previous?.Serialize() != map.Serialize() : hadMap)
        {
            ReplacePlayback(track, map);
        }

        SetTiming(timing, null);
        OnTimeMapChanged();
        PersistState();
    }

    /// <summary>
    /// Zamijeni datoteku koju mpv prikazuje za ovaj titl: ispravljena kopija (karta) ili obična UTF-8 kopija (bez karte).
    /// Ključ titla ostaje isti (originalna datoteka), pa se zapamćeni pomak i odabir ne mijenjaju.
    /// </summary>
    private void ReplacePlayback(SubtitleTrackInfo track, SubtitleTimeMap? map)
    {
        if (track.Prepared is not { IsBitmap: false } current || _player is null)
        {
            return;
        }

        int generation = _generation;
        string? video = _videoPath;
        double? fps = _videoFps;
        _ = Task.Run(() =>
        {
            // Isti encoding kao do sada (bez ponovnog prepoznavanja); oznaka ostaje ista, a ne "ručno".
            PreparedSubtitle plain = SubtitleService.Prepare(current.OriginalPath, video, fps, current.CodePage) with
            {
                EncodingName = current.EncodingName,
            };
            return map is null ? plain : SubtitleService.PrepareMapped(plain, map, fps);
        }).ContinueWith(task =>
        {
            if (generation != _generation || _player is null)
            {
                return;
            }

            if (task.Exception is { } ex)
            {
                Log.Subtitles.Warn($"Ispravljeni titlovi nisu pripremljeni: {ex.InnerException?.Message}");
                Message?.Invoke(Loc.T("subs.mappedFailed", ex.InnerException?.Message));
                return;
            }

            PreparedSubtitle updated = task.Result;
            if (string.Equals(updated.PlaybackPath, current.PlaybackPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _player.CommandAsync("sub-remove", track.Id.ToString(CultureInfo.InvariantCulture));
            AddToPlayer(updated, select: true);
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void OnTimeMapChanged()
    {
        OnPropertyChanged(nameof(TimeMap));
        OnPropertyChanged(nameof(HasCorrection));
        OnPropertyChanged(nameof(TimingSummary));
    }

    private void TryCompletePoint()
    {
        if (_audioMarkMs is not double target || _subtitleMarkOriginalMs is not double original)
        {
            return;
        }

        _audioMarkMs = null;
        _subtitleMarkOriginalMs = null;

        if (_firstPoint is { } first && Math.Abs(original - first.Original) >= 60_000)
        {
            SubtitleTiming two = SubtitleTiming.FromTwoPoints(first.Original, first.Target, original, target);
            if (two.Speed is < 0.85 or > 1.15)
            {
                _firstPoint = null;
                SetSyncStatus(() => Loc.T("zx.invalidStatus"));
                Message?.Invoke(Loc.T("zx.invalidMessage"));
                return;
            }

            SetTiming(two, null);
            PersistState();
            double delay = Timing.DelayMs;
            SetSyncStatus(() => Loc.T("zx.twoPointsStatus", two.Speed.ToString("0.0000", Loc.Culture), FormatDelay(delay)));
            Message?.Invoke(Loc.T("zx.twoPointsMessage", two.Speed.ToString("0.0000", Loc.Culture), DelayText));
            LogManual($"dvije točke (replika {original / 1000:0.000} s → {target / 1000:0.000} s): {TimingSummary}");
            return;
        }

        _firstPoint = (original, target);
        SetTiming(Timing.AlignPoint(original, target), null);
        PersistState();
        LogManual($"točka po replici (replika {original / 1000:0.000} s → {target / 1000:0.000} s): {TimingSummary}");
        SetSyncStatus(() => Loc.T("zx.firstPointStatus"));
        Message?.Invoke(Loc.T("zx.onePointMessage", DelayText));
    }

    private static string FormatDelay(double ms)
    {
        string sign = ms > 0.5 ? "+" : ms < -0.5 ? "−" : string.Empty;
        return $"{sign}{(Math.Abs(ms) / 1000).ToString("0.000", Loc.Culture)} s";
    }

    private static bool TryParseNumber(string? text, out double value)
    {
        string s = (text ?? string.Empty).Replace('−', '-').Replace(',', '.').Replace("ms", string.Empty)
            .Replace("×", string.Empty).Replace(" ", string.Empty);
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.True;
}

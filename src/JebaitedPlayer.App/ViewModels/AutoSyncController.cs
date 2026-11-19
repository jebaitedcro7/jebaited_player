using System.Diagnostics;
using System.Windows;
using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.Mpv;
using JebaitedPlayer.Services;
using JebaitedPlayer.Subtitles;
using JebaitedPlayer.Sync;

namespace JebaitedPlayer.ViewModels;

/// <summary>
/// Automatska sinkronizacija vanjskih titlova prema govoru (M5).
/// <list type="bullet">
/// <item>Kad se odabere vanjski tekstualni titl kojem sinkronizacija još nije poznata (nije zapamćena ni ručno postavljena),
/// zvuk se analizira u pozadini i titl se poravna. Video se za to vrijeme normalno reproducira.</item>
/// <item>Primjenjuje se samo provjeren rezultat (većina dijelova filma se slaže). Inače se titlovi ostavljaju kako jesu
/// i to se jasno kaže; titlovi koji se uopće ne poklapaju s govorom dobiju upozorenje.</item>
/// <item>Isti rezultat služi za provjeru titlova s interneta prije učitavanja.</item>
/// </list>
/// </summary>
public sealed class AutoSyncController : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly PlayerViewModel _vm;
    private readonly SpeechAnalysisService _speech;
    private CancellationTokenSource? _cts;
    private string? _audioTrack;
    private bool _isBusy;
    private Func<string> _status = () => Loc.T("autosync.notStarted");
    private double _progress;

    public AutoSyncController(AppSettings settings, PlayerViewModel vm, SpeechAnalysisService speech)
    {
        _settings = settings;
        _vm = vm;
        _speech = speech;
        SyncCommand = new RelayCommand(SyncSelected);
        _vm.Subtitles.ExternalSelected += (_, e) =>
        {
            if (_settings.AutoSyncSubtitles && !e.TimingKnown)
            {
                _ = SyncAsync(e.Track, manual: false);
            }
        };
        _speech.Progress += p => Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            ProgressValue = p;
            if (IsBusy)
            {
                SetStatus(() => Loc.T("autosync.analyzingProgress", p));
            }
        });
    }

    public event Action<string>? Message;

    public RelayCommand SyncCommand { get; }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetField(ref _isBusy, value);
    }

    /// <summary>Opis stanja na trenutnom jeziku (slaže se pri čitanju, pa prati promjenu jezika).</summary>
    public string Status => _status();

    /// <summary>Promjena jezika: status se ponovno složi.</summary>
    public void RefreshTexts() => OnPropertyChanged(nameof(Status));

    /// <summary>Opis rezultata analize na jeziku sučelja.</summary>
    public static string DetailText(SyncResult r) =>
        r.DetailKey.Length == 0 ? r.Detail : Loc.T("syncDetail." + r.DetailKey, r.DetailArgs);

    private void SetStatus(Func<string> status)
    {
        _status = status;
        OnPropertyChanged(nameof(Status));
    }

    public double ProgressValue
    {
        get => _progress;
        private set => SetField(ref _progress, value);
    }

    /// <summary>Zadnji rezultat (za dijagnostiku).</summary>
    public SyncResult? LastResult { get; private set; }

    public string? LastTrackKey { get; private set; }

    public long LastAlignMs { get; private set; }

    public long LastAnalysisMs => _speech.LastAnalysisMs;

    /// <summary>Broj završenih provjera (dijagnostika čeka da se poveća).</summary>
    public int CompletedRuns { get; private set; }

    /// <summary>Može li se trenutni video provjeriti prema govoru (uključeno u postavkama, ima zvuk).</summary>
    public bool CanVerify => _settings.AutoSyncSubtitles && !_vm.IsAudioOnly && _vm.Subtitles.VideoPath != null && _audioTrack != "no";

    public void Attach(MpvPlayer player)
    {
        player.PropertyChanged += (_, e) =>
        {
            if (e.Name == "aid")
            {
                _audioTrack = e.Value as string;
            }
        };
        // Novi video: stara analiza više ne treba.
        player.StartFile += (_, _) => Reset();
    }

    /// <summary>Prekini analizu i zaboravi rezultat (novi video ili povratak na početni ekran).</summary>
    public void Reset()
    {
        _cts?.Cancel();
        _speech.Cancel();
        LastResult = null;
        LastTrackKey = null;
        IsBusy = false;
        ProgressValue = 0;
        SetStatus(() => Loc.T("autosync.notStarted"));
    }

    /// <summary>Ručno pokretanje (tipka A, izbornik, prozor sinkronizacije) za odabrane titlove.</summary>
    public void SyncSelected()
    {
        SubtitleTrackInfo? track = _vm.Subtitles.SelectedTrack;
        if (track?.Prepared is not { IsBitmap: false })
        {
            Message?.Invoke(Loc.T("autosync.onlyExternal"));
            return;
        }

        _ = SyncAsync(track, manual: true);
    }

    /// <summary>Provjera titlova (npr. s interneta) prije učitavanja. Null ako provjera nije moguća.</summary>
    public async Task<SyncResult?> VerifyAsync(IReadOnlyList<Cue> cues, CancellationToken ct)
    {
        if (!CanVerify || _vm.Subtitles.VideoPath is not { } video)
        {
            return null;
        }

        if (cues.Count < 15)
        {
            return new SyncResult { Verdict = SyncVerdict.NotEnoughData, Detail = $"premalo replika ({cues.Count})", DetailKey = "fewCues", DetailArgs = [cues.Count] };
        }

        SpeechTrack speech = await _speech.GetAsync(video, AudioTrackForAnalysis, ct);
        return await Task.Run(() => SubtitleAligner.Align(speech, cues), ct);
    }

    private string? AudioTrackForAnalysis => _audioTrack is { } a && long.TryParse(a, out _) ? a : null;

    private async Task SyncAsync(SubtitleTrackInfo track, bool manual)
    {
        SubtitleController subs = _vm.Subtitles;
        if (subs.VideoPath is not { } video || track.Prepared is not { } prepared)
        {
            return;
        }

        if (_vm.IsAudioOnly || _audioTrack == "no")
        {
            if (manual)
            {
                Message?.Invoke(Loc.T("autosync.noAudio"));
            }

            return;
        }

        IReadOnlyList<Cue> cues = prepared.Timings;
        if (cues.Count < 15)
        {
            int count = cues.Count;
            SetStatus(() => Loc.T("autosync.tooFewCues", count));
            if (manual)
            {
                Message?.Invoke(Status);
            }

            return;
        }

        _cts?.Cancel();
        var cts = new CancellationTokenSource();
        _cts = cts;
        SubtitleTiming before = subs.Timing;
        SubtitleTimeMap? beforeMap = subs.TimeMap;
        IsBusy = true;
        SetStatus(() => Loc.T("autosync.analyzing"));
        if (manual)
        {
            Message?.Invoke(Loc.T("autosync.syncing"));
        }

        try
        {
            SpeechTrack speech = await _speech.GetAsync(video, AudioTrackForAnalysis, cts.Token);
            SetStatus(() => Loc.T("autosync.aligning"));
            var sw = Stopwatch.StartNew();
            SyncResult r = await Task.Run(() => SubtitleAligner.Align(speech, cues), cts.Token);
            LastAlignMs = sw.ElapsedMilliseconds;
            if (cts.IsCancellationRequested || subs.VideoPath != video || subs.SelectedTrack?.Key != track.Key)
            {
                return;
            }

            LastResult = r;
            LastTrackKey = track.Key;
            Log.Subtitles.Info($"Sinkronizacija prema govoru ({Path.GetFileName(prepared.OriginalPath)}): {r.Verdict}, brzina {r.Speed:0.000000}, " +
                     $"pomak {r.Offset:0.000} s, dokaz {r.Evidence:0.000}, z {r.FinalScore:0.0}, {r.Detail}, poravnanje {LastAlignMs} ms" +
                     (r.IsPiecewise ? "; dijelovi: " + DescribeSegments(r) : string.Empty));
            Apply(r, track, before, beforeMap, manual);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or MpvException or DllNotFoundException or InvalidOperationException)
        {
            string error = ex.Message;
            SetStatus(() => Loc.T("autosync.analysisFailed", error));
            Log.Subtitles.Warn(Status);
            if (manual)
            {
                Message?.Invoke(Status);
            }
        }
        finally
        {
            if (_cts == cts)
            {
                IsBusy = false;
                CompletedRuns++;
            }
        }
    }

    private void Apply(SyncResult r, SubtitleTrackInfo track, SubtitleTiming before, SubtitleTimeMap? beforeMap, bool manual)
    {
        SubtitleController subs = _vm.Subtitles;
        string Parts() => Loc.T("autosync.agree", r.AgreeingWindows, r.Windows);
        switch (r.Verdict)
        {
            case SyncVerdict.InSync:
            case SyncVerdict.Corrected:
            case SyncVerdict.CorrectedInParts:
            {
                if (!manual && (subs.Timing != before || !ReferenceEquals(subs.TimeMap, beforeMap)))
                {
                    SetStatus(() => Loc.T("autosync.manualWins"));
                    Message?.Invoke(Loc.T("autosync.manualWinsMessage"));
                    return;
                }

                Func<string> what;
                if (r.Verdict == SyncVerdict.CorrectedInParts)
                {
                    // Titlovi za drugo izdanje: svaki dio dobije svoj pomak (ispravljena kopija), mpv bez dodatnog pomaka.
                    var map = new SubtitleTimeMap(r.Segments.Select(s => new SubtitleTimeMap.Segment(s.From * 1000, s.Speed, s.Offset * 1000)));
                    subs.ApplyAutoSync(track, SubtitleTiming.Identity, map);
                    int count = r.Segments.Count;
                    double min = map.MinOffsetMs / 1000, max = map.MaxOffsetMs / 1000;
                    what = () => Loc.T("autosync.whatParts", count, FormatSeconds(min), FormatSeconds(max));
                }
                else
                {
                    SubtitleTiming target = r.Verdict == SyncVerdict.InSync ? SubtitleTiming.Identity : new SubtitleTiming(r.Speed, r.Offset * 1000);
                    subs.ApplyAutoSync(track, target);
                    what = r.Verdict == SyncVerdict.InSync
                        ? () => Loc.T("autosync.whatInSync")
                        : () => Loc.T("autosync.whatSynced", Describe(target));
                }

                SetStatus(() => Loc.T("autosync.statusResult", what(), Parts()));
                Message?.Invoke(Loc.T("autosync.messageResult", what()));
                break;
            }

            case SyncVerdict.Uncertain:
                SetStatus(() => Loc.T("autosync.uncertainStatus", DetailText(r)));
                Message?.Invoke(Loc.T("autosync.uncertainMessage", DetailText(r)));
                break;

            case SyncVerdict.Mismatch:
                SetStatus(() => Loc.T("autosync.mismatchStatus"));
                Message?.Invoke(Loc.T("autosync.mismatchMessage"));
                break;

            default:
                SetStatus(() => Loc.T("autosync.notEnoughStatus", DetailText(r)));
                if (manual)
                {
                    Message?.Invoke(Status);
                }

                break;
        }
    }

    private static string FormatSeconds(double seconds)
    {
        string sign = seconds > 0.005 ? "+" : seconds < -0.005 ? "−" : string.Empty;
        return $"{sign}{Math.Abs(seconds).ToString("0.0", Loc.Culture)} s";
    }

    private static string DescribeSegments(SyncResult r) => string.Join(" | ", r.Segments.Select(s =>
        $"{FormatSeconds(s.Offset)} od {TimeFormat.Format(s.From, forceHours: false)}{(s.Confirmed ? string.Empty : " (slab dokaz)")}"));

    public static string Describe(SubtitleTiming t)
    {
        string sign = t.DelayMs > 0.5 ? "+" : t.DelayMs < -0.5 ? "−" : string.Empty;
        string text = Loc.T("timing.offset", sign + (Math.Abs(t.DelayMs) / 1000).ToString("0.00", Loc.Culture));
        if (Math.Abs(t.Speed - 1) > 1e-6)
        {
            string speed = t.Speed.ToString("0.0000", Loc.Culture);
            text += FrameRatePreset.Find(t.Speed, 0.0004) is { } preset
                ? Loc.T("timing.speedPreset", speed, preset.Description)
                : Loc.T("timing.speed", speed);
        }

        return text;
    }
}

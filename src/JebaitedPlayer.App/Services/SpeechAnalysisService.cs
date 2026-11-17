using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.Mpv;
using JebaitedPlayer.Sync;

namespace JebaitedPlayer.Services;

/// <summary>
/// Gdje se u videu govori. Zvuk izvlači drugi, nevidljivi mpv (brže od stvarnog vremena), WebRTC VAD označi govor,
/// a rezultat (~10 KB po satu) se sprema na disk, pa se isti video ne analizira ponovno.
/// <para>Jedna analiza po videu i audio traci: više zahtjeva dijeli isti zadatak. Promjena videa prekida staru analizu.</para>
/// </summary>
public sealed class SpeechAnalysisService(string cacheDir)
{
    private readonly object _gate = new();
    private string? _key;
    private Task<SpeechTrack>? _task;
    private CancellationTokenSource? _cts;

    /// <summary>Napredak trenutne analize (0–1), poziva se iz pozadinske dretve.</summary>
    public event Action<double>? Progress;

    /// <summary>Trajanje zadnje analize zvuka u ms (0 = iz predmemorije).</summary>
    public long LastAnalysisMs { get; private set; }

    public bool IsBusy
    {
        get
        {
            lock (_gate)
            {
                return _task is { IsCompleted: false };
            }
        }
    }

    public Task<SpeechTrack> GetAsync(string videoPath, string? audioTrack, CancellationToken ct)
    {
        string key = CacheKey(videoPath, audioTrack);
        lock (_gate)
        {
            if (_key != key || _task is null || _task.IsFaulted || _task.IsCanceled)
            {
                _cts?.Cancel();
                _cts = new CancellationTokenSource();
                _key = key;
                CancellationToken token = _cts.Token;
                _task = Task.Run(() => AnalyzeAsync(videoPath, audioTrack, key, token), token);
            }

            return _task.WaitAsync(ct);
        }
    }

    /// <summary>Prekida analizu (npr. otvoren je drugi video).</summary>
    public void Cancel()
    {
        lock (_gate)
        {
            _cts?.Cancel();
            _key = null;
            _task = null;
        }
    }

    public void CleanupCache(TimeSpan maxAge)
    {
        try
        {
            if (!Directory.Exists(cacheDir))
            {
                return;
            }

            DateTime limit = DateTime.UtcNow - maxAge;
            foreach (string file in Directory.EnumerateFiles(cacheDir, "*.vad"))
            {
                if (File.GetLastAccessTimeUtc(file) < limit && File.GetLastWriteTimeUtc(file) < limit)
                {
                    File.Delete(file);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Čišćenje analize govora: {ex.Message}");
        }
    }

    private async Task<SpeechTrack> AnalyzeAsync(string videoPath, string? audioTrack, string key, CancellationToken ct)
    {
        string cacheFile = Path.Combine(cacheDir, key + ".vad");
        if (TryLoad(cacheFile) is { } cached)
        {
            LastAnalysisMs = 0;
            Progress?.Invoke(1);
            return cached;
        }

        var sw = Stopwatch.StartNew();
        using var detector = new SpeechDetector();

        // Samo središnji kanal (dijalog) kad postoji: uz sve kanale detektor i glazbu i efekte smatra govorom.
        AudioExtraction extraction = await MpvAudioExtractor.ExtractAsync(videoPath, audioTrack, null, null,
            (buffer, count) => detector.Push(buffer, count), new SyncProgress(p => Progress?.Invoke(p)), ct,
            AudioChannelMode.Dialogue).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        SpeechTrack speech = detector.Finish();
        LastAnalysisMs = sw.ElapsedMilliseconds;
        string channels = extraction.DialogueOnly ? "središnji kanal" : "svi kanali";
        Log.Info($"Analiza govora: {extraction.Samples / 16000.0:0} s zvuka za {sw.Elapsed.TotalSeconds:0.0} s, govor {speech.SpeechRatio:P0}, " +
                 $"{channels} (izvor {extraction.ChannelLayout ?? "?"}) ({Path.GetFileName(videoPath)})");

        try
        {
            Directory.CreateDirectory(cacheDir);
            string temp = cacheFile + ".tmp";
            using (FileStream fs = File.Create(temp))
            {
                speech.Save(fs);
            }

            File.Move(temp, cacheFile, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Analiza govora nije spremljena: {ex.Message}");
        }

        return speech;
    }

    private static SpeechTrack? TryLoad(string file)
    {
        try
        {
            if (!File.Exists(file))
            {
                return null;
            }

            using FileStream fs = File.OpenRead(file);
            return SpeechTrack.Load(fs);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or EndOfStreamException or UnauthorizedAccessException)
        {
            Log.Warn($"Predmemorija analize govora nije valjana: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Ključ: putanja, veličina i vrijeme izmjene datoteke te audio traka (nova verzija datoteke = nova analiza).
    /// v2: analizira se samo središnji kanal, pa se analize iz ranijih verzija (svi kanali) ne koriste.
    /// </summary>
    private static string CacheKey(string videoPath, string? audioTrack)
    {
        var info = new FileInfo(videoPath);
        string raw = $"{info.FullName.ToUpperInvariant()}|{(info.Exists ? info.Length : 0)}|{(info.Exists ? info.LastWriteTimeUtc.Ticks : 0)}|{audioTrack ?? "auto"}|v2";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)))[..32].ToLowerInvariant();
    }

    /// <summary>IProgress bez SynchronizationContexta (poziva se izravno u pozadinskoj dretvi).</summary>
    private sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        private int _lastPercent = -1;

        public void Report(double value)
        {
            int percent = (int)(value * 100);
            if (percent != _lastPercent)
            {
                _lastPercent = percent;
                report(value);
            }
        }
    }
}

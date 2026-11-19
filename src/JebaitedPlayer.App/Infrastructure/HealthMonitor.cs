using System.Diagnostics;
using System.Windows.Threading;
using JebaitedPlayer.Diagnostics;

namespace JebaitedPlayer.Infrastructure;

/// <summary>
/// Jedan lagani timer (5 s) na dretvi iz poola, samo dok je player pokrenut:
/// (1) provjera odziva sučelja (ping preko Dispatchera; kašnjenje &gt; 3 s je upozorenje, &gt; 10 s greška),
/// (2) svakih 30 s memorija i CPU procesa (upozorenje pri visokoj potrošnji ili velikom rastu), svakih 5 min kratki INFO uzorak.
/// Ne dira druge procese.
/// </summary>
internal sealed class HealthMonitor : IDisposable
{
    private const int PeriodMs = 5000;
    private const long RamWarnBytes = 1500L * 1024 * 1024;
    private const long RamGrowthWarnBytes = 300L * 1024 * 1024;

    private readonly Dispatcher _dispatcher;
    private readonly Func<bool> _isPlaying;
    private readonly Process _process = Process.GetCurrentProcess();
    private Timer? _timer;
    private int _tick;
    private long _pingPostedAt;
    private bool _hangReported;
    private TimeSpan _lastCpu;
    private long _lastCpuAt;
    private int _highCpuSamples;
    private bool _ramWarned;
    private long _ramBaseline;
    private long _ramBaselineAt;

    public HealthMonitor(Dispatcher dispatcher, Func<bool> isPlaying)
    {
        _dispatcher = dispatcher;
        _isPlaying = isPlaying;
    }

    public void Start()
    {
        _lastCpu = TotalCpu();
        _lastCpuAt = Stopwatch.GetTimestamp();
        _timer = new Timer(_ => Tick(), null, PeriodMs, PeriodMs);
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
        _process.Dispose();
    }

    private TimeSpan TotalCpu()
    {
        _process.Refresh();
        return _process.TotalProcessorTime;
    }

    private void Tick()
    {
        try
        {
            CheckUi();
            if (++_tick % 6 == 0)
            {
                SampleResources();
            }
        }
        catch (Exception)
        {
            // Nadzor nikad ne smije ugroziti player.
        }
    }

    private void CheckUi()
    {
        long posted = Interlocked.Read(ref _pingPostedAt);
        if (posted != 0)
        {
            // Prethodni ping još nije obrađen: sučelje ne odgovara.
            long waited = Environment.TickCount64 - posted;
            if (waited > 10_000 && !_hangReported)
            {
                _hangReported = true;
                DiagLog.Error(LogCat.Ui, "ui.hang", nameof(HealthMonitor), "UI thread not responding", null, ("waitedMs", waited));
            }
            else if (waited > 3_000 && !_hangReported)
            {
                DiagLog.Warn(LogCat.Ui, "ui.slow", nameof(HealthMonitor), "UI thread is slow to respond", ("waitedMs", waited));
            }

            return;
        }

        long now = Environment.TickCount64;
        Interlocked.Exchange(ref _pingPostedAt, now);
        _dispatcher.BeginInvoke(DispatcherPriority.Normal, () =>
        {
            long late = Environment.TickCount64 - Interlocked.Exchange(ref _pingPostedAt, 0);
            if (_hangReported)
            {
                _hangReported = false;
                DiagLog.Warn(LogCat.Ui, "ui.hang.recovered", nameof(HealthMonitor), "UI thread responding again", ("hungMs", late));
            }
            else if (late > 3_000)
            {
                DiagLog.Warn(LogCat.Ui, "ui.slow", nameof(HealthMonitor), "UI thread responded late", ("latencyMs", late));
            }
        });
    }

    private void SampleResources()
    {
        _process.Refresh();
        long ws = _process.WorkingSet64;
        long priv = _process.PrivateMemorySize64;
        TimeSpan cpu = _process.TotalProcessorTime;
        long nowTs = Stopwatch.GetTimestamp();
        double wallSec = (nowTs - _lastCpuAt) / (double)Stopwatch.Frequency;
        double cpuPct = wallSec > 0 ? (cpu - _lastCpu).TotalSeconds / wallSec / Environment.ProcessorCount * 100 : 0;
        _lastCpu = cpu;
        _lastCpuAt = nowTs;

        long nowTick = Environment.TickCount64;
        if (_ramBaselineAt == 0 || nowTick - _ramBaselineAt > 300_000)
        {
            _ramBaseline = ws;
            _ramBaselineAt = nowTick;
        }

        bool playing = _isPlaying();
        DiagLog.Debug(LogCat.Stability, "health.sample", nameof(HealthMonitor), "Resource sample",
            ("workingSetMB", ws >> 20), ("privateMB", priv >> 20), ("cpuPct", Math.Round(cpuPct, 1)),
            ("threads", _process.Threads.Count), ("handles", _process.HandleCount), ("gcMB", GC.GetTotalMemory(false) >> 20), ("playing", playing));

        if (_tick % 60 == 0)
        {
            DiagLog.Info(LogCat.Stability, "health.summary", nameof(HealthMonitor), "Resource summary",
                ("workingSetMB", ws >> 20), ("cpuPct", Math.Round(cpuPct, 1)), ("playing", playing));
        }

        if (ws > RamWarnBytes && !_ramWarned)
        {
            _ramWarned = true;
            DiagLog.Warn(LogCat.Stability, "memory.high", nameof(HealthMonitor), "High memory use", ("workingSetMB", ws >> 20));
        }
        else if (ws < RamWarnBytes * 8 / 10)
        {
            _ramWarned = false;
        }

        if (ws - _ramBaseline > RamGrowthWarnBytes)
        {
            DiagLog.Warn(LogCat.Stability, "memory.growth", nameof(HealthMonitor), "Memory grew quickly",
                ("fromMB", _ramBaseline >> 20), ("toMB", ws >> 20));
            _ramBaseline = ws;
            _ramBaselineAt = nowTick;
        }

        if (cpuPct > 80)
        {
            if (++_highCpuSamples == 2)
            {
                DiagLog.Warn(LogCat.Stability, "cpu.high", nameof(HealthMonitor), "High CPU use",
                    ("cpuPct", Math.Round(cpuPct, 1)), ("playing", playing));
            }
        }
        else
        {
            _highCpuSamples = 0;
        }
    }
}

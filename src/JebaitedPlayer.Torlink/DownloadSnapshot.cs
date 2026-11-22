namespace JebaitedPlayer.Torlink;

public enum DownloadState
{
    Queued,
    Downloading,
    Paused,
    Completed,
    Failed,
}

/// <summary>
/// Jedna stavka za sučelje. Postotak, brzina i status su iz Torlinkova API-ja; ukupna veličina iz <c>queue.json</c>/<c>history.json</c>;
/// <see cref="DownloadedBytes"/> i <see cref="EtaSeconds"/> su IZRAČUNATI iz njih (u sučelju se označavaju s "~").
/// </summary>
public sealed record DownloadItem(
    string Id, string Name, DownloadState State, double Percent, long TotalBytes, long DownloadedBytes, double Speed,
    double? EtaSeconds, int Peers, string Dir, string? Error, bool Seeding, long Uploaded, long CompletedAt);

/// <summary>Spoj API-ja i datoteka stanja u četiri popisa: Aktivna (preuzima ili pauzirano), Red, Završena, Neuspjela.</summary>
public sealed class DownloadSnapshot
{
    public static readonly DownloadSnapshot Empty = new([], [], [], []);

    public DownloadSnapshot(IReadOnlyList<DownloadItem> active, IReadOnlyList<DownloadItem> queued,
        IReadOnlyList<DownloadItem> completed, IReadOnlyList<DownloadItem> failed)
    {
        Active = active;
        Queued = queued;
        Completed = completed;
        Failed = failed;
    }

    public IReadOnlyList<DownloadItem> Active { get; }
    public IReadOnlyList<DownloadItem> Queued { get; }
    public IReadOnlyList<DownloadItem> Completed { get; }
    public IReadOnlyList<DownloadItem> Failed { get; }

    public int DownloadingCount => Active.Count(i => i.State == DownloadState.Downloading);

    /// <summary>Ima li posla koji ne smije stati (preuzima se ili čeka u redu).</summary>
    public bool HasWork => DownloadingCount > 0 || Queued.Count > 0;

    public double TotalSpeed => Active.Where(i => i.State == DownloadState.Downloading).Sum(i => i.Speed);

    public bool Contains(string id) =>
        Active.Concat(Queued).Concat(Completed).Concat(Failed).Any(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));

    public bool IsCompleted(string id) => Completed.Any(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));

    public static DownloadSnapshot Build(ApiStatus status, IReadOnlyDictionary<string, QueueEntry> queue, IReadOnlyList<HistoryEntry> history)
    {
        var active = new List<DownloadItem>();
        var queued = new List<DownloadItem>();
        var failed = new List<DownloadItem>();

        foreach (ApiDownload d in status.Downloads)
        {
            queue.TryGetValue(d.Id, out QueueEntry? q);
            long total = q?.TotalBytes ?? 0;
            double pct = Math.Clamp(d.Progress, 0, 100);
            long done = total > 0 ? (long)(total * pct / 100.0) : 0;
            DownloadState state = d.Status switch
            {
                "downloading" => DownloadState.Downloading,
                "paused" => DownloadState.Paused,
                "failed" => DownloadState.Failed,
                "completed" => DownloadState.Completed,
                _ => DownloadState.Queued,
            };
            double? eta = state == DownloadState.Downloading && d.Speed > 0 && total > 0 ? Math.Max(0, (total - done) / d.Speed) : null;
            var item = new DownloadItem(d.Id, d.Name, state, pct, total, done, state == DownloadState.Downloading ? d.Speed : 0, eta, d.Peers,
                q?.Dir ?? string.Empty, q?.Error, false, 0, 0);
            switch (state)
            {
                case DownloadState.Failed: failed.Add(item); break;
                case DownloadState.Queued: queued.Add(item); break;
                case DownloadState.Completed: break;
                default: active.Add(item); break;
            }
        }

        var seedById = status.Seeds.ToDictionary(s => s.Id, StringComparer.OrdinalIgnoreCase);
        var completed = new List<DownloadItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (HistoryEntry h in history.OrderByDescending(h => h.CompletedAt))
        {
            if (!seen.Add(h.Id) || active.Any(a => a.Id == h.Id) || queued.Any(a => a.Id == h.Id))
            {
                continue;
            }

            seedById.TryGetValue(h.Id, out ApiSeed? seed);
            completed.Add(new DownloadItem(h.Id, h.Name, DownloadState.Completed, 100, h.SizeBytes, h.SizeBytes, 0, null, seed?.Peers ?? 0,
                h.Dir, null, seed?.Status == "seeding", seed?.Uploaded ?? 0, h.CompletedAt));
        }

        return new DownloadSnapshot(active, queued, completed, failed);
    }

    public static string FormatEta(double? seconds)
    {
        if (seconds is not { } s || double.IsNaN(s) || double.IsInfinity(s))
        {
            return "—";
        }

        if (s < 90)
        {
            return $"~{Math.Max(1, (int)Math.Round(s))} s";
        }

        if (s < 5400)
        {
            return $"~{(int)Math.Round(s / 60)} min";
        }

        return s < 172800 ? $"~{s / 3600:0.#} h" : "> 2 d";
    }

    public static string FormatSize(long bytes)
    {
        if (bytes <= 0)
        {
            return "—";
        }

        return bytes >= 1_000_000_000 ? $"{bytes / 1e9:0.0} GB" : bytes >= 1_000_000 ? $"{bytes / 1e6:0} MB" : $"{bytes / 1e3:0} KB";
    }

    public static string FormatSpeed(double bytesPerSecond) =>
        bytesPerSecond >= 1_000_000 ? $"{bytesPerSecond / 1e6:0.0} MB/s" : $"{bytesPerSecond / 1e3:0} KB/s";
}

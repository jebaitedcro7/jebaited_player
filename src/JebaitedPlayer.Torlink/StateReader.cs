using System.Text.Json;

namespace JebaitedPlayer.Torlink;

public sealed record QueueEntry(string Id, string Status, long TotalBytes, long DownloadedBytes, string Dir, string? Error, long AddedAt);

public sealed record HistoryEntry(string Id, string Name, long SizeBytes, string Dir, long CompletedAt);

/// <summary>
/// Čita Torlinkove datoteke stanja (<c>queue.json</c>, <c>history.json</c>). Napomena iz testa: <c>queue.json</c> NIJE živ
/// (tijekom preuzimanja ostaje na 0 %), pa se iz njega uzima samo ukupna veličina, mapa i pogreška; napredak dolazi iz API-ja.
/// Čitanje je otporno: Torlink piše atomarno, ali datoteka može nedostajati ili biti u tijeku zamjene.
/// </summary>
public sealed class StateReader
{
    private readonly TorlinkPaths _paths;

    public StateReader(TorlinkPaths paths) => _paths = paths;

    public IReadOnlyDictionary<string, QueueEntry> ReadQueue()
    {
        var map = new Dictionary<string, QueueEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement e in ReadArray(_paths.QueueFile))
        {
            string id = ApiStatus.Str(e, "id");
            if (id.Length == 0)
            {
                continue;
            }

            map[id] = new QueueEntry(id, ApiStatus.Str(e, "status"), (long)ApiStatus.Num(e, "totalBytes"),
                (long)ApiStatus.Num(e, "downloadedBytes"), ApiStatus.Str(e, "dir"),
                e.TryGetProperty("error", out JsonElement er) && er.ValueKind == JsonValueKind.String ? er.GetString() : null,
                (long)ApiStatus.Num(e, "addedAt"));
        }

        return map;
    }

    public IReadOnlyList<HistoryEntry> ReadHistory()
    {
        var list = new List<HistoryEntry>();
        foreach (JsonElement e in ReadArray(_paths.HistoryFile))
        {
            string id = ApiStatus.Str(e, "id");
            if (id.Length == 0)
            {
                continue;
            }

            list.Add(new HistoryEntry(id, ApiStatus.Str(e, "name"), (long)ApiStatus.Num(e, "sizeBytes"), ApiStatus.Str(e, "dir"),
                (long)ApiStatus.Num(e, "completedAt")));
        }

        return list;
    }

    /// <summary>Ima li nedovršenih stavki koje Torlink nakon pokretanja sam nastavlja (preuzima ili čeka u redu)?</summary>
    public bool HasResumableWork() =>
        ReadQueue().Values.Any(q => q.Status is "downloading" or "queued");

    private static IEnumerable<JsonElement> ReadArray(string file)
    {
        try
        {
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using JsonDocument doc = JsonDocument.Parse(fs);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            // Elementi se kloniraju jer JsonDocument se zatvara ovdje.
            return doc.RootElement.EnumerateArray().Select(e => e.Clone()).ToList();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}

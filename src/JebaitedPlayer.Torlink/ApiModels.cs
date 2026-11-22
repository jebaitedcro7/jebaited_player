using System.Text.Json;

namespace JebaitedPlayer.Torlink;

/// <summary>Stavka iz <c>GET /downloads</c>. <c>Progress</c> je cijeli postotak 0–100 (ne razlomak): provjereno u stvarnom Torlinku.</summary>
public sealed record ApiDownload(string Id, string Name, string Status, double Progress, int Peers, double Speed);

public sealed record ApiSeed(string Id, string Name, string Status, int Peers, long Uploaded);

public sealed record ApiStatus(IReadOnlyList<ApiDownload> Downloads, IReadOnlyList<ApiSeed> Seeds, bool NetworkOnline, int? PeerPort, int Incoming)
{
    public static readonly ApiStatus Empty = new([], [], true, null, 0);

    /// <summary>Tolerantno čitanje: nedostajuća polja dobivaju zadane vrijednosti, nepoznata se ignoriraju.</summary>
    public static ApiStatus Parse(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        var downloads = new List<ApiDownload>();
        var seeds = new List<ApiSeed>();

        if (root.TryGetProperty("downloads", out JsonElement d) && d.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement e in d.EnumerateArray())
            {
                string id = Str(e, "id");
                if (id.Length == 0)
                {
                    continue;
                }

                downloads.Add(new ApiDownload(id, Str(e, "name"), Str(e, "status"), Num(e, "progress"), (int)Num(e, "peers"), Num(e, "speed")));
            }
        }

        if (root.TryGetProperty("seeds", out JsonElement s) && s.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement e in s.EnumerateArray())
            {
                string id = Str(e, "id");
                if (id.Length == 0)
                {
                    continue;
                }

                seeds.Add(new ApiSeed(id, Str(e, "name"), Str(e, "status"), (int)Num(e, "peers"), (long)Num(e, "uploaded")));
            }
        }

        bool online = true;
        int? port = null;
        int incoming = 0;
        if (root.TryGetProperty("network", out JsonElement n) && n.ValueKind == JsonValueKind.Object)
        {
            if (n.TryGetProperty("online", out JsonElement o) && o.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                online = o.GetBoolean();
            }

            if (n.TryGetProperty("peerPort", out JsonElement p) && p.ValueKind == JsonValueKind.Number)
            {
                port = p.GetInt32();
            }

            incoming = (int)Num(n, "incoming");
        }

        return new ApiStatus(downloads, seeds, online, port, incoming);
    }

    internal static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty;

    internal static double Num(JsonElement e, string name) =>
        e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out double d) ? d : 0;
}

public enum AddOutcome
{
    Added,
    Invalid,
    TooLarge,
    Unauthorized,
    Failed,
}

public sealed record TorrentResult(
    string InfoHash, string Name, long SizeBytes, int Seeders, int Leechers, int? NumFiles, string Source, string Magnet, long? Added);

public sealed record SourceStatus(string Id, bool Ok, int Count, string? Error, string? Code)
{
    public string Label => SourceNames.Label(Id);
}

public sealed record SearchResultSet(string Query, string Category, IReadOnlyList<TorrentResult> Results, IReadOnlyList<SourceStatus> Sources)
{
    /// <summary>Svi izvori su pali (Torlink tada vraća izlazni kod 1): nema interneta ili su svi nedostupni.</summary>
    public bool AllFailed => Sources.Count > 0 && Sources.All(s => !s.Ok);

    public static SearchResultSet Parse(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        var results = new List<TorrentResult>();
        if (root.TryGetProperty("results", out JsonElement r) && r.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement e in r.EnumerateArray())
            {
                string hash = ApiStatus.Str(e, "infoHash");
                string magnet = ApiStatus.Str(e, "magnet");
                if (hash.Length == 0 || magnet.Length == 0)
                {
                    continue;
                }

                int? files = e.TryGetProperty("numFiles", out JsonElement nf) && nf.ValueKind == JsonValueKind.Number ? nf.GetInt32() : null;
                long? added = e.TryGetProperty("added", out JsonElement ad) && ad.ValueKind == JsonValueKind.Number ? (long)ad.GetDouble() : null;
                results.Add(new TorrentResult(hash, ApiStatus.Str(e, "name"), (long)ApiStatus.Num(e, "sizeBytes"), (int)ApiStatus.Num(e, "seeders"),
                    (int)ApiStatus.Num(e, "leechers"), files, ApiStatus.Str(e, "source"), magnet, added));
            }
        }

        var sources = new List<SourceStatus>();
        if (root.TryGetProperty("sources", out JsonElement s) && s.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty p in s.EnumerateObject())
            {
                JsonElement v = p.Value;
                bool ok = v.TryGetProperty("ok", out JsonElement okEl) && okEl.ValueKind == JsonValueKind.True;
                string? err = v.TryGetProperty("error", out JsonElement er) && er.ValueKind == JsonValueKind.String ? er.GetString() : null;
                string? code = v.TryGetProperty("code", out JsonElement co) && co.ValueKind == JsonValueKind.String ? co.GetString() : null;
                sources.Add(new SourceStatus(p.Name, ok, (int)ApiStatus.Num(v, "count"), err, code));
            }
        }

        return new SearchResultSet(ApiStatus.Str(root, "query"), ApiStatus.Str(root, "category"), results, sources);
    }
}

public static class SourceNames
{
    public static string Label(string id) => id switch
    {
        "yts" => "YTS",
        "tpb-movies" or "tpb-tv" => "TPB",
        "eztv" => "EZTV",
        "nyaa" => "Nyaa",
        "subsplease" => "SubsPlease",
        "fitgirl" => "FitGirl",
        "bittorrented" => "BitTorrented",
        _ => id,
    };

    /// <summary>Izvori čiji popis nema podatke o seederima (0 znači "nepoznato", ne "mrtav").</summary>
    public static bool ReportsHealth(string id) => id is not ("fitgirl" or "subsplease");
}

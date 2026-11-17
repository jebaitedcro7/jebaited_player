using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace JebaitedPlayer.Online;

/// <summary>
/// Pretraga svih uključenih izvora paralelno (s vremenskim ograničenjem), cache rezultata,
/// bodovanje i preuzimanje. Greška jednog izvora ne ruši ostale.
/// </summary>
/// <param name="downloadCacheDir">
/// Preuzeti titlovi po izvoru i ID-u datoteke (30 dana): isti titl se ne preuzima ponovno, pa se ne troši dnevni limit
/// (OpenSubtitles: 20 preuzimanja na dan; prije je isti titl znao biti preuzet i 6 puta).
/// </param>
public sealed class SubtitleSearchService(IReadOnlyList<ISubtitleProvider> providers, string? cacheDir, TimeSpan? cacheAge = null,
    string? downloadCacheDir = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };
    private static readonly TimeSpan DownloadCacheAge = TimeSpan.FromDays(30);
    private readonly TimeSpan _cacheAge = cacheAge ?? TimeSpan.FromDays(3);

    public IReadOnlyList<ISubtitleProvider> Providers => providers;

    public bool AnyAvailable => providers.Any(p => p.IsAvailable);

    public async Task<SearchOutcome> SearchAsync(SubtitleQuery query, bool useCache, CancellationToken ct)
    {
        var tasks = providers.Where(p => p.IsAvailable).Select(p => SearchOneAsync(p, query, useCache, ct)).ToList();
        var results = await Task.WhenAll(tasks);
        var all = results.SelectMany(r => r.Candidates).ToList();
        return new SearchOutcome(SubtitleScorer.ScoreAndSort(all, query), results.Select(r => r.Result).ToList());
    }

    /// <summary>Je li zadnje preuzimanje došlo iz predmemorije (za log i poruke).</summary>
    public bool LastDownloadFromCache { get; private set; }

    public async Task<DownloadedSubtitle> DownloadAsync(SubtitleCandidate candidate, SubtitleQuery query, CancellationToken ct)
    {
        ISubtitleProvider provider = providers.FirstOrDefault(p => p.Name == candidate.Provider)
                                     ?? throw new ProviderException($"Izvor {candidate.Provider} nije dostupan.");
        string? file = DownloadCacheFile(candidate);
        if (file != null && TryReadDownload(file) is { } cached)
        {
            LastDownloadFromCache = true;
            return cached;
        }

        DownloadedSubtitle downloaded = await provider.DownloadAsync(candidate, query, ct);
        LastDownloadFromCache = false;
        if (file != null)
        {
            WriteDownload(file, downloaded);
        }

        return downloaded;
    }

    /// <summary>Briše spremljene rezultate pretrage koji sadrže API ključ (starije verzije su ga spremale u čistom tekstu).</summary>
    public static int PurgeCachedKeys(string cacheDir)
    {
        int deleted = 0;
        try
        {
            if (!Directory.Exists(cacheDir))
            {
                return 0;
            }

            foreach (string file in Directory.EnumerateFiles(cacheDir, "*.json"))
            {
                if (File.ReadAllText(file).Contains("api_key", StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(file);
                    deleted++;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return deleted;
    }

    private string? DownloadCacheFile(SubtitleCandidate c)
    {
        if (downloadCacheDir is null)
        {
            return null;
        }

        string key = $"{c.Provider}|{c.DownloadReference}";
        return Path.Combine(downloadCacheDir, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..24].ToLowerInvariant() + ".json");
    }

    private static DownloadedSubtitle? TryReadDownload(string file)
    {
        try
        {
            if (File.Exists(file) && DateTime.UtcNow - File.GetLastWriteTimeUtc(file) < DownloadCacheAge)
            {
                return JsonSerializer.Deserialize<DownloadedSubtitle>(File.ReadAllText(file), JsonOptions);
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
        }

        return null;
    }

    private static void WriteDownload(string file, DownloadedSubtitle downloaded)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, JsonSerializer.Serialize(downloaded, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private async Task<(ProviderResult Result, IReadOnlyList<SubtitleCandidate> Candidates)> SearchOneAsync(
        ISubtitleProvider provider, SubtitleQuery query, bool useCache, CancellationToken ct)
    {
        string? cacheFile = CacheFile(provider.Name, query);
        if (useCache && cacheFile != null && TryReadCache(cacheFile) is { } cached)
        {
            return (new ProviderResult(provider.Name, true, cached.Count, null, true), cached);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(25));
        try
        {
            IReadOnlyList<SubtitleCandidate> found = await provider.SearchAsync(query, timeout.Token);
            if (cacheFile != null)
            {
                WriteCache(cacheFile, found);
            }

            return (new ProviderResult(provider.Name, true, found.Count, null, false), found);
        }
        catch (ProviderException ex)
        {
            return (new ProviderResult(provider.Name, false, 0, ex.Message, false), []);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return (new ProviderResult(provider.Name, false, 0, $"{provider.Name} ne odgovara (isteklo vrijeme).", false), []);
        }
        catch (HttpRequestException ex)
        {
            return (new ProviderResult(provider.Name, false, 0, $"{provider.Name} nije dostupan ({ex.StatusCode?.ToString() ?? ex.Message}).", false), []);
        }
        catch (JsonException)
        {
            return (new ProviderResult(provider.Name, false, 0, $"{provider.Name} je vratio neočekivan odgovor.", false), []);
        }
    }

    private string? CacheFile(string provider, SubtitleQuery q)
    {
        if (cacheDir is null)
        {
            return null;
        }

        string key = $"{provider}|{q.Title.ToLowerInvariant()}|{q.Year}|{q.Season}|{q.Episode}|{q.MovieHash}|{string.Join(",", q.Languages)}";
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..24].ToLowerInvariant();
        return Path.Combine(cacheDir, hash + ".json");
    }

    private List<SubtitleCandidate>? TryReadCache(string file)
    {
        try
        {
            if (File.Exists(file) && DateTime.UtcNow - File.GetLastWriteTimeUtc(file) < _cacheAge)
            {
                string json = File.ReadAllText(file);

                // Starije verzije spremale su SubDL poveznice s API ključem u čistom tekstu: takva datoteka se briše.
                if (json.Contains("api_key", StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(file);
                    return null;
                }

                return JsonSerializer.Deserialize<List<SubtitleCandidate>>(json, JsonOptions);
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
        }

        return null;
    }

    private static void WriteCache(string file, IReadOnlyList<SubtitleCandidate> candidates)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, JsonSerializer.Serialize(candidates, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}

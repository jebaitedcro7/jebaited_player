namespace JebaitedPlayer.Torlink;

public enum UpdateStep
{
    Check,
    Download,
    Verify,
    Backup,
    Replace,
    HealthCheck,
    Done,
    RolledBack,
}

public sealed record UpdateProgress(UpdateStep Step, double Fraction = 0);

public sealed record UpdateInfo(
    RuntimeManifest? Installed, RuntimeRelease? Runtime, UpstreamRelease? Upstream, bool Available, string? Error)
{
    /// <summary>Upstream je novija verzija od naše: naš paket još nije izgrađen (treba pričekati naš CI).</summary>
    public bool UpstreamAheadOfRuntime =>
        Upstream is not null && TorlinkVersion.TryParse(Upstream.Version, 1, out TorlinkVersion up) &&
        (Runtime is null || (up.Major, up.Minor, up.Patch).CompareTo((Runtime.Version.Major, Runtime.Version.Minor, Runtime.Version.Patch)) > 0);
}

public sealed record UpdateResult(bool Success, UpdateStep Step, bool RolledBack, string? Detail = null);

/// <summary>
/// Sigurno ažuriranje runtimea: samo naše izdanje s GitHuba, zip se provjerava prema SHA256SUMS.txt istog izdanja, a svaka
/// datoteka prema MANIFEST.json. Prije zamjene postojeći runtime se čuva (<c>runtime.prev</c>); ako zdravstvena provjera nove
/// verzije padne, automatski se vraća prethodna. Nikad automatski: uvijek ga pokreće korisnik.
/// </summary>
public sealed class Updater
{
    public const long MaxZipBytes = 400L * 1024 * 1024;

    private readonly TorlinkPaths _paths;
    private readonly RuntimeStore _store;
    private readonly IReleaseFeed _feed;

    public Updater(TorlinkPaths paths, RuntimeStore store, IReleaseFeed feed)
    {
        _paths = paths;
        _store = store;
        _feed = feed;
    }

    public async Task<UpdateInfo> CheckAsync(CancellationToken ct = default)
    {
        RuntimeManifest? installed = _store.Installed();
        UpstreamRelease? upstream = null;
        RuntimeRelease? runtime = null;
        string? error = null;

        try
        {
            upstream = await _feed.GetUpstreamLatestAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsNetworkError(ex, ct))
        {
            error = "upstream";
        }

        try
        {
            runtime = await _feed.GetRuntimeLatestAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsNetworkError(ex, ct))
        {
            error = error is null ? "runtime" : "both";
        }

        bool available = runtime is not null && (installed is null || runtime.Version > installed.Version);
        return new UpdateInfo(installed, runtime, upstream, available, error);
    }

    /// <param name="stopProcess">Zaustavlja Torlinkove procese prije zamjene datoteka (inače su zaključane).</param>
    /// <param name="healthCheck">Pokreće novu verziju (<c>--version</c>, <c>/health</c>); true = radi.</param>
    public async Task<UpdateResult> InstallAsync(RuntimeRelease release, IProgress<UpdateProgress>? progress, Func<Task> stopProcess,
        Func<Task<bool>> healthCheck, CancellationToken ct = default)
    {
        string zip = Path.Combine(_paths.DownloadCache, release.ZipName);
        try
        {
            progress?.Report(new UpdateProgress(UpdateStep.Download));
            string sums = await _feed.GetTextAsync(release.SumsUrl, ct).ConfigureAwait(false);
            string? expected = ParseSha256(sums, release.ZipName);
            if (expected is null)
            {
                return new UpdateResult(false, UpdateStep.Verify, false, "no-checksum");
            }

            var dl = new Progress<double>(f => progress?.Report(new UpdateProgress(UpdateStep.Download, f)));
            await _feed.DownloadAsync(release.ZipUrl, zip, MaxZipBytes, dl, ct).ConfigureAwait(false);

            progress?.Report(new UpdateProgress(UpdateStep.Verify));
            await stopProcess().ConfigureAwait(false);

            progress?.Report(new UpdateProgress(UpdateStep.Backup));
            var vp = new Progress<double>(f => progress?.Report(new UpdateProgress(UpdateStep.Verify, f)));
            InstallResult r = await Task.Run(() => _store.InstallFromZip(zip, expected, vp), ct).ConfigureAwait(false);
            if (!r.Success)
            {
                return new UpdateResult(false, r.Failure == InstallFailure.SwapFailed ? UpdateStep.Replace : UpdateStep.Verify, false, r.Failure + (r.Detail is null ? string.Empty : ": " + r.Detail));
            }

            progress?.Report(new UpdateProgress(UpdateStep.HealthCheck));
            bool healthy = await healthCheck().ConfigureAwait(false);
            if (!healthy)
            {
                bool back = await Task.Run(_store.AbortInstall, ct).ConfigureAwait(false);
                progress?.Report(new UpdateProgress(UpdateStep.RolledBack));
                return new UpdateResult(false, UpdateStep.HealthCheck, back, "health");
            }

            _store.CommitInstall();
            progress?.Report(new UpdateProgress(UpdateStep.Done, 1));
            return new UpdateResult(true, UpdateStep.Done, false);
        }
        catch (Exception ex) when (IsNetworkError(ex, ct))
        {
            return new UpdateResult(false, UpdateStep.Download, false, ex.GetType().Name);
        }
        finally
        {
            TryDelete(zip);
            TryDelete(zip + ".part");
        }
    }

    /// <summary>Izvuci SHA-256 za datoteku iz SHA256SUMS.txt (<c>&lt;hex&gt;  &lt;ime&gt;</c>, i oblik s "*" ispred imena).</summary>
    public static string? ParseSha256(string sums, string fileName)
    {
        foreach (string raw in sums.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length < 66)
            {
                continue;
            }

            string hash = line[..64];
            string rest = line[64..].Trim().TrimStart('*').Trim();
            if (string.Equals(rest, fileName, StringComparison.OrdinalIgnoreCase) && hash.All(Uri.IsHexDigit))
            {
                return hash.ToLowerInvariant();
            }
        }

        return null;
    }

    private static bool IsNetworkError(Exception ex, CancellationToken ct) =>
        !ct.IsCancellationRequested && ex is HttpRequestException or TaskCanceledException or InvalidDataException or IOException or System.Text.Json.JsonException;

    private static void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}

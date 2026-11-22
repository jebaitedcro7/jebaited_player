using System.IO.Compression;
using System.Security.Cryptography;

namespace JebaitedPlayer.Torlink;

public enum InstallFailure
{
    None,
    ZipHashMismatch,
    BadArchive,
    BadManifest,
    FileHashMismatch,
    UnlistedFile,
    SwapFailed,
}

public sealed record InstallResult(bool Success, InstallFailure Failure, string? Detail = null)
{
    public static InstallResult Ok { get; } = new(true, InstallFailure.None);
}

/// <summary>
/// Instalacija runtimea (Node + Torlink): provjera SHA-256 zipa, raspakiravanje u privremenu mapu bez izlaska iz nje, provjera
/// SHA-256 SVAKE datoteke prema <c>MANIFEST.json</c> (i odbijanje nepopisanih), pa zamjena uz čuvanje prethodne verzije.
/// Stanje (<c>state</c>) je izvan runtimea i nikad se ne dira.
/// </summary>
public sealed class RuntimeStore
{
    private readonly TorlinkPaths _paths;

    public RuntimeStore(TorlinkPaths paths) => _paths = paths;

    /// <summary>Instalirani runtime ili null (nema ga, oštećen manifest ili nedostaje node.exe/cli.cjs).</summary>
    public RuntimeManifest? Installed()
    {
        RuntimeManifest? m = RuntimeManifest.TryRead(_paths.ManifestFile);
        return m is not null && File.Exists(_paths.NodeExe) && File.Exists(_paths.CliScript) ? m : null;
    }

    public bool HasPrevious => Directory.Exists(_paths.PreviousDir) && RuntimeManifest.TryRead(Path.Combine(_paths.PreviousDir, RuntimeManifest.FileName)) is not null;

    public RuntimeManifest? PreviousManifest() => RuntimeManifest.TryRead(Path.Combine(_paths.PreviousDir, RuntimeManifest.FileName));

    public static string Sha256File(string file)
    {
        using FileStream fs = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
    }

    public InstallResult InstallFromZip(string zipPath, string expectedZipSha256, IProgress<double>? progress = null)
    {
        if (!string.Equals(Sha256File(zipPath), expectedZipSha256.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return new InstallResult(false, InstallFailure.ZipHashMismatch);
        }

        string staging = _paths.StagingDir;
        DeleteDir(staging);
        Directory.CreateDirectory(staging);
        try
        {
            InstallResult ex = Extract(zipPath, staging, progress);
            if (!ex.Success)
            {
                DeleteDir(staging);
                return ex;
            }

            InstallResult v = VerifyDirectory(staging);
            if (!v.Success)
            {
                DeleteDir(staging);
                return v;
            }
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            DeleteDir(staging);
            return new InstallResult(false, InstallFailure.BadArchive, e.Message);
        }

        return Swap();
    }

    /// <summary>Provjera cijelog direktorija prema njegovom manifestu (isti kod za instalaciju i za zdravstvenu provjeru).</summary>
    public static InstallResult VerifyDirectory(string dir)
    {
        RuntimeManifest? m = RuntimeManifest.TryRead(Path.Combine(dir, RuntimeManifest.FileName));
        if (m is null)
        {
            return new InstallResult(false, InstallFailure.BadManifest);
        }

        foreach ((string rel, string sha) in m.Files)
        {
            string? full = SafeCombine(dir, rel);
            if (full is null || !File.Exists(full))
            {
                return new InstallResult(false, InstallFailure.BadManifest, rel);
            }

            if (!string.Equals(Sha256File(full), sha, StringComparison.OrdinalIgnoreCase))
            {
                return new InstallResult(false, InstallFailure.FileHashMismatch, rel);
            }
        }

        var listed = new HashSet<string>(m.Files.Keys.Select(Normalize), StringComparer.OrdinalIgnoreCase) { RuntimeManifest.FileName };
        foreach (string file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
        {
            string rel = Normalize(Path.GetRelativePath(dir, file));
            if (!listed.Contains(rel))
            {
                return new InstallResult(false, InstallFailure.UnlistedFile, rel);
            }
        }

        return InstallResult.Ok;
    }

    private static InstallResult Extract(string zipPath, string target, IProgress<double>? progress)
    {
        using ZipArchive zip = ZipFile.OpenRead(zipPath);
        int total = Math.Max(1, zip.Entries.Count);
        int i = 0;
        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            i++;
            if (entry.FullName.EndsWith('/') || entry.Name.Length == 0)
            {
                continue;
            }

            string? dest = SafeCombine(target, entry.FullName);
            if (dest is null)
            {
                return new InstallResult(false, InstallFailure.BadArchive, entry.FullName);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            entry.ExtractToFile(dest, overwrite: false);
            progress?.Report(i / (double)total);
        }

        return InstallResult.Ok;
    }

    /// <summary>Spoj korijena i relativne putanje iz arhive; null ako bi izašao iz korijena (zip-slip), apsolutna putanja ili "..".</summary>
    internal static string? SafeCombine(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(':'))
        {
            return null;
        }

        string rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string full = Path.GetFullPath(Path.Combine(rootFull, relative.Replace('/', Path.DirectorySeparatorChar)));
        return full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    private static string Normalize(string rel) => rel.Replace('\\', '/');

    private InstallResult Swap()
    {
        try
        {
            // Lanac verzija: nova → runtime, dosadašnja → prev, dosadašnja prev → old (briše se tek nakon potvrde, vidi CommitInstall).
            DeleteDir(_paths.OldDir);
            if (Directory.Exists(_paths.PreviousDir))
            {
                MoveWithRetry(_paths.PreviousDir, _paths.OldDir);
            }

            bool hadCurrent = Directory.Exists(_paths.RuntimeDir);
            if (hadCurrent)
            {
                MoveWithRetry(_paths.RuntimeDir, _paths.PreviousDir);
            }

            try
            {
                MoveWithRetry(_paths.StagingDir, _paths.RuntimeDir);
            }
            catch
            {
                AbortInstall();
                throw;
            }

            return InstallResult.Ok;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new InstallResult(false, InstallFailure.SwapFailed, e.Message);
        }
    }

    /// <summary>Nova verzija je prošla zdravstvenu provjeru: stara "prije prethodne" više nije potrebna.</summary>
    public void CommitInstall() => DeleteDir(_paths.OldDir);

    /// <summary>
    /// Neuspjela nova verzija: baci je, vrati dosadašnju (prev → runtime) i lanac (old → prev). Zaključane datoteke se ponavljaju;
    /// vraća true samo kad je ispravna verzija opet na mjestu.
    /// </summary>
    public bool AbortInstall()
    {
        try
        {
            if (Directory.Exists(_paths.RuntimeDir))
            {
                DeleteDirWithRetry(_paths.RuntimeDir);
            }

            if (Directory.Exists(_paths.PreviousDir))
            {
                MoveWithRetry(_paths.PreviousDir, _paths.RuntimeDir);
            }

            if (Directory.Exists(_paths.OldDir))
            {
                MoveWithRetry(_paths.OldDir, _paths.PreviousDir);
            }

            return Installed() is not null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Prekinuta instalacija (pad playera usred ažuriranja): bez ispravnog runtimea vrati prethodni; inače očisti ostatke.</summary>
    public void RecoverIncompleteInstall()
    {
        if (Installed() is null)
        {
            if (HasPrevious)
            {
                AbortInstall();
            }

            DeleteDir(_paths.StagingDir);
        }
        else
        {
            DeleteDir(_paths.OldDir);
            DeleteDir(_paths.StagingDir);
        }
    }

    /// <summary>Vrati prethodnu verziju: trenutna postaje "prev" (da se vraćanje može poništiti).</summary>
    public bool Rollback()
    {
        if (!HasPrevious)
        {
            return false;
        }

        string temp = _paths.Root + Path.DirectorySeparatorChar + "runtime.swap";
        try
        {
            DeleteDir(temp);
            bool hadCurrent = Directory.Exists(_paths.RuntimeDir);
            if (hadCurrent)
            {
                MoveWithRetry(_paths.RuntimeDir, temp);
            }

            try
            {
                MoveWithRetry(_paths.PreviousDir, _paths.RuntimeDir);
            }
            catch
            {
                if (hadCurrent && !Directory.Exists(_paths.RuntimeDir) && Directory.Exists(temp))
                {
                    MoveWithRetry(temp, _paths.RuntimeDir);
                }

                throw;
            }

            if (hadCurrent)
            {
                MoveWithRetry(temp, _paths.PreviousDir);
            }

            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Premještanje mape s ponavljanjem: tek ugašen proces ili antivirus nakratko drže datoteke i Windows tada odbija premještanje.</summary>
    internal static void MoveWithRetry(string source, string destination, int attempts = 20, int delayMs = 250)
    {
        for (int i = 1; ; i++)
        {
            try
            {
                Directory.Move(source, destination);
                return;
            }
            catch (Exception e) when (i < attempts && e is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(delayMs);
            }
        }
    }

    private static void DeleteDirWithRetry(string dir, int attempts = 20, int delayMs = 250)
    {
        for (int i = 1; ; i++)
        {
            try
            {
                Directory.Delete(dir, recursive: true);
                return;
            }
            catch (Exception e) when (i < attempts && e is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(delayMs);
            }
        }
    }

    public void RemoveAll()
    {
        DeleteDir(_paths.RuntimeDir);
        DeleteDir(_paths.PreviousDir);
        DeleteDir(_paths.OldDir);
        DeleteDir(_paths.StagingDir);
        DeleteDir(_paths.DownloadCache);
    }

    private static void DeleteDir(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // zaključana datoteka: ostaje; sljedeća instalacija ponovno pokušava
        }
    }
}

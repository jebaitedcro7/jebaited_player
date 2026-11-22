namespace JebaitedPlayer.Torlink;

/// <summary>Provjera slobodnog prostora prije dodavanja: Torlink to sam ne javlja, a pun disk bi dao neuspjelo preuzimanje.</summary>
public static class DiskGuard
{
    public const long SafetyMarginBytes = 200L * 1024 * 1024;

    public sealed record Result(bool Enough, long NeededBytes, long FreeBytes, string Root);

    /// <param name="freeBytes">Zamjena za stvarno čitanje (testovi); vraća null ako se ne može odrediti.</param>
    public static Result Check(string targetDir, long sizeBytes, Func<string, long?>? freeBytes = null)
    {
        string full;
        try
        {
            full = Path.GetFullPath(targetDir);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new Result(true, sizeBytes, long.MaxValue, targetDir);
        }

        string root = Path.GetPathRoot(full) ?? full;
        long? free = (freeBytes ?? DefaultFree)(root);
        long needed = sizeBytes <= 0 ? 0 : sizeBytes + SafetyMarginBytes;

        // Ako se prostor ne može odrediti (npr. mrežni put), ne blokiramo: Torlink će sam javiti pogrešku.
        return new Result(free is null || sizeBytes <= 0 || free >= needed, needed, free ?? long.MaxValue, root);
    }

    private static long? DefaultFree(string root)
    {
        try
        {
            return new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

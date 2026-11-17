using System.Runtime.InteropServices;
using System.Text;

namespace JebaitedPlayer.Infrastructure;

/// <summary>
/// Kriptiranje API ključeva i lozinki preko Windows DPAPI (vezano uz tvoj Windows račun).
/// Na disku je samo kriptirani zapis; ključ se ne može pročitati s drugog računa ili računala.
/// </summary>
internal static class Secret
{
    private const string Prefix = "dpapi:";
    private const int CryptProtectUiForbidden = 0x1;
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("jebaited_player");

    public static string? Protect(string? plain)
    {
        if (string.IsNullOrEmpty(plain))
        {
            return null;
        }

        byte[]? encrypted = Transform(Encoding.UTF8.GetBytes(plain), protect: true);
        return encrypted is null ? null : Prefix + Convert.ToBase64String(encrypted);
    }

    public static string? Unprotect(string? stored)
    {
        if (string.IsNullOrEmpty(stored) || !stored.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            byte[]? plain = Transform(Convert.FromBase64String(stored[Prefix.Length..]), protect: false);
            return plain is null ? null : Encoding.UTF8.GetString(plain);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static byte[]? Transform(byte[] data, bool protect)
    {
        var input = new DataBlob();
        var entropy = new DataBlob();
        var output = new DataBlob();
        GCHandle dataHandle = GCHandle.Alloc(data, GCHandleType.Pinned);
        GCHandle entropyHandle = GCHandle.Alloc(Entropy, GCHandleType.Pinned);
        try
        {
            input.cbData = data.Length;
            input.pbData = dataHandle.AddrOfPinnedObject();
            entropy.cbData = Entropy.Length;
            entropy.pbData = entropyHandle.AddrOfPinnedObject();
            bool ok = protect
                ? CryptProtectData(ref input, "jebaited_player", ref entropy, 0, 0, CryptProtectUiForbidden, ref output)
                : CryptUnprotectData(ref input, 0, ref entropy, 0, 0, CryptProtectUiForbidden, ref output);
            if (!ok)
            {
                return null;
            }

            byte[] result = new byte[output.cbData];
            Marshal.Copy(output.pbData, result, 0, output.cbData);
            return result;
        }
        finally
        {
            dataHandle.Free();
            entropyHandle.Free();
            if (output.pbData != 0)
            {
                LocalFree(output.pbData);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int cbData;
        public nint pbData;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref DataBlob dataIn, string? description, ref DataBlob entropy,
        nint reserved, nint prompt, int flags, ref DataBlob dataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(ref DataBlob dataIn, nint description, ref DataBlob entropy,
        nint reserved, nint prompt, int flags, ref DataBlob dataOut);

    [DllImport("kernel32.dll")]
    private static extern nint LocalFree(nint handle);
}

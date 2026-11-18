using System.Runtime.InteropServices;
using System.Text;

namespace JebaitedPlayer.Infrastructure;

/// <summary>
/// Inačica iz Microsoft Storea (MSIX paket). Isti exe kao GitHub inačica; razlika se prepoznaje u Windowsu:
/// u paketu nadogradnje radi Store (Velopack se ne pokreće), vrste datoteka pridružuje manifest paketa (ne registry),
/// a podaci su u mapi paketa (<see cref="AppPaths.DataDir"/>).
/// </summary>
public static class AppPackage
{
    private const int ErrorSuccess = 0;
    private const int ErrorInsufficientBuffer = 122;

    /// <summary>Package family name (npr. jebaitedplayer.jebaitedplayer_5g6w8vrmj8ate) ili null izvan paketa.</summary>
    public static string? FamilyName { get; } = Detect();

    /// <summary>True kad proces radi iz MSIX paketa (Store ili lokalno registriran paket).</summary>
    public static bool IsStore => FamilyName != null;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetCurrentPackageFamilyName(ref int packageFamilyNameLength, StringBuilder? packageFamilyName);

    private static string? Detect()
    {
        try
        {
            // Bez paketa: APPMODEL_ERROR_NO_PACKAGE (15700). U paketu: prvo duljina (122), zatim ime.
            int length = 0;
            if (GetCurrentPackageFamilyName(ref length, null) != ErrorInsufficientBuffer)
            {
                return null;
            }

            var name = new StringBuilder(length);
            return GetCurrentPackageFamilyName(ref length, name) == ErrorSuccess && name.Length > 0 ? name.ToString() : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}

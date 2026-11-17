using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace JebaitedPlayer.Infrastructure;

/// <summary>
/// Registracija u Windowsu, samo za trenutnog korisnika (HKCU, ne treba administrator):
/// <list type="bullet">
/// <item>"Otvori pomoću → jebaited_player" za video i audio datoteke (<c>OpenWithProgids</c>),</item>
/// <item>jebaited_player na popisu "Zadane aplikacije" za video formate (<c>RegisteredApplications</c>),</item>
/// <item>pokretanje upisom "jebaited_player" u Win+R (<c>App Paths</c>).</item>
/// </list>
/// Zadani player se ovdje nikad ne postavlja: Windows to dopušta samo korisniku (<see cref="OpenDefaultAppsSettings"/>).
/// <para>Sve se piše pod <c>root</c> (zadano <c>Software</c>); testovi koriste zaseban ključ.</para>
/// </summary>
internal static class ShellIntegration
{
    public const string AppName = "jebaited_player";
    public const string ExeName = "jebaited_player.exe";
    public const string ProgIdPrefix = "jebaited_player.";
    public const string DefaultRoot = "Software";

    private const string Description = "Lagani video player s automatskim pronalaženjem i sinkronizacijom hrvatskih titlova.";

    /// <summary>Registriraj (ili osvježi) jebaited_player za zadani exe. Sigurno za ponavljanje.</summary>
    public static int Register(string exePath, string root = DefaultRoot)
    {
        exePath = Path.GetFullPath(exePath);
        string command = $"\"{exePath}\" \"%1\"";
        string icon = $"\"{exePath}\",0";
        string[] video = MediaFormats.Video.Select(e => "." + e).ToArray();
        string[] audio = MediaFormats.Audio.Select(e => "." + e).ToArray();
        RegistryKey hkcu = Registry.CurrentUser;

        // Stara registracija (npr. format koji više nije podržan) uklanja se prije nove.
        RemoveFileTypes(root);

        foreach (string ext in video.Concat(audio))
        {
            bool isVideo = video.Contains(ext);
            string progId = ProgIdPrefix + ext.TrimStart('.');
            using (RegistryKey key = hkcu.CreateSubKey($@"{root}\Classes\{progId}"))
            {
                key.SetValue(string.Empty, $"{ext.TrimStart('.').ToUpperInvariant()} {(isVideo ? "video" : "zvuk")}");
                key.SetValue("FriendlyTypeName", $"{ext.TrimStart('.').ToUpperInvariant()} {(isVideo ? "video" : "zvuk")} (jebaited_player)");
            }

            using (RegistryKey key = hkcu.CreateSubKey($@"{root}\Classes\{progId}\DefaultIcon"))
            {
                key.SetValue(string.Empty, icon);
            }

            WriteOpenVerb(hkcu, $@"{root}\Classes\{progId}", command);

            using RegistryKey openWith = hkcu.CreateSubKey($@"{root}\Classes\{ext}\OpenWithProgids");
            openWith.SetValue(progId, Array.Empty<byte>(), RegistryValueKind.None);
        }

        // "Otvori pomoću → Odaberi drugu aplikaciju" i naziv u izborniku.
        string application = $@"{root}\Classes\Applications\{ExeName}";
        using (RegistryKey key = hkcu.CreateSubKey(application))
        {
            key.SetValue("FriendlyAppName", AppName);
        }

        using (RegistryKey key = hkcu.CreateSubKey($@"{application}\DefaultIcon"))
        {
            key.SetValue(string.Empty, icon);
        }

        WriteOpenVerb(hkcu, application, command);
        using (RegistryKey key = hkcu.CreateSubKey($@"{application}\SupportedTypes"))
        {
            foreach (string ext in video.Concat(audio))
            {
                key.SetValue(ext, string.Empty);
            }
        }

        // "Zadane aplikacije": samo video formati, da "Postavi zadano" ne preuzme i glazbu.
        string capabilities = $@"{root}\{AppName}\Capabilities";
        using (RegistryKey key = hkcu.CreateSubKey(capabilities))
        {
            key.SetValue("ApplicationName", AppName);
            key.SetValue("ApplicationDescription", Description);
            key.SetValue("ApplicationIcon", icon);
        }

        using (RegistryKey key = hkcu.CreateSubKey($@"{capabilities}\FileAssociations"))
        {
            foreach (string ext in video)
            {
                key.SetValue(ext, ProgIdPrefix + ext.TrimStart('.'));
            }
        }

        using (RegistryKey key = hkcu.CreateSubKey($@"{root}\{AppName}"))
        {
            key.SetValue("RegisteredExtensions", video.Concat(audio).ToArray(), RegistryValueKind.MultiString);
            key.SetValue("ExePath", exePath);
        }

        using (RegistryKey key = hkcu.CreateSubKey($@"{root}\RegisteredApplications"))
        {
            key.SetValue(AppName, capabilities);
        }

        using (RegistryKey key = hkcu.CreateSubKey($@"{root}\Microsoft\Windows\CurrentVersion\App Paths\{ExeName}"))
        {
            key.SetValue(string.Empty, exePath);
            key.SetValue("Path", Path.GetDirectoryName(exePath)!);
        }

        NotifyShell(root);
        return video.Length + audio.Length;
    }

    /// <summary>Ukloni sve što je <see cref="Register"/> zapisao. Postavke i povijest gledanja ostaju.</summary>
    public static void Unregister(string root = DefaultRoot)
    {
        RegistryKey hkcu = Registry.CurrentUser;
        RemoveFileTypes(root);
        hkcu.DeleteSubKeyTree($@"{root}\Classes\Applications\{ExeName}", throwOnMissingSubKey: false);
        using (RegistryKey? key = hkcu.OpenSubKey($@"{root}\RegisteredApplications", writable: true))
        {
            key?.DeleteValue(AppName, throwOnMissingValue: false);
        }

        hkcu.DeleteSubKeyTree($@"{root}\{AppName}", throwOnMissingSubKey: false);
        hkcu.DeleteSubKeyTree($@"{root}\Microsoft\Windows\CurrentVersion\App Paths\{ExeName}", throwOnMissingSubKey: false);
        NotifyShell(root);
    }

    /// <summary>Exe na koji pokazuje registracija (null = nije registriran).</summary>
    public static string? RegisteredExe(string root = DefaultRoot)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey($@"{root}\{AppName}");
        return key?.GetValue("ExePath") as string;
    }

    public static bool IsRegisteredFor(string exePath, string root = DefaultRoot) =>
        RegisteredExe(root) is { } registered &&
        string.Equals(Path.GetFullPath(registered), Path.GetFullPath(exePath), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Otvori Postavke sustava Windows na stranici jebaited_playera u "Zadanim aplikacijama"
    /// (Windows 11; inače opću stranicu zadanih aplikacija). Odabir potvrđuje korisnik.
    /// </summary>
    public static void OpenDefaultAppsSettings()
    {
        foreach (string uri in new[] { $"ms-settings:defaultapps?registeredAppUser={AppName}", "ms-settings:defaultapps" })
        {
            try
            {
                Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
                return;
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                Log.Warn($"Postavke sustava Windows nisu otvorene ({uri}): {ex.Message}");
            }
        }
    }

    private static void WriteOpenVerb(RegistryKey hkcu, string baseKey, string command)
    {
        using (RegistryKey shell = hkcu.CreateSubKey($@"{baseKey}\shell"))
        {
            shell.SetValue(string.Empty, "open");
        }

        using (RegistryKey open = hkcu.CreateSubKey($@"{baseKey}\shell\open"))
        {
            open.SetValue(string.Empty, "Reproduciraj u jebaited_playeru");

            // Bez ovoga Explorer ne nudi naredbu za više od 15 označenih datoteka.
            open.SetValue("MultiSelectModel", "Player");
        }

        using RegistryKey cmd = hkcu.CreateSubKey($@"{baseKey}\shell\open\command");
        cmd.SetValue(string.Empty, command);
    }

    private static void RemoveFileTypes(string root)
    {
        RegistryKey hkcu = Registry.CurrentUser;
        var extensions = new HashSet<string>(MediaFormats.Video.Concat(MediaFormats.Audio).Select(e => "." + e), StringComparer.OrdinalIgnoreCase);
        using (RegistryKey? app = hkcu.OpenSubKey($@"{root}\{AppName}"))
        {
            if (app?.GetValue("RegisteredExtensions") is string[] earlier)
            {
                extensions.UnionWith(earlier);
            }
        }

        foreach (string ext in extensions)
        {
            using RegistryKey? openWith = hkcu.OpenSubKey($@"{root}\Classes\{ext}\OpenWithProgids", writable: true);
            if (openWith is null)
            {
                continue;
            }

            foreach (string name in openWith.GetValueNames().Where(n => n.StartsWith(ProgIdPrefix, StringComparison.OrdinalIgnoreCase)))
            {
                openWith.DeleteValue(name, throwOnMissingValue: false);
            }
        }

        using RegistryKey? classes = hkcu.OpenSubKey($@"{root}\Classes", writable: true);
        if (classes is null)
        {
            return;
        }

        foreach (string name in classes.GetSubKeyNames().Where(n => n.StartsWith(ProgIdPrefix, StringComparison.OrdinalIgnoreCase)))
        {
            classes.DeleteSubKeyTree(name, throwOnMissingSubKey: false);
        }
    }

    private static void NotifyShell(string root)
    {
        if (root == DefaultRoot)
        {
            // Explorer odmah osvježi "Otvori pomoću" i ikone.
            SHChangeNotify(0x08000000 /* SHCNE_ASSOCCHANGED */, 0x0000 /* SHCNF_IDLIST */, 0, 0);
        }
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, nint item1, nint item2);
}

using System.Diagnostics;
using System.IO;
using System.Reflection;
using JebaitedPlayer.Infrastructure;

namespace JebaitedPlayer.App.Tests;

[TestClass]
public sealed class OnlineSetupTests
{
    [TestMethod]
    public void AppKeyRoundTripAndDevBuildHasNone()
    {
        string encoded = AppSecrets.Encode("Abc123xyzKEY456-test");
        Assert.AreNotEqual("Abc123xyzKEY456-test", encoded);
        StringAssert.DoesNotMatch(encoded, new System.Text.RegularExpressions.Regex("Abc123"));
        Assert.AreEqual("Abc123xyzKEY456-test", AppSecrets.Decode(encoded));
        Assert.IsNull(AppSecrets.Decode("nije base64 !!"));
        Assert.IsNull(AppSecrets.Decode(""));

        // Razvojni build nema ključ aplikacije (dolazi samo iz secrets\ u skripti za izdanje).
        Assert.IsFalse(AppSecrets.HasOpenSubtitlesAppKey);
    }

    [TestMethod]
    public void ReleaseScriptUsesTheSameObfuscation()
    {
        // Isti postupak kao u tools/release.ps1 (PowerShell), da ključ iz izdanja player stvarno pročita.
        string script = File.ReadAllText(Path.Combine(RepoRoot(), "tools", "release.ps1"));
        StringAssert.Contains(script, "\"jebaited_player/opensubtitles\"");
        StringAssert.Contains(script, "-bxor $mask[$i % $mask.Length]");
        StringAssert.Contains(script, "-p:JebaitedOsAppKey=");

        string ps = """
            $key = 'Abc123xyzKEY456-test'
            $mask = [Text.Encoding]::ASCII.GetBytes("jebaited_player/opensubtitles")
            $bytes = [Text.Encoding]::UTF8.GetBytes($key)
            for ($i = 0; $i -lt $bytes.Length; $i++) { $bytes[$i] = $bytes[$i] -bxor $mask[$i % $mask.Length] }
            [Convert]::ToBase64String($bytes)
            """;
        var psi = new ProcessStartInfo("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", ps])
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using Process p = Process.Start(psi)!;
        string output = p.StandardOutput.ReadToEnd().Trim();
        p.WaitForExit(30_000);
        Assert.AreEqual(AppSecrets.Encode("Abc123xyzKEY456-test"), output);
    }

    [TestMethod]
    public void SavingEncryptsAndCountsAsPersonalSetup()
    {
        var o = new OnlineSettings { OpenSubtitlesEnabled = false, SubDlEnabled = false };
        Assert.IsFalse(OnlineSetup.HasPersonalSetup(o));

        OnlineSetup.SaveOpenSubtitles(o, "  korisnik ", "tajna lozinka", "  osobni-kljuc ");
        Assert.IsTrue(o.OpenSubtitlesEnabled);
        Assert.AreEqual("korisnik", o.OpenSubtitlesUsername);
        StringAssert.StartsWith(o.OpenSubtitlesPassword, "dpapi:");
        Assert.IsFalse(o.OpenSubtitlesPassword!.Contains("tajna"));
        Assert.AreEqual("tajna lozinka", Secret.Unprotect(o.OpenSubtitlesPassword));
        Assert.AreEqual("osobni-kljuc", OnlineSetup.OpenSubtitlesKey(o));
        Assert.IsTrue(OnlineSetup.HasPersonalSetup(o));

        var creds = OnlineSetup.OpenSubtitlesCredentials(o)!;
        Assert.AreEqual("osobni-kljuc", creds.ApiKey);
        Assert.AreEqual("korisnik", creds.Username);
        Assert.AreEqual("tajna lozinka", creds.Password);
    }

    [TestMethod]
    public void WithoutAnyKeyOpenSubtitlesIsNotSetUp()
    {
        // Razvojni build bez osobnog ključa: korisničko ime samo po sebi nije dovoljno.
        var o = new OnlineSettings { SubDlEnabled = false, OpenSubtitlesUsername = "korisnik" };
        Assert.IsNull(OnlineSetup.OpenSubtitlesCredentials(o));
        Assert.IsFalse(OnlineSetup.HasPersonalSetup(o));
        StringAssert.Contains(OnlineSetup.Summary(o), "OpenSubtitles: nije postavljen");
    }

    [TestMethod]
    public void SubDlAndTitloviCount()
    {
        var o = new OnlineSettings { OpenSubtitlesEnabled = false };
        OnlineSetup.SaveSubDl(o, " \"abcDEF123\" ".Trim().Trim('"'));
        Assert.AreEqual("abcDEF123", Secret.Unprotect(o.SubDlApiKey));
        Assert.IsTrue(OnlineSetup.HasPersonalSetup(o));

        var t = new OnlineSettings { OpenSubtitlesEnabled = false, SubDlEnabled = false };
        OnlineSetup.SaveTitlovi(t, "ivan", "lozinka");
        Assert.IsTrue(t.TitloviEnabled);
        Assert.IsTrue(OnlineSetup.HasPersonalSetup(t));
        StringAssert.Contains(OnlineSetup.Summary(t), "Titlovi.com: prijavljen kao ivan");
    }

    [TestMethod]
    public void PastedKeyIsCleaned()
    {
        Assert.AreEqual("abc123", OnlineSetup.CleanPastedKey("  \"abc123\"\r\n"));
        Assert.AreEqual("abc123", OnlineSetup.CleanPastedKey("'abc123'"));
        Assert.AreEqual(string.Empty, OnlineSetup.CleanPastedKey(null));
    }

    [TestMethod]
    public void WizardIsOfferedOnlyOnce() => Assert.IsFalse(new AppSettings().OnlineWizardOffered);

    private static string RepoRoot()
    {
        string? dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        while (dir != null && !File.Exists(Path.Combine(dir, "jebaited_player.slnx")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        return dir ?? throw new InvalidOperationException("Korijen repozitorija nije pronađen.");
    }
}

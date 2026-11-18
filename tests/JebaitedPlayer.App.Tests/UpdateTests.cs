using System.IO;
using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.Services;

namespace JebaitedPlayer.App.Tests;

[TestClass]
public sealed class UpdateTests
{
    [TestMethod]
    public void RepositoryUrlIsBakedIntoTheApp()
    {
        Assert.IsNotNull(UpdateService.RepositoryUrl);
        StringAssert.StartsWith(UpdateService.RepositoryUrl, "https://github.com/");
    }

    [TestMethod]
    public async Task DevelopmentBuildNeverUpdates()
    {
        // Testovi se ne pokreću iz instalacije, pa nema provjere (ni mreže).
        var updates = new UpdateService();
        Assert.IsFalse(updates.IsInstalled);
        Assert.AreEqual(UpdateState.NotInstalled, updates.State);
        Assert.IsNull(await updates.CheckAndDownloadAsync());
        Assert.IsFalse(updates.ApplyOnExit());
        StringAssert.Contains(updates.Status, "instaliranoj");
    }

    [TestMethod]
    public async Task StoreVersionLeavesUpdatesToTheStore()
    {
        // Inačica iz Microsoft Storea: nikakva provjera na GitHubu ni Velopack; tekst kaže da nadogradnje radi Store.
        var updates = new UpdateService(store: true);
        Assert.IsTrue(updates.IsStore);
        Assert.IsFalse(updates.IsInstalled);
        Assert.AreEqual(UpdateState.NotInstalled, updates.State);
        Assert.IsNull(await updates.CheckAndDownloadAsync());
        Assert.IsFalse(updates.ApplyOnExit());
        StringAssert.Contains(updates.Status, "Microsoft Store");
    }

    [TestMethod]
    public void TestsDoNotRunAsStorePackage()
    {
        Assert.IsFalse(AppPackage.IsStore);
        Assert.IsFalse(new UpdateService().IsStore);
    }

    [TestMethod]
    public void StoreManifestMatchesThePlayer()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "jebaited_player.slnx")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        Assert.IsNotNull(dir, "Korijen repozitorija nije pronađen.");
        var xml = System.Xml.Linq.XDocument.Load(Path.Combine(dir, "packaging", "msix", "AppxManifest.xml"));
        System.Xml.Linq.XNamespace f = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
        System.Xml.Linq.XNamespace uap = "http://schemas.microsoft.com/appx/manifest/uap/windows10";

        // Identitet iz Partner Centera (Product identity).
        System.Xml.Linq.XElement identity = xml.Root!.Element(f + "Identity")!;
        Assert.AreEqual("jebaitedplayer.jebaitedplayer", (string?)identity.Attribute("Name"));
        Assert.AreEqual("CN=873A0756-180F-4761-8305-AE4D54EB9627", (string?)identity.Attribute("Publisher"));
        Assert.AreEqual("jebaited_cro", xml.Root.Element(f + "Properties")!.Element(f + "PublisherDisplayName")!.Value);
        Assert.AreEqual("jebaited_player.exe", (string?)xml.Descendants(f + "Application").Single().Attribute("Executable"));

        // Iste vrste datoteka kao GitHub inačica (MediaScanner).
        string[] manifestTypes = [.. xml.Descendants(uap + "FileType").Select(e => e.Value).Order()];
        string[] playerTypes = [.. JebaitedPlayer.Library.MediaScanner.VideoExtensions.Concat(JebaitedPlayer.Library.MediaScanner.AudioExtensions).Order()];
        CollectionAssert.AreEqual(playerTypes, manifestTypes);
    }

    [TestMethod]
    public void DataLivesOutsideTheInstallFolder()
    {
        // Velopack instalira u %LOCALAPPDATA%\JebaitedPlayer i pri nadogradnji mijenja mapu "current";
        // postavke, povijest i ključevi su u %LOCALAPPDATA%\jebaited_player i nisu dio instalacije.
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string install = Path.Combine(local, "JebaitedPlayer");
        Assert.IsFalse(string.Equals(AppPaths.DataDir, install, StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(AppPaths.DataDir.StartsWith(install + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        StringAssert.EndsWith(AppPaths.SettingsFile, Path.Combine("jebaited_player", "settings.json"));
    }

    [TestMethod]
    public void UpdateSourceOptionIsAbsolute()
    {
        StartupOptions o = StartupOptions.Parse(["--diag-update-source", "releases"]);
        Assert.IsTrue(Path.IsPathFullyQualified(o.DiagUpdateSource!));
    }

    [TestMethod]
    public void AutoUpdateIsOnByDefault() => Assert.IsTrue(new AppSettings().AutoUpdate);
}

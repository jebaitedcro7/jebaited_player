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

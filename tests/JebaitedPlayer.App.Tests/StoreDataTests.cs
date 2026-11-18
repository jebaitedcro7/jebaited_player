using System.IO;
using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.Library;
using Microsoft.Data.Sqlite;

namespace JebaitedPlayer.App.Tests;

/// <summary>Store (MSIX) inačica: vlastita mapa podataka i jednokratno preuzimanje podataka GitHub inačice.</summary>
[TestClass]
public sealed class StoreDataTests
{
    [TestMethod]
    public void StoreVersionKeepsDataInItsPackageFolder()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        Assert.AreEqual(Path.Combine(local, "Packages", "pfn_x", "LocalState", "jebaited_player"), AppPaths.ResolveDataDir(null, "pfn_x"));
        Assert.AreEqual(Path.Combine(local, "jebaited_player"), AppPaths.ResolveDataDir(null, null));
        Assert.AreEqual(@"C:\testdata", AppPaths.ResolveDataDir(@"C:\testdata", "pfn_x"));
        Assert.AreEqual(Path.Combine(local, "jebaited_player"), AppPaths.GitHubDataDir);
    }

    [TestMethod]
    public void StoreVersionImportsGitHubDataOnce()
    {
        string root = Path.Combine(Path.GetTempPath(), "jp-import-" + Guid.NewGuid().ToString("N"));
        string from = Path.Combine(root, "github");
        string to = Path.Combine(root, "store");
        Directory.CreateDirectory(from);
        try
        {
            File.WriteAllText(Path.Combine(from, "settings.json"), """{"Language":"hr"}""");
            using (var github = new WatchStore(Path.Combine(from, "library.db")))
            {
                github.SavePosition(@"D:\Video\film.mkv", 120, 3600, false);
            }

            Assert.AreEqual(2, LegacyDataImport.Run(from, to));
            Assert.AreEqual("""{"Language":"hr"}""", File.ReadAllText(Path.Combine(to, "settings.json")));
            using (var copy = new WatchStore(Path.Combine(to, "library.db")))
            {
                Assert.AreEqual(120, copy.Get(@"D:\Video\film.mkv")!.Position);
            }

            // Drugo pokretanje: Store inačica već ima svoje postavke, ništa se ne prepisuje; izvor ostaje netaknut.
            File.WriteAllText(Path.Combine(to, "settings.json"), """{"Language":"en"}""");
            Assert.AreEqual(0, LegacyDataImport.Run(from, to));
            Assert.AreEqual("""{"Language":"en"}""", File.ReadAllText(Path.Combine(to, "settings.json")));
            Assert.AreEqual("""{"Language":"hr"}""", File.ReadAllText(Path.Combine(from, "settings.json")));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void NothingToImportWithoutGitHubData()
    {
        string root = Path.Combine(Path.GetTempPath(), "jp-import-" + Guid.NewGuid().ToString("N"));
        Assert.AreEqual(0, LegacyDataImport.Run(Path.Combine(root, "github"), Path.Combine(root, "store")));
        Assert.IsFalse(Directory.Exists(root));
    }
}

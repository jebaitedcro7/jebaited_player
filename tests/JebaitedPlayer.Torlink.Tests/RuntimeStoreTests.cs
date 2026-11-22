using System.Security.Cryptography;
using System.Text;

namespace JebaitedPlayer.Torlink.Tests;

[TestClass]
public class RuntimeStoreTests
{
    private static string Sha(string file) => RuntimeStore.Sha256File(file);

    [TestMethod]
    public void Install_ValidPackage_Succeeds_AndIsDetected()
    {
        using var t = new TempDir();
        var store = new RuntimeStore(t.Paths);
        Assert.IsNull(store.Installed());
        string zip = TestRuntime.BuildZip(Path.Combine(t.Path, "dl"), "1.11.1");
        InstallResult r = store.InstallFromZip(zip, Sha(zip));
        Assert.IsTrue(r.Success, r.Failure + " " + r.Detail);
        Assert.AreEqual("1.11.1", store.Installed()!.TorlinkVersion);
        Assert.IsFalse(Directory.Exists(t.Paths.StagingDir));
    }

    [TestMethod]
    public void Install_RejectsWrongZipHash_WithoutTouchingInstalled()
    {
        using var t = new TempDir();
        var store = new RuntimeStore(t.Paths);
        TestRuntime.Install(t.Paths, "1.11.1");
        string zip = TestRuntime.BuildZip(Path.Combine(t.Path, "dl"), "1.12.0");
        InstallResult r = store.InstallFromZip(zip, new string('0', 64));
        Assert.AreEqual(InstallFailure.ZipHashMismatch, r.Failure);
        Assert.AreEqual("1.11.1", store.Installed()!.TorlinkVersion);
    }

    [TestMethod]
    public void Install_RejectsFileThatDoesNotMatchManifest()
    {
        using var t = new TempDir();
        var store = new RuntimeStore(t.Paths);
        string zip = TestRuntime.BuildZip(Path.Combine(t.Path, "dl"), "1.12.0", tamper: f => f["torlink/dist/cli.cjs"] = Encoding.UTF8.GetBytes("evil();"));
        InstallResult r = store.InstallFromZip(zip, Sha(zip));
        Assert.AreEqual(InstallFailure.FileHashMismatch, r.Failure);
        Assert.AreEqual("torlink/dist/cli.cjs", r.Detail);
        Assert.IsNull(store.Installed());
        Assert.IsFalse(Directory.Exists(t.Paths.StagingDir), "privremena mapa se čisti");
    }

    [TestMethod]
    public void Install_RejectsFileNotListedInManifest()
    {
        using var t = new TempDir();
        var store = new RuntimeStore(t.Paths);
        string zip = TestRuntime.BuildZip(Path.Combine(t.Path, "dl"), "1.12.0", extra: [("torlink/dist/injected.js", Encoding.UTF8.GetBytes("x"))]);
        InstallResult r = store.InstallFromZip(zip, Sha(zip));
        Assert.AreEqual(InstallFailure.UnlistedFile, r.Failure);
    }

    [TestMethod]
    public void Install_RejectsManifestWithMissingFile()
    {
        using var t = new TempDir();
        var store = new RuntimeStore(t.Paths);
        string zip = TestRuntime.BuildZip(Path.Combine(t.Path, "dl"), "1.12.0", manifest: m => m.Files["torlink/ghost.js"] = new string('a', 64));
        Assert.AreEqual(InstallFailure.BadManifest, store.InstallFromZip(zip, Sha(zip)).Failure);
    }

    [TestMethod]
    public void Install_RejectsZipSlip()
    {
        using var t = new TempDir();
        var store = new RuntimeStore(t.Paths);
        string zip = TestRuntime.BuildZip(Path.Combine(t.Path, "dl"), "1.12.0", extra: [("../outside.txt", Encoding.UTF8.GetBytes("x"))]);
        InstallResult r = store.InstallFromZip(zip, Sha(zip));
        Assert.AreEqual(InstallFailure.BadArchive, r.Failure);
        Assert.IsFalse(File.Exists(Path.Combine(t.Paths.Root, "outside.txt")));
    }

    [TestMethod]
    public void SafeCombine_BlocksEscapesAndAbsolutePaths()
    {
        Assert.IsNull(RuntimeStore.SafeCombine(@"C:\root", "../x"));
        Assert.IsNull(RuntimeStore.SafeCombine(@"C:\root", "a/../../x"));
        Assert.IsNull(RuntimeStore.SafeCombine(@"C:\root", @"C:\Windows\x"));
        Assert.IsNull(RuntimeStore.SafeCombine(@"C:\root", "/etc/x"));
        Assert.IsNull(RuntimeStore.SafeCombine(@"C:\root", "a:b"));
        Assert.AreEqual(@"C:\root\a\b.txt", RuntimeStore.SafeCombine(@"C:\root", "a/b.txt"));
    }

    [TestMethod]
    public void Upgrade_KeepsPrevious_AndRollbackSwapsBack_AndStateIsUntouched()
    {
        using var t = new TempDir();
        var store = new RuntimeStore(t.Paths);
        TestRuntime.Install(t.Paths, "1.11.1");
        Directory.CreateDirectory(t.Paths.StateDataDir);
        File.WriteAllText(t.Paths.HistoryFile, "[]");

        string zip = TestRuntime.BuildZip(Path.Combine(t.Path, "dl"), "1.12.0");
        Assert.IsTrue(store.InstallFromZip(zip, Sha(zip)).Success);
        Assert.AreEqual("1.12.0", store.Installed()!.TorlinkVersion);
        Assert.IsTrue(store.HasPrevious);
        Assert.AreEqual("1.11.1", store.PreviousManifest()!.TorlinkVersion);

        Assert.IsTrue(store.Rollback());
        Assert.AreEqual("1.11.1", store.Installed()!.TorlinkVersion);
        Assert.AreEqual("1.12.0", store.PreviousManifest()!.TorlinkVersion, "vraćanje se može poništiti");
        Assert.IsTrue(File.Exists(t.Paths.HistoryFile), "stanje (povijest, red) se nikad ne dira");
    }

    [TestMethod]
    public void Rollback_WithoutPrevious_ReturnsFalse()
    {
        using var t = new TempDir();
        TestRuntime.Install(t.Paths);
        Assert.IsFalse(new RuntimeStore(t.Paths).Rollback());
    }

    [TestMethod]
    public void Installed_NullWhenManifestOrExecutableMissing()
    {
        using var t = new TempDir();
        var store = new RuntimeStore(t.Paths);
        TestRuntime.Install(t.Paths);
        Assert.IsNotNull(store.Installed());
        File.Delete(t.Paths.NodeExe);
        Assert.IsNull(store.Installed());
        TestRuntime.Install(t.Paths);
        File.WriteAllText(t.Paths.ManifestFile, "{ not json");
        Assert.IsNull(store.Installed());
    }

    [TestMethod]
    public void RemoveAll_DeletesRuntimeButNotState()
    {
        using var t = new TempDir();
        TestRuntime.Install(t.Paths);
        Directory.CreateDirectory(t.Paths.StateDataDir);
        File.WriteAllText(t.Paths.HistoryFile, "[]");
        new RuntimeStore(t.Paths).RemoveAll();
        Assert.IsFalse(Directory.Exists(t.Paths.RuntimeDir));
        Assert.IsTrue(File.Exists(t.Paths.HistoryFile));
    }

    [TestMethod]
    public void Sha256_MatchesFramework()
    {
        using var t = new TempDir();
        Directory.CreateDirectory(t.Path);
        string f = Path.Combine(t.Path, "a.bin");
        File.WriteAllBytes(f, [1, 2, 3]);
        Assert.AreEqual(Convert.ToHexString(SHA256.HashData(new byte[] { 1, 2, 3 })).ToLowerInvariant(), RuntimeStore.Sha256File(f));
    }
    [TestMethod]
    public void FailedThirdInstall_AbortRestoresCurrentAndKeepsTheWholeChain()
    {
        using var t = new TempDir();
        var store = new RuntimeStore(t.Paths);
        string z1 = TestRuntime.BuildZip(Path.Combine(t.Path, "d1"), "1.10.0");
        string z2 = TestRuntime.BuildZip(Path.Combine(t.Path, "d2"), "1.11.0");
        string z3 = TestRuntime.BuildZip(Path.Combine(t.Path, "d3"), "1.12.0");
        Assert.IsTrue(store.InstallFromZip(z1, Sha(z1)).Success);
        Assert.IsTrue(store.InstallFromZip(z2, Sha(z2)).Success);
        store.CommitInstall();
        Assert.IsTrue(store.InstallFromZip(z3, Sha(z3)).Success);
        Assert.AreEqual("1.12.0", store.Installed()!.TorlinkVersion);
        Assert.IsTrue(Directory.Exists(t.Paths.OldDir), "stara prethodna se čuva do potvrde");

        Assert.IsTrue(store.AbortInstall());
        Assert.AreEqual("1.11.0", store.Installed()!.TorlinkVersion, "vraćena je dosadašnja");
        Assert.AreEqual("1.10.0", store.PreviousManifest()!.TorlinkVersion, "lanac verzija je netaknut: ručno vraćanje i dalje vodi na 1.10.0");
        Assert.IsFalse(Directory.Exists(t.Paths.OldDir));
    }

    [TestMethod]
    public void CommitInstall_DropsTheOldestVersion()
    {
        using var t = new TempDir();
        var store = new RuntimeStore(t.Paths);
        string z1 = TestRuntime.BuildZip(Path.Combine(t.Path, "d1"), "1.10.0");
        string z2 = TestRuntime.BuildZip(Path.Combine(t.Path, "d2"), "1.11.0");
        string z3 = TestRuntime.BuildZip(Path.Combine(t.Path, "d3"), "1.12.0");
        store.InstallFromZip(z1, Sha(z1));
        store.InstallFromZip(z2, Sha(z2));
        store.InstallFromZip(z3, Sha(z3));
        store.CommitInstall();
        Assert.IsFalse(Directory.Exists(t.Paths.OldDir));
        Assert.AreEqual("1.12.0", store.Installed()!.TorlinkVersion);
        Assert.AreEqual("1.11.0", store.PreviousManifest()!.TorlinkVersion);
    }

    [TestMethod]
    public void RecoverIncompleteInstall_RestoresPreviousWhenRuntimeIsMissing()
    {
        using var t = new TempDir();
        var store = new RuntimeStore(t.Paths);
        string z1 = TestRuntime.BuildZip(Path.Combine(t.Path, "d1"), "1.10.0");
        string z2 = TestRuntime.BuildZip(Path.Combine(t.Path, "d2"), "1.11.0");
        store.InstallFromZip(z1, Sha(z1));
        store.CommitInstall();
        store.InstallFromZip(z2, Sha(z2));

        // pad playera usred ažuriranja: nova verzija je pokvarena i nije potvrđena
        File.Delete(t.Paths.NodeExe);
        Assert.IsNull(store.Installed());
        store.RecoverIncompleteInstall();
        Assert.AreEqual("1.10.0", store.Installed()!.TorlinkVersion);
    }

    [TestMethod]
    public void RecoverIncompleteInstall_KeepsValidRuntimeAndClearsLeftovers()
    {
        using var t = new TempDir();
        var store = new RuntimeStore(t.Paths);
        TestRuntime.Install(t.Paths, "1.11.1");
        Directory.CreateDirectory(t.Paths.OldDir);
        Directory.CreateDirectory(t.Paths.StagingDir);
        store.RecoverIncompleteInstall();
        Assert.AreEqual("1.11.1", store.Installed()!.TorlinkVersion);
        Assert.IsFalse(Directory.Exists(t.Paths.OldDir));
        Assert.IsFalse(Directory.Exists(t.Paths.StagingDir));
    }

    [TestMethod]
    public void MoveWithRetry_WaitsForALockedFolderToBeReleased()
    {
        using var t = new TempDir();
        string src = Path.Combine(t.Path, "src");
        string dst = Path.Combine(t.Path, "dst");
        Directory.CreateDirectory(src);
        string file = Path.Combine(src, "a.bin");
        File.WriteAllBytes(file, [1]);
        var held = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None);
        _ = Task.Run(async () =>
        {
            await Task.Delay(700);
            held.Dispose();
        });
        RuntimeStore.MoveWithRetry(src, dst, attempts: 20, delayMs: 100);
        Assert.IsTrue(File.Exists(Path.Combine(dst, "a.bin")));
        Assert.IsFalse(Directory.Exists(src));
    }

    [TestMethod]
    public void MoveWithRetry_GivesUpAfterTheLastAttempt()
    {
        using var t = new TempDir();
        string src = Path.Combine(t.Path, "src");
        Directory.CreateDirectory(src);
        string file = Path.Combine(src, "a.bin");
        File.WriteAllBytes(file, [1]);
        using var held = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None);
        Assert.ThrowsException<IOException>(() => RuntimeStore.MoveWithRetry(src, Path.Combine(t.Path, "dst"), attempts: 3, delayMs: 50));
    }
}

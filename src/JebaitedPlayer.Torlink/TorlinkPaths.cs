namespace JebaitedPlayer.Torlink;

/// <summary>
/// Sve putanje Torlink modula. Korijen je <c>&lt;DataDir&gt;\torlink</c> (poštuje JEBAITED_DATA_DIR jer ga zadaje pozivatelj):
/// <c>runtime</c> (Node + Torlink), <c>runtime.prev</c> (prethodna verzija za vraćanje), <c>runtime.new</c> (privremeno pri
/// instalaciji), <c>state</c> (Torlinkovo stanje: red, povijest, dijeljenje, konfiguracija). Stanje se nikad ne dira pri ažuriranju.
/// </summary>
public sealed class TorlinkPaths
{
    public TorlinkPaths(string dataDir)
    {
        Root = Path.Combine(dataDir, "torlink");
    }

    public string Root { get; }
    public string RuntimeDir => Path.Combine(Root, "runtime");
    public string PreviousDir => Path.Combine(Root, "runtime.prev");
    public string StagingDir => Path.Combine(Root, "runtime.new");

    /// <summary>Verzija prije prethodne: čuva se dok nova instalacija nije potvrđena zdravstvenom provjerom (neuspjelo ažuriranje se vraća bez gubitka lanca verzija).</summary>
    public string OldDir => Path.Combine(Root, "runtime.old");
    public string DownloadCache => Path.Combine(Root, "cache");
    public string StateDir => Path.Combine(Root, "state");
    public string StateDataDir => Path.Combine(StateDir, "data");
    public string StateConfigDir => Path.Combine(StateDir, "config");

    public string NodeExe => Path.Combine(RuntimeDir, "node.exe");
    public string CliScript => Path.Combine(RuntimeDir, "torlink", "dist", "cli.cjs");
    public string ManifestFile => Path.Combine(RuntimeDir, RuntimeManifest.FileName);

    public string QueueFile => Path.Combine(StateDataDir, "queue.json");
    public string HistoryFile => Path.Combine(StateDataDir, "history.json");
    public string SeedsFile => Path.Combine(StateDataDir, "seeds.json");
    public string ConfigFile => Path.Combine(StateConfigDir, "config.json");
    public string PidFile => Path.Combine(StateDir, "serve.pid");

    /// <summary>Torlinkov "boot marker": postavlja se pri podizanju, a briše 4 s kasnije. Ostatak znači da je prethodni proces pao pri podizanju, pa Torlink vraća stavke PAUZIRANE.</summary>
    public string BootMarkerFile => Path.Combine(StateDataDir, "boot.marker");

    /// <summary>Zadana mapa preuzimanja (ista kao u samom Torlinku: Downloads\torlink).</summary>
    public static string DefaultDownloadDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "torlink");
}

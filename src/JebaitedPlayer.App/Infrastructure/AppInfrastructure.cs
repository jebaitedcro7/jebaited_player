using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace JebaitedPlayer.Infrastructure;

public static class AppPaths
{
    /// <summary>
    /// Postavke, povijest, ključevi i predmemorija. Namjerno izvan mape programa (%LOCALAPPDATA%\JebaitedPlayer),
    /// pa ih nadogradnja ni ponovna instalacija ne diraju. <c>JEBAITED_DATA_DIR</c> je samo za testove nadogradnje.
    /// </summary>
    public static string DataDir { get; } =
        Environment.GetEnvironmentVariable("JEBAITED_DATA_DIR") is { Length: > 0 } testDir && Path.IsPathFullyQualified(testDir)
            ? testDir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "jebaited_player");

    public static string LogsDir => Path.Combine(DataDir, "logs");

    public static string SettingsFile => Path.Combine(DataDir, "settings.json");

    /// <summary>Cache prevedenih shadera (gpu-next); bez njega se svaki start prevode iznova (~1 s).</summary>
    public static string ShaderCacheDir => Path.Combine(DataDir, "cache", "shaders");

    /// <summary>Titlovi pretvoreni u UTF-8 za mpv. Originalne datoteke se nikad ne mijenjaju.</summary>
    public static string SubtitleCacheDir => Path.Combine(DataDir, "cache", "subtitles");

    /// <summary>Lokalna baza: povijest gledanja, pozicije, titlovi po videu.</summary>
    public static string LibraryDb => Path.Combine(DataDir, "library.db");

    /// <summary>Rezultati pretrage titlova na internetu (vrijede 3 dana).</summary>
    public static string SearchCacheDir => Path.Combine(DataDir, "cache", "search");

    /// <summary>Preuzeti titlovi po izvoru i ID-u (30 dana): isti titl se ne preuzima ponovno (dnevni limit izvora).</summary>
    public static string DownloadCacheDir => Path.Combine(DataDir, "cache", "downloads");

    /// <summary>Analiza govora po videu (~10 KB na sat filma), da se zvuk ne analizira ponovno.</summary>
    public static string SpeechCacheDir => Path.Combine(DataDir, "cache", "speech");

    /// <summary>Preuzeti titlovi kad se ne mogu spremiti uz video (npr. mapa samo za čitanje).</summary>
    public static string DownloadedSubtitlesDir => Path.Combine(DataDir, "downloaded-subtitles");

    public static string Version { get; } =
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "0.0.0";
}

/// <summary>Jednostavan log u %LOCALAPPDATA%\jebaited_player\logs\app.log. Siguran za više dretvi.</summary>
public static class Log
{
    private static readonly object Gate = new();
    private static string? _path;

    public static string? FilePath => _path;

    public static void Init()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.LogsDir);
            _path = Path.Combine(AppPaths.LogsDir, "app.log");
            if (File.Exists(_path) && new FileInfo(_path).Length > 2_000_000)
            {
                File.Delete(_path);
            }
        }
        catch
        {
            _path = null;
        }

        Info($"--- jebaited_player {AppPaths.Version} pokrenut ---");
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}: {ex}");

    public static void Mpv(string prefix, string level, string text) =>
        Write("MPV-" + level.ToUpperInvariant(), $"[{prefix}] {text}");

    private static void Write(string level, string message)
    {
        string? path = _path;
        if (path is null)
        {
            return;
        }

        string line = $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)} {level} {message}{Environment.NewLine}";
        lock (Gate)
        {
            try
            {
                File.AppendAllText(path, line);
            }
            catch
            {
                // Log nikad ne smije srušiti aplikaciju.
            }
        }
    }
}

/// <summary>Postavke koje se pamte između pokretanja (od M3 prelaze u SQLite).</summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public double Volume { get; set; } = 100;
    public bool Muted { get; set; }
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }

    /// <summary>Prioritet jezika titlova (prvi = najvažniji).</summary>
    public List<string> SubtitleLanguages { get; set; } = ["hr", "sr", "bs", "en"];

    /// <summary>Automatski učitaj lokalne titlove istog naziva kao video.</summary>
    public bool AutoLoadSubtitles { get; set; } = true;

    /// <summary>Provjeri vanjske titlove prema govoru u zvuku i sam ispravi pomak i brzinu kad je to pouzdano.</summary>
    public bool AutoSyncSubtitles { get; set; } = true;

    /// <summary>Sučelje preko GPU-a (WPF). Zadano isključeno: video je uvijek na GPU-u, a sučelje je pouzdanije bez njega.</summary>
    public bool UiGpuRendering { get; set; }

    /// <summary>"dark" (zadano) ili "light".</summary>
    public string Theme { get; set; } = "dark";

    /// <summary>Na kraju epizode sama krene sljedeća (nakon odbrojavanja). Isključeno: samo ponudi.</summary>
    public bool AutoPlayNext { get; set; } = true;

    /// <summary>Odbrojavanje do sljedeće epizode (s).</summary>
    public int UpNextSeconds { get; set; } = 10;

    /// <summary>Automatski provjeri i preuzmi novu verziju (instalira se pri zatvaranju playera).</summary>
    public bool AutoUpdate { get; set; } = true;

    /// <summary>Vodič za titlove s interneta je već ponuđen pri prvom pokretanju (kasnije samo iz izbornika).</summary>
    public bool OnlineWizardOffered { get; set; }

    /// <summary>Mapa za snimke zaslona; prazno = Slike\jebaited_player.</summary>
    public string? ScreenshotFolder { get; set; }

    public SubtitleStyleSettings SubtitleStyle { get; set; } = new();

    public OnlineSettings Online { get; set; } = new();

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsFile)) ?? new AppSettings();
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Postavke nisu učitane, koriste se zadane: {ex.Message}");
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDir);
            string tmp = AppPaths.SettingsFile + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOptions));
            File.Move(tmp, AppPaths.SettingsFile, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Warn($"Postavke nisu spremljene: {ex.Message}");
        }
    }
}

/// <summary>Izvori titlova na internetu. Ključevi i lozinke spremaju se kriptirani (DPAPI), nikad kao čisti tekst.</summary>
public sealed class OnlineSettings
{
    public bool AutoSearch { get; set; } = true;
    public bool SaveNextToVideo { get; set; } = true;

    public bool OpenSubtitlesEnabled { get; set; } = true;
    public string? OpenSubtitlesApiKey { get; set; }
    public string? OpenSubtitlesUsername { get; set; }
    public string? OpenSubtitlesPassword { get; set; }

    public bool SubDlEnabled { get; set; } = true;
    public string? SubDlApiKey { get; set; }

    public bool TitloviEnabled { get; set; }
    public string? TitloviUsername { get; set; }
    public string? TitloviPassword { get; set; }
}

/// <summary>Izgled titlova (primjenjuje se na SRT/VTT/MicroDVD; ASS titlovi zadržavaju svoje stilove).</summary>
public sealed class SubtitleStyleSettings
{
    public static IReadOnlyList<string> Fonts { get; } =
        ["Segoe UI Semibold", "Segoe UI", "Arial", "Calibri", "Verdana", "Tahoma", "Trebuchet MS"];

    public static IReadOnlyList<(string Name, string Color)> Colors { get; } =
    [
        ("Bijela", "#FFFFFF"),
        ("Žuta", "#FFE14D"),
        ("Svijetlosiva", "#D9D9D9"),
        ("Svijetloplava", "#A8DCFF"),
    ];

    public string FontName { get; set; } = "Segoe UI Semibold";
    public double FontSize { get; set; } = 44;
    public string Color { get; set; } = "#FFFFFF";
    public double OutlineSize { get; set; } = 2.5;
    public bool Bold { get; set; }
    public bool BackgroundBox { get; set; }

    /// <summary>Okomiti položaj: 100 = dno, manje = više.</summary>
    public double Position { get; set; } = 100;
}

/// <summary>
/// Argumenti naredbenog retka: datoteke za otvaranje i dijagnostički način rada
/// (<c>--diag &lt;sekunde&gt; [--diag-out putanja.json] [--diag-fullscreen] [--diag-controls]</c>).
/// </summary>
public sealed class StartupOptions
{
    public List<string> Files { get; } = new();
    public int? DiagSeconds { get; private set; }
    public string? DiagOut { get; private set; }
    public bool DiagFullscreen { get; private set; }
    public bool DiagShowControls { get; private set; }

    /// <summary>Dijagnostika titlova: na kojoj sekundi zaustaviti video i pročitati prikazani titl.</summary>
    public double? DiagProbeSeconds { get; private set; }
    public double? DiagSubDelayMs { get; private set; }
    public double? DiagSubSpeed { get; private set; }

    /// <summary>Dvije točke za test sinkronizacije: "orig1:cilj1;orig2:cilj2" (ms).</summary>
    public string? DiagSyncPoints { get; private set; }

    /// <summary>Ručno zadani encoding (kodna stranica) za titlove iz naredbenog retka.</summary>
    public int? DiagEncoding { get; private set; }

    /// <summary>Za snimke zaslona: otvori "sync", "settings" ili "menu" nakon početka reprodukcije.</summary>
    public string? DiagShow { get; private set; }

    /// <summary>Test spremanja: nakon provjere titlova spremi sinkroniziranu kopiju na ovu putanju.</summary>
    public string? DiagSaveSubtitles { get; private set; }

    /// <summary>Baza za dijagnostiku (da testovi ne diraju tvoju stvarnu povijest gledanja).</summary>
    public string? DiagDb { get; private set; }

    /// <summary>Test nastavka gledanja: "continue" ili "start".</summary>
    public string? DiagResume { get; private set; }

    /// <summary>Test sljedeće epizode: odbrojavanje u sekundama (i bez ponavljanja datoteke).</summary>
    public int? DiagUpNextSeconds { get; private set; }

    /// <summary>Test pamćenja titlova: odaberi titlove ovog jezika ("none" = bez titlova) kao da je korisnik kliknuo.</summary>
    public string? DiagSelectSubtitle { get; private set; }

    /// <summary>Test pretrage bez mreže i ključeva: "sure", "unsure" ili "none" (lažni izvor s gotovim rezultatima).</summary>
    public string? DiagFakeOnline { get; private set; }

    /// <summary>
    /// Dijagnostika inače ne traži titlove na internetu (da testovi ne troše dnevni limit korisnikovih ključeva);
    /// ovom zastavicom koriste se pravi izvori iz postavki.
    /// </summary>
    public bool DiagOnlineLive { get; private set; }

    /// <summary>Dijagnostika čeka da automatska sinkronizacija prema govoru završi (N puta) prije provjere titlova.</summary>
    public bool DiagWaitSync => DiagWaitSyncRuns > 0;

    public int DiagWaitSyncRuns { get; private set; }

    /// <summary>Koliko nakon prvog kadra počinje mjerenje (zadano 5 s); kraće = mjeri se i ono što se radi odmah po otvaranju.</summary>
    public int? DiagWarmupMs { get; private set; }

    /// <summary>Tema samo za ovo pokretanje ("dark" ili "light"), bez spremanja u postavke.</summary>
    public string? DiagTheme { get; private set; }

    /// <summary>
    /// Tipke koje dijagnostika "pritisne" redom (isti put kao prava tipkovnica), npr. "B;OemPlus;O;S;Ctrl+D".
    /// </summary>
    public string? DiagKeys { get; private set; }

    /// <summary>Automatska sljedeća epizoda samo za ovo pokretanje ("on"/"off").</summary>
    public bool? DiagAutoNext { get; private set; }

    /// <summary>Automatska sinkronizacija prema govoru samo za ovo pokretanje ("on"/"off"), bez spremanja u postavke.</summary>
    public bool? DiagAutoSync { get; private set; }

    /// <summary>Test poravnanja po replici: "izvorno:cilj;…" (s) – za svaku točku premota na cilj i odabere repliku s tim izvornim vremenom.</summary>
    public string? DiagAnchors { get; private set; }

    /// <summary>Sučelje (ne video) iscrtava se bez GPU-a (to je i zadano, osim uz --gpu-ui ili postavku UiGpuRendering).</summary>
    public bool SoftwareRender { get; private set; }

    /// <summary>Sučelje se iscrtava preko GPU-a (WPF). Video ide preko GPU-a uvijek.</summary>
    public bool GpuUi { get; private set; }

    /// <summary>Zaseban player čak i kad je jedan već otvoren (<c>--new-window</c>).</summary>
    public bool NewWindow { get; private set; }

    /// <summary>Naredba bez sučelja: "register" (<c>--register</c>) ili "unregister" (<c>--unregister</c>) u Windowsu.</summary>
    public string? RegisterCommand { get; private set; }

    /// <summary>Test registracije: ključ ispod HKCU umjesto <c>Software</c> (stvarna registracija se ne dira).</summary>
    public string? DiagRegistryRoot { get; private set; }

    /// <summary>Test jedne instance: vlastito ime (radi i u dijagnostici, ne dira tvoj otvoreni player).</summary>
    public string? DiagInstanceName { get; private set; }

    /// <summary>Test nadogradnje: mapa s izdanjima umjesto GitHuba; dijagnostika odmah provjeri i preuzme.</summary>
    public string? DiagUpdateSource { get; private set; }

    public bool IsDiagnostics => DiagSeconds.HasValue;

    public static StartupOptions Parse(string[] args)
    {
        var o = new StartupOptions();
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            switch (a)
            {
                case "--diag" when i + 1 < args.Length:
                    o.DiagSeconds = int.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                case "--diag-out" when i + 1 < args.Length:
                    o.DiagOut = Path.GetFullPath(args[++i]);
                    break;
                case "--diag-fullscreen":
                    o.DiagFullscreen = true;
                    break;
                case "--diag-controls":
                    o.DiagShowControls = true;
                    break;
                case "--diag-probe" when i + 1 < args.Length:
                    o.DiagProbeSeconds = double.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                case "--diag-sub-delay" when i + 1 < args.Length:
                    o.DiagSubDelayMs = double.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                case "--diag-sub-speed" when i + 1 < args.Length:
                    o.DiagSubSpeed = double.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                case "--diag-sync-points" when i + 1 < args.Length:
                    o.DiagSyncPoints = args[++i];
                    break;
                case "--diag-encoding" when i + 1 < args.Length:
                    o.DiagEncoding = int.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                case "--diag-show" when i + 1 < args.Length:
                    o.DiagShow = args[++i];
                    break;
                case "--diag-save" when i + 1 < args.Length:
                    o.DiagSaveSubtitles = Path.GetFullPath(args[++i]);
                    break;
                case "--diag-db" when i + 1 < args.Length:
                    o.DiagDb = Path.GetFullPath(args[++i]);
                    break;
                case "--diag-resume" when i + 1 < args.Length:
                    o.DiagResume = args[++i];
                    break;
                case "--diag-upnext" when i + 1 < args.Length:
                    o.DiagUpNextSeconds = int.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                case "--diag-select-sub" when i + 1 < args.Length:
                    o.DiagSelectSubtitle = args[++i];
                    break;
                case "--diag-fake-online" when i + 1 < args.Length:
                    o.DiagFakeOnline = args[++i];
                    break;
                case "--diag-online-live":
                    o.DiagOnlineLive = true;
                    break;
                case "--diag-wait-sync" when i + 1 < args.Length:
                    o.DiagWaitSyncRuns = int.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                case "--diag-warmup" when i + 1 < args.Length:
                    o.DiagWarmupMs = int.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                case "--diag-theme" when i + 1 < args.Length:
                    o.DiagTheme = args[++i] == "light" ? "light" : "dark";
                    break;
                case "--diag-keys" when i + 1 < args.Length:
                    o.DiagKeys = args[++i];
                    break;
                case "--diag-autonext" when i + 1 < args.Length:
                    o.DiagAutoNext = args[++i] != "off";
                    break;
                case "--diag-autosync" when i + 1 < args.Length:
                    o.DiagAutoSync = args[++i] != "off";
                    break;
                case "--diag-anchors" when i + 1 < args.Length:
                    o.DiagAnchors = args[++i];
                    break;
                case "--software-render":
                    o.SoftwareRender = true;
                    break;
                case "--gpu-ui":
                    o.GpuUi = true;
                    break;
                case "--new-window":
                    o.NewWindow = true;
                    break;
                case "--register":
                    o.RegisterCommand = "register";
                    break;
                case "--unregister":
                    o.RegisterCommand = "unregister";
                    break;
                case "--diag-registry-root" when i + 1 < args.Length:
                    o.DiagRegistryRoot = args[++i];
                    break;
                case "--diag-instance-name" when i + 1 < args.Length:
                    o.DiagInstanceName = args[++i];
                    break;
                case "--diag-update-source" when i + 1 < args.Length:
                    o.DiagUpdateSource = Path.GetFullPath(args[++i]);
                    break;
                default:
                    if (!a.StartsWith("--", StringComparison.Ordinal))
                    {
                        o.Files.Add(Path.GetFullPath(a.TrimEnd('"')));
                    }

                    break;
            }
        }

        return o;
    }
}

public static class TimeFormat
{
    public static string Format(double seconds, bool forceHours)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0)
        {
            seconds = 0;
        }

        var t = TimeSpan.FromSeconds(Math.Floor(seconds));
        return forceHours || t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
            : $"{t.Minutes:00}:{t.Seconds:00}";
    }
}

public static class MediaFormats
{
    public static readonly string[] Video =
        ["mp4", "mkv", "avi", "mov", "wmv", "flv", "webm", "mpeg", "mpg", "m4v", "ts", "mts", "m2ts", "vob", "3gp", "ogv"];

    public static readonly string[] Audio =
        ["mp3", "aac", "flac", "wav", "ogg", "opus", "ac3", "dts", "eac3", "m4a", "wma", "mka"];

    public static string DialogFilter =>
        $"Video i audio|{Pattern(Video.Concat(Audio))}|Video|{Pattern(Video)}|Audio|{Pattern(Audio)}|Sve datoteke|*.*";

    public static string SubtitleDialogFilter =>
        $"Titlovi|{string.Join(";", JebaitedPlayer.Subtitles.SubtitleFormats.Extensions.Select(e => "*" + e))}|Sve datoteke|*.*";

    public static bool IsSubtitle(string path) => JebaitedPlayer.Subtitles.SubtitleFormats.IsSubtitleFile(path);

    private static string Pattern(IEnumerable<string> extensions) =>
        string.Join(";", extensions.Select(e => "*." + e));
}

public sealed record ShortcutInfo(string Keys, string Description);

public static class KeyboardShortcuts
{
    public static IReadOnlyList<ShortcutInfo> All { get; } =
    [
        new("Razmaknica / K", "Reproduciraj / pauziraj"),
        new("← / →", "5 s natrag / naprijed"),
        new("Ctrl + ← / →", "30 s natrag / naprijed"),
        new("↑ / ↓ / kotačić", "Glasnoća"),
        new("M", "Isključi / uključi zvuk"),
        new("F / dvoklik", "Cijeli zaslon (Esc za izlaz)"),
        new(", / .", "Kadar natrag / naprijed"),
        new("N / P / L", "Sljedeća / prethodna epizoda / popis epizoda"),
        new("Ctrl + O / Ctrl + Shift + O", "Otvori video / otvori mapu"),
        new("Ctrl + T / Ctrl + F", "Učitaj titlove iz datoteke / traži na internetu"),
        new("V / J", "Prikaži ili sakrij titlove / sljedeći titlovi"),
        new("G / H", "Titlovi ranije / kasnije (100 ms; Shift 1 s, Ctrl 10 ms)"),
        new("Z, zatim X", "Sinkronizacija: čujem repliku, zatim njezin titl"),
        new("A", "Automatska sinkronizacija titlova prema govoru"),
        new("B", "Sljedeća audio traka"),
        new("+ / − / Backspace", "Brža / sporija / normalna reprodukcija (0,5–2×)"),
        new("O", "Omjer slike"),
        new("S / Shift + S", "Snimka zaslona (s titlovima / bez titlova)"),
        new("Ctrl + D", "Dodaj u omiljene / ukloni"),
        new("Ctrl + Backspace", "Vrati originalnu sinkronizaciju titlova"),
        new("F1", "Prikaži prečace"),
    ];

    public static string AsOsdText() => string.Join("\n", All.Select(s => $"{s.Keys}   —   {s.Description}"));
}

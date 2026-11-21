using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using JebaitedPlayer.Diagnostics;

namespace JebaitedPlayer.Infrastructure;

public static class AppPaths
{
    /// <summary>
    /// Postavke, povijest, ključevi i predmemorija. Namjerno izvan mape programa (%LOCALAPPDATA%\JebaitedPlayer),
    /// pa ih nadogradnja ni ponovna instalacija ne diraju. <c>JEBAITED_DATA_DIR</c> je samo za testove nadogradnje.
    /// </summary>
    public static string DataDir { get; } = ResolveDataDir(Environment.GetEnvironmentVariable("JEBAITED_DATA_DIR"), AppPackage.FamilyName);

    /// <summary>Mapa podataka GitHub inačice; Store inačica iz nje jednom preuzme postavke i povijest (<see cref="LegacyDataImport"/>).</summary>
    public static string GitHubDataDir => Path.Combine(LocalAppData, "jebaited_player");

    private static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    /// <summary>
    /// Testna mapa (JEBAITED_DATA_DIR), inače: Store inačica u vlastitoj mapi paketa (LocalState, Windows je ne virtualizira
    /// pa nema podataka podijeljenih između stvarne i virtualne mape), GitHub inačica u %LOCALAPPDATA%\jebaited_player.
    /// </summary>
    internal static string ResolveDataDir(string? testDir, string? packageFamilyName) =>
        testDir is { Length: > 0 } && Path.IsPathFullyQualified(testDir)
            ? testDir
            : packageFamilyName is { Length: > 0 }
                ? Path.Combine(LocalAppData, "Packages", packageFamilyName, "LocalState", "jebaited_player")
                : Path.Combine(LocalAppData, "jebaited_player");

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

/// <summary>
/// Sučelje za zapisivanje. Dok traje sesija playera (<see cref="StartSession"/>) sve ide u lokalni logger
/// (JebaitedPlayer.Diagnostics, logs\GGGG\MM\GGGG-MM-DD\*.log). Izvan sesije (install/uninstall hookovi, uvoz podataka)
/// piše se u kratki logspp.log. Siguran za više dretvi; nikad ne baca iznimku.
/// </summary>
public static class Log
{
    private static readonly object Gate = new();
    private static string? _path;

    /// <summary>Datoteka koju korisnik može otvoriti kad se dogodi greška (današnji errors.log ili app.log).</summary>
    public static string? FilePath => DiagLog.Current is { } d
        ? Path.Combine(d.DayDir(DateTime.Now), "errors.log")
        : _path;

    public static void Init()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.LogsDir);
            _path = Path.Combine(AppPaths.LogsDir, "app.log");
            if (File.Exists(_path) && new FileInfo(_path).Length > 500_000)
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

    /// <summary>Pokreće logger sesije (samo glavni proces playera; kraj sesije je <see cref="EndSession"/>).</summary>
    public static void StartSession()
    {
        try
        {
            DiagLog.Start(AppPaths.LogsDir, AppPaths.Version);
        }
        catch (Exception)
        {
            // Logger nikad ne smije spriječiti start playera.
        }
    }

    public static void EndSession() => DiagLog.Stop();

    public static void EndSessionCrashed() => DiagLog.Abort();

    public static void Info(string message) => Write(LogLevel.Info, "INFO", message, null);

    public static void Warn(string message) => Write(LogLevel.Warning, "WARN", message, null);

    public static void Error(string message, Exception? ex = null) => Write(LogLevel.Error, "ERROR", message, ex);

    public static LogChannel Playback => new(LogCat.Playback);

    public static LogChannel Subtitles => new(LogCat.Subtitle);

    public static LogChannel Ui => new(LogCat.Ui);

    public static LogChannel Stability => new(LogCat.Stability);

    public static LogChannel Updates => new(LogCat.Update);

    public static LogChannel Usage => new(LogCat.Usage);

    /// <summary>mpv poruke (razina warn i više): kategorija i vrsta prema modulu i tekstu, da se greške zvuka, slike i titlova razlikuju.</summary>
    public static void Mpv(string prefix, string level, string text)
    {
        LogLevel lvl = level.ToLowerInvariant() switch
        {
            "fatal" => LogLevel.Critical,
            "error" => LogLevel.Error,
            "warn" => LogLevel.Warning,
            "info" => LogLevel.Info,
            _ => LogLevel.Debug,
        };
        if (DiagLog.Current is { } d)
        {
            (string cat, string type) = ClassifyMpv(prefix, text);
            d.Log(lvl, cat, type, "mpv:" + prefix, text);
        }
        else
        {
            WriteFile("MPV-" + level.ToUpperInvariant(), $"[{prefix}] {text}");
        }
    }

    internal static (string Category, string Type) ClassifyMpv(string prefix, string text)
    {
        string p = prefix.ToLowerInvariant();
        string t = text.ToLowerInvariant();
        string cat = p is "ad" or "ao" or "af" || p.StartsWith("ffmpeg/audio", StringComparison.Ordinal) ? LogCat.Audio
            : p is "vd" or "vo" or "vf" or "gpu" or "d3d11" or "libplacebo" || p.StartsWith("ffmpeg/video", StringComparison.Ordinal) ? LogCat.Video
            : p is "sub" or "ass" or "osd/ass" || t.Contains("subtitle", StringComparison.Ordinal) ? LogCat.Subtitle
            : LogCat.Playback;
        string type = t.Contains("desync", StringComparison.Ordinal) || t.Contains("av sync", StringComparison.Ordinal) ? "avsync"
            : t.Contains("error while decoding", StringComparison.Ordinal) || t.Contains("decode", StringComparison.Ordinal) && t.Contains("fail", StringComparison.Ordinal) ? "decode"
            : t.Contains("failed to open", StringComparison.Ordinal) || t.Contains("cannot open", StringComparison.Ordinal) || t.Contains("no such file", StringComparison.Ordinal) ? "open"
            : t.Contains("unsupported", StringComparison.Ordinal) || t.Contains("codec", StringComparison.Ordinal) || t.Contains("format", StringComparison.Ordinal) ? "format"
            : t.Contains("dropping", StringComparison.Ordinal) || t.Contains("too slow", StringComparison.Ordinal) || t.Contains("underrun", StringComparison.Ordinal) ? "performance"
            : "message";
        return (cat, $"mpv.{cat}.{type}");
    }

    private static void Write(LogLevel level, string label, string message, Exception? ex)
    {
        if (DiagLog.Current is { } d)
        {
            d.Log(level, LogCat.App, "app." + label.ToLowerInvariant(), "Log", message, ex);
            return;
        }

        WriteFile(label, ex is null ? message : $"{message}: {ex}");
    }

    private static void WriteFile(string level, string message)
    {
        string? path = _path;
        if (path is null)
        {
            return;
        }

        string line = $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)} {level} {Redactor.Clean(message)}{Environment.NewLine}";
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

/// <summary>Zapisi jedne kategorije (npr. <c>Log.Subtitles.Warn(...)</c>); isto sučelje kao <see cref="Log"/>.</summary>
public readonly struct LogChannel(string category)
{
    public void Info(string message) => Write(LogLevel.Info, message, null);

    public void Warn(string message) => Write(LogLevel.Warning, message, null);

    public void Error(string message, Exception? ex = null) => Write(LogLevel.Error, message, ex);

    private void Write(LogLevel level, string message, Exception? ex)
    {
        if (DiagLog.Current is { } d)
        {
            d.Log(level, category, $"{category}.{level.ToString().ToLowerInvariant()}", "Log", message, ex);
        }
        else
        {
            Log.Info(message);
        }
    }
}

/// <summary>M3a: što se dogodilo s oštećenom ili nečitljivom datotekom postavki pri pokretanju.</summary>
public enum SettingsLoadOutcome
{
    /// <summary>Datoteka je bila oštećena; postavke su vraćene iz automatske kopije (M1a).</summary>
    RestoredFromBackup,

    /// <summary>Datoteka je bila oštećena, a ispravne kopije nema; vrijede zadane postavke.</summary>
    ResetToDefaults,

    /// <summary>Datoteka se sada ne može pročitati (zaključana, prava); vrijede zadane i datoteka se ne prepisuje.</summary>
    Unreadable,
}

/// <summary>M3a: izvještaj za poruku korisniku (oštećena datoteka ostaje sačuvana pod <see cref="CorruptFile"/>).</summary>
public sealed record SettingsLoadReport(SettingsLoadOutcome Outcome, string? CorruptFile, DateTime? BackupCreatedUtc, string Error)
{
    /// <summary>Poruka korisniku na jeziku sučelja.</summary>
    public string Describe() => Outcome switch
    {
        SettingsLoadOutcome.RestoredFromBackup => Loc.T("settings.damagedRestored",
            BackupCreatedUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "?", Path.GetFileName(CorruptFile ?? string.Empty)),
        SettingsLoadOutcome.ResetToDefaults => Loc.T("settings.damagedReset", Path.GetFileName(CorruptFile ?? string.Empty)),
        _ => Loc.T("settings.unreadable", Error),
    };
}

/// <summary>Postavke koje se pamte između pokretanja (od M3 prelaze u SQLite).</summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>Oblik datoteke settings.json (M3a). 1 = datoteke bez oznake (do 1.5.x), 2 = od 1.6.3.</summary>
    public const int CurrentSettingsVersion = 2;

    /// <summary>Datoteka iz koje su postavke učitane (i u koju se spremaju).</summary>
    private string? _file;

    /// <summary>Postojeća datoteka nije pročitana (zaključana, prava): ova sesija je ne smije prepisati zadanim vrijednostima.</summary>
    private bool _doNotOverwrite;

    /// <summary>
    /// M3a: oblik datoteke. Stare datoteke ga nemaju (= 1) i pri učitavanju prolaze kroz migraciju; spremanje
    /// zapisuje trenutni oblik. Datoteka iz novije verzije zadržava svoj (veći) broj.
    /// </summary>
    public int SettingsVersion { get; set; } = CurrentSettingsVersion;

    /// <summary>M3a: nepoznata polja (npr. iz novije verzije playera) ostaju u datoteci i nakon spremanja.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? UnknownFields { get; set; }

    /// <summary>M3a: oštećena ili nečitljiva datoteka pri pokretanju (za poruku korisniku); null = sve u redu.</summary>
    [JsonIgnore]
    public SettingsLoadReport? LoadReport { get; private set; }

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

    /// <summary>
    /// Jezik sučelja ("en", "de", "hr"…). Prazno dok ga korisnik ne odabere (prvo pokretanje ili nadogradnja s 1.0.x):
    /// tada se ponudi odabir, a bez odabira vrijedi engleski.
    /// </summary>
    public string? Language { get; set; }

    /// <summary>Postavke nisu postojale (nova instalacija): zadani jezici titlova prate odabrani jezik sučelja.</summary>
    [JsonIgnore]
    public bool IsNew { get; private set; }

    /// <summary>Zadani prioritet jezika titlova za novi jezik sučelja (samo za novu instalaciju).</summary>
    public static List<string> DefaultSubtitleLanguages(string uiLanguage) => uiLanguage switch
    {
        "hr" => ["hr", "sr", "bs", "en"],
        "bs" => ["bs", "hr", "sr", "en"],
        "sr" => ["sr", "hr", "bs", "en"],
        "sl" => ["sl", "hr", "en"],
        "de" => ["de", "en"],
        _ => ["en"],
    };

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

    // --- UPGRADE 3: nove postavke (stare settings.json datoteke ih nemaju, pa vrijede zadane) ---------------------

    /// <summary>Ponovno otvaranje započetog videa: "ask" (kartica, zadano), "resume" (nastavi sam) ili "start" (od početka).</summary>
    public string ResumeMode { get; set; } = "ask";

    /// <summary>Strelica lijevo/desno premota ovoliko sekundi (Ctrl + strelica šest puta više).</summary>
    public int SeekStepSeconds { get; set; } = 5;

    /// <summary>Video otvoren iz playera ili Windowsa ide odmah u cijeli zaslon.</summary>
    public bool OpenFullscreen { get; set; }

    /// <summary>Dok se video reproducira, player je iznad ostalih prozora.</summary>
    public bool OnTopWhilePlaying { get; set; }

    /// <summary>Željeni jezik zvuka (kod, npr. "en"); prazno = kako je zadano u datoteci.</summary>
    public string? AudioLanguage { get; set; }

    /// <summary>Izlazni uređaj zvuka (mpv "audio-device"); "auto" = zadani uređaj sustava.</summary>
    public string AudioDevice { get; set; } = "auto";

    /// <summary>
    /// Ciklus 4 (V4, priprema za Discord): zaseban izlaz za glazbu, radio i mini player, npr. virtualni kabel prema
    /// Discordu, dok filmovi ostaju na zvučnicima. Prazno = isti uređaj kao <see cref="AudioDevice"/>.
    /// </summary>
    public string MusicAudioDevice { get; set; } = string.Empty;

    /// <summary>Uređaj za trenutnu vrstu sadržaja (glazba i radio ili filmovi).</summary>
    public string OutputDevice(bool music) => music && MusicAudioDevice.Length > 0 ? MusicAudioDevice : AudioDevice;

    /// <summary>Pomak zvuka u ms (pozitivno = zvuk kasnije), npr. za Bluetooth slušalice.</summary>
    public double AudioDelayMs { get; set; }

    /// <summary>Ujednačavanje glasnoće (tiši glasni prizori, razumljiviji dijalog).</summary>
    public bool NormalizeAudio { get; set; }

    /// <summary>Equalizer za filmove i serije (ciklus 1): 10 pojaseva, preseti i vlastiti preset.</summary>
    public EqualizerSettings Equalizer { get; set; } = new();

    private EqualizerSettings? _musicEqualizer;

    /// <summary>
    /// Equalizer za glazbu i radio (1.4.1), odvojen od onog za filmove. Postavke iz 1.3/1.4 ga nemaju:
    /// tada kreće kao kopija dosadašnjeg equalizera, pa se zvuk nakon nadogradnje ne mijenja.
    /// </summary>
    public EqualizerSettings MusicEqualizer
    {
        get => _musicEqualizer ??= Equalizer.Clone();
        set => _musicEqualizer = value;
    }

    /// <summary>Mini Music Player (ciklus 2).</summary>
    public MiniPlayerSettings MiniPlayer { get; set; } = new();

    /// <summary>2.0: boja isticanja: "" = zadana zelena, inače #RRGGBB (gotova ili vlastita boja).</summary>
    public string AccentColor { get; set; } = string.Empty;

    /// <summary>2.0: promijenjeni prečaci (ID radnje → do dvije tipke, npr. "Ctrl+O"); zadani se ne spremaju.</summary>
    public Dictionary<string, string[]> Shortcuts { get; set; } = new();

    /// <summary>Hardversko dekodiranje (grafička kartica).</summary>
    public bool HardwareDecoding { get; set; } = true;

    /// <summary>Kvaliteta slike: "balanced" (zadano), "high" ili "fast".</summary>
    public string VideoQuality { get; set; } = "balanced";

    /// <summary>Omjer slike za nove videe ("auto" = izvorni).</summary>
    public string DefaultAspect { get; set; } = "auto";

    /// <summary>HDR prolaz na HDR zaslon (kad je HDR uključen u Windowsima).</summary>
    public bool HdrPassthrough { get; set; }

    /// <summary>
    /// V8 (ciklus 5): Client ID korisnikove Spotify aplikacije (developer.spotify.com). Nije tajna (PKCE);
    /// token prijave je kriptiran u zasebnoj datoteci spotify-auth.dat, nikad u settings.json.
    /// </summary>
    public string? SpotifyClientId { get; set; }

    /// <summary>N1 (ciklus 6): pristanak za slanje redigiranih izvještaja o greškama.</summary>
    public JebaitedPlayer.Services.ErrorReportSettings ErrorReports { get; set; } = new();

    /// <summary>N3 (ciklus 6): datum, vrijeme i prognoza na početnom ekranu.</summary>
    public JebaitedPlayer.Services.HomeInfoSettings HomeInfo { get; set; } = new();

    /// <summary>N4 (ciklus 5): slika pozadine početnog ekrana (samo putanja, slika se ne kopira); prazno = svemir.</summary>
    public string? HomeBackgroundImage { get; set; }

    /// <summary>N4: "fill" (ispuni, zadano), "fit" (prilagodi) ili "center" (sredina).</summary>
    public string HomeBackgroundMode { get; set; } = "fill";

    public static readonly IReadOnlyList<string> HomeBackgroundModes = ["fill", "fit", "center"];

    /// <summary>Koliko sekundi bez pomicanja miša kontrole (i Home) ostaju vidljive.</summary>
    public double ControlsHideSeconds { get; set; } = 2.5;

    /// <summary>Kodiranje za stare titlove koji nisu UTF-8 (npr. 1250); prazno = prepoznaj automatski.</summary>
    public int? SubtitleCodePage { get; set; }

    /// <summary>Ponuđene vrijednosti (postavke; neispravna vrijednost iz datoteke vraća se na zadanu).</summary>
    public static readonly IReadOnlyList<int> SeekSteps = [5, 10, 15, 30];

    public static readonly IReadOnlyList<double> HideDelays = [1.5, 2.5, 4, 6, 10];

    public static readonly IReadOnlyList<string> ResumeModes = ["ask", "resume", "start"];

    public static readonly IReadOnlyList<string> VideoQualities = ["balanced", "high", "fast"];

    /// <summary>Vrijednosti izvan ponuđenih (ručno uređena datoteka) vraćaju se na zadane.</summary>
    public void Normalize()
    {
        if (!ResumeModes.Contains(ResumeMode))
        {
            ResumeMode = "ask";
        }

        if (!SeekSteps.Contains(SeekStepSeconds))
        {
            SeekStepSeconds = 5;
        }

        if (!VideoQualities.Contains(VideoQuality))
        {
            VideoQuality = "balanced";
        }

        if (!HideDelays.Any(d => Math.Abs(d - ControlsHideSeconds) < 0.01))
        {
            ControlsHideSeconds = 2.5;
        }

        AudioDelayMs = Math.Clamp(Math.Round(AudioDelayMs / 50) * 50, -2000, 2000);
        if (string.IsNullOrWhiteSpace(AudioDevice))
        {
            AudioDevice = "auto";
        }

        MusicAudioDevice = MusicAudioDevice?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(HomeBackgroundImage))
        {
            HomeBackgroundImage = null;
        }

        HomeInfo ??= new JebaitedPlayer.Services.HomeInfoSettings();
        ErrorReports ??= new JebaitedPlayer.Services.ErrorReportSettings();
        if (!HomeBackgroundModes.Contains(HomeBackgroundMode))
        {
            HomeBackgroundMode = "fill";
        }

        if (string.IsNullOrWhiteSpace(DefaultAspect))
        {
            DefaultAspect = "auto";
        }

        Equalizer ??= new EqualizerSettings();
        Equalizer.Normalize();
        MusicEqualizer.Normalize();
        MiniPlayer ??= new MiniPlayerSettings();
        MiniPlayer.Normalize();
        Shortcuts ??= new Dictionary<string, string[]>();
        JebaitedPlayer.Infrastructure.Shortcuts.Normalize(this);
        AccentColor = AccentColors.Normalize(AccentColor);
    }

    public SubtitleStyleSettings SubtitleStyle { get; set; } = new();

    public OnlineSettings Online { get; set; } = new();

    public static AppSettings Load() => Load(AppPaths.DataDir, DateTime.UtcNow);

    /// <summary>
    /// M3a: učitavanje koje nikad ne gubi postavke. Prije je oštećena datoteka tiho dala zadane vrijednosti, a prvo
    /// spremanje ju je prepisalo. Sada se oštećena datoteka premjesti u settings.corrupt-&lt;vrijeme&gt;.json i postavke se
    /// vrate iz najnovije ispravne automatske kopije (M1a). Datoteka koja se ne može pročitati ostaje netaknuta.
    /// <paramref name="recover"/> = false: samo čitanje (npr. jezik za registraciju u Windowsu), bez premještanja.
    /// </summary>
    internal static AppSettings Load(string dataDir, DateTime nowUtc, bool recover = true)
    {
        string file = Path.Combine(dataDir, "settings.json");
        if (!File.Exists(file))
        {
            return new AppSettings { IsNew = true, _file = file };
        }

        string json;
        try
        {
            json = File.ReadAllText(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Postavke se ne mogu pročitati, vrijede zadane, a datoteka se ne prepisuje: {ex.Message}");
            return new AppSettings
            {
                _file = file, _doNotOverwrite = true, LoadReport = new SettingsLoadReport(SettingsLoadOutcome.Unreadable, null, null, ex.Message),
            };
        }

        try
        {
            AppSettings loaded = Parse(json);
            loaded._file = file;
            return loaded;
        }
        catch (InvalidDataException ex)
        {
            return recover
                ? RecoverDamaged(dataDir, file, nowUtc, ex.Message)
                : new AppSettings { _file = file, _doNotOverwrite = true };
        }
    }

    /// <summary>Settings.json → postavke (uz migraciju starog oblika). Sve što nisu ispravne postavke baca <see cref="InvalidDataException"/>.</summary>
    internal static AppSettings Parse(string json)
    {
        JsonObject root;
        try
        {
            root = JsonNode.Parse(json) as JsonObject ?? throw new InvalidDataException("Settings are not a JSON object.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Invalid JSON: " + ex.Message, ex);
        }

        int version = root[nameof(SettingsVersion)] is JsonValue v && v.TryGetValue(out int n) ? n : 1;

        // Migracije oblika idu ovdje, od najstarijeg prema novijem. 1 → 2 ne mijenja polja (dodaje samo oznaku
        // oblika); nove vrijednosti iz novijih verzija dobivaju zadano iz inicijalizatora, a neispravne ispravlja Normalize.
        AppSettings settings;
        try
        {
            settings = root.Deserialize<AppSettings>() ?? throw new InvalidDataException("Empty settings.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Invalid settings: " + ex.Message, ex);
        }

        settings.SettingsVersion = Math.Max(version, CurrentSettingsVersion);
        settings.Normalize();
        settings.RememberForDiff();
        return settings;
    }

    private static AppSettings RecoverDamaged(string dataDir, string file, DateTime nowUtc, string error)
    {
        string corrupt = Path.Combine(dataDir, $"settings.corrupt-{nowUtc.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.json");
        if (File.Exists(corrupt))
        {
            corrupt = corrupt[..^5] + "-" + Guid.NewGuid().ToString("N")[..6] + ".json";
        }

        try
        {
            File.Move(file, corrupt);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Oštećene postavke se ne mogu premjestiti, vrijede zadane, a datoteka se ne prepisuje: {ex.Message}");
            return new AppSettings
            {
                _file = file, _doNotOverwrite = true, LoadReport = new SettingsLoadReport(SettingsLoadOutcome.Unreadable, null, null, error),
            };
        }

        DiagLog.Warn(LogCat.Stability, "prefs.damaged", nameof(AppSettings), "Settings file damaged", ("error", error), ("keptAs", Path.GetFileName(corrupt)));
        foreach (BackupInfo backup in SettingsBackup.List(dataDir))
        {
            string copy = Path.Combine(backup.Folder, "settings.json");
            if (!File.Exists(copy))
            {
                continue;
            }

            try
            {
                string json = File.ReadAllText(copy);
                AppSettings restored = Parse(json);
                File.WriteAllText(file, json);
                restored._file = file;
                restored.LoadReport = new SettingsLoadReport(SettingsLoadOutcome.RestoredFromBackup, corrupt, backup.Created, error);
                Log.Warn($"Postavke su bile oštećene ({error}); vraćene su iz kopije {Path.GetFileName(backup.Folder)}, a oštećena datoteka je sačuvana kao {Path.GetFileName(corrupt)}.");
                return restored;
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                Log.Warn($"Kopija postavki {Path.GetFileName(backup.Folder)} nije upotrebljiva: {ex.Message}");
            }
        }

        Log.Warn($"Postavke su bile oštećene ({error}), a ispravne kopije nema: vrijede zadane. Oštećena datoteka: {Path.GetFileName(corrupt)}.");
        return new AppSettings
        {
            IsNew = true, _file = file, LoadReport = new SettingsLoadReport(SettingsLoadOutcome.ResetToDefaults, corrupt, null, error),
        };
    }

    private Dictionary<string, string>? _lastSaved;

    private static readonly string[] SkipInDiff = ["Window", "Folder", "Key", "Pass", "User", "Secret", "Token", "Path"];

    /// <summary>Zapamti stanje postavki da se pri spremanju zabilježi samo što se promijenilo (bez putanja, ključeva i položaja prozora).</summary>
    internal void RememberForDiff() => _lastSaved = Flatten();

    private Dictionary<string, string> Flatten()
    {
        var result = new Dictionary<string, string>();
        try
        {
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(this, JsonOptions));
            Walk(doc.RootElement, string.Empty, result);
        }
        catch (Exception)
        {
            // Samo dijagnostika.
        }

        return result;
    }

    private static void Walk(JsonElement e, string prefix, Dictionary<string, string> into)
    {
        if (e.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty p in e.EnumerateObject())
            {
                if (!SkipInDiff.Any(s => p.Name.Contains(s, StringComparison.OrdinalIgnoreCase)))
                {
                    Walk(p.Value, prefix.Length == 0 ? p.Name : prefix + "." + p.Name, into);
                }
            }
        }
        else
        {
            into[prefix] = e.ToString();
        }
    }

    private void LogChanges()
    {
        Dictionary<string, string> now = Flatten();
        if (_lastSaved is { } before)
        {
            foreach ((string key, string value) in now)
            {
                if (!before.TryGetValue(key, out string? old) || old != value)
                {
                    DiagLog.Info(LogCat.Usage, "prefs.changed", "AppSettings", "Setting changed", ("name", key), ("from", old), ("to", value));
                }
            }
        }

        _lastSaved = now;
    }

    public void Save()
    {
        if (_doNotOverwrite)
        {
            // M3a: postojeća datoteka nije pročitana; zadane vrijednosti ove sesije je ne smiju prepisati.
            Log.Warn("Postavke se ne spremaju: postojeća datoteka nije pročitana pri pokretanju.");
            return;
        }

        string file = _file ?? AppPaths.SettingsFile;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            string tmp = file + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOptions));
            File.Move(tmp, file, overwrite: true);
            LogChanges();
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

    /// <summary>Boje: ključ naziva (prijevod u Languages/*.json) i boja.</summary>
    public static IReadOnlyList<(string NameKey, string Color)> Colors { get; } =
    [
        ("subSettings.colorWhite", "#FFFFFF"),
        ("subSettings.colorYellow", "#FFE14D"),
        ("subSettings.colorLightGray", "#D9D9D9"),
        ("subSettings.colorLightBlue", "#A8DCFF"),
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
    /// Jezik sučelja samo za ovo pokretanje ("en", "de", "hr"…), bez spremanja u postavke. Bez njega dijagnostika radi
    /// na hrvatskom (provjere u skriptama su pisane za hrvatske tekstove).
    /// </summary>
    public string? DiagLanguage { get; private set; }

    /// <summary>Mapa za snimke u dijagnostici (npr. neutralna putanja za snimke zaslona u priručniku).</summary>
    public string? DiagScreenshotFolder { get; private set; }

    /// <summary>
    /// Tipke koje dijagnostika "pritisne" redom (isti put kao prava tipkovnica), npr. "B;OemPlus;O;S;Ctrl+D".
    /// </summary>
    public string? DiagKeys { get; private set; }

    /// <summary>Test loggera: upiši kontrolirane događaje svih razina (bez pravog crasha).</summary>
    public bool DiagLogInject { get; private set; }

    /// <summary>
    /// Test sučelja stvarnim mišem (UPGRADE 3): "timeline" (klik i povlačenje po traci), "home" (tipka Home, skrivanje
    /// s kontrolama, nastavak gledanja), "home-fullscreen" (isto u cijelom zaslonu). Pomiče pravi kursor.
    /// </summary>
    public string? DiagUiTest { get; private set; }

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
                case "--diag-lang" when i + 1 < args.Length:
                    o.DiagLanguage = args[++i];
                    break;
                case "--diag-screenshot-folder" when i + 1 < args.Length:
                    o.DiagScreenshotFolder = args[++i];
                    break;
                case "--diag-log-inject":
                    o.DiagLogInject = true;
                    break;
                case "--diag-keys" when i + 1 < args.Length:
                    o.DiagKeys = args[++i];
                    break;
                case "--diag-ui-test" when i + 1 < args.Length:
                    o.DiagUiTest = args[++i];
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
        $"{Loc.T("dialog.videoAndAudio")}|{Pattern(Video.Concat(Audio))}|Video|{Pattern(Video)}|Audio|{Pattern(Audio)}|{Loc.T("dialog.allFiles")}|*.*";

    public static string SubtitleDialogFilter =>
        $"{Loc.T("controls.subtitles")}|{string.Join(";", JebaitedPlayer.Subtitles.SubtitleFormats.Extensions.Select(e => "*" + e))}|{Loc.T("dialog.allFiles")}|*.*";

    public static bool IsSubtitle(string path) => JebaitedPlayer.Subtitles.SubtitleFormats.IsSubtitleFile(path);

    private static string Pattern(IEnumerable<string> extensions) =>
        string.Join(";", extensions.Select(e => "*." + e));
}

public sealed record ShortcutInfo(string Keys, string Description);

public static class KeyboardShortcuts
{
    /// <summary>Postavke iz kojih se čitaju tipke (postavlja glavni prozor); bez toga zadane tipke.</summary>
    public static AppSettings Settings { get; set; } = new();

    /// <summary>
    /// F1 i početni ekran: najvažniji prečaci sa stvarnim tipkama (2.0: iz registra i korisnikovih promjena), na trenutnom
    /// jeziku. Red bez ijedne tipke se ne prikazuje. Svi prečaci su u Postavke → Prečaci.
    /// </summary>
    public static IReadOnlyList<ShortcutInfo> All
    {
        get
        {
            AppSettings s = Settings;
            string Keys(params string[] ids) => string.Join(" / ", ids.Select(id => Shortcuts.Gesture(s, id)).Where(k => k.Length > 0));
            string Full(string id) => Shortcuts.Display(s, id);
            static string With(string keys, string extra) => keys.Length == 0 ? string.Empty : $"{keys} / {extra}";
            string first = Shortcuts.Gesture(s, "markAudio"), then = Shortcuts.Gesture(s, "markSubtitle");
            ShortcutInfo[] rows =
            [
                new(Full("playPause"), Loc.T("shortcut.playPause")),
                new(Keys("seekBack", "seekForward"), Loc.T("shortcut.seek5")),
                new(Keys("seekBackLong", "seekForwardLong"), Loc.T("shortcut.seek30")),
                new(With(Keys("volumeUp", "volumeDown"), Loc.T("key.wheel")), Loc.T("shortcut.volume")),
                new(Full("mute"), Loc.T("shortcut.mute")),
                new(With(Full("fullscreen"), Loc.T("key.doubleClick")), Loc.T("shortcut.fullscreen")),
                new(Keys("frameBack", "frameForward"), Loc.T("shortcut.frame")),
                new(Keys("nextEpisode", "previousEpisode", "playlist"), Loc.T("shortcut.episodes")),
                new(Keys("openFile", "openFolder"), Loc.T("shortcut.open")),
                new(Keys("loadSubtitles", "searchSubtitles"), Loc.T("shortcut.subtitleFiles")),
                new(Keys("toggleSubtitles", "nextSubtitles"), Loc.T("shortcut.subtitleToggle")),
                new(Keys("subtitleEarlier", "subtitleLater"), Loc.T("shortcut.subtitleDelay")),
                new(first.Length > 0 && then.Length > 0 ? Loc.T("shortcut.zx.keys", first, then) : string.Empty, Loc.T("shortcut.zx")),
                new(Full("autoSync"), Loc.T("shortcut.autoSync")),
                new(Full("nextAudio"), Loc.T("menu.nextAudio")),
                new(Keys("speedUp", "speedDown", "speedNormal"), Loc.T("shortcut.speed")),
                new(Full("aspect"), Loc.T("shortcut.aspect")),
                new(Keys("screenshot", "screenshotPictureOnly"), Loc.T("shortcut.screenshot")),
                new(Full("favorite"), Loc.T("shortcut.favorite")),
                new(Full("resetSync"), Loc.T("shortcut.resetSync")),
                new(Full("miniPlayer"), Loc.T("menu.miniPlayer")),
                new(Keys("radio", "spotify", "youTube", "discord"), Loc.T("shortcut.services")),
                new(Full("help"), Loc.T("shortcut.help")),
            ];
            return rows.Where(r => r.Keys.Length > 0).ToList();
        }
    }

    public static string AsOsdText() => string.Join("\n", All.Select(s => $"{s.Keys}   —   {s.Description}"));
}

using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using JebaitedPlayer.Library;

namespace JebaitedPlayer.Infrastructure;

/// <summary>Jedna automatska kopija (mapa u backups\).</summary>
public sealed record BackupInfo(string Folder, DateTime Created, string Reason);

/// <summary>
/// Ciklus 2, M1a: automatske lokalne kopije postavki (settings.json) i povijesti gledanja (library.db).
/// Kopija nastaje jednom dnevno pri pokretanju i prije svake nadogradnje; čuva se zadnjih <see cref="Keep"/>.
/// Sve ostaje u mapi podataka na ovom računalu (backups\), ništa se ne šalje.
/// </summary>
public static class SettingsBackup
{
    public const int Keep = 7;

    public static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    private const string StampFormat = "yyyyMMdd-HHmmss";

    public static string BackupsDir(string dataDir) => Path.Combine(dataDir, "backups");

    /// <summary>Pri pokretanju: nova kopija samo ako zadnja ne postoji ili je starija od 24 h.</summary>
    public static BackupInfo? CreateIfDue(string dataDir, WatchStore? store, DateTime nowUtc)
    {
        BackupInfo? last = List(dataDir).FirstOrDefault();
        return last is not null && nowUtc - last.Created < Interval && nowUtc >= last.Created
            ? null
            : Create(dataDir, store, "start", nowUtc);
    }

    /// <summary>
    /// Nova kopija s razlogom ("start", "update-1.4.0", "import"). Baza se kopira preko otvorene veze (dosljedno),
    /// a bez nje kao datoteka (npr. pri zatvaranju, kad je baza već zatvorena).
    /// </summary>
    public static BackupInfo? Create(string dataDir, WatchStore? store, string reason, DateTime nowUtc)
    {
        string settings = Path.Combine(dataDir, "settings.json");
        string library = Path.Combine(dataDir, "library.db");
        if (!File.Exists(settings) && !File.Exists(library))
        {
            return null;
        }

        string safeReason = new(reason.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' ? c : '-').ToArray());
        string folder = Path.Combine(BackupsDir(dataDir), nowUtc.ToString(StampFormat, CultureInfo.InvariantCulture) + "_" + safeReason);
        if (Directory.Exists(folder))
        {
            folder += "-" + Guid.NewGuid().ToString("N")[..6];
        }

        string tmp = folder + ".tmp";
        Directory.CreateDirectory(tmp);
        try
        {
            if (File.Exists(settings))
            {
                File.Copy(settings, Path.Combine(tmp, "settings.json"), overwrite: true);
            }

            if (store is not null)
            {
                store.BackupTo(Path.Combine(tmp, "library.db"));
            }
            else if (File.Exists(library))
            {
                File.Copy(library, Path.Combine(tmp, "library.db"), overwrite: true);
                if (File.Exists(library + "-wal"))
                {
                    File.Copy(library + "-wal", Path.Combine(tmp, "library.db-wal"), overwrite: true);
                }
            }

            // Mapa dobiva pravo ime tek kad je kopija potpuna (prekinuta kopija se nikad ne čita kao dobra).
            Directory.Move(tmp, folder);
        }
        catch
        {
            TryDelete(tmp);
            throw;
        }

        Prune(dataDir, Keep);
        return new BackupInfo(folder, nowUtc, safeReason);
    }

    /// <summary>Kopije, najnovija prva.</summary>
    public static IReadOnlyList<BackupInfo> List(string dataDir)
    {
        string dir = BackupsDir(dataDir);
        if (!Directory.Exists(dir))
        {
            return [];
        }

        var list = new List<BackupInfo>();
        foreach (string folder in Directory.EnumerateDirectories(dir))
        {
            string name = Path.GetFileName(folder);
            int split = name.IndexOf('_');
            if (split == StampFormat.Length && !name.EndsWith(".tmp", StringComparison.Ordinal) &&
                DateTime.TryParseExact(name[..split], StampFormat, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime created))
            {
                list.Add(new BackupInfo(folder, created, name[(split + 1)..]));
            }
        }

        return list.OrderByDescending(b => b.Created).ThenByDescending(b => b.Folder, StringComparer.Ordinal).ToList();
    }

    private static void Prune(string dataDir, int keep)
    {
        foreach (BackupInfo old in List(dataDir).Skip(keep))
        {
            TryDelete(old.Folder);
        }

        foreach (string stale in Directory.EnumerateDirectories(BackupsDir(dataDir), "*.tmp"))
        {
            TryDelete(stale);
        }
    }

    private static void TryDelete(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Stara kopija postavki nije obrisana: {ex.Message}");
        }
    }
}

/// <summary>
/// Ciklus 2, M1b: ručni izvoz i uvoz postavki. Izvoz ne sadrži lozinke ni ključeve za stranice s titlovima
/// ni položaj prozora; uvoz ih zato ne dira. Uvoz prihvaća i settings.json iz automatske kopije.
/// </summary>
public static class SettingsTransfer
{
    public const string Kind = "jebaited_player.settings";

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>Postavke vezane uz ovo računalo i zaslon: ne izvoze se i uvoz ih ne mijenja.</summary>
    private static readonly string[] MachineOnly =
    [
        nameof(AppSettings.WindowLeft), nameof(AppSettings.WindowTop), nameof(AppSettings.WindowWidth),
        nameof(AppSettings.WindowHeight), nameof(AppSettings.WindowMaximized),
        nameof(AppSettings.HomeBackgroundImage), // putanja do slike na ovom računalu (N4)
    ];

    public static bool IsSecret(string name) =>
        name.EndsWith("ApiKey", StringComparison.Ordinal) || name.EndsWith("Username", StringComparison.Ordinal) ||
        name.EndsWith("Password", StringComparison.Ordinal);

    public static string DefaultFileName(DateTime now) => $"jebaited_player-settings-{now:yyyy-MM-dd}.json";

    public static string Export(AppSettings settings, string appVersion, DateTime nowUtc)
    {
        JsonObject values = JsonSerializer.SerializeToNode(settings)!.AsObject();
        foreach (string name in MachineOnly)
        {
            values.Remove(name);
        }

        if (values[nameof(AppSettings.Online)] is JsonObject online)
        {
            foreach (string key in online.Select(p => p.Key).Where(IsSecret).ToList())
            {
                online.Remove(key);
            }
        }

        var root = new JsonObject
        {
            ["kind"] = Kind,
            ["app"] = "jebaited_player",
            ["version"] = appVersion,
            ["exported"] = nowUtc.ToString("o", CultureInfo.InvariantCulture),
            ["settings"] = values,
        };
        return root.ToJsonString(Indented);
    }

    /// <summary>
    /// Pročitaj izvezenu datoteku ili settings.json (kopija). Datoteka koja nije postavka playera baca
    /// <see cref="InvalidDataException"/>, da slučajno odabrana datoteka ne vrati sve na zadano.
    /// </summary>
    public static AppSettings Read(string json)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Not JSON.", ex);
        }

        JsonObject values = (root is JsonObject o && o["kind"]?.GetValueKind() == JsonValueKind.String && (string?)o["kind"] == Kind
                                ? o["settings"] as JsonObject
                                : root as JsonObject)
                            ?? throw new InvalidDataException("No settings.");
        HashSet<string> known = typeof(AppSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).ToHashSet();
        if (values.Count(p => known.Contains(p.Key)) < 3)
        {
            throw new InvalidDataException("Not jebaited_player settings.");
        }

        AppSettings settings;
        try
        {
            settings = values.Deserialize<AppSettings>() ?? throw new InvalidDataException("Empty settings.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Invalid settings.", ex);
        }

        settings.Normalize();
        return settings;
    }

    /// <summary>Prepiši uvezene vrijednosti u postavke koje player koristi (isti objekt), osim položaja prozora i lozinki/ključeva.</summary>
    public static void ApplyTo(AppSettings target, AppSettings imported)
    {
        foreach (PropertyInfo p in typeof(AppSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.GetSetMethod() is null || p.GetCustomAttribute<JsonIgnoreAttribute>() is not null ||
                MachineOnly.Contains(p.Name) || p.Name == nameof(AppSettings.Online))
            {
                continue;
            }

            p.SetValue(target, p.GetValue(imported));
        }

        foreach (PropertyInfo p in typeof(OnlineSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.GetSetMethod() is not null && !IsSecret(p.Name))
            {
                p.SetValue(target.Online, p.GetValue(imported.Online));
            }
        }
    }
}

/// <summary>
/// Ciklus 3, M2a: radna kopija postavki u prozoru postavki. Promjene vrijede odmah; „Spremi” ih trajno sprema,
/// a „Odbaci promjene” vraća stanje zadnjeg spremanja. Glasnoća, položaji prozora i mini playera nisu „postavke”
/// koje se odbacuju (mijenjaju se i izvan prozora postavki).
/// </summary>
public static class SettingsWorkingCopy
{
    private static readonly string[] Volatile =
    [
        nameof(AppSettings.Volume), nameof(AppSettings.Muted), nameof(AppSettings.WindowLeft), nameof(AppSettings.WindowTop),
        nameof(AppSettings.WindowWidth), nameof(AppSettings.WindowHeight), nameof(AppSettings.WindowMaximized),
    ];

    private static readonly string[] VolatileMini =
    [
        nameof(MiniPlayerSettings.Left), nameof(MiniPlayerSettings.Top), nameof(MiniPlayerSettings.Scale), nameof(MiniPlayerSettings.Compact),
        nameof(MiniPlayerSettings.ShowEqualizer), nameof(MiniPlayerSettings.ShowPlaylist), nameof(MiniPlayerSettings.WasOpen),
        nameof(MiniPlayerSettings.Design), nameof(MiniPlayerSettings.InfoFields),
    ];

    /// <summary>Cijelo stanje (za vraćanje).</summary>
    public static string Snapshot(AppSettings s) => JsonSerializer.Serialize(s);

    /// <summary>Usporedivo stanje: bez glasnoće i položaja (za „nespremljene promjene”).</summary>
    public static string Comparable(AppSettings s)
    {
        JsonObject o = JsonSerializer.SerializeToNode(s)!.AsObject();
        foreach (string name in Volatile)
        {
            o.Remove(name);
        }

        if (o[nameof(AppSettings.MiniPlayer)] is JsonObject mini)
        {
            foreach (string name in VolatileMini)
            {
                mini.Remove(name);
            }
        }

        // Ključevi i lozinke se uređuju u vlastitom prozoru (izvori titlova) i ne odbacuju se ovdje.
        if (o[nameof(AppSettings.Online)] is JsonObject online)
        {
            foreach (string key in online.Select(p => p.Key).Where(SettingsTransfer.IsSecret).ToList())
            {
                online.Remove(key);
            }
        }

        return o.ToJsonString();
    }

    public static string ComparableSnapshot(string snapshot) => Comparable(JsonSerializer.Deserialize<AppSettings>(snapshot)!);

    public static bool IsDirty(AppSettings current, string snapshot) => Comparable(current) != ComparableSnapshot(snapshot);

    /// <summary>Vrati stanje iz snimke (u isti objekt), a glasnoću, položaje i ključeve ostavi kakvi su sada.</summary>
    public static void Revert(AppSettings current, string snapshot)
    {
        AppSettings saved = JsonSerializer.Deserialize<AppSettings>(snapshot)!;
        saved.Normalize();
        saved.Volume = current.Volume;
        saved.Muted = current.Muted;
        MiniPlayerSettings now = current.MiniPlayer;
        (saved.MiniPlayer.Left, saved.MiniPlayer.Top, saved.MiniPlayer.Scale) = (now.Left, now.Top, now.Scale);
        (saved.MiniPlayer.Compact, saved.MiniPlayer.ShowEqualizer, saved.MiniPlayer.ShowPlaylist, saved.MiniPlayer.WasOpen) =
            (now.Compact, now.ShowEqualizer, now.ShowPlaylist, now.WasOpen);
        (saved.MiniPlayer.Design, saved.MiniPlayer.InfoFields) = (now.Design, now.InfoFields);
        SettingsTransfer.ApplyTo(current, saved);

        // Uvoz ne dira sliku pozadine (putanja na ovom računalu), ali Odbaci je vraća (N4).
        current.HomeBackgroundImage = saved.HomeBackgroundImage;
    }
}

/// <summary>
/// Ciklus 3, M2b: zadane vrijednosti za jednu karticu postavki ili za sve. Nikad se ne diraju jezik, ključevi i
/// lozinke, glasnoća te položaji prozora i mini playera. Kao i ostale promjene, trajno vrijedi tek na „Spremi”.
/// </summary>
public static class SettingsDefaults
{
    /// <summary>Kartice koje imaju zadane vrijednosti (Jezik i O aplikaciji nemaju).</summary>
    public static readonly IReadOnlyList<string> Tabs = ["general", "subtitles", "audio", "video", "appearance", "shortcuts", "updates"];

    public static void Reset(AppSettings s, string? tab)
    {
        var d = new AppSettings();
        foreach (string t in tab is null ? Tabs : [tab])
        {
            switch (t)
            {
                case "general":
                    (s.ResumeMode, s.SeekStepSeconds, s.OpenFullscreen, s.OnTopWhilePlaying) = (d.ResumeMode, d.SeekStepSeconds, d.OpenFullscreen, d.OnTopWhilePlaying);
                    (s.AutoPlayNext, s.UpNextSeconds, s.ScreenshotFolder) = (d.AutoPlayNext, d.UpNextSeconds, d.ScreenshotFolder);
                    s.UpNextLeadSeconds = d.UpNextLeadSeconds;
                    break;
                case "subtitles":
                    s.SubtitleLanguages = AppSettings.DefaultSubtitleLanguages(s.Language ?? "en");
                    (s.AutoLoadSubtitles, s.AutoSyncSubtitles, s.SubtitleCodePage) = (d.AutoLoadSubtitles, d.AutoSyncSubtitles, d.SubtitleCodePage);
                    s.SubtitleStyle = new SubtitleStyleSettings();
                    (s.Online.AutoSearch, s.Online.SaveNextToVideo) = (d.Online.AutoSearch, d.Online.SaveNextToVideo);
                    (s.Online.OpenSubtitlesEnabled, s.Online.SubDlEnabled, s.Online.TitloviEnabled) =
                        (d.Online.OpenSubtitlesEnabled, d.Online.SubDlEnabled, d.Online.TitloviEnabled);
                    break;
                case "audio":
                    (s.NormalizeAudio, s.AudioLanguage, s.AudioDevice, s.AudioDelayMs) = (d.NormalizeAudio, d.AudioLanguage, d.AudioDevice, d.AudioDelayMs);
                    s.MusicAudioDevice = d.MusicAudioDevice;
                    s.Equalizer = new EqualizerSettings();
                    s.MusicEqualizer = new EqualizerSettings();
                    break;
                case "video":
                    (s.HardwareDecoding, s.VideoQuality, s.DefaultAspect, s.HdrPassthrough) = (d.HardwareDecoding, d.VideoQuality, d.DefaultAspect, d.HdrPassthrough);
                    break;
                case "appearance":
                    (s.Theme, s.ControlsHideSeconds, s.UiGpuRendering, s.AccentColor) = (d.Theme, d.ControlsHideSeconds, d.UiGpuRendering, d.AccentColor);
                    (s.MiniPlayer.Font, s.MiniPlayer.VuStyle, s.MiniPlayer.AutoSwitch) = (d.MiniPlayer.Font, d.MiniPlayer.VuStyle, d.MiniPlayer.AutoSwitch);
                    (s.HomeBackgroundImage, s.HomeBackgroundMode) = (d.HomeBackgroundImage, d.HomeBackgroundMode);
                    (s.HomeInfo.ShowTime, s.HomeInfo.ShowDate, s.HomeInfo.ShowWeather) = (true, true, false);
                    s.HomeInfo.ResetStyle();
                    break;
                case "shortcuts":
                    s.Shortcuts = new Dictionary<string, string[]>();
                    break;
                case "updates":
                    s.AutoUpdate = d.AutoUpdate;
                    break;
            }
        }
    }
}

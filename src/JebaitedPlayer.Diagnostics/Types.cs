using System.Text.Json;
using System.Text.Json.Serialization;

namespace JebaitedPlayer.Diagnostics;

public enum LogLevel
{
    Debug = 0,
    Info = 1,
    Warning = 2,
    Error = 3,
    Critical = 4,
}

/// <summary>Kategorije događaja (polje "cat" u zapisu).</summary>
public static class LogCat
{
    public const string App = "app";
    public const string Usage = "usage";
    public const string Playback = "playback";
    public const string Audio = "audio";
    public const string Video = "video";
    public const string Subtitle = "subtitle";
    public const string Ui = "ui";
    public const string Stability = "stability";
    public const string Update = "update";
    public const string Logger = "logger";
}

/// <summary>Postavke loggera (logger_config.json). Nepoznata ili neispravna datoteka znači zadane vrijednosti.</summary>
public sealed class LoggerOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>Najniža razina koja ide u session.log ("Info" zadano). Debug ide samo u diagnostics.log (ako je uključen) i u kontekst grešaka.</summary>
    public LogLevel MinLevel { get; set; } = LogLevel.Info;

    /// <summary>Piši DEBUG događaje u diagnostics.log (zadano isključeno; kontekst oko grešaka svejedno sadrži DEBUG).</summary>
    public bool DebugFile { get; set; }

    public int QueueCapacity { get; set; } = 2048;

    /// <summary>Koliko zadnjih događaja se čuva u memoriji kao kontekst prije greške.</summary>
    public int ContextBefore { get; set; } = 200;

    /// <summary>Koliko se događaja nakon greške dodatno upiše u errors.log.</summary>
    public int ContextAfter { get; set; } = 20;

    public int FlushMs { get; set; } = 250;

    public long MaxFileBytes { get; set; } = 5 * 1024 * 1024;

    public int MaxRotations { get; set; } = 5;

    /// <summary>Normalni dani se čuvaju ovoliko dana.</summary>
    public int KeepDays { get; set; } = 14;

    /// <summary>Dani s ERROR/CRITICAL (nakon KeepDays samo errors.log).</summary>
    public int KeepErrorDays { get; set; } = 30;

    /// <summary>Ciljana ukupna veličina logova; iznad nje brišu se najstariji dani.</summary>
    public long TargetBytes { get; set; } = 50L * 1024 * 1024;

    /// <summary>Tvrdi maksimum; iznad njega brišu se i rotirane datoteke današnjeg dana.</summary>
    public long MaxBytes { get; set; } = 150L * 1024 * 1024;

    /// <summary>Slobodno mjesta na disku ispod kojeg se bilježe samo WARNING i više.</summary>
    public long LowDiskBytes { get; set; } = 200L * 1024 * 1024;

    /// <summary>Slobodno mjesta na disku ispod kojeg se zapisivanje isključuje do sljedećeg čišćenja.</summary>
    public long CriticalDiskBytes { get; set; } = 50L * 1024 * 1024;

    public int JanitorMinutes { get; set; } = 30;

    internal static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static LoggerOptions Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var o = JsonSerializer.Deserialize<LoggerOptions>(File.ReadAllText(path), Json);
                if (o != null)
                {
                    return o.Normalize();
                }
            }
        }
        catch (Exception)
        {
            // Neispravna datoteka: zadane vrijednosti.
        }

        return new LoggerOptions();
    }

    public void Save(string path)
    {
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(this, Json));
        }
        catch (Exception)
        {
            // Nije bitno za rad.
        }
    }

    private LoggerOptions Normalize()
    {
        QueueCapacity = Math.Clamp(QueueCapacity, 64, 16384);
        ContextBefore = Math.Clamp(ContextBefore, 0, 2000);
        ContextAfter = Math.Clamp(ContextAfter, 0, 500);
        FlushMs = Math.Clamp(FlushMs, 50, 5000);
        MaxFileBytes = Math.Clamp(MaxFileBytes, 256 * 1024, 50L * 1024 * 1024);
        MaxRotations = Math.Clamp(MaxRotations, 1, 20);
        KeepDays = Math.Clamp(KeepDays, 1, 90);
        KeepErrorDays = Math.Clamp(KeepErrorDays, KeepDays, 180);
        TargetBytes = Math.Clamp(TargetBytes, 5L * 1024 * 1024, 1024L * 1024 * 1024);
        MaxBytes = Math.Clamp(MaxBytes, TargetBytes, 2048L * 1024 * 1024);
        JanitorMinutes = Math.Clamp(JanitorMinutes, 5, 1440);
        return this;
    }
}

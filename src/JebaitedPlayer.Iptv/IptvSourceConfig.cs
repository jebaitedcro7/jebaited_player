using System.Text.Json;
using System.Text.Json.Serialization;

namespace JebaitedPlayer.Iptv;

/// <summary>
/// Postavke jednog izvora. <see cref="PasswordProtected"/> je već zaštićen (DPAPI) izvan ove biblioteke; ovdje je neprozirni tekst.
/// Za M3U URL s vjerodajnicama u adresi cijela adresa ide u <see cref="UrlProtected"/>.
/// </summary>
public sealed class IptvSourceConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "IPTV";
    public IptvSourceKind Kind { get; set; }
    public string? Server { get; set; }
    public string? Username { get; set; }
    public string? PasswordProtected { get; set; }
    public string? UrlProtected { get; set; }
    public string? FilePath { get; set; }
    public string? XmltvUrl { get; set; }
}

public sealed class IptvSettings
{
    public bool Enabled { get; set; } = true;
    public string? DefaultSourceId { get; set; }
    public ChannelSort DefaultSort { get; set; } = ChannelSort.Original;
    public string? DefaultCategory { get; set; }
    public bool CompactRows { get; set; }
    public bool ResumeLastChannel { get; set; }

    /// <summary>Izgled IPTV sekcije: naziv fonta (null = zadani), mjerilo teksta (1 = 100 %), boje kao #RRGGBB (null = tema).</summary>
    public string? FontFamily { get; set; }
    public double TextScale { get; set; } = 1.0;

    /// <summary>Širina lijevog IPTV panela i popisa kategorija (zemalja), u pikselima; korisnik ih povlači mišem.</summary>
    public double PanelWidth { get; set; } = 520;
    public double CategoryWidth { get; set; } = 150;
    public string? AccentColor { get; set; }
    public string? BackgroundColor { get; set; }
    public string? LastChannelId { get; set; }
    public string? LastSourceId { get; set; }
    public List<IptvSourceConfig> Sources { get; set; } = [];
}

/// <summary>JSON spremište: <c>iptv.json</c> (postavke i izvori) i <c>iptv-user-{izvor}.json</c> (favoriti, redoslijed…).</summary>
public sealed class IptvStore(string directory)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    private string SettingsPath => Path.Combine(directory, "iptv.json");

    private string UserPath(string sourceId) => Path.Combine(directory, "iptv-user-" + Sanitize(sourceId) + ".json");

    public IptvSettings LoadSettings() => Read<IptvSettings>(SettingsPath) ?? new IptvSettings();

    public void SaveSettings(IptvSettings s) => Write(SettingsPath, s);

    public IptvUserData LoadUser(string sourceId) => Read<IptvUserData>(UserPath(sourceId)) ?? new IptvUserData();

    public void SaveUser(string sourceId, IptvUserData d) => Write(UserPath(sourceId), d);

    public void DeleteUser(string sourceId)
    {
        try { File.Delete(UserPath(sourceId)); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>Izvoz neosjetljivih postavki (bez lozinki, korisničkih imena i adresa s vjerodajnicama).</summary>
    public string ExportPreferences(IptvSettings s, IptvUserData user)
    {
        var safe = new
        {
            s.DefaultSort,
            s.DefaultCategory,
            s.CompactRows,
            s.ResumeLastChannel,
            user.Favorites,
            user.HiddenChannels,
            user.HiddenCategories,
            user.ManualOrder,
        };
        return JsonSerializer.Serialize(safe, Json);
    }

    public void ImportPreferences(string json, IptvSettings s, IptvUserData user)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement r = doc.RootElement;
        if (r.TryGetProperty("DefaultSort", out JsonElement v) && Enum.TryParse(v.GetString(), out ChannelSort sort))
        {
            s.DefaultSort = sort;
        }

        if (r.TryGetProperty("DefaultCategory", out v)) { s.DefaultCategory = v.GetString(); }
        if (r.TryGetProperty("CompactRows", out v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False) { s.CompactRows = v.GetBoolean(); }
        if (r.TryGetProperty("ResumeLastChannel", out v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False) { s.ResumeLastChannel = v.GetBoolean(); }
        user.Favorites = Strings(r, "Favorites") ?? user.Favorites;
        user.HiddenChannels = Strings(r, "HiddenChannels") ?? user.HiddenChannels;
        user.HiddenCategories = Strings(r, "HiddenCategories") ?? user.HiddenCategories;
        user.ManualOrder = Strings(r, "ManualOrder") ?? user.ManualOrder;
    }

    private static List<string>? Strings(JsonElement r, string name) =>
        r.TryGetProperty(name, out JsonElement a) && a.ValueKind == JsonValueKind.Array
            ? a.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).Distinct().ToList()
            : null;

    private static string Sanitize(string id) => new(id.Where(char.IsLetterOrDigit).ToArray());

    private static T? Read<T>(string path) where T : class
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json) : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // Atomarno: pisanje u privremenu datoteku pa zamjena (prekid usred zapisa ne uništava postojeće podatke).
    private void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(directory);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, Json));
        File.Move(tmp, path, overwrite: true);
    }
}

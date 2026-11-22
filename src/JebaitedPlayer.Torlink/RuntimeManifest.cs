using System.Text.Json;
using System.Text.Json.Serialization;

namespace JebaitedPlayer.Torlink;

/// <summary>
/// <c>MANIFEST.json</c> u korijenu runtime paketa (piše ga naš CI): verzije i SHA-256 svake datoteke.
/// Instalacija odbija paket u kojem se ijedna datoteka ne podudara.
/// </summary>
public sealed class RuntimeManifest
{
    public const string FileName = "MANIFEST.json";
    public const int CurrentSchema = 1;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    [JsonPropertyName("schema")] public int Schema { get; set; } = CurrentSchema;
    [JsonPropertyName("torlinkVersion")] public string TorlinkVersion { get; set; } = string.Empty;
    [JsonPropertyName("revision")] public int Revision { get; set; } = 1;
    [JsonPropertyName("nodeVersion")] public string NodeVersion { get; set; } = string.Empty;
    [JsonPropertyName("upstreamCommit")] public string UpstreamCommit { get; set; } = string.Empty;
    [JsonPropertyName("builtAt")] public string BuiltAt { get; set; } = string.Empty;

    /// <summary>Relativna putanja (kose crte) → SHA-256 (mala slova).</summary>
    [JsonPropertyName("files")] public Dictionary<string, string> Files { get; set; } = new();

    [JsonIgnore] public string Display => $"{TorlinkVersion} (r{Revision}, Node {NodeVersion})";

    [JsonIgnore]
    public TorlinkVersion Version =>
        Torlink.TorlinkVersion.TryParse(TorlinkVersion, Revision, out TorlinkVersion v) ? v : default;

    public static RuntimeManifest? TryRead(string file)
    {
        try
        {
            if (!File.Exists(file))
            {
                return null;
            }

            var m = JsonSerializer.Deserialize<RuntimeManifest>(File.ReadAllText(file), Json);
            return m is { Schema: CurrentSchema } && m.TorlinkVersion.Length > 0 && m.Files.Count > 0 ? m : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this, Json);
}

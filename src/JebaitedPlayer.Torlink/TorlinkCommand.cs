using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace JebaitedPlayer.Torlink;

/// <summary>Argumenti i okolina za Torlinkove procese (čiste funkcije, jedinično testirane). Token nikad ne ide u argumente.</summary>
public static class TorlinkCommand
{
    public const string TokenVariable = "TORLINK_API_TOKEN";

    public static IReadOnlyList<string> ServeArgs(TorlinkPaths paths, TorlinkOptions o, int port)
    {
        var a = new List<string> { paths.CliScript, "serve", "--port", port.ToString(), "--host", "127.0.0.1", "--to", o.EffectiveDownloadDir };
        if (!o.Playlist)
        {
            a.Add("--no-playlist");
        }

        // Dijeljenje isključeno: Torlinkov "reaper" zaustavlja dijeljenje nakon 1 s (provjera svakih 30 s i pri pokretanju);
        // player uz to odmah zove stop-seed. Datoteke se ne brišu.
        a.AddRange(["--seed-time", o.SeedAfter ? (o.SeedHours * 3600).ToString() : "1"]);
        AddNetwork(a, o);
        return a;
    }

    public static IReadOnlyList<string> SearchArgs(TorlinkPaths paths, TorlinkOptions o, string query, string? category, int timeoutSeconds)
    {
        var a = new List<string> { paths.CliScript, "search", query };
        if (!string.IsNullOrEmpty(category))
        {
            a.AddRange(["--category", category]);
        }

        a.AddRange(["--timeout", $"{Math.Max(5, timeoutSeconds)}s"]);
        if (!string.IsNullOrWhiteSpace(o.Interface))
        {
            a.AddRange(["--interface", o.Interface.Trim()]);
        }

        return a;
    }

    private static void AddNetwork(List<string> a, TorlinkOptions o)
    {
        if (!string.IsNullOrWhiteSpace(o.Interface))
        {
            a.AddRange(["--interface", o.Interface.Trim()]);
        }

        if (o.PeerPort is >= 1 and <= 65535)
        {
            a.AddRange(["--peer-port", o.PeerPort.ToString()]);
        }
    }

    /// <summary>Okolina procesa: vlastito stanje, bez provjere ažuriranja i bez WebRTC-a; token samo ovdje.</summary>
    public static IReadOnlyDictionary<string, string> Environment(TorlinkPaths paths, TorlinkOptions o, string? token)
    {
        var e = new Dictionary<string, string>
        {
            ["TORLINK_STATE_DIR"] = paths.StateDir,
            ["TORLINK_NO_UPDATE_CHECK"] = "1",
            ["TORLINK_NO_WEBRTC"] = "1",
            ["TORLINK_MAX_DOWNLOADS"] = Math.Max(0, o.MaxDownloads).ToString(),
            ["NO_COLOR"] = "1",
        };
        if (!string.IsNullOrEmpty(token))
        {
            e[TokenVariable] = token;
        }

        return e;
    }

    /// <summary>Torlinkov config.json (samo downloadDir i trackeri, što Torlink stvarno čita).</summary>
    public static string ConfigJson(TorlinkOptions o)
    {
        var obj = new JsonObject
        {
            ["downloadDir"] = o.EffectiveDownloadDir,
            ["trackers"] = new JsonArray(o.Trackers.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => (JsonNode?)JsonValue.Create(t.Trim())).ToArray()),
        };
        return obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    public static void WriteConfig(TorlinkPaths paths, TorlinkOptions o)
    {
        Directory.CreateDirectory(paths.StateConfigDir);
        string tmp = paths.ConfigFile + ".tmp";
        File.WriteAllText(tmp, ConfigJson(o), new UTF8Encoding(false));
        File.Move(tmp, paths.ConfigFile, overwrite: true);
    }

    public static ProcessStartInfo StartInfo(TorlinkPaths paths, IEnumerable<string> args, IReadOnlyDictionary<string, string> env)
    {
        var psi = new ProcessStartInfo(paths.NodeExe)
        {
            WorkingDirectory = paths.RuntimeDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (string arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        foreach ((string k, string v) in env)
        {
            psi.Environment[k] = v;
        }

        return psi;
    }
}

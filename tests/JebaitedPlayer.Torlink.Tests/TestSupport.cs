using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace JebaitedPlayer.Torlink.Tests;

internal sealed class TempDir : IDisposable
{
    public TempDir() => Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "torlink-tests-" + Guid.NewGuid().ToString("N"));

    public string Path { get; }

    public TorlinkPaths Paths => new(Path);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, true);
            }
        }
        catch (IOException)
        {
        }
    }
}

internal static class TestRuntime
{
    /// <summary>Stvori minimalan "instaliran" runtime (lažni node.exe i cli.cjs + ispravan manifest).</summary>
    public static RuntimeManifest Install(TorlinkPaths paths, string version = "1.11.1", int revision = 1)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(paths.CliScript)!);
        File.WriteAllText(paths.NodeExe, "node-" + version);
        File.WriteAllText(paths.CliScript, "cli-" + version);
        var m = new RuntimeManifest
        {
            TorlinkVersion = version,
            Revision = revision,
            NodeVersion = "22.23.5",
            Files =
            {
                ["node.exe"] = RuntimeStore.Sha256File(paths.NodeExe),
                ["torlink/dist/cli.cjs"] = RuntimeStore.Sha256File(paths.CliScript),
            },
        };
        File.WriteAllText(paths.ManifestFile, m.ToJson());
        return m;
    }

    /// <summary>Zip u obliku našeg paketa; <paramref name="tamper"/> mijenja sadržaj nakon izračuna manifesta.</summary>
    public static string BuildZip(string dir, string version, int revision = 1, Action<Dictionary<string, byte[]>>? tamper = null,
        Action<RuntimeManifest>? manifest = null, IEnumerable<(string Name, byte[] Data)>? extra = null)
    {
        var files = new Dictionary<string, byte[]>
        {
            ["node.exe"] = Encoding.UTF8.GetBytes("node-" + version),
            ["torlink/dist/cli.cjs"] = Encoding.UTF8.GetBytes("cli-" + version),
            ["torlink/package.json"] = Encoding.UTF8.GetBytes("{\"name\":\"torlnk\",\"version\":\"" + version + "\"}"),
        };
        var m = new RuntimeManifest { TorlinkVersion = version, Revision = revision, NodeVersion = "22.23.5" };
        foreach ((string name, byte[] data) in files)
        {
            m.Files[name] = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
        }

        manifest?.Invoke(m);
        tamper?.Invoke(files);
        Directory.CreateDirectory(dir);
        string zip = Path.Combine(dir, $"torlink-runtime-{version}-r{revision}-win-x64.zip");
        using (ZipArchive z = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            foreach ((string name, byte[] data) in files)
            {
                Add(z, name, data);
            }

            Add(z, RuntimeManifest.FileName, Encoding.UTF8.GetBytes(m.ToJson()));
            foreach ((string name, byte[] data) in extra ?? [])
            {
                Add(z, name, data);
            }
        }

        return zip;
    }

    private static void Add(ZipArchive z, string name, byte[] data)
    {
        ZipArchiveEntry e = z.CreateEntry(name);
        using Stream s = e.Open();
        s.Write(data);
    }
}

internal sealed class FakeHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

    public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

    public List<(HttpMethod Method, string Path, string Body)> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        lock (Requests)
        {
            Requests.Add((request.Method, request.RequestUri!.AbsolutePath, body));
        }

        return _respond(request);
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}

internal sealed class FakeProcess : IServeProcess
{
    private static int _next = 1000;

    public int Id { get; } = Interlocked.Increment(ref _next);
    public bool HasExited { get; private set; }
    public bool Killed { get; private set; }
    public event Action<int>? Exited;
    public IReadOnlyList<string> RecentOutput { get; } = ["fake"];

    public void Kill()
    {
        Killed = true;
        HasExited = true;
        Exited?.Invoke(1);
    }

    public void Crash()
    {
        HasExited = true;
        Exited?.Invoke(1);
    }

    public void Dispose()
    {
    }
}

internal sealed class FakeLauncher : IProcessLauncher
{
    public List<FakeProcess> Started { get; } = [];
    public List<ProcessStartInfo> StartInfos { get; } = [];
    public Func<ProcessStartInfo, ProcessResult>? Run { get; set; }

    public IServeProcess Start(ProcessStartInfo startInfo)
    {
        var p = new FakeProcess();
        Started.Add(p);
        StartInfos.Add(startInfo);
        return p;
    }

    public Task<ProcessResult> RunAsync(ProcessStartInfo startInfo, TimeSpan timeout, CancellationToken ct)
    {
        StartInfos.Add(startInfo);
        return Task.FromResult(Run?.Invoke(startInfo) ?? new ProcessResult(0, "{}", string.Empty, false));
    }
}

internal sealed class FakeClock : TimeProvider
{
    private DateTimeOffset _now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

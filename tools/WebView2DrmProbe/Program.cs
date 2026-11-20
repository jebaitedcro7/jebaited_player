using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

// Pokus S2 (ciklus 5): u WebView2 (Edge/Chromium ugrađen u aplikaciju) traži Widevine i PlayReady preko EME,
// kao što to radi Spotify Web Playback SDK. Bez prijave i bez Premiuma: provjera samo je li DRM uopće dostupan.
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        string output = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "drm-result.json");
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var web = new WebView2();
        var window = new Window { Width = 320, Height = 200, Content = web, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -2000, Top = -2000 };
        window.Loaded += async (_, _) =>
        {
            string result;
            try
            {
                string data = Path.Combine(Path.GetTempPath(), "jp-webview2-probe");
                CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync(null, data);
                await web.EnsureCoreWebView2Async(env);
                // EME postoji samo u sigurnom kontekstu (https), pa se pokus radi na javnoj https stranici.
                var loaded = new TaskCompletionSource<bool>();
                web.CoreWebView2.NavigationCompleted += (_, e) => loaded.TrySetResult(e.IsSuccess);
                web.CoreWebView2.Navigate("https://example.com/");
                await Task.WhenAny(loaded.Task, Task.Delay(15000));
                await Task.Delay(500);
                string script = """
                    (async () => {
                      const cfg = [{ initDataTypes: ['cenc'], audioCapabilities: [{ contentType: 'audio/mp4; codecs="mp4a.40.2"' }] }];
                      const test = async (ks) => { try { await navigator.requestMediaKeySystemAccess(ks, cfg); return 'yes'; } catch (e) { return 'no: ' + e.name; } };
                      return JSON.stringify({ widevine: await test('com.widevine.alpha'), playready: await test('com.microsoft.playready.recommendation'),
                                              userAgent: navigator.userAgent, eme: typeof navigator.requestMediaKeySystemAccess });
                    })()
                    """;
                // ExecuteScriptAsync ne čeka Promise; zato se rezultat čita drugim pozivom preko globalne varijable.
                await web.CoreWebView2.ExecuteScriptAsync("window.__probe = null; (" + script.Replace("return JSON.stringify", "window.__probe = JSON.stringify") + ")");
                string value = "null";
                for (int i = 0; i < 20 && value == "null"; i++)
                {
                    await Task.Delay(250);
                    value = await web.CoreWebView2.ExecuteScriptAsync("window.__probe");
                }

                result = $"{{\"runtime\":\"{CoreWebView2Environment.GetAvailableBrowserVersionString()}\",\"probe\":{value}}}";
            }
            catch (Exception ex)
            {
                result = $"{{\"error\":{System.Text.Json.JsonSerializer.Serialize(ex.GetType().Name + ": " + ex.Message)}}}";
            }

            File.WriteAllText(output, result);
            app.Shutdown();
        };
        window.Show();
        return app.Run();
    }
}

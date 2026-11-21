using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.Online;
using JebaitedPlayer.Services;
using Microsoft.Web.WebView2.Core;

namespace JebaitedPlayer.Views;

/// <summary>
/// Ciklus 6, V9: vidljiv ugrađeni YouTube player (IFrame API) u WebView2. Stranica dolazi s https virtualnog hosta, pa
/// YouTube dobiva ispravan referrer. Kad je prozor minimiziran, reprodukcija se pauzira (player mora biti vidljiv).
/// </summary>
public partial class YouTubeWindow : Window
{
    internal const string Host = "player.jebaited.local";

    private const string PlayerHtml = """
        <!doctype html><html><head><meta charset="utf-8"><title>player</title>
        <style>html,body{margin:0;height:100%;background:#000;overflow:hidden}#p{width:100%;height:100%}</style></head>
        <body><div id="p"></div><script>
        const q = new URLSearchParams(location.search); var player;
        function post(){ try { const d = player.getVideoData ? player.getVideoData() : {};
          chrome.webview.postMessage(JSON.stringify({ s: player.getPlayerState(), t: player.getCurrentTime() || 0, d: player.getDuration() || 0,
            title: d.title || '', id: d.video_id || '' })); } catch (e) {} }
        function onYouTubeIframeAPIReady(){
          const vars = { autoplay: 1, playsinline: 1, rel: 0, origin: location.origin };
          if (q.get('list')) { vars.listType = 'playlist'; vars.list = q.get('list'); }
          const opts = { width: '100%', height: '100%', playerVars: vars, events: {
            onReady: e => { if (q.get('mute') === '1') e.target.mute(); e.target.playVideo(); post(); },
            onStateChange: post,
            onError: e => chrome.webview.postMessage(JSON.stringify({ error: e.data })) } };
          if (q.get('v')) opts.videoId = q.get('v');
          player = new YT.Player('p', opts); setInterval(post, 1000); }
        </script><script src="https://www.youtube.com/iframe_api"></script></body></html>
        """;

    private readonly YouTubeRemote _remote;
    private readonly string _folder;
    private bool _ready;
    private bool _showKey;

    internal YouTubeWindow(YouTubeRemote remote, string dataDir)
    {
        _remote = remote;
        _folder = Path.Combine(dataDir, "cache", "youtube");
        InitializeComponent();
        WindowTheme.UseDarkTitleBar(this);
        _remote.Changed += OnRemoteChanged;
        StateChanged += async (_, _) =>
        {
            if (WindowState == WindowState.Minimized)
            {
                await _remote.PauseAsync();
            }
        };
        Closed += (_, _) =>
        {
            _remote.Changed -= OnRemoteChanged;
            _remote.Command = null;
            _remote.IsActiveSource = false;
            Web.Dispose();
        };
        Loaded += async (_, _) => await InitializeAsync();
        Render();
    }

    /// <summary>Dijagnostika: utišan player.</summary>
    internal bool Mute { get; set; }

    internal IReadOnlyList<YouTubeItem> Items => ItemList.ItemsSource as IReadOnlyList<YouTubeItem> ?? [];

    internal bool IsReady => _ready;

    /// <summary>Zadnja greška ugrađenog playera (IFrame kôd: 2, 5, 100, 101, 150, 153…).</summary>
    internal int? PlayerError { get; private set; }

    private async Task InitializeAsync()
    {
        try
        {
            Directory.CreateDirectory(_folder);
            File.WriteAllText(Path.Combine(_folder, "player.html"), PlayerHtml);
            CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(Path.GetDirectoryName(_folder)!, "webview2"));
            await Web.EnsureCoreWebView2Async(env);
            Web.CoreWebView2.SetVirtualHostNameToFolderMapping(Host, _folder, CoreWebView2HostResourceAccessKind.Allow);
            Web.CoreWebView2.Settings.AreDevToolsEnabled = false;
            Web.CoreWebView2.Settings.IsStatusBarEnabled = false;
            Web.CoreWebView2.WebMessageReceived += OnMessage;
            // Linkovi iz playera (npr. „Gledaj na YouTubeu”) otvaraju se u pregledniku, ne u ovom prozoru.
            Web.CoreWebView2.NewWindowRequested += (_, e) =>
            {
                e.Handled = true;
                if (e.Uri.StartsWith("https://", StringComparison.Ordinal))
                {
                    Process.Start(new ProcessStartInfo(e.Uri) { UseShellExecute = true });
                }
            };
            _remote.Command = script => Web.CoreWebView2.ExecuteScriptAsync(script);
            _ready = true;
        }
        catch (Exception ex) when (ex is WebView2RuntimeNotFoundException or COMException or InvalidOperationException or IOException)
        {
            Log.Warn($"WebView2 nije dostupan: {ex.Message}");
            WebMissingText.Visibility = Visibility.Visible;
        }
    }

    /// <summary>Pusti video ili playlistu (link ili ID).</summary>
    internal bool Play(YouTubeTarget target)
    {
        if (!_ready)
        {
            StatusText.Text = Loc.T("youtube.webviewMissing");
            return false;
        }

        var q = new List<string>();
        if (target.VideoId is { } v)
        {
            q.Add("v=" + v);
        }

        if (target.PlaylistId is { } l)
        {
            q.Add("list=" + Uri.EscapeDataString(l));
        }

        if (Mute)
        {
            q.Add("mute=1");
        }

        PlayerError = null;
        Web.CoreWebView2.Navigate($"https://{Host}/player.html?{string.Join("&", q)}");
        return true;
    }

    private void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(e.TryGetWebMessageAsString());
            JsonElement r = doc.RootElement;
            if (r.TryGetProperty("error", out JsonElement err))
            {
                PlayerError = err.ValueKind == JsonValueKind.Number ? err.GetInt32() : -1;
                _remote.SetStatus(Loc.T("youtube.errPlayer", PlayerError?.ToString(CultureInfo.InvariantCulture) ?? "?"));
                return;
            }

            var state = new YouTubeState(r.GetProperty("s").GetInt32(), r.GetProperty("t").GetDouble(), r.GetProperty("d").GetDouble(),
                r.GetProperty("title").GetString() ?? string.Empty, r.GetProperty("id").GetString() ?? string.Empty);
            // Izvor postaje tek pri prijelazu u „svira” (korisnik je pokrenuo), ne pri svakoj poruci stanja.
            if (state.IsPlaying && _remote.State is not { IsPlaying: true } && !_remote.IsActiveSource)
            {
                _remote.IsActiveSource = true;
            }

            _remote.ReportState(state);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            Log.Warn($"YouTube poruka: {ex.Message}");
        }
    }

    private void OnRemoteChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(Render);

    private void Render()
    {
        KeyPanel.Visibility = _remote.HasApiKey && !_showKey ? Visibility.Collapsed : Visibility.Visible;
        ChangeKeyButton.Visibility = _remote.HasApiKey ? Visibility.Visible : Visibility.Collapsed;
        SearchRow.IsEnabled = _remote.HasApiKey;
        StatusText.Text = _remote.Status;
        Title = _remote.State is { Title.Length: > 0 } s ? $"{Loc.T("youtube.title")} · {s.Title}" : Loc.T("youtube.title");
    }

    private void OnPlayLink(object sender, RoutedEventArgs e) => _ = PlayLinkAsync(LinkBox.Text);

    private void OnLinkKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            _ = PlayLinkAsync(LinkBox.Text);
        }
    }

    /// <summary>Link: video svira odmah; playlista svira odmah, a uz ključ se prikažu i njezine stavke.</summary>
    internal async Task<bool> PlayLinkAsync(string text)
    {
        if (YouTubeLinks.Parse(text) is not { } target)
        {
            StatusText.Text = Loc.T("youtube.badLink");
            return false;
        }

        bool ok = Play(target);
        if (ok && target.PlaylistId is { } list && _remote.HasApiKey)
        {
            ItemList.ItemsSource = await _remote.PlaylistAsync(list);
        }

        return ok;
    }

    private void OnSearch(object sender, RoutedEventArgs e) => _ = SearchAsync(SearchBox.Text);

    private void OnSearchKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            _ = SearchAsync(SearchBox.Text);
        }
    }

    internal async Task<int> SearchAsync(string query)
    {
        if (query.Trim().Length == 0)
        {
            return 0;
        }

        IReadOnlyList<YouTubeItem> found = await _remote.SearchAsync(query);
        ItemList.ItemsSource = found;
        if (found.Count == 0 && _remote.Status.Length == 0)
        {
            StatusText.Text = Loc.T("youtube.nothingFound");
        }

        return found.Count;
    }

    private void OnPlayItem(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: YouTubeItem item })
        {
            Play(new YouTubeTarget(item.VideoId, null));
        }
    }

    private void OnSaveKey(object sender, RoutedEventArgs e)
    {
        string key = KeyBox.Password.Trim();
        if (key.Length == 0 && _remote.HasApiKey)
        {
            // Prazno polje uklanja spremljeni ključ.
            _remote.SetApiKey(null);
            _showKey = false;
            Render();
            return;
        }

        if (key.Length < 30 || key.Any(char.IsWhiteSpace))
        {
            StatusText.Text = Loc.T("youtube.badKey");
            return;
        }

        _remote.SetApiKey(key);
        KeyBox.Password = string.Empty;
        _showKey = false;
        Render();
    }

    /// <summary>Promjena ili uklanjanje spremljenog ključa (npr. kad ga YouTube ne prihvati).</summary>
    private void OnChangeKey(object sender, RoutedEventArgs e)
    {
        _showKey = !_showKey;
        Render();
        if (_showKey)
        {
            KeyBox.Focus();
        }
    }

    private void OnOpenConsole(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(YouTubeRemote.ApiKeyGuide) { UseShellExecute = true });
}

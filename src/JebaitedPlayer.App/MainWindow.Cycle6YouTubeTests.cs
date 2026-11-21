using System.Windows;
using JebaitedPlayer.Online;
using JebaitedPlayer.Views;

namespace JebaitedPlayer;

/// <summary>Ciklus 6, V9 u pravom playeru: --diag-ui-test youtube (stvarni ugrađeni YouTube player, uz JEBAITED_DATA_DIR).</summary>
public partial class MainWindow
{
    private async Task<Dictionary<string, object?>> YouTubeUiTestAsync()
    {
        var r = new Dictionary<string, object?>();
        if (Environment.GetEnvironmentVariable("JEBAITED_DATA_DIR") is not { Length: > 0 })
        {
            r["error"] = "JEBAITED_DATA_DIR is required (never your real data)";
            return r;
        }

        await WaitForAsync(() => _vm.HasMedia && !_vm.IsLoading && _vm.Position > 1, 10000);
        string music = _vm.CurrentPath!;
        _player?.SetPropertyAsync("mute", "yes");
        _diagAutoSwitch = true;
        ShowMiniPlayer();
        await Task.Delay(600);
        MiniPlayerWindow mini = _mini!;

        ShowYouTube(mini);
        YouTubeWindow w = _youTubeWindow!;
        w.Mute = true;
        await WaitForAsync(() => w.IsReady, 15000);
        r["ready"] = w.IsReady;
        r["keyPanel"] = w.KeyPanel.IsVisible;
        r["badLink"] = await w.PlayLinkAsync("https://example.com/x");
        r["badLinkStatus"] = w.StatusText.Text;

        // 1) Stvarni ugrađeni player: javni video YouTube Developersa (utišano).
        r["played"] = await w.PlayLinkAsync("https://www.youtube.com/watch?v=M7lc1UVf-VE");
        await WaitForAsync(() => YouTube.State is { IsPlaying: true, CurrentTime: > 2 } || w.PlayerError is not null, 30000);
        r["embed"] = new Dictionary<string, object?>
        {
            ["state"] = YouTube.State?.PlayerState, ["time"] = YouTube.State?.CurrentTime, ["duration"] = YouTube.State?.Duration,
            ["title"] = YouTube.State?.Title, ["id"] = YouTube.State?.VideoId, ["error"] = w.PlayerError, ["active"] = YouTube.IsActiveSource,
            ["localPaused"] = _vm.IsPaused,
        };
        r["windowScreenshot"] = SaveWindowPng("youtube_window", w);
        mini.Tick();
        r["mini"] = new Dictionary<string, object?> { ["source"] = mini.SourceText.Text, ["marquee"] = mini.MarqueeTextValue };
        r["miniScreenshot"] = SaveWindowPng("mini_youtube", mini);

        // 2) Pauza i nastavak kroz tipke mini playera.
        ClickButton(mini.PauseButton);
        await WaitForAsync(() => YouTube.State is { PlayerState: 2 }, 8000);
        r["pausedByMini"] = YouTube.State?.PlayerState;
        ClickButton(mini.PlayButton);
        await WaitForAsync(() => YouTube.State is { IsPlaying: true }, 8000);
        r["playedByMini"] = YouTube.State?.PlayerState;

        // 3) Pretraga i greške (simulirani Data API, ključ samo za test).
        using var fake = new FakeHttpServer((_, path, _) => path.Contains("q=kvota", StringComparison.Ordinal)
            ? (403, """{ "error": { "code": 403, "errors": [ { "reason": "quotaExceeded" } ] } }""")
            : (200, """{ "items": [ { "id": { "videoId": "M7lc1UVf-VE" }, "snippet": { "title": "Test song", "channelTitle": "Test channel" } } ] }"""));
        YouTube.UseTestServer(fake.Base, "test-key-0123456789012345678901234567");
        Dispatcher.Invoke(() => { });
        r["found"] = await w.SearchAsync("test");
        r["foundTitle"] = w.Items.FirstOrDefault()?.Title;
        await w.SearchAsync("kvota");
        r["quotaStatus"] = YouTube.Status;

        // 3b) Spremljeni ključ se može promijeniti i ukloniti (prazno polje + Spremi ključ).
        r["keyPanelHiddenWithKey"] = !w.KeyPanel.IsVisible;
        ClickButton(w.ChangeKeyButton);
        await Task.Delay(200);
        r["keyPanelShown"] = w.KeyPanel.IsVisible;
        w.KeyBox.Password = string.Empty;
        ClickButton(w.SaveKeyButton);
        await Task.Delay(200);
        r["keyRemoved"] = !YouTube.HasApiKey;
        r["keyRemovedStatus"] = w.StatusText.Text;
        r["keyPanelAfterRemove"] = w.KeyPanel.IsVisible;

        // 4) Lokalna datoteka vraća mini player na lokalno, a YouTube prestaje biti izvor.
        string other = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(music)!, "audio_only.flac");
        OpenFiles([other]);
        await WaitForAsync(() => _vm.HasMedia && !_vm.IsLoading && _vm.CurrentPath == other && _vm.Position > 0.5, 10000);
        mini.Tick();
        r["local"] = new Dictionary<string, object?> { ["active"] = YouTube.IsActiveSource, ["source"] = mini.SourceText.Text };
        w.Close();
        await Task.Delay(500);
        r["closed"] = YouTube.Command is null;
        return r;
    }
}

using System.IO;
using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.Views;

namespace JebaitedPlayer;

/// <summary>
/// Dijagnostički testovi ciklusa 3 u pravom playeru: --diag-ui-test settings3 (M2a/M2b, samo uz JEBAITED_DATA_DIR)
/// i --diag-ui-test radio (V3, stvarna mreža: Radio Browser i stvarni stream).
/// </summary>
public partial class MainWindow
{
    /// <summary>Klik na gumb ▶ u retku popisa postaja (kroz stvarni predložak, kao mišem).</summary>
    private static bool ClickRadioPlay(RadioWindow radio, int index)
    {
        radio.StationList.UpdateLayout();
        if (radio.StationList.ItemContainerGenerator.ContainerFromIndex(index) is System.Windows.Controls.ContentPresenter cp &&
            cp.ContentTemplate.FindName("PlayButton", cp) is System.Windows.Controls.Button button)
        {
            button.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            return true;
        }

        return false;
    }

    private async Task<Dictionary<string, object?>> Settings3UiTestAsync()
    {
        var r = new Dictionary<string, object?>();
        if (Environment.GetEnvironmentVariable("JEBAITED_DATA_DIR") is not { Length: > 0 })
        {
            r["error"] = "JEBAITED_DATA_DIR is required (never your real data)";
            return r;
        }

        await WaitForAsync(() => _vm.Duration > 0, 8000);
        string file = Path.Combine(AppPaths.DataDir, "settings.json");
        ShowPlayerSettings(SettingsTab.General);
        await Task.Delay(700);
        PlayerSettingsWindow w = _playerSettingsWindow!;
        r["start"] = new Dictionary<string, object?> { ["dirty"] = w.IsDirty, ["status"] = w.SaveStatusText.Text, ["saveEnabled"] = w.SaveButton.IsEnabled };

        // 1) Promjena → nespremljeno; Odbaci → natrag.
        string originalResume = _settings.ResumeMode;
        _settings.ResumeMode = "start";
        await Task.Delay(700);
        r["changed"] = new Dictionary<string, object?> { ["dirty"] = w.IsDirty, ["status"] = w.SaveStatusText.Text, ["saveEnabled"] = w.SaveButton.IsEnabled };
        w.DiscardChanges();
        await Task.Delay(1200);
        PlayerSettingsWindow w2 = _playerSettingsWindow!;
        r["discarded"] = new Dictionary<string, object?>
        {
            ["resume"] = _settings.ResumeMode, ["original"] = originalResume, ["dirty"] = w2.IsDirty, ["newWindow"] = !ReferenceEquals(w, w2),
        };

        // 2) Spremi → datoteka na disku.
        _settings.ResumeMode = "resume";
        await Task.Delay(500);
        w2.SaveNow();
        r["saved"] = new Dictionary<string, object?>
        {
            ["dirty"] = w2.IsDirty, ["status"] = w2.SaveStatusText.Text, ["fileHasResume"] = File.ReadAllText(file).Contains("\"ResumeMode\": \"resume\"", StringComparison.Ordinal),
        };

        // 3) Zadano: kartica Općenito, pa sve; jezik, glasnoća i ključ ostaju.
        w2.ResetDefaults("general", confirm: false);
        await Task.Delay(1200);
        PlayerSettingsWindow w3 = _playerSettingsWindow!;
        r["defaultsTab"] = new Dictionary<string, object?> { ["resume"] = _settings.ResumeMode, ["theme"] = _settings.Theme, ["dirty"] = w3.IsDirty };
        w3.ResetDefaults(null, confirm: false);
        await Task.Delay(1200);
        PlayerSettingsWindow w4 = _playerSettingsWindow!;
        r["defaultsAll"] = new Dictionary<string, object?>
        {
            ["theme"] = _settings.Theme, ["appliedTheme"] = WindowTheme.Current, ["language"] = _settings.Language, ["volume"] = _vm.Volume,
            ["key"] = _settings.Online.OpenSubtitlesApiKey, ["seek"] = _settings.SeekStepSeconds, ["dirty"] = w4.IsDirty,
        };
        r["defaultsScreenshot"] = SaveWindowPng("settings_footer", w4);

        // 4) Odbaci nakon zadanog: natrag na spremljeno.
        w4.DiscardChanges();
        await Task.Delay(1200);
        r["afterDiscard"] = new Dictionary<string, object?> { ["resume"] = _settings.ResumeMode, ["theme"] = _settings.Theme, ["dirty"] = _playerSettingsWindow?.IsDirty };
        _playerSettingsWindow?.Close();
        return r;
    }

    /// <summary>
    /// 1.5.2: player pokrenut bez datoteke, odmah u mini playeru (kao kod vlasnika), pa radio kao prvo što se pusti.
    /// Ranije mpv u tom slučaju nije postojao i ništa nije sviralo.
    /// </summary>
    private async Task<Dictionary<string, object?>> RadioColdUiTestAsync()
    {
        var r = new Dictionary<string, object?> { ["playerBefore"] = _player is not null, ["hasMediaBefore"] = _vm.HasMedia };
        _diagAutoSwitch = true;
        ShowMiniPlayer();
        await Task.Delay(800);
        r["playerAfterMini"] = _player is not null;
        _player?.SetPropertyAsync("mute", "yes");
        ShowRadio(_mini);
        await Task.Delay(600);
        RadioWindow radio = _radio!;
        r["count"] = await radio.SearchAsync(null, "jazz", null);
        RadioRow? playing = null;
        int index = 0;
        foreach (RadioRow row in radio.Results.Take(5).ToList())
        {
            r["clicked"] = ClickRadioPlay(radio, index++);
            await WaitForAsync(() => _vm.HasMedia && !_vm.IsLoading && _vm.Position > 2 && _vm.CurrentPath == row.Url, 15000);
            if (_vm.HasMedia && _vm.Position > 2 && _vm.CurrentPath == row.Url)
            {
                playing = row;
                break;
            }
        }

        await Task.Delay(1200);
        r["station"] = playing?.Name;
        r["position"] = Math.Round(_vm.Position, 1);
        r["status"] = radio.Status;
        r["miniVisible"] = _mini?.IsVisible;
        r["mainVisible"] = IsVisible;
        r["welcomeVisible"] = WelcomePanel.Visibility == System.Windows.Visibility.Visible;
        radio.Close();
        return r;
    }

    private async Task<Dictionary<string, object?>> RadioUiTestAsync()
    {
        var r = new Dictionary<string, object?>();
        await WaitForAsync(() => _vm.Duration > 0, 8000);
        _player?.SetPropertyAsync("mute", "yes");
        _diagAutoSwitch = true;
        ShowRadio();
        await Task.Delay(600);
        RadioWindow radio = _radio!;

        // 1) Stvarna pretraga.
        int count = await radio.SearchAsync(null, "jazz", null);
        r["search"] = new Dictionary<string, object?> { ["count"] = count, ["first"] = radio.Results.Take(3).Select(x => x.Name + " | " + x.Detail).ToArray() };
        r["radioScreenshot"] = SaveWindowPng("radio_window", radio);

        // 2) Pokreni postaju (do 4 pokušaja, neke postaje ne rade): mini player, uživo, glazbeni EQ, ne ide u povijest.
        RadioRow? playing = null;
        int index = 0;
        foreach (RadioRow row in radio.Results.Take(4).ToList())
        {
            // 1.5.1: klik na gumb ▶ u retku (kao korisnik), ne izravni poziv.
            r["clickedPlayButton"] = ClickRadioPlay(radio, index++);
            await WaitForAsync(() => _vm.HasMedia && !_vm.IsLoading && _vm.Position > 2 && _vm.CurrentPath == row.Url, 15000);
            if (_vm.HasMedia && _vm.Position > 2 && _vm.CurrentPath == row.Url)
            {
                playing = row;
                break;
            }
        }

        await Task.Delay(1500);
        r["feedback"] = new Dictionary<string, object?>
        {
            ["status"] = radio.Status, ["rowPlaying"] = playing?.IsPlaying, ["radioVisible"] = radio.IsVisible,
            ["playGlyph"] = playing?.PlayGlyph == "",
        };
        MiniPlayerWindow? mini = _mini;
        r["stream"] = new Dictionary<string, object?>
        {
            ["station"] = playing?.Name,
            ["position"] = Math.Round(_vm.Position, 1),
            ["paused"] = _vm.IsPaused,
            ["musicMode"] = MpvConfig.MusicMode,
            ["miniVisible"] = mini?.IsVisible,
            ["mainVisible"] = IsVisible,
            ["source"] = mini?.SourceText.Text,
            ["live"] = mini?.RemainingText.Text,
            ["title"] = mini?.MarqueeTextValue,
            ["icy"] = await Task.Run(() => _player?.GetPropertyString("metadata/by-key/icy-title")),
            ["libraryCurrent"] = _vm.Library.Current?.Label,
        };
        if (mini is not null)
        {
            r["miniScreenshot"] = SaveWindowPng("mini_radio", mini);
        }

        // 3) Favorit, kriv URL, ručni URL.
        if (playing is not null)
        {
            radio.ToggleFavorite(playing);
            r["favorite"] = new Dictionary<string, object?>
            {
                ["rows"] = radio.FavoriteRows.Count, ["inDb"] = _store?.IsRadioFavorite(playing.Url), ["star"] = playing.IsFavorite,
            };
            r["badUrl"] = radio.PlayUrl("nije adresa");
            bool ok = radio.PlayUrl(playing.Url);
            await WaitForAsync(() => _vm.HasMedia && !_vm.IsLoading && _vm.Position > 2, 15000);
            r["manualUrl"] = new Dictionary<string, object?> { ["accepted"] = ok, ["position"] = Math.Round(_vm.Position, 1), ["path"] = _vm.CurrentPath == playing.Url };
            radio.ToggleFavorite(playing);
            r["favoriteRemoved"] = _store?.IsRadioFavorite(playing.Url);
        }

        r["radioScreenshotPlaying"] = SaveWindowPng("radio_playing", radio);
        r["historyHasUrls"] = (_store?.Recent(100) ?? []).Any(e => e.Path.StartsWith("http", StringComparison.OrdinalIgnoreCase));
        radio.Close();
        return r;
    }
}

using System.Windows.Input;
using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.Views;

namespace JebaitedPlayer;

/// <summary>
/// 2.0 u pravom playeru: --diag-ui-test shortcuts (uz JEBAITED_DATA_DIR). Kartica Prečaci: snimanje nove tipke i stvarna
/// radnja, sukob s potvrdom zamjene, rezervirana tipka, Esc, Delete, F1 sa stvarnim tipkama, Spremi u datoteku,
/// Zadano za radnju i za karticu.
/// </summary>
public partial class MainWindow
{
    private async Task<Dictionary<string, object?>> ShortcutsUiTestAsync()
    {
        var r = new Dictionary<string, object?>();
        if (Environment.GetEnvironmentVariable("JEBAITED_DATA_DIR") is not { Length: > 0 })
        {
            r["error"] = "JEBAITED_DATA_DIR is required (never your real data)";
            return r;
        }

        await WaitForAsync(() => _vm.HasMedia && !_vm.IsLoading && _vm.Position > 0.5, 10000);
        _player?.SetPropertyAsync("mute", "yes");
        ShowPlayerSettings(SettingsTab.Shortcuts);
        await WaitForAsync(() => _playerSettingsWindow is { IsLoaded: true }, 5000);
        PlayerSettingsWindow w = _playerSettingsWindow!;
        await Task.Delay(300);
        r["tab"] = w.SelectedTab.ToString();
        r["rows"] = Shortcuts.Actions.Count(a => w.ShortcutButton(a.Id, 0) is not null);
        r["firstButton"] = w.ShortcutButton("playPause", 0)?.Content as string;
        r["tabScreenshot"] = SaveWindowPng("settings_shortcuts", w);

        // 1) Druga tipka za „Glasnije”: U; stvarni pritisak mijenja glasnoću, a ↑ i dalje radi.
        _vm.Volume = 50;
        System.Windows.Controls.Button slot = w.ShortcutButton("volumeUp", 1)!;
        w.StartShortcutCapture(slot, "volumeUp", 1);
        r["capturing"] = slot.Content as string;
        r["set"] = w.ProcessShortcutKey(Key.U, ModifierKeys.None);
        r["uWorks"] = HandleShortcut(Key.U, ModifierKeys.None) && _vm.Volume > 50;
        double afterU = _vm.Volume;
        r["upStillWorks"] = HandleShortcut(Key.Up, ModifierKeys.None) && _vm.Volume > afterU;

        // 2) Sukob: V ima „Prikaži / sakrij titlove”. Prvi pritisak samo upozori, drugi premjesti.
        w.StartShortcutCapture(w.ShortcutButton("volumeUp", 0)!, "volumeUp", 0);
        r["conflict"] = w.ProcessShortcutKey(Key.V, ModifierKeys.None);
        r["conflictKept"] = Shortcuts.Resolve(_settings, Key.V, ModifierKeys.None);
        r["moved"] = w.ProcessShortcutKey(Key.V, ModifierKeys.None);
        r["vNow"] = Shortcuts.Resolve(_settings, Key.V, ModifierKeys.None);
        r["upNow"] = Shortcuts.Resolve(_settings, Key.Up, ModifierKeys.None);
        r["toggleSubtitlesKeys"] = Shortcuts.Display(_settings, "toggleSubtitles");

        // 3) Rezervirana tipka (Tab) se odbija; Esc odustaje bez promjene.
        w.StartShortcutCapture(w.ShortcutButton("mute", 1)!, "mute", 1);
        r["reserved"] = w.ProcessShortcutKey(Key.Tab, ModifierKeys.None);
        r["escape"] = w.ProcessShortcutKey(Key.Escape, ModifierKeys.None);
        r["muteAfterEscape"] = Shortcuts.Display(_settings, "mute");

        // 4) Delete briše tipku: M više ne utišava.
        w.StartShortcutCapture(w.ShortcutButton("mute", 0)!, "mute", 0);
        r["cleared"] = w.ProcessShortcutKey(Key.Delete, ModifierKeys.None);
        r["mAfterClear"] = HandleShortcut(Key.M, ModifierKeys.None);

        // 5) F1 i izbornik pokazuju stvarne tipke.
        KeyboardShortcuts.Settings = _settings;
        r["f1Volume"] = KeyboardShortcuts.All.FirstOrDefault(x => x.Description == Loc.T("shortcut.volume"))?.Keys;
        r["f1HasMute"] = KeyboardShortcuts.All.Any(x => x.Description == Loc.T("shortcut.mute"));
        r["menuGesture"] = Shortcuts.Gesture(_settings, "volumeUp");

        // 6) Spremi: promjene su u settings.json testne mape.
        w.SaveNow();
        string json = System.IO.File.ReadAllText(System.IO.Path.Combine(AppPaths.DataDir, "settings.json"));
        r["savedVolumeUp"] = json.Contains("\"volumeUp\"", StringComparison.Ordinal);
        r["savedMute"] = json.Contains("\"mute\"", StringComparison.Ordinal);
        r["savedOnlyChanges"] = !json.Contains("\"playPause\"", StringComparison.Ordinal);
        await Task.Delay(200);
        r["changedScreenshot"] = SaveWindowPng("settings_shortcuts_changed", w);

        // 7) Zadano za jednu radnju (poveznica u retku), pa Zadano za cijelu karticu.
        w.ResetShortcutForTests("mute");
        r["muteReset"] = Shortcuts.Display(_settings, "mute");
        r["mWorksAgain"] = Shortcuts.Resolve(_settings, Key.M, ModifierKeys.None);
        w.ResetDefaults("shortcuts", confirm: false);
        await Task.Delay(600);
        r["afterDefaults"] = _settings.Shortcuts.Count;
        r["vAfter"] = Shortcuts.Resolve(_settings, Key.V, ModifierKeys.None);
        r["upAfter"] = Shortcuts.Resolve(_settings, Key.Up, ModifierKeys.None);
        r["uAfter"] = Shortcuts.Resolve(_settings, Key.U, ModifierKeys.None);
        _playerSettingsWindow?.Close();
        return r;
    }
}

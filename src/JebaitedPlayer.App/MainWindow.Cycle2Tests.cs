using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.Views;

namespace JebaitedPlayer;

/// <summary>
/// Dijagnostički test ciklusa 2 u pravom playeru (--diag-ui-test backup), samo uz testnu mapu podataka
/// (JEBAITED_DATA_DIR): dnevna kopija pri pokretanju, izvoz, uvoz koji se odmah primijeni, odbijena kriva datoteka.
/// </summary>
public partial class MainWindow
{
    private async Task<Dictionary<string, object?>> BackupUiTestAsync()
    {
        var r = new Dictionary<string, object?>();
        if (Environment.GetEnvironmentVariable("JEBAITED_DATA_DIR") is not { Length: > 0 })
        {
            r["error"] = "JEBAITED_DATA_DIR is required (never your real data)";
            return r;
        }

        await WaitForAsync(() => _vm.Duration > 0, 8000);
        _player?.SetPropertyAsync("mute", "yes");
        string dir = Path.GetDirectoryName(_options.DiagOut!)!;
        ISettingsHost host = this;

        // 1) Dnevna kopija pri pokretanju (u pozadini).
        await WaitForAsync(() => SettingsBackup.List(AppPaths.DataDir).Count > 0, 6000);
        IReadOnlyList<BackupInfo> start = SettingsBackup.List(AppPaths.DataDir);
        r["startBackups"] = start.Select(b => new Dictionary<string, object?>
        {
            ["reason"] = b.Reason,
            ["files"] = Directory.GetFiles(b.Folder).Select(Path.GetFileName).Order().ToArray(),
        }).ToArray();

        // 2) Kartica Općenito: stanje kopija.
        ShowPlayerSettings(SettingsTab.General);
        await Task.Delay(700);
        PlayerSettingsWindow w = _playerSettingsWindow!;
        r["statusText"] = w.BackupStatusText.Text;
        w.BackupStatusText.BringIntoView();
        await Task.Delay(300);
        r["generalScreenshot"] = SaveWindowPng("backup_general", w);

        // 3) Izvoz.
        string exportFile = Path.Combine(dir, "ciklus2-izvoz.json");
        r["exported"] = w.ExportTo(exportFile);
        string exported = File.ReadAllText(exportFile);
        r["exportHasEqualizer"] = exported.Contains("\"Equalizer\"", StringComparison.Ordinal);
        r["exportHasSecretsOrWindow"] = exported.Contains("ApiKey", StringComparison.Ordinal) || exported.Contains("Password", StringComparison.Ordinal) ||
                                        exported.Contains("WindowLeft", StringComparison.Ordinal);

        // 4) Uvoz izmijenjene datoteke: svijetla tema, glasnoća 20, Rock EQ, korak 15 s. Mora vrijediti odmah.
        JsonObject root = JsonNode.Parse(exported)!.AsObject();
        JsonObject s = root["settings"]!.AsObject();
        s["Theme"] = "light";
        s["Volume"] = 20;
        s["SeekStepSeconds"] = 15;
        s["Equalizer"] = new JsonObject { ["Enabled"] = true, ["Preset"] = "rock", ["Preamp"] = 0, ["CustomGains"] = new JsonArray(0, 0, 0, 0, 0, 0, 0, 0, 0, 0) };
        string importFile = Path.Combine(dir, "ciklus2-uvoz.json");
        File.WriteAllText(importFile, root.ToJsonString());
        int before = SettingsBackup.List(AppPaths.DataDir).Count;
        r["imported"] = w.ImportFrom(importFile, confirm: false);
        await Task.Delay(1200);
        r["afterImport"] = new Dictionary<string, object?>
        {
            ["theme"] = WindowTheme.Current,
            ["volume"] = _vm.Volume,
            ["seekStep"] = _settings.SeekStepSeconds,
            ["eqPreset"] = _settings.Equalizer.Preset,
            ["af"] = await AfAsync(),
            ["windowReopened"] = _playerSettingsWindow is { IsLoaded: true } nw && !ReferenceEquals(nw, w),
            ["backupsBefore"] = before,
            ["backupsAfter"] = SettingsBackup.List(AppPaths.DataDir).Count,
            ["newestReason"] = SettingsBackup.List(AppPaths.DataDir).FirstOrDefault()?.Reason,
            ["paused"] = _vm.IsPaused,
            ["apiKey"] = _settings.Online.OpenSubtitlesApiKey,
            ["windowLeft"] = _settings.WindowLeft,
        };

        // 5) Kriva datoteka: odbijena, ništa se ne mijenja.
        string badFile = Path.Combine(dir, "ciklus2-kriva.json");
        File.WriteAllText(badFile, """{ "nesto": 1, "drugo": 2 }""");
        PlayerSettingsWindow w2 = _playerSettingsWindow!;
        r["badImported"] = w2.ImportFrom(badFile, confirm: false);
        r["afterBad"] = new Dictionary<string, object?> { ["theme"] = WindowTheme.Current, ["volume"] = _vm.Volume, ["eqPreset"] = _settings.Equalizer.Preset };
        w2.Close();
        WindowTheme.Apply("dark");
        return r;
    }
}

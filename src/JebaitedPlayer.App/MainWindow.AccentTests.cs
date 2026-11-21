using System.Windows;
using System.Windows.Media;
using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.Views;

namespace JebaitedPlayer;

/// <summary>
/// 2.0 u pravom playeru: --diag-ui-test accent (uz JEBAITED_DATA_DIR). Kružići, gotova boja u glavnom prozoru, mini LCD-u i
/// VU metru, svijetla tema (boja se prilagodi), birač s pregledom uživo i odustajanjem, vlastita boja, Spremi, Zadano.
/// </summary>
public partial class MainWindow
{
    private static string Hex(object? brush) => brush is SolidColorBrush b ? AccentColors.ToHex(b.Color) : string.Empty;

    private async Task<Dictionary<string, object?>> AccentUiTestAsync()
    {
        var r = new Dictionary<string, object?>();
        if (Environment.GetEnvironmentVariable("JEBAITED_DATA_DIR") is not { Length: > 0 })
        {
            r["error"] = "JEBAITED_DATA_DIR is required (never your real data)";
            return r;
        }

        await WaitForAsync(() => _vm.HasMedia && !_vm.IsLoading && _vm.Position > 0.5, 10000);
        _player?.SetPropertyAsync("mute", "yes");
        ShowMiniPlayer();
        await Task.Delay(600);
        MiniPlayerWindow mini = _mini!;
        ShowPlayerSettings(SettingsTab.Appearance);
        await WaitForAsync(() => _playerSettingsWindow is { IsLoaded: true }, 5000);
        PlayerSettingsWindow w = _playerSettingsWindow!;
        await Task.Delay(300);

        Dictionary<string, object?> State() => new()
        {
            ["accent"] = Hex(Application.Current.TryFindResource("AccentBrush")),
            ["foreground"] = Hex(Application.Current.TryFindResource("AccentForegroundBrush")),
            ["selection"] = Hex(Application.Current.TryFindResource("SelectionBrush")),
            ["lcd"] = AccentColors.ToHex(mini.LcdTextColor),
            ["vu"] = AccentColors.ToHex(VuMeterView.BarColor),
            ["saved"] = _settings.AccentColor,
            ["swatches"] = string.Join(",", w.AccentSwatches.Select(s => s.Checked ? s.Id + "*" : s.Id)),
        };

        // 1) Početno: zadana zelena, 8 kružića, zelena odabrana.
        r["start"] = State();

        // 2) Gotova boja (plava): glavni prozor, mini LCD i VU metar odmah.
        w.SelectAccentPresetForTests("blue");
        await Task.Delay(300);
        r["blue"] = State();
        r["blueExpected"] = AccentColors.ToHex(AccentColors.For("#6CB4FF", dark: true).Accent);
        r["settingsBlue"] = SaveWindowPng("settings_accent_blue", w);
        r["miniBlue"] = SaveWindowPng("mini_accent_blue", mini);

        // 3) Svijetla tema: boja se potamni (kontrast s pozadinom najmanje 3:1); natrag na tamnu.
        WindowTheme.Apply("light");
        await Task.Delay(300);
        r["light"] = State();
        AccentColors.TryParse(Hex(Application.Current.TryFindResource("AccentBrush")), out Color lightAccent);
        r["lightContrast"] = Math.Round(AccentColors.Contrast(lightAccent, Color.FromRgb(0xF3, 0xF3, 0xF3)), 2);
        r["settingsLight"] = SaveWindowPng("settings_accent_light", w);
        WindowTheme.Apply("dark");
        await Task.Delay(200);
        r["backDark"] = State();

        // 4) Birač vlastite boje: pregled uživo u cijelom playeru, Odustani vraća prijašnju (plavu).
        var picker = new ColorPickerWindow(_settings.AccentColor) { Owner = w };
        picker.Previewing += WindowTheme.ApplyAccent;
        picker.Show();
        picker.SetHexForTests("#FF00AA");
        await Task.Delay(200);
        r["pickerHex"] = picker.Hex;
        r["pickerPreview"] = AccentColors.ToHex(picker.PreviewColor);
        r["livePreview"] = State();
        picker.SetHueForTests(30);
        await Task.Delay(100);
        r["pickerAfterHue"] = picker.Hex;
        r["pickerScreenshot"] = SaveWindowPng("color_picker", picker);
        picker.Previewing -= WindowTheme.ApplyAccent;
        picker.Close();
        WindowTheme.ApplyAccent(_settings.AccentColor); // kao Odustani
        await Task.Delay(200);
        r["afterCancel"] = State();

        // 5) Vlastita boja („Koristi boju”): svoj kružić, spremljena u postavke.
        w.SetCustomAccentForTests("#FF00AA");
        await Task.Delay(300);
        r["custom"] = State();
        w.SaveNow();
        string json = System.IO.File.ReadAllText(System.IO.Path.Combine(AppPaths.DataDir, "settings.json"));
        r["savedInFile"] = json.Contains("\"AccentColor\": \"#FF00AA\"", StringComparison.Ordinal) || json.Contains("\"AccentColor\":\"#FF00AA\"", StringComparison.Ordinal);
        r["miniCustom"] = SaveWindowPng("mini_accent_custom", mini);

        // 6) Zadano za karticu Izgled vraća zelenu (resursi teme, bez vlastitih).
        w.ResetDefaults("appearance", confirm: false);
        await WaitForAsync(() => _playerSettingsWindow is { IsLoaded: true } fresh && fresh != w, 5000);
        w = _playerSettingsWindow!; // Zadano ponovno otvara prozor postavki
        await Task.Delay(300);
        r["afterDefaults"] = State();
        r["overrideRemoved"] = WindowTheme.Accent.Length == 0;
        _playerSettingsWindow?.Close();
        return r;
    }
}

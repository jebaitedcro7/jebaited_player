using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using JebaitedPlayer.Infrastructure;

namespace JebaitedPlayer.Views;

/// <summary>
/// 2.0: Postavke → Izgled → Boja isticanja. Kružić = gotova boja (zelena je zadana), „Vlastita boja…” otvara birač.
/// Promjena se vidi odmah u svim prozorima; Spremi / Odbaci / Zadano kao i ostale postavke.
/// </summary>
public partial class PlayerSettingsWindow
{
    private bool _loadingAccent;

    private void LoadAccent()
    {
        _loadingAccent = true;
        AccentPanel.Children.Clear();
        string current = AccentColors.Normalize(_settings.AccentColor);
        bool preset = false;
        foreach ((string id, string hex) in AccentColors.Presets)
        {
            string value = AccentColors.Normalize(hex);
            RadioButton swatch = AccentSwatch(hex, AccentColors.PresetName(id), id);
            swatch.IsChecked = value == current;
            preset |= value == current;
            swatch.Checked += (_, _) => SetAccent(value);
            AccentPanel.Children.Add(swatch);
        }

        if (!preset)
        {
            // Vlastita boja: svoj kružić, odabran.
            RadioButton custom = AccentSwatch(current, Loc.T("settings.accentCustomName"), "custom");
            custom.IsChecked = true;
            AccentPanel.Children.Add(custom);
        }

        AccentHexText.Text = current.Length == 0 ? AccentColors.DefaultHex : current;
        _loadingAccent = false;
    }

    private RadioButton AccentSwatch(string hex, string name, string id)
    {
        AccentColors.TryParse(hex, out Color color);
        return new RadioButton
        {
            Style = (Style)FindResource("ColorSwatch"),
            Tag = new SolidColorBrush(color),
            GroupName = "Accent",
            ToolTip = name,
            Name = "Accent_" + id,
        };
    }

    private void SetAccent(string hex)
    {
        if (_loadingAccent)
        {
            return;
        }

        _settings.AccentColor = AccentColors.Normalize(hex);
        WindowTheme.ApplyAccent(_settings.AccentColor);
        AccentHexText.Text = _settings.AccentColor.Length == 0 ? AccentColors.DefaultHex : _settings.AccentColor;
        Dispatcher.BeginInvoke(LoadAccent); // kružići se slažu ponovno izvan događaja kliknutog kružića
        UpdateSaveState();
    }

    private void OnAccentCustom(object sender, RoutedEventArgs e)
    {
        string start = _settings.AccentColor.Length == 0 ? AccentColors.DefaultHex : _settings.AccentColor;
        var picker = new ColorPickerWindow(start) { Owner = this };
        picker.Previewing += WindowTheme.ApplyAccent; // vidi se odmah u cijelom playeru
        bool accepted = picker.ShowDialog() == true;
        picker.Previewing -= WindowTheme.ApplyAccent;
        if (accepted)
        {
            SetAccent(picker.Hex);
        }
        else
        {
            WindowTheme.ApplyAccent(_settings.AccentColor); // odustajanje vraća prijašnju boju
        }
    }

    /// <summary>Dijagnostika: odabir gotove boje kao klik na kružić.</summary>
    internal void SelectAccentPresetForTests(string id) =>
        AccentPanel.Children.OfType<RadioButton>().First(r => r.Name == "Accent_" + id).IsChecked = true;

    /// <summary>Dijagnostika: vlastita boja kao „Koristi boju” u biraču.</summary>
    internal void SetCustomAccentForTests(string hex) => SetAccent(hex);

    /// <summary>Dijagnostika: kružići (ID → odabran).</summary>
    internal IReadOnlyList<(string Id, bool Checked)> AccentSwatches => AccentPanel.Children.OfType<RadioButton>()
        .Select(r => (r.Name["Accent_".Length..], r.IsChecked == true)).ToList();
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using JebaitedPlayer.Infrastructure;

namespace JebaitedPlayer.Views;

/// <summary>
/// Equalizer: uključivanje, preset, Vrati i 11 okomitih klizača (preamp + 10 pojaseva). Pomicanje klizača od
/// ugrađenog preseta pravi vlastiti; mpv dobiva novi filtar 150 ms nakon što povlačenje stane (bez pucketanja).
/// </summary>
public partial class EqualizerPanel : UserControl
{
    private readonly List<(Slider Slider, TextBlock Value)> _bands = [];
    private (Slider Slider, TextBlock Value) _preamp;
    private EqualizerSettings _eq = new();
    private Action _apply = () => { };
    private DispatcherTimer? _applyTimer;
    private bool _updating = true;

    public EqualizerPanel() => InitializeComponent();

    /// <param name="apply">Primijeni postavke zvuka na mpv (ISettingsHost.ApplyPlaybackSettings).</param>
    /// <param name="sliderHeight">Visina klizača (postavke 132, mini player manje).</param>
    internal void Initialize(EqualizerSettings eq, Action apply, double sliderHeight = 132, double columnWidth = 40)
    {
        _eq = eq;
        _apply = apply;
        _preamp = AddColumn(Loc.T("settings.eqPreamp"), eq.Preamp, isPreamp: true, sliderHeight, Math.Max(columnWidth + 6, 58));
        Bands.Children.Add(new Border { Width = 1, Margin = new Thickness(6, 0, 10, 22), Background = (Brush)FindResource("MenuBorderBrush") });
        double[] gains = eq.Gains;
        for (int i = 0; i < Equalizer.BandCount; i++)
        {
            _bands.Add(AddColumn(Equalizer.FrequencyLabel(Equalizer.Frequencies[i]), gains[i], isPreamp: false, sliderHeight, columnWidth, i));
        }

        EnabledBox.IsChecked = eq.Enabled;
        EnabledBox.Checked += (_, _) => SetEnabled(true);
        EnabledBox.Unchecked += (_, _) => SetEnabled(false);
        _applyTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(150), DispatcherPriority.Normal, (_, _) =>
        {
            _applyTimer!.Stop();
            _apply();
        }, Dispatcher);
        _applyTimer.Stop();
        _updating = false;
        Refresh();
    }

    /// <summary>Zatvaranje prozora dok čeka primjena: primijeni odmah.</summary>
    internal void Flush()
    {
        if (_applyTimer is { IsEnabled: true })
        {
            _applyTimer.Stop();
            _apply();
        }
    }

    /// <summary>Promjena izvana (drugi prozor, uvoz postavki) ili jezika: prikaži trenutne vrijednosti.</summary>
    internal void Refresh()
    {
        _updating = true;
        double[] gains = _eq.Gains;
        for (int i = 0; i < _bands.Count; i++)
        {
            _bands[i].Slider.Value = gains[i];
            _bands[i].Value.Text = Db(gains[i]);
        }

        if (_preamp.Slider is { } preamp)
        {
            preamp.Value = _eq.Preamp;
            _preamp.Value.Text = Db(_eq.Preamp);
        }

        EnabledBox.IsChecked = _eq.Enabled;
        _updating = false;
        Panel.IsEnabled = _eq.Enabled;
        Panel.Opacity = _eq.Enabled ? 1 : 0.45;
        PresetButton.Content = Loc.T("eq." + _eq.Preset);
    }

    private (Slider Slider, TextBlock Value) AddColumn(string label, double value, bool isPreamp, double height, double width, int band = -1)
    {
        var valueText = new TextBlock { FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 4), Text = Db(value) };
        valueText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        var slider = new Slider { Style = (Style)FindResource("EqSlider"), Height = height, Value = value };
        slider.ValueChanged += (_, e) =>
        {
            valueText.Text = Db(e.NewValue);
            if (_updating)
            {
                return;
            }

            if (isPreamp)
            {
                _eq.Preamp = Equalizer.Clamp(e.NewValue);
            }
            else
            {
                SetBand(band, e.NewValue);
            }

            ScheduleApply();
        };
        var column = new StackPanel { Width = width };
        column.Children.Add(valueText);
        column.Children.Add(slider);
        column.Children.Add(new TextBlock { Text = label, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) });
        Bands.Children.Add(column);
        return (slider, valueText);
    }

    private void SetBand(int band, double value)
    {
        if (_eq.Preset != Equalizer.Custom)
        {
            _eq.CustomGains = _eq.Gains;
            _eq.Preset = Equalizer.Custom;
            PresetButton.Content = Loc.T("eq.custom");
        }

        _eq.CustomGains[band] = Equalizer.Clamp(value);
    }

    private void SetEnabled(bool enabled)
    {
        if (_updating)
        {
            return;
        }

        _eq.Enabled = enabled;
        Refresh();
        _apply();
    }

    private void ScheduleApply()
    {
        if (_eq.Enabled && _applyTimer is not null)
        {
            _applyTimer.Stop();
            _applyTimer.Start();
        }
    }

    private void OnPresetClick(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = PresetButton, Placement = PlacementMode.Bottom };
        foreach (string id in Equalizer.Presets.Select(p => p.Id).Append(Equalizer.Custom))
        {
            var item = new MenuItem { Header = Loc.T("eq." + id), IsChecked = id == _eq.Preset };
            item.Click += (_, _) => ChoosePreset(id);
            menu.Items.Add(item);
        }

        menu.IsOpen = true;
    }

    internal void ChoosePreset(string id)
    {
        _eq.Preset = id;
        Refresh();
        if (_eq.Enabled)
        {
            _apply();
        }
    }

    /// <summary>Vrati na Flat i 0 dB pojačanja (vlastiti preset ostaje zapamćen pod „Vlastiti”).</summary>
    private void OnReset(object sender, RoutedEventArgs e)
    {
        _eq.Preamp = 0;
        ChoosePreset("flat");
    }

    private static string Db(double value) => Math.Abs(value) < 0.05 ? "0" : value.ToString("+0.#;−0.#", Loc.Culture);
}

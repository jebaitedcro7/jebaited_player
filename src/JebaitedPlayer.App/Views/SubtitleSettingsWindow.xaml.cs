using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.Subtitles;
using JebaitedPlayer.ViewModels;

namespace JebaitedPlayer.Views;

/// <summary>Postavke titlova: prioritet jezika, automatsko učitavanje i izgled. Sve se primjenjuje odmah.</summary>
public partial class SubtitleSettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly SubtitleController _subtitles;
    private bool _loading = true;

    public SubtitleSettingsWindow(AppSettings settings, SubtitleController subtitles)
    {
        _settings = settings;
        _subtitles = subtitles;
        InitializeComponent();
        WindowTheme.UseDarkTitleBar(this);
        MaxHeight = Math.Max(400, SystemParameters.WorkArea.Height - 24);

        foreach (var (name, color) in SubtitleStyleSettings.Colors)
        {
            var swatch = new RadioButton
            {
                Style = (Style)FindResource("ColorSwatch"),
                Tag = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)),
                ToolTip = name,
                GroupName = "SubtitleColor",
            };
            swatch.Checked += (_, _) => UpdateStyle(s => s.Color = color);
            ColorPanel.Children.Add(swatch);
        }

        LoadFromSettings();

        AutoLoadBox.Checked += (_, _) => _settings.AutoLoadSubtitles = true;
        AutoLoadBox.Unchecked += (_, _) => _settings.AutoLoadSubtitles = false;
        AutoSyncBox.Checked += (_, _) => _settings.AutoSyncSubtitles = true;
        AutoSyncBox.Unchecked += (_, _) => _settings.AutoSyncSubtitles = false;
        SizeSlider.ValueChanged += (_, e) => UpdateStyle(s => s.FontSize = Math.Round(e.NewValue));
        OutlineSlider.ValueChanged += (_, e) => UpdateStyle(s => s.OutlineSize = Math.Round(e.NewValue * 2) / 2);
        PositionSlider.ValueChanged += (_, e) => UpdateStyle(s => s.Position = Math.Round(e.NewValue));
        BoldBox.Checked += (_, _) => UpdateStyle(s => s.Bold = true);
        BoldBox.Unchecked += (_, _) => UpdateStyle(s => s.Bold = false);
        BackgroundBox.Checked += (_, _) => UpdateStyle(s => s.BackgroundBox = true);
        BackgroundBox.Unchecked += (_, _) => UpdateStyle(s => s.BackgroundBox = false);
        Closed += (_, _) => _settings.Save();
        _loading = false;
    }

    private void LoadFromSettings()
    {
        _loading = true;
        RefreshLanguages();
        AutoLoadBox.IsChecked = _settings.AutoLoadSubtitles;
        AutoSyncBox.IsChecked = _settings.AutoSyncSubtitles;

        SubtitleStyleSettings s = _settings.SubtitleStyle;
        FontButton.Content = s.FontName;
        SizeSlider.Value = s.FontSize;
        OutlineSlider.Value = s.OutlineSize;
        PositionSlider.Value = s.Position;
        BoldBox.IsChecked = s.Bold;
        BackgroundBox.IsChecked = s.BackgroundBox;
        for (int i = 0; i < SubtitleStyleSettings.Colors.Count; i++)
        {
            ((RadioButton)ColorPanel.Children[i]).IsChecked =
                string.Equals(SubtitleStyleSettings.Colors[i].Color, s.Color, StringComparison.OrdinalIgnoreCase);
        }

        _loading = false;
    }

    private void RefreshLanguages(int select = -1)
    {
        LanguageList.ItemsSource = _settings.SubtitleLanguages
            .Select((code, i) => $"{i + 1}.  {SubtitleLanguage.DisplayName(code)}")
            .ToList();
        if (select >= 0 && select < _settings.SubtitleLanguages.Count)
        {
            LanguageList.SelectedIndex = select;
        }
    }

    private void LanguagesChanged(int select)
    {
        RefreshLanguages(select);
        _subtitles.ApplyLanguagePriority();
    }

    private void OnMoveUp(object sender, RoutedEventArgs e)
    {
        int i = LanguageList.SelectedIndex;
        if (i > 0)
        {
            (_settings.SubtitleLanguages[i - 1], _settings.SubtitleLanguages[i]) = (_settings.SubtitleLanguages[i], _settings.SubtitleLanguages[i - 1]);
            LanguagesChanged(i - 1);
        }
    }

    private void OnMoveDown(object sender, RoutedEventArgs e)
    {
        int i = LanguageList.SelectedIndex;
        if (i >= 0 && i < _settings.SubtitleLanguages.Count - 1)
        {
            (_settings.SubtitleLanguages[i + 1], _settings.SubtitleLanguages[i]) = (_settings.SubtitleLanguages[i], _settings.SubtitleLanguages[i + 1]);
            LanguagesChanged(i + 1);
        }
    }

    private void OnRemove(object sender, RoutedEventArgs e)
    {
        int i = LanguageList.SelectedIndex;
        if (i >= 0 && _settings.SubtitleLanguages.Count > 1)
        {
            _settings.SubtitleLanguages.RemoveAt(i);
            LanguagesChanged(Math.Min(i, _settings.SubtitleLanguages.Count - 1));
        }
    }

    private void OnAddLanguage(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = AddLanguageButton, Placement = PlacementMode.Bottom };
        foreach (string code in SubtitleLanguage.Selectable.Where(c => !_settings.SubtitleLanguages.Contains(c)))
        {
            var item = new MenuItem { Header = SubtitleLanguage.DisplayName(code) };
            item.Click += (_, _) =>
            {
                _settings.SubtitleLanguages.Add(code);
                LanguagesChanged(_settings.SubtitleLanguages.Count - 1);
            };
            menu.Items.Add(item);
        }

        if (menu.Items.Count > 0)
        {
            menu.IsOpen = true;
        }
    }

    private void OnChooseFont(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = FontButton, Placement = PlacementMode.Bottom };
        foreach (string font in SubtitleStyleSettings.Fonts)
        {
            var item = new MenuItem
            {
                Header = font,
                FontFamily = new FontFamily(font),
                IsChecked = font == _settings.SubtitleStyle.FontName,
            };
            item.Click += (_, _) =>
            {
                FontButton.Content = font;
                UpdateStyle(s => s.FontName = font);
            };
            menu.Items.Add(item);
        }

        menu.IsOpen = true;
    }

    private void OnResetStyle(object sender, RoutedEventArgs e)
    {
        _settings.SubtitleStyle = new SubtitleStyleSettings();
        LoadFromSettings();
        _subtitles.ApplyStyle();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void UpdateStyle(Action<SubtitleStyleSettings> change)
    {
        if (_loading)
        {
            return;
        }

        change(_settings.SubtitleStyle);
        _subtitles.ApplyStyle();
    }
}

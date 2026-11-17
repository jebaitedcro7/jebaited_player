using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.Subtitles;
using JebaitedPlayer.ViewModels;

namespace JebaitedPlayer.Views;

/// <summary>
/// Postavke titlova u kartici "Titlovi" (UPGRADE 3; prije zaseban prozor): prioritet jezika (prvi = zadani), automatsko
/// učitavanje i traženje, izvori na internetu, kodiranje starih titlova, izgled i sinkronizacija. Sve se primjenjuje odmah.
/// </summary>
public partial class SubtitleSettingsPanel : UserControl
{
    private readonly AppSettings _settings;
    private readonly SubtitleController _subtitles;
    private readonly Action _openSources;
    private readonly Action _openOnlineSetup;
    private bool _loading = true;

    public SubtitleSettingsPanel(AppSettings settings, SubtitleController subtitles, Action openSources, Action openOnlineSetup)
    {
        _settings = settings;
        _subtitles = subtitles;
        _openSources = openSources;
        _openOnlineSetup = openOnlineSetup;
        InitializeComponent();

        foreach (var (nameKey, color) in SubtitleStyleSettings.Colors)
        {
            var swatch = new RadioButton
            {
                Style = (Style)FindResource("ColorSwatch"),
                Tag = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)),
                GroupName = "SubtitleColor",
            };
            swatch.Bind(ToolTipProperty, nameKey);
            swatch.Checked += (_, _) => UpdateStyle(s => s.Color = color);
            ColorPanel.Children.Add(swatch);
        }

        LoadFromSettings();

        AutoLoadBox.Checked += (_, _) => _settings.AutoLoadSubtitles = true;
        AutoLoadBox.Unchecked += (_, _) => _settings.AutoLoadSubtitles = false;
        AutoSyncBox.Checked += (_, _) => _settings.AutoSyncSubtitles = true;
        AutoSyncBox.Unchecked += (_, _) => _settings.AutoSyncSubtitles = false;
        AutoSearchBox.Checked += (_, _) => _settings.Online.AutoSearch = true;
        AutoSearchBox.Unchecked += (_, _) => _settings.Online.AutoSearch = false;
        SaveNextBox.Checked += (_, _) => _settings.Online.SaveNextToVideo = true;
        SaveNextBox.Unchecked += (_, _) => _settings.Online.SaveNextToVideo = false;
        SizeSlider.ValueChanged += (_, e) => UpdateStyle(s => s.FontSize = Math.Round(e.NewValue));
        OutlineSlider.ValueChanged += (_, e) => UpdateStyle(s => s.OutlineSize = Math.Round(e.NewValue * 2) / 2);
        PositionSlider.ValueChanged += (_, e) => UpdateStyle(s => s.Position = Math.Round(e.NewValue));
        BoldBox.Checked += (_, _) => UpdateStyle(s => s.Bold = true);
        BoldBox.Unchecked += (_, _) => UpdateStyle(s => s.Bold = false);
        BackgroundBox.Checked += (_, _) => UpdateStyle(s => s.BackgroundBox = true);
        BackgroundBox.Unchecked += (_, _) => UpdateStyle(s => s.BackgroundBox = false);
        _loading = false;
    }

    /// <summary>Promjena jezika sučelja: nazivi jezika i kodiranja na novom jeziku.</summary>
    public void RefreshTexts()
    {
        RefreshLanguages(LanguageList.SelectedIndex);
        UpdateEncodingText();
    }

    private void LoadFromSettings()
    {
        _loading = true;
        RefreshLanguages();
        AutoLoadBox.IsChecked = _settings.AutoLoadSubtitles;
        AutoSyncBox.IsChecked = _settings.AutoSyncSubtitles;
        AutoSearchBox.IsChecked = _settings.Online.AutoSearch;
        SaveNextBox.IsChecked = _settings.Online.SaveNextToVideo;
        UpdateEncodingText();

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

    private void UpdateEncodingText() =>
        EncodingButton.Content = _settings.SubtitleCodePage is int cp ? SubtitleEncoding.NameOf(cp) : Loc.T("menu.encodingAuto");

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

    private void OnEncodingClick(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = EncodingButton, Placement = PlacementMode.Bottom };
        var auto = new MenuItem { Header = Loc.T("menu.encodingAuto"), IsChecked = _settings.SubtitleCodePage is null };
        auto.Click += (_, _) => SetEncoding(null);
        menu.Items.Add(auto);
        foreach (var (codePage, name) in SubtitleEncoding.ManualChoices.Where(c => c.CodePage is not (65001 or 1200 or 1201)))
        {
            int cp = codePage;
            var item = new MenuItem { Header = name, IsChecked = _settings.SubtitleCodePage == cp };
            item.Click += (_, _) => SetEncoding(cp);
            menu.Items.Add(item);
        }

        menu.IsOpen = true;
    }

    private void SetEncoding(int? codePage)
    {
        _settings.SubtitleCodePage = codePage;
        Services.SubtitleService.FallbackCodePage = codePage;
        UpdateEncodingText();
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

    private void OnSources(object sender, RoutedEventArgs e) => _openSources();

    private void OnOnlineSetup(object sender, RoutedEventArgs e) => _openOnlineSetup();

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

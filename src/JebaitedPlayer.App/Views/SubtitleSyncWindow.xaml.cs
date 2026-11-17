using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.Subtitles;
using JebaitedPlayer.ViewModels;

namespace JebaitedPlayer.Views;

/// <summary>
/// Sinkronizacija titlova, četiri jasno odvojena načina: automatski prema govoru, pomak (svi titlovi jednako),
/// postupna korekcija (titlovi sve više kasne ili žure, sidreno na trenutnoj replici) i poravnanje po replici (bez žurbe,
/// iz popisa replika; više točaka daje pomak, brzinu ili ispravak po dijelovima). FPS omjeri i stari Z/X su pod "Napredno".
/// </summary>
public partial class SubtitleSyncWindow : Window
{
    private readonly SubtitleController _subtitles;
    private readonly Func<KeyEventArgs, bool> _forwardKey;

    public SubtitleSyncWindow(SubtitleController subtitles, AutoSyncController autoSync, Func<KeyEventArgs, bool> forwardKey)
    {
        _subtitles = subtitles;
        _forwardKey = forwardKey;
        InitializeComponent();
        DataContext = subtitles;
        AutoPanel.DataContext = autoSync;
        MaxHeight = Math.Max(400, SystemParameters.WorkArea.Height - 24);
        WindowTheme.UseDarkTitleBar(this);

        BuildPresets();
        this.OnLanguageChanged(BuildPresets);

        DelayBox.KeyDown += CommitOnEnter;
        SpeedBox.KeyDown += CommitOnEnter;
        PreviewKeyDown += OnPreviewKeyDown;
        Loaded += (_, _) => _subtitles.RefreshNearbyCues();
    }

    /// <summary>Gumbi za drugi FPS (nazivi s brojevima prema jeziku, pa se ponovno slažu kad se jezik promijeni).</summary>
    private void BuildPresets()
    {
        PresetPanel.Children.Clear();
        AddPreset(Loc.T("sync.noCorrection"), 1.0);
        foreach (FrameRatePreset preset in SubtitleTiming.FrameRatePresets)
        {
            AddPreset(preset.Text, preset.Speed);
        }
    }

    private void AddPreset(string text, double speed)
    {
        var button = new Button { Content = text, Style = (Style)FindResource("SecondaryButton") };
        button.Click += (_, _) => _subtitles.ApplySpeedPreset(speed);
        PresetPanel.Children.Add(button);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
            return;
        }

        // Dok se piše u polje ili bira replika strelicama, tipke pripadaju njima; inače rade prečaci playera (G/H, razmaknica…).
        if (Keyboard.FocusedElement is TextBox || (Keyboard.FocusedElement is ListBoxItem && e.Key is Key.Up or Key.Down))
        {
            return;
        }

        e.Handled = _forwardKey(e);
    }

    private static void CommitOnEnter(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox box)
        {
            box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            box.SelectAll();
            e.Handled = true;
        }
    }

    private void OnDelayStep(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag } &&
            double.TryParse(tag, NumberStyles.Float, CultureInfo.InvariantCulture, out double ms))
        {
            _subtitles.AdjustDelay(ms);
        }
    }

    private void OnGradualLate(object sender, RoutedEventArgs e) => _subtitles.AdjustGradual(-1);

    private void OnGradualEarly(object sender, RoutedEventArgs e) => _subtitles.AdjustGradual(+1);

    private void OnGradualReset(object sender, RoutedEventArgs e) => _subtitles.ResetGradual();

    private void OnRefreshCues(object sender, RoutedEventArgs e) => _subtitles.RefreshNearbyCues();

    private void OnCueSelectionChanged(object sender, SelectionChangedEventArgs e) => AnchorButton.IsEnabled = CueList.SelectedItem != null;

    private void OnAddAnchor(object sender, RoutedEventArgs e)
    {
        if (CueList.SelectedItem is CueRow row)
        {
            _subtitles.AddAnchor(row);
        }
    }

    private void OnCueDoubleClick(object sender, MouseButtonEventArgs e) => OnAddAnchor(sender, e);

    private void OnRemoveAnchor(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: AnchorRow row })
        {
            _subtitles.RemoveAnchor(row);
        }
    }

    private void OnClearAnchors(object sender, RoutedEventArgs e) => _subtitles.ClearAnchors();

    private void OnToggleAdvanced(object sender, RoutedEventArgs e)
    {
        bool show = AdvancedPanel.Visibility != Visibility.Visible;
        AdvancedPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (sender is Button button)
        {
            button.Bind(ContentProperty, show ? "sync.advancedOpen" : "sync.advanced");
        }
    }

    private void OnMarkAudio(object sender, RoutedEventArgs e) => _subtitles.MarkAudio();

    private void OnMarkSubtitle(object sender, RoutedEventArgs e) => _subtitles.MarkSubtitle();

    private void OnResetPoints(object sender, RoutedEventArgs e) => _subtitles.ResetSyncPoints();

    private void OnReset(object sender, RoutedEventArgs e) => _subtitles.ResetSync();

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (_subtitles.SelectedTrack is not { } track)
        {
            return;
        }

        string suggested = _subtitles.SuggestedSavePath(track);
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = Loc.T("dialog.saveSynced"),
            FileName = Path.GetFileName(suggested),
            InitialDirectory = Path.GetDirectoryName(suggested),
            Filter = $"{Loc.T("dialog.srtSubtitles")}|*.srt|{Loc.T("dialog.assSubtitles")}|*.ass;*.ssa|{Loc.T("dialog.allFiles")}|*.*",
            FilterIndex = Path.GetExtension(suggested) == ".srt" ? 1 : 2,
            OverwritePrompt = true,
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            _subtitles.SaveSyncedCopy(track, dialog.FileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            MessageBox.Show(this, Loc.T("error.saveSyncedFailed", ex.Message), "jebaited_player", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}

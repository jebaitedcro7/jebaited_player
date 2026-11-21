using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace JebaitedPlayer.Views;

/// <summary>Što skin prikazuje: isto stanje za sve izglede (izračunava ga <see cref="MiniPlayerWindow"/>).</summary>
internal readonly record struct MiniSkinState(
    string Title, string Time, string Remaining, string Source, double Position, double Duration, bool CanSeek,
    bool Playing, string MuteGlyph, bool Live, double Volume, bool EqOn, bool PlaylistOn);

/// <summary>
/// 2.1.7: zajednička osnova za izglede 2 (kartica), 4 (bočna ploča) i 5 (NLO zaslon). Svi izgledi imaju iste elemente
/// (naslov, vrijeme, izvor, LIVE, traka, Play/Pause, Prethodna/Sljedeća, Stop, Otvori, glasnoća, Discord, EQ, popis)
/// i iste radnje; razlikuje se samo raspored. Radnje se šalju mini playeru.
/// </summary>
public abstract class MiniSkinBase : UserControl
{
    internal MiniPlayerWindow? Mini { get; set; }

    internal abstract MarqueeView TitleMarquee { get; }

    internal abstract TextBlock TimeBlock { get; }

    internal abstract TextBlock SourceBlock { get; }

    internal abstract Slider SeekBar { get; }

    internal abstract Button PlayPauseButton { get; }

    internal abstract Button MuteButton { get; }

    internal abstract Slider VolumeBar { get; }

    internal abstract LiveBadge LiveMark { get; }

    internal abstract Button DiscordButton { get; }

    internal abstract ToggleButton EqButton { get; }

    internal abstract ToggleButton PlaylistButton { get; }

    internal void Apply(in MiniSkinState s)
    {
        TitleMarquee.Text = s.Title;
        TimeBlock.Text = s.Time;
        SourceBlock.Text = s.Source;
        SeekBar.IsEnabled = s.CanSeek;
        SeekBar.Maximum = Math.Max(1, s.Duration);
        if (Mini is { IsSeeking: false })
        {
            SeekBar.Value = Math.Clamp(s.Position, 0, SeekBar.Maximum);
        }

        PlayPauseButton.Content = s.Playing ? "" : "";
        MuteButton.Content = s.MuteGlyph;
        LiveMark.Visibility = s.Live ? Visibility.Visible : Visibility.Collapsed;
        EqButton.IsChecked = s.EqOn;
        PlaylistButton.IsChecked = s.PlaylistOn;
        Mini?.SyncVolume(VolumeBar, s.Volume);
    }

    protected void OnPrevious(object sender, RoutedEventArgs e) => Mini?.OnPrevious(sender, e);

    protected void OnPlayPause(object sender, RoutedEventArgs e) => Mini?.OnPlayPause(sender, e);

    protected void OnNext(object sender, RoutedEventArgs e) => Mini?.OnNext(sender, e);

    protected void OnStop(object sender, RoutedEventArgs e) => Mini?.OnStop(sender, e);

    protected void OnOpen(object sender, RoutedEventArgs e) => Mini?.OnOpen(sender, e);

    protected void OnMute(object sender, RoutedEventArgs e) => Mini?.OnMute(sender, e);

    protected void OnMinimize(object sender, RoutedEventArgs e) => Mini?.OnMinimize(sender, e);

    protected void OnFullPlayer(object sender, RoutedEventArgs e) => Mini?.OnFullPlayer(sender, e);

    protected void OnClosePlayer(object sender, RoutedEventArgs e) => Mini?.OnClosePlayer(sender, e);

    protected void OnStrip(object sender, RoutedEventArgs e) => Mini?.SetCompact(true);

    protected void OnDiscord(object sender, RoutedEventArgs e) => Mini?.OnDiscord(sender, e);

    protected void OnSourceClick(object sender, MouseButtonEventArgs e) => Mini?.OnSourceClick(sender, e);

    protected void OnPanels(object sender, RoutedEventArgs e) =>
        Mini?.SetPanels(EqButton.IsChecked == true, PlaylistButton.IsChecked == true);

    protected void OnInfoMenu(object sender, MouseButtonEventArgs e) => Mini?.OnInfoMenu(sender, e);
}

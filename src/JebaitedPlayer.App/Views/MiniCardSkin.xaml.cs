using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace JebaitedPlayer.Views;

/// <summary>Izgled 2: kartica s omotom.</summary>
public partial class MiniCardSkin : MiniSkinBase
{
    public MiniCardSkin() => InitializeComponent();

    internal override MarqueeView TitleMarquee => Title;
    internal override TextBlock TimeBlock => Time;
    internal override TextBlock SourceBlock => Source;
    internal override Slider SeekBar => Seek;
    internal override Button PlayPauseButton => PlayPause;
    internal override Button MuteButton => Mute;
    internal override Slider VolumeBar => Volume;
    internal override LiveBadge LiveMark => Live;
    internal override Button DiscordButton => Discord;
    internal override ToggleButton EqButton => Eq;
    internal override ToggleButton PlaylistButton => Playlist;
    internal override Image? CoverImage => Cover;
}

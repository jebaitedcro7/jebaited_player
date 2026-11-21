using System.Windows;
using System.Windows.Input;
using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.Services;
using JebaitedPlayer.Services.Discord;

namespace JebaitedPlayer;

/// <summary>
/// 2.1.7: Home ekran s kontrolama reprodukcije. Tipka Home prije je zaustavljala glazbu (<c>GoHome</c> je zvao Stop).
/// Sada glazba i radio sviraju dalje: početni ekran samo prekrije prikaz playera, a ista traka kontrola (naslov, Play/Pause,
/// Prethodna/Sljedeća, glasnoća, izvor) čita i mijenja isto stanje kao player i mini player. Audio engine se ne dira.
/// </summary>
public partial class MainWindow
{
    /// <summary>Korisnik je na početnom ekranu, a glazba ili radio i dalje sviraju.</summary>
    private bool _homeView;

    private int _homeTicks;
    private string _homeTrack = string.Empty;

    internal bool IsHomeView => _homeView;

    internal string HomeTrackTextForTests => HomeTrackText.Text;

    /// <summary>Home za glazbu i radio ne zaustavlja ništa (video se i dalje zatvara, kao prije).</summary>
    private bool HomeKeepsPlaying => _vm.HasMedia && (_vm.IsAudioOnly || _vm.IsLiveStream);

    private void InitializeHomePlayback()
    {
        // Kotačić miša mijenja glasnoću samo iznad trake glasnoće (i ovdje).
        HomeVolumeArea.PreviewMouseWheel += (_, e) =>
        {
            _vm.Volume += e.Delta > 0 ? ViewModels.PlayerViewModel.VolumeWheelStep : -ViewModels.PlayerViewModel.VolumeWheelStep;
            e.Handled = true;
        };
    }

    private void EnterHomeWhilePlaying()
    {
        _homeView = true;
        Log.Info($"Povratak na početni ekran uz reprodukciju: {Path.GetFileName(_vm.CurrentPath)} svira dalje.");
        UpdatePanels();
        UpdateOverlayVisibility();
        UpdateCards();
        UpdateHomeClockTimer();
        _homeTicks = 0;
        UpdateHomePlayback();
        RestoreKeyboardFocus();
    }

    /// <summary>Povratak na prikaz playera (gumb na traci ili učitavanje nove datoteke).</summary>
    internal void LeaveHomeView()
    {
        if (!_homeView)
        {
            return;
        }

        _homeView = false;
        UpdatePanels();
        UpdateOverlayVisibility();
        UpdateCards();
        UpdateHomeClockTimer();
        RestoreKeyboardFocus();
    }

    private bool SpotifyIsSource => _spotify?.IsActiveSource == true;

    private bool YouTubeIsSource => _youTube?.IsActiveSource == true;

    /// <summary>Svakih 200 ms (UI timer), samo dok je početni ekran vidljiv: stvarno stanje reprodukcije.</summary>
    private void UpdateHomePlayback()
    {
        bool visible = WelcomePanel.IsVisible && (_vm.HasMedia || _vm.StoppedStream != null || SpotifyIsSource || YouTubeIsSource);
        HomePlaybackBar.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (!visible)
        {
            HomeLive.Visibility = Visibility.Collapsed;
            return;
        }

        bool spotify = SpotifyIsSource, youtube = YouTubeIsSource;
        bool stream = !spotify && !youtube && (_vm.IsLiveStream || _vm.StoppedStream != null);
        bool live = !spotify && !youtube && _vm.IsLiveStream && !_vm.IsPaused;

        string source = Loc.T(spotify ? "mini.sourceSpotify" : youtube ? "mini.sourceYouTube" : stream ? "mini.sourceRadio" : "mini.sourceLocal");
        if (_discord is { State: DiscordBotState.InChannel, Decision: DiscordAudioDecision.Send })
        {
            source += " · " + Loc.T("home.sourceDiscord");
        }

        HomeSourceText.Text = source;
        HomeLive.Visibility = live ? Visibility.Visible : Visibility.Collapsed;

        string time = string.Empty;
        bool playing;
        if (spotify)
        {
            JebaitedPlayer.Online.SpotifyPlayback? s = _spotify!.State;
            playing = s is { IsPlaying: true };
            _homeTrack = s is { Track.Length: > 0 } ? (s.Artists.Length > 0 ? $"{s.Artists} — {s.Track}" : s.Track) : Loc.T("spotify.nothingPlaying");
            time = s is { DurationMs: > 0 } ? $"{TimeFormat.Format((s.ProgressMs) / 1000.0, false)} / {TimeFormat.Format(s.DurationMs / 1000.0, false)}" : string.Empty;
        }
        else if (youtube)
        {
            YouTubeState? s = _youTube!.State;
            playing = s is { IsPlaying: true };
            _homeTrack = s is { Title.Length: > 0 } ? s.Title : Loc.T("youtube.title");
            time = s is { Duration: > 0 } ? $"{TimeFormat.Format(s.CurrentTime, false)} / {TimeFormat.Format(s.Duration, false)}" : string.Empty;
        }
        else
        {
            playing = _vm.HasMedia && !_vm.IsPaused;
            if (_vm.StoppedStream is { } stopped && !_vm.HasMedia)
            {
                _homeTrack = $"{stopped.Title}  ···  {Loc.T("mini.stopped")}";
            }
            else
            {
                if (_homeTicks++ % 5 == 0 || _homeTrack.Length == 0)
                {
                    _ = RefreshHomeTrackAsync();
                }

                time = _vm.IsLiveStream
                    ? TimeFormat.Format(_vm.Position, _vm.Position >= 3600)
                    : _vm.Duration > 0 ? $"{TimeFormat.Format(_vm.Position, _vm.Duration >= 3600)} / {TimeFormat.Format(_vm.Duration, _vm.Duration >= 3600)}" : string.Empty;
            }
        }

        HomeTrackText.Text = _homeTrack;
        HomeTimeInfo.Text = time;
        HomePlayPauseButton.Content = playing ? "" : "";
        HomeVolumeArea.Visibility = spotify || youtube ? Visibility.Collapsed : Visibility.Visible;
        bool canSkip = spotify || youtube || (!stream && _vm.Library.HasPlaylist);
        HomePrevButton.IsEnabled = HomeNextButton.IsEnabled = canSkip;
    }

    private async Task RefreshHomeTrackAsync()
    {
        MpvPlayerAccess? access = _player is null ? null : new MpvPlayerAccess(_player);
        if (access is null)
        {
            return;
        }

        bool stream = _vm.IsLiveStream;
        string? path = _vm.CurrentPath;
        string title = _vm.Title;
        TrackMeta meta = await Task.Run(() => TrackInfo.Read(access.Get, path, title, stream));
        _homeTrack = TrackInfo.Format(meta, _settings.MiniPlayer.InfoFields);
        HomeTrackText.Text = _homeTrack;
    }

    /// <summary>Mali omotač: mpv se čita s pozadinske dretve, a dohvat ne smije bacati.</summary>
    private sealed class MpvPlayerAccess(JebaitedPlayer.Mpv.MpvPlayer player)
    {
        public string? Get(string name)
        {
            try
            {
                return player.GetPropertyString(name);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    private void OnHomePrevious(object sender, RoutedEventArgs e)
    {
        if (YouTubeIsSource)
        {
            _ = _youTube!.PreviousAsync();
        }
        else if (SpotifyIsSource)
        {
            _ = _spotify!.PreviousAsync();
        }
        else
        {
            _vm.Library.PlayPrevious();
        }
    }

    private void OnHomeNext(object sender, RoutedEventArgs e)
    {
        if (YouTubeIsSource)
        {
            _ = _youTube!.NextAsync();
        }
        else if (SpotifyIsSource)
        {
            _ = _spotify!.NextAsync();
        }
        else
        {
            _vm.Library.PlayNext();
        }
    }

    private void OnHomePlayPause(object sender, RoutedEventArgs e)
    {
        if (YouTubeIsSource)
        {
            _ = _youTube!.TogglePlayAsync();
        }
        else if (SpotifyIsSource)
        {
            _ = _spotify!.TogglePlayAsync();
        }
        else if (_vm.HasMedia || _vm.StoppedStream != null)
        {
            _vm.TogglePause();
        }

        UpdateHomePlayback();
    }

    private void OnHomeShowPlayer(object sender, RoutedEventArgs e)
    {
        if (_vm.HasMedia)
        {
            LeaveHomeView();
        }
        else if (IsMiniPlayerActive is false)
        {
            ShowMiniPlayer();
        }
    }
}

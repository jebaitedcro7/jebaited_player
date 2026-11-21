using System.Windows;
using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.Services;
using JebaitedPlayer.ViewModels;
using JebaitedPlayer.Views;

namespace JebaitedPlayer;

/// <summary>Ciklus 6, V9: YouTube Music (službeni ugrađeni player + Data API s korisnikovim ključem).</summary>
public partial class MainWindow
{
    private YouTubeRemote? _youTube;
    private YouTubeWindow? _youTubeWindow;
    private bool _youTubeWasActive;

    internal YouTubeWindow? YouTubeWindowForTests => _youTubeWindow;

    internal YouTubeRemote YouTube
    {
        get
        {
            if (_youTube is null)
            {
                _youTube = new YouTubeRemote(AppPaths.DataDir,
                    persist: !_options.IsDiagnostics || Environment.GetEnvironmentVariable("JEBAITED_DATA_DIR") is { Length: > 0 });
                _youTube.Changed += (_, _) => Dispatcher.BeginInvoke(OnYouTubeChanged);
                _vm.LoadStarting += (_, _) => StopYouTubeSource();
                _vm.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(PlayerViewModel.IsPaused) && !_vm.IsPaused && _vm.HasMedia)
                    {
                        StopYouTubeSource();
                    }
                };
            }

            return _youTube;
        }
    }

    /// <summary>Lokalna datoteka ili radio: YouTube se pauzira i više nije izvor (jedan zvuk u isto vrijeme).</summary>
    private void StopYouTubeSource()
    {
        if (_youTube is { IsActiveSource: true } yt)
        {
            yt.IsActiveSource = false;
            _ = yt.PauseAsync();
        }
    }

    /// <summary>YouTube je postao izvor: lokalni zvuk se pauzira, a Spotify prestaje biti izvor (jedan zvuk u isto vrijeme).</summary>
    private void OnYouTubeChanged()
    {
        bool active = _youTube?.IsActiveSource == true;
        if (active && !_youTubeWasActive)
        {
            if (_vm.HasMedia)
            {
                if (_vm.IsLiveStream)
                {
                    _vm.StopPlayback();
                }
                else
                {
                    _vm.SetPaused(true);
                }
            }

            if (_spotify is { IsActiveSource: true } spotify)
            {
                spotify.IsActiveSource = false;
            }
        }

        _youTubeWasActive = active;
        UpdateDiscordInputs();
        _mini?.Tick();
    }

    internal void ShowYouTube(Window? owner = null)
    {
        if (_youTubeWindow is { IsLoaded: true })
        {
            _youTubeWindow.Activate();
            return;
        }

        _youTubeWindow = new YouTubeWindow(YouTube, AppPaths.DataDir) { Owner = owner ?? (IsMiniPlayerActive ? _mini : this) };
        _youTubeWindow.Closed += (_, _) =>
        {
            _youTubeWindow = null;
            UpdateDiscordInputs();
        };

        // V4-B: dok je YouTube otvoren, bot za Discord šuti (prije nego WebView2 išta pusti).
        UpdateDiscordInputs();
        _youTubeWindow.Show();
    }

    void IMiniPlayerHost.ShowYouTube(Window owner) => ShowYouTube(owner);

    YouTubeRemote IMiniPlayerHost.YouTube => YouTube;
}

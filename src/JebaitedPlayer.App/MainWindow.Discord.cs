using System.ComponentModel;
using System.Windows;
using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.Services.Discord;
using JebaitedPlayer.ViewModels;
using JebaitedPlayer.Views;

namespace JebaitedPlayer;

/// <summary>V4-B: bot za Discord (vlastiti bot korisnika). Ovdje je veza playera i bota.</summary>
public partial class MainWindow
{
    private DiscordBot? _discord;
    private DiscordWindow? _discordWindow;

    internal DiscordWindow? DiscordWindowForTests => _discordWindow;

    /// <summary>Jedan za cijeli player; stvara se tek kad zatreba (bez bota nema ni mreže ni hvatanja zvuka).</summary>
    internal DiscordBot Discord
    {
        get
        {
            if (_discord is null)
            {
                _discord = new DiscordBot(AppPaths.DataDir,
                    persist: !_options.IsDiagnostics || Environment.GetEnvironmentVariable("JEBAITED_DATA_DIR") is { Length: > 0 });
                _discord.Changed += (_, _) => Dispatcher.BeginInvoke(() => _mini?.Tick());
                _vm.PropertyChanged += OnPlayerChangedForDiscord;
                UpdateDiscordInputs();
            }

            return _discord;
        }
    }

    /// <summary>Bot ako je već stvoren (mini player ga samo prikazuje, ne stvara).</summary>
    DiscordBot? IMiniPlayerHost.DiscordBot => _discord;

    /// <summary>
    /// Stanje playera za DiscordAudioGate: šalje se samo lokalna glazba i radio.
    /// YouTube se računa čim je prozor otvoren (WebView2 je u istom stablu procesa, pa bi se njegov zvuk uhvatio).
    /// </summary>
    internal DiscordAudioInputs CurrentDiscordInputs() => new(
        HasMedia: _vm.HasMedia,
        IsPaused: _vm.IsPaused,
        HasVideo: _vm.HasMedia && !_vm.IsAudioOnly,
        YouTubeOpen: _youTubeWindow is not null || _youTube?.IsWindowOpen == true,
        SpotifyActive: _spotify?.IsActiveSource == true);

    /// <summary>
    /// Javlja botu stanje odmah pri promjeni (pauza, nova datoteka, video, YouTube, Spotify). Pri učitavanju nove datoteke
    /// IsAudioOnly je najprije false, pa bot šuti dok se ne zna da je datoteka samo zvuk (film nikad ne procuri).
    /// </summary>
    internal void UpdateDiscordInputs()
    {
        if (_discord is not null)
        {
            _discord.Inputs = CurrentDiscordInputs();
            UpdateDiscordGain();
        }
    }

    /// <summary>2.1.12: pojačanje za Discord prema glasnoći playera i postavci „prati player”.</summary>
    internal void UpdateDiscordGain()
    {
        if (_discord is not null)
        {
            _discord.SendGain = DiscordVolume.Gain(_settings.DiscordFollowPlayerVolume, _vm.Volume, _vm.IsMuted, _settings.DiscordVolume);
        }
    }

    private void OnPlayerChangedForDiscord(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PlayerViewModel.HasMedia) or nameof(PlayerViewModel.IsPaused) or nameof(PlayerViewModel.IsAudioOnly))
        {
            UpdateDiscordInputs();
        }
        else if (e.PropertyName is nameof(PlayerViewModel.Volume) or nameof(PlayerViewModel.IsMuted))
        {
            UpdateDiscordGain();
        }
    }

    internal void ShowDiscord(Window? owner = null)
    {
        if (_discordWindow is { IsLoaded: true })
        {
            _discordWindow.Activate();
            return;
        }

        _discordWindow = new DiscordWindow(Discord, _settings, UpdateDiscordGain) { Owner = owner ?? (IsMiniPlayerActive ? _mini : this) };
        _discordWindow.Closed += (_, _) => _discordWindow = null;
        _discordWindow.Show();
    }

    void IMiniPlayerHost.ShowDiscord(Window owner) => ShowDiscord(owner);

    /// <summary>Zatvaranje playera: bot izlazi iz kanala i odjavljuje se (najviše 3 s, UI ne zastaje).</summary>
    private async Task CloseDiscordAsync()
    {
        if (_discord is { } bot)
        {
            await bot.CloseAsync(TimeSpan.FromSeconds(3));
            bot.Dispose();
        }
    }
}

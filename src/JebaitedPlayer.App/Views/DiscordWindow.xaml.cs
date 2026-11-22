using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.Services.Discord;

namespace JebaitedPlayer.Views;

/// <summary>
/// V4-B: bot za Discord. Vodič za vlastitog bota i token (DPAPI), spajanje, „Uđi u moj kanal” ili izbor kanala,
/// izlazak, stanje slanja (što se šalje ili zašto je tišina) i razina zvuka, pozivnica bota.
/// </summary>
public partial class DiscordWindow : Window
{
    private readonly DiscordBot _bot;
    private readonly DispatcherTimer _timer;
    private bool _editingToken;
    private string? _message;
    private string _channelsKey = string.Empty;
    private DateTime? _onlineSince;

    private readonly AppSettings _settings;
    private readonly Action _volumeChanged;
    private bool _loadingVolume;

    internal DiscordWindow(DiscordBot bot, AppSettings settings, Action volumeChanged)
    {
        _bot = bot;
        _settings = settings;
        _volumeChanged = volumeChanged;
        InitializeComponent();
        WindowTheme.UseDarkTitleBar(this);
        LoadVolume();

        // 4× u sekundi, samo dok je bot u kanalu (razina i odluka) ili dok popis servera još nije stigao
        // (Discord ga šalje tek nakon prijave, pa bi inače ostala poruka „bot ne vidi nijedan kanal”).
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) =>
        {
            if (_bot.State == DiscordBotState.InChannel)
            {
                RenderLive();
            }
            else
            {
                Render();
            }
        }, Dispatcher);
        _timer.Stop();
        _bot.Changed += OnBotChanged;
        Closed += (_, _) =>
        {
            _bot.Changed -= OnBotChanged;
            _timer.Stop();
        };
        Loaded += (_, _) => Render();
    }

    private void OnBotChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(Render);

    // --- 2.1.12: glasnoća na Discordu --------------------------------------------------------------------------------

    private void LoadVolume()
    {
        _loadingVolume = true;
        FollowVolumeBox.IsChecked = _settings.DiscordFollowPlayerVolume;
        VolumeSlider.Value = Math.Clamp(double.IsFinite(_settings.DiscordVolume) ? _settings.DiscordVolume : DiscordVolume.DefaultVolume, 0, 100);
        _loadingVolume = false;
        RenderVolume();
    }

    private void RenderVolume()
    {
        bool follow = FollowVolumeBox.IsChecked == true;
        VolumeSlider.IsEnabled = !follow;
        VolumeValue.Text = follow ? string.Empty : $"{VolumeSlider.Value:0} %";
    }

    /// <summary>Kvačica „prati player” (i za provjeru).</summary>
    internal void SetFollowPlayerVolume(bool follow)
    {
        FollowVolumeBox.IsChecked = follow;
        OnVolumeMode(FollowVolumeBox, new RoutedEventArgs());
    }

    internal void SetDiscordVolume(double volume) => VolumeSlider.Value = volume;

    private void OnVolumeMode(object sender, RoutedEventArgs e)
    {
        _settings.DiscordFollowPlayerVolume = FollowVolumeBox.IsChecked == true;
        Log.Info($"Discord: glasnoća {(_settings.DiscordFollowPlayerVolume ? "prati player" : $"vlastita ({_settings.DiscordVolume:0} %)")}.");
        RenderVolume();
        _volumeChanged();
    }

    private void OnDiscordVolume(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loadingVolume || !IsInitialized)
        {
            return;
        }

        _settings.DiscordVolume = Math.Round(e.NewValue);
        RenderVolume();
        _volumeChanged();
    }

    /// <summary>Poruka greške bota za korisnika (dijagnostika je koristi za provjeru svih vrijednosti).</summary>
    internal static string? ErrorText(DiscordBotError error, string? detail) => error switch
    {
        DiscordBotError.None => null,
        DiscordBotError.BadToken => Loc.T("discord.errorBadToken"),
        DiscordBotError.Network => Loc.T("discord.errorNetwork", detail ?? string.Empty),
        DiscordBotError.NotSupported => Loc.T("discord.errorNotSupported"),
        DiscordBotError.JoinFailed => Loc.T("discord.errorJoinFailed", detail ?? string.Empty),
        DiscordBotError.NoPermission => Loc.T("discord.errorNoPermission", detail ?? string.Empty),
        DiscordBotError.ChannelFull => Loc.T("discord.errorChannelFull"),
        _ => Loc.T("discord.errorOther", detail ?? string.Empty),
    };

    /// <summary>Što bot trenutno šalje ili zašto šalje tišinu.</summary>
    internal static string DecisionText(DiscordAudioDecision decision) => decision switch
    {
        DiscordAudioDecision.Send => Loc.T("discord.sendingMusic"),
        DiscordAudioDecision.SilencePaused => Loc.T("discord.silencePaused"),
        DiscordAudioDecision.SilenceVideo => Loc.T("discord.silenceVideo"),
        DiscordAudioDecision.SilenceYouTube => Loc.T("discord.silenceYouTube"),
        DiscordAudioDecision.SilenceSpotify => Loc.T("discord.silenceSpotify"),
        _ => Loc.T("discord.silenceNothing"),
    };

    internal void Render()
    {
        bool setup = !_bot.HasToken || _editingToken;
        DiscordBotState state = _bot.State;
        bool online = state is DiscordBotState.Online or DiscordBotState.Joining or DiscordBotState.InChannel;
        bool inChannel = state == DiscordBotState.InChannel;

        SetupPanel.Visibility = setup ? Visibility.Visible : Visibility.Collapsed;
        BotPanel.Visibility = setup ? Visibility.Collapsed : Visibility.Visible;
        ConnectButton.Visibility = online ? Visibility.Collapsed : Visibility.Visible;
        ConnectButton.IsEnabled = state != DiscordBotState.Connecting;
        DisconnectButton.Visibility = online ? Visibility.Visible : Visibility.Collapsed;
        BotText.Text = state switch
        {
            DiscordBotState.Connecting => Loc.T("discord.connecting"),
            DiscordBotState.Online or DiscordBotState.Joining or DiscordBotState.InChannel => Loc.T("discord.online", _bot.BotName ?? string.Empty),
            _ => Loc.T("discord.offline"),
        };

        ChannelPanel.Visibility = online && !setup ? Visibility.Visible : Visibility.Collapsed;
        ChannelText.Text = inChannel && _bot.Channel is { } channel
            ? Loc.T("discord.inChannel", channel.ChannelName, channel.GuildName)
            : state == DiscordBotState.Joining ? Loc.T("discord.joining") : Loc.T("discord.notInChannel");
        bool canJoin = state == DiscordBotState.Online && DiscordBot.IsSupported;
        JoinMineButton.IsEnabled = canJoin;
        ChannelList.IsEnabled = canJoin;
        LeaveButton.Visibility = inChannel ? Visibility.Visible : Visibility.Collapsed;
        if (online)
        {
            FillChannels();
        }

        InviteButton.Visibility = _bot.InviteUrl is null ? Visibility.Collapsed : Visibility.Visible;

        // Popis servera stiže nakon prijave: poruka „nema kanala” tek ako ga ni nakon 3 s nema.
        _onlineSince = online ? _onlineSince ?? DateTime.UtcNow : null;
        bool waitingForServers = online && _channelsKey.Length == 0;
        bool noChannels = waitingForServers && DateTime.UtcNow - _onlineSince > TimeSpan.FromSeconds(3);
        StatusText.Text = ErrorText(_bot.Error, _bot.ErrorDetail)
            ?? (!DiscordBot.IsSupported ? Loc.T("discord.errorNotSupported") : null)
            ?? _message
            ?? (noChannels ? Loc.T("discord.noChannels") : string.Empty);

        bool tick = inChannel || waitingForServers;
        if (tick != _timer.IsEnabled)
        {
            _timer.IsEnabled = tick;
        }

        RenderLive();
    }

    private void RenderLive()
    {
        bool inChannel = _bot.State == DiscordBotState.InChannel;
        SendingText.Visibility = LevelBar.Visibility = inChannel ? Visibility.Visible : Visibility.Collapsed;
        if (inChannel)
        {
            SendingText.Text = DecisionText(_bot.Decision);
            LevelBar.Value = _bot.Decision == DiscordAudioDecision.Send ? _bot.LastPeak : 0;
        }
    }

    /// <summary>Popis glasovnih kanala (kanal, ispod server); puni se ponovno samo kad se popis promijeni.</summary>
    private void FillChannels()
    {
        IReadOnlyList<DiscordVoiceChannel> channels = _bot.VoiceChannels();
        string key = string.Join("|", channels.Select(c => $"{c.GuildId}:{c.ChannelId}:{c.ChannelName}"));
        if (key != _channelsKey)
        {
            _channelsKey = key;
            ChannelList.ItemsSource = channels;
        }
    }

    /// <summary>Dijagnostika: kanali na popisu.</summary>
    internal IReadOnlyList<DiscordVoiceChannel> Channels => ChannelList.ItemsSource as IReadOnlyList<DiscordVoiceChannel> ?? [];

    // --- Postavljanje ----------------------------------------------------------------------------------------------------

    private void OnOpenPortal(object sender, RoutedEventArgs e) => OpenUrl(DiscordBot.PortalUrl);

    private async void OnSaveToken(object sender, RoutedEventArgs e) => await SaveTokenAsync(TokenBox.Password);

    private async void OnTokenKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await SaveTokenAsync(TokenBox.Password);
        }
    }

    /// <summary>Sprema token (provjeri oblik) i odmah spaja bota; prazno polje pri promjeni uklanja token.</summary>
    internal async Task SaveTokenAsync(string text)
    {
        string token = text.Trim();
        if (token.Length == 0 && _editingToken && _bot.HasToken)
        {
            await _bot.DisconnectAsync();
            _bot.SetToken(null);
            TokenBox.Clear();
            _editingToken = false;
            _message = Loc.T("discord.tokenRemoved");
            Render();
            return;
        }

        if (!DiscordBot.LooksLikeToken(token))
        {
            _message = Loc.T("discord.badTokenShape");
            Render();
            return;
        }

        if (_bot.IsActive)
        {
            await _bot.DisconnectAsync();
        }

        _bot.SetToken(token);
        TokenBox.Clear();
        _editingToken = false;
        _message = null;
        Render();
        await ConnectAsync();
    }

    private void OnChangeToken(object sender, RoutedEventArgs e)
    {
        _editingToken = true;
        _message = null;
        TokenBox.Clear();
        Render();
        TokenBox.Focus();
    }

    // --- Bot i kanal -----------------------------------------------------------------------------------------------------

    private async void OnConnect(object sender, RoutedEventArgs e) => await ConnectAsync();

    internal async Task ConnectAsync()
    {
        _message = null;
        await _bot.ConnectAsync();
        Render();
    }

    private async void OnDisconnect(object sender, RoutedEventArgs e)
    {
        _message = null;
        await _bot.DisconnectAsync();
        Render();
    }

    private async void OnJoinMine(object sender, RoutedEventArgs e) => await JoinMineAsync();

    /// <summary>„Uđi u moj kanal”: kanal u kojem je vlasnik bota (ili netko drugi ako vlasnik nije u kanalu).</summary>
    internal async Task<bool> JoinMineAsync()
    {
        _message = null;
        if (_bot.ChannelWithMember() is not { } channel)
        {
            _message = Loc.T("discord.noMemberChannel");
            Render();
            return false;
        }

        bool joined = await _bot.JoinAsync(channel.GuildId, channel.ChannelId);
        Render();
        return joined;
    }

    private async void OnJoinChannel(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: DiscordVoiceChannel channel })
        {
            _message = null;
            await _bot.JoinAsync(channel.GuildId, channel.ChannelId);
            Render();
        }
    }

    private async void OnLeave(object sender, RoutedEventArgs e)
    {
        await _bot.LeaveAsync();
        Render();
    }

    private void OnInvite(object sender, RoutedEventArgs e)
    {
        if (_bot.InviteUrl is { } url)
        {
            OpenUrl(url);
        }
    }

    private static void OpenUrl(string url)
    {
        if (url.StartsWith("https://", StringComparison.Ordinal))
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
    }
}

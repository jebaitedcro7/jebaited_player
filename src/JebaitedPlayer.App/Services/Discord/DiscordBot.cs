using System.IO;
using System.Net;
using System.Threading.Channels;
using JebaitedPlayer.Infrastructure;
using NetCord;
using NetCord.Gateway;
using NetCord.Gateway.Compression;
using NetCord.Gateway.Voice;
using NetCord.Logging;
using NetCord.Rest;

namespace JebaitedPlayer.Services.Discord;

public enum DiscordBotState
{
    NoToken,
    Offline,
    Connecting,
    Online,
    Joining,
    InChannel,
    Error,
}

public enum DiscordBotError
{
    None,
    BadToken,
    Network,
    NotSupported,
    JoinFailed,
    NoPermission,
    ChannelFull,
    Other,
}

public sealed record DiscordVoiceChannel(ulong GuildId, string GuildName, ulong ChannelId, string ChannelName);

/// <summary>
/// V4-B: vlastiti Discord bot korisnika (NetCord + DAVE preko libdave s OpenSSL 3).
/// Šalje samo zvuk playera (process loopback), i to samo lokalnu glazbu i radio (DiscordAudioGate).
/// Token je kriptiran (DPAPI) u discord-bot.dat i nikad ne ide u log, postavke ni izvoz.
/// </summary>
internal sealed class DiscordBot : IDisposable
{
    /// <summary>View Channel (1 &lt;&lt; 10), Connect (1 &lt;&lt; 20) i Speak (1 &lt;&lt; 21): jedine dozvole koje bot treba (View Channel i za privatne kanale).</summary>
    public const long Permissions = (1L << 10) | (1L << 20) | (1L << 21);
    public const string PortalUrl = "https://discord.com/developers/applications";
    private static readonly byte[] OpusSilence = [0xF8, 0xFF, 0xFE];

    private readonly string _tokenFile;
    private readonly bool _persist;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _token;
    private GatewayClient? _client;
    private VoiceClient? _voice;
    private Stream? _voiceStream;
    private ulong _guildId;
    private ProcessAudioCapture? _capture;
    private OpusFramer? _framer;
    private Channel<ReadOnlyMemory<byte>>? _packets;
    private CancellationTokenSource? _pumpCts;
    private Task? _pump;
    private volatile bool _sending;
    private DiscordAudioInputs _inputs;
    private readonly object _inputsLock = new();

    public DiscordBot(string dataDir, bool persist = true)
    {
        _tokenFile = Path.Combine(dataDir, "discord-bot.dat");
        _persist = persist;
        if (File.Exists(_tokenFile))
        {
            try
            {
                _token = Secret.Unprotect(File.ReadAllText(_tokenFile));
            }
            catch (IOException ex)
            {
                Log.Warn($"Discord token nije pročitan: {ex.Message}");
            }
        }

        State = HasToken ? DiscordBotState.Offline : DiscordBotState.NoToken;
    }

    public event EventHandler? Changed;

    public DiscordBotState State { get; private set; }

    public DiscordBotError Error { get; private set; }

    public string? ErrorDetail { get; private set; }

    public bool HasToken => _token is { Length: > 0 };

    public string? BotName { get; private set; }

    public ulong ApplicationId { get; private set; }

    public DiscordVoiceChannel? Channel { get; private set; }

    /// <summary>Zadnja odluka o slanju (za prikaz: „šalje glazbu” ili zašto je tišina).</summary>
    public DiscordAudioDecision Decision { get; private set; } = DiscordAudioDecision.SilenceNothingPlaying;

    public int LastPeak => _capture?.LastPeak ?? 0;

    private double _sendGain = 1;

    /// <summary>2.1.12: pojačanje za Discord (<see cref="DiscordVolume.Gain"/>); 1 = Discord čuje točno ono što player svira.</summary>
    public double SendGain
    {
        get => _sendGain;
        set
        {
            _sendGain = value;
            if (_capture is { } capture)
            {
                capture.Gain = value;
            }
        }
    }

    /// <summary>Format hvatanja ("float32" ili "pcm16"); prazno dok se ne šalje.</summary>
    public string CaptureFormat => _capture?.FormatName ?? string.Empty;

    public long PacketsSent { get; private set; }

    /// <summary>Poveznica kojom korisnik poziva svog bota na server (samo Connect i Speak).</summary>
    public string? InviteUrl => ApplicationId == 0 ? null
        : $"https://discord.com/oauth2/authorize?client_id={ApplicationId}&scope=bot&permissions={Permissions}";

    /// <summary>Stanje playera; postavlja MainWindow (s UI dretve), čita dretva za hvatanje zvuka (pod bravom, nikad napola).</summary>
    public DiscordAudioInputs Inputs
    {
        get
        {
            lock (_inputsLock)
            {
                return _inputs;
            }
        }

        set
        {
            lock (_inputsLock)
            {
                _inputs = value;
            }
        }
    }

    /// <summary>Samo za testove: zamjena za pravi zvuk (npr. bez Discorda).</summary>
    internal Action<ReadOnlyMemory<byte>>? PacketSink { get; set; }

    /// <summary>Korisnički ID samog bota (iz Ready), da se on ne računa kao „netko u kanalu”.</summary>
    public ulong BotUserId { get; private set; }

    /// <summary>Vlasnik aplikacije bota (korisnik playera), da bot ulazi u njegov kanal; 0 ako nije poznat.</summary>
    public ulong OwnerUserId { get; private set; }

    /// <summary>Samo za dijagnostiku: najniža razina NetCord poruka (zadano samo upozorenja i greške).</summary>
    internal NetCord.Logging.LogLevel NetCordLogLevel { get; set; } = NetCord.Logging.LogLevel.Warning;

    /// <summary>Samo za dijagnostiku: kopija NetCord poruka (token je iz njih uklonjen).</summary>
    internal Action<string>? LogSink { get; set; }

    public static bool IsSupported => ProcessAudioCapture.IsSupported;

    /// <summary>
    /// Oblik tokena bota: 50–100 znakova bez razmaka, tri neprazna dijela odvojena točkom, a prvi dio je base64 ID-a bota
    /// (samo znamenke), isto kao što provjerava NetCord. Je li token važeći zna samo Discord.
    /// </summary>
    public static bool LooksLikeToken(string token)
    {
        if (token.Length is < 50 or > 100 || token.Any(char.IsWhiteSpace))
        {
            return false;
        }

        string[] parts = token.Split('.');
        return parts.Length == 3 && parts.All(p => p.Length > 0) && IsBotIdPart(parts[0]);
    }

    private static bool IsBotIdPart(string part)
    {
        string b64 = part.Replace('-', '+').Replace('_', '/');
        b64 = b64.PadRight(b64.Length + ((4 - (b64.Length % 4)) % 4), '=');
        Span<byte> bytes = stackalloc byte[b64.Length];
        if (!Convert.TryFromBase64String(b64, bytes, out int length) || length is < 15 or > 20)
        {
            return false;
        }

        ReadOnlySpan<byte> digits = bytes[..length];
        return digits.IndexOfAnyExceptInRange((byte)'0', (byte)'9') < 0 && digits[0] != (byte)'0';
    }

    public void SetToken(string? token)
    {
        _token = string.IsNullOrWhiteSpace(token) ? null : token.Trim();
        if (_persist)
        {
            try
            {
                if (_token is null)
                {
                    File.Delete(_tokenFile);
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_tokenFile)!);
                    File.WriteAllText(_tokenFile, Secret.Protect(_token) ?? string.Empty);
                }
            }
            catch (IOException ex)
            {
                Log.Warn($"Discord token nije spremljen: {ex.Message}");
            }
        }

        SetState(HasToken ? DiscordBotState.Offline : DiscordBotState.NoToken);
    }

    /// <summary>Prijava bota: provjera tokena (REST), zatim gateway dok ne stigne Ready.</summary>
    public async Task<bool> ConnectAsync(CancellationToken ct = default)
    {
        if (!HasToken)
        {
            SetState(DiscordBotState.NoToken);
            return false;
        }

        await _gate.WaitAsync(ct);
        try
        {
            if (State is DiscordBotState.Online or DiscordBotState.InChannel)
            {
                return true;
            }

            SetState(DiscordBotState.Connecting);
            BotToken token;
            try
            {
                token = new BotToken(_token!);
            }
            catch (ArgumentException)
            {
                // NetCord odbija token koji nije oblika bota (npr. Client Secret); to je kriv token, ne greška programa.
                Fail(DiscordBotError.BadToken, null);
                return false;
            }

            try
            {
                using var rest = new RestClient(token, new RestClientConfiguration());
                CurrentUser me = await rest.GetCurrentUserAsync(cancellationToken: ct);
                BotName = me.Username;
                try
                {
                    CurrentApplication app = await rest.GetCurrentBotApplicationInformationAsync(cancellationToken: ct);
                    OwnerUserId = app.Team?.OwnerId ?? app.Owner?.Id ?? 0;
                }
                catch (RestException ex)
                {
                    Log.Warn($"Discord: vlasnik aplikacije nije pročitan ({(int)ex.StatusCode}).");
                }
            }
            catch (RestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                Fail(DiscordBotError.BadToken, null);
                return false;
            }
            catch (HttpRequestException ex)
            {
                Fail(DiscordBotError.Network, ex.Message);
                return false;
            }

            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _client = new GatewayClient(token, new GatewayClientConfiguration
            {
                Intents = GatewayIntents.Guilds | GatewayIntents.GuildVoiceStates,
                Compression = new UncompressedGatewayCompression(),
                Logger = new NetCordLog(this),
            });
            _client.Ready += e =>
            {
                ApplicationId = e.ApplicationId;
                BotUserId = e.User.Id;
                ready.TrySetResult();
                return default;
            };
            _client.GuildCreate += _ =>
            {
                Raise();
                return default;
            };
            _client.Disconnect += e =>
            {
                if (!e.Reconnect)
                {
                    ready.TrySetException(new InvalidOperationException($"{e.CloseStatus} {e.CloseStatusDescription}"));
                    Log.Warn($"Discord: veza zatvorena ({e.CloseStatus} {e.CloseStatusDescription}).");
                }

                return default;
            };

            await _client.StartAsync(cancellationToken: ct);
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(20), ct);
            Log.Info($"Discord: bot je prijavljen ({BotName}).");
            SetState(DiscordBotState.Online);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            Fail(ex is TimeoutException or HttpRequestException ? DiscordBotError.Network : DiscordBotError.Other, ex.Message);
            await CloseClientAsync();
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Glasovni kanali na serverima na koje je bot pozvan.</summary>
    public IReadOnlyList<DiscordVoiceChannel> VoiceChannels()
    {
        if (_client is null)
        {
            return [];
        }

        return _client.Cache.Guilds.Values
            .OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
            .SelectMany(g => g.Channels.Values.OfType<VoiceGuildChannel>()
                .OrderBy(c => c.Position)
                .Select(c => new DiscordVoiceChannel(g.Id, g.Name, c.Id, c.Name)))
            .ToList();
    }

    /// <summary>
    /// Glasovni kanal za „uđi u moj kanal”: prvo kanal u kojem je vlasnik bota, a ako ga nema, kanal u kojem je
    /// bilo tko drugi (ne bot); null ako nikoga nema.
    /// </summary>
    public DiscordVoiceChannel? ChannelWithMember() =>
        OwnerVoiceChannel() ?? FindVoiceChannel(v => v.UserId != BotUserId && v.User?.IsBot != true);

    /// <summary>Glasovni kanal u kojem je sada vlasnik bota; null ako nije ni u jednom.</summary>
    public DiscordVoiceChannel? OwnerVoiceChannel() =>
        OwnerUserId == 0 ? null : FindVoiceChannel(v => v.UserId == OwnerUserId);

    private DiscordVoiceChannel? FindVoiceChannel(Func<VoiceState, bool> who)
    {
        if (_client is null)
        {
            return null;
        }

        foreach (Guild g in _client.Cache.Guilds.Values.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            foreach (VoiceState v in g.VoiceStates.Values)
            {
                if (v.ChannelId is ulong id && who(v)
                    && g.Channels.TryGetValue(id, out IGuildChannel? channel) && channel is VoiceGuildChannel voice)
                {
                    return new DiscordVoiceChannel(g.Id, g.Name, id, voice.Name);
                }
            }
        }

        return null;
    }

    /// <summary>Stvarne dozvole bota u kanalu (uloge i prepisivanja dozvola kanala); null ako server nije u cacheu.</summary>
    internal NetCord.Permissions? BotChannelPermissions(ulong guildId, ulong channelId)
    {
        if (_client is null || !_client.Cache.Guilds.TryGetValue(guildId, out Guild? g) || !g.Users.TryGetValue(BotUserId, out GuildUser? me))
        {
            return null;
        }

        return me.GetChannelPermissions(g, channelId);
    }

    /// <summary>
    /// Zašto bot ne može ući i govoriti u kanalu: NoPermission (detalj = nedostajuće dozvole, imena kao u Discordu)
    /// ili ChannelFull; null ako može.
    /// </summary>
    internal (DiscordBotError Error, string? Detail)? JoinProblem(ulong guildId, ulong channelId)
    {
        if (BotChannelPermissions(guildId, channelId) is not { } p || p.HasFlag(NetCord.Permissions.Administrator))
        {
            return null;
        }

        var missing = new List<string>();
        (NetCord.Permissions Flag, string Name)[] needed =
            [(NetCord.Permissions.ViewChannel, "View Channel"), (NetCord.Permissions.Connect, "Connect"), (NetCord.Permissions.Speak, "Speak")];
        foreach ((NetCord.Permissions flag, string name) in needed)
        {
            if (!p.HasFlag(flag))
            {
                missing.Add(name);
            }
        }

        if (missing.Count > 0)
        {
            return (DiscordBotError.NoPermission, string.Join(", ", missing));
        }

        Guild g = _client!.Cache.Guilds[guildId];
        if (g.Channels.TryGetValue(channelId, out IGuildChannel? c) && c is VoiceGuildChannel { UserLimit: > 0 } voice
            && g.VoiceStates.Values.Count(v => v.ChannelId == channelId) >= voice.UserLimit && !p.HasFlag(NetCord.Permissions.MoveUsers))
        {
            return (DiscordBotError.ChannelFull, null);
        }

        return null;
    }

    /// <summary>Ulazak u glasovni kanal i početak slanja zvuka playera (samo glazba i radio).</summary>
    public async Task<bool> JoinAsync(ulong guildId, ulong channelId, CancellationToken ct = default)
    {
        if (!IsSupported)
        {
            Fail(DiscordBotError.NotSupported, null);
            return false;
        }

        if (State != DiscordBotState.Online && !await ConnectAsync(ct))
        {
            return false;
        }

        await _gate.WaitAsync(ct);
        try
        {
            SetState(DiscordBotState.Joining);
            DiscordVoiceChannel? target = VoiceChannels().FirstOrDefault(c => c.GuildId == guildId && c.ChannelId == channelId);

            // Bez dozvole Discord ne odgovara nego šuti, pa bi ulazak samo istekao; zato se provjerava unaprijed.
            if (JoinProblem(guildId, channelId) is { } problem)
            {
                Fail(problem.Error, problem.Detail);
                SetState(DiscordBotState.Online, keepError: true);
                return false;
            }
            _voice = await _client!.JoinVoiceChannelAsync(guildId, channelId, new VoiceClientConfiguration { Logger = new NetCordLog(this) },
                cancellationToken: ct);
            _guildId = guildId;
            await _voice.StartAsync(ct);
            await _voice.EnterSpeakingStateAsync(new SpeakingProperties(SpeakingFlags.Microphone), cancellationToken: ct);
            _voiceStream = _voice.CreateVoiceStream(new VoiceStreamConfiguration { NormalizeSpeed = true });
            StartAudio(packet => _voiceStream.WriteAsync(packet));
            Channel = target ?? new DiscordVoiceChannel(guildId, string.Empty, channelId, string.Empty);
            Log.Info($"Discord: bot je u kanalu {Channel.ChannelName} ({Channel.GuildName}).");
            SetState(DiscordBotState.InChannel);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            await StopAudioAsync();
            await CloseVoiceAsync();
            Fail(DiscordBotError.JoinFailed, ex.Message);
            if (_client is not null)
            {
                SetState(DiscordBotState.Online, keepError: true);
            }

            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Samo za testove: hvatanje i kodiranje bez Discorda (paketi idu u PacketSink).</summary>
    internal void StartLocalAudioForTests()
    {
        StartAudio(packet =>
        {
            PacketSink?.Invoke(packet);
            return ValueTask.CompletedTask;
        });
    }

    internal Task StopLocalAudioForTests() => StopAudioAsync();

    public async Task LeaveAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await StopAudioAsync();
            await CloseVoiceAsync();
            if (_client is not null && _guildId != 0)
            {
                try
                {
                    await _client.UpdateVoiceStateAsync(new VoiceStateProperties(_guildId, null));
                }
                catch (Exception ex)
                {
                    Log.Warn($"Discord: izlazak iz kanala nije javljen ({ex.Message}).");
                }
            }

            _guildId = 0;
            Channel = null;
            SetState(_client is null ? (HasToken ? DiscordBotState.Offline : DiscordBotState.NoToken) : DiscordBotState.Online);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DisconnectAsync()
    {
        await LeaveAsync();
        await CloseClientAsync();
        SetState(HasToken ? DiscordBotState.Offline : DiscordBotState.NoToken);
    }

    /// <summary>Uredno zatvaranje (izlazak iz kanala i odjava), za zatvaranje playera; čeka najviše timeout.</summary>
    public async Task CloseAsync(TimeSpan timeout)
    {
        if (!IsActive)
        {
            return;
        }

        try
        {
            await DisconnectAsync().WaitAsync(timeout);
        }
        catch (Exception ex)
        {
            Log.Warn($"Discord: zatvaranje ({ex.Message}).");
        }
    }

    /// <summary>Bot je spojen, ulazi u kanal ili šalje zvuk.</summary>
    public bool IsActive => _client is not null || _voice is not null || _capture is not null;

    /// <summary>
    /// Ne čeka: sinkrono čekanje s UI dretve moglo bi zastati jer nastavci trebaju istu dretvu. Uredno zatvaranje je
    /// CloseAsync; ovdje se, ako je bot još aktivan, odjava samo pokrene u pozadini.
    /// </summary>
    public void Dispose()
    {
        if (IsActive)
        {
            _ = DisconnectAsync().ContinueWith(t => Log.Warn($"Discord: zatvaranje ({t.Exception?.GetBaseException().Message})."),
                TaskContinuationOptions.OnlyOnFaulted);
        }
    }

    private void StartAudio(Func<ReadOnlyMemory<byte>, ValueTask> send)
    {
        _packets = System.Threading.Channels.Channel.CreateBounded<ReadOnlyMemory<byte>>(
            new BoundedChannelOptions(25) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true, SingleWriter = true });
        _framer = new OpusFramer(p => _packets.Writer.TryWrite(p));
        _pumpCts = new CancellationTokenSource();
        CancellationToken token = _pumpCts.Token;
        ChannelReader<ReadOnlyMemory<byte>> reader = _packets.Reader;
        _pump = Task.Run(async () =>
        {
            try
            {
                await foreach (ReadOnlyMemory<byte> packet in reader.ReadAllAsync(token))
                {
                    await send(packet);
                    PacketsSent++;
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Log.Warn($"Discord: slanje zvuka je prekinuto ({ex.Message}).");
            }
        }, token);
        _capture = new ProcessAudioCapture(OnSamples) { Gain = _sendGain };
        _capture.Start();
    }

    private void OnSamples(ReadOnlySpan<short> samples)
    {
        DiscordAudioDecision decision = DiscordAudioGate.Decide(Inputs);
        if (decision != Decision)
        {
            Decision = decision;
            Raise();
        }

        if (decision == DiscordAudioDecision.Send)
        {
            _sending = true;
            _framer?.Push(samples);
        }
        else if (_sending)
        {
            // Prijelaz na tišinu: pet Opus okvira tišine, da Discord ne „dopunjava” zvuk.
            _sending = false;
            _framer?.Reset();
            for (int i = 0; i < 5; i++)
            {
                _packets?.Writer.TryWrite(OpusSilence);
            }
        }
    }

    private async Task StopAudioAsync()
    {
        _capture?.Dispose();
        _capture = null;
        _packets?.Writer.TryComplete();
        if (_pump is not null)
        {
            try
            {
                await _pump.WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch (Exception)
            {
                _pumpCts?.Cancel();
            }
        }

        _pumpCts?.Dispose();
        _pumpCts = null;
        _pump = null;
        _packets = null;
        _framer = null;
        _sending = false;
    }

    private async Task CloseVoiceAsync()
    {
        _voiceStream?.Dispose();
        _voiceStream = null;
        if (_voice is not null)
        {
            try
            {
                await _voice.CloseAsync();
            }
            catch (Exception ex)
            {
                Log.Warn($"Discord: glasovna veza ({ex.Message}).");
            }

            _voice.Dispose();
            _voice = null;
        }
    }

    private async Task CloseClientAsync()
    {
        if (_client is not null)
        {
            try
            {
                await _client.CloseAsync();
            }
            catch (Exception ex)
            {
                Log.Warn($"Discord: zatvaranje veze ({ex.Message}).");
            }

            _client.Dispose();
            _client = null;
        }
    }

    private void Fail(DiscordBotError error, string? detail)
    {
        Error = error;
        ErrorDetail = detail;
        Log.Warn($"Discord: {error} {detail}");
        SetState(DiscordBotState.Error, keepError: true);
    }

    private void SetState(DiscordBotState state, bool keepError = false)
    {
        State = state;
        if (!keepError)
        {
            Error = DiscordBotError.None;
            ErrorDetail = null;
        }

        Raise();
    }

    private void Raise() => Changed?.Invoke(this, EventArgs.Empty);

    /// <summary>NetCord poruke u dnevnik playera (zadano samo upozorenja i greške); token se iz teksta uvijek uklanja.</summary>
    private sealed class NetCordLog(DiscordBot bot) : IGatewayLogger, IVoiceLogger
    {
        public bool IsEnabled(NetCord.Logging.LogLevel logLevel) => logLevel >= bot.NetCordLogLevel;

        public void Log<TState>(NetCord.Logging.LogLevel logLevel, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            string text = formatter(state, exception);
            if (exception is not null && !text.Contains(exception.Message, StringComparison.Ordinal))
            {
                text += $" ({exception.GetType().Name}: {exception.Message})";
            }

            if (bot._token is { Length: > 0 } secret)
            {
                text = text.Replace(secret, "[token]", StringComparison.Ordinal);
            }

            string line = $"Discord (NetCord {logLevel}): {text}";
            if (logLevel >= NetCord.Logging.LogLevel.Warning)
            {
                Infrastructure.Log.Warn(line);
            }
            else
            {
                Infrastructure.Log.Info(line);
            }

            bot.LogSink?.Invoke(line);
        }
    }
}

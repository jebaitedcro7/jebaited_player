using Concentus;
using Concentus.Enums;

namespace JebaitedPlayer.Services.Discord;

/// <summary>Što bot smije poslati: samo lokalnu glazbu i radio. Filmovi, YouTube i Spotify nikad.</summary>
public enum DiscordAudioDecision
{
    Send,
    SilenceNothingPlaying,
    SilencePaused,
    SilenceVideo,
    SilenceYouTube,
    SilenceSpotify,
}

/// <param name="HasMedia">Player ima otvorenu datoteku ili postaju.</param>
/// <param name="IsPaused">Lokalna reprodukcija je pauzirana.</param>
/// <param name="HasVideo">Otvorena datoteka ima sliku (film, spot); radio i glazba nemaju.</param>
/// <param name="YouTubeOpen">Prozor YouTube Musica je otvoren (njegov zvuk je u istom stablu procesa).</param>
/// <param name="SpotifyActive">Spotify je izvor (svira u Spotifyju, ali lokalno se ništa ne šalje).</param>
public readonly record struct DiscordAudioInputs(bool HasMedia, bool IsPaused, bool HasVideo, bool YouTubeOpen, bool SpotifyActive);

public static class DiscordAudioGate
{
    public static DiscordAudioDecision Decide(DiscordAudioInputs s)
    {
        // YouTube prvo: dok je prozor otvoren, njegov zvuk bi se uhvatio zajedno s playerom (uvjeti YouTubea to ne dopuštaju).
        if (s.YouTubeOpen)
        {
            return DiscordAudioDecision.SilenceYouTube;
        }

        if (s.SpotifyActive)
        {
            return DiscordAudioDecision.SilenceSpotify;
        }

        if (!s.HasMedia)
        {
            return DiscordAudioDecision.SilenceNothingPlaying;
        }

        if (s.HasVideo)
        {
            return DiscordAudioDecision.SilenceVideo;
        }

        return s.IsPaused ? DiscordAudioDecision.SilencePaused : DiscordAudioDecision.Send;
    }
}

/// <summary>
/// 2.1.12: glasnoća na Discordu. Zvuk se hvata iz izlaza playera, dakle nakon njegove glasnoće (mpv: (glasnoća/100)³).
/// Kad Discord ne prati player, uhvaćeni zvuk se pojača ili utiša tako da u kanalu zvuči kao da player svira na
/// glasnoći za Discord. Utišan player (ili glasnoća 0) i dalje šalje tišinu: iz izlaza se ne može dobiti zvuk koji
/// player ne pušta.
/// </summary>
public static class DiscordVolume
{
    /// <summary>Najveće pojačanje (+40 dB); ispod toga, npr. player na 3 %, Discord postaje tiši.</summary>
    public const double MaxGain = 100;

    public const double DefaultVolume = 60;

    /// <summary>Pojačanje za uhvaćene uzorke: 1 kad Discord prati player.</summary>
    public static double Gain(bool followPlayer, double playerVolume, bool playerMuted, double discordVolume)
    {
        if (followPlayer)
        {
            return 1;
        }

        double player = Cubic(playerVolume);
        if (playerMuted || player <= 0)
        {
            return 0;
        }

        return Math.Min(Cubic(Math.Clamp(discordVolume, 0, 100)) / player, MaxGain);
    }

    /// <summary>mpv glasnoća 0–100 (i više) → linearno pojačanje.</summary>
    public static double Cubic(double volume)
    {
        double v = Math.Max(0, volume) / 100;
        return v * v * v;
    }

    /// <summary>Float uzorci (-1..1) → 16 bita uz pojačanje i rezanje na granicama; vraća najveću apsolutnu vrijednost.</summary>
    public static int ToPcm16(ReadOnlySpan<float> input, Span<short> output, double gain)
    {
        int peak = 0;
        float g = (float)gain;
        for (int i = 0; i < input.Length; i++)
        {
            float v = input[i] * g * 32767f;
            int s = float.IsNaN(v) ? 0 : (int)MathF.Round(Math.Clamp(v, -32768f, 32767f));
            output[i] = (short)s;
            int a = s == short.MinValue ? short.MaxValue : Math.Abs(s);
            if (a > peak)
            {
                peak = a;
            }
        }

        return peak;
    }

    /// <summary>16-bitni uzorci (kad Windows ne da float) uz pojačanje i rezanje; vraća najveću apsolutnu vrijednost.</summary>
    public static int ApplyGain(Span<short> samples, double gain)
    {
        int peak = 0;
        bool unity = Math.Abs(gain - 1) < 1e-9;
        for (int i = 0; i < samples.Length; i++)
        {
            int s = unity ? samples[i] : (int)Math.Round(Math.Clamp(samples[i] * gain, short.MinValue, short.MaxValue));
            samples[i] = (short)s;
            int a = s == short.MinValue ? short.MaxValue : Math.Abs(s);
            if (a > peak)
            {
                peak = a;
            }
        }

        return peak;
    }
}

/// <summary>PCM 48 kHz stereo (16 bita) → Opus okviri od 20 ms (Concentus, bez nativnog opus.dll).</summary>
public sealed class OpusFramer
{
    public const int FrameSamplesPerChannel = 960; // 20 ms na 48 kHz
    public const int FrameSamples = FrameSamplesPerChannel * ProcessAudioCapture.Channels;
    private const int MaxPacket = 4000;

    private readonly IOpusEncoder _encoder;
    private readonly short[] _pcm = new short[FrameSamples];
    private readonly byte[] _packet = new byte[MaxPacket];
    private readonly Action<ReadOnlyMemory<byte>> _onPacket;
    private int _filled;

    public OpusFramer(Action<ReadOnlyMemory<byte>> onPacket, int bitrate = 96_000)
    {
        _onPacket = onPacket;
        _encoder = OpusCodecFactory.CreateEncoder(ProcessAudioCapture.SampleRate, ProcessAudioCapture.Channels, OpusApplication.OPUS_APPLICATION_AUDIO);
        _encoder.Bitrate = bitrate;
        _encoder.Complexity = 5;
        _encoder.UseVBR = true;
        _encoder.SignalType = OpusSignal.OPUS_SIGNAL_MUSIC;
    }

    public long PacketsEncoded { get; private set; }

    /// <summary>Dodaje uzorke; za svakih punih 20 ms pozove onPacket s kopijom Opus paketa.</summary>
    public void Push(ReadOnlySpan<short> samples)
    {
        while (!samples.IsEmpty)
        {
            int take = Math.Min(FrameSamples - _filled, samples.Length);
            samples[..take].CopyTo(_pcm.AsSpan(_filled));
            _filled += take;
            samples = samples[take..];
            if (_filled == FrameSamples)
            {
                int length = _encoder.Encode(_pcm, FrameSamplesPerChannel, _packet, MaxPacket);
                _filled = 0;
                if (length > 0)
                {
                    PacketsEncoded++;
                    _onPacket(_packet.AsSpan(0, length).ToArray());
                }
            }
        }
    }

    /// <summary>Odbacuje započeti okvir (npr. kad bot prijeđe na tišinu).</summary>
    public void Reset() => _filled = 0;
}

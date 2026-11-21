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

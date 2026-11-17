using System.Globalization;
using System.IO.Pipes;

namespace JebaitedPlayer.Mpv;

/// <summary>
/// Izvlači zvuk iz datoteke kao 16 kHz mono 16-bitni PCM, brže od stvarnog vremena.
/// <para>
/// Koristi drugi, nevidljivi libmpv (bez videa, bez prozora, bez korisničke konfiguracije) s izlazom <c>ao=pcm</c>
/// u imenovanu cijev (named pipe), pa nema privremenih datoteka ni dodatnih alata. Dekodiranje radi isti mpv/FFmpeg
/// kao i reprodukcija, pa su podržani svi formati koje player može pustiti.
/// </para>
/// </summary>
public static class MpvAudioExtractor
{
    public const int SampleRate = 16000;

    // Samo središnji kanal (FC). U višekanalnom zvuku (5.1, 7.1) dijalog je u središnjem kanalu, a glazba i efekti
    // u ostalima; miješanjem svih kanala detektor govora i glazbu i efekte smatra govorom (izmjereno: 75–78 % epizode).
    private const string DialogueFilter = "lavfi=[pan=mono|c0=FC]";

    /// <summary>
    /// Šalje uzorke u <paramref name="onSamples"/> (iz pozadinske dretve) dok ne dođe do kraja datoteke ili <paramref name="end"/>.
    /// <paramref name="audioTrack"/> je mpv ID audio trake ("1", "2"…) ili null za zadanu.
    /// Uz <see cref="AudioChannelMode.Dialogue"/> uzima se samo središnji kanal kad postoji; inače (stereo, mono) svi kanali.
    /// Napredak (0–1) javlja se prema trajanju datoteke.
    /// </summary>
    public static async Task<AudioExtraction> ExtractAsync(string path, string? audioTrack, double? start, double? end,
        Action<short[], int> onSamples, IProgress<double>? progress, CancellationToken ct,
        AudioChannelMode channels = AudioChannelMode.Mix)
    {
        // Filtar se smije uključiti samo kad središnji kanal postoji: na stereo zvuku FFmpeg ne javi grešku, nego da tišinu.
        string? layout = null;
        if (channels == AudioChannelMode.Dialogue)
        {
            layout = await ProbeChannelLayoutAsync(path, audioTrack, ct).ConfigureAwait(false);
        }

        bool dialogue = HasCenterChannel(layout);
        (long samples, bool filterDropped) = await ExtractCoreAsync(path, audioTrack, start, end, onSamples, progress, ct,
            dialogue ? DialogueFilter : null).ConfigureAwait(false);
        return new AudioExtraction(samples, dialogue && !filterDropped, layout);
    }

    /// <summary>
    /// Ima li raspored kanala (kako ga mpv opisuje, npr. "5.1(side)", "stereo", "fl-fr-fc-lfe") zaseban središnji kanal.
    /// Mono se ne računa: tu je ionako sve u jednom kanalu.
    /// </summary>
    public static bool HasCenterChannel(string? layout)
    {
        if (string.IsNullOrWhiteSpace(layout))
        {
            return false;
        }

        string l = layout.Trim().ToLowerInvariant();
        if (l.Split('-').Contains("fc"))
        {
            return true;
        }

        string[] withCenter =
        [
            "3.0", "4.0", "3.1", "5.0", "5.0(side)", "4.1", "5.1", "5.1(side)", "6.0", "hexagonal", "6.1", "6.1(back)",
            "7.0", "7.1", "7.1(wide)", "7.1(wide-side)", "7.1(top)", "octagonal", "hexadecagonal", "22.2",
            "5.1.2", "5.1.4", "7.1.2", "7.1.4", "7.2.3", "9.1.4", "9.1.6",
        ];
        return withCenter.Contains(l);
    }

    /// <summary>Raspored kanala odabrane audio trake (npr. "5.1(side)"), bez dekodiranja zvuka. Null ako nije poznat.</summary>
    public static async Task<string?> ProbeChannelLayoutAsync(string path, string? audioTrack, CancellationToken ct)
    {
        var player = new MpvPlayer(null);
        var result = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            player.SetOption("config", "no");
            player.SetOption("terminal", "no");
            player.TrySetOption("load-scripts", "no");
            player.TrySetOption("ytdl", "no");
            player.SetOption("input-default-bindings", "no");
            player.SetOption("idle", "yes");
            player.SetOption("pause", "yes");
            player.SetOption("vid", "no");
            player.SetOption("sid", "no");
            player.SetOption("ao", "null");
            player.SetOption("aid", audioTrack ?? "auto");

            // Dretva događaja smije sinkrono čitati svojstva (vidi MpvPlayer).
            player.FileLoaded += (_, _) => result.TrySetResult(
                player.GetPropertyString("current-tracks/audio/demux-channels") ?? player.GetPropertyString("audio-params/channels"));
            player.EndFile += (_, _) => result.TrySetResult(null);
            player.Shutdown += (_, _) => result.TrySetResult(null);
            player.Initialize();
            player.CommandAsync("loadfile", path, "replace");

            Task done = await Task.WhenAny(result.Task, Task.Delay(TimeSpan.FromSeconds(15), ct)).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            return done == result.Task ? await result.Task.ConfigureAwait(false) : null;
        }
        finally
        {
            await Task.Run(player.Dispose, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static async Task<(long Samples, bool FilterDropped)> ExtractCoreAsync(string path, string? audioTrack, double? start,
        double? end, Action<short[], int> onSamples, IProgress<double>? progress, CancellationToken ct, string? audioFilter)
    {
        string pipeName = "jebaited_pcm_" + Guid.NewGuid().ToString("N");
        var pipe = new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous, inBufferSize: 1 << 16, outBufferSize: 0);

        var player = new MpvPlayer(null);
        var ended = new TaskCompletionSource<MpvEndFileEventArgs?>(TaskCreationOptions.RunContinuationsAsynchronously);

        // mpv se gasi točno jednom: na kraju datoteke (time se zatvara audio izlaz pa čitanje dobije kraj cijevi)
        // ili kad prekinemo. Gašenje ide u pozadinskoj dretvi, nikad u dretvi događaja.
        Task disposed = ended.Task.ContinueWith(_ => player.Dispose(), CancellationToken.None,
            TaskContinuationOptions.None, TaskScheduler.Default);
        string? error = null;
        bool filterDropped = false;
        try
        {
            ConfigureHeadless(player, @"\\.\pipe\" + pipeName, audioTrack, start, end);
            if (audioFilter != null)
            {
                player.SetOption("af", audioFilter);
            }

            player.EndFile += (_, e) => ended.TrySetResult(e);
            player.Shutdown += (_, _) => ended.TrySetResult(null);
            player.LogMessage += (_, level, text) =>
            {
                if (text.Contains("Disabling filter", StringComparison.OrdinalIgnoreCase))
                {
                    filterDropped = true;
                }
                else if (level is "error" or "fatal" && audioFilter != null && text.Contains("lavfi", StringComparison.OrdinalIgnoreCase))
                {
                    // Greška filtra (nema središnjeg kanala); mpv ga isključi i nastavi bez njega.
                    filterDropped = true;
                }
                else if (level is "error" or "fatal")
                {
                    error ??= text;
                }
            };
            player.RequestLogMessages(audioFilter != null ? "warn" : "error");
            player.Initialize();
            player.CommandAsync("loadfile", path, "replace");

            // mpv otvara cijev tek kad pokrene audio izlaz; ako datoteka nema zvuka, cijev se nikad ne otvori.
            Task connect = pipe.WaitForConnectionAsync(ct);
            Task first = await Task.WhenAny(connect, ended.Task).ConfigureAwait(false);
            if (first != connect)
            {
                MpvEndFileEventArgs? e = await ended.Task.ConfigureAwait(false);
                throw new InvalidDataException(e?.ErrorText ?? error ?? "Datoteka nema zvuk koji se može analizirati.");
            }

            await connect.ConfigureAwait(false);

            // Datoteka je učitana (audio izlaz radi), pa je trajanje poznato. Poslije ovoga mpv više ne diramo.
            double? duration = player.GetPropertyDouble("duration");
            double span = duration is > 0 ? Math.Min(end ?? duration.Value, duration.Value) - (start ?? 0) : 0;

            long total = 0;
            byte[] buffer = new byte[1 << 16];
            short[] samples = new short[buffer.Length / 2];
            int carry = 0;
            while (true)
            {
                int read = await pipe.ReadAsync(buffer.AsMemory(carry, buffer.Length - carry), ct).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                int bytes = carry + read;
                int count = bytes / 2;
                Buffer.BlockCopy(buffer, 0, samples, 0, count * 2);
                carry = bytes - count * 2;
                if (carry > 0)
                {
                    buffer[0] = buffer[count * 2];
                }

                onSamples(samples, count);
                total += count;
                if (span > 0)
                {
                    progress?.Report(Math.Clamp(total / (double)SampleRate / span, 0, 1));
                }
            }

            return (total, filterDropped);
        }
        finally
        {
            // Prvo zatvaramo cijev: ako je mpv zapeo na pisanju (prekid), pisanje odmah pukne i mpv se može ugasiti.
            pipe.Dispose();
            ended.TrySetResult(null);
            await disposed.ConfigureAwait(false);
        }
    }

    private static void ConfigureHeadless(MpvPlayer p, string output, string? audioTrack, double? start, double? end)
    {
        // Bez korisničke konfiguracije, skripti i prozora: samo dekodiranje zvuka.
        p.SetOption("config", "no");
        p.SetOption("terminal", "no");
        p.TrySetOption("load-scripts", "no");
        p.TrySetOption("ytdl", "no");
        p.TrySetOption("osc", "no");
        p.SetOption("input-default-bindings", "no");
        p.SetOption("idle", "yes");
        p.SetOption("keep-open", "no");
        p.SetOption("vid", "no");
        p.SetOption("sid", "no");
        p.SetOption("audio-display", "no");
        p.TrySetOption("cover-art-auto", "no");
        p.SetOption("aid", audioTrack ?? "auto");
        p.SetOption("volume", "100");
        p.SetOption("mute", "no");
        p.TrySetOption("replaygain", "no");
        p.SetOption("audio-pitch-correction", "no");
        p.SetOption("speed", "1");

        // ao=pcm piše što brže može (nije vezan uz sat), u 16 kHz mono s16 bez WAV zaglavlja.
        p.SetOption("ao", "pcm");
        p.SetOption("ao-pcm-file", output);
        p.SetOption("ao-pcm-waveheader", "no");
        p.SetOption("audio-format", "s16");
        p.SetOption("audio-samplerate", SampleRate.ToString(CultureInfo.InvariantCulture));
        p.SetOption("audio-channels", "mono");

        if (start is > 0)
        {
            p.SetOption("start", start.Value.ToString("R", CultureInfo.InvariantCulture));
        }

        if (end is > 0)
        {
            p.SetOption("end", end.Value.ToString("R", CultureInfo.InvariantCulture));
        }
    }
}

/// <summary>Koji kanali zvuka idu u analizu govora.</summary>
public enum AudioChannelMode
{
    /// <summary>Svi kanali pomiješani u mono.</summary>
    Mix,

    /// <summary>Samo središnji kanal (dijalog) kad postoji, inače svi kanali.</summary>
    Dialogue,
}

/// <param name="Samples">Broj uzoraka (16 kHz).</param>
/// <param name="DialogueOnly">Analiziran je samo središnji kanal.</param>
/// <param name="ChannelLayout">Raspored kanala izvora (npr. "5.1(side)"), ako je provjeren.</param>
public sealed record AudioExtraction(long Samples, bool DialogueOnly, string? ChannelLayout = null);

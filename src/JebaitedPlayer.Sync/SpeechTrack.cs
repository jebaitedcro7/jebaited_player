using WebRtcVadSharp;

namespace JebaitedPlayer.Sync;

/// <summary>Gdje se u zvuku govori: jedna vrijednost (0/1) za svakih 10 ms.</summary>
public sealed class SpeechTrack
{
    public const int FramesPerSecond = 100;
    private const string Magic = "JPVAD1";
    private const string MagicWithStrict = "JPVAD2";

    private readonly byte[] _frames;
    private int[]? _prefix;

    public SpeechTrack(byte[] frames)
    {
        _frames = frames;
        int speech = 0;
        foreach (byte f in frames)
        {
            speech += f;
        }

        SpeechFrames = speech;
    }

    public int Length => _frames.Length;

    public double DurationSeconds => _frames.Length / (double)FramesPerSecond;

    public int SpeechFrames { get; }

    public double SpeechRatio => _frames.Length == 0 ? 0 : SpeechFrames / (double)_frames.Length;

    public ReadOnlySpan<byte> Frames => _frames;

    /// <summary>
    /// Isti zvuk, strožim detektorom (manje lažnog govora usred glazbe i buke). Koristi se kad rezultat s osnovnim
    /// detektorom nije čist, jer pomaci ondje često ostanu neodređeni.
    /// </summary>
    public SpeechTrack? Strict { get; init; }

    /// <summary>Isti zvuk uz priložen strožiji rezultat (<see cref="Strict"/>).</summary>
    public SpeechTrack WithStrict(SpeechTrack strict) => new(_frames) { Strict = strict };

    /// <summary>Kopija kružno pomaknuta za <paramref name="frames"/> okvira: zvuk koji sigurno ne odgovara titlovima (razina slučajnosti).</summary>
    internal SpeechTrack Rotate(int frames)
    {
        int n = _frames.Length;
        if (n == 0)
        {
            return this;
        }

        int shift = ((frames % n) + n) % n;
        var rotated = new byte[n];
        Array.Copy(_frames, shift, rotated, 0, n - shift);
        Array.Copy(_frames, 0, rotated, n - shift, shift);
        return new SpeechTrack(rotated);
    }

    /// <summary>Zbroj govornih okvira u [0, i); izvan raspona zvuka nema govora.</summary>
    internal int Prefix(int i)
    {
        _prefix ??= BuildPrefix();
        return _prefix[Math.Clamp(i, 0, _frames.Length)];
    }

    private int[] BuildPrefix()
    {
        var p = new int[_frames.Length + 1];
        for (int i = 0; i < _frames.Length; i++)
        {
            p[i + 1] = p[i] + _frames[i];
        }

        return p;
    }

    public void Save(Stream stream)
    {
        using var w = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true);
        w.Write(Strict is null ? Magic : MagicWithStrict);
        WriteFrames(w, _frames);
        if (Strict is not null)
        {
            WriteFrames(w, Strict._frames);
        }
    }

    public static SpeechTrack Load(Stream stream)
    {
        using var r = new BinaryReader(stream, System.Text.Encoding.ASCII, leaveOpen: true);
        string magic = r.ReadString();
        if (magic != Magic && magic != MagicWithStrict)
        {
            throw new InvalidDataException("Nepoznat format analize govora.");
        }

        byte[] frames = ReadFrames(r);
        return new SpeechTrack(frames) { Strict = magic == MagicWithStrict ? new SpeechTrack(ReadFrames(r)) : null };
    }

    private static void WriteFrames(BinaryWriter w, byte[] frames)
    {
        w.Write(frames.Length);
        byte[] packed = new byte[(frames.Length + 7) / 8];
        for (int i = 0; i < frames.Length; i++)
        {
            if (frames[i] != 0)
            {
                packed[i >> 3] |= (byte)(1 << (i & 7));
            }
        }

        w.Write(packed);
    }

    private static byte[] ReadFrames(BinaryReader r)
    {
        int length = r.ReadInt32();
        if (length is < 0 or > 100 * 60 * 60 * 24)
        {
            throw new InvalidDataException("Neispravna duljina analize govora.");
        }

        byte[] packed = r.ReadBytes((length + 7) / 8);
        if (packed.Length != (length + 7) / 8)
        {
            throw new InvalidDataException("Analiza govora je skraćena.");
        }

        byte[] frames = new byte[length];
        for (int i = 0; i < length; i++)
        {
            frames[i] = (byte)((packed[i >> 3] >> (i & 7)) & 1);
        }

        return frames;
    }
}

/// <summary>
/// Detektor govora (WebRTC VAD) za tok 16 kHz mono uzoraka. Uzorci se mogu slati u komadima bilo koje veličine.
/// </summary>
public sealed class SpeechDetector : IDisposable
{
    public const int SampleRate = 16000;
    private const int FrameSamples = SampleRate / SpeechTrack.FramesPerSecond;

    private readonly WebRtcVad _vad;
    private readonly short[] _frame = new short[FrameSamples];
    private int _fill;
    private byte[] _frames = new byte[100 * 60 * 30];
    private int _count;

    public SpeechDetector(OperatingMode mode = OperatingMode.Aggressive)
    {
        _vad = new WebRtcVad
        {
            SampleRate = WebRtcVadSharp.SampleRate.Is16kHz,
            FrameLength = FrameLength.Is10ms,
            OperatingMode = mode,
        };
    }

    /// <summary>Strožiji detektor (VeryAggressive): manje lažnog govora usred glazbe i buke.</summary>
    public static SpeechDetector CreateStrict() => new(OperatingMode.VeryAggressive);

    public int FrameCount => _count;

    public void Push(short[] samples, int count)
    {
        int i = 0;
        while (i < count)
        {
            int take = Math.Min(FrameSamples - _fill, count - i);
            Array.Copy(samples, i, _frame, _fill, take);
            _fill += take;
            i += take;
            if (_fill == FrameSamples)
            {
                Add(_vad.HasSpeech(_frame) ? (byte)1 : (byte)0);
                _fill = 0;
            }
        }
    }

    public SpeechTrack Finish() => new(_frames.AsSpan(0, _count).ToArray());

    public void Dispose() => _vad.Dispose();

    private void Add(byte value)
    {
        if (_count == _frames.Length)
        {
            Array.Resize(ref _frames, _frames.Length * 2);
        }

        _frames[_count++] = value;
    }
}

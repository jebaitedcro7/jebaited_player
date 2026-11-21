using System.Runtime.InteropServices;
using System.Threading;

namespace JebaitedPlayer.Services.Discord;

/// <summary>
/// V4-B: hvatanje zvuka samo ovog procesa (i njegovih podprocesa) preko WASAPI „process loopback”, bez nove biblioteke.
/// Daje PCM 48 kHz, stereo, 16 bita. Mikrofon i ostali programi se ne hvataju.
/// Treba Windows 11 / Windows 10 build 20348 ili noviji.
/// </summary>
public sealed class ProcessAudioCapture : IDisposable
{
    public const int SampleRate = 48000;
    public const int Channels = 2;
    public const int MinimumBuild = 20348;

    private readonly Action<ReadOnlySpan<short>> _onSamples;
    private Thread? _thread;
    private volatile bool _stop;
    private readonly ManualResetEventSlim _started = new(false);
    private Exception? _startError;

    public ProcessAudioCapture(Action<ReadOnlySpan<short>> onSamples) => _onSamples = onSamples;

    public static bool IsSupported => OperatingSystem.IsWindows() && Environment.OSVersion.Version.Build >= MinimumBuild;

    /// <summary>Najveća apsolutna vrijednost uzorka u zadnjem paketu (0–32767), za testove i pokazivač razine.</summary>
    public int LastPeak { get; private set; }

    public long FramesCaptured { get; private set; }

    public bool IsRunning => _thread is { IsAlive: true };

    /// <summary>Pokreće hvatanje na vlastitoj MTA dretvi; baca grešku ako aktivacija ne uspije.</summary>
    public void Start()
    {
        if (!IsSupported)
        {
            throw new PlatformNotSupportedException("Process loopback needs Windows build 20348 or newer.");
        }

        _stop = false;
        _started.Reset();
        _thread = new Thread(Run) { IsBackground = true, Name = "jp-process-loopback" };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
        if (!_started.Wait(TimeSpan.FromSeconds(10)))
        {
            _stop = true;
            throw new TimeoutException("Audio capture did not start.");
        }

        if (_startError is { } e)
        {
            throw e;
        }
    }

    public void Stop()
    {
        _stop = true;
        _thread?.Join(2000);
        _thread = null;
    }

    public void Dispose()
    {
        Stop();
        _started.Dispose();
    }

    private void Run()
    {
        IAudioClient? client = null;
        IAudioCaptureClient? capture = null;
        IntPtr evt = IntPtr.Zero;
        try
        {
            client = Activate((uint)Environment.ProcessId);
            var format = new WaveFormatEx
            {
                wFormatTag = 1, // WAVE_FORMAT_PCM
                nChannels = Channels,
                nSamplesPerSec = SampleRate,
                wBitsPerSample = 16,
                nBlockAlign = Channels * 2,
                nAvgBytesPerSec = SampleRate * Channels * 2,
                cbSize = 0,
            };
            const uint flags = AudclntStreamflagsLoopback | AudclntStreamflagsEventcallback | AudclntStreamflagsAutoconvertpcm | AudclntStreamflagsSrcDefaultQuality;
            Check(client.Initialize(0, flags, 200_000, 0, ref format, IntPtr.Zero), "Initialize");
            evt = CreateEventW(IntPtr.Zero, false, false, null);
            Check(client.SetEventHandle(evt), "SetEventHandle");
            Guid iid = typeof(IAudioCaptureClient).GUID;
            Check(client.GetService(ref iid, out object service), "GetService");
            capture = (IAudioCaptureClient)service;
            Check(client.Start(), "Start");
            _started.Set();

            short[] silence = [];
            short[] buffer = new short[SampleRate / 50 * Channels * 4];
            while (!_stop)
            {
                _ = WaitForSingleObject(evt, 100);
                while (!_stop && capture.GetNextPacketSize(out uint packet) == 0 && packet > 0)
                {
                    Check(capture.GetBuffer(out IntPtr data, out uint frames, out uint bufferFlags, out _, out _), "GetBuffer");
                    int count = (int)frames * Channels;
                    if ((bufferFlags & AudclntBufferflagsSilent) != 0 || data == IntPtr.Zero)
                    {
                        if (silence.Length < count)
                        {
                            silence = new short[count];
                        }

                        LastPeak = 0;
                        _onSamples(silence.AsSpan(0, count));
                    }
                    else
                    {
                        if (buffer.Length < count)
                        {
                            buffer = new short[count];
                        }

                        Marshal.Copy(data, buffer, 0, count);
                        ReadOnlySpan<short> samples = buffer.AsSpan(0, count);

                        int peak = 0;
                        foreach (short s in samples)
                        {
                            int a = s == short.MinValue ? short.MaxValue : Math.Abs((int)s);
                            if (a > peak)
                            {
                                peak = a;
                            }
                        }

                        LastPeak = peak;
                        _onSamples(samples);
                    }

                    FramesCaptured += frames;
                    _ = capture.ReleaseBuffer(frames);
                }
            }

            _ = client.Stop();
        }
        catch (Exception ex)
        {
            _startError ??= ex;
            _started.Set();
        }
        finally
        {
            if (capture is not null)
            {
                Marshal.ReleaseComObject(capture);
            }

            if (client is not null)
            {
                Marshal.ReleaseComObject(client);
            }

            if (evt != IntPtr.Zero)
            {
                _ = CloseHandle(evt);
            }
        }
    }

    private static IAudioClient Activate(uint processId)
    {
        // AUDIOCLIENT_ACTIVATION_PARAMS { type = PROCESS_LOOPBACK, { pid, INCLUDE_TARGET_PROCESS_TREE } } u PROPVARIANT (VT_BLOB).
        IntPtr parameters = Marshal.AllocHGlobal(12);
        IntPtr variant = Marshal.AllocHGlobal(24);
        try
        {
            Marshal.WriteInt32(parameters, 0, 1);
            Marshal.WriteInt32(parameters, 4, (int)processId);
            Marshal.WriteInt32(parameters, 8, 0);
            for (int i = 0; i < 24; i += 8)
            {
                Marshal.WriteInt64(variant, i, 0);
            }

            Marshal.WriteInt16(variant, 0, 65); // VT_BLOB
            Marshal.WriteInt32(variant, 8, 12);
            Marshal.WriteIntPtr(variant, 16, parameters);

            var handler = new ActivationHandler();
            Guid iid = typeof(IAudioClient).GUID;
            ActivateAudioInterfaceAsync("VAD\\Process_Loopback", ref iid, variant, handler, out IActivateAudioInterfaceAsyncOperation operation);
            if (!handler.Done.Wait(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException("Audio interface activation timed out.");
            }

            Check(operation.GetActivateResult(out int result, out object activated), "GetActivateResult");
            Check(result, "ActivateAudioInterfaceAsync");
            Marshal.ReleaseComObject(operation);
            return (IAudioClient)activated;
        }
        finally
        {
            Marshal.FreeHGlobal(variant);
            Marshal.FreeHGlobal(parameters);
        }
    }

    private static void Check(int hr, string what)
    {
        if (hr < 0)
        {
            throw new COMException($"WASAPI {what} failed (0x{hr:X8}).", hr);
        }
    }

    private const uint AudclntStreamflagsLoopback = 0x00020000;
    private const uint AudclntStreamflagsEventcallback = 0x00040000;
    private const uint AudclntStreamflagsSrcDefaultQuality = 0x08000000;
    private const uint AudclntStreamflagsAutoconvertpcm = 0x80000000;
    private const uint AudclntBufferflagsSilent = 0x2;

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    public struct WaveFormatEx
    {
        public ushort wFormatTag;
        public ushort nChannels;
        public uint nSamplesPerSec;
        public uint nAvgBytesPerSec;
        public ushort nBlockAlign;
        public ushort wBitsPerSample;
        public ushort cbSize;
    }

    [DllImport("Mmdevapi.dll", ExactSpelling = true, PreserveSig = false)]
    private static extern void ActivateAudioInterfaceAsync([MarshalAs(UnmanagedType.LPWStr)] string deviceInterfacePath, ref Guid riid, IntPtr activationParams,
        IActivateAudioInterfaceCompletionHandler completionHandler, out IActivateAudioInterfaceAsyncOperation activationOperation);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateEventW(IntPtr attributes, bool manualReset, bool initialState, string? name);

    [DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    [ComVisible(true)]
    public sealed class ActivationHandler : IActivateAudioInterfaceCompletionHandler, IAgileObject
    {
        public ManualResetEventSlim Done { get; } = new(false);

        public int ActivateCompleted(IActivateAudioInterfaceAsyncOperation activateOperation)
        {
            Done.Set();
            return 0;
        }
    }

    [ComImport, Guid("94ea2b94-e9cc-49e0-c0ff-ee64ca8f5b90"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAgileObject
    {
    }

    [ComImport, Guid("41D949AB-9862-444A-80F6-C261334DA5EB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IActivateAudioInterfaceCompletionHandler
    {
        [PreserveSig]
        int ActivateCompleted(IActivateAudioInterfaceAsyncOperation activateOperation);
    }

    [ComImport, Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IActivateAudioInterfaceAsyncOperation
    {
        [PreserveSig]
        int GetActivateResult(out int activateResult, [MarshalAs(UnmanagedType.IUnknown)] out object activatedInterface);
    }

    [ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioClient
    {
        [PreserveSig] int Initialize(int shareMode, uint streamFlags, long bufferDuration, long periodicity, ref WaveFormatEx format, IntPtr audioSessionGuid);
        [PreserveSig] int GetBufferSize(out uint bufferFrames);
        [PreserveSig] int GetStreamLatency(out long latency);
        [PreserveSig] int GetCurrentPadding(out uint padding);
        [PreserveSig] int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closestMatch);
        [PreserveSig] int GetMixFormat(out IntPtr format);
        [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
        [PreserveSig] int Start();
        [PreserveSig] int Stop();
        [PreserveSig] int Reset();
        [PreserveSig] int SetEventHandle(IntPtr eventHandle);
        [PreserveSig] int GetService(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
    }

    [ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioCaptureClient
    {
        [PreserveSig] int GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong devicePosition, out ulong qpcPosition);
        [PreserveSig] int ReleaseBuffer(uint frames);
        [PreserveSig] int GetNextPacketSize(out uint frames);
    }
}

using System.Runtime.InteropServices;

namespace JebaitedPlayer.Mpv.Native;

/// <summary>mpv_format iz client.h.</summary>
public enum MpvFormat
{
    None = 0,
    String = 1,
    OsdString = 2,
    Flag = 3,
    Int64 = 4,
    Double = 5,
    Node = 6,
    NodeArray = 7,
    NodeMap = 8,
    ByteArray = 9,
}

/// <summary>mpv_event_id iz client.h (API 2.x).</summary>
internal enum MpvEventId
{
    None = 0,
    Shutdown = 1,
    LogMessage = 2,
    GetPropertyReply = 3,
    SetPropertyReply = 4,
    CommandReply = 5,
    StartFile = 6,
    EndFile = 7,
    FileLoaded = 8,
    ClientMessage = 16,
    VideoReconfig = 17,
    AudioReconfig = 18,
    Seek = 20,
    PlaybackRestart = 21,
    PropertyChange = 22,
    QueueOverflow = 24,
    Hook = 25,
}

[StructLayout(LayoutKind.Sequential)]
internal struct MpvEvent
{
    public MpvEventId EventId;
    public int Error;
    public ulong ReplyUserdata;
    public nint Data;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MpvEventProperty
{
    public nint Name;
    public MpvFormat Format;
    public nint Data;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MpvEventEndFile
{
    public int Reason;
    public int Error;
    public long PlaylistEntryId;
    public long PlaylistInsertId;
    public int PlaylistInsertNumEntries;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MpvEventLogMessage
{
    public nint Prefix;
    public nint Level;
    public nint Text;
    public int LogLevel;
}

/// <summary>P/Invoke deklaracije za libmpv-2.dll (client API 2.x).</summary>
internal static partial class LibMpv
{
    private const string Lib = "libmpv-2.dll";

    [LibraryImport(Lib)]
    internal static partial ulong mpv_client_api_version();

    [LibraryImport(Lib)]
    internal static partial nint mpv_create();

    [LibraryImport(Lib)]
    internal static partial int mpv_initialize(nint ctx);

    [LibraryImport(Lib)]
    internal static partial void mpv_terminate_destroy(nint ctx);

    [LibraryImport(Lib)]
    internal static partial nint mpv_error_string(int error);

    [LibraryImport(Lib)]
    internal static partial void mpv_free(nint data);

    [LibraryImport(Lib)]
    internal static partial nint mpv_wait_event(nint ctx, double timeout);

    [LibraryImport(Lib)]
    internal static partial void mpv_wakeup(nint ctx);

    [LibraryImport(Lib)]
    internal static partial int mpv_command(nint ctx, nint[] args);

    [LibraryImport(Lib)]
    internal static partial int mpv_command_async(nint ctx, ulong replyUserdata, nint[] args);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int mpv_set_option_string(nint ctx, string name, string data);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint mpv_get_property_string(nint ctx, string name);

    [LibraryImport(Lib, EntryPoint = "mpv_get_property", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int mpv_get_property_double(nint ctx, string name, MpvFormat format, out double data);

    [LibraryImport(Lib, EntryPoint = "mpv_get_property", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int mpv_get_property_long(nint ctx, string name, MpvFormat format, out long data);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int mpv_observe_property(nint ctx, ulong replyUserdata, string name, MpvFormat format);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int mpv_request_log_messages(nint ctx, string minLevel);
}

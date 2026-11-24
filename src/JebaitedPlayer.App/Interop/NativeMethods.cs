using System.Runtime.InteropServices;
using System.Text;

namespace JebaitedPlayer.Interop;

/// <summary>Win32 funkcije potrebne za ugradnju mpv prozora i prozirni sloj s kontrolama.</summary>
internal static class NativeMethods
{
    public const int WS_CHILD = 0x40000000;
    public const int WS_VISIBLE = 0x10000000;
    public const int WS_CLIPCHILDREN = 0x02000000;
    public const int WS_CLIPSIBLINGS = 0x04000000;
    public const int WS_EX_NOACTIVATE = 0x08000000;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int GWL_EXSTYLE = -20;

    public const uint CS_VREDRAW = 0x0001;
    public const uint CS_HREDRAW = 0x0002;
    public const uint CS_DBLCLKS = 0x0008;

    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_NOOWNERZORDER = 0x0200;
    public const uint SWP_ASYNCWINDOWPOS = 0x4000;

    public const int WM_CREATE = 0x0001;
    public const int WM_SIZE = 0x0005;
    public const int WM_SETFOCUS = 0x0007;
    public const int WM_SETCURSOR = 0x0020;
    public const int WM_MOUSEACTIVATE = 0x0021;
    public const int WM_MOUSEMOVE = 0x0200;
    public const int WM_LBUTTONDOWN = 0x0201;
    public const int WM_LBUTTONDBLCLK = 0x0203;
    public const int WM_RBUTTONUP = 0x0205;
    public const int WM_MOUSEWHEEL = 0x020A;
    public const int WM_PARENTNOTIFY = 0x0210;
    public const int WM_DROPFILES = 0x0233;

    public const int HTCLIENT = 1;
    public const int IDC_ARROW = 32512;
    public const int BLACK_BRUSH = 4;
    public const uint GA_ROOT = 2;

    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    public const int DWMWA_CAPTION_COLOR = 35;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    public delegate bool EnumWindowsProc(nint hwnd, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern ushort RegisterClassEx(ref WNDCLASSEX wc);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern nint CreateWindowEx(int exStyle, string className, string windowName, int style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyWindow(nint hwnd);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern nint GetModuleHandle(string? name);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
    public static extern nint GetProcAddress(nint module, string procName);

    [DllImport("user32.dll")]
    public static extern nint LoadCursor(nint instance, nint cursorName);

    [DllImport("gdi32.dll")]
    public static extern nint GetStockObject(int obj);

    [DllImport("user32.dll")]
    public static extern nint SetCursor(nint cursor);

    [DllImport("user32.dll")]
    public static extern bool EnumChildWindows(nint parent, EnumWindowsProc proc, nint lParam);

    [DllImport("user32.dll")]
    public static extern bool EnableWindow(nint hwnd, bool enable);

    [DllImport("user32.dll")]
    public static extern bool IsWindowEnabled(nint hwnd);

    [DllImport("user32.dll")]
    public static extern nint GetParent(nint hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    public static extern bool GetClientRect(nint hwnd, out RECT rect);

    [DllImport("user32.dll")]
    public static extern bool ClientToScreen(nint hwnd, ref POINT pt);

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT pt);

    [DllImport("user32.dll")]
    public static extern nint WindowFromPoint(POINT pt);

    [DllImport("user32.dll")]
    public static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern nint GetAncestor(nint hwnd, uint flags);

    [DllImport("user32.dll")]
    public static extern nint SetFocus(nint hwnd);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static extern nint GetWindowLongPtr(nint hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    public static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);

    [DllImport("shell32.dll")]
    public static extern void DragAcceptFiles(nint hwnd, bool accept);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern uint DragQueryFile(nint hDrop, uint index, StringBuilder? file, uint size);

    [DllImport("shell32.dll")]
    public static extern void DragFinish(nint hDrop);

    /// <summary>Dijagnostika bez miša (JEBAITED_DIAG_NO_MOUSE=1): testovi ne smiju micati kursor ni „klikati” (korisnik radi na drugom monitoru).</summary>
    public static bool NoMouse { get; } = Environment.GetEnvironmentVariable("JEBAITED_DIAG_NO_MOUSE") == "1";

    public static bool SetCursorPos(int x, int y) => NoMouse || SetCursorPosNative(x, y);

    [DllImport("user32.dll", EntryPoint = "SetCursorPos")]
    private static extern bool SetCursorPosNative(int x, int y);

    /// <summary>Dijagnostika (UPGRADE 3): pritisak ili otpuštanje lijeve tipke miša na mjestu kursora (pravi ulaz, kao korisnik).</summary>
    public static void MouseButton(bool down)
    {
        if (NoMouse)
        {
            return;
        }

        var input = new INPUT { type = 0, mi = new MOUSEINPUT { dwFlags = down ? 0x0002u : 0x0004u } };
        SendInput(1, [input], Marshal.SizeOf<INPUT>());
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, INPUT[] inputs, int size);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public MOUSEINPUT mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Interop;
using static JebaitedPlayer.Interop.NativeMethods;

namespace JebaitedPlayer.Controls;

/// <summary>
/// Win32 prozor u koji mpv crta video (mpv dobiva njegov HWND kao <c>wid</c>).
/// <para>
/// mpv unutar njega stvara svoj prozor-dijete. Taj prozor onemogućujemo (EnableWindow false),
/// pa Windows sav miš (klik, dvoklik, kotačić, desni klik) i drag &amp; drop šalje ovom prozoru,
/// a mi ih prosljeđujemo aplikaciji. mpv tako samo crta i nikad ne krade fokus tipkovnice.
/// </para>
/// </summary>
public sealed class VideoHost : HwndHost
{
    private const string ClassName = "JebaitedPlayerVideoHost";
    private static bool _classRegistered;

    private nint _hwnd;
    private int _lastX = int.MinValue;
    private int _lastY = int.MinValue;
    private long _ignoreClickUntil;
    private bool _cursorHidden;

    public event EventHandler? VideoMouseMove;
    public event EventHandler? VideoClick;
    public event EventHandler? VideoDoubleClick;
    public event EventHandler? VideoRightClick;
    public event EventHandler<int>? VideoMouseWheel;
    public event EventHandler<string[]>? FilesDropped;
    public event EventHandler? FocusReceived;

    /// <summary>HWND koji se predaje mpv-u kao <c>wid</c>. 0 dok prozor nije stvoren.</summary>
    public nint VideoHandle => _hwnd;

    /// <summary>Skriva pokazivač miša iznad videa (kad su kontrole skrivene).</summary>
    public bool CursorHidden
    {
        get => _cursorHidden;
        set
        {
            if (_cursorHidden == value)
            {
                return;
            }

            _cursorHidden = value;
            if (IsCursorOverVideo())
            {
                SetCursor(value ? 0 : LoadCursor(0, IDC_ARROW));
            }
        }
    }

    /// <summary>Onemogućuje sve prozore koje je mpv stvorio unutar ovog prozora (vidi opis klase).</summary>
    public void DisableVideoChildren()
    {
        if (_hwnd == 0)
        {
            return;
        }

        EnumChildWindows(_hwnd, (child, _) =>
        {
            if (IsWindowEnabled(child))
            {
                EnableWindow(child, false);
            }

            return true;
        }, 0);
    }

    /// <summary>Za dijagnostiku: koliko prozora-djece postoji i koliko ih je onemogućeno.</summary>
    public (int Total, int Disabled) GetChildWindowInfo()
    {
        int total = 0, disabled = 0;
        if (_hwnd != 0)
        {
            EnumChildWindows(_hwnd, (child, _) =>
            {
                total++;
                if (!IsWindowEnabled(child))
                {
                    disabled++;
                }

                return true;
            }, 0);
        }

        return (total, disabled);
    }

    /// <summary>Za dijagnostiku: bi li klik u sredinu videa stigao ovom prozoru (a ne mpv-ovom).</summary>
    public bool IsCenterRoutedToHost()
    {
        if (_hwnd == 0 || !GetClientRect(_hwnd, out RECT rc))
        {
            return false;
        }

        var pt = new POINT { X = rc.Right / 2, Y = rc.Bottom / 2 };
        ClientToScreen(_hwnd, ref pt);
        return WindowFromPoint(pt) == _hwnd;
    }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        EnsureWindowClass();
        _hwnd = CreateWindowEx(0, ClassName, "jebaited_player video",
            WS_CHILD | WS_VISIBLE | WS_CLIPCHILDREN | WS_CLIPSIBLINGS,
            0, 0, 1, 1, hwndParent.Handle, 0, GetModuleHandle(null), 0);
        if (_hwnd == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        DragAcceptFiles(_hwnd, true);
        return new HandleRef(this, _hwnd);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        DestroyWindow(hwnd.Handle);
        _hwnd = 0;
    }

    protected override nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        switch (msg)
        {
            case WM_SIZE:
                ResizeVideoChildren(LoWord(lParam), HiWord(lParam));
                break;

            case WM_PARENTNOTIFY:
                if (LoWord(wParam) == WM_CREATE)
                {
                    // mpv je upravo stvorio svoj prozor; onemogući ga čim UI dretva stigne.
                    Dispatcher.BeginInvoke(DisableVideoChildren);
                }

                break;

            case WM_SETCURSOR:
                if (LoWord(lParam) == HTCLIENT)
                {
                    SetCursor(_cursorHidden ? 0 : LoadCursor(0, IDC_ARROW));
                    handled = true;
                    return 1;
                }

                break;

            case WM_MOUSEACTIVATE:
                // Prvi klik u neaktivan prozor samo ga aktivira; ne želimo da pritom pauzira video.
                if (GetForegroundWindow() != GetAncestor(hwnd, GA_ROOT))
                {
                    _ignoreClickUntil = Environment.TickCount64 + 500;
                }

                break;

            case WM_MOUSEMOVE:
            {
                int x = (short)LoWord(lParam);
                int y = (short)HiWord(lParam);
                if (x != _lastX || y != _lastY)
                {
                    _lastX = x;
                    _lastY = y;
                    VideoMouseMove?.Invoke(this, EventArgs.Empty);
                }

                break;
            }

            case WM_LBUTTONDOWN:
                if (Environment.TickCount64 > _ignoreClickUntil)
                {
                    VideoClick?.Invoke(this, EventArgs.Empty);
                }

                _ignoreClickUntil = 0;
                handled = true;
                return 0;

            case WM_LBUTTONDBLCLK:
                VideoDoubleClick?.Invoke(this, EventArgs.Empty);
                handled = true;
                return 0;

            case WM_RBUTTONUP:
                VideoRightClick?.Invoke(this, EventArgs.Empty);
                handled = true;
                return 0;

            case WM_MOUSEWHEEL:
                VideoMouseWheel?.Invoke(this, (short)HiWord(wParam));
                handled = true;
                return 0;

            case WM_DROPFILES:
                FilesDropped?.Invoke(this, ReadDroppedFiles(wParam));
                handled = true;
                return 0;

            case WM_SETFOCUS:
                FocusReceived?.Invoke(this, EventArgs.Empty);
                break;
        }

        return base.WndProc(hwnd, msg, wParam, lParam, ref handled);
    }

    private void ResizeVideoChildren(int width, int height)
    {
        nint self = _hwnd;
        if (self == 0)
        {
            return;
        }

        EnumChildWindows(self, (child, _) =>
        {
            if (GetParent(child) == self)
            {
                // Asinkrono: mpv-ov prozor pripada drugoj dretvi, a UI ne smije čekati na nju.
                SetWindowPos(child, 0, 0, 0, width, height, SWP_NOZORDER | SWP_NOACTIVATE | SWP_ASYNCWINDOWPOS);
            }

            return true;
        }, 0);
    }

    private bool IsCursorOverVideo()
    {
        return _hwnd != 0 && GetCursorPos(out POINT pt) && WindowFromPoint(pt) == _hwnd;
    }

    private static string[] ReadDroppedFiles(nint hDrop)
    {
        try
        {
            uint count = DragQueryFile(hDrop, 0xFFFFFFFF, null, 0);
            var files = new string[count];
            for (uint i = 0; i < count; i++)
            {
                uint length = DragQueryFile(hDrop, i, null, 0);
                var sb = new StringBuilder((int)length + 1);
                DragQueryFile(hDrop, i, sb, length + 1);
                files[i] = sb.ToString();
            }

            return files;
        }
        finally
        {
            DragFinish(hDrop);
        }
    }

    private static void EnsureWindowClass()
    {
        if (_classRegistered)
        {
            return;
        }

        var wc = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            style = CS_DBLCLKS | CS_HREDRAW | CS_VREDRAW,
            // Poruke obrađuje HwndHost (WndProc gore); klasa koristi standardni DefWindowProc.
            lpfnWndProc = GetProcAddress(GetModuleHandle("user32.dll"), "DefWindowProcW"),
            hInstance = GetModuleHandle(null),
            hbrBackground = GetStockObject(BLACK_BRUSH),
            lpszClassName = ClassName,
        };

        if (RegisterClassEx(ref wc) == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        _classRegistered = true;
    }

    private static int LoWord(nint value) => (int)((long)value & 0xFFFF);

    private static int HiWord(nint value) => (int)(((long)value >> 16) & 0xFFFF);
}

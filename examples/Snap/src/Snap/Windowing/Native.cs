using System.Runtime.InteropServices;
using Snap.Core;

namespace Snap.Windowing;

/// The Win32 calls Snap needs to find, measure, and move other apps' windows,
/// register hotkeys, and show a tray icon. One place, so they are easy to review.
internal static unsafe partial class Native
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public readonly Box ToBox() => Box.FromEdges(Left, Top, Right, Bottom);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor, rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WNDCLASSEXW
    {
        public uint cbSize, style;
        public delegate* unmanaged<nint, uint, nint, nint, nint> lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public nint hInstance, hIcon, hCursor, hbrBackground;
        public char* lpszMenuName, lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public nint hWnd;
        public uint uID, uFlags, uCallbackMessage;
        public nint hIcon;
        public fixed char szTip[128];
        public uint dwState, dwStateMask;
        public fixed char szInfo[256];
        public uint uVersion;
        public fixed char szInfoTitle[64];
        public uint dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    public const uint SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010, SWP_NOOWNERZORDER = 0x0200;
    public const int SW_RESTORE = 9, SW_MAXIMIZE = 3;
    public const uint GA_ROOT = 2;
    public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    public const uint EVENT_SYSTEM_FOREGROUND = 3, WINEVENT_OUTOFCONTEXT = 0;
    public const uint MOD_NOREPEAT = 0x4000;
    public const uint WM_NULL = 0, WM_CLOSE = 0x0010, WM_HOTKEY = 0x0312, WM_APP = 0x8000;
    public const uint WM_LBUTTONUP = 0x0202, WM_RBUTTONUP = 0x0205, WM_CONTEXTMENU = 0x007B, NIN_SELECT = 0x0400, NIN_KEYSELECT = 0x0401;
    public const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETVERSION = 4;
    public const uint NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4, NIF_SHOWTIP = 0x80;
    public const uint MF_STRING = 0, MF_GRAYED = 1, MF_SEPARATOR = 0x800;
    public const uint TPM_RETURNCMD = 0x100, TPM_RIGHTBUTTON = 2, TPM_BOTTOMALIGN = 0x20;
    public const uint WS_POPUP = 0x80000000, WS_EX_TOOLWINDOW = 0x80;
    public const uint IMAGE_ICON = 1, LR_LOADFROMFILE = 0x10;
    public const int SM_CXSMICON = 49, SM_CYSMICON = 50;
    public const int MDT_EFFECTIVE_DPI = 0;

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool GetWindowRect(nint hwnd, out RECT rect);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool IsZoomed(nint hwnd);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool IsIconic(nint hwnd);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool IsWindow(nint hwnd);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool IsWindowVisible(nint hwnd);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool ShowWindow(nint hwnd, int command);
    [LibraryImport("user32.dll")] public static partial nint GetForegroundWindow();
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool SetForegroundWindow(nint hwnd);
    [LibraryImport("user32.dll")] public static partial nint GetAncestor(nint hwnd, uint flags);
    [LibraryImport("user32.dll")] public static partial int GetClassNameW(nint hwnd, char* name, int max);
    [LibraryImport("user32.dll")] public static partial uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool EnumDisplayMonitors(nint hdc, nint clip, delegate* unmanaged<nint, nint, RECT*, nint, int> callback, nint data);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool GetMonitorInfoW(nint monitor, ref MONITORINFO info);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool MessageBeep(uint type);
    [LibraryImport("user32.dll")] public static partial nint SetWinEventHook(uint min, uint max, nint module, delegate* unmanaged<nint, uint, nint, int, int, uint, uint, void> callback, uint pid, uint thread, uint flags);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool UnhookWinEvent(nint hook);
    [LibraryImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool RegisterHotKey(nint hwnd, int id, uint mods, uint key);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool UnregisterHotKey(nint hwnd, int id);
    [LibraryImport("user32.dll", SetLastError = true)] public static partial ushort RegisterClassExW(ref WNDCLASSEXW cls);
    [LibraryImport("user32.dll", SetLastError = true)] public static partial nint CreateWindowExW(uint exStyle, char* className, char* name, uint style, int x, int y, int w, int h, nint parent, nint menu, nint instance, nint param);
    [LibraryImport("user32.dll")] public static partial nint DefWindowProcW(nint hwnd, uint msg, nint w, nint l);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool DestroyWindow(nint hwnd);
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)] public static partial uint RegisterWindowMessageW(string name);
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)] public static partial nint FindWindowW(string className, string? title);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool PostMessageW(nint hwnd, uint msg, nint w, nint l);
    [LibraryImport("user32.dll")] public static partial nint CreatePopupMenu();
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool AppendMenuW(nint menu, uint flags, nuint id, string? text);
    [LibraryImport("user32.dll")] public static partial int TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint hwnd, nint parameters);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool DestroyMenu(nint menu);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool GetCursorPos(out POINT point);
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)] public static partial nint LoadImageW(nint instance, string name, uint type, int cx, int cy, uint flags);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool DestroyIcon(nint icon);
    [LibraryImport("user32.dll")] public static partial int GetSystemMetrics(int index);
    [LibraryImport("shell32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool Shell_NotifyIconW(uint message, ref NOTIFYICONDATAW data);
    [LibraryImport("shcore.dll")] public static partial int GetDpiForMonitor(nint monitor, int type, out uint x, out uint y);
    [LibraryImport("dwmapi.dll")] public static partial int DwmGetWindowAttribute(nint hwnd, int attribute, out RECT value, int size);
    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)] public static partial nint GetModuleHandleW(string? name);

    public static string ClassName(nint hwnd)
    {
        var buffer = stackalloc char[256];
        var length = GetClassNameW(hwnd, buffer, 256);
        return new string(buffer, 0, length);
    }

    /// What people see: the window without its invisible resize border.
    public static Box VisibleFrame(nint hwnd) =>
        DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out var r, sizeof(RECT)) == 0 ? r.ToBox() : OuterRect(hwnd);

    public static Box OuterRect(nint hwnd) => GetWindowRect(hwnd, out var r) ? r.ToBox() : default;

    public sealed record Display(nint Handle, Box Monitor, Box Work, uint Dpi);

    [ThreadStatic] private static List<nint>? _monitors;

    [UnmanagedCallersOnly]
    private static int OnMonitor(nint monitor, nint hdc, RECT* rect, nint data)
    {
        _monitors?.Add(monitor);
        return 1;
    }

    /// Every display with its full rect, its work area (no taskbar), and its DPI, in physical pixels.
    public static List<Display> Displays()
    {
        _monitors = [];
        EnumDisplayMonitors(0, 0, &OnMonitor, 0);
        var displays = new List<Display>();
        foreach (var monitor in _monitors)
        {
            var info = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
            if (!GetMonitorInfoW(monitor, ref info)) continue;
            var dpi = GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out var x, out _) == 0 ? x : 96;
            displays.Add(new(monitor, info.rcMonitor.ToBox(), info.rcWork.ToBox(), dpi));
        }
        return displays;
    }
}

# Overlay recipes

Windows that sit over other apps: frozen screens, pickers, panels, key
caps, a cursor halo, toasts that stay out of recordings. Each shape ran in
Onyx for Windows (a tray suite built with BAWA) on Windows 11 build 26200,
one display at 125 percent. Multi-display and mixed-DPI setups were not
tested there.

## Contents

- Borderless overlay window
- Place a panel by its visible frame
- Panels that never activate
- Click-through layered window
- Keep a window out of captures
- Low-level mouse hook

## Borderless overlay window

A WinUI window whose client area is the whole window, with no rounded
corners, no outline, and no fade. A frozen screen, a full-display picker,
and a test window with known pixels need all three.

`OverlappedPresenter.SetBorderAndTitleBar(false, false)` leaves
`WS_DLGFRAME`: the client area sits 3 px inside the window on every side.
A frozen screen drawn there came out scaled and 3 px off, and a window
highlight missed by 3 to 4 px. Clearing the style with `SetWindowLongPtr`
does not stick: the presenter puts it back each time the window shows.
Answer `WM_NCCALCSIZE` instead.

```csharp
internal static unsafe partial class Borderless
{
    private const uint WM_NCCALCSIZE = 0x0083;
    private const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10, SWP_FRAMECHANGED = 0x20;
    private const int DWMWA_TRANSITIONS_FORCEDISABLED = 3, DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWA_BORDER_COLOR = 34;

    /// Call after creating the window, and again after every AppWindow.Show.
    public static void Apply(nint hwnd)
    {
        // The client area is the whole window, whatever the styles say.
        if (SetWindowSubclass(hwnd, &NoFrameProc, 0x4F4E5958, 0))
            SetWindowPos(hwnd, 0, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
    }

    /// Windows 11 rounds every top-level window and draws a 1 px border over its content
    /// (#EAEAEA here). On a full-screen overlay that is an outline around each display.
    /// And no fade: a frozen screen must appear at once.
    public static void SquareNoBorderNoFade(nint hwnd)
    {
        int doNotRound = 1 /* DWMWCP_DONOTROUND */, noColor = unchecked((int)0xFFFFFFFE) /* DWMWA_COLOR_NONE */, on = 1;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, in doNotRound, sizeof(int));
        DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, in noColor, sizeof(int));
        DwmSetWindowAttribute(hwnd, DWMWA_TRANSITIONS_FORCEDISABLED, in on, sizeof(int));
    }

    [UnmanagedCallersOnly]
    private static nint NoFrameProc(nint hwnd, uint message, nint w, nint l, nuint id, nuint data) =>
        message == WM_NCCALCSIZE && w != 0 ? 0 : DefSubclassProc(hwnd, message, w, l);

    [LibraryImport("comctl32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowSubclass(nint hwnd, delegate* unmanaged<nint, uint, nint, nint, nuint, nuint, nint> proc, nuint id, nuint data);
    [LibraryImport("comctl32.dll")] private static partial nint DefSubclassProc(nint hwnd, uint message, nint w, nint l);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
    [LibraryImport("dwmapi.dll")] private static partial int DwmSetWindowAttribute(nint hwnd, int attribute, in int value, int size);
}

// The window:
var presenter = OverlappedPresenter.Create();
presenter.SetBorderAndTitleBar(false, false);
presenter.IsResizable = presenter.IsMaximizable = presenter.IsMinimizable = false;
presenter.IsAlwaysOnTop = true;
AppWindow.SetPresenter(presenter);
AppWindow.IsShownInSwitchers = false;
Borderless.SquareNoBorderNoFade(this.Handle());
Borderless.Apply(this.Handle());

// Showing it over one display, in physical pixels:
AppWindow.MoveAndResize(rect);
if (OuterRect(hwnd) != rect) AppWindow.MoveAndResize(rect);   // see the DPI gotcha
AppWindow.Show(activate);
Borderless.Apply(hwnd);                                        // the presenter put WS_DLGFRAME back
```

Gotchas:

- One window per display. A window across displays with different scales
  gets one DPI and draws at the wrong size on the others.
- Moving a window onto a display with another scale makes Windows send
  `WM_DPICHANGED` with its own suggested size, and WinUI applies it. Move
  a second time when the outer rect is not the display rect. Not tested
  on a second display.
- Read the window's scale from `GetDpiForWindow(hwnd) / 96`.
  `XamlRoot.RasterizationScale` can lag a frame behind after a move.
- `SetWindowSubclass` is exported by comctl32 5.82, so it works without a
  Common Controls 6 manifest on Windows 10 and 11.
- Turn transitions off on test windows too. A check that read the screen
  mid-fade got #FE3B30 for #FF3B30.

Self-test: open the window over the app's own test window, read the
screen back with the GDI freeze ([capture.md](capture.md)), and compare
it byte for byte with the picture it shows. The client rect
(`GetClientRect`) equals the window rect.

## Place a panel by its visible frame

A panel that keeps its border (for rounded corners and the shadow) has an
invisible resize border around it, like every bordered window.
`AppWindow.MoveAndResize(target)` sets the outer rect, so the visible
panel comes out smaller and off by the border. Measured at 125 percent: a
card meant to be 315 x 186 px showed as 299 x 178.

```csharp
internal static class Placement
{
    /// Moves the window so its visible frame, not its outer rect, lands on target (physical pixels).
    /// Call after Show: a hidden window has no DWM frame to measure.
    public static void PlaceVisible(Window window, Win32.RECT target)
    {
        var hwnd = window.Handle();
        window.AppWindow.MoveAndResize(new(target.Left, target.Top, target.Width, target.Height));
        Win32.GetWindowRect(hwnd, out var outer);
        Win32.DwmGetWindowAttribute(hwnd, Win32.DWMWA_EXTENDED_FRAME_BOUNDS, out var visible, Marshal.SizeOf<Win32.RECT>());
        if (outer.Equals(visible)) return;
        // OuterFor: grow the outer rect by the border on each side.
        window.AppWindow.MoveAndResize(new(
            target.Left - (visible.Left - outer.Left),
            target.Top - (visible.Top - outer.Top),
            target.Width + (outer.Width - visible.Width),
            target.Height + (outer.Height - visible.Height)));
    }
}
```

The same math moves other apps' windows ("Move a window" in
[recipes.md](recipes.md)). Keep it in one pure function
(`OuterFor(target, outer, visible)`) with a unit test.

Gotchas:

- Show first, measure, move again. Before the first show, the outer rect
  and the visible frame are equal, and the fix does nothing.
- A flyout or panel that measures its content before the content is in
  the window comes out too short: templated controls (`Button`,
  `ToggleSwitch`) have no template yet and measure small. Set the
  content, let it lay out once (`SizeChanged`, or `UpdateLayout` after
  show), then size the window.

## Panels that never activate

A switcher or a toolbar over other apps: clicks work, and the window you
were in keeps focus until you pick something. `WS_EX_NOACTIVATE` alone is
not enough for clicks. Answer `WM_MOUSEACTIVATE` too.

```csharp
internal static unsafe partial class NoActivate
{
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
    private const uint WM_MOUSEACTIVATE = 0x21;
    private const nint MA_NOACTIVATE = 3;

    /// Out of the window list, never takes focus, and a click does not activate it.
    public static void Apply(nint hwnd)
    {
        SetWindowLongPtrW(hwnd, GWL_EXSTYLE, GetWindowLongPtrW(hwnd, GWL_EXSTYLE) | (nint)(WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW));
        SetWindowSubclass(hwnd, &Proc, 1, 0);
    }

    [UnmanagedCallersOnly]
    private static nint Proc(nint hwnd, uint message, nint w, nint l, nuint id, nuint data) =>
        message == WM_MOUSEACTIVATE ? MA_NOACTIVATE : DefSubclassProc(hwnd, message, w, l);

    [LibraryImport("user32.dll")] private static partial nint GetWindowLongPtrW(nint hwnd, int index);
    [LibraryImport("user32.dll")] private static partial nint SetWindowLongPtrW(nint hwnd, int index, nint value);
    [LibraryImport("comctl32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowSubclass(nint hwnd, delegate* unmanaged<nint, uint, nint, nint, nuint, nuint, nint> proc, nuint id, nuint data);
    [LibraryImport("comctl32.dll")] private static partial nint DefSubclassProc(nint hwnd, uint message, nint w, nint l);
}

// Show it without taking focus:
panel.AppWindow.MoveAndResize(rect);
panel.AppWindow.Show(activateWindow: false);
```

Gotchas:

- The styles work when set after creation on a WinUI window.
- Buttons in such a panel get keyboard focus on show and draw a focus
  rectangle. Set `IsTabStop = false` on them.
- Keys never reach it: Windows sends keys only to the foreground window.
  A switcher reads its keys from the low-level keyboard hook instead.

## Click-through layered window

Key caps, a cursor halo, click ripples, a toast: shapes with soft edges
over any app, that clicks pass through. WinUI windows have no per-pixel
transparent background, so this is a plain Win32 popup drawn with
`UpdateLayeredWindow` from a 32-bit premultiplied BGRA bitmap.

```csharp
/// One click-through, topmost, never-activated popup. Use it only on the thread that made it.
public sealed unsafe partial class LayeredWindow : IDisposable
{
    private nint _memDc, _bitmap, _old;
    private uint* _bits;

    public nint Handle { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    /// Premultiplied BGRA, top row first. Valid until the next Resize.
    public Span<uint> Pixels => new(_bits, Width * Height);
    public nint Bits => (nint)_bits;

    public LayeredWindow(string title, bool excludeFromCapture = false)
    {
        Handle = CreateOverlayWindow(title);   // below
        if (excludeFromCapture) SetWindowDisplayAffinity(Handle, 0x11 /* WDA_EXCLUDEFROMCAPTURE */);
    }

    /// Makes the bitmap this size in physical pixels.
    public void Resize(int width, int height)
    {
        if (width == Width && height == Height && _bitmap != 0) return;
        FreeBitmap();
        var screen = GetDC(0);
        _memDc = CreateCompatibleDC(screen);
        ReleaseDC(0, screen);
        var header = new BITMAPINFOHEADER { biSize = (uint)sizeof(BITMAPINFOHEADER), biWidth = width, biHeight = -height /* top row first */, biPlanes = 1, biBitCount = 32 };
        _bitmap = CreateDIBSection(_memDc, ref header, 0, out var bits, 0, 0);
        _bits = (uint*)bits;
        _old = SelectObject(_memDc, _bitmap);
        (Width, Height) = (width, height);
    }

    /// Shows the bitmap with its top left at (x, y), physical pixels.
    public void Present(int x, int y, double opacity = 1)
    {
        var pos = new POINT { X = x, Y = y };
        var size = new SIZE { Cx = Width, Cy = Height };
        var src = new POINT();
        var blend = new BLENDFUNCTION { SourceConstantAlpha = (byte)Math.Clamp(opacity * 255, 0, 255), AlphaFormat = 1 /* AC_SRC_ALPHA */ };
        UpdateLayeredWindow(Handle, 0, &pos, &size, _memDc, &src, 0, &blend, 2 /* ULW_ALPHA */);
        Raise();
    }

    /// Topmost is shared with other apps, the taskbar, and Start: claim it each time it shows.
    public void Raise() => SetWindowPos(Handle, -1 /* HWND_TOPMOST */, 0, 0, 0, 0, 0x1 | 0x2 | 0x10 | 0x40 /* NOSIZE NOMOVE NOACTIVATE SHOWWINDOW */);

    /// One SetWindowPos, no redraw: cheap enough for every mouse move.
    public void Move(int x, int y) => SetWindowPos(Handle, 0, x, y, 0, 0, 0x1 | 0x4 | 0x10 /* NOSIZE NOZORDER NOACTIVATE */);

    /// Only the overall opacity changes: a fade costs no redraw.
    public void Fade(double opacity)
    {
        var blend = new BLENDFUNCTION { SourceConstantAlpha = (byte)Math.Clamp(opacity * 255, 0, 255), AlphaFormat = 1 };
        UpdateLayeredWindow(Handle, 0, null, null, 0, null, 0, &blend, 2);
    }

    public void Hide() => ShowWindow(Handle, 0 /* SW_HIDE */);

    private void FreeBitmap()
    {
        if (_memDc != 0) { SelectObject(_memDc, _old); DeleteDC(_memDc); }
        if (_bitmap != 0) DeleteObject(_bitmap);
        _memDc = _bitmap = _old = 0;
        _bits = null;
        Width = Height = 0;
    }

    public void Dispose()
    {
        FreeBitmap();
        if (Handle != 0) DestroyWindow(Handle);
        Handle = 0;
    }

    private static ushort _class;

    [UnmanagedCallersOnly]
    private static nint WndProc(nint hwnd, uint msg, nint w, nint l) => DefWindowProcW(hwnd, msg, w, l);

    private static nint CreateOverlayWindow(string title)
    {
        const uint WS_POPUP = 0x80000000;
        const uint WS_EX_LAYERED = 0x80000, WS_EX_TRANSPARENT = 0x20, WS_EX_TOPMOST = 0x8, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
        var module = GetModuleHandleW(null);
        fixed (char* name = "MyAppOverlay")
        fixed (char* text = title)
        {
            if (_class == 0)
            {
                var cls = new WNDCLASSEXW { cbSize = (uint)sizeof(WNDCLASSEXW), lpfnWndProc = &WndProc, hInstance = module, lpszClassName = name };
                _class = RegisterClassExW(ref cls);
            }
            return CreateWindowExW(WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
                name, text, WS_POPUP, 0, 0, 1, 1, 0, 0, module, 0);
        }
    }

    // Structs: POINT, SIZE, BLENDFUNCTION (four bytes), BITMAPINFOHEADER, and WNDCLASSEXW as in
    // "Message window" (recipes.md). Imports: user32 CreateWindowExW, RegisterClassExW,
    // DefWindowProcW, DestroyWindow, ShowWindow, SetWindowPos, UpdateLayeredWindow, GetDC,
    // ReleaseDC, SetWindowDisplayAffinity; gdi32 CreateCompatibleDC, DeleteDC, SelectObject,
    // DeleteObject, CreateDIBSection; kernel32 GetModuleHandleW.
}
```

Draw into the bitmap with GDI+ (`System.Drawing.Common` package), which
wraps the DIB's memory with no copy:

```csharp
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

using var bitmap = new Bitmap(window.Width, window.Height, window.Width * 4, PixelFormat.Format32bppPArgb, window.Bits);
using var g = Graphics.FromImage(bitmap);
g.Clear(Color.Transparent);
g.SmoothingMode = SmoothingMode.AntiAlias;
g.PixelOffsetMode = PixelOffsetMode.HighQuality;
// Grayscale antialiasing. ClearType needs an opaque background and leaves color fringes here.
g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
// ... fill a rounded path, draw the text ...
window.Present(x, y);
```

Gotchas:

- GDI `DrawText` writes alpha 0, so text drawn with GDI is invisible in a
  layered window. Use GDI+ or Direct2D.
- Every number is a physical pixel. Size the bitmap from the DPI of the
  display under the point (`MonitorFromPoint`, then `GetDpiForMonitor`),
  and redraw when that DPI changes.
- These overlays are a fixed dark plate with white text, not theme
  colors: they must read over any app and any wallpaper.
- `PrintWindow` cannot capture a layered Win32 window, so snapshot mode
  cannot photograph it. Paint the real bitmap, wrap its pixels in a
  `SoftwareBitmap` (`Bgra8`, `Premultiplied`, which matches the DIB
  exactly), and show it in a WinUI preview window through a
  `SoftwareBitmapSource`.

## Keep a window out of captures

A recording capsule, a toast meant for the person recording, a picker:
present on screen, missing from Snipping Tool, OBS, Teams, and the app's
own recorder.

```csharp
const uint WDA_NONE = 0, WDA_EXCLUDEFROMCAPTURE = 0x11;

// Before the window first shows, so no frame leaks.
SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE);
// Read it back in a self-test.
var excluded = GetWindowDisplayAffinity(hwnd, out var affinity) && affinity == WDA_EXCLUDEFROMCAPTURE;

[LibraryImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
static partial bool SetWindowDisplayAffinity(nint hwnd, uint affinity);
[LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
static partial bool GetWindowDisplayAffinity(nint hwnd, out uint affinity);
```

Gotchas:

- It also blanks `PrintWindow(PW_RENDERFULLCONTENT)`: the recording
  capsule's snapshot was a black rectangle. Snapshot mode sets `WDA_NONE`
  first, then sets the exclusion back for real use.
- It worked on a WinUI window and on an `UpdateLayeredWindow` window on
  Windows 11 26100 and 26200. `WDA_EXCLUDEFROMCAPTURE` needs Windows 10
  2004 or later (from Microsoft's docs, from memory; older builds know
  only `WDA_MONITOR`, which shows black).
- A Windows notification (`Shell_NotifyIcon` balloon or a toast) lands in
  recordings and in the notification center. A note for the person who
  records is an excluded overlay instead.

## Low-level mouse hook

A cursor halo, click ripples, or "reset on any click" for a snippet
engine. `WH_MOUSE_LL` sees every move and button on the desktop. No
permission.

Give the hook a thread of its own that also owns the overlay windows it
moves. `SetWindowPos` on a window another thread owns sends that thread a
message and waits, so a hook that moves a UI-thread window blocks every
mouse move whenever the UI thread is busy. The hook stores the point and
posts a message to its own thread; the message loop moves the window.

```csharp
public sealed unsafe class CursorHalo : IDisposable
{
    private const uint WM_APP = 0x8000, MsgMove = WM_APP + 1, MsgButton = WM_APP + 2, WM_TIMER = 0x113, WM_QUIT = 0x12;
    private const uint WM_MOUSEMOVE = 0x200, WM_LBUTTONDOWN = 0x201, WM_RBUTTONDOWN = 0x204;

    private static CursorHalo? _instance;
    private Thread? _thread;
    private uint _threadId;
    private nint _hook;
    private LayeredWindow? _halo;
    private int _x, _y, _movePending;
    private nuint _timerId;

    public nint HookHandle => _hook;   // for the self-test: 0 after Stop

    public void Start()
    {
        _instance = this;
        using var ready = new ManualResetEventSlim();
        _thread = new Thread(() => Run(ready)) { IsBackground = true, Name = "Cursor halo" };
        _thread.Start();
        ready.Wait(TimeSpan.FromSeconds(2));
    }

    public void Stop()
    {
        if (_thread is null) return;
        PostThreadMessageW(_threadId, WM_QUIT, 0, 0);
        _thread.Join(TimeSpan.FromSeconds(2));
        _thread = null;
    }

    private void Run(ManualResetEventSlim ready)
    {
        _threadId = GetCurrentThreadId();
        _halo = new LayeredWindow("Cursor halo");      // made on this thread, so it belongs to it
        // Paint the halo once at the pointer: Resize, draw with GDI+, Present.
        _hook = SetWindowsHookExW(14 /* WH_MOUSE_LL */, &Proc, GetModuleHandleW(null), 0);
        ready.Set();
        while (GetMessageW(out var msg, 0, 0, 0) > 0)
        {
            try
            {
                switch (msg.message)
                {
                    case MsgMove:
                        Interlocked.Exchange(ref _movePending, 0);
                        _halo.Move(_x - _halo.Width / 2, _y - _halo.Height / 2);
                        break;
                    case MsgButton:
                        // Start a ripple at the point in lParam, then a 15 ms thread timer if none runs.
                        // SetTimer(0, ...) picks its own id; keep it for KillTimer.
                        if (_timerId == 0) _timerId = SetTimer(0, 0, 15, 0);
                        break;
                    case WM_TIMER:
                        // A thread timer has hwnd 0: handle it here, DispatchMessage has nowhere to send it.
                        // Animate one frame. When nothing moves: KillTimer(0, _timerId); _timerId = 0.
                        break;
                    default:
                        DispatchMessageW(ref msg);
                        break;
                }
            }
            catch (Exception e) { AgentLog.Line($"cursor halo: {e}"); }
        }
        if (_hook != 0) UnhookWindowsHookEx(_hook);
        _hook = 0;
        _halo.Dispose();
    }

    /// Runs on the hook's thread, inside GetMessage. Store and post; never draw here.
    [UnmanagedCallersOnly]
    private static nint Proc(int code, nint w, nint l)
    {
        if (code >= 0 && _instance is { } self)
        {
            try
            {
                var info = (MSLLHOOKSTRUCT*)l;
                if ((uint)w == WM_MOUSEMOVE)
                {
                    (self._x, self._y) = (info->pt.X, info->pt.Y);
                    // One pending move at a time: moves arrive far faster than frames.
                    if (Interlocked.Exchange(ref self._movePending, 1) == 0) PostThreadMessageW(self._threadId, MsgMove, 0, 0);
                }
                else if ((uint)w is WM_LBUTTONDOWN or WM_RBUTTONDOWN)
                    PostThreadMessageW(self._threadId, MsgButton, w, (nint)(((long)info->pt.Y << 32) | (uint)info->pt.X));
            }
            catch (Exception e) { AgentLog.Line($"mouse hook: {e.Message}"); }
        }
        return CallNextHookEx(0, code, w, l);
    }

    public void Dispose() => Stop();

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData, flags, time; public nint dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct MSG { public nint hwnd; public uint message; public nint wParam, lParam; public uint time; public int x, y; }
    // Imports as in "Low-level keyboard hook" (recipes.md), plus user32 DispatchMessageW(ref MSG),
    // SetTimer(nint, nuint, uint, nint) and KillTimer(nint, nuint).
}
```

Gotchas:

- `SetTimer(0, id, ...)` without a window ignores `id` and returns a new
  one. `KillTimer(0, id)` needs the returned id, or the timer runs
  forever.
- Low-level hook callbacks run on the installing thread inside
  `GetMessage`, so the hook and the loop share fields without locks.
- Only the buttons matter for "reset on a click": a hook that ignores
  `WM_MOUSEMOVE` and acts on `WM_LBUTTONDOWN`, `WM_RBUTTONDOWN`,
  `WM_MBUTTONDOWN`, and `WM_XBUTTONDOWN` is a dozen lines on its own
  thread.
- Turning the feature off removes the hook and every overlay window.
  Check both in the self-test (`HookHandle == 0`, no windows left).
- Like the keyboard hook, it sees nothing over elevated windows, and
  Windows removes it silently after a callback that takes over a second.
  In a suite with several mouse users, move it into the host next to the
  keyboard hook.

Self-test: post a button message to the hook's thread (a ripple at a
point) instead of clicking. Nothing clicks in the person's windows, and
everything after the hook callback runs for real.

Done when each overlay shows at the right physical size, never takes
focus it should not, passes clicks through where it must, and its
self-test reads its pixels or state back without touching the person's
windows.

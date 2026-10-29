# Integration recipes

Short, working shapes for a WinUI 3 unpackaged app made from the BAWA
template. Every block compiled in a template app, and the hotkey, tray
icon, window move, keyboard hook, capture, single instance, and debug
bridge recipes ran on Windows 11. The clipboard, paste, keyboard hook,
and window capture shapes were revised from Onyx for Windows, a
twelve-feature tray suite built with BAWA, where they ran on Windows 11
build 26200.

The template enables `AllowUnsafeBlocks` and keeps its Win32 calls in
`UI/Win32.cs` (`Win32.RECT`, `GetWindowRect`, `DwmGetWindowAttribute`,
`SetForegroundWindow`, `GetWindowThreadProcessId`). The recipes use those
and declare the rest next to the code that needs them, with
`LibraryImport`.

More recipes, from the same suite, live next to this file:

- [overlays.md](overlays.md): truly borderless windows, placing a panel
  by its visible frame, panels that never activate, click-through layered
  windows, capture exclusion, the low-level mouse hook.
- [capture.md](capture.md): the GDI screen freeze, pickers over the
  frozen image, screen recording to MP4, DWM thumbnails, safe capture
  tests.
- [system.md](system.md): the window list for a switcher, app icons,
  Windows Search, per-sample tray icons, UI Automation, power requests,
  ports and command lines.

## Contents

- Message window
- Global hotkey (RegisterHotKey)
- Tray icon
- Move a window
- Launch at login
- Single instance
- Non-activating panel
- Clipboard listener
- Paste into the front app
- Low-level keyboard hook
- Screen capture
- Toast notifications
- URL protocol
- Debug bridge
- Elevation and UIPI

## Message window

Hotkeys, the clipboard listener, and the tray icon all talk to a window
handle. WinUI hides its own window procedure, so give them a small hidden
window of their own. Create it on the UI thread: WinUI's message loop
dispatches its messages, and handlers run on the UI thread.

```csharp
using System.Runtime.InteropServices;

namespace MyApp.UI;

/// A hidden window that receives Win32 messages on the UI thread.
/// messageOnly: true for hotkeys and the clipboard. A tray icon needs false:
/// message-only windows get no broadcasts, and the tray sends "TaskbarCreated" as one.
internal sealed unsafe partial class MessageWindow : IDisposable
{
    private static readonly Dictionary<nint, MessageWindow> Live = [];
    private static ushort _atom;

    public nint Handle { get; }

    /// Return true when the message was handled.
    public Func<uint, nint, nint, bool>? Handler { get; set; }

    public MessageWindow(bool messageOnly = true)
    {
        var instance = GetModuleHandleW(null);
        if (_atom == 0)
        {
            fixed (char* name = "AppMessageWindow")
            {
                var wc = new WNDCLASSEXW { cbSize = (uint)sizeof(WNDCLASSEXW), lpfnWndProc = &WndProc, hInstance = instance, lpszClassName = name };
                _atom = RegisterClassExW(in wc);
            }
        }
        Handle = CreateWindowExW(0, _atom, 0, 0, 0, 0, 0, 0, messageOnly ? HWND_MESSAGE : 0, 0, instance, 0);
        Live[Handle] = this;
    }

    public void Dispose()
    {
        Live.Remove(Handle);
        DestroyWindow(Handle);
    }

    [UnmanagedCallersOnly]
    private static nint WndProc(nint hwnd, uint message, nint wParam, nint lParam)
    {
        // An exception that leaves this method ends the process. Catch everything.
        try
        {
            if (Live.TryGetValue(hwnd, out var window) && window.Handler?.Invoke(message, wParam, lParam) == true) return 0;
        }
        catch (Exception e)
        {
            AgentLog.Line($"MessageWindow handler failed: {e}");
        }
        return DefWindowProcW(hwnd, message, wParam, lParam);
    }

    private const nint HWND_MESSAGE = -3;

    [StructLayout(LayoutKind.Sequential)]
    private struct WNDCLASSEXW
    {
        public uint cbSize, style;
        public delegate* unmanaged<nint, uint, nint, nint, nint> lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public nint hInstance, hIcon, hCursor, hbrBackground;
        public char* lpszMenuName, lpszClassName;
        public nint hIconSm;
    }

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)] private static partial nint GetModuleHandleW(string? name);
    [LibraryImport("user32.dll")] private static partial ushort RegisterClassExW(in WNDCLASSEXW wc);
    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint CreateWindowExW(uint exStyle, nint className, nint windowName, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool DestroyWindow(nint hwnd);
    [LibraryImport("user32.dll")] private static partial nint DefWindowProcW(nint hwnd, uint message, nint wParam, nint lParam);
}
```

The window procedure is a static `[UnmanagedCallersOnly]` method behind a
function pointer, so the garbage collector can never move or free it. A
delegate passed to Win32 needs a field that keeps it alive; a function
pointer does not.

## Global hotkey (RegisterHotKey)

A system-wide shortcut (Ctrl+Alt+Space opens the app from anywhere). No
permission, and the keys never reach the front app.

```csharp
internal sealed partial class GlobalHotkey : IDisposable
{
    public const uint Alt = 0x1, Ctrl = 0x2, Shift = 0x4, Win = 0x8;
    private const uint MOD_NOREPEAT = 0x4000, WM_HOTKEY = 0x0312;
    private readonly MessageWindow _window = new();
    private readonly Dictionary<int, Action> _actions = [];
    private int _nextId;

    public GlobalHotkey() => _window.Handler = (message, wParam, _) =>
    {
        if (message != WM_HOTKEY || !_actions.TryGetValue((int)wParam, out var action)) return false;
        action();                       // on the UI thread
        return true;
    };

    /// 0 when registered. 1409 (ERROR_HOTKEY_ALREADY_REGISTERED): another app owns the combo.
    public int Register(uint modifiers, Windows.System.VirtualKey key, Action action)
    {
        var id = ++_nextId;
        if (!RegisterHotKey(_window.Handle, id, modifiers | MOD_NOREPEAT, (uint)key)) return Marshal.GetLastPInvokeError();
        _actions[id] = action;
        return 0;
    }

    public void Dispose()
    {
        foreach (var id in _actions.Keys) UnregisterHotKey(_window.Handle, id);
        _window.Dispose();
    }

    [LibraryImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(nint hwnd, int id);
}

// In OnLaunched, after the window exists. Keep the object alive for the app's lifetime.
_hotkey = new GlobalHotkey();
var error = _hotkey.Register(GlobalHotkey.Ctrl | GlobalHotkey.Alt, VirtualKey.Space, () => MainWindow.Activate());
```

Gotchas:

- Keep the result. A taken combo fails with error 1409, and nothing else
  tells the user. Show "Ctrl+Alt+Space is used by another app" next to
  the shortcut in Settings, with a way to pick another.
- A registration can also succeed and still never fire, when another app
  grabs the keys first with a low-level keyboard hook. Press the combo
  once after you register it, during development.
- The Win key belongs to Windows. Most Win+letter combos are taken. Prefer
  Ctrl+Alt or Ctrl+Shift combos.
- `MOD_NOREPEAT` stops a held combo from firing again and again.
- Right after `WM_HOTKEY`, the app may bring its own window to the front.
  Call `Activate()` in the handler, not later from a timer.

## Tray icon

The Windows counterpart of a menu bar extra: an icon in the notification
area with a menu. `Shell_NotifyIcon` on a hidden top-level window.

```csharp
internal sealed unsafe partial class TrayIcon : IDisposable
{
    private const uint WM_TRAY = 0x8001, NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETVERSION = 4;
    private const uint NIF_MESSAGE = 0x1, NIF_ICON = 0x2, NIF_TIP = 0x4, NIF_SHOWTIP = 0x80;
    private const uint NIN_SELECT = 0x400, NIN_KEYSELECT = 0x401, WM_CONTEXTMENU = 0x7B;
    private readonly MessageWindow _window = new(messageOnly: false);   // must see "TaskbarCreated"
    private readonly uint _taskbarCreated = RegisterWindowMessageW("TaskbarCreated");
    private readonly nint _icon;
    private readonly string _tip;

    /// Left click or Enter on the icon.
    public Action? Open { get; set; }

    /// Right-click menu. A null action draws a separator.
    public List<(string Label, Action? Run)> Menu { get; } = [];

    public TrayIcon(string icoPath, string tip)
    {
        var size = GetSystemMetricsForDpi(49 /* SM_CXSMICON */, GetDpiForSystem());
        _icon = LoadImageW(0, icoPath, 1 /* IMAGE_ICON */, size, size, 0x10 /* LR_LOADFROMFILE */);
        _tip = tip;
        _window.Handler = OnMessage;
        Add();
    }

    private void Add()
    {
        var data = Data(NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP);
        Shell_NotifyIconW(NIM_ADD, ref data);
        data.uVersion = 4;                                   // NOTIFYICON_VERSION_4
        Shell_NotifyIconW(NIM_SETVERSION, ref data);
    }

    /// Live status ("Syncing 3 of 10") without opening anything.
    public void SetTip(string tip)
    {
        var data = Data(NIF_TIP | NIF_SHOWTIP, tip);
        Shell_NotifyIconW(NIM_MODIFY, ref data);
    }

    private NOTIFYICONDATAW Data(uint flags, string? tip = null)
    {
        var data = new NOTIFYICONDATAW { cbSize = (uint)sizeof(NOTIFYICONDATAW), hWnd = _window.Handle, uID = 1, uFlags = flags, uCallbackMessage = WM_TRAY, hIcon = _icon };
        var text = (tip ?? _tip).AsSpan();
        text[..Math.Min(text.Length, 127)].CopyTo(new Span<char>(data.szTip, 128));
        return data;
    }

    private bool OnMessage(uint message, nint wParam, nint lParam)
    {
        if (message == _taskbarCreated) { Add(); return true; }    // Explorer restarted
        if (message != WM_TRAY) return false;
        switch ((uint)(lParam & 0xFFFF))
        {
            case NIN_SELECT or NIN_KEYSELECT:
                Open?.Invoke();
                break;
            case WM_CONTEXTMENU:                                    // right click, Shift+F10, menu key
                ShowMenu((short)(wParam & 0xFFFF), (short)((wParam >> 16) & 0xFFFF));
                break;
        }
        return true;
    }

    private void ShowMenu(int x, int y)
    {
        var menu = CreatePopupMenu();
        for (var i = 0; i < Menu.Count; i++)
            AppendMenuW(menu, Menu[i].Run is null ? 0x800u /* MF_SEPARATOR */ : 0u, (nuint)(i + 1), Menu[i].Label);
        // Without these two calls the menu stays open when the user clicks elsewhere.
        SetForegroundWindow(_window.Handle);
        var chosen = TrackPopupMenuEx(menu, 0x100 /* TPM_RETURNCMD */ | 0x2 /* TPM_RIGHTBUTTON */, x, y, _window.Handle, 0);
        PostMessageW(_window.Handle, 0 /* WM_NULL */, 0, 0);
        DestroyMenu(menu);
        if (chosen > 0) Menu[chosen - 1].Run?.Invoke();
    }

    public void Dispose()
    {
        var data = Data(0);
        Shell_NotifyIconW(NIM_DELETE, ref data);
        DestroyIcon(_icon);
        _window.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NOTIFYICONDATAW
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

    [LibraryImport("shell32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool Shell_NotifyIconW(uint message, ref NOTIFYICONDATAW data);
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)] private static partial uint RegisterWindowMessageW(string name);
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)] private static partial nint LoadImageW(nint instance, string name, uint type, int cx, int cy, uint flags);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool DestroyIcon(nint icon);
    [LibraryImport("user32.dll")] private static partial int GetSystemMetricsForDpi(int index, uint dpi);
    [LibraryImport("user32.dll")] private static partial uint GetDpiForSystem();
    [LibraryImport("user32.dll")] private static partial nint CreatePopupMenu();
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool AppendMenuW(nint menu, uint flags, nuint id, string? text);
    [LibraryImport("user32.dll")] private static partial int TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint hwnd, nint tpm);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool DestroyMenu(nint menu);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool PostMessageW(nint hwnd, uint message, nint wParam, nint lParam);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool SetForegroundWindow(nint hwnd);
}

// In OnLaunched:
_tray = new TrayIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"), "MyApp");
_tray.Open = () => MainWindow.Activate();
_tray.Menu.Add(("Open MyApp", () => MainWindow.Activate()));
_tray.Menu.Add(("-", null));
_tray.Menu.Add(("Quit", () => Application.Current.Exit()));
```

Gotchas:

- Windows puts a new icon in the overflow ("Show hidden icons"). The app
  cannot pin itself. Tell the user once: Settings > Personalization >
  Taskbar > Other system tray icons.
- Explorer restarts (crash, update) remove every icon. The
  `TaskbarCreated` message is the cue to add it again, and only a
  top-level window receives it.
- A tray-only app never calls `Activate()` on its main window at launch.
  Create the window, keep it hidden, and show it from the tray. Hide it
  on close (`AppWindow.Closing`: set `args.Cancel = true`, then
  `AppWindow.Hide()`), so Quit in the menu is the only exit.
- Never minimize a WinUI window before its first frame. In the template,
  a window minimized at launch crashed the app (COMException 0x80040111).
  Start hidden instead.
- Win32 popup menus stay light when Windows is in dark mode. If that
  matters, show a small borderless WinUI window as the menu instead.
- `Dispose` the icon on exit, or a dead icon stays in the tray until the
  pointer moves over it.
- Windows 11 remembers every icon it has seen, keyed by exe path and
  `uID`, under `HKCU\Control Panel\NotifyIconSettings` (with the
  person's "show" choice in `IsPromoted`), and never removes the key.
  So a test run, a build folder, and a worktree never call `NIM_ADD`.
  Skip the `TrayIcon` in test runs, or give it a `live: false` mode that
  does everything but `Shell_NotifyIcon`.
- More icons from the same exe (one per live metric) need their own
  `uID` values (101 and up) on their own window, so they never share the
  main icon's remembered choice. Prefer `uID` to `guidItem`: a GUID is
  tied to the exe path, and the same GUID from a second build folder
  fails `NIM_ADD`. See "Per-sample tray icons" in [system.md](system.md).
- In a suite, the host owns this icon and features add menu rows. A
  toggle action gives the host a state function (`Func<bool>? IsOn` on
  the action), and the host adds `MF_CHECKED` (0x8) to that row while it
  returns true.

## Move a window

Window managers, "snap to left half", restore a layout. `SetWindowPos`
works on other apps' windows with no permission, except elevated ones
(see "Elevation and UIPI").

```csharp
internal static partial class WindowMover
{
    /// Moves a window so its visible frame fills `target` (physical pixels).
    public static bool MoveVisible(nint hwnd, Win32.RECT target)
    {
        if (IsZoomed(hwnd)) ShowWindow(hwnd, 9 /* SW_RESTORE */);   // a maximized window ignores moves
        Win32.GetWindowRect(hwnd, out var outer);
        Win32.DwmGetWindowAttribute(hwnd, Win32.DWMWA_EXTENDED_FRAME_BOUNDS, out var frame, Marshal.SizeOf<Win32.RECT>());
        // The invisible resize border: GetWindowRect includes it, the user never sees it.
        int left = frame.Left - outer.Left, top = frame.Top - outer.Top;
        int right = outer.Right - frame.Right, bottom = outer.Bottom - frame.Bottom;
        return SetWindowPos(hwnd, 0, target.Left - left, target.Top - top,
            target.Width + left + right, target.Height + top + bottom, 0x0004 /* SWP_NOZORDER */ | 0x0010 /* SWP_NOACTIVATE */);
    }

    /// The monitor's area without the taskbar, in physical pixels.
    public static Win32.RECT WorkArea(nint hwnd)
    {
        var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfoW(MonitorFromWindow(hwnd, 2 /* MONITOR_DEFAULTTONEAREST */), ref info);
        return info.rcWork;
    }

    /// Cloaked windows exist but are not on screen (other virtual desktops, suspended Store apps).
    public static bool IsCloaked(nint hwnd) =>
        DwmGetWindowAttribute(hwnd, 14 /* DWMWA_CLOAKED */, out int cloaked, sizeof(int)) == 0 && cloaked != 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO { public uint cbSize; public Win32.RECT rcMonitor, rcWork; public uint dwFlags; }

    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool IsZoomed(nint hwnd);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool ShowWindow(nint hwnd, int command);
    [LibraryImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
    [LibraryImport("user32.dll")] private static partial nint MonitorFromWindow(nint hwnd, uint flags);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool GetMonitorInfoW(nint monitor, ref MONITORINFO info);
    [LibraryImport("dwmapi.dll")] private static partial int DwmGetWindowAttribute(nint hwnd, int attribute, out int value, int size);
}

// Snap the front window to the left half of its monitor.
var front = Win32.GetForegroundWindow();
var area = WindowMover.WorkArea(front);
WindowMover.MoveVisible(front, new Win32.RECT { Left = area.Left, Top = area.Top, Right = area.Left + area.Width / 2, Bottom = area.Bottom });
```

Gotchas:

- DPI: every number here is a physical pixel. That holds only because the
  template's `app.manifest` declares `PerMonitorV2`. Without it, Windows
  scales the numbers and windows land in the wrong place on a 150 percent
  display. The app's own window takes epx through `UI/WindowBehavior.cs`.
- `DWMWA_EXTENDED_FRAME_BOUNDS`: on Windows 10 and 11, `GetWindowRect`
  includes an invisible resize border on the left, right, and bottom
  (measured here: 8 px each at 125 percent, 0 at the top). Skip the
  correction and "left half" leaves a gap and overlaps the
  next window.
- A window that moves to a monitor with another DPI scales itself after
  it arrives. Move it there first, then call `MoveVisible` again to set
  the final size.
- Skip windows that are cloaked, minimized (`IsIconic`), tool windows,
  and the shell (`Progman`, `WorkerW`, `Shell_TrayWnd` class names) when
  you list "real" windows with `EnumWindows`. The full filter a switcher
  used is "Window list for a switcher" in [system.md](system.md).
- The same border math places the app's own bordered panels: a panel
  moved by its outer rect shows smaller than asked. See "Place a panel by
  its visible frame" in [overlays.md](overlays.md).
- `SetWindowPos` returns false for an elevated window when the app is not
  elevated. Check the result and say why it did nothing.

## Launch at login

An unpackaged app starts at sign-in through a value under the user's Run
key. No admin rights, no prompt.

```csharp
using Microsoft.Win32;

public static class LoginItem
{
    private const string Name = "MyApp";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    /// The installed exe, never a build folder.
    private static string Command => $"\"{Path.Combine(AppContext.BaseDirectory, Name + ".exe")}\" --login";

    /// On only when the Run value exists and Task Manager has not turned it off.
    public static bool IsOn
    {
        get
        {
            using var run = Registry.CurrentUser.OpenSubKey(RunKey);
            if (run?.GetValue(Name) is not string) return false;
            using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey);
            return approved?.GetValue(Name) is not byte[] { Length: > 0 } state || state[0] != 3;
        }
    }

    public static void Set(bool on)
    {
        using var run = Registry.CurrentUser.CreateSubKey(RunKey);
        if (on) run.SetValue(Name, Command);
        else run.DeleteValue(Name, throwOnMissingValue: false);
        // Turning it on in the app also clears an "off" that Task Manager left behind.
        using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true);
        approved?.DeleteValue(Name, throwOnMissingValue: false);
    }

    public static bool StartedAtLogin => Environment.GetCommandLineArgs().Contains("--login");
}
```

Gotchas:

- The value stores a path. `build.ps1` installs to the same path every
  time (`%LOCALAPPDATA%\Programs\<App>`), so the entry survives rebuilds.
  Never register from a test run (`AppPaths.IsOverridden`).
- People turn startup apps off in Task Manager (Startup apps) or Settings
  (Apps > Startup). That leaves the Run value in place and writes a value
  of the same name under `StartupApproved\Run`. The first byte is 02 when
  on and 03 when off on this PC; this is what Task Manager writes, not a
  documented API. Read both, so the Settings toggle tells the truth.
- Started with `--login`, open quietly: no window, or the tray icon only.
  Never minimize the window at launch (see "Tray icon").
- `ms-settings:startupapps` opens the Startup apps page, for a "Manage in
  Windows Settings" link.
- Remove the value when the user uninstalls (`LoginItem.Set(false)` in
  an uninstall step), or Windows keeps trying to start a missing exe.

## Single instance

A second launch (Start menu, a URL, a file) hands its arguments to the
running copy and quits. The Windows App SDK's `AppInstance` does the
handover. It must run before any window exists, so the app gets its own
`Main`.

In the csproj, in the main `PropertyGroup`:

```xml
<DefineConstants>$(DefineConstants);DISABLE_XAML_GENERATED_MAIN</DefineConstants>
```

`Program.cs`:

```csharp
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace MyApp;

public static partial class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        // One key per database: a test copy (APP_STORE in %TEMP%) runs next to the real app.
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(AppPaths.Store.ToLowerInvariant())))[..12];
        var main = AppInstance.FindOrRegisterForKey("main-" + hash);
        if (!main.IsCurrent)
        {
            Redirect(main);
            return 0;
        }
        main.Activated += (_, e) => App.OnActivatedAgain(e);
        Application.Start(p =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new App();
        });
        return 0;
    }

    /// The redirect runs on another thread while this STA thread waits with
    /// CoWaitForMultipleObjects, as in Microsoft's sample. A plain Wait() here can deadlock.
    private static void Redirect(AppInstance target)
    {
        var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
        // The user just started this process, so it may hand the foreground to the running copy.
        AllowSetForegroundWindow(target.ProcessId);
        using var done = new ManualResetEvent(false);
        Task.Run(() =>
        {
            target.RedirectActivationToAsync(activation).AsTask().Wait();
            done.Set();
        });
        CoWaitForMultipleObjects(0, 0xFFFFFFFF, 1, [done.SafeWaitHandle.DangerousGetHandle()], out _);
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoWaitForMultipleObjects(uint flags, uint timeout, uint count, nint[] handles, out uint index);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllowSetForegroundWindow(uint processId);
}
```

In `App.xaml.cs`:

```csharp
/// A second launch handed over to this copy. Runs on a background thread.
public static void OnActivatedAgain(AppActivationArguments e)
{
    // The whole command line of the second launch: "C:\...\MyApp.exe" myapp://action/new
    var commandLine = e.Data is Windows.ApplicationModel.Activation.ILaunchActivatedEventArgs launch ? launch.Arguments : "";
    MainWindow?.DispatcherQueue.TryEnqueue(() =>
    {
        MainWindow.Activate();
        if (UrlScheme.Find(commandLine) is { } url) Actions.Run(url);   // the app's own action runner
    });
}
```

Gotchas:

- Tested: the second process exits with code 0 within a second, and the
  first one gets the full command line in `Arguments`.
- Key the instance on the store path, like above. The self-test, the
  snapshots, and the `agent-cli` check start copies on a `%TEMP%` store
  while the real app runs. A fixed key would redirect them into the
  user's real app.
- `Activated` arrives on a background thread. Touch the window only
  through its `DispatcherQueue`.

## Non-activating panel

Two different jobs on Windows:

1. **An overlay that never takes focus**: a keystroke display, a timer,
   a toolbar over other apps. Clicks work, typing does not: Windows sends
   keys only to the foreground window.
2. **A launcher that takes typing, then gives focus back**: remember the
   front window, activate the panel, and on Enter hide the panel and hand
   the foreground back before you paste.

```csharp
internal static partial class Panel
{
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x00000080;

    /// A borderless, always-on-top window with no taskbar button and no Alt+Tab entry.
    public static void MakeOverlay(Window window, bool takesFocus)
    {
        var hwnd = window.Handle();
        var style = GetWindowLongPtrW(hwnd, GWL_EXSTYLE) | (nint)WS_EX_TOOLWINDOW;
        if (!takesFocus) style |= (nint)WS_EX_NOACTIVATE;
        SetWindowLongPtrW(hwnd, GWL_EXSTYLE, style);
        window.AppWindow.IsShownInSwitchers = false;
        if (window.AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = false;
            presenter.IsMinimizable = false;
            presenter.IsMaximizable = false;
            presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
        }
    }

    [LibraryImport("user32.dll")] private static partial nint GetWindowLongPtrW(nint hwnd, int index);
    [LibraryImport("user32.dll")] private static partial nint SetWindowLongPtrW(nint hwnd, int index, nint value);
}

// 1. Overlay: show without taking focus.
overlay.AppWindow.Show(activateWindow: false);

// 2. Launcher, in the hotkey handler:
_previous = Win32.GetForegroundWindow();
launcher.Activate();
// On Enter:
launcher.AppWindow.Hide();
Win32.SetForegroundWindow(_previous);    // allowed: this app is in front right now
```

Gotchas:

- Build the panel once, a few seconds after launch, and keep it hidden.
  Creating a WinUI window on the first key press makes the first open slow
  (see the `performance` skill).
- Windows lets only the foreground app hand out the foreground. Give
  focus back while the panel is still in front, before any `await`.
- Position it with `AppWindow.Move` in physical pixels, on the monitor
  under the pointer or the front window, inside `WorkArea`.
- `SetBorderAndTitleBar(false, false)` is not borderless: `WS_DLGFRAME`
  stays, and the content sits 3 px inside the window. Fine for a panel;
  a frozen screen or a picker needs the fix in "Borderless overlay
  window" ([overlays.md](overlays.md)). A panel that keeps its border
  (for the rounded corners and shadow) has an invisible resize border, so
  place it by its visible frame (same file).
- Focus on the first open is lost. `Focus(FocusState.Programmatic)` right
  after showing does nothing while the content is not loaded, and
  `Activated` can arrive before `Loaded`. Focus again in `Loaded`, and
  once more through `DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, ...)`.
- A panel that never takes focus still draws a focus rectangle on its
  first button. Set `IsTabStop = false` on its buttons.
- `WS_EX_NOACTIVATE` stops activation by the window manager, but a click
  can still activate the window. Answer `WM_MOUSEACTIVATE` with
  `MA_NOACTIVATE` in a subclass ("Panels that never activate" in
  [overlays.md](overlays.md)).
- `AppWindow.IsVisible` once said false for a shown non-activating panel
  (one run in five), so a `Hide` that checked it did nothing. Check
  `IsWindowVisible(hwnd)` before you hide such a panel.
- A panel that hides on deactivate closes under a self-test when another
  test run takes the foreground. Turn hide-on-deactivate off while the
  test runs.

## Clipboard listener

Clipboard history and "copy, then act" features. Windows calls you on
every change; no polling, no permission. Read and write with the Win32
calls, not WinRT `Clipboard.GetContent`: only Win32 shows the marks other
tools leave (the `CanIncludeInClipboardHistory` value, `Clipboard Viewer
Ignore`) and lets you read only the formats you keep. Asking for a
format that its app renders on demand makes that app render it, and a
busy app (Excel with a large range) then blocks you.

```csharp
using System.Text;

internal sealed partial class ClipboardWatcher : IDisposable
{
    private const uint WM_CLIPBOARDUPDATE = 0x031D, CF_UNICODETEXT = 13, GMEM_MOVEABLE = 0x2;
    // Marks other tools put on content that a history must not keep.
    private static readonly uint Exclude = RegisterClipboardFormatW("ExcludeClipboardContentFromMonitorProcessing");
    private static readonly uint ViewerIgnore = RegisterClipboardFormatW("Clipboard Viewer Ignore");
    private static readonly uint CanInclude = RegisterClipboardFormatW("CanIncludeInClipboardHistory");
    // This app's own helper writes (a snippet pasted through the clipboard) carry this.
    private static readonly uint Own = RegisterClipboardFormatW("MyApp.Internal");

    // Listens, and owns every write: after OpenClipboard(0), EmptyClipboard leaves no owner
    // and SetClipboardData fails.
    private readonly MessageWindow _window = new();
    private uint _lastSequence;
    private bool _readScheduled;

    public event Action<string>? TextCopied;

    public ClipboardWatcher()
    {
        _window.Handler = (message, _, _) =>
        {
            if (message != WM_CLIPBOARDUPDATE) return false;
            if (!_readScheduled) { _readScheduled = true; _ = ReadSoonAsync(); }
            return true;
        };
        AddClipboardFormatListener(_window.Handle);
    }

    private async Task ReadSoonAsync()
    {
        await Task.Delay(100);   // one copy raises several updates while the app writes its formats
        _readScheduled = false;
        var sequence = GetClipboardSequenceNumber();
        if (sequence == _lastSequence) return;   // our own write, or read already
        _lastSequence = sequence;
        if (!await OpenAsync()) return;
        string? text;
        try { text = IsMarked() ? null : ReadText(); }
        finally { CloseClipboard(); }            // while it is open, no app on the PC can copy
        if (!string.IsNullOrWhiteSpace(text)) TextCopied?.Invoke(text);
    }

    /// One clipboard change with these formats. Returns false when another app held the clipboard.
    public async Task<bool> WriteAsync(IReadOnlyList<(uint Format, byte[] Data)> entries)
    {
        if (!await OpenAsync()) return false;
        try
        {
            EmptyClipboard();
            foreach (var (format, data) in entries) Put(format, data);
        }
        finally { CloseClipboard(); }
        _lastSequence = GetClipboardSequenceNumber();   // the listener skips this change
        return true;
    }

    public static (uint, byte[]) Text(string text) => (CF_UNICODETEXT, Encoding.Unicode.GetBytes(text + "\0"));

    /// Keeps a helper write out of this app's history, Win+V history, and other clipboard tools.
    public static IEnumerable<(uint, byte[])> Marks() => [(Own, [1]), (Exclude, [0]), (CanInclude, BitConverter.GetBytes(0u))];

    /// Another app may hold the clipboard open for a moment: ten tries, 50 ms apart.
    private async Task<bool> OpenAsync()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            if (OpenClipboard(_window.Handle)) return true;
            await Task.Delay(50);
        }
        return false;
    }

    private static bool IsMarked() =>
        IsClipboardFormatAvailable(Own) || IsClipboardFormatAvailable(Exclude) || IsClipboardFormatAvailable(ViewerIgnore)
        || (IsClipboardFormatAvailable(CanInclude) && ReadDword(CanInclude) == 0);

    private static unsafe uint? ReadDword(uint format)
    {
        var handle = GetClipboardData(format);
        if (handle == 0 || GlobalSize(handle) < 4) return null;
        var p = GlobalLock(handle);
        if (p == 0) return null;
        try { return *(uint*)p; }
        finally { GlobalUnlock(handle); }
    }

    private static unsafe string? ReadText()
    {
        if (!IsClipboardFormatAvailable(CF_UNICODETEXT)) return null;
        var handle = GetClipboardData(CF_UNICODETEXT);
        var p = handle == 0 ? 0 : GlobalLock(handle);
        if (p == 0) return null;
        try
        {
            var chars = new ReadOnlySpan<char>((char*)p, (int)(GlobalSize(handle) / 2));
            var end = chars.IndexOf('\0');
            return new string(end >= 0 ? chars[..end] : chars);
        }
        finally { GlobalUnlock(handle); }
    }

    private static unsafe void Put(uint format, byte[] data)
    {
        var memory = GlobalAlloc(GMEM_MOVEABLE, (nuint)data.Length);
        if (memory == 0) return;
        data.CopyTo(new Span<byte>((void*)GlobalLock(memory), data.Length));
        GlobalUnlock(memory);
        // Once SetClipboardData succeeds, Windows owns the memory. Free it only when it fails.
        if (SetClipboardData(format, memory) == 0) GlobalFree(memory);
    }

    public void Dispose()
    {
        RemoveClipboardFormatListener(_window.Handle);
        _window.Dispose();
    }

    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool AddClipboardFormatListener(nint hwnd);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool RemoveClipboardFormatListener(nint hwnd);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool OpenClipboard(nint owner);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool CloseClipboard();
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool EmptyClipboard();
    [LibraryImport("user32.dll")] private static partial nint GetClipboardData(uint format);
    [LibraryImport("user32.dll")] private static partial nint SetClipboardData(uint format, nint data);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool IsClipboardFormatAvailable(uint format);
    [LibraryImport("user32.dll")] private static partial uint GetClipboardSequenceNumber();
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)] private static partial uint RegisterClipboardFormatW(string name);
    [LibraryImport("kernel32.dll")] private static partial nint GlobalAlloc(uint flags, nuint bytes);
    [LibraryImport("kernel32.dll")] private static partial nint GlobalLock(nint memory);
    [LibraryImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool GlobalUnlock(nint memory);
    [LibraryImport("kernel32.dll")] private static partial nuint GlobalSize(nint memory);
    [LibraryImport("kernel32.dll")] private static partial nint GlobalFree(nint memory);
}
```

Gotchas:

- Skip all four marks: your own format, `ExcludeClipboardContentFromMonitorProcessing`
  (password managers), `Clipboard Viewer Ignore`, and a
  `CanIncludeInClipboardHistory` value of 0. Storing a copied password in
  a history database is a security bug.
- To keep a write out of cloud clipboard sync too, add a
  `CanUploadToCloudClipboard` format with a DWORD 0. This is from
  Microsoft's clipboard docs, from memory; Onyx did not use it.
- Read only what you keep, in one order: files (`CF_HDROP`, through
  `DragQueryFileW`), then text that is not only whitespace, then `PNG`,
  then `CF_DIBV5`, then `CF_DIB`. Excel and Word put text and a picture of
  a range on the clipboard; the text wins.
- `CF_DIB` has no file header. Add a 14-byte `BM` header to decode it
  with `BitmapDecoder`, and decode with `BitmapAlphaMode.Ignore`: a 32-bit
  screenshot often has alpha 0 everywhere and saves as a fully
  transparent PNG.
- An image write: `PNG` (browsers and chat apps read it) plus a
  bottom-up 32-bit `CF_DIB` for apps that do not. A files write:
  `CF_HDROP` (a 20-byte `DROPFILES` header with `fWide = 1`, then the
  paths, each ending in `\0`, then one more `\0`) plus the paths as text.
- `GetClipboardSequenceNumber` is the Windows `changeCount`. Store it
  before any other check, so a skipped copy is never read again.
- Keep `unsafe` on the sync helpers, not on the class: an `unsafe` class
  with `async` methods fails with CS4004.
- For a quick text or image write without marks, WinRT works:
  `Clipboard.SetContent(package)` then `Clipboard.Flush()`, retried three
  times on `COMException`. For an image, `package.SetBitmap(...)` plus
  `package.SetData("PNG", stream)`.

Self-test: never read or write the person's clipboard. Read and write
through an interface, swap in fakes, and post `WM_CLIPBOARDUPDATE` to the
listener's own window so the real message path, the 100 ms wait, and the
sequence check run. A write-then-restore cannot be exact (delayed
rendering, the owner changes), and every write lands in Win+V history.
The real Win32 paths are covered by manual checks.

## Paste into the front app

Write the clipboard, hand the foreground back to the target, wait until
the user lets go of the hotkey's modifiers, check the target is still in
front, then send Ctrl+V with `SendInput`. The same `Send` types text and
Backspaces for snippet expansion.

```csharp
internal static partial class Paste
{
    /// Marks every event this app sends, so a hook (its own, or the shared one in a suite)
    /// can tell them from other synthetic input.
    public const nuint Marker = 0x4F4E5958;   // "ONYX"
    private const ushort VK_BACK = 0x08, VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, V = 0x56, VK_LWIN = 0x5B, VK_RWIN = 0x5C;

    /// False when the keys were not sent: the clipboard holds the text, so say "press Ctrl+V".
    /// target: "the window you were using" from a foreground tracker, not GetForegroundWindow now
    /// (that is your own panel). hidePanel hides the launcher or history panel.
    public static async Task<bool> IntoAsync(nint target, string text, ClipboardWatcher clipboard, Action hidePanel)
    {
        if (!await clipboard.WriteAsync([ClipboardWatcher.Text(text)])) return false;
        if (target == 0 || !IsWindow(target)) return false;
        if (Elevation.IsElevated(target) != false) return false;   // UIPI would drop the keys silently
        Win32.SetForegroundWindow(target);    // while this app is still in front
        hidePanel();
        // Still holding Alt or Win from Win+Alt+V? Then Ctrl+V arrives as Ctrl+Alt+V.
        for (var waited = 0; waited < 500 && ModifierDown(); waited += 10) await Task.Delay(10);
        await Task.Delay(60);
        // The keys go to whatever is in front. Send them only to the window you mean.
        if (GetAncestor(Win32.GetForegroundWindow(), 2 /* GA_ROOT */) != target) return false;
        return Send([Key(VK_CONTROL, false), Key(V, false), Key(V, true), Key(VK_CONTROL, true)]);
    }

    /// Backspaces, then text as Unicode key events, in one call, so the person's own typing
    /// never lands in between. Both halves of a surrogate pair (an emoji) stay together.
    public static bool BackspaceAndType(int backspaces, string text)
    {
        var inputs = new List<INPUT>();
        for (var i = 0; i < backspaces; i++) { inputs.Add(Key(VK_BACK, false)); inputs.Add(Key(VK_BACK, true)); }
        foreach (var c in text) { inputs.Add(Unit(c, false)); inputs.Add(Unit(c, true)); }
        return Send(inputs);
    }

    private static unsafe bool Send(List<INPUT> inputs)
    {
        var array = inputs.ToArray();
        fixed (INPUT* p = array) return SendInput((uint)array.Length, p, sizeof(INPUT)) == array.Length;
    }

    private static bool ModifierDown() =>
        new[] { VK_MENU, VK_LWIN, VK_RWIN, VK_SHIFT }.Any(vk => (GetAsyncKeyState(vk) & 0x8000) != 0);

    private static INPUT Key(ushort vk, bool up) =>
        new() { type = 1 /* INPUT_KEYBOARD */, u = new() { ki = new() { wVk = vk, dwFlags = up ? 0x2u /* KEYEVENTF_KEYUP */ : 0u, dwExtraInfo = Marker } } };

    private static INPUT Unit(char c, bool up) =>
        new() { type = 1, u = new() { ki = new() { wScan = c, dwFlags = 0x4u /* KEYEVENTF_UNICODE */ | (up ? 0x2u : 0u), dwExtraInfo = Marker } } };

    // INPUT holds a union. Declare the mouse member too, so sizeof(INPUT) is 40 on 64-bit Windows
    // with ki at offset 8. With only KEYBDINPUT, SendInput sends nothing and returns 0.
    [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint type; public InputUnion u; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public nuint dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public nuint dwExtraInfo; }

    [LibraryImport("user32.dll")] private static unsafe partial uint SendInput(uint count, INPUT* inputs, int size);
    [LibraryImport("user32.dll")] private static partial short GetAsyncKeyState(int key);
    [LibraryImport("user32.dll")] private static partial nint GetAncestor(nint hwnd, uint flags);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool IsWindow(nint hwnd);
}
```

Gotchas:

- `SendInput` reports success even when UIPI blocked the keys: Microsoft
  documents that neither the return value nor `GetLastError` shows a UIPI
  block. Check the target's elevation first, and tell the user: "Copied.
  Press Ctrl+V: Terminal runs as administrator."
- Check `GetForegroundWindow` (its root, through `GetAncestor`) right
  before every `SendInput`, not once at the start. Another window can take
  the front between the hide and the send, and the keys then go there.
- Mark every event with `dwExtraInfo`. Your hook sees your own keys as
  injected; the marker tells them apart from voice typing, on-screen
  keyboards, and AutoHotkey, which you may still want to see.
- Count Backspaces by text elements (`StringInfo`), not UTF-16 units, so
  an emoji in a keyword does not leave half a surrogate pair.
- `LibraryImport` with a `char` parameter does not build (SYSLIB1051).
  `VkKeyScanExW` and similar calls take `ushort`.
- Some apps (terminals, remote desktops, games) ignore Ctrl+V or read raw
  input. Offer "Copied, press Ctrl+V" as the fallback.
- Text longer than a line goes through the clipboard with `Marks()`, and
  the person's clipboard comes back about 350 ms later. Snapshot every
  `HGLOBAL` format before you write, skip the GDI handle formats and the
  ones Windows makes on its own (`CF_TEXT`, `CF_OEMTEXT`, `CF_LOCALE`),
  and write the snapshot back with the same marks.

Self-test: send keys only into a test window of the app's own, check
`GetForegroundWindow()` right before every send, send nothing when
another window is in front, and retry when another test run took the
front. A `TextBox` whose `Paste` handler sets `Handled = true` proves
Ctrl+V arrived without reading the clipboard.

## Low-level keyboard hook

For hold-to-show (hold Alt, press a key), key remapping, snippet
expansion, or a keystroke overlay: every key press on the desktop, before
any app sees it. No permission. Prefer `RegisterHotKey` when a plain
combo is enough. In a suite, the host runs one of these for every feature
(SKILL.md, "Suites").

```csharp
internal static unsafe partial class KeyboardHook
{
    public readonly record struct KeyEvent(int VirtualKey, int ScanCode, bool Down, bool Injected,
        bool AltDown, bool Extended, nuint ExtraInfo);

    private static Func<KeyEvent, bool>? _onKey;
    private static uint _threadId;

    /// onKey returns true to swallow the key. Runs on the hook thread: copy what you need and return.
    public static void Start(Func<KeyEvent, bool> onKey)
    {
        _onKey = onKey;
        // Its own thread with its own message loop: a busy UI thread would lag every key on the PC.
        var thread = new Thread(() =>
        {
            _threadId = GetCurrentThreadId();
            var hook = SetWindowsHookExW(13 /* WH_KEYBOARD_LL */, &Proc, GetModuleHandleW(null), 0);
            if (hook == 0) { AgentLog.Line($"keyboard hook failed: {Marshal.GetLastPInvokeError()}"); return; }
            while (GetMessageW(out _, 0, 0, 0) > 0) { }
            UnhookWindowsHookEx(hook);
        }) { IsBackground = true, Name = "Keyboard hook" };
        thread.Start();
    }

    public static void Stop() => PostThreadMessageW(_threadId, 0x0012 /* WM_QUIT */, 0, 0);

    [UnmanagedCallersOnly]
    private static nint Proc(int code, nint wParam, nint lParam)
    {
        if (code >= 0)
        {
            var info = (KBDLLHOOKSTRUCT*)lParam;
            var e = new KeyEvent((int)info->vkCode, (int)info->scanCode,
                Down: wParam is 0x100 or 0x104,                 // WM_KEYDOWN, WM_SYSKEYDOWN
                Injected: (info->flags & 0x10) != 0,            // LLKHF_INJECTED: SendInput, yours or another app's
                AltDown: (info->flags & 0x20) != 0,             // LLKHF_ALTDOWN
                Extended: (info->flags & 0x01) != 0,            // LLKHF_EXTENDED: arrows, right Ctrl, keypad Enter
                ExtraInfo: (nuint)info->dwExtraInfo);
            try
            {
                if (_onKey?.Invoke(e) == true) return 1;
            }
            catch (Exception ex)
            {
                // An exception that leaves an unmanaged callback ends the process.
                AgentLog.Line($"keyboard hook: {ex.Message}");
            }
        }
        return CallNextHookEx(0, code, wParam, lParam);
    }

    [StructLayout(LayoutKind.Sequential)] private struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public nint dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct MSG { public nint hwnd; public uint message; public nint wParam, lParam; public uint time; public int x, y, lPrivate; }

    [LibraryImport("user32.dll", SetLastError = true)] private static partial nint SetWindowsHookExW(int id, delegate* unmanaged<int, nint, nint, nint> proc, nint module, uint threadId);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool UnhookWindowsHookEx(nint hook);
    [LibraryImport("user32.dll")] private static partial nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);
    [LibraryImport("user32.dll")] private static partial int GetMessageW(out MSG msg, nint hwnd, uint min, uint max);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool PostThreadMessageW(uint thread, uint message, nint wParam, nint lParam);
    [LibraryImport("kernel32.dll")] private static partial uint GetCurrentThreadId();
    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)] private static partial nint GetModuleHandleW(string? name);
}
```

Gotchas:

- The hook needs a message loop on the thread that installed it. Windows
  calls the hook by sending that thread a message.
- Keep the callback tiny: queue the event, then hand work to the UI
  thread with `DispatcherQueue.TryEnqueue`. Windows waits at most 1 second
  (`LowLevelHooksTimeout`, capped at 1000 ms since Windows 10 1709), and
  on Windows 7 and later a hook that times out is removed silently. The
  app cannot tell. If the feature stops working after a hang, reinstall
  the hook.
- Filter injected keys in the consumer, not in the hook. A keystroke
  overlay drops them; a switcher may want them; a snippet engine drops
  only its own (`ExtraInfo == Paste.Marker`).
- Return 1 only for keys you really consume. Swallowing a key-down but not
  its key-up leaves Windows thinking the key is still held. Keep a set of
  swallowed key-downs and swallow their key-ups whatever your state:
  Enter can close a switcher, and its key-up comes later.
- A hook in a normal app does not see keys typed into an elevated window.
- Watching keys only (a keystroke overlay): use this hook and return
  false. Raw Input marks injected keys only by `hDevice == 0`, needs a
  window for `WM_INPUT`, and has no Alt flag.
- Key names: `GetKeyNameText` needs the extended bit, or the arrows read
  "Num 4". For letters and punctuation, `MapVirtualKeyEx(vk,
  MAPVK_VK_TO_CHAR, layout)` with the layout of the foreground window's
  thread (`GetKeyboardLayout`) shows a German layout's own keys. For typed
  text, `ToUnicodeEx` with flag 0x4 and a key state array built from the
  modifiers you tracked.
- AltGr arrives as a fake left Ctrl (scan code `0x21D`) plus right Alt.
  While both are down, count neither as a modifier, or `@` on a German
  layout reads as Ctrl+Alt+Q and a trigger on Alt fires.
- Match a trigger key by `scanCode` (0x29 is the key above Tab on every
  layout), and require Alt without Ctrl or Win.
- Swallowing Alt+key and then releasing Alt opens the menu bar of the
  window behind. Sending one press of the unassigned key 0xE8 while Alt
  is down stops it (the AutoHotkey trick). Onyx's switcher does this; the
  menu bar check stayed manual.
- `GetAsyncKeyState` in the callback is fine for Shift, Ctrl, and Win;
  only the current key's own state lags.

Self-test: call the handler with synthetic `KeyEvent`s instead of
`SendInput`. Injected keys are dropped by design, and `SendInput` would
type into whatever is in front. Nothing reaches the person's windows, and
the real code from the handler on runs. The physical key press stays a
manual check.

## Screen capture

Screenshots, pickers, window thumbnails, and recording. No permission
prompt. Pick the API by what is captured:

| Capture | API | Why |
| --- | --- | --- |
| A display or an area | GDI `BitBlt` of the virtual screen ([capture.md](capture.md), "Screen freeze") | No border, no prompt, every display at every scale in one call, single-digit ms for a 600 x 400 area |
| A picker (area, window, color) | Freeze first, then show the picker over the frozen image (capture.md) | The picker can never be in its own picture, and the screen holds still while the person aims |
| One window | `GraphicsCaptureItem.TryCreateFromWindowId`, below | Reads the window's own content even when other windows cover it. Fall back to cropping the frozen screen |
| A recording | `Windows.Graphics.Capture` into Media Foundation ([capture.md](capture.md)) | GPU frames, hardware H.264 |
| Live previews | `DwmRegisterThumbnail` ([capture.md](capture.md)) | DWM draws them; no frames to copy |

`Windows.Graphics.Capture` draws a yellow border around what it captures.
On Windows 11 the person can allow borderless capture. Read the state
with `CheckAccess` (it never prompts), and call `RequestAccessAsync` only
on a real capture or from an "Ask again" button:

```csharp
using Windows.Foundation.Metadata;
using Windows.Graphics.Capture;
using Windows.Security.Authorization.AppCapabilityAccess;

internal static class CaptureBorder
{
    public static bool CanHide => ApiInformation.IsPropertyPresent(typeof(GraphicsCaptureSession).FullName, "IsBorderRequired");

    /// Null when this Windows has no such setting. No prompt.
    public static AppCapabilityAccessStatus? State()
    {
        if (!CanHide) return null;
        try { return AppCapability.Create("graphicsCaptureWithoutBorder").CheckAccess(); }
        catch (Exception e) when (e is COMException or UnauthorizedAccessException or ArgumentException) { return null; }
    }

    /// May show a Windows prompt. Never from a test run.
    public static async Task<AppCapabilityAccessStatus?> AskAsync()
    {
        if (!CanHide) return null;
        try { return await GraphicsCaptureAccess.RequestAccessAsync(GraphicsCaptureAccessKind.Borderless); }
        catch (Exception e) when (e is COMException or UnauthorizedAccessException or InvalidCastException) { return null; }
    }
}
```

On the PC that built Onyx (Windows 11 26200, unpackaged),
`CheckAccess` returned `Allowed` with no prompt and no package
capability. The `RequestAccessAsync` prompt was never seen.

One window, cropped to what people see:

```csharp
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;

internal static class WindowShot
{
    private static IDirect3DDevice? _device;   // one for the app's lifetime; creating one per capture is slow

    public static bool IsSupported =>
        ApiInformation.IsMethodPresent(typeof(GraphicsCaptureItem).FullName, nameof(GraphicsCaptureItem.TryCreateFromWindowId))
        && GraphicsCaptureSession.IsSupported();

    /// BGRA pixels of the window's visible frame, or null. Physical pixels.
    public static async Task<(byte[] Bgra, int Width, int Height)?> CaptureAsync(nint hwnd)
    {
        if (!IsSupported) return null;
        // Windows 11: no COM interop needed.
        var item = GraphicsCaptureItem.TryCreateFromWindowId(new global::Windows.UI.WindowId((ulong)hwnd));
        if (item is null) return null;
        _device ??= Direct3D.CreateDevice();
        Win32.GetWindowRect(hwnd, out var outer);
        Win32.DwmGetWindowAttribute(hwnd, Win32.DWMWA_EXTENDED_FRAME_BOUNDS, out var visible, Marshal.SizeOf<Win32.RECT>());

        using var pool = Direct3D11CaptureFramePool.CreateFreeThreaded(_device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 1, item.Size);
        using var session = pool.CreateCaptureSession(item);
        session.IsCursorCaptureEnabled = false;
        if (CaptureBorder.CanHide && CaptureBorder.State() == AppCapabilityAccessStatus.Allowed)
        {
            try { session.IsBorderRequired = false; }
            catch (Exception e) when (e is COMException or UnauthorizedAccessException) { }
        }
        var arrived = new TaskCompletionSource<Direct3D11CaptureFrame?>(TaskCreationOptions.RunContinuationsAsynchronously);
        pool.FrameArrived += (sender, _) => arrived.TrySetResult(sender.TryGetNextFrame());
        session.StartCapture();
        using var frame = await arrived.Task.WaitAsync(TimeSpan.FromSeconds(2));
        if (frame is null) return null;
        using var bitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface, BitmapAlphaMode.Premultiplied);
        int w = bitmap.PixelWidth, h = bitmap.PixelHeight;
        var bytes = new byte[w * h * 4];
        bitmap.CopyToBuffer(bytes.AsBuffer());

        // The frame is the window rect, invisible resize border included. Keep the visible frame.
        if (w == visible.Width && h == visible.Height) return (bytes, w, h);
        int x = Math.Max(0, visible.Left - outer.Left), y = Math.Max(0, visible.Top - outer.Top);
        int cw = Math.Min(visible.Width, w - x), ch = Math.Min(visible.Height, h - y);
        var cropped = new byte[cw * ch * 4];
        for (var row = 0; row < ch; row++) Array.Copy(bytes, ((y + row) * w + x) * 4, cropped, row * cw * 4, cw * 4);
        return (cropped, cw, ch);
    }
}

internal static partial class Direct3D
{
    private static readonly Guid IDXGIDevice = new("54EC77FA-1377-44E6-8C32-88FD5F44C84C");

    /// A WinRT device for capture. A recorder needs the device and context kept instead (capture.md).
    public static IDirect3DDevice CreateDevice()
    {
        Marshal.ThrowExceptionForHR(D3D11CreateDevice(0, 1 /* HARDWARE */, 0, 0x20 /* BGRA_SUPPORT */, 0, 0, 7 /* SDK_VERSION */, out var device, out _, out var context));
        try
        {
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(device, in IDXGIDevice, out var dxgi));
            try
            {
                Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi, out var inspectable));
                try { return WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(inspectable); }
                finally { Marshal.Release(inspectable); }
            }
            finally { Marshal.Release(dxgi); }
        }
        finally
        {
            Marshal.Release(context);
            Marshal.Release(device);
        }
    }

    [LibraryImport("d3d11.dll")]
    private static partial int D3D11CreateDevice(nint adapter, int driverType, nint software, uint flags, nint levels, uint levelCount, uint sdkVersion, out nint device, out int level, out nint context);
    [LibraryImport("d3d11.dll")]
    private static partial int CreateDirect3D11DeviceFromDXGIDevice(nint dxgiDevice, out nint graphicsDevice);
}
```

Gotchas:

- Measured at 125 percent: an 800 x 600 window rect captured as
  800 x 600, with a visible frame of 784 x 592. Without the crop, the
  shot has a transparent border on three sides.
- `TryCreateFromWindowId` is Windows 11. On Windows 10 19041 and later,
  create the item through `IGraphicsCaptureItemInterop.CreateForWindow`
  (slot 3) or `CreateForMonitor` (slot 4) on the
  `Windows.Graphics.Capture.GraphicsCaptureItem` activation factory. The
  recorder in [capture.md](capture.md) does it with a vtable call.
- When the capture fails (a protected window, a timeout), crop the window
  from the frozen screen instead. That copy shows whatever covers it.
- A minimized window draws nothing and sends no frames. Restore it first.
- DRM video and some protected windows capture as black.
- A window excluded from capture (`WDA_EXCLUDEFROMCAPTURE`) is missing
  from every capture, including `PrintWindow`. See [overlays.md](overlays.md).
- Inside a namespace named `MyApp.Features.Windows`, `Windows.UI.WindowId`
  resolves to that namespace (CS0234). Write `global::Windows...`, or
  name the namespace something else.
- Never call a capture from a self-test on the person's screen. See "Test
  captures safely" in [capture.md](capture.md).

## Toast notifications

`AppNotificationManager` from the Windows App SDK works in an unpackaged,
self-contained app. Register once at launch, before you show anything.

```csharp
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

public static class Toasts
{
    /// In OnLaunched, before the first Show. onClick runs on a background thread.
    public static void Register(Action<IDictionary<string, string>> onClick)
    {
        AppNotificationManager.Default.NotificationInvoked += (_, args) => onClick(args.Arguments);
        AppNotificationManager.Default.Register();
    }

    /// Off when the user turned the app's notifications off in Settings, or a policy did.
    public static bool Allowed => AppNotificationManager.Default.Setting == AppNotificationSetting.Enabled;

    public static void Show(string title, string body, string itemId)
    {
        var toast = new AppNotificationBuilder()
            .AddArgument("action", "open")
            .AddArgument("item", itemId)
            .AddText(title)
            .AddText(body)
            .BuildNotification();
        AppNotificationManager.Default.Show(toast);
    }

    /// In the uninstall step: removes what Register wrote for this app.
    public static void RemoveRegistration() => AppNotificationManager.Default.UnregisterAll();
}
```

Gotchas:

- AppUserModelID: an unpackaged app has no package identity, and the
  older `Windows.UI.Notifications` API needed a Start menu shortcut with
  an AppUserModelID and a COM activator. `AppNotificationManager.Register`
  sets up the registration for an unpackaged app itself, so use it rather
  than the old API.
- Elevated apps cannot show notifications: `Show` fails without an error.
  Test as the normal user.
- A click on a toast when the app is closed starts the app. With "Single
  instance", a click while it runs reaches `NotificationInvoked` in the
  running copy. Microsoft's guidance: call `Register` before
  `AppInstance.GetActivatedEventArgs`.
- Do not disturb hides banners; the toast still lands in the
  notification center. Never put anything there that the app does not
  also show.
- `Allowed` false: show "Notifications are off for MyApp" in Settings
  with a link to `ms-settings:notifications`.

## URL protocol

`myapp://action/new?title=Milk` from scripts, Stream Deck, PowerToys
Run, or a link. An unpackaged app registers the scheme under the user's
classes key. No admin rights.

```csharp
using Microsoft.Win32;
using System.Text.RegularExpressions;

public static partial class UrlScheme
{
    public const string Scheme = "myapp";

    /// Run from the installed exe (build.ps1 or first launch), never from a test run.
    public static void Register()
    {
        var exe = Path.Combine(AppContext.BaseDirectory, "MyApp.exe");
        using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{Scheme}");
        key.SetValue("", "URL:MyApp");
        key.SetValue("URL Protocol", "");
        using (var icon = key.CreateSubKey("DefaultIcon")) icon.SetValue("", $"\"{exe}\",0");
        using var command = key.CreateSubKey(@"shell\open\command");
        command.SetValue("", $"\"{exe}\" \"%1\"");
    }

    public static void Unregister() => Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{Scheme}", throwOnMissingSubKey: false);

    /// The first myapp:// URL in a command line, or null.
    public static Uri? Find(string commandLine) =>
        UrlPattern().Match(commandLine) is { Success: true } m && Uri.TryCreate(m.Value, UriKind.Absolute, out var uri) ? uri : null;

    [GeneratedRegex(@"myapp://[^\s""]+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();
}
```

Test it with `Start-Process "myapp://action/new"`. At launch, read
`UrlScheme.Find(Environment.CommandLine)`. With "Single instance", a URL
opened while the app runs reaches `OnActivatedAgain` in the running copy.

Gotchas:

- Any web page can open a `myapp://` link (the browser asks the user
  once). Treat the URL as untrusted input: read actions and actions that
  can be undone only. Never delete or send from a URL without asking in
  the app.
- The registration stores the exe path. Install to the same path every
  time, and remove the key on uninstall.
- Windows App SDK also offers
  `ActivationRegistrationManager.RegisterForProtocolActivation` for
  unpackaged apps. The plain registry key above is easier to read and to
  remove.

## Debug bridge

A development-only command channel, so an agent can drive the running
app and read its state. If the app has the agent CLI (`agent-cli` skill),
that pipe is the bridge; skip this recipe. Otherwise, the app listens on
a named pipe only when `APP_DEBUG_PIPE` is set.

```csharp
using System.IO.Pipes;
using System.Text.Json;

/// APP_DEBUG_PIPE=<name> makes the app answer one-line commands on that pipe.
public static class DebugBridge
{
    public static void StartIfRequested(MainWindow window, AppModel model)
    {
        var name = Environment.GetEnvironmentVariable("APP_DEBUG_PIPE");
        if (string.IsNullOrEmpty(name)) return;
        var queue = window.DispatcherQueue;
        _ = Task.Run(async () =>
        {
            while (true)
            {
                await using var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync();
                using var reader = new StreamReader(pipe, leaveOpen: true);
                var command = await reader.ReadLineAsync() ?? "status";
                var done = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                queue.TryEnqueue(() =>
                {
                    try { done.SetResult(Run(command, window, model)); }
                    catch (Exception e) { done.SetResult(JsonSerializer.Serialize(new { error = e.Message })); }
                });
                await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
                await writer.WriteLineAsync(await done.Task);
                pipe.WaitForPipeDrain();
            }
        });
    }

    /// Runs on the UI thread, like a button. Returns one line of JSON.
    private static string Run(string command, MainWindow window, AppModel model)
    {
        var words = command.Split(' ', 2);
        switch (words[0])
        {
            case "open" when words.Length == 2: window.Show(words[1]); break;
            case "new": model.AddItem(words.Length == 2 ? words[1] : ""); break;
        }
        return JsonSerializer.Serialize(new { page = model.Page.Id(), items = model.Items.Count, selected = model.Selection?.Title });
    }
}
```

Start it in `OnLaunched` after the window exists. A script drives it:

```powershell
function Send-Debug([string]$Command) {
    $pipe = New-Object System.IO.Pipes.NamedPipeClientStream('.', $env:APP_DEBUG_PIPE, [System.IO.Pipes.PipeDirection]::InOut)
    $pipe.Connect(5000)
    $writer = New-Object System.IO.StreamWriter($pipe); $writer.AutoFlush = $true
    $writer.WriteLine($Command)
    $line = (New-Object System.IO.StreamReader($pipe)).ReadLine()
    $pipe.Dispose(); $line
}
Send-Debug 'new Buy milk'      # {"page":"all","items":1,"selected":"Buy milk"}
```

Gotchas:

- Set `APP_DEBUG_PIPE` and `APP_STORE` (in `%TEMP%`) only for test runs.
  A shipped app without the variable never opens the pipe.
- `CurrentUserOnly`: only the same Windows user can connect.
- Commands run on the UI thread through the model, like buttons do. The
  `test-windows-app` skill builds on this.

## Elevation and UIPI

Windows has no Accessibility or Input Monitoring permission. The wall
that replaces them is User Interface Privilege Isolation (UIPI): a normal
app cannot send input to, hook keys from, or move windows of an app that
runs as administrator.

```csharp
internal static partial class Elevation
{
    /// This process runs elevated (.NET 8 and later).
    public static bool ThisAppIsElevated => Environment.IsPrivilegedProcess;

    /// True or false for the window's process; null when Windows will not say,
    /// which in practice means it is elevated or a protected system process.
    public static bool? IsElevated(nint hwnd)
    {
        Win32.GetWindowThreadProcessId(hwnd, out var pid);
        var process = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
        if (process == 0) return null;
        try
        {
            if (!OpenProcessToken(process, 0x8 /* TOKEN_QUERY */, out var token)) return null;
            try
            {
                return GetTokenInformation(token, 20 /* TokenElevation */, out var elevated, sizeof(int), out _) ? elevated != 0 : null;
            }
            finally { CloseHandle(token); }
        }
        finally { CloseHandle(process); }
    }

    [LibraryImport("kernel32.dll")] private static partial nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);
    [LibraryImport("advapi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool OpenProcessToken(nint process, uint access, out nint token);
    [LibraryImport("advapi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool GetTokenInformation(nint token, int infoClass, out int info, int length, out int returned);
    [LibraryImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool CloseHandle(nint handle);
}
```

What UIPI blocks for a normal app, against an elevated window:

- `SendInput`: the keys vanish, and the call still reports success.
- Low-level keyboard hooks: no keys typed into the elevated window.
- Moving and resizing its windows. Window managers such as PowerToys
  FancyZones have to run elevated to move elevated windows.
- `SendMessage` and `PostMessage` to its windows: Microsoft documents that
  they return success and drop the message.
- Drag and drop from Explorer into an elevated app.

Rules:

- Run as the normal user. Never set `requireAdministrator` in
  `app.manifest`: the app would prompt on every launch, lose toasts
  (they fail when elevated), and refuse drops from Explorer.
- Before you act on another window, check `Elevation.IsElevated`. When it
  is not `false`, show one line: "This window runs as administrator.
  MyApp can only paste into it when MyApp also runs as administrator."
- `uiAccess="true"` in the manifest lifts UIPI for accessibility tools,
  but only for a signed exe installed under Program Files. An unpackaged
  BAWA app in `%LOCALAPPDATA%` does not qualify.

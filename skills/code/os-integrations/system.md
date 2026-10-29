# System recipes

Reading what the rest of Windows knows: other apps' windows, their icons,
the search index, another app's focused control, the power manager, and
the process table. Plus tray icons drawn per sample. Each shape ran in
a tray suite built with BAWA on Windows 11 build
26200, unless a line says otherwise.

## Contents

- Window list for a switcher
- App and file icons
- Windows Search
- Per-sample tray icons
- UI Automation from WinUI
- Power requests
- Ports and command lines

## Window list for a switcher

`EnumWindows` walks every top-level window front to back. Most are not
windows a person would switch to. Read a few facts per window, then
filter them in a pure function with unit tests.

```csharp
public sealed record WindowInfo(nint Handle, uint Pid, string Path, string Title, string ClassName,
    bool Visible, bool Minimized, bool Cloaked, bool Owned, bool ToolWindow, bool AppWindow, bool NoActivate,
    double WidthEpx, double HeightEpx);

public static class WindowFilter
{
    // The desktop (Progman, titled "Program Manager") is visible, unowned, not a tool window,
    // not cloaked, and full screen. These are not windows to switch to.
    private static readonly HashSet<string> ShellClasses = ["Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd"];

    public static bool Keeps(WindowInfo w)
    {
        if (!w.Visible || w.Minimized || w.Cloaked || w.ToolWindow) return false;
        // An owned window (a dialog) or a no-activate window (an overlay) counts only when it
        // asks for a taskbar button with WS_EX_APPWINDOW, as in Windows' own Alt+Tab.
        if ((w.Owned || w.NoActivate) && !w.AppWindow) return false;
        if (ShellClasses.Contains(w.ClassName)) return false;
        return w.WidthEpx >= 60 && w.HeightEpx >= 60;
    }
}

internal static unsafe class WindowList
{
    [ThreadStatic] private static List<nint>? _found;

    [UnmanagedCallersOnly]
    private static int OnWindow(nint hwnd, nint data) { _found?.Add(hwnd); return 1; }

    /// Every top-level window, front to back.
    public static List<nint> All()
    {
        _found = [];
        EnumWindows(&OnWindow, 0);
        var list = _found;
        _found = null;
        return list;
    }

    // Describe(hwnd): IsWindowVisible first (most windows are hidden), then GWL_EXSTYLE
    // (WS_EX_TOOLWINDOW 0x80, WS_EX_APPWINDOW 0x40000, WS_EX_NOACTIVATE 0x08000000),
    // GetWindow(GW_OWNER), DWMWA_CLOAKED, IsIconic, the class name, the visible frame divided by
    // the monitor's scale, and the process path from QueryFullProcessImageNameW.
}
```

Gotchas:

- Cloaked windows (`DWMWA_CLOAKED`) are on other virtual desktops or are
  suspended Store apps: visible to `IsWindowVisible`, not on screen.
- Packaged apps (Settings, Calculator) live inside
  `ApplicationFrameHost.exe`. Name them after the process of a child
  window (`EnumChildWindows`) whose pid differs from the frame's.
- An app's name is the exe's `FileVersionInfo.FileDescription`
  ("Microsoft Edge"), else its file name. Cache by path, with a cap.
- Leave out your own windows. A foreground tracker that knows "own or
  shell" (by process id and the shell class names, plus
  `NotifyIconOverflowWindow`) does both jobs.
- A window picker for screenshots also drops click-through windows
  (`WS_EX_TRANSPARENT`) and untitled tool windows.
- Self-test: feed `WindowFilter` recorded `WindowInfo` rows, and run the
  live switcher only over test windows of your own.

## App and file icons

A switcher and a launcher both need the icon of an exe, a shortcut, or a
file, as a XAML image. Try the shell thumbnail first; it handles `.lnk`
targets and most files. It throws `COMException` for
`C:\Windows\explorer.exe` and `System32\cmd.exe` on Windows 11 (Notepad
and Task Manager work). Then take the exe's own icon.

```csharp
internal static partial class ShellIcon
{
    public static async Task<ImageSource?> LoadAsync(string path, int size = 64)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            using var thumb = await file.GetThumbnailAsync(ThumbnailMode.SingleItem, (uint)size);
            if (thumb is { Size: > 0 })
            {
                var image = new BitmapImage();
                await image.SetSourceAsync(thumb);
                return image;
            }
        }
        catch (Exception e) when (e is COMException or UnauthorizedAccessException or FileNotFoundException or ArgumentException) { }
        return FromExe(path, size);
    }

    /// The first icon in the file at this size, copied from the HICON's color bitmap.
    public static unsafe ImageSource? FromExe(string path, int size)
    {
        nint icon = 0;
        try
        {
            if (SHDefExtractIconW(path, 0, 0, &icon, null, (uint)size) != 0 || icon == 0) return null;
            if (!GetIconInfo(icon, out var info)) return null;
            try
            {
                var header = new BITMAPINFOHEADER { biSize = (uint)sizeof(BITMAPINFOHEADER), biWidth = size, biHeight = -size, biPlanes = 1, biBitCount = 32 };
                var pixels = new byte[size * size * 4];
                var dc = GetDC(0);
                try { fixed (byte* p = pixels) if (GetDIBits(dc, info.hbmColor, 0, (uint)size, p, &header, 0) == 0) return null; }
                finally { ReleaseDC(0, dc); }
                // Old icons have no alpha channel: treat every pixel as opaque.
                if (!pixels.Where((_, i) => i % 4 == 3).Any(a => a != 0))
                    for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
                // WriteableBitmap wants premultiplied BGRA.
                for (var i = 0; i < pixels.Length; i += 4)
                {
                    var a = pixels[i + 3];
                    if (a == 255) continue;
                    pixels[i] = (byte)(pixels[i] * a / 255);
                    pixels[i + 1] = (byte)(pixels[i + 1] * a / 255);
                    pixels[i + 2] = (byte)(pixels[i + 2] * a / 255);
                }
                var bitmap = new WriteableBitmap(size, size);
                using (var stream = bitmap.PixelBuffer.AsStream()) stream.Write(pixels);
                bitmap.Invalidate();
                return bitmap;
            }
            finally
            {
                if (info.hbmColor != 0) DeleteObject(info.hbmColor);
                if (info.hbmMask != 0) DeleteObject(info.hbmMask);
            }
        }
        finally { if (icon != 0) DestroyIcon(icon); }
    }

    // BOOL as int, so the struct stays blittable for LibraryImport.
    [StructLayout(LayoutKind.Sequential)] private struct ICONINFO { public int fIcon; public uint xHotspot, yHotspot; public nint hbmMask, hbmColor; }

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static unsafe partial int SHDefExtractIconW(string file, int index, uint flags, nint* large, nint* small, uint size);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool GetIconInfo(nint icon, out ICONINFO info);
    [LibraryImport("gdi32.dll")] private static unsafe partial int GetDIBits(nint dc, nint bitmap, uint start, uint lines, void* bits, BITMAPINFOHEADER* info, uint usage);
    // Plus BITMAPINFOHEADER, GetDC, ReleaseDC, DeleteObject, DestroyIcon.
}
```

Gotchas:

- `GetIconInfo` returns two new bitmaps. Delete both, and destroy the
  icon, or GDI objects leak until the 10,000 limit.
- Cache by path on the UI thread, as a `Task<ImageSource?>`, with a cap
  (clear it past 150). Show a glyph when the result is null.
- Packaged apps from `PackageManager.FindPackagesForUser("")` (it works
  unpackaged, about 1 s, so load it in the background) open with
  `shell:AppsFolder\<AUMID>` through `Process.Start` with
  `UseShellExecute = true`.

## Windows Search

File names from the Windows Search index through its OLE DB provider.
Add the `System.Data.OleDb` package (10.0.0 was tested). Only indexed
folders ever show up.

```csharp
using System.Data.OleDb;

public static class FileSearch
{
    private const string Connection = "Provider=Search.CollatorDSO;Extended Properties='Application=Windows'";

    /// Synchronous: run it on a background thread. Null when Windows Search is not running
    /// (the service is stopped or disabled, and Open throws).
    public static List<string>? Run(string sql)
    {
        try
        {
            using var connection = new OleDbConnection(Connection);
            connection.Open();
            using var command = new OleDbCommand(sql, connection);
            using var reader = command.ExecuteReader();
            var paths = new List<string>();
            while (reader.Read())
                if (!reader.IsDBNull(0) && reader.GetString(0) is { Length: > 0 } path) paths.Add(path);
            return paths;
        }
        catch (Exception e) when (e is OleDbException or InvalidOperationException or COMException) { return null; }
    }

    /// One CONTAINS per typed word, as a word prefix, inside the given folders. No shortcuts.
    public static string? NameQuery(string text, IEnumerable<string> folders, int top = 80)
    {
        var words = Words(text);
        if (words.Count == 0) return null;
        var clauses = words.Select(w => $"CONTAINS(System.FileName, '\"{w}*\"')").ToList();
        clauses.Add("System.FileExtension <> '.lnk'");
        return $"SELECT TOP {top} System.ItemPathDisplay FROM SystemIndex WHERE {string.Join(" AND ", clauses)} AND ({Scope(folders)})";
    }

    private static List<string> Words(string text) =>
        text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Clean).Where(w => w.Length > 0).ToList();

    // A ' doubles; " * and % go, so typed text never breaks the query.
    private static string Clean(string word) => word.Replace("\"", "").Replace("*", "").Replace("%", "").Replace("'", "''");

    private static string Scope(IEnumerable<string> folders) =>
        string.Join(" OR ", folders.Select(f => $"SCOPE='file:{Clean(f.TrimEnd('\\'))}'"));
}
```

Gotchas:

- `CONTAINS(System.FileName, '"word*"')` matches word prefixes, not
  substrings. For "newest files with these extensions", one build used
  `System.FileExtension = '.cs' OR ...` with
  `System.FileName LIKE '%word%'` and `ORDER BY System.DateModified DESC`.
- Show instant rows (apps) first, and file rows when the query returns
  (80 ms worked). Tag each query with a generation number and drop
  results from an older one.
- A self-test cannot depend on the index (it may not contain `%TEMP%`, or
  the service may be off). Make the query a delegate and fake it. Test the
  SQL builder with unit tests.
- Still a manual check: files appearing within about 150 ms, and
  the service stopped.

## Per-sample tray icons

Live numbers in the notification area: one icon per metric (CPU, memory,
network), drawn at the taskbar's size each sample. Each icon is its own
`NOTIFYICONDATA` on the feature's own hidden top-level window.

```csharp
internal sealed unsafe class StatsTray : IDisposable
{
    private readonly Dictionary<Metric, (nint Icon, string Key, string Tip)> _icons = [];
    private readonly bool _live;

    /// live false in test runs: icons are drawn and replaced, never added. Windows 11 keeps a
    /// NotifyIconSettings entry per exe path and uID for good, so every dev build folder would leave one.
    public StatsTray(bool live) { _live = live; /* top-level window: message-only ones never get TaskbarCreated */ }

    private static uint Id(Metric metric) => 101 + (uint)metric;   // never the main icon's uID 1

    /// Redraw only icons whose text changed. key: what decides a redraw ("42/16/dark").
    public void Show(Metric metric, byte[] pixels, string key, string tip, int size)
    {
        var exists = _icons.TryGetValue(metric, out var current);
        if (exists && current.Key == key)
        {
            if (current.Tip != tip) { _icons[metric] = current with { Tip = tip }; Modify(metric, NIF_TIP | NIF_SHOWTIP); }
            return;
        }
        _icons[metric] = (ToIcon(pixels, size), key, tip);
        if (exists)
        {
            Modify(metric, NIF_ICON | NIF_TIP | NIF_SHOWTIP);
            DestroyIcon(current.Icon);   // after NIM_MODIFY the old icon is no longer used
        }
        else Add(metric);                // NIM_ADD, then NIM_SETVERSION with uVersion 4, only when _live
    }

    /// An HICON from straight-alpha BGRA. CreateIconIndirect copies both bitmaps: delete them at once,
    /// or the app reaches the 10,000 GDI object limit in a few hours.
    public static nint ToIcon(byte[] pixels, int size)
    {
        var info = new BITMAPINFOHEADER { biSize = (uint)sizeof(BITMAPINFOHEADER), biWidth = size, biHeight = -size, biPlanes = 1, biBitCount = 32 };
        var color = CreateDIBSection(0, ref info, 0, out var bits, 0, 0);
        pixels.CopyTo(new Span<byte>((void*)bits, pixels.Length));
        var maskBytes = new byte[(size + 15) / 16 * 2 * size];
        nint mask;
        fixed (byte* m = maskBytes) mask = CreateBitmap(size, size, 1, 1, (nint)m);
        var icon = new ICONINFO { fIcon = 1, hbmMask = mask, hbmColor = color };   // fIcon is an int, not a bool
        var handle = CreateIconIndirect(ref icon);
        DeleteObject(color);
        DeleteObject(mask);
        return handle;
    }

    // Add, Modify, Remove: Shell_NotifyIconW with hWnd = this window, uID = Id(metric), skipped when !_live.
    // On "TaskbarCreated": Add every icon again. Dispose: NIM_DELETE and DestroyIcon each one.
}
```

Gotchas:

- Use `uID` values from 101 on the feature's window, not GUIDs. Windows
  keys the remembered "show" choice by exe path and `uID`; a second icon
  with `uID` 1 would share the main icon's entry. A GUID is tied to the
  exe path, and the same exe runs from several paths during development
  (installed, and one Debug build per worktree).
- Size: `GetSystemMetricsForDpi(SM_CXSMICON, dpi)` with the DPI of the
  taskbar's window (`FindWindow("Shell_TrayWnd")`, `GetDpiForWindow`):
  16 px at 100 percent, 24 at 150, 32 at 200.
- The taskbar has its own theme: `SystemUsesLightTheme` under
  `HKCU\...\Themes\Personalize`, not `AppsUseLightTheme`. Missing means
  dark. Redraw on `WM_SETTINGCHANGE` with "ImmersiveColorSet" and on
  `WM_DISPLAYCHANGE`.
- Digits: draw white text on black into a 32-bit DIB with
  `ANTIALIASED_QUALITY`, take the gray value as alpha, then tint. Center
  by ink, not by the font cell (digits sit high in their cell). Two bold
  digits do not fit 16 px in Segoe UI; "Bahnschrift SemiBold Condensed"
  at an em of 1.4 times the icon size, shrunk until
  `GetTextExtentPoint32` fits, filled 16 px with ink 16 x 14.
- Straight alpha for an icon. Premultiply only for a XAML
  `WriteableBitmap` preview.
- `[MarshalAs(UnmanagedType.Bool)] bool` in `ICONINFO` breaks
  `LibraryImport` (SYSLIB1051). Use `int`.
- `Shell_NotifyIconGetRect` returns 0 when Windows has the icon (shown or
  in the overflow): a cheap "is it there" check.
- Each new icon lands in the overflow. In a suite whose main icon already
  shows every number in its flyout, leave these off by default.
- Not verified: the real `NIM_ADD` of these icons never ran
  (test runs must not add icons). The painting, 100 redraws with flat GDI
  and USER object counts, and the add, remove, and redraw decisions were
  tested. Turning the icons on in the installed build is a manual check.

## UI Automation from WinUI

Ask another app's UI a question, such as "is the focused control a
password field?" (a keystroke overlay hides typed text there). The
managed `UIAutomationClient` assembly is WPF, so a .NET 10 WinUI app
calls the COM API directly: `CoCreateInstance(CUIAutomation)` and two
vtable calls.

```csharp
internal sealed unsafe class PasswordFocus : IDisposable
{
    private static readonly Guid ClsidCUIAutomation = new("ff48dba4-60ef-4201-aa87-54103eef594e");
    private static readonly Guid IidIUIAutomation = new("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee");
    private const int UIA_IsPasswordPropertyId = 30019;
    private const uint EVENT_OBJECT_FOCUS = 0x8005, WINEVENT_OUTOFCONTEXT = 0;

    private static PasswordFocus? _current;
    private nint _hook, _automation;
    private readonly Lock _gate = new();
    private int _version;
    private volatile bool _maybePassword = true;

    /// Until the answer comes, and when UI Automation cannot tell, it counts as a password.
    public bool MaybePassword => _maybePassword;

    public void Start()
    {
        _current = this;
        _hook = SetWinEventHook(EVENT_OBJECT_FOCUS, EVENT_OBJECT_FOCUS, 0, &OnFocus, 0, 0, WINEVENT_OUTOFCONTEXT);
        Check();
    }

    [UnmanagedCallersOnly]
    private static void OnFocus(nint hook, uint ev, nint hwnd, int obj, int child, uint thread, uint time)
    {
        try { _current?.Check(); }
        catch (Exception e) { AgentLog.Line($"password focus: {e.Message}"); }
    }

    /// Never on the UI thread: UI Automation can wait on the other app for a long time.
    private void Check()
    {
        _maybePassword = true;
        var version = Interlocked.Increment(ref _version);
        Task.Run(() =>
        {
            bool? answer;
            lock (_gate) answer = IsPassword();
            if (version == _version) _maybePassword = answer != false;
        });
    }

    private bool? IsPassword()
    {
        if (_automation == 0 && CoCreateInstance(ClsidCUIAutomation, 0, 1 /* CLSCTX_INPROC_SERVER */, IidIUIAutomation, out _automation) != 0) return null;
        nint element;
        // IUIAutomation::GetFocusedElement is slot 8.
        if (((delegate* unmanaged<nint, nint*, int>)(*(nint**)_automation)[8])(_automation, &element) != 0 || element == 0) return null;
        try
        {
            var variant = stackalloc byte[24];
            // IUIAutomationElement::GetCurrentPropertyValue is slot 10. VT_BOOL is 11; the value sits at offset 8.
            if (((delegate* unmanaged<nint, int, byte*, int>)(*(nint**)element)[10])(element, UIA_IsPasswordPropertyId, variant) != 0) return null;
            return *(ushort*)variant == 11 ? *(short*)(variant + 8) != 0 : null;
        }
        finally { Marshal.Release(element); }
    }

    public void Dispose()
    {
        if (_hook != 0) UnhookWinEvent(_hook);
        if (_automation != 0) Marshal.Release(_automation);
        _hook = _automation = 0;
        if (_current == this) _current = null;
    }

    [LibraryImport("ole32.dll")] private static partial int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid, out nint instance);
    [LibraryImport("user32.dll")] private static partial nint SetWinEventHook(uint min, uint max, nint module, delegate* unmanaged<nint, uint, nint, int, int, uint, uint, void> callback, uint pid, uint thread, uint flags);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool UnhookWinEvent(nint hook);
}
```

Gotchas:

- An out-of-context WinEvent hook calls back on the thread that set it,
  through its message loop. Set it on the UI thread.
- Run the hook only while the answer matters (for example only in "All keys"
  mode).
- Other properties work the same way: look up the property id and read
  the `VARIANT` type at offset 0 and the value at offset 8.
- Still a manual check: the Edge password field.

## Power requests

Keep the PC awake with a reason that `powercfg /requests` names, and
check that it holds without admin rights.

```csharp
internal sealed partial class KeepAwake : IDisposable
{
    public const string Reason = "MyApp Keep awake";
    private nint _request, _reason;

    public bool IsOn => _request != 0;

    /// False when Windows refused. Every start closes the old handle first, so a leaked handle can
    /// never keep the PC awake after the switch says off.
    public bool Start()
    {
        Release();
        var context = new REASON_CONTEXT { Version = 0 /* POWER_REQUEST_CONTEXT_VERSION */, Flags = 0x1 /* SIMPLE_STRING */,
            SimpleReasonString = _reason = Marshal.StringToHGlobalUni(Reason) };
        var request = PowerCreateRequest(ref context);
        if (request is 0 or -1) { Release(); return false; }
        _request = request;
        if (PowerSetRequest(request, 1 /* SystemRequired */) & PowerSetRequest(request, 0 /* DisplayRequired */)) return true;
        Release();
        return false;
    }

    public void Release()
    {
        if (_request != 0)
        {
            PowerClearRequest(_request, 1);
            PowerClearRequest(_request, 0);
            CloseHandle(_request);
            _request = 0;
        }
        if (_reason != 0) { Marshal.FreeHGlobal(_reason); _reason = 0; }
    }

    /// ES_SYSTEM_REQUIRED (1) | ES_DISPLAY_REQUIRED (2) as the power manager sees it, for every app.
    public static uint ExecutionState() =>
        CallNtPowerInformation(16 /* SystemExecutionState */, 0, 0, out var state, 4) == 0 ? state : 0;

    public void Dispose() => Release();

    /// The simple string arm of the union, padded to the union's size: 32 bytes on 64-bit Windows.
    [StructLayout(LayoutKind.Sequential)]
    private struct REASON_CONTEXT { public uint Version, Flags; public nint SimpleReasonString; private readonly nint _detailed1, _detailed2; }
    [LibraryImport("kernel32.dll", SetLastError = true)] private static partial nint PowerCreateRequest(ref REASON_CONTEXT context);
    [LibraryImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool PowerSetRequest(nint request, int type);
    [LibraryImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool PowerClearRequest(nint request, int type);
    [LibraryImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool CloseHandle(nint handle);
    [LibraryImport("powrprof.dll")] private static partial uint CallNtPowerInformation(int level, nint input, uint inputLength, out uint output, uint outputLength);
}
```

Gotchas:

- `powercfg /requests` needs an admin terminal. `CallNtPowerInformation`
  needs none: one self-test logged 0x0 before, 0x3 while on, and 0x0
  after.
- The state is system-wide. During another test run's snapshots it read
  0x3 before, during, and after, because snapshot mode held
  `SetThreadExecutionState(DISPLAY | SYSTEM)`. When the state is already
  set before you start, fall back to your own handle count, and say so in
  the check.
- A timed session ends with a one-shot `DispatcherQueueTimer`; the
  feature's `Stop` releases the request.
- Name the reason after the app the person installed. Inside a suite it
  is the suite's name ("<App> Keep awake"), not the feature's.

## Ports and command lines

A "free a port" tool lists TCP listeners with their program and command
line. No WMI: `System.Management` is a package, and WMI costs 100 ms or
more.

- `GetExtendedTcpTable` with `TCP_TABLE_OWNER_PID_LISTENER` (3), once for
  `AF_INET` and once for `AF_INET6`. It can return
  `ERROR_INSUFFICIENT_BUFFER` (122) on the second call when a listener
  opens between the size call and the data call: retry with the new
  size. `new IPAddress(row.dwLocalAddr)` takes the address as it is; it is
  already in network order.
- The command line: `NtQueryInformationProcess(handle,
  ProcessCommandLineInformation = 60, ...)` with a handle opened for
  `PROCESS_QUERY_LIMITED_INFORMATION`. Call it once with a null buffer
  for the size, then again; the result is a `UNICODE_STRING`. Windows 8.1
  and later, microseconds per process. Elevated processes return
  nothing: show the program name then.
- Leave out services: `ProcessIdToSessionId(pid)` is 0 for them.
  Without that filter the list is mostly svchost, lsass, and spoolsv.
- Refuse to end your own process by pid (`Environment.ProcessId`), not by
  name: a renamed or debug build has another name. Before ending a pid,
  check it is still the same program (name and start time;
  `Process.StartTime` throws for elevated processes, so compare the name
  only then).
- Self-test: start a child `powershell.exe -NoProfile -Command` with a
  loopback `TcpListener` on port 0 that prints its port, find it in the
  table by pid and port, end it through the real code, and check the port
  is gone. Loopback listeners raise no firewall prompt.

Done when each recipe's self-test passes on the app's own test data
(test windows, fake queries, a child process it started), and the manual
checks each section lists are done once on a real install.

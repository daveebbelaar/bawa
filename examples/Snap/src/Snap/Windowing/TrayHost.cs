using System.Runtime.InteropServices;

namespace Snap.Windowing;

/// A hidden top-level window that receives hotkeys (WM_HOTKEY), tray icon clicks, and the
/// "show Settings" message a second copy sends. Not a message-only window: those never get
/// the TaskbarCreated broadcast, and the tray icon would vanish for good after Explorer restarts.
public sealed unsafe class TrayHost : IDisposable
{
    public const string ClassName = "SnapTrayHost";
    public const uint ShowSettingsMessage = Native.WM_APP + 2;
    private const uint TrayMessage = Native.WM_APP + 1;

    private static TrayHost? _instance;
    private readonly uint _taskbarCreated;
    private readonly bool _withIcon;
    private nint _icon;
    private string _tip = "Snap";

    public TrayHost(bool withIcon)
    {
        _instance = this;
        _withIcon = withIcon;
        fixed (char* name = ClassName)
        {
            var cls = new Native.WNDCLASSEXW
            {
                cbSize = (uint)sizeof(Native.WNDCLASSEXW),
                lpfnWndProc = &WndProc,
                hInstance = Native.GetModuleHandleW(null),
                lpszClassName = name,
            };
            var atom = Native.RegisterClassExW(ref cls);
            var classError = Marshal.GetLastPInvokeError();
            Handle = Native.CreateWindowExW(Native.WS_EX_TOOLWINDOW, name, name, Native.WS_POPUP, 0, 0, 0, 0, 0, 0, cls.hInstance, 0);
            AgentLog.Trace($"TRAY class atom {atom} (error {classError}), window {Handle:X} (error {Marshal.GetLastPInvokeError()})");
        }
        _taskbarCreated = Native.RegisterWindowMessageW("TaskbarCreated");
        if (_withIcon) AddIcon();
    }

    public nint Handle { get; }

    public event Action<int>? Hotkey;
    public event Action? ShowSettingsRequested;
    public void RequestSettings() => ShowSettingsRequested?.Invoke();
    /// Returns the menu items, in order. Id 0 is a separator.
    public Func<IReadOnlyList<(int Id, string Text, bool Enabled)>>? MenuItems { get; set; }
    public event Action<int>? MenuChosen;

    [UnmanagedCallersOnly]
    private static nint WndProc(nint hwnd, uint msg, nint w, nint l)
    {
        try
        {
            if (_instance is { } host && host.Handle == hwnd && host.Handle != 0 && host.OnMessage(msg, w, l)) return 0;
        }
        catch (Exception e)
        {
            // An exception may not leave an unmanaged callback: it would end the process with no message.
            AgentLog.Line($"CRASH in tray window: {e}");
        }
        return Native.DefWindowProcW(hwnd, msg, w, l);
    }

    private bool OnMessage(uint msg, nint w, nint l)
    {
        if (msg == Native.WM_HOTKEY) { AgentLog.Trace($"HOTKEY {w}"); Hotkey?.Invoke((int)w); return true; }
        if (msg == ShowSettingsMessage) { ShowSettingsRequested?.Invoke(); return true; }
        if (msg == _taskbarCreated && _withIcon) { AddIcon(); return true; }
        if (msg == TrayMessage)
        {
            var ev = (uint)(l & 0xFFFF);
            if (ev is Native.WM_CONTEXTMENU or Native.WM_RBUTTONUP) ShowMenu();
            else if (ev is Native.NIN_SELECT or Native.NIN_KEYSELECT) ShowSettingsRequested?.Invoke();
            return true;
        }
        return false;
    }

    private Native.NOTIFYICONDATAW IconData(uint flags)
    {
        var data = new Native.NOTIFYICONDATAW
        {
            cbSize = (uint)sizeof(Native.NOTIFYICONDATAW),
            hWnd = Handle,
            uID = 1,
            uFlags = flags,
            uCallbackMessage = TrayMessage,
            hIcon = _icon,
        };
        var tip = _tip.Length > 127 ? _tip[..127] : _tip;
        for (var i = 0; i < tip.Length; i++) data.szTip[i] = tip[i];
        return data;
    }

    private void AddIcon()
    {
        if (_icon == 0)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            _icon = Native.LoadImageW(0, path, Native.IMAGE_ICON,
                Native.GetSystemMetrics(Native.SM_CXSMICON), Native.GetSystemMetrics(Native.SM_CYSMICON), Native.LR_LOADFROMFILE);
        }
        var data = IconData(Native.NIF_MESSAGE | Native.NIF_ICON | Native.NIF_TIP | Native.NIF_SHOWTIP);
        Native.Shell_NotifyIconW(Native.NIM_ADD, ref data);
        data.uVersion = 4; // NOTIFYICON_VERSION_4: the event comes in the low word of lParam
        Native.Shell_NotifyIconW(Native.NIM_SETVERSION, ref data);
    }

    /// Changes the tooltip, for example to say why a window did not move.
    public void SetTip(string tip)
    {
        _tip = tip;
        if (!_withIcon) return;
        var data = IconData(Native.NIF_TIP | Native.NIF_SHOWTIP);
        Native.Shell_NotifyIconW(Native.NIM_MODIFY, ref data);
    }

    private void ShowMenu()
    {
        if (MenuItems is null) return;
        var menu = Native.CreatePopupMenu();
        foreach (var (id, text, enabled) in MenuItems())
        {
            if (id == 0) Native.AppendMenuW(menu, Native.MF_SEPARATOR, 0, null);
            else Native.AppendMenuW(menu, Native.MF_STRING | (enabled ? 0 : Native.MF_GRAYED), (nuint)id, text);
        }
        Native.GetCursorPos(out var point);
        // Without this the menu never closes when you click elsewhere.
        Native.SetForegroundWindow(Handle);
        var chosen = Native.TrackPopupMenuEx(menu, Native.TPM_RETURNCMD | Native.TPM_RIGHTBUTTON | Native.TPM_BOTTOMALIGN, point.X, point.Y, Handle, 0);
        Native.PostMessageW(Handle, Native.WM_NULL, 0, 0);
        Native.DestroyMenu(menu);
        if (chosen != 0) MenuChosen?.Invoke(chosen);
    }

    public void Dispose()
    {
        if (_withIcon)
        {
            var data = IconData(0);
            Native.Shell_NotifyIconW(Native.NIM_DELETE, ref data);
        }
        if (_icon != 0) Native.DestroyIcon(_icon);
        Native.DestroyWindow(Handle);
        if (_instance == this) _instance = null;
    }
}

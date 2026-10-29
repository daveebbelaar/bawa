# Window switcher

Hold Alt, press the key above Tab, and pick any window on the current desktop
from a grid of live previews. Let go to jump to it. Replaces: Windows' own
Alt+Tab, and Task View for picking one window. Ported from BAMA's window
switcher spec.

## Brief

```markdown
# Switch

A tray app that switches between windows with live previews: hold Alt, tap `, let go.

## Replaces
Windows' Alt+Tab and Task View, for picking one window.

## Main jobs
1. Hold Alt and press ` (the key above Tab) to step through every window on this desktop; let go to focus it.
2. See a live preview, the app icon, and the title of each window to find the right one.
3. Pick Alt+` or Alt+Tab as the trigger, and turn previews on or off.

## Data
- Settings: enabled (default on), trigger ("backtick" or "tab", default "backtick"),
  previews (default on). settings.json in %LOCALAPPDATA%\Switch.
- App info cache: process path to app name and icon. Memory only.

## Shape
A borderless, topmost panel that never takes focus, shown while Alt is held, plus a
tray icon and a Settings window. Windows 11 first, Windows 10 19041 or later still runs.
No taskbar button for the panel. Local data. Works offline. No accounts.

## System hooks
Global hotkeys (RegisterHotKey Alt+` and Alt+Shift+`), a low-level keyboard hook for
the session keys and the Alt release, DWM thumbnails for previews, tray icon.

## Reference
Windows' own Alt+Tab

## Look
One acrylic panel with Windows 11 rounded corners (radius 8) and the system shadow,
tiles of 236 by 184 epx, selected tile with a 22% accent fill and a 2 epx accent border.
No open animation: it must feel instant.

## Not in version one
- Windows on other virtual desktops and minimized windows
- Close, minimize, or quit from the switcher
- Search, app filters, and an exclude list
```

## How it works

| Part | API | Permission |
| --- | --- | --- |
| Trigger Alt+` and Alt+Shift+` | `RegisterHotKey` with `MOD_ALT \| MOD_NOREPEAT` (plus `MOD_SHIFT`) and the key for scan code 0x29, from `MapVirtualKey(0x29, MAPVK_VSC_TO_VK)` | None |
| Alt+Tab mode | `SetWindowsHookEx(WH_KEYBOARD_LL)`: Tab with `LLKHF_ALTDOWN` returns 1, so Windows' switcher never sees it | None |
| Keys while open (Esc, arrows, Enter, the trigger key) | The same hook, which swallows them only while a session is open | None |
| Notice Alt going up | Key-up of `VK_LMENU` or `VK_RMENU` in the hook, plus a 25 ms poll of `GetAsyncKeyState(VK_MENU)` | None |
| List windows | `EnumWindows`, `IsWindowVisible`, `IsIconic`, `GetWindowLongPtr(GWL_EXSTYLE)`, `GetWindow(GW_OWNER)`, `DwmGetWindowAttribute` with `DWMWA_CLOAKED` and `DWMWA_EXTENDED_FRAME_BOUNDS` | None |
| Title, app name, icon | `GetWindowText`, `GetWindowThreadProcessId`, `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)`, `QueryFullProcessImageName`, `FileVersionInfo.FileDescription`, `StorageFile.GetThumbnailAsync(ThumbnailMode.SingleItem, 64)` on the exe | None |
| Previews | `DwmRegisterThumbnail`, `DwmUpdateThumbnailProperties`, `DwmQueryThumbnailSourceSize`, `DwmUnregisterThumbnail` | None |
| Focus one window | `SetForegroundWindow`, then `Win32.ForceForeground` (the `AttachThreadInput` fallback in `UI/Win32.cs`) | None (UIPI applies, see Gotchas) |
| Panel | WinUI `Window` with `OverlappedPresenter.SetBorderAndTitleBar(true, false)`, `IsAlwaysOnTop`, `WS_EX_NOACTIVATE \| WS_EX_TOOLWINDOW`, shown with `AppWindow.Show(false)` | None |
| Screen and DPI under the pointer | `GetCursorPos`, `DisplayArea.GetFromPoint`, `MonitorFromPoint`, `GetDpiForMonitor(MDT_EFFECTIVE_DPI)` | None |
| Tray icon | `Shell_NotifyIcon`, `TrackPopupMenuEx` | None |

Recipes: [global hotkey](../skills/code/os-integrations/recipes.md#global-hotkey-registerhotkey),
[low-level keyboard hook](../skills/code/os-integrations/recipes.md#low-level-keyboard-hook),
[non-activating panel](../skills/code/os-integrations/recipes.md#non-activating-panel),
[tray icon](../skills/code/os-integrations/recipes.md#tray-icon),
[single instance](../skills/code/os-integrations/recipes.md#single-instance),
[elevation and UIPI](../skills/code/os-integrations/recipes.md#elevation-and-uipi).

Main types:

- `WindowInfo` (Core): hwnd, pid, process path, title, style flags, cloaked, visible frame in epx. A plain record, so the filter is testable.
- `WindowFilter` (Core): the rules below, pure. Unit tested.
- `SwitcherSession` (Core): items count, selection, columns; `Step`, `StepRow`, wrap. Unit tested.
- `WindowList`: walks `EnumWindows`, builds `WindowInfo`, filters, caches app name and icon per process path.
- `WindowSwitcher` (`ObservableObject`): Items, Selection, IsActive; `Open`, `Step`, `Commit`, `Cancel`.
- `SwitcherWindow`: the panel and the tile grid.
- `Thumbnails`: registers, places, and removes the DWM thumbnails for the tiles in view.
- `KeyboardHook`: one `WH_KEYBOARD_LL` hook on its own thread with a message loop.
- `HotkeyCenter`, `KeyCombo`: `RegisterHotKey` on the tray icon's window, failures recorded.
- `TrayIcon`: the notification area icon and its menu.
- Snapshot mode (`APP_SNAPSHOT`) also renders the panel with seeded windows as the page `switcher`.

Behaviors that matter:

- **Which windows.** Visible, not minimized, `DWMWA_CLOAKED` is 0, no owner (or `WS_EX_APPWINDOW` set), no `WS_EX_TOOLWINDOW`, and a visible frame of at least 60 by 60 epx (frame from `DWMWA_EXTENDED_FRAME_BOUNDS`, divided by the window's DPI scale). Keep the `EnumWindows` order: it is front to back. An empty title falls back to the app name. Switch's own Settings window counts; its panel is a tool window, so it never does.
- **App name.** `FileDescription` of the exe, else the file name without `.exe`. A window owned by `ApplicationFrameHost.exe` is a packaged app: use the process of its child window from another process.
- **First press.** Open and select index 1 (the window behind the current one). Alt+Shift+` opens on the last item. Later presses add or subtract one and wrap around.
- **Quick tap.** If Alt is already up when the list is built, select index 1 and switch at once. No panel.
- **Show delay.** Show the panel 90 ms after opening, and only if the session is still active. A fast Alt+` never flashes the panel.
- **Menu mask.** When a session opens, send one press of the unassigned key `0xE8` with `SendInput`, marked in `dwExtraInfo`. Then the Alt release does not open the menu bar of the window behind.
- **Let go.** The hook sees the Alt key-up and commits. A 25 ms `DispatcherQueueTimer` on the UI thread reads `GetAsyncKeyState(VK_MENU)` too, in case the hook missed it.
- **Keys while open.** The hook swallows these, and their key-ups, only while a session is open. Esc cancels. Left and Right step by one. Up and Down step by one row (the column count). Enter commits. The trigger key steps. A click on a tile selects and commits.
- **Grid size.** Use the work area of the monitor under the pointer and that monitor's DPI. Columns = how many 236 epx tiles with 8 epx spacing fit in 90% of the width, minus padding 16 on each side, at least 1, at most the window count. Height stops at 90% of the work area; the grid scrolls.
- **Previews.** Only when on. Register a DWM thumbnail for each tile that is fully in view, at most 24. Place it in the tile's preview area in physical pixels (epx times `XamlRoot.RasterizationScale`), keep the source's aspect ratio, and set `rcSource` to the visible frame so the invisible borders are cut off. On scroll, place them again; a tile partly out of view hides its thumbnail and shows the large icon. Unregister every thumbnail when the session closes.
- **Focus.** Hide the panel first, then focus the window (snippet below). One of Switch's own windows: `Activate()`.
- **Alt+Tab mode.** The hook returns 1 for Tab while `LLKHF_ALTDOWN` is set and opens or steps the session. Unregister the Alt+` hotkeys in this mode. Switching back to Alt+` stops the swallowing at once.
- **Prewarm.** 2 s after launch, build the panel, show it off screen without activation, and hide it. Fill the icon cache. The first Alt+` is then as quick as the rest.

The focus call, without the error handling:

```csharp
Panel.AppWindow.Hide();
if (!Win32.SetForegroundWindow(hwnd) || Win32.GetForegroundWindow() != hwnd)
    Win32.ForceForeground(hwnd);   // AttachThreadInput for a moment, UI/Win32.cs
```

The hook callback. Decide fast, post the work, and return:

```csharp
[UnmanagedCallersOnly]
static unsafe nint OnKey(int code, nint wParam, nint lParam)
{
    if (code >= 0)
    {
        var key = *(KBDLLHOOKSTRUCT*)lParam;
        var up = wParam is WM_KEYUP or WM_SYSKEYUP;
        var alt = (key.flags & LLKHF_ALTDOWN) != 0;
        // Router reads volatile session state and posts to the UI DispatcherQueue.
        if ((key.flags & LLKHF_INJECTED) == 0 && Router.Take(key.vkCode, up, alt))
            return 1;   // swallowed: neither the app in front nor Windows' Alt+Tab sees it
    }
    return CallNextHookEx(0, code, wParam, lParam);
}
```

## Stories

```markdown
## M1 Core loop

### S-001 Open the switcher with Alt+`
As the user, I hold Alt and press `, and a panel shows every window on this desktop.
Screens: switcher.png
Acceptance:
- [snap] Tiles show the app icon and the window title, with the second tile selected.
- [unit] The window filter drops tool windows, owned windows, cloaked windows, minimized windows, and windows under 60 epx.
- [manual] The panel shows about 90 ms after the press, centered on the monitor under the pointer, and the app in front keeps focus.
Status: todo

### S-002 Step through windows
As the user, I press ` again to move forward and Shift+` to move back while holding Alt.
Depends: S-001
Acceptance:
- [unit] Stepping past the last item wraps to the first, and back from the first wraps to the last.
- [snap] The accent border sits on the selected tile.
Status: todo

### S-003 Let go to switch
As the user, I let go of Alt and the selected window comes to the front with keyboard focus.
Depends: S-001
Acceptance:
- [manual] Picking the second of two File Explorer windows focuses that window.
- [manual] Typing right after the switch goes into the new window.
- [manual] A quick tap of Alt+` lands in the window I used before and never shows the panel.
- [manual] After a switch away from Notepad, Notepad's menu bar shows no access key underlines.
Status: todo

### S-004 Cancel with Esc
As the user, I press Esc while holding Alt and the panel closes without switching.
Depends: S-001
Acceptance:
- [self] After cancel, the hook swallows no keys and no thumbnail is registered.
Status: todo

### S-005 See when the trigger cannot work
As the user, I open Settings and see whether my trigger works.
Screens: settings-general.png
Acceptance:
- [snap] The trigger row shows "Ready" or an orange "Another app uses Alt+`. Pick another trigger."
- [snap] The footer says "Switch cannot see keys while an app running as administrator is in front."
- [manual] Starting Switch a second time opens the first copy's Settings and registers nothing.
Status: todo

### S-006 Tray icon
As the user, I right-click the Switch icon to see the trigger, open Settings, or quit.
Acceptance:
- [unit] The menu model has "Switch windows: Alt+`", a warning row when the trigger is taken, Settings, and Quit.
- [manual] No taskbar button while only the tray icon runs; closing Settings keeps the app running.
Status: todo

## M2 Previews and settings

### S-007 Show live window previews
As the user, I see a live picture of each window in its tile.
Depends: S-001
Screens: switcher-previews.png
Acceptance:
- [manual] Tiles show the windows at once, and a playing video moves inside its tile.
- [self] A session with 30 windows registers at most 24 thumbnails and none after it closes.
Status: todo

### S-008 Work with previews off
As the user, I turn previews off and still find my windows.
Depends: S-007
Acceptance:
- [snap] Tiles show a large app icon in place of the picture, and the window title.
- [self] With previews off, opening the switcher registers no thumbnail.
Status: todo

### S-009 Move with arrows and click
As the user, I use the arrow keys while holding Alt, or click a tile.
Depends: S-002
Acceptance:
- [unit] Down moves the selection by the column count and wraps.
- [manual] One click on a tile switches, and the window I leave never shows as inactive before the switch.
Status: todo

### S-010 Use Alt+Tab as the trigger
As the user, I pick "Alt+Tab (replaces the Windows switcher)" in Settings.
Screens: settings-general.png
Acceptance:
- [manual] Alt+Tab opens Switch and Windows' own switcher does not appear.
- [manual] After quitting Switch, or ending it in Task Manager, Alt+Tab shows Windows' switcher again.
Status: todo

### S-011 Turn the switcher or previews off
As the user, I turn off the switcher or the previews in Settings.
Acceptance:
- [self] With the switcher off, no trigger hotkey is registered and the hook passes every key.
- [self] With previews off, no thumbnail is registered.
Status: todo

## Dropped
- Activity log of switches
- Per-feature animation speed setting
- URL protocol and debug bridge preview command
```

## Gotchas

- `SetForegroundWindow` is refused when Windows thinks your process has no right to take the foreground. It returns false and only flashes the taskbar button. Check `GetForegroundWindow` after the call and fall back to `Win32.ForceForeground`, which joins the input of the front thread for a moment.
- Windows silently removes a low-level hook whose callback is too slow (the `LowLevelHooksTimeout`, at most 1 s since Windows 10 1709). Nothing tells the app. Install the hook on its own thread with a `GetMessage` loop, never on the UI thread, and only decide and post in the callback.
- The callback runs before the key state updates, so `GetAsyncKeyState` inside it reads the old state. Use the flags in `KBDLLHOOKSTRUCT` there, and read `GetAsyncKeyState` only from the 25 ms timer.
- A hook delegate that the garbage collector frees crashes the app on the next key. Use an `[UnmanagedCallersOnly]` static method and a function pointer, or keep the delegate in a static field.
- While an app running as administrator is in front, a normal app's hook gets no keys (UIPI). Alt+Tab mode and the session keys stop there. Say so in Settings; running Switch elevated is not the default.
- A lone Alt press and release opens the menu bar of the app in front. The switcher eats the key between them, so mask the press with a `SendInput` of key `0xE8`, the same trick AutoHotkey uses.
- `VK_OEM_3` is ` only on US layouts. Register the key for scan code 0x29 (the key above Tab) through `MapVirtualKey`, and show its real label from `GetKeyNameText`.
- Take Alt+Tab in the hook, by returning 1 for Tab while `LLKHF_ALTDOWN` is set. Nothing persists: when Switch quits or crashes, the hook is gone and Windows' Alt+Tab works again. No restore step at quit is needed.
- Windows on other virtual desktops and suspended packaged apps report `IsWindowVisible` true. Check `DWMWA_CLOAKED`, or the panel fills with windows you cannot see.
- `GetWindowRect` includes the invisible resize borders of Windows 10 and 11. Use `DWMWA_EXTENDED_FRAME_BOUNDS` for the 60 epx check and for the preview's source rect.
- Packaged apps (Settings, Calculator) live inside `ApplicationFrameHost.exe`. Without the child window lookup, every one of them is named "Application Frame Host".
- A DWM thumbnail is not a XAML element. DWM draws it on the panel's window at a rect in physical pixels, over the XAML content. Keep the preview area of a tile empty in XAML, put the selection border around it, and place every thumbnail again after a scroll or a DPI change.
- On a PC with two monitors at different scales, the hidden panel still has the DPI of the last monitor. Size it with `GetDpiForMonitor` for the monitor under the pointer before showing it.
- A click on a `WS_EX_NOACTIVATE` window must still not activate it. Return `MA_NOACTIVATE` for `WM_MOUSEACTIVATE` in the panel's window subclass, so the window you leave keeps focus until the switch.
- App icons come from the exe through an async thumbnail call. Cache them per process path and clear the cache past 150 entries.
- The first open after launch builds the XAML tree of the panel and is slow. Prewarm it 2 s after launch.
- A second copy of Switch fails `RegisterHotKey` with `ERROR_HOTKEY_ALREADY_REGISTERED` and shows a false "Another app uses Alt+`" warning. Make the app single instance and send the second launch to the first copy's Settings.
- Explorer restarts drop the tray icon. Re-add it when the `TaskbarCreated` message arrives. A message-only window never gets that broadcast, so use a hidden normal window.
- Changed from BAMA: tiles are 184 epx high, spacing 8, and padding 16, to sit on BAWA's 4 epx grid. Radius 8 is the Windows 11 corner for a panel. The 30 pt shadow margin is gone because DWM draws the shadow outside the window. The 440 by 280 px capture cap is gone because DWM thumbnails are live and drawn at the tile's size.

Done when holding Alt and pressing ` shows every window on the current desktop with icon, title, and a live preview, and letting go focuses the chosen window, including the second of two File Explorer windows.

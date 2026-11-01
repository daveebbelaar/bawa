# Window snapper

Snap the front window to the left or right half, a wide side, or full screen with a keyboard shortcut, and move it to the next display. Replaces: Rectangle-style snapping on top of Windows' own Snap layouts, and the snapping part of PowerToys FancyZones. Ported from BAMA's window-snapper spec.

## Brief

```markdown
# Snap

A tray app that moves and resizes the front window with keyboard shortcuts, for anyone who wants Rectangle's exact halves and wide sides on Windows.

## Replaces
Win+arrow snapping (which cycles through states and opens Snap assist), and FancyZones for people who only want halves and wide sides.

## Main jobs
1. Snap the front window to the left half, the right half, or full screen with a shortcut.
2. Snap it to a wide left or right side (two thirds by default), or send it to the next display.
3. Change any shortcut, the wide width, and the gap around windows in Settings.

## Data
- Shortcut: command, virtual key, modifiers. Stored in settings.json in %LOCALAPPDATA%\Snap.
- Layout: wide fraction (default 2/3), gap in epx (default 0). settings.json.
- Restore frame: window handle and its frame before the first snap. Memory only.

## Shape
Tray icon plus a Settings window, Windows 11 (Windows 10 19041 or later runs).
No taskbar button while only the tray icon runs. Local data. Works offline. No accounts.

## System hooks
Global shortcuts (RegisterHotKey), tray icon, launch at login (HKCU Run key).

## Reference
Rectangle, Magnet

## Look
Native Windows 11, Windows accent. Settings looks like Windows Settings: one card per shortcut with a layout glyph.

## Not in version one
- Drag a window to a screen edge to snap it (Windows does this already)
- Thirds, quarters, center
- A URL protocol and command line actions
```

## How it works

| Part | API | Permission |
| --- | --- | --- |
| Global shortcuts | `RegisterHotKey` on a hidden window, `WM_HOTKEY` | None |
| Know which window to move | `GetForegroundWindow`, `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` | None |
| Read the visible frame | `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)` | None |
| Read and set the window rect | `GetWindowRect`, `SetWindowPos(SWP_NOZORDER \| SWP_NOACTIVATE)` | None, but not for elevated windows (UIPI) |
| Un-maximize first | `IsZoomed`, `ShowWindow(SW_RESTORE)` | None |
| Usable screen area | `EnumDisplayMonitors`, `GetMonitorInfo` `rcWork` (no taskbar) | None |
| Gap at the monitor's scale | `GetDpiForMonitor(MDT_EFFECTIVE_DPI)` | None |
| Tray icon and menu | `Shell_NotifyIcon`, `TrackPopupMenuEx` | None |
| Launch at login | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` | None |
| Failure sound | `MessageBeep(MB_OK)` | None |

Recipes: [global hotkey](../../../skills/code/os-integrations/recipes.md#global-hotkey-registerhotkey), [move a window](../../../skills/code/os-integrations/recipes.md#move-a-window), [tray icon](../../../skills/code/os-integrations/recipes.md#tray-icon), [launch at login](../../../skills/code/os-integrations/recipes.md#launch-at-login), [single instance](../../../skills/code/os-integrations/recipes.md#single-instance), [elevation](../../../skills/code/os-integrations/recipes.md#elevation-and-uipi).

Main types:

- `SnapCommand` (Core): enum of `Left`, `Right`, `Maximize`, `LeftWide`, `RightWide`, `NextDisplay`, `PreviousDisplay`, `Restore`.
- `Box` (Core): an integer rectangle in physical pixels, with `Intersect`, `Area`, `Right`, `Bottom`.
- `Layout` (Core): pure functions that turn a command, a window frame, and a work area into a target frame. Unit tested.
- `KeyCombo` and `ShortcutMap` (Core): a virtual key plus modifiers, display text ("Ctrl+Alt+Left"), and the rule that one combo belongs to one command.
- `WindowManager` (app): picks the target window, reads its visible frame, applies the target, remembers Restore frames.
- `HotkeyCenter` (app): registers the saved shortcuts on a hidden window, keeps the ones that failed, can suspend and resume.
- `TrayIcon` (app): the icon, its menu, and the hidden window that receives both tray and hotkey messages.
- `ShortcutButton` (app): a button that records the next combo.

Default shortcuts: Left half Ctrl+Alt+Left, Right half Ctrl+Alt+Right, Maximize Ctrl+Alt+Enter, Left wide Ctrl+Alt+Down, Right wide Ctrl+Alt+Up, Next display Ctrl+Alt+Shift+Right, Previous display Ctrl+Alt+Shift+Left, Restore Ctrl+Alt+Backspace.

Behaviors that matter:

- **Which screen.** The monitor with the largest intersection of its full rect and the window's visible frame. Use its `rcWork` as the area, so the taskbar stays clear.
- **Halves.** Left: `x = area.X`, `width = area.Width / 2`, full height. Right: `x = area.X + area.Width / 2`, the rest of the width. Maximize: the whole area, as a normal window, not the maximized state.
- **Wide.** One setting, not a cycle: two thirds (default), three quarters, or five sixths. Clamp to 0.5 to 0.9. Left wide starts at `area.X`; Right wide ends at `area.Right`. Settings titles show the fraction: "Left wide (3/4)".
- **Gap.** 0 to 40 epx in steps of 2, default 0, scaled by the target monitor's DPI. A full gap on a side that touches the area edge, half a gap on a side that meets a neighbour. Two halves then have one full gap between them.
- **Next and previous display.** Sort monitors by `(X, Y)` and wrap around. Keep the relative position and size: scale x, y, width, and height by the ratio of the two work areas, then intersect with the new area. If that is empty, use the whole new area. With one display, do nothing.
- **Restore.** Before the first snap of a window, store its visible frame under its handle. Later snaps keep the first one. Restore puts it back and forgets it. No stored frame: beep.
- **Target window.** The foreground window, unless it is Snap's own window, the taskbar (`Shell_TrayWnd`), or the desktop (`Progman`, `WorkerW`). Then use the last other top-level window, tracked with `EVENT_SYSTEM_FOREGROUND`. A tray menu click makes Snap the foreground app, so the tray menu always uses the tracked window.
- **Failure.** No window, a minimized window, or a window `SetWindowPos` refuses (an elevated app): `MessageBeep(MB_OK)` and nothing else.
- **Check and retry.** After setting the frame, read the visible frame back. If it is more than 2 px off, set it again after 80 ms. If the window is still larger than the target (an app with a minimum size), keep it inside the screen against the edge it was sent to: right-side commands align its right edge, all commands align its top.
- **Shortcut rules.** One combo belongs to one command: setting it removes it from any other command. A combo needs Ctrl, Alt, or Win, except F1 to F12. Shift alone is not enough.

Windows draws an invisible resize border around most windows (7 px at 100% on the left, right, and bottom). `GetWindowRect` includes it; what people see is `DWMWA_EXTENDED_FRAME_BOUNDS`. Work in visible frames, and convert at the last step:

```csharp
// The rect SetWindowPos needs so the visible frame lands on target.
static Box OuterFor(Box target, Box outer, Box visible) => new(
    target.X - (visible.X - outer.X),
    target.Y - (visible.Y - outer.Y),
    target.Width + (outer.Width - visible.Width),
    target.Height + (outer.Height - visible.Height));
```

## Stories

```markdown
## M1 Core loop

### S-001 Snap the front window to the left half
As the user, I press Ctrl+Alt+Left and the front window fills the left half of its screen.
Acceptance:
- [unit] `Layout` returns x = area.X, width = area.Width / 2, full height for a 1920x1032 area.
- [unit] Converting a visible frame to the outer rect and back gives the original.
- [self] A test window snapped left has its visible frame within 2 px of the target.
- [manual] In Edge, Ctrl+Alt+Left fills the left half above the taskbar with no gap from the invisible border.
Status: todo

### S-002 Snap the front window to the right half
As the user, I press Ctrl+Alt+Right and the front window fills the right half.
Depends: S-001
Acceptance:
- [unit] `Layout` returns x = area.X + area.Width / 2, and the two halves cover the area exactly on an odd width.
- [self] A test window snapped right lands on the target.
Status: todo

### S-003 Maximize the front window
As the user, I press Ctrl+Alt+Enter and the front window fills the work area without entering the maximized state.
Acceptance:
- [unit] `Layout` returns the whole work area.
- [self] A maximized test window is restored first, then fills the area, and `IsZoomed` is false.
Status: todo

### S-004 Snap to a wide side
As the user, I press Ctrl+Alt+Down or Ctrl+Alt+Up and the window takes two thirds of the screen on the left or right.
Depends: S-001
Acceptance:
- [unit] Right wide at 2/3 ends exactly at area.Right.
- [unit] A wide fraction of 0.95 is clamped to 0.9.
Status: todo

### S-005 See which shortcuts work
As the user, I open Snap for the first time and Settings shows every shortcut, and warns about any that another app owns.
Screens: shortcuts-light.png
Acceptance:
- [snap] Each command is a card with its layout glyph, its name, and its shortcut.
- [snap] A shortcut that failed to register shows a warning glyph and "Another app uses this shortcut."
- [self] Every default shortcut is registered or reported as failed; none is silently missing.
Status: todo

### S-006 Run in the tray
As the user, I close Settings and Snap keeps running in the tray, with no taskbar button.
Screens: tray menu (manual)
Acceptance:
- [self] Closing the Settings window hides it; the process keeps running.
- [manual] The tray menu lists every command with its shortcut, then Settings and Exit.
- [manual] Clicking Left half in the tray menu snaps the window I used before, not Snap's.
- [manual] Starting Snap again while it runs opens Settings instead of a second copy.
Status: todo

### S-007 Press a shortcut on a window Snap cannot move
As the user, I press a shortcut on an app running as administrator and Snap tells me why nothing moved.
Acceptance:
- [manual] The PC beeps and the window does not move.
- [manual] The tray tooltip says "Snap can't move windows of apps running as administrator."
Status: todo

## M2 Settings and displays

### S-008 Record a new shortcut
As the user, I select a shortcut in Settings and press a new combo.
Screens: shortcuts-light.png
Acceptance:
- [snap] While recording, the button says "Press a shortcut" in the accent style.
- [self] While recording, all global shortcuts are unregistered and come back after.
- [unit] Assigning a combo used by another command removes it from that command.
- [unit] A combo with only Shift, or no modifier, is refused unless the key is F1 to F12.
- [manual] Esc cancels, Backspace clears, a key without a modifier beeps.
Status: todo

### S-009 Pick the wide width and the gap
As the user, I choose Two thirds, Three quarters, or Five sixths, and a gap from 0 to 40 epx.
Screens: layout-light.png
Acceptance:
- [unit] With a 10 px gap, two halves leave 10 px at each screen edge and 10 px between them.
- [snap] The Left wide card title changes to "Left wide (3/4)".
Status: todo

### S-010 Move the window to the next display
As the user, I press Ctrl+Alt+Shift+Right and the window moves to the next display at the same relative spot.
Acceptance:
- [unit] A left-half window on a 1920 wide display becomes a left-half window on a 2560 wide display.
- [unit] With one display, the command returns no frame.
- [manual] Moving from a 150% display to a 100% display ends at the right size.
Status: todo

### S-011 Restore a snapped window
As the user, I press Ctrl+Alt+Backspace and the window goes back to where it was before I first snapped it.
Acceptance:
- [unit] Two snaps in a row keep the frame from before the first snap.
- [self] Restore puts a snapped test window back within 2 px.
- [manual] Restore with nothing stored beeps.
Status: todo

### S-012 Launch at login
As the user, I turn on "Open at sign-in" in Settings.
Acceptance:
- [self] The switch writes and removes the Run key value, and reads it back.
Status: todo

## Dropped
- Layout glyphs in the tray menu (a Win32 menu has no glyph font; the Settings cards show them)
- Center, thirds, quarters
- URL protocol and command line actions
```

## Gotchas

- `GetWindowRect` includes an invisible resize border, about 7 px at 100% on the left, right, and bottom. Snap with it and every window has a visible gap at the left edge and hangs 7 px off the bottom. Work in `DWMWA_EXTENDED_FRAME_BOUNDS` and add the border back for `SetWindowPos`.
- Declare the process PerMonitorV2 DPI aware (the template's `app.manifest` does). Without it, Windows scales coordinates for you and halves land in the wrong place on a 150% display.
- A maximized window ignores `SetWindowPos` sizes and keeps its maximized state. `ShowWindow(SW_RESTORE)` first, then move.
- Windows refuses `SetWindowPos` on a window of an elevated app when Snap is not elevated (UIPI). The call returns false with `ERROR_ACCESS_DENIED`. Beep and say why; never ask the user to run Snap as administrator.
- Ctrl+Alt+arrow keys rotate the screen on some PCs with old Intel graphics drivers, and the driver takes the combo first. `RegisterHotKey` then fails. Show the failure on the card so the user can pick another combo.
- `RegisterHotKey` fails when another app owns the combo, and nothing else tells you. Keep the result of every call and show failures in Settings.
- Win+arrow keys belong to Windows Snap. `RegisterHotKey` with `MOD_WIN` and an arrow fails or fights with the shell. Use Ctrl+Alt.
- The recorder never sees a combo that is still registered: Windows delivers it as `WM_HOTKEY` instead of a key press. Unregister every hotkey while recording, register again when done.
- A WinUI app exits when its last window closes. Set `DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown` and hide the Settings window on close (`AppWindow.Closing`, `args.Cancel = true`, `AppWindow.Hide()`), or the tray app dies when Settings closes.
- The tray menu needs `SetForegroundWindow` on the hidden window before `TrackPopupMenuEx`, or it never closes when you click elsewhere. That makes Snap the foreground app, so menu commands must use the window tracked before, not `GetForegroundWindow`.
- After Explorer restarts, the tray icon is gone. Listen for the `TaskbarCreated` message (`RegisterWindowMessage`) on a hidden top-level window, not a message-only window, which never gets broadcasts, and add the icon again.
- A test run must not join the running copy. Single-instance redirection skips itself when `APP_STORE` is set, and the self-test unregisters its hotkeys before exit, so a running Snap keeps its shortcuts.
- Some apps (terminals, Steam) snap their size to a character grid or a minimum. After the retry, check the size and pin the window to the edge it was sent to.

Done when every default shortcut snaps the foreground window of any normal app to the right frame on each connected display, a failed shortcut shows in Settings, Settings changes every shortcut, the wide width, and the gap, and Snap keeps running in the tray after Settings closes.

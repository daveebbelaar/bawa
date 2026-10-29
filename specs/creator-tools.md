# Creator tools

Tray tools for screen-recorded tutorials: show the keys you press, highlight the cursor and clicks, get the PC ready to film with one switch, drop YouTube chapter markers, and read a script from a teleprompter. Replaces: Carnac, PowerToys Mouse Highlighter. Ported from BAMA's creator-tools spec.

## Brief

```markdown
# Studio

Tray tools for people who record screen tutorials and YouTube videos on Windows.

## Replaces
Carnac, PowerToys Mouse Highlighter, writing chapter times on paper, a script in a
second window

## Main jobs
1. Show my shortcuts and clicks on screen, and turn on Recording mode with one switch.
2. Drop chapter markers while I record and copy them for YouTube.
3. Read my script from a teleprompter near the camera.

## Data
- ChapterSession: start (time), markers. Saved to %LOCALAPPDATA%\Studio\chapters.json.
- ChapterMarker: id, offset (seconds since start), label.
- RecordingSnapshot: what Recording mode changed (desktop icons before, taskbar
  auto-hide before, overlays, "on" command ran). Saved to
  %LOCALAPPDATA%\Studio\recording.json.
- Settings in %LOCALAPPDATA%\Studio\settings.json: keystroke mode, halo size,
  teleprompter text, speed, font size, width, opacity, mirror, Focus commands, hotkeys.

## Shape
Tray icon in the notification area, click-through overlay windows, a chapter prompt,
and a WinUI Settings window. Windows 11 first, Windows 10 19041 or later runs.
Unpackaged .exe. Local data. Works offline. No accounts.

## System hooks
Global shortcuts (RegisterHotKey), a low-level keyboard hook and a low-level mouse
hook while the overlays are on, the tray icon, capture exclusion for Studio's own
notes, single instance. No elevation.

## Reference
Carnac (key caps), PowerToys Mouse Highlighter (cursor halo)

## Look
Native WinUI with a violet accent (#7B5CFF). Overlays are dark translucent rounded
capsules; the teleprompter is a dark plate so white text reads on any wallpaper.

## Not in version one
- Drawing on the screen
- Zoom and spotlight effects
- Its own screen recorder
```

## How it works

| Part | API | Permission |
| --- | --- | --- |
| Keystroke overlay | `SetWindowsHookEx(WH_KEYBOARD_LL)` on a dedicated thread with its own message loop | None. Sees no input aimed at elevated windows (UIPI) |
| Hide password keys | Skip `LLKHF_INJECTED` events; in "All keys" mode, UI Automation `IUIAutomation::GetFocusedElement` and `IUIAutomationElement::GetCurrentPropertyValue(UIA_IsPasswordPropertyId)`, refreshed on `SetWinEventHook(EVENT_OBJECT_FOCUS)`, on a pool thread | None |
| Key names and typed text | `MapVirtualKeyEx(vk, MAPVK_VK_TO_CHAR, layout of the foreground thread)` for letters, digits, and punctuation; a fixed name table for the rest; `ToUnicodeEx` with flag bit 2 so the keyboard state is not changed | None |
| Cursor halo and click ripple | `SetWindowsHookEx(WH_MOUSE_LL)` on the same thread, which also owns the halo and ripple windows | None. Same UIPI limit |
| Overlay windows | Plain Win32 windows: `WS_POPUP`, `WS_EX_LAYERED \| WS_EX_TRANSPARENT \| WS_EX_TOPMOST \| WS_EX_TOOLWINDOW \| WS_EX_NOACTIVATE`, drawn with `UpdateLayeredWindow` from a 32-bit premultiplied DIB; text through GDI+ (`System.Drawing.Common`) on that DIB | None |
| Keep notes out of recordings | `SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE)` (Windows 10 2004 or later) | None |
| Global shortcuts | `RegisterHotKey` ([recipe](../skills/code/os-integrations/recipes.md#global-hotkey-registerhotkey)) | None |
| Hide desktop icons | Read `HideIcons` in `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced`; toggle with `WM_COMMAND 0x7402` to the desktop's `SHELLDLL_DefView` | None |
| Hide the taskbar | `SHAppBarMessage(ABM_GETSTATE)` and `ABM_SETSTATE` with `ABS_AUTOHIDE` | None |
| Focus (Do not disturb) | No public API. Runs two commands the user writes, through `pwsh` or `powershell.exe` | None |
| Chapter CSV | `FileSavePicker` with `InitializeWithWindow.Initialize(picker, hwnd)` | None |

Nothing here needs a permission prompt or elevation. The trade-off is UIPI: while an app that runs as administrator is in front, the hooks see nothing. Say so on the Keystrokes page.

Recipes: [tray icon](../skills/code/os-integrations/recipes.md#tray-icon), [low-level keyboard hook](../skills/code/os-integrations/recipes.md#low-level-keyboard-hook), [non-activating panel](../skills/code/os-integrations/recipes.md#non-activating-panel), [screen capture](../skills/code/os-integrations/recipes.md#screen-capture) (for the test recording check), [single instance](../skills/code/os-integrations/recipes.md#single-instance), [elevation and UIPI](../skills/code/os-integrations/recipes.md#elevation-and-uipi).

Types to create:
- `InputHooks`: one background thread with a message loop that owns both hooks. The keyboard callback copies the event into a `Channel` and returns at once.
- `ModifierState` and `KeystrokeModel` (Core): held modifiers with the AltGr rule, and the entries, counts, and skip rules (injected keys included). Unit tested.
- `KeystrokeOverlay`: `ObservableObject` entries, mode, the overlay window, fade timing.
- `CursorHighlight`: the halo window that follows the pointer, a pool of 4 ripple windows, hooks only while on. The hook thread owns these windows: the mouse callback stores the point and posts a thread message to itself, and the loop moves the window.
- `LayeredWindow`: one Win32 popup with the overlay styles, a DIB section, and `Present(bitmap, x, y)` through `UpdateLayeredWindow`. Every overlay uses it.
- `RecordingMode`: turn on, turn off, the saved `RecordingSnapshot`, resume at launch.
- `ChapterClock`: session, markers, the chapter prompt (a small WinUI window), YouTube text, CSV export, issues.
- `Teleprompter`: text and look settings, clock-based position, the plate window, a render loop while playing.
- `Toast` (dark capsule, excluded from capture), `HotkeyCenter`, `ShortcutRecorder`, `SettingsWindow` (pages: Keystrokes, Cursor, Recording mode, Chapters, Teleprompter).

Behaviors that matter:
- **Keystroke modes.** "Shortcuts only" (default) shows combos with Ctrl, Alt, or Win, plus Esc, Tab, Enter, keypad Enter, and the arrows. Shift alone does not make a combo. "All keys" shows typed text as one cap that grows, last 24 characters.
- **Key caps.** Modifiers read as words, in this order: Ctrl, Alt, Shift, Win. So Ctrl+Shift+K shows as three caps: Ctrl, Shift, K. Letters, digits, and punctuation come from `MapVirtualKeyEx(vk, MAPVK_VK_TO_CHAR, layout)` with the layout of the foreground window's thread, so a German layout shows its own keys. Every other key (arrows, Enter, keypad Enter, F-keys) comes from a fixed name table.
- **AltGr.** On layouts with AltGr, Windows sends a fake left Ctrl with scan code `0x21D` just before right Alt. While that Ctrl and right Alt are down, neither counts as a modifier. A real left Ctrl has scan code `0x1D`.
- **Keystroke timing.** Each key holds 1.5 s, then the capsule fades over 0.35 s and the window hides. At most 4 entries; older ones at 50% opacity. The same combo again within 1.5 s shows "×2", "×3". Keys live in memory only while on screen. Nothing is logged or saved.
- **Keystroke window.** 1200 × 150 epx, centered on the display with the pointer, 40 epx above the bottom of its work area (so above the taskbar). Caps are 20 epx text. Multiply every size by that display's scale (`GetDpiForMonitor` / 96).
- **Skip what the viewer should not see:** injected keys (`LLKHF_INJECTED`, which is how password managers type), keys while the focused element is a password field, the app's own shortcuts (compare virtual key and modifiers with every bound combo; in an app built from several specs, every feature's shortcuts), and typing in the chapter prompt.
- **Cursor halo.** Diameter 50, 70 (default), or 100 epx. Radial gradient of #7B5CFF at alpha 0.38, 0.2, 0 (stops 0, 0.55, 1). A press scales it to 0.78 over 0.12 s. Each click adds a ring 1.1 times the halo that scales 0.3 to 1.25 and fades 0.95 to 0 over 0.5 s, ease-out, at the click point. Right clicks use the lighter #A88FFF.
- **Recording mode** turns on what its options say (defaults: hide desktop icons on, hide the taskbar on, keystrokes on, cursor on, Focus commands empty). It records what it changed and turns off exactly that. Options can change only while it is off. The tray icon gets a red dot and its tooltip says "Studio, recording mode on"; the first tray menu item reads "Turn off recording mode".
- **Focus commands.** Two text fields, "Command when it turns on" and "Command when it turns off", run hidden through `pwsh -NoProfile -EncodedCommand`, or `powershell.exe` when pwsh is missing. A link opens `ms-settings:notifications` so the user can set Do not disturb by hand.
- **Chapters.** Start sets 00:00 with a first marker "Intro". Mark without a session starts one. A new marker is "Chapter N" and opens the prompt; Enter saves the label, Esc keeps "Chapter N". A second mark while the prompt is open saves what was typed first. Restart with more than one marker needs a second press within 3 s. Rename saves after 0.6 s; add and delete save at once.
- **Chapter prompt.** A small WinUI window, centered on the display with the pointer, always on top, excluded from capture. It has to take focus to get typing, so save `GetForegroundWindow()` before showing it and give focus back to that window on Enter or Esc.
- **Chapter export.** Lines like `05:07 Setup`, and `1:05:07` from one hour on. The first line is always 00:00. Warn under 3 chapters or any chapter under 10 seconds (YouTube rejects those). CSV header `index,timestamp,seconds,label`, UTF-8, file name `chapters-YYYY-MM-DD.csv`.
- **Teleprompter.** Speed 45 epx/s (10 to 200, shortcuts step 5), font 34 epx (20 to 72), width 760 epx (480 to 1400), plate opacity 0.85 (0.3 to 1), mirror off. Height is max(200, font × 5.5), centered 8 epx below the top of the work area on the display with the pointer, close to a webcam on top of the screen. The line you read sits 30% down, between two small violet arrows. It pauses at the end; play from the end starts at the top. It is click-through and excluded from capture.
- **Shortcuts.** None set by default, so nothing clashes. Every action is also in the tray menu. Each toggle shows a toast ("Keystrokes on", "Speed 50").

The hook callback only copies and returns. Work happens on a reader:

```csharp
private nint KeyboardProc(int code, nint wParam, nint lParam)
{
    if (code >= 0)
    {
        var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
        _keys.Writer.TryWrite(new KeyEvent((uint)wParam, info.vkCode, info.flags, info.time));
    }
    return CallNextHookEx(0, code, wParam, lParam);   // always pass the key on
}
// Keep the delegate in a field: _proc = KeyboardProc; SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0)
```

Teleprompter position comes from the clock, so nothing runs while paused:

```csharp
public double Position(long nowTicks)
{
    if (_playStart is not { } start) return Math.Min(_basePosition, MaxPosition);
    var moved = Speed * Stopwatch.GetElapsedTime(start, nowTicks).TotalSeconds;
    return Math.Min(_basePosition + moved, MaxPosition);
}
// Render loop, only while playing: draw at ReadingLine - Position(Stopwatch.GetTimestamp()), then DwmFlush() to wait for the next frame.
```

## Stories

```markdown
# Stories

## M1 Core loop

### S-001 Toggle each tool from the tray
As the user, I right-click the tray icon and switch keystrokes and the cursor halo on or off.
Screens: tray-menu.png
Acceptance:
- [snap] The menu lists Keystrokes and Cursor highlight with check marks, plus Settings and Quit.
- [self] Each item changes the tool's state and shows a toast, a layered window that reads back WDA_EXCLUDEFROMCAPTURE from GetWindowDisplayAffinity. A Windows notification from the tray icon is not this toast: it lands in recordings and in Notification Center.
Status: todo

### S-002 Know what the keystroke overlay can see
As the user, I turn on keystrokes and the Keystrokes page tells me what it shows and what it cannot see.
Screens: settings-keystrokes.png
Acceptance:
- [snap] The page says "Keys typed into apps running as administrator do not show. Studio never saves or sends your keys."
- [self] The keyboard hook handle is non-zero only while keystrokes are on, and zero after turning them off. With one keyboard hook shared by several features, check this only while no other feature holds it.
- [manual] With an elevated Terminal in front, no keys show and nothing breaks; keys show again within 1 s of switching back to a normal app.
Status: todo

### S-003 Show shortcuts as key caps
As the user, I press Ctrl+K and see Ctrl K as key caps at the bottom of the screen.
Screens: keystrokes.png, settings-keystrokes.png
Depends: S-002
Acceptance:
- [snap] Up to 4 entries in a dark capsule; the newest at full opacity, older at 50%.
- [unit] The same combo three times within 1.5 s gives one entry with count 3.
- [unit] In "All keys" mode typed text keeps the last 24 characters; arrows never become text.
- [unit] Injected key events never make an entry.
- [unit] A left Ctrl with scan code 0x21D plus right Alt counts as no modifier, so AltGr+Q gives "@" as text; a left Ctrl with scan code 0x1D plus Alt is a Ctrl+Alt combo.
- [self] Synthetic key events fed to the hook handler (not `SendInput`, whose keys are injected) make the expected entries, and nothing reaches the person's windows.
- [manual] A password field in Edge shows nothing in "All keys" mode, AltGr+Q on a German layout shows "@" as text and not as a Ctrl+Alt combo, and the overlay never takes a click.
Status: todo

### S-004 Highlight the cursor and clicks
As the user, I turn on the halo and every click draws a ripple.
Screens: cursor.png, settings-cursor.png
Acceptance:
- [snap] A violet halo follows the pointer; Small, Medium, Large set 50, 70, 100 epx.
- [manual] It follows the pointer across two displays with different scaling without lag or a size jump, and right clicks ripple lighter.
- [self] Posting a ripple message to the mouse hook thread, in place of a click, shows a ripple window at that point.
- [self] Turning it off removes the mouse hook and every overlay window.
Status: todo

### S-005 Set shortcuts in Settings
As the user, I record a shortcut for each action in Settings.
Screens: settings-keystrokes.png
Acceptance:
- [snap] Every action has a recorder field; a combo another app owns (ERROR_HOTKEY_ALREADY_REGISTERED) shows "Already in use".
- [self] Posting the `WM_HOTKEY` a recorded shortcut causes runs its action, and the same combo fed to the hook handler makes no entry in the keystroke overlay.
- [manual] Pressing a recorded shortcut runs its action, and it never shows in the keystroke overlay.
Status: todo

## M2 Recording mode and chapters

### S-006 Turn on Recording mode
As the user, I flip one switch and desktop icons, the taskbar, and the overlays change for filming.
Screens: tray-menu.png, settings-recording.png
Acceptance:
- [manual] Icons hide at once and the taskbar auto-hides; turning it off puts back exactly what it changed. Icons that were already hidden stay as they are.
- [manual] With Focus commands filled in, it runs the "on" command, and the "off" one when turned off, with no console window flashing.
- [snap] The tray icon shows a red dot while on.
Status: todo

### S-007 Restore after a quit, a crash, or sign-out
As the user, I quit with Recording mode on and my desktop comes back.
Depends: S-006
Acceptance:
- [self] A saved snapshot at launch shows Recording mode as on, and turning it off restores.
- [manual] Quitting from the tray with it on restores icons and the taskbar before the app exits.
- [manual] Signing out with it on restores them too (WM_ENDSESSION).
Status: todo

### S-008 Mark a chapter while recording
As the user, I press the chapter shortcut, type a label, and press Enter.
Screens: chapter-prompt.png
Acceptance:
- [snap] The prompt shows the timestamp, a text box, and hints for Enter and Esc.
- [unit] Timestamps read 05:07 under an hour and 1:05:07 after.
- [manual] After Enter, focus is back in the app I was using, and OBS keeps recording.
Status: todo

### S-009 Copy chapters for YouTube
As the user, I copy the list and paste it into the video description.
Screens: settings-chapters.png
Depends: S-008
Acceptance:
- [unit] The first line is pinned to 00:00; the issues list catches under 3 chapters and chapters under 10 s.
- [snap] Settings lists chapters with editable labels, remove buttons, Copy for YouTube, Export CSV, and Clear with a confirm.
Status: todo

## M3 Teleprompter

### S-010 Read a script from the teleprompter
As the user, I paste my script in Settings and show it at the top of the screen.
Screens: teleprompter.png, settings-teleprompter.png
Acceptance:
- [snap] White text on a dark plate, faded at top and bottom, violet arrows at the reading line, "Paused" badge.
- [snap] Font size, width, and opacity sliders show their values; mirror flips the text; a footer says to make a test recording.
- [manual] A test recording with the Windows Snipping Tool and with OBS shows no teleprompter.
Status: todo

### S-011 Play, pause, and change speed
As the user, I start scrolling with a shortcut and match my pace with faster and slower.
Depends: S-010
Acceptance:
- [unit] Changing speed while playing keeps the current position.
- [self] It pauses itself at the end, play from the end starts at the top, and no render loop runs while paused.
Status: todo

## Dropped
- Pausing clipboard history in Recording mode: Studio has no clipboard history.
- Turning Do not disturb on directly: Windows has no public API for it. The user's own commands and a link to Notifications settings stand in.
- Debug bridge commands and status fields for every tool.
- Per-feature animation speed settings.
```

## Gotchas

- **Hooks need a thread with a message loop, and not the UI thread.** A low-level hook is called by sending a message to the thread that installed it. On the WinUI thread, any slow layout delays every key and click on the whole PC. Run both hooks on one dedicated thread with a `GetMessage` loop, and hand events to the UI through a channel.
- **A slow hook is removed without a word.** If the callback takes longer than `LowLevelHooksTimeout` (at most 1000 ms on Windows 10 1709 and later), Windows 7 and later remove the hook silently. Never call UI Automation, the registry, or the UI from the callback. Copy, return, process elsewhere.
- **Keep the hook delegate alive.** Pass a delegate stored in a field to `SetWindowsHookEx`. A lambda that the garbage collector frees crashes the app on the next key, often minutes later.
- **The hooks are blind to elevated windows.** UIPI blocks input to a higher integrity window from reaching a normal app's low-level hooks. Studio does not run as administrator to fix this; elevating would also make every script it runs elevated. Say it on the Keystrokes page instead.
- **Antivirus watches keyboard hooks.** An unsigned exe that installs `WH_KEYBOARD_LL` looks like a keylogger to some scanners. Install the hook only while the overlay is on, remove it when off, write nothing about keys to disk or to `APP_LOG`, and sign the exe before you share it.
- **Track modifiers from the hook events.** Inside a low-level callback, `GetAsyncKeyState` still holds the state from before this key. Keep your own set of held modifiers from the down and up events, and clear it on a session lock (`WTSRegisterSessionNotification`, then `WTS_SESSION_LOCK`), or Ctrl sticks after Win+L.
- **AltGr is Ctrl plus Alt.** On layouts with AltGr, Windows sends a left Ctrl just before right Alt. Tell that fake Ctrl by its scan code `0x21D` and treat the pair as AltGr, or every "@" on a German keyboard shows as a Ctrl+Alt shortcut.
- **`GetKeyNameText` needs the extended-key bit.** Without `LLKHF_EXTENDED` moved into bit 24 of its argument, the arrows read as "Num 4" and keypad Enter reads as Enter. It also names keys in the system language, not the layout's. Use `MapVirtualKeyEx` for printable keys and a name table for the rest.
- **UI Automation has no managed wrapper in a WinUI app.** `UIAutomationClient` is a WPF assembly, and .NET 10 without WPF has none. Call `CoCreateInstance(CUIAutomation)` and two vtable slots: `IUIAutomation::GetFocusedElement` (slot 8) and `IUIAutomationElement::GetCurrentPropertyValue` (slot 10) with `UIA_IsPasswordPropertyId`. Run it on a pool thread on `EVENT_OBJECT_FOCUS`, never in the hook.
- **`ToUnicodeEx` can eat dead keys.** Calling it from the hook changes the kernel keyboard state, so ´ then e stops making é in the user's app. Pass flag bit 2 (Windows 10 1607 and later), which leaves the state alone.
- **No secure input flag on Windows.** macOS tells you when a password field has focus; Windows does not. Ask UI Automation for `CurrentIsPassword` when focus changes, cache the answer, and treat "unknown" as a password. Password managers inject keys, so skipping `LLKHF_INJECTED` covers autotype.
- **A WinUI window has no built-in per-pixel transparent background.** Draw the overlays in plain Win32 layered windows with `UpdateLayeredWindow` and a premultiplied BGRA bitmap. Click-through needs both `WS_EX_LAYERED` and `WS_EX_TRANSPARENT`. Acrylic does not exist there, so the capsules are a dark plate at 85% with a 1 epx light border.
- **GDI text has no alpha.** `DrawText` into the DIB writes alpha 0, so the caps' labels vanish. Wrap the DIB's memory in GDI+ (`new Bitmap(w, h, stride, PixelFormat.Format32bppPArgb, bits)` from `System.Drawing.Common`) and draw with `TextRenderingHint.AntiAliasGridFit`; ClearType leaves color fringes on a layered window.
- **A fade costs no redraw.** `UpdateLayeredWindow` with only a `BLENDFUNCTION` (no position, size, or DC) changes the overall alpha of the last bitmap.
- **The mouse hook must not wait on the UI thread.** `SetWindowPos` on a window another thread owns sends that thread a message and waits. A halo owned by the UI thread and moved from the hook thread blocks the hook whenever the UI is busy. Let the hook thread own the halo and ripple windows; low-level callbacks run on that thread inside `GetMessage`, so storing the point and posting to itself needs no locks.
- **Thread timers.** `SetTimer(0, id, ...)` without a window ignores `id` and returns a new one; `KillTimer(0, id)` needs the returned id, or the timer runs forever. Its `WM_TIMER` has `hwnd == 0`, so handle it in the loop before `DispatchMessage`.
- **Snapshots cannot see layered windows.** `PrintWindow` does not capture a layered Win32 window. For the `keystrokes` and `cursor` snapshots, paint the real overlay bitmap and show it in a WinUI preview window through a `SoftwareBitmapSource` (premultiplied BGRA matches the DIB exactly).
- **A full-screen overlay per display is too slow.** Redrawing a 4K layered window on every mouse move uploads 33 MB a frame. Move one small halo window with `UpdateLayeredWindow` (position only when the image has not changed), and draw each ripple in its own short-lived window at the click point.
- **Physical pixels everywhere.** With PerMonitorV2, hook points, `GetCursorPos`, and `SetWindowPos` are physical pixels. Convert epx with the scale of the display under the pointer (`MonitorFromPoint` then `GetDpiForMonitor`), and redraw the halo when the pointer crosses to a display with another scale.
- **Topmost is shared.** Other topmost windows, the taskbar, and Start can cover the overlays. Call `SetWindowPos(HWND_TOPMOST, SWP_NOACTIVATE | SWP_SHOWWINDOW)` each time an overlay shows. Exclusive full-screen games draw over everything; borderless full screen is fine.
- **`WDA_EXCLUDEFROMCAPTURE` covers Windows capture paths only.** Windows Graphics Capture, desktop duplication, and screenshot tools leave the window out. A capture card or a phone camera still sees it. Set it only on the teleprompter, the chapter prompt, and toasts; the keystroke overlay and halo are meant to be recorded. It works on an `UpdateLayeredWindow` window (Windows 11 26100). Check the return value, read it back with `GetWindowDisplayAffinity`, and tell the user to make a short test recording.
- **Desktop icons: missing is not false.** `HideIcons` may be missing, which means shown. Save the state you read, toggle with `WM_COMMAND 0x7402` only when it differs, and restore by the same rule. Never restart Explorer for this: it closes the user's folder windows mid-recording.
- **The taskbar setting belongs to the user.** Save the `ABM_GETSTATE` result in the snapshot before `ABM_SETSTATE`, and put back exactly that.
- **Restore on every way out.** Write the snapshot to `recording.json` on turn-on, so the next launch can restore after a crash. On quit and on `WM_ENDSESSION`, restore on the calling thread before returning; a `Task` started then may never run before the process ends.
- **Focus commands flash a console.** Start them with `CreateNoWindow = true` and `-EncodedCommand` (Base64 of UTF-16LE text), so quotes in the user's command survive.
- **The chapter prompt has to take focus.** Windows gives keyboard input only to the foreground window, so the macOS non-activating panel trick does not exist. The hotkey press lets Studio call `SetForegroundWindow`. Give focus back to the saved window when the prompt closes.
- **The teleprompter must not tick while paused.** Work out the position from `basePosition + speed × elapsed` and stop the render loop when not playing. When speed changes, set `basePosition` to the current position and reset `playStart`, or the text jumps. Render each paragraph to a cached bitmap once and copy only the visible ones per frame.

Done when keystrokes and the cursor halo show in a test recording while the teleprompter, prompt, and toasts do not, Recording mode hides and restores the desktop icons and taskbar, chapters copy as valid YouTube text, and the teleprompter scrolls at the set speed while staying out of the user's clicks.

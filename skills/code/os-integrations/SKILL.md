---
name: os-integrations
description: Deep Windows integrations for a native app, such as global hotkeys, keyboard and mouse hooks, tray icons, overlays and panels, moving other apps' windows, screen capture and recording, clipboard, sending keys, toasts, URL protocols, launch at login, single instance, and the elevation limits each one hits. Use when an app must reach outside its own window or control other apps. Use when one app hosts several such features as a suite.
---

# Windows integrations

A Windows app is a set of entry points around the system, not one window.
This skill picks the API for each entry point and the limit it runs into.
Code for every row marked "recipe" is in [recipes.md](recipes.md). Rows
marked with another file have their code there:

- [overlays.md](overlays.md): borderless and click-through overlay
  windows, placing panels, capture exclusion, the low-level mouse hook.
- [capture.md](capture.md): screen freeze, pickers, screen recording to
  MP4, live window thumbnails, and testing captures safely.
- [system.md](system.md): the window list for a switcher, app icons,
  Windows Search, live tray icons, UI Automation, power requests, process
  command lines and ports.

## Pick the API

| Want | API | Permission or limit |
| --- | --- | --- |
| Global shortcut (key combo) | `RegisterHotKey` on a hidden window (recipe) | None. Fails with error 1409 when another app owns the combo |
| Hold-to-show (hold Alt, press a key), key remapping, snippet expansion | `SetWindowsHookEx(WH_KEYBOARD_LL)` on its own thread (recipe) | None. Sees no keys typed into elevated windows |
| Watch keys without changing them (keystroke overlay) | The same `WH_KEYBOARD_LL` hook, returning false for every key (recipe). In a suite, the shared hook | None. Not Raw Input: it marks injected keys only by `hDevice == 0`, needs a window for `WM_INPUT`, and has no `LLKHF_ALTDOWN` |
| Watch the mouse (cursor highlight, click ripples, reset on a click) | `SetWindowsHookEx(WH_MOUSE_LL)` on a thread that also owns the overlay windows ([overlays.md](overlays.md)) | None |
| List other apps' windows (a switcher, a window picker) | `EnumWindows` plus a filter ([system.md](system.md)) | None |
| Tray icon (the menu bar extra) | `Shell_NotifyIcon` on a hidden window (recipe) | None. New icons start in the overflow |
| Live numbers in the tray (CPU, memory) | One icon per metric on the feature's own window, redrawn per sample ([system.md](system.md)) | None. Windows keeps a settings entry per icon for good |
| Live status in the taskbar (a timer, a sync) | Tray icon tooltip, or `ITaskbarList3` progress on the taskbar button | None |
| Desktop widgets (Widgets board), share targets, Explorer context menus, startup tasks, Windows AI APIs | Need package identity | Add a sparse package to the unpackaged app (`distribute` skill, step 5), as PowerToys does for its context menus |
| Tray-only app, no taskbar button | Never `Activate()` the main window at launch; the tray icon shows it | None |
| Spotlight-style panel that keeps the other app in front | Topmost window with `WS_EX_NOACTIVATE`, or one that activates and hands focus back (recipe) | None |
| A window with no frame at all (a frozen screen, a full-display picker) | WinUI window plus a `WM_NCCALCSIZE` subclass and three DWM attributes ([overlays.md](overlays.md)) | None |
| Click-through overlay with per-pixel alpha (key caps, a halo) | Win32 layered popup drawn with `UpdateLayeredWindow` ([overlays.md](overlays.md)) | None. `PrintWindow` cannot capture it |
| Keep a window out of screenshots and recordings | `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` ([overlays.md](overlays.md)) | None. Also blanks `PrintWindow` |
| Move and resize other apps' windows | `SetWindowPos`, corrected by `DWMWA_EXTENDED_FRAME_BOUNDS` (recipe) | None. Elevated windows need an elevated app |
| Paste into the front app, type text | Clipboard, then `SendInput` with a marker in `dwExtraInfo` (recipe) | None. UIPI drops it silently for elevated windows |
| Screenshot of a display or an area | GDI `BitBlt` of the virtual screen, frozen before any picker shows ([capture.md](capture.md)) | None. No border, no prompt |
| Screenshot of one window | `GraphicsCaptureItem.TryCreateFromWindowId`, cropped to the visible frame (recipe) | None. Yellow border unless the person allowed borderless capture |
| Window thumbnails | Live: `DwmRegisterThumbnail` ([capture.md](capture.md)). Still: the window screenshot above | None |
| Screen recording to MP4 | `Windows.Graphics.Capture` into `MediaStreamSource` and `MediaTranscoder` ([capture.md](capture.md)) | Yellow border as above. Microphone: Settings > Privacy & security > Microphone |
| Clipboard history | `AddClipboardFormatListener`, then Win32 reads (recipe) | None. Skip content marked by password managers and by other tools |
| Text in images | `Windows.Media.Ocr.OcrEngine` | None |
| Find files by name | The Windows Search index through OLE DB ([system.md](system.md)) | Only indexed folders. Nothing when the service is off |
| App and file icons | `StorageFile.GetThumbnailAsync`, then `SHDefExtractIcon` ([system.md](system.md)) | None |
| Ask another app's UI a question (is the focused field a password?) | UI Automation through raw COM ([system.md](system.md)) | None. The managed UIA library is WPF only |
| Launch at login | `HKCU\...\CurrentVersion\Run` value (recipe) | None. People can turn it off in Task Manager |
| One running copy, second launch hands over | `AppInstance.FindOrRegisterForKey` in a custom `Main` (recipe) | None |
| Links from scripts, Stream Deck, PowerToys Run | URL protocol under `HKCU\Software\Classes` (recipe) | None |
| Agent or script reads and changes data | A console CLI that talks to the running app over a named pipe (`agent-cli` skill) | None |
| A CLI that writes its own files tells the app to reload | A named `EventWaitHandle` (`Local\<App>-changed`) the app waits on | None |
| Keep the PC awake | `SetThreadExecutionState` (already in `UI/Win32.cs`), or a `PowerCreateRequest` handle with a reason ([system.md](system.md)) | None |
| Check that the PC is kept awake | `CallNtPowerInformation(SystemExecutionState)` ([system.md](system.md)) | None. `powercfg /requests` needs an admin terminal |
| System stats | `GetSystemTimes`, `GlobalMemoryStatusEx`, `GetSystemPowerStatus`, on a sampler thread with EcoQoS (`performance` skill) | None |
| Listening ports and process command lines | `GetExtendedTcpTable`, `NtQueryInformationProcess` ([system.md](system.md)) | None. Elevated processes return no command line |
| Audio devices | `Windows.Media.Devices.MediaDevice`, or the Core Audio `IMMDeviceEnumerator` | None |
| Notifications | `AppNotificationManager` from the Windows App SDK (recipe) | The user can turn them off. Not shown for elevated apps |
| Share with other apps | `DataTransferManager` through `IDataTransferManagerInterop` | None |

Prefer the row with no limit when two rows solve the same job.

## From macOS permissions to Windows limits

BAMA's Mac apps ask for permissions. Windows asks for almost none, and has
other walls instead:

| macOS | Windows |
| --- | --- |
| Accessibility (event taps, AX window control, posting keys) | No permission. UIPI blocks input, hooks, and window moves into elevated (administrator) apps. See "Elevation and UIPI" in recipes.md |
| Input Monitoring | No permission. A low-level hook sees every key except those typed into elevated windows |
| Screen Recording | No permission. GDI reads the screen with no sign. `Windows.Graphics.Capture` draws a yellow border around what it captures, until the person allows borderless capture |
| Menu bar extra | Tray icon. Windows hides new icons in the overflow until the user pins them |
| Login item approval | None. The Run key works at once; Task Manager can turn it off later |
| Notification permission prompt | None at first. People turn notifications off per app in Settings |
| Camera, microphone, location | Settings > Privacy & security, with a "Let desktop apps access" switch for each |

## Limits the user can see

- Ask nothing up front. Show a limit at the moment it bites, in one line
  that says what broke and what to do: "This window runs as
  administrator. MyApp cannot paste into it."
- Show the state of every hook on one Settings page: the hotkey (works, or
  "used by another app"), launch at login (on, off, or "turned off in
  Task Manager"), notifications (`AppNotificationManager.Default.Setting`),
  the capture border (read with `CheckAccess`, which never prompts), and
  the tray icon. Each gets a button that fixes it or opens the right
  page: `ms-settings:startupapps`, `ms-settings:notifications`,
  `ms-settings:privacy-microphone`, `ms-settings:privacy-webcam`.
- Read the state when Settings opens and when the window gets focus.
  Windows calls nothing when the user changes these elsewhere.
- Run as the normal user. Never mark the app `requireAdministrator`.

## Tray icons, taskbar, and toasts

- A tray icon shows a menu on right-click and opens the main window on
  left-click or Enter. Its .ico has 16, 20, 24, and 32 px sizes that read
  on light and dark taskbars. People can turn it off in Settings, and
  nothing is reachable only through it: the main window and the agent CLI
  offer the same actions.
- Tell the user once how to pin the icon: Settings > Personalization >
  Taskbar > Other system tray icons. The app cannot pin itself.
- A toast says one thing that happened, with a click that opens the exact
  place. Never put anything in a toast that the app does not also show.
- A note meant for the person while they record (a "Keystrokes on" pill)
  is an overlay excluded from capture, not a toast: toasts land in the
  recording and in the notification center.
- Badge the taskbar button (`ITaskbarList3.SetOverlayIcon`) only with a
  count of unread items.

## The install path keeps registrations

The Run key, the URL protocol, the toast registration, and every tray
icon store the exe's path. To keep them working across rebuilds:

- Install to the same path every time. `build.ps1` installs to
  `%LOCALAPPDATA%\Programs\<App>\<App>.exe`.
- Register from the installed exe only, never from a build folder or a
  test run (`APP_STORE` set). That includes `NIM_ADD`: Windows 11 writes a
  key per exe path and icon id under `HKCU\Control Panel\NotifyIconSettings`
  and never removes it. One dev PC had dozens of entries for
  old dev folders. A test run draws its icons but never adds them.
- Remove every registration in the uninstall step: the Run value, the
  `HKCU\Software\Classes\<scheme>` key, and
  `AppNotificationManager.Default.UnregisterAll()`.
- Files built on this PC carry no Mark of the Web, so SmartScreen does
  not warn about them. A copy downloaded from the internet gets a warning
  until it is signed (`ship` stage).

If a hook works from the build folder but not after install, a
registration still points at the old path. Remove it and register again
from the installed exe.

## Suites

In a PowerToys-shaped app (one tray app, many features), the host owns
every integration that Windows allows only once per process or per
person, and features subscribe. The `new-windows-app` skill's
[suite.md](../../start/new-windows-app/suite.md) has the whole shape.
For integrations:

| The host owns | Features get |
| --- | --- |
| The tray icon (uID 1) and its hidden top-level window | Menu rows and a flyout block. A feature with icons of its own (live stats) puts them on its own window with uIDs from 101 |
| Single instance | Nothing to do: a second launch hands over to the running copy (it opens Settings) |
| The hotkey registry: one `RegisterHotKey` per action, keyed by action id | A default combo per action, and "used by another app" when it failed. No two actions share a default |
| One `WH_KEYBOARD_LL` hook on its own thread | `Subscribe(Func<KeyEvent, bool>)`, disposed in `Stop`. The hook starts with the first subscriber and stops with the last |
| A foreground tracker (`SetWinEventHook(EVENT_SYSTEM_FOREGROUND)`) | "The window you were using", never the app's own panel or the taskbar |

The shared keyboard hook is the one that matters most. Windows removes a
hook that takes over a second, and ten hooks in one process are ten
chances. The shape:

```csharp
public readonly record struct KeyEvent(int VirtualKey, int ScanCode, bool Down, bool Injected,
    bool AltDown, uint Time, bool Extended, nuint ExtraInfo);

// In a feature's Start, and Dispose in Stop:
_subscription = Host.Input.Subscribe(e =>
{
    if (e.Injected && e.ExtraInfo == MyMarker) return false;   // our own SendInput
    _queue.Enqueue(e);                                          // copy, then return at once
    Host.Dispatcher.TryEnqueue(Drain);
    return false;                                               // true only to swallow the key
});
```

Rules for the shared hook:

- The event carries everything a subscriber needs: the injected flag, the
  extended-key flag (for key names and keypad Enter), Alt, and
  `dwExtraInfo`. Each feature filters for itself: the keystroke overlay
  drops injected keys, a switcher may want them. A first version without
  `Extended` and `ExtraInfo` pushed two features into workarounds.
- Expose a subscriber count, so a feature's self-test can check that its
  own subscription went away while others still listen.
- Other one-per-process jobs follow the same rule: a feature that needs a
  mouse hook, a clipboard listener, or a message window brings one only
  until the host has one. Move it into the host when a second feature
  needs it.
- Run background samplers with EcoQoS on their own thread, never on the
  whole process: a throttled process slows every other feature
  (`performance` skill, "Timers, polling, and energy").

## Make every feature reachable three ways

Each feature gets: a command with a `KeyboardAccelerator`, a URL
(`<app>://action/<name>`), and an agent CLI command (`agent-cli` skill).
The same action enum backs all three, so a new action shows up everywhere
at once. Add a self-test check that every action has a command label, a
URL name, and a CLI command. The URL and the CLI also let
`test-windows-app` drive the feature from a script.

Self-tests of these integrations act only on the app's own test windows:
feed hook handlers synthetic events, read captures from a test window, and
check `GetForegroundWindow` before every `SendInput`. Each recipe ends
with how it was tested safely.

Done when every entry point in the brief's "System hooks" works from a
clean install, Settings shows the state of every hook it needs, and no
test run left a registration or a tray icon entry behind.

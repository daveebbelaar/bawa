---
name: os-integrations
description: Deep Windows integrations for a native app, such as global hotkeys, tray icons, floating panels, moving other apps' windows, screen capture, clipboard, toasts, URL protocols, launch at login, single instance, a CLI, and the elevation limits each one hits. Use when an app must reach outside its own window or control other apps.
---

# Windows integrations

A Windows app is a set of entry points around the system, not one window.
This skill picks the API for each entry point and the limit it runs into.
Code for every row marked "recipe" is in [recipes.md](recipes.md).

## Pick the API

| Want | API | Permission or limit |
| --- | --- | --- |
| Global shortcut (key combo) | `RegisterHotKey` on a message-only window (recipe) | None. Fails with error 1409 when another app owns the combo |
| Hold-to-show (hold Alt, press a key) or key remapping | `SetWindowsHookEx(WH_KEYBOARD_LL)` on its own thread (recipe) | None. Sees no keys typed into elevated windows |
| Watch keys without changing them (keystroke overlay) | Raw Input (`RegisterRawInputDevices`, `RIDEV_INPUTSINK`) | None |
| List other apps' windows | `EnumWindows`, skipping cloaked ones (`DWMWA_CLOAKED`) | None |
| Tray icon (the menu bar extra) | `Shell_NotifyIcon` on a hidden window (recipe) | None. New icons start in the overflow |
| Live status in the taskbar (a timer, a sync) | Tray icon tooltip, or `ITaskbarList3` progress on the taskbar button | None |
| Desktop widgets (Widgets board), share targets, Explorer context menus, startup tasks, Windows AI APIs | Need package identity | Add a sparse package to the unpackaged app (`distribute` skill, step 5), as PowerToys does for its context menus |
| Tray-only app, no taskbar button | Never `Activate()` the main window at launch; the tray icon shows it | None |
| Spotlight-style panel that keeps the other app in front | Topmost borderless window with `WS_EX_NOACTIVATE`, or one that activates and hands focus back (recipe) | None |
| Move and resize other apps' windows | `SetWindowPos`, corrected by `DWMWA_EXTENDED_FRAME_BOUNDS` (recipe) | None. Elevated windows need an elevated app |
| Paste into the front app | Clipboard, then `SendInput` Ctrl+V (recipe) | None. UIPI drops it silently for elevated windows |
| Screenshot | `Windows.Graphics.Capture` with `CreateForMonitor` (recipe) | None. Yellow border while capturing |
| Window thumbnails | Live: `DwmRegisterThumbnail`. Still: `Windows.Graphics.Capture` with `CreateForWindow` | None |
| Screen recording to MP4 | `Windows.Graphics.Capture` + Media Foundation ([gpu-media.md](../performance/gpu-media.md)) | Microphone: Settings > Privacy & security > Microphone |
| Clipboard history | `AddClipboardFormatListener` (recipe) | None. Skip content marked `ExcludeClipboardContentFromMonitorProcessing` |
| Text in images | `Windows.Media.Ocr.OcrEngine` | None |
| Launch at login | `HKCU\...\CurrentVersion\Run` value (recipe) | None. People can turn it off in Task Manager |
| One running copy, second launch hands over | `AppInstance.FindOrRegisterForKey` in a custom `Main` (recipe) | None |
| Links from scripts, Stream Deck, PowerToys Run | URL protocol under `HKCU\Software\Classes` (recipe) | None |
| Agent or script reads and changes data | A console CLI that talks to the running app over a named pipe (`agent-cli` skill) | None |
| A CLI that writes its own files tells the app to reload | A named `EventWaitHandle` (`Local\<App>-changed`) the app waits on | None |
| Keep the PC awake | `SetThreadExecutionState` (already in `UI/Win32.cs`) | None |
| System stats | `GetSystemTimes`, `GlobalMemoryStatusEx`, `GetSystemPowerStatus` | None |
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
| Screen Recording | No permission. Windows draws a yellow border around what `Windows.Graphics.Capture` captures |
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
  and the tray icon. Each gets a button that fixes it or opens the right
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
- Badge the taskbar button (`ITaskbarList3.SetOverlayIcon`) only with a
  count of unread items.

## The install path keeps registrations

The Run key, the URL protocol, and the toast registration all store the
exe's path. To keep them working across rebuilds:

- Install to the same path every time. `build.ps1` installs to
  `%LOCALAPPDATA%\Programs\<App>\<App>.exe`.
- Register from the installed exe only, never from a build folder or a
  test run (`APP_STORE` set).
- Remove every registration in the uninstall step: the Run value, the
  `HKCU\Software\Classes\<scheme>` key, and
  `AppNotificationManager.Default.UnregisterAll()`.
- Files built on this PC carry no Mark of the Web, so SmartScreen does
  not warn about them. A copy downloaded from the internet gets a warning
  until it is signed (`ship` stage).

If a hook works from the build folder but not after install, a
registration still points at the old path. Remove it and register again
from the installed exe.

## Make every feature reachable three ways

Each feature gets: a command with a `KeyboardAccelerator`, a URL
(`<app>://action/<name>`), and an agent CLI command (`agent-cli` skill).
The same action enum backs all three, so a new action shows up everywhere
at once. Add a self-test check that every action has a command label, a
URL name, and a CLI command. The URL and the CLI also let
`test-windows-app` drive the feature from a script.

Done when every entry point in the brief's "System hooks" works from a
clean install, and Settings shows the state of every hook it needs.

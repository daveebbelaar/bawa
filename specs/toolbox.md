# Toolbox

One tray panel for the small Windows settings you flip every day: keep the PC awake, show hidden files and file extensions, hide desktop icons, dark mode, taskbar auto-hide, and sound devices. Replaces: PowerToys Awake, Caffeine, Winaero Tweaker, SoundSwitch. Ported from BAMA's toolbox spec.

## Brief

```markdown
# Toolbox

A tray panel of one-click toggles for the Windows settings you change every day.

## Replaces
PowerToys Awake, Caffeine, Winaero Tweaker, SoundSwitch

## Main jobs
1. Keep the PC awake until I turn it off, for a set time, or while one app runs.
2. Flip hidden files, file extensions, desktop icons, dark mode, and taskbar auto-hide
   with one click.
3. Switch the sound output and input device.

## Data
- Settings: Keep awake duration (default "Until turned off"), shortcuts, open at login.
  %LOCALAPPDATA%\Toolbox\settings.json.
- Keep awake session: on, end time, app pid, name, and start time. Memory only.

## Shape
Tray icon in the notification area with a flyout panel above it, plus a WinUI Settings
window. Windows 11 first, Windows 10 19041 or later runs. Unpackaged .exe, no taskbar
button while only the flyout is open. Local data. Works offline. No accounts.

## System hooks
Tray icon, global shortcuts for four toggles (no defaults), launch at login, single
instance. No elevation: every setting here lives under HKCU or in the shell.

## Reference
PowerToys Awake for Keep awake, Windows 11 Quick Settings for the look.

## Look
Native WinUI, system accent color, Mica on Settings, Acrylic on the flyout. A grid of
Quick Settings tiles: rounded rectangle, Segoe Fluent icon, a one-word label under it,
accent fill when on. A short dark toast near the pointer confirms each change.

## Not in version one
- Keep awake schedules and battery rules
- More hidden settings (screenshot folder, key repeat, protected system files)
- Per-app sound output
- Reverse mouse wheel
```

## How it works

| Part | API | Permission |
| --- | --- | --- |
| Keep awake | `PowerCreateRequest` with a `REASON_CONTEXT` string, `PowerSetRequest` for `PowerRequestSystemRequired` and `PowerRequestDisplayRequired`, `PowerClearRequest` | None |
| Stop when an app quits | `Process.GetProcessById(pid)`, `EnableRaisingEvents`, `Exited` | None for your own apps; see Gotchas for elevated ones |
| Hidden files, file extensions | `Hidden` and `HideFileExt` DWORDs in `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced`, then `SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST \| SHCNF_FLUSH)` | None |
| Desktop icons | Read `HideIcons` in the same key. Toggle with `PostMessage(WM_COMMAND, 0x7402)` to the desktop's `SHELLDLL_DefView` window (undocumented, see Gotchas) | None |
| Taskbar auto-hide | `SHAppBarMessage(ABM_GETSTATE)` and `ABM_SETSTATE` with `ABS_AUTOHIDE` | None |
| Read and set dark mode | `AppsUseLightTheme` and `SystemUsesLightTheme` DWORDs in `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize`, then `SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, 0, "ImmersiveColorSet")` | None |
| Follow dark mode changes | `Windows.UI.ViewManagement.UISettings.ColorValuesChanged` | None |
| List sound devices | MMDevice API: `IMMDeviceEnumerator.EnumAudioEndpoints(eRender or eCapture, DEVICE_STATE_ACTIVE)`, `PKEY_Device_FriendlyName`, `GetDefaultAudioEndpoint(eRender, eConsole)` | None |
| Follow device changes | `IMMNotificationClient` through `RegisterEndpointNotificationCallback` | None |
| Set the default device | No public API. The undocumented `IPolicyConfig.SetDefaultEndpoint` COM interface, behind one class, with `ms-settings:sound` as the fallback | None |
| Shortcuts | `RegisterHotKey` | None |
| Tray icon and flyout | `Shell_NotifyIcon`, `Shell_NotifyIconGetRect` to place the flyout, a borderless WinUI window | None |

Recipes: [global hotkey](../skills/code/os-integrations/recipes.md#global-hotkey-registerhotkey), [tray icon](../skills/code/os-integrations/recipes.md#tray-icon), [launch at login](../skills/code/os-integrations/recipes.md#launch-at-login), [single instance](../skills/code/os-integrations/recipes.md#single-instance), [non-activating panel](../skills/code/os-integrations/recipes.md#non-activating-panel) (the toast), [elevation and UIPI](../skills/code/os-integrations/recipes.md#elevation-and-uipi).

Main types:

- `KeepAwake`: `ObservableObject`. Holds the power request handle, the end timer, and the app watch. `KeepAwakeDuration`: `Minutes15`, `Hour1`, `Hours2`, `Indefinitely`.
- `SystemToggle`: enum `HiddenFiles`, `FileExtensions`, `DesktopIcons`, `DarkMode`, `TaskbarAutoHide`, with title, one-word label, detail line, Segoe Fluent glyph, toast text.
- `SystemToggles`: reads and writes the setting behind each toggle. No state. Lives in `Toolbox.Core` except the Win32 calls.
- `QuickToggles`: `ObservableObject` live state for the views, a `Busy` set, optimistic changes.
- `AudioDevices`: `ObservableObject` list of outputs and inputs and the current defaults, in MMDevice ids.
- `DefaultDeviceSetter`: the only class that touches `IPolicyConfig`. Returns true or false, never throws.
- `TrayIcon`: the notification area icon, left click opens the flyout, right click shows Settings and Quit.
- `Toast`: a dark rounded capsule near the pointer, gone after 1.5 s, click-through, never takes focus.
- `FlyoutWindow`: the tiles, the Keep awake timer menu, the sound menu.

Behaviors that matter:

- **Keep awake.** It stops idle sleep and keeps the display on: set both `PowerRequestSystemRequired` and `PowerRequestDisplayRequired`. Closing the lid, pressing the power button, or picking Sleep in Start still sleeps the PC; say so in Settings. Every start first clears and closes the old request and stops the timer and app watch. Timed: a one-shot `DispatcherQueueTimer` that turns it off. While an app runs: watch `Process.Exited` and stop with a toast "Code quit. Keep awake is off". Changing the duration while a timed session runs restarts it with the new length.
- **Keep awake text.** Settings: "On, 14:59 left", "On while Code runs", "On until you turn it off", or "Off. Your PC sleeps as usual." The tile label shows the time left, else "Awake". The tray tooltip says the same. Clock format `h:mm:ss` over an hour, else `m:ss`, rounded up. Tick once a second with a `DispatcherQueueTimer` that runs only while the flyout or Settings is open.
- **Keep awake tile.** Click toggles with the saved duration. Right-click shows 15 minutes, 1 hour, 2 hours, Until turned off, and Turn off.
- **Toggles.** Click sets the new state right away, marks the tile busy (a small `ProgressRing`, further clicks ignored), and runs the change in a `Task` off the UI thread. On success: toast. On failure: put the old state back and toast the error.
- **Refresh.** Read every toggle again when the flyout or Settings opens (the flyout window stays alive, so read on its `Activated` event), on `UISettings.ColorValuesChanged`, and on `WM_SETTINGCHANGE` in the tray window.
- **Sound.** The sound menu lists outputs, then inputs, each with a check on the current default. Picking one calls `DefaultDeviceSetter` for `eConsole`, `eMultimedia`, and `eCommunications`. Treat only `eConsole` as the result; ignore the other two failing. When it returns false, open `ms-settings:sound` and toast "Windows did not allow the switch. Pick the device in Sound settings." Refresh on device added, removed, state changed, and default changed.
- **App picker.** Settings lists running apps that have a main window (`Process.MainWindowHandle != 0`), with their icon and name, sorted by name.

| Toggle | Value and key | Missing means | Applies by |
| --- | --- | --- | --- |
| Show hidden files | `Hidden` in `Explorer\Advanced`: 1 shows, 2 hides | hide | `SHChangeNotify` |
| Show file extensions | `HideFileExt` in `Explorer\Advanced`: 0 shows, 1 hides | hide | `SHChangeNotify` |
| Show desktop icons | `HideIcons` in `Explorer\Advanced`: 1 hides | show | `WM_COMMAND 0x7402` to `SHELLDLL_DefView` |
| Hide the taskbar automatically | `ABM_GETSTATE` has `ABS_AUTOHIDE` | n/a | `ABM_SETSTATE` |
| Dark mode | `AppsUseLightTheme == 0` in `Themes\Personalize` | off (light) | both values, then `WM_SETTINGCHANGE "ImmersiveColorSet"` |

Reading an Explorer value. Missing is common, so return null and use the "missing means" column:

```csharp
const string Advanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";

static int? ReadDword(string keyPath, string name)
{
    using var key = Registry.CurrentUser.OpenSubKey(keyPath);
    return key?.GetValue(name) is int value ? value : null;
}

bool showHidden = ReadDword(Advanced, "Hidden") == 1;          // missing or 2: hidden
bool showExtensions = ReadDword(Advanced, "HideFileExt") == 0; // missing or 1: hidden
```

Keep awake. One request handle, two request types:

```csharp
var reason = new REASON_CONTEXT
{
    Version = POWER_REQUEST_CONTEXT_VERSION,          // 0
    Flags = POWER_REQUEST_CONTEXT_SIMPLE_STRING,      // 1
    SimpleReasonString = Marshal.StringToHGlobalUni("Toolbox Keep awake"),
};
_request = PowerCreateRequest(ref reason);            // INVALID_HANDLE_VALUE on failure
PowerSetRequest(_request, PowerRequestType.SystemRequired);
PowerSetRequest(_request, PowerRequestType.DisplayRequired);
// Off: PowerClearRequest for both types, CloseHandle(_request), free the string.
```

Applying an Explorer change, off the UI thread:

```csharp
Registry.SetValue(@"HKEY_CURRENT_USER\" + Advanced, "Hidden", on ? 1 : 2, RegistryValueKind.DWord);
SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST | SHCNF_FLUSH, 0, 0); // 0x08000000, 0x0000 | 0x1000
```

## Stories

```markdown
## M1 Core loop

### S-001 Turn Keep awake on and off
As the user, I click the cup tile in the tray flyout and the PC stops going to sleep.
Screens: flyout.png
Acceptance:
- [snap] The cup tile shows its accent on state and the label "Awake".
- [self] KeepAwake holds exactly one power request handle while on and none after the second click.
- [manual] In an admin terminal, `powercfg /requests` lists "Toolbox Keep awake" under DISPLAY and SYSTEM; it is gone after the second click.
- [manual] A toast "Keep awake on" shows for 1.5 s near the pointer.
Status: todo

### S-002 Keep the PC awake for a set time
As the user, I right-click the cup tile and pick 15 minutes, 1 hour, or 2 hours.
Depends: S-001
Acceptance:
- [unit] `Clock(3599)` is "59:59" and `Clock(7199.2)` is "2:00:00".
- [snap] The tile label counts down, for example "14:59".
- [self] Starting a new session clears the old request first: only one handle exists.
Status: todo

### S-003 Show or hide hidden files
As the user, I click the eye tile and File Explorer shows hidden files.
Screens: flyout.png
Acceptance:
- [unit] `Hidden` 1 reads as on; 2 and a missing value read as off.
- [manual] An open File Explorer window on the user folder shows AppData within 1 s, with no Explorer restart; the tile shows a spinner until it is done.
Status: todo

### S-004 Show or hide file extensions
As the user, I click the extensions tile and file names show ".docx" and ".exe".
Acceptance:
- [unit] `HideFileExt` 0 reads as on; 1 and a missing value read as off.
- [manual] An open File Explorer window shows the extensions within 1 s, and hides them again with a second click.
Status: todo

### S-005 Hide desktop icons and the taskbar
As the user, I click the desktop tile for a clean desktop before I share my screen, and the taskbar tile to hide the taskbar.
Acceptance:
- [unit] A missing `HideIcons` value reads as icons shown.
- [manual] Desktop icons disappear at once without an Explorer restart, and come back with a second click.
- [manual] The taskbar tile turns on auto-hide; the tile reads the state back with ABM_GETSTATE and matches Settings > Personalization > Taskbar.
Status: todo

### S-006 Switch dark mode
As the user, I click the moon tile and Windows and my apps switch to dark.
Acceptance:
- [self] After the switch, `AppsUseLightTheme` and `SystemUsesLightTheme` are both 0, and both 1 after a second click.
- [manual] The taskbar, File Explorer, and Settings change within 2 s, and the tile stays in sync when I change the mode in Settings > Personalization > Colors.
Status: todo

### S-007 Switch the sound output and input
As the user, I open the sound menu in the flyout header and pick a device.
Screens: flyout-sound.png
Acceptance:
- [snap] The header shows the current output name; the menu lists outputs and inputs with a check on the current ones.
- [manual] Plugging in headphones adds them to the list without reopening the flyout.
- [manual] Picking a device makes it the default in Settings > System > Sound. When the switch fails, Sound settings opens with a toast that says so.
Status: todo

## M2 Settings and extras

### S-008 Keep the PC awake while an app runs
As the user, I pick an app in Settings and Keep awake stays on until that app quits.
Screens: settings.png
Acceptance:
- [manual] Quitting the app turns Keep awake off with a toast naming the app.
- [snap] The status line reads "On while Code runs".
Status: todo

### S-009 Settings page
As the user, I open Settings and see Keep awake, the five toggles with a detail line each, Sound, and Shortcuts.
Screens: settings.png
Acceptance:
- [snap] Each toggle is a SettingsCard with a glyph, a title, one line of detail, and a ToggleSwitch.
- [snap] The Keep awake footer says "Closing the lid or pressing the power button still sleeps the PC."
Status: todo

### S-010 Give a toggle a shortcut
As the user, I record a shortcut for Keep awake, hidden files, desktop icons, or dark mode.
Depends: S-009
Acceptance:
- [manual] The shortcut flips the setting and shows the toast.
- [snap] A shortcut another app owns (RegisterHotKey fails with ERROR_HOTKEY_ALREADY_REGISTERED) shows an orange "Already in use" next to the field.
Status: todo

### S-011 Launch at login
As the user, I turn on "Open at login" in Settings.
Acceptance:
- [self] The toggle reflects whether HKCU\Software\Microsoft\Windows\CurrentVersion\Run has a Toolbox value pointing at the installed exe.
- [manual] After signing out and in, the tray icon is there and Keep awake is off.
Status: todo

## Dropped
- Debug bridge commands, URL protocol, and Start menu jump list for each toggle
- App icon picker
- Reverse mouse wheel: Windows has no per-user setting for it. It needs either a mouse hook that swallows and re-sends every wheel event, or the per-device `FlipFlopWheel` value under HKLM, which needs admin.
```

## Gotchas

- **Explorer does not watch these registry values.** Writing `Hidden` or `HideFileExt` changes nothing on screen until Explorer is told. Call `SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST | SHCNF_FLUSH)` after the write. Restarting explorer.exe also works, but it closes every Explorer window and redraws the taskbar; never do that for a toggle. If a Windows build ignores the notify, `SHGetSetSettings` with `SSF_SHOWALLOBJECTS` or `SSF_SHOWEXTENSIONS` is the shell call that sets the same state.
- **Hidden means the Hidden attribute, not a dot.** Windows hides files with the Hidden attribute. `.gitignore` and `.env` already show. Test with AppData in the user folder, not with a dot file.
- **`HideIcons` alone does nothing until Explorer restarts.** The desktop menu's View > Show desktop icons is the only live path: post `WM_COMMAND` with `0x7402` to the `SHELLDLL_DefView` window. It is an undocumented command id, and it toggles, so read `HideIcons` first and send it only when the state differs. Look for `SHELLDLL_DefView` under `Progman` first, then under each `WorkerW` window; it moves there when a wallpaper slideshow runs. Read `HideIcons` back 500 ms later and toast an error if it did not change.
- **Two values make dark mode.** `AppsUseLightTheme` is apps, `SystemUsesLightTheme` is the taskbar and Start. Write both, or Settings shows the mode as "Custom". Then broadcast `WM_SETTINGCHANGE` with "ImmersiveColorSet". Use `SendMessageTimeout` with `SMTO_ABORTIFHUNG` and a 1000 ms timeout, off the UI thread: a plain broadcast `SendMessage` waits on every hung window. Old Win32 apps that ignore the message switch on their next launch. Unlike macOS, no permission prompt is involved.
- **Keep a reference to `UISettings`.** A `UISettings` object created in a method and dropped is collected, and `ColorValuesChanged` stops firing. Hold it in a field. Its event fires on a background thread; go through the `DispatcherQueue` before touching the view model.
- **Power requests die on user sleep.** Lid close, the power button, and Start > Sleep end every request; Windows documents this. On Modern Standby laptops on battery, a system request also ends 5 minutes after the sleep timeout. Say both in Settings, not only the lid.
- **One handle, cleared before it is closed.** A leaked request handle keeps the PC awake after the tile says off. Clear both request types, close the handle, and do it on quit too. `SetThreadExecutionState` is the older way; it belongs to the calling thread, so a call from a pool thread holds or drops the state with that thread. `PowerCreateRequest` is per handle and shows a reason in `powercfg /requests`.
- **`powercfg /requests` needs an admin terminal.** The app runs without elevation, so self-tests check the handle count inside `KeepAwake`, and only the manual line uses powercfg.
- **Watching an elevated app can fail.** `Process.Exited` needs a handle to the process; for an app running as administrator, opening it can throw access denied. Then poll every 2 s for a process with the same pid, name, and start time, and stop when it is gone.
- **Setting the default sound device has no public API.** Windows ships no supported call for it. `IPolicyConfig` is the private COM interface that open-source sound switchers use. Keep it in `DefaultDeviceSetter`, catch every exception there, and always fall back to `ms-settings:sound`. Expect a Windows update to break it one day; the fallback keeps the app honest.
- **Use one kind of device id.** WinRT `DeviceInformation` ids wrap the MMDevice id in `\\?\SWD#MMDEVAPI#...#{interface guid}`. Mixing the two means the check mark never matches the default. Use MMDevice ids everywhere.
- **`IMMNotificationClient` calls come on a COM worker thread.** Do no work there and never wait on a lock inside the callback. Post to the `DispatcherQueue` and re-read the list from the UI side. Keep the client object in a field so it is not collected while registered.
- **The taskbar auto-hide state is the user's.** Set it only on a click, never on launch or quit. Read it back with `ABM_GETSTATE` after `ABM_SETSTATE` and show what Windows reports, not what you asked for.
- **Windows 11 hides new tray icons in the overflow.** The first launch shows a toast that says to pin Toolbox in Settings > Personalization > Taskbar > Other system tray icons. Re-add the icon when the registered `TaskbarCreated` message arrives, or it vanishes after Explorer restarts.
- **Snapshots of a toggle can lie.** A `ToggleSwitch` in an inactive window still draws its state, but the flyout closes when it loses focus, so a snapshot may catch it mid-close. Check toggle state with `[self]` lines that read the registry, not only snapshots.

Done when every tile in the tray flyout changes its Windows setting with no Explorer restart and shows the true state after a change made in Windows Settings, Keep awake holds exactly one power request while on and none while off, and the sound menu switches the default device or opens Sound settings when Windows refuses.

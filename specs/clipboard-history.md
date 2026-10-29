# Clipboard history

Keeps everything you copy (text, images, files) and pastes any of it back
with a shortcut and Enter. Replaces: Ditto, and Windows' own Win+V clipboard
history. Ported from BAMA's clipboard history spec.

## Brief

```markdown
# Clips

A clipboard history for one person: every copy is kept, searchable, and one keypress away from being pasted again.

## Replaces
Ditto and Windows' Win+V history, and copying the same thing twice.

## Main jobs
1. Open the history with a shortcut and paste an older item into the app I'm in.
2. Search the history by text, file name, or the app it came from.
3. Delete one item, or clear the whole history.

## Data
- ClipItem: id, kind (text, image, files), text (or file paths, one per line), image file name, date, app name.
  Clips.db (SQLite) in %LOCALAPPDATA%\Clips
- Images: PNG files in %LOCALAPPDATA%\Clips\clips, named by content hash

## Shape
Borderless, topmost panel opened with a global shortcut, a tray icon, and a Settings window.
Windows 11 first, Windows 10 19041 or later still runs. No taskbar button for the panel.
Local data. Works offline. No accounts.

## System hooks
Global shortcut (default Win+Alt+V), clipboard format listener, paste into the front app, tray icon.

## Reference
Ditto, Win+V

## Look
Native Fluent. One acrylic panel with rounded corners: the list on the left (320 epx),
a preview on the right, the accent color on the selected row.

## Not in version one
- Sync between PCs
- Pinned items and pinboards
- Rich text: text keeps only its plain string (no HTML or RTF)
- Editing an item before pasting
```

## How it works

| Part | API | Permission |
| --- | --- | --- |
| Watch the clipboard | `AddClipboardFormatListener` on the tray icon's window, `WM_CLIPBOARDUPDATE`, `GetClipboardSequenceNumber` | None |
| Read an item | `OpenClipboard`, `GetClipboardData` for `CF_HDROP` (`DragQueryFile`), `CF_UNICODETEXT`, the registered `PNG` format, `CF_DIBV5` or `CF_DIB`. Win32, not WinRT `Clipboard.GetContent`: WinRT gives no access to the `CanIncludeInClipboardHistory` DWORD or the `Clipboard Viewer Ignore` marker, and no control over which formats are rendered | None |
| Skip passwords | `RegisterClipboardFormat` and `IsClipboardFormatAvailable` for `ExcludeClipboardContentFromMonitorProcessing`, `CanIncludeInClipboardHistory`, `Clipboard Viewer Ignore` | None |
| Source app | `GetForegroundWindow`, `GetWindowThreadProcessId`, `QueryFullProcessImageName`, `FileVersionInfo.FileDescription` | None |
| Global shortcut | `RegisterHotKey` with `MOD_WIN \| MOD_ALT \| MOD_NOREPEAT` and `V` | None |
| Panel | WinUI `Window`, `OverlappedPresenter.SetBorderAndTitleBar(true, false)`, `IsAlwaysOnTop`, `AppWindow.IsShownInSwitchers = false`, `DesktopAcrylicBackdrop` | None |
| Paste into the front app | write the clipboard, `SetForegroundWindow` on the saved window, `SendInput` Ctrl+V | None (UIPI blocks apps running as administrator) |
| Is the target elevated | `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)`, `OpenProcessToken`, `GetTokenInformation(TokenElevation)` | None |
| Images | `BitmapDecoder`, `BitmapEncoder.PngEncoderId`; rows use `BitmapImage` with `DecodePixelType = Logical` | None |
| "Copied" message | `AppNotificationManager` toast | None |
| Tray icon | `Shell_NotifyIcon`, `TrackPopupMenuEx` | None |

Recipes: [global hotkey](../skills/code/os-integrations/recipes.md#global-hotkey-registerhotkey),
[clipboard listener](../skills/code/os-integrations/recipes.md#clipboard-listener),
[paste into the front app](../skills/code/os-integrations/recipes.md#paste-into-the-front-app),
[tray icon](../skills/code/os-integrations/recipes.md#tray-icon),
[toast notifications](../skills/code/os-integrations/recipes.md#toast-notifications),
[elevation and UIPI](../skills/code/os-integrations/recipes.md#elevation-and-uipi).

Types to create:

- `ClipItem` (Core, with `Preview` and `FilePaths`) and `ClipStore` (Core, SQLite: add, dedupe, trim, delete, clear).
- `ClipboardHistory` (`ObservableObject`): listens, reads, skips, and feeds `ClipStore`; `Copy`, `Delete`, `Clear`.
- `ClipboardReader` and `ClipboardWriter`: the Win32 calls, the own marker format, and the skip list. `ClipboardHistory` reads through an `IClipboardSource` (sequence number and read) and writes through an `IClipboardTarget`, so the self-test swaps in fakes and never touches the person's clipboard.
- `SyntheticInput` (Ctrl+V with a marker) and `Elevation` (is this window's process elevated).
- `ClipsPanel` (`ObservableObject`: search, selection, `Paste(item, pasteIntoApp)`), `ClipsWindow`, `ClipRow`, `ClipPreview`.
- `HotkeyCenter`, `KeyCombo`, `ShortcutRecorder`, `SettingsPage`, `TrayIcon`.
- Snapshot mode (`APP_SNAPSHOT`) also renders the panel with seeded clips as pages `clips-panel` and `clips-empty`.

Exact behavior:

- Listen with `AddClipboardFormatListener`. On `WM_CLIPBOARDUPDATE`, wait 100 ms, then read once. Return early when `GetClipboardSequenceNumber()` did not change; store the new number before any other check.
- Skip the copy when the formats include any of: the app's own format (`Clips.Internal`), `ExcludeClipboardContentFromMonitorProcessing`, `CanIncludeInClipboardHistory` holding a DWORD 0, `Clipboard Viewer Ignore`.
- Open with `OpenClipboard(trayHwnd)`. When it fails, retry up to 10 times, 50 ms apart. Close it in `finally`.
- Read in this order, first hit wins: `CF_HDROP` (files), `CF_UNICODETEXT`, `PNG`, `CF_DIBV5` or `CF_DIB` (convert to PNG). Skip text that is only whitespace and go on to the image.
- Image file name: the first 12 bytes of the SHA256 of the PNG, as hex, plus `.png`. Write only when the file is missing. Store PNG data as it came.
- Add: write the PNG first, then the row. Remove any item with the same kind, text, and image file, then insert at the top. The same copy twice moves to the top. Load in insert order (`ORDER BY rowid DESC`): ordering by date loses the order of two copies in the same tick.
- Limit: default 200, choices 50, 100, 200, 500, 1000, never under 10. Trimmed items delete their PNG when no other item uses it.
- Save: write each change to Clips.db at once (SQLite with WAL). Load once at start.
- Search: case-insensitive `Contains(query, StringComparison.CurrentCultureIgnoreCase)` on the preview or the app name. Preview: trimmed text, "Image", or file names joined with ", ".
- Copy back: `OpenClipboard(ownerHwnd)`, `EmptyClipboard`, then text as `CF_UNICODETEXT`, an image as `PNG` and `CF_DIB`, files as `CF_HDROP` (a `DROPFILES` header plus the paths). The listener then sees the same content and moves it to the top. That is the behavior you want.
- Paste: copy back, hide the panel, `SetForegroundWindow` on the window saved when the panel opened, wait until Alt, Shift, and Win are up (at most 500 ms), then send Ctrl+V 60 ms later. When the target is elevated, skip the keys: the item stays on the clipboard and a toast says "Copied. Press Ctrl+V: <app> runs as administrator."
- Keys: Up, Down, Enter pastes, Ctrl+Enter copies only, Ctrl+Delete deletes, Ctrl+1 to 9 pastes that row, Esc closes. Double-click pastes. The panel closes when it loses activation.
- Panel: 780 by 480 epx, top 16% below the top of the work area of the monitor with the pointer. Rows 40 epx. Image rows also show the relative time, since every image reads "Image".
- Preview: text in Cascadia Mono, selectable (`IsTextSelectionEnabled`) in a `ScrollViewer`; an image scaled to fit; files with icon, name, and folder. Footer: app name, relative date, and the Enter, Ctrl+Enter, Ctrl+Delete hints.

Sending Ctrl+V. Mark each event so your own hooks can skip it. `INPUT` is the
full union from `UI/Win32.cs`:

```csharp
public const nuint Marker = 0x434C4950;   // "CLIP"

public static void Paste()
{
    (ushort Key, bool Up)[] keys = [(VK_CONTROL, false), (VK_V, false), (VK_V, true), (VK_CONTROL, true)];
    var inputs = keys.Select(k => new INPUT
    {
        type = INPUT_KEYBOARD,
        ki = new KEYBDINPUT { wVk = k.Key, dwFlags = k.Up ? KEYEVENTF_KEYUP : 0, dwExtraInfo = Marker },
    }).ToArray();
    SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());   // one call: not interleaved
}
```

## Stories

```markdown
# Stories

## M1 Core loop

### S-001 Keep every copy
As the user, I copy text in any app and it is saved to the history.
Acceptance:
- [self] With a fake `IClipboardSource` holding text and a new sequence number, posting `WM_CLIPBOARDUPDATE` to the listener window runs the real message path (the 100 ms wait, the sequence check) and the text shows up as the first item within 1 s. The self-test never writes or reads the real clipboard: a write lands in Win+V history and in any clipboard manager, and a restore cannot be exact.
- [manual] Text copied in Notepad shows up as the first item within 1 s.
- [unit] Copying the same text twice keeps one item, at the top.
- [unit] With a limit of 50, the 51st copy drops the oldest item.
- [self] The history survives a restart (Clips.db).
Status: todo

### S-002 Open the history with the shortcut
As the user, I press Win+Alt+V and the clipboard panel appears.
Screens: clips-panel, clips-empty
Depends: S-001
Acceptance:
- [snap] List on the left, preview on the right, search box focused.
- [snap] An empty history shows "Nothing here yet. Copy something and it shows up here."
- [manual] Esc or a click outside closes the panel, and focus goes back to the app I was in.
Status: todo

### S-003 Know when paste cannot reach the app
As the user, when the app in front runs as administrator, Clips tells me to paste myself.
Screens: settings-general
Acceptance:
- [manual] With an elevated Terminal in front, Enter copies the item and a toast says "Copied. Press Ctrl+V: Terminal runs as administrator."
- [snap] Settings says "Clips cannot paste into apps running as administrator. The item is copied, so press Ctrl+V."
Status: todo

### S-004 Paste an item into the app I was using
As the user, I pick an item with the arrow keys and press Enter; it is pasted where my cursor was.
Depends: S-002, S-003
Acceptance:
- [self] Enter pastes into a test window of the app's own through a fake `IClipboardTarget`. The test window's `TextBox.Paste` handler sets `Handled = true`, so it proves the Ctrl+V arrived without reading the clipboard. The item moves to the top and keeps its app name. The panel's hide on deactivate is off while the test runs, and the test checks `GetForegroundWindow()` before every step and retries: parallel test runs share one foreground.
- [manual] Enter in Notepad pastes the item; the panel is gone before the paste lands.
- [manual] The same works in a browser text field and in Word.
- [manual] The pasted item moves to the top of the history.
Status: todo

### S-005 Search the history
As the user, I type in the search box and the list narrows.
Screens: clips-search
Depends: S-002
Acceptance:
- [unit] A query matches the preview text or the app name, ignoring case.
- [snap] No hits shows "No matches. Try another word."
- [manual] The selection resets to the first row on every change.
Status: todo

### S-006 Keep images and files
As the user, I take a screenshot with Win+Shift+S or copy a file in File Explorer and it shows up with the right preview.
Screens: clips-image, clips-files
Depends: S-001
Acceptance:
- [snap] An image row shows a 24 epx thumbnail and its relative time; the preview shows the image.
- [snap] A files row shows the file names; the preview lists icon, name, and folder per file.
- [unit] The same image twice is stored as one PNG file.
- [manual] A range of Excel cells is saved as text, not as a picture.
Status: todo

### S-007 Never save passwords
As the user, a password copied from my password manager is not saved.
Depends: S-001
Acceptance:
- [unit] A clipboard with ExcludeClipboardContentFromMonitorProcessing, CanIncludeInClipboardHistory set to 0, or Clipboard Viewer Ignore is skipped.
- [self] A fake source with each of those formats, fed through the listener window, adds nothing.
- [manual] Copying a password in KeePass adds nothing to the list.
Status: todo

### S-008 Use the tray icon
As the user, I right-click the tray icon and see Open Clips (with its shortcut), Settings, and Quit.
Acceptance:
- [unit] The menu model shows the current shortcut next to "Open Clips", Windows key first: "Win+Alt+V", not "Alt+Win+V".
- [manual] No taskbar button while only the tray icon runs; closing Settings keeps the app running.
Status: todo

## M2 Speed and settings

### S-009 Copy only, paste by number, or delete
As the user, Ctrl+Enter only copies, Ctrl+3 pastes the third row, Ctrl+Delete deletes the selected item.
Depends: S-004
Acceptance:
- [manual] Ctrl+Enter closes the panel and shows a short "Copied" toast.
- [snap] The first nine rows show Ctrl+1 to 9 key caps.
- [unit] Deleting the last item that uses an image removes its PNG file.
Status: todo

### S-010 Change the shortcut
As the user, I record a new shortcut in Settings.
Screens: settings-general
Acceptance:
- [manual] Shortcuts pause while recording. Esc cancels, Backspace clears.
- [manual] When another app owns the combo, an orange warning shows next to it.
Status: todo

### S-011 Choose how much to keep, or clear it all
As the user, I turn history off, pick a limit, or clear the history.
Screens: settings-history
Acceptance:
- [manual] With "Keep clipboard history" off, new copies are not saved.
- [manual] "Clear history" asks for confirmation and shows the item count.
- [snap] The footer says passwords from password managers are never saved.
Status: todo

## Dropped
- Pause while recording the screen.
- Activity stats, and per-feature motion speed for the selection animation.
```

## Gotchas

- Password managers mark their copies. KeePass sets `ExcludeClipboardContentFromMonitorProcessing`, `Clipboard Viewer Ignore`, and `CanIncludeInClipboardHistory` to 0. Without the skip list, passwords end up in a plain SQLite file.
- Your own helper writes (a snippet expansion, a one-off paste) must carry the app's own format and `ExcludeClipboardContentFromMonitorProcessing`, or they flood the history of Clips, Ditto, and Win+V. Copying an item back is not marked on purpose, so it moves to the top.
- `OpenClipboard` fails while another process has the clipboard open. Retry 10 times, 50 ms apart, and always `CloseClipboard` in `finally`. While you hold it open, no app on the PC can copy or paste.
- `SetClipboardData` fails after `OpenClipboard(0)` plus `EmptyClipboard`. Open with an owner window (the tray and listener window, or a message-only window) for every write. WinRT `Clipboard.SetContent` hides this.
- One copy can fire `WM_CLIPBOARDUPDATE` more than once, because some apps write their formats in steps. Wait 100 ms and compare `GetClipboardSequenceNumber` before reading.
- Read only the formats you use. A delayed-rendered format makes the owning app render it when you ask, and a busy owner (Excel with a big range) blocks you while it does.
- Read order matters, and it differs from macOS. Excel and Word put text and a picture of the selection on the clipboard. Read `CF_HDROP` first, then text, then the image, or a range of cells shows up as a picture.
- `CF_DIB` has no file header, and a 32-bit `BI_RGB` DIB often has an alpha byte of 0, so the screenshot saves as a fully transparent PNG. Prefer the `PNG` format when it is there. Otherwise add a `BITMAPFILEHEADER` and decode with `BitmapAlphaMode.Ignore`.
- After `SetClipboardData` succeeds, the system owns the `HGLOBAL`. Never free it; free it only when the call fails.
- `SendInput` with an `INPUT` struct that holds only `KEYBDINPUT` sends nothing and returns 0. Declare the full union, so `Marshal.SizeOf<INPUT>()` is 40 on 64-bit Windows.
- A window with `WS_EX_NOACTIVATE` cannot take typing, so the panel activates. Save the foreground window when it opens, give focus back with `SetForegroundWindow` before the paste, and send Ctrl+V 60 ms later so focus lands first.
- Windows still holds the shortcut's keys when Enter comes fast. Wait until Alt and Win are up, or Ctrl+V arrives as Ctrl+Alt+V or Win+Ctrl+V.
- `SendInput` into an app running as administrator is blocked by UIPI, and neither the return value nor `GetLastError` says so. Check the target's elevation first. When `OpenProcessToken` fails with access denied, treat the target as elevated too.
- Ctrl+Alt+letter fires on AltGr, which types characters on many layouts (AltGr+C is ć on Polish). The default is Win+Alt+V, which also leaves Win+V to Windows.
- Rows are 40 epx, so never load the full PNG for a row. Use a `BitmapImage` with `DecodePixelType = Logical` and `DecodePixelWidth = 24`; XAML decodes it small, off the UI thread.
- The first open built the panel and its XAML tree and came up late. Build it 2 s after launch, show it off screen with `AppWindow.Show(false)`, let it draw one frame, and hide it. Focus the search box in the `Activated` handler, again on `Loaded`, and once more at low dispatcher priority: on the first open `Activated` can come before `Loaded`, and focus set while the content is not loaded does nothing.
- An `AutoSuggestBox` raises `TextChanged` for a change made in code, later, on its own, and it can reset the selection after your code moved it. Refresh only when `args.Reason == AutoSuggestionBoxTextChangeReason.UserInput`, and refresh directly when code sets the text.
- Write shortcuts with the Windows key first, as Windows does: "Win+Alt+V", "Win+Shift+S". A `KeyCombo.ToString()` that lists modifiers in flag order reads "Alt+Win+V".
- Explorer restarts drop the tray icon. Re-add it on the `TaskbarCreated` message. A message-only window never gets that broadcast, so the tray and listener window is a hidden normal window.
- Closing the Settings window must not end the app. Cancel `AppWindow.Closing` and call `AppWindow.Hide()`.
- Changed from BAMA: the 0.5 s poll is now an event (`WM_CLIPBOARDUPDATE`) plus a 100 ms wait, since Windows tells you about every change. The 1 s save delay is gone, since SQLite writes one row cheaply. Rows are 40 epx, not 38, to sit on BAWA's 4 epx grid. The 60 pt shadow margin is gone, because DWM draws the shadow outside the window. There is no permission to grant, so the Accessibility story became the elevated-app story.

Done when copying text, a Win+Shift+S screenshot, and a File Explorer file each adds one item, Win+Alt+V then Enter pastes the chosen item into Notepad, a KeePass password copy never appears, and the history survives a restart.

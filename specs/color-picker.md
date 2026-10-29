# Color picker

Pick a color anywhere on screen with a shortcut, copy it as hex, and keep
colors in named collections. Replaces: PowerToys Color Picker, Just Color Picker.
Ported from BAMA's color picker spec.

## Brief

```markdown
# Pick

A tray color picker for designers and developers who grab colors from the screen all day.

## Replaces
PowerToys Color Picker, Just Color Picker, zooming into a screenshot in Paint

## Main jobs
1. Pick a color from anywhere on screen and copy it in my format.
2. See recent colors in the tray flyout and copy one again with a click.
3. Keep colors in named collections and add to them by picking or typing hex.

## Data
- Color: hex string, "#RRGGBB" in uppercase
- ColorCollection: id, name, position, colors (hex strings, no duplicates)
- Recent: up to 12 hex strings, newest first
- Settings: picker shortcut, copy format
- SQLite at %LOCALAPPDATA%\Pick\Pick.db, preferences in settings.json next to it (a JSON file
  written to a temp file and then moved is enough for 12 recent colors and a few collections)

## Shape
Tray icon with a flyout (a borderless WinUI window above the icon) plus a Settings window.
Windows 11 first, Windows 10 19041 or later runs. Unpackaged .exe. Local data. Works offline. No accounts.

## System hooks
Global shortcut, tray icon, single instance. No permission needed.

## Reference
PowerToys Color Picker

## Look
Native WinUI, system accent color. Acrylic flyout, Mica Settings window.
Swatches are rounded squares (4 epx corners) with a hairline edge.

## Not in version one
- Contrast checker and color harmonies
- Export to ASE, CSS, or Tailwind files
- Sync between PCs
```

## How it works

| Part | API | Permission |
| --- | --- | --- |
| Shortcut | `RegisterHotKey` on a message window, `WM_HOTKEY` ([recipe](../skills/code/os-integrations/recipes.md#global-hotkey-registerhotkey)) | None |
| Tray icon, no taskbar button | `Shell_NotifyIcon` with `NOTIFYICON_VERSION_4` ([recipe](../skills/code/os-integrations/recipes.md#tray-icon)) | None |
| Flyout | WinUI `Window`, `OverlappedPresenter.SetBorderAndTitleBar(false, false)`, `IsAlwaysOnTop`, `AppWindow.IsShownInSwitchers = false`, placed with `Shell_NotifyIconGetRect` ([recipe](../skills/code/os-integrations/recipes.md#move-a-window)) | None |
| Freeze the screen | GDI `BitBlt` from `GetDC(0)` into a top-down 32-bit `CreateDIBSection`, over the whole virtual screen | None |
| Magnifier and pick | One topmost window per display, painted from the frozen bitmap: plain Win32 with `BitBlt` and `StretchBlt` (`COLORONCOLOR`), or a WinUI window made truly borderless (see [screenshots.md](screenshots.md) Gotchas) with the loupe zoomed byte by byte | None |
| Copy | `Windows.ApplicationModel.DataTransfer.Clipboard.SetContent`, then `Clipboard.Flush()`, behind one `ClipboardWriter` that a test run swaps for a fake | None |
| Toast | Small topmost window with `WS_EX_NOACTIVATE`, `WS_EX_TRANSPARENT`, `WS_EX_TOOLWINDOW` ([recipe](../skills/code/os-integrations/recipes.md#non-activating-panel)) | None |
| One copy running | `AppInstance.FindOrRegisterForKey` ([recipe](../skills/code/os-integrations/recipes.md#single-instance)) | None |
| Save | SQLite through Microsoft.Data.Sqlite in `Pick.Core` | None |

GDI reads the composed desktop with no prompt and no yellow capture border, so Pick never asks
for anything. The app is DPI aware per monitor (PerMonitorV2, already in the template's
`app.manifest`), so every coordinate below is in physical pixels.

Types: `HexColor` (pure functions in `Pick.Core`: bytes to hex, parse, normalize, format; unit
tested), `Loupe` (`Pick.Core`: the zoom, byte by byte; unit tested), `ColorStore` (`Pick.Core`,
SQLite: `collections`, `colors`, `recent`; saves on every change), `ScreenFreeze` (`Capture(box)`
for any rect, the frozen bitmap and its origin), `PickerOverlay` (one window per display, the
loupe, keys), `ColorPicker` (hides the flyout, freezes, shows the overlays, copies, adds to
Recent; it takes the areas to cover as a parameter and has a switch that ignores real pointer
and key input, so a self-test drives `Move`, `Press`, `Release`, and `OnKey` itself and never
calls `SetCursorPos`), `SwatchGrid` (click copies, right-click shows formats and
Remove), `TrayFlyout`, `SettingsPage` and `CollectionsPage` in the main window, plus
`HotkeyCenter`, `ShortcutBox`, `Toast`, `TrayIcon`.

Behavior that matters:

- Default shortcut: Ctrl+Alt+Shift+C.
- Tray icon: left-click (`NIN_SELECT`) opens the flyout, right-click (`WM_CONTEXTMENU`) shows
  Pick a color, Settings, Quit. Closing the Settings window hides it; Quit exits.
- Picking: freeze every display at once, show one overlay per display with the frozen picture,
  and a loupe next to the pointer: 15 x 15 pixels at 8x (120 epx square, scaled by the display's
  DPI), a center square outline, and the hex under it. A click picks, on the button release:
  picking on the press hides the overlay and the release lands on the app below. Arrow keys move
  the pointer one physical pixel (`SetCursorPos`), Enter picks. Escape or right-click cancels and
  changes nothing.
- After a pick: add to Recent, copy in the chosen format, show a toast "Copied #6342E6" with a
  swatch, bottom center of the display the pick came from, for 1.5 s.
- Recent: remove the same hex first, insert at the front, keep 12.
- Copy formats, default `hex`: `hex` gives `#6342E6`, `hexBare` gives `6342E6`, `rgb` gives
  `rgb(99, 66, 230)`, `csharp` gives `Color.FromArgb(255, 99, 66, 230)`. Settings names each one
  with its example: "Hex (#6342E6)", "CSS (rgb(99, 66, 230))".
- Right-click on any swatch lists every format as "Copy <value>", plus Remove inside a collection.
- Typed hex: accept `5b47e0`, `#5B47E0`, `#abc`, and spaces around it. Store as `#5B47E0`.
  Reject anything else: the field stays, and the text under it says "Use 3 or 6 hex digits, like #5B47E0".
- A collection never holds the same hex twice. New collections are named "New collection".
- First launch seeds one collection, "Brand", with a few sample colors, so the flyout is not empty.
- Picking into a collection (eyedropper button on its row) adds the color there and to Recent.
- Every change is one SQLite transaction. Collection order is a `position` column.

Read the pixel from the frozen bitmap. The virtual screen can start left of or above 0,0, and
GDI stores each pixel as blue, green, red, alpha:

```csharp
public static class HexColor
{
    // screenX and screenY are physical pixels; left and top come from SM_XVIRTUALSCREEN and SM_YVIRTUALSCREEN.
    public static string FromFrozen(ReadOnlySpan<byte> bgra, int stride, int left, int top, int screenX, int screenY)
    {
        var i = (screenY - top) * stride + (screenX - left) * 4;
        return FromRgb(bgra[i + 2], bgra[i + 1], bgra[i]);
    }

    public static string FromRgb(byte r, byte g, byte b) => $"#{r:X2}{g:X2}{b:X2}";
}
```

## Stories

```markdown
# Stories

## M1 Core loop

### S-001 Pick a color with the shortcut
As the user, I press Ctrl+Alt+Shift+C, click anywhere, and the color is copied.
Screens: picker, toast
Acceptance:
- [self] A pick on the test window goes to the front of Recent and is copied in each format through the `ClipboardWriter` fake; Escape and right-click copy nothing and add nothing. The real clipboard is never written or read.
- [manual] The loupe appears next to the pointer on every display; clicking copies `#RRGGBB` and a toast shows the value and a swatch.
- [manual] No permission prompt and no yellow capture border appear, on first launch or later.
- [manual] Escape copies nothing, adds nothing to Recent, and the app I was in gets focus back.
Status: todo

### S-002 Read the exact pixel
As the user, I get the color of the pixel under the pointer, on any display, at any scale.
Acceptance:
- [unit] `HexColor.FromFrozen` reads blue, green, red in that order and gives `#6342E6` for bytes E6 42 63 FF.
- [unit] With the virtual screen starting at (-1920, -200), screen point (-1920, -200) reads bitmap pixel (0, 0).
- [self] A borderless test window of four known colors (no two channels equal), topmost near the bottom-left corner: freezing only its rect, the hex at each color's center matches both `GetPixel(GetDC(0), x, y)` at the same physical point and the known color. The self-test never freezes the full screen.
- [self] The picker, given only the test window's area, shows the frozen picture pixel for pixel; the loupe's center block is the pixel under the pointer; Right moves one physical pixel and Enter picks it.
- [manual] On each display, at each scale, the hex under the pointer matches the screen.
Status: todo

### S-003 See recent colors in the tray flyout
As the user, I click the tray icon and see my last 12 picks, newest first.
Screens: flyout, flyout-empty
Depends: S-001
Acceptance:
- [snap] The flyout shows a Pick button, a Recent row of swatches, and the collections below.
- [unit] Picking a color already in Recent moves it to the front; the list never passes 12.
- [snap] With nothing saved, the flyout says "Pick a color to start a collection."
Status: todo

### S-004 Copy a swatch again
As the user, I click a swatch to copy it, or right-click it to copy another format.
Depends: S-003
Acceptance:
- [manual] Click copies in the chosen format and shows the toast.
- [snap] Right-click lists "Copy #6342E6", "Copy 6342E6", "Copy rgb(99, 66, 230)", and "Copy Color.FromArgb(255, 99, 66, 230)".
- [snap] Hovering a swatch shows its hex as a tooltip.
Status: todo

### S-005 Pick from the flyout
As the user, I click Pick in the tray flyout and pick a color.
Depends: S-001
Acceptance:
- [self] The flyout is hidden before the freeze, so it is never in the loupe: after the hide, the picker waits 250 ms when the flyout was open, then calls `DwmFlush`.
- [manual] The loupe opens within a quarter second of the click.
Status: todo

### S-006 Choose the copy format and shortcut
As the user, I pick a copy format and change the picker shortcut in Settings.
Screens: settings-picker
Acceptance:
- [unit] Each format gives the exact text above for `#6342E6`, with or without `#`, in any case.
- [manual] Click the shortcut box and press a combo; Escape cancels, Delete clears; the old shortcut pauses while recording.
- [snap] A combo another app owns shows "In use by another app" under the box.
Status: todo

### S-007 Keep colors after a restart
As the user, I quit and reopen Pick and my Recent and collections are still there.
Acceptance:
- [unit] `ColorStore` round-trips through SQLite with names, order, and colors intact.
- [unit] A missing database loads the seeded "Brand" collection; a broken file does not crash, it is renamed `Pick.db.broken` and a fresh one starts.
Status: todo

## M2 Collections

### S-008 Create and rename a collection
As the user, I click New collection and type its name in the header.
Screens: settings-collections
Acceptance:
- [snap] A new, empty collection says "No colors yet. Pick one from the screen or type a hex code."
- [manual] The name saves as I type.
Status: todo

### S-009 Add a color by picking
As the user, I click the eyedropper on a collection and pick a color into it.
Depends: S-008
Acceptance:
- [unit] Adding a hex the collection already has changes nothing.
- [manual] The color lands in that collection and in Recent.
Status: todo

### S-010 Add a color by typing hex
As the user, I type a hex code in a collection's field and press Enter or Add.
Depends: S-008
Acceptance:
- [unit] `5b47e0`, `#5B47E0`, and ` #abc ` become `#5B47E0` and `#AABBCC`; `#12345`, `zzzzzz`, `#ab` are rejected.
- [snap] A rejected code stays in the field with "Use 3 or 6 hex digits, like #5B47E0" under it.
Status: todo

### S-011 Remove a color or delete a collection
As the user, I remove a swatch with right-click, or delete a whole collection with its delete button.
Depends: S-008
Acceptance:
- [snap] After delete, an InfoBar says "Deleted "<name>"." with an Undo button.
- [manual] Undo (or Ctrl+Z) puts the collection back at the same position.
- [manual] Clear on Recent also offers Undo.
Status: todo

## Dropped
- Usage stats and the activity log
- Debug bridge color commands ([recipe](../skills/code/os-integrations/recipes.md#debug-bridge), later)
```

## Gotchas

- Windows has no system loupe like the macOS color sampler. Build it from a frozen GDI copy of
  the screen. One `BitBlt` of the virtual screen reads every display, needs no permission, shows
  no yellow border, and the click lands on your overlay, never on the app below.
- Freeze first, then show the overlays. The overlays can then never be in their own picture.
  Hide the flyout before the freeze, wait out its hide animation (250 ms when it was open), and
  call `DwmFlush()`, so the desktop is redrawn without it. A hidden window can still be on screen
  while its hide animation plays, and one `DwmFlush` is not enough on its own.
- Use one overlay window per display, not one window across the whole virtual screen. A window
  that spans displays with different scales gets one DPI, and the loupe is the wrong size on the
  others. Place each one with `SetWindowPos` in physical pixels from `EnumDisplayMonitors`.
- Virtual screen coordinates go negative when a display sits left of or above the main one.
  Subtract `SM_XVIRTUALSCREEN` and `SM_YVIRTUALSCREEN` before indexing the bitmap, or the left
  display reads the wrong pixels or throws.
- Pass a negative `biHeight` to `CreateDIBSection`. A positive height gives a bottom-up bitmap,
  and every pick reads the row mirrored from the bottom.
- GDI stores pixels as blue, green, red, alpha. Reading them as RGB swaps red and blue, and a
  gray test color hides the bug. Test with a color like `#6342E6`.
- Zoom the loupe with `SetStretchBltMode(COLORONCOLOR)`. The default `HALFTONE` blends pixels,
  so the center square shows a color that is not on screen. XAML has no nearest-neighbor scaling
  for an `Image`, so a WinUI loupe zooms the bytes itself (each pixel as an 8 x 8 block) and is
  drawn at 1:1 physical pixels, with its position rounded to whole physical pixels.
- Keys in a full-screen overlay need a focused control. A `Grid` cannot take focus, so its
  `KeyDown` never fires. Use a `UserControl` with `IsTabStop = true`, focus it with
  `Focus(FocusState.Programmatic)` after `Activate`, and set `UseSystemFocusVisuals = false`.
  `ProtectedCursor` is protected, so a crosshair cursor needs that subclass too.
- A WinUI overlay keeps a 3 px frame, rounded corners, a gray outline, and a fade-in unless you
  remove them: the same fixes as the screenshot picker (see [screenshots.md](screenshots.md)).
- Build every format from the parsed components, never by slicing the string. A stored value
  without `#` turns `"6342E6"` into `"342E6"` when you cut one character off the front.
- Call `Clipboard.Flush()` after `SetContent`. Without it the copied text belongs to Pick and
  disappears when Pick quits.
- `Clipboard.SetContent` throws when another app holds the clipboard open (clipboard managers,
  Remote Desktop). Retry three times, 50 ms apart, before showing "Couldn't copy".
- Ctrl+Alt is AltGr on many keyboard layouts (Polish, German), so Ctrl+Alt+C can type a letter
  there. The default adds Shift. Win key combos are risky too: Windows keeps taking new ones.
- `RegisterHotKey` fails with `ERROR_HOTKEY_ALREADY_REGISTERED` when another app owns the combo,
  for example PowerToys Color Picker on Win+Shift+C. Show "In use by another app" instead of failing silently.
- Unregister all shortcuts while a shortcut box records, or pressing the current combo fires
  the picker instead of being recorded.
- Deleting a collection must be undoable. Keep the removed collection and its position, show
  Undo in an InfoBar, and bind Ctrl+Z to it.
- Renaming a collection saves on every key. Rebuilding the list on that change recreates the
  `TextBox` and drops focus after one letter: skip the page refresh while renaming.
- WinUI has no `WrapPanel` without another toolkit package. `VariableSizedWrapGrid` with a fixed
  item size wraps swatches well enough.
- A `MenuFlyoutItem` can be in one menu only. Build the format items for each menu, or moving
  them from another `MenuFlyout` throws.
- A tray app must not quit when its last window closes. Cancel `AppWindow.Closing` and hide the
  window; only Quit in the tray menu exits.
- Keep `HexColor` and `ColorStore` in `Pick.Core` with no WinUI references, so the xUnit project
  tests them without starting the app.

Done when the shortcut copies any on-screen color as hex on every display with no prompt and no
capture border, Recent shows the last 12 picks in the tray flyout, and collections survive a restart.

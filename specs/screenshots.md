# Screenshots

Capture the full screen, an area, or one window with a shortcut, then copy, save, drag
out, or mark it up with arrows, boxes, text, and blur. Replaces: Snipping Tool, ShareX,
Greenshot. Ported from BAMA's screenshots spec.

## Brief

```markdown
# Shot

A tray screenshot tool for people who send a lot of marked-up screenshots.

## Replaces
Snipping Tool, ShareX, Greenshot, PrtScn plus Paint for markup

## Main jobs
1. Capture the screen, an area, or a window with a shortcut.
2. Copy, save, or drag the capture from a card in the corner.
3. Mark up a capture with arrows, boxes, text, and blur, then copy or save it.

## Data
- Capture: image (physical pixels), display scale, saved file path (optional)
- Annotation: tool, start and end (image pixels), color, size, text, blur style and strength
- Settings in settings.json under %LOCALAPPDATA%\Shot\: shortcuts, Print screen key, copy, save, card, sound, save folder

## Shape
Tray icon plus overlays (picker, corner card) and one editor window.
Windows 11 first, Windows 10 19041 or later runs. Unpackaged .exe. Local data. Works offline. No accounts.

## System hooks
Global shortcuts, tray icon, single instance. Window captures ask once to hide the capture border.

## Reference
Snipping Tool

## Look
Native WinUI, system accent color. Acrylic corner card with dark, readable buttons. Mica editor window.

## Not in version one
- Scrolling capture, timed capture, cloud upload
- OCR copy text, ellipse, step counter, highlighter, resize handles
```

## How it works

| Part | API | Permission |
| --- | --- | --- |
| Shortcuts | `RegisterHotKey`, `WM_HOTKEY` ([recipe](../skills/code/os-integrations/recipes.md#global-hotkey-registerhotkey)) | None |
| Tray icon, no taskbar button | `Shell_NotifyIcon`, `NOTIFYICON_VERSION_4` ([recipe](../skills/code/os-integrations/recipes.md#tray-icon)) | None |
| Capture a display or an area | GDI `BitBlt` from `GetDC(0)` over the virtual screen into a top-down `CreateDIBSection` ([recipe](../skills/code/os-integrations/recipes.md#screen-capture)) | None |
| Capture a window | `GraphicsCaptureItem.TryCreateFromWindowId` (or `IGraphicsCaptureItemInterop.CreateForWindow` on Windows 10), `Direct3D11CaptureFramePool.CreateFreeThreaded`, one frame, `SoftwareBitmap.CreateCopyFromSurfaceAsync` | Capture border unless the user allows borderless |
| Hide the border | `GraphicsCaptureAccess.RequestAccessAsync(GraphicsCaptureAccessKind.Borderless)`, then `session.IsBorderRequired = false` (Windows 11) | One prompt |
| Window under the pointer | `EnumWindows` (top of z-order first), `IsWindowVisible`, `DwmGetWindowAttribute` with `DWMWA_CLOAKED` and `DWMWA_EXTENDED_FRAME_BOUNDS` | None |
| Picker | One plain Win32 topmost window per display, painted from the frozen bitmap | None |
| Corner card | WinUI window, `WS_EX_NOACTIVATE`, `WS_EX_TOOLWINDOW`, shown with `SW_SHOWNOACTIVATE` ([recipe](../skills/code/os-integrations/recipes.md#non-activating-panel)) | None |
| Copy | `DataPackage.SetBitmap` plus a `"PNG"` format, `Clipboard.SetContent`, `Clipboard.Flush()` | None |
| Drag out | `UIElement.CanDrag`, `DragStarting`, `DataPackage.SetStorageItems` | None |
| Editor canvas, blur | Win2D `CanvasControl`, `CanvasRenderTarget` for export, `GaussianBlurEffect` | None |
| Shutter sound | `PlaySound` with `SND_FILENAME` and `SND_ASYNC`, a bundled WAV | None |
| One copy running | `AppInstance.FindOrRegisterForKey` ([recipe](../skills/code/os-integrations/recipes.md#single-instance)) | None |

Windows has no Screen Recording permission. GDI reads the composed desktop with no prompt, so
display and area captures need nothing. Window captures use Windows.Graphics.Capture, because it
reads a window even when other windows cover it. That API draws a yellow border around the
window while it captures, unless the user allowed borderless capture. The app is PerMonitorV2
DPI aware (template `app.manifest`), so all capture math is in physical pixels.

Types: `CaptureService` (freeze, crop, window frame), `ScreenFreeze` (bitmap plus virtual
screen origin), `SelectionOverlay` (one window per display), `WindowFinder`, `CaptureCard`,
`AnnotationDocument` (`Shot.Core`: shapes, undo, hit testing), `AnnotationRenderer` (one shape
into a `CanvasDrawingSession`, for canvas and export), `Pixelate` (`Shot.Core`, block average on
BGRA bytes), `AnnotationEditor` (window, keys), `CaptureAccess`, `HotkeyCenter`, `ShortcutBox`,
`SettingsPage`.

Behavior that matters:

- Shortcuts: Ctrl+Alt+Shift+3 screen (the display under the pointer), +4 area, +5 window.
- Optional: "Use the Print screen key" in Settings makes PrtScn start an area capture. It works
  only when Windows' own "Use the Print screen key to open screen capture" is off. Settings reads
  `HKCU\Control Panel\Keyboard\PrintScreenKeyForSnippingEnabled`, says which app gets the key,
  and has a button that opens `ms-settings:easeofaccess-keyboard`.
- Area and window freeze all displays at once, then open the picker on every display. Drag for an
  area, click for a window, Space switches mode, Escape or right-click cancels. A drag under
  4 x 4 epx is a click.
- The size label shows physical pixels. An area that crosses two displays is cropped from the
  one frozen bitmap, so it works across displays with different scales.
- Crop an area from the frozen bitmap at `(x - virtualLeft, y - virtualTop)`. Window captures
  are cropped to `DWMWA_EXTENDED_FRAME_BOUNDS`, so they have no invisible border and no shadow.
- After a capture: copy (on), save (off), card (on), sound (on). Folder: the Screenshots known
  folder (`SHGetKnownFolderPath` with `FOLDERID_Screenshots`, usually `Pictures\Screenshots`).
  File name: `Shot yyyy-MM-dd 'at' HH.mm.ss.png`.
- Card: 252 epx wide, 16:9 picture, 6 epx inset, 18 epx from the bottom-left of the work area
  (above the taskbar). Hides after 8 s unless hovered. Hover: Copy, Save, Annotate, Close.
  Drag out the saved file or a temp PNG.
- Editor: fits the image in 85% of the work area, min 1000 x 320 epx. Shapes use image pixels,
  top-left origin. Sizes in epx times the capture scale (display DPI / 96): arrow 3, 5, 8;
  box 2, 3.5, 6; text 14, 20, 30.
- Palette: red `#FF3B30` (default), orange, yellow, green, blue, violet, white, black.
- Keys: `V` select, `A` arrow, `R` box, `T` text, `B` blur, `1` to `8` color, `[` `]` size,
  Ctrl+Z, Ctrl+Y and Ctrl+Shift+Z redo, Ctrl+C copy, Ctrl+S save, Delete removes the selection.
- Blur: pixelate at 70% by default. Strongest block `max(longSide / 60, 24 * scale)` pixels,
  strongest radius `max(longSide / 50, 26 * scale)`. Undo keeps 200 steps of shape lists.

Freeze every display in one call. The virtual screen origin can be negative:

```csharp
int left = GetSystemMetrics(SM_XVIRTUALSCREEN), top = GetSystemMetrics(SM_YVIRTUALSCREEN);
int width = GetSystemMetrics(SM_CXVIRTUALSCREEN), height = GetSystemMetrics(SM_CYVIRTUALSCREEN);
var screen = GetDC(0);
var memory = CreateCompatibleDC(screen);
var info = new BITMAPINFOHEADER { biSize = 40, biWidth = width, biHeight = -height, biPlanes = 1, biBitCount = 32 };
var bitmap = CreateDIBSection(screen, ref info, 0, out var bits, 0, 0);   // negative height: top-down rows
var old = SelectObject(memory, bitmap);
BitBlt(memory, 0, 0, width, height, screen, left, top, SRCCOPY | CAPTUREBLT);  // CAPTUREBLT keeps menus and tooltips
SelectObject(memory, old);
DeleteDC(memory);
ReleaseDC(0, screen);
// bits now holds BGRA rows of width * 4 bytes. Keep left and top with them.
```

## Stories

```markdown
# Stories

## M1 Core loop

### S-001 Hide the capture border
As the user, I capture a window for the first time and am asked once to allow captures without a border.
Screens: settings-capture-border
Acceptance:
- [manual] On Windows 11 the first window capture calls `RequestAccessAsync(Borderless)`; after Allow, window captures show no yellow border.
- [snap] Settings shows "Capture border" as Hidden or Shown, with a line on why and an Ask again button.
- [manual] On Windows 10, or after Deny, the border shows for the moment of the capture and is not in the image.
Status: todo

### S-002 Capture the full screen
As the user, I press Ctrl+Alt+Shift+3 and the display under the pointer is copied.
Acceptance:
- [self] The image is the display's size in physical pixels, on a 100% and a 150% display, without the app's own windows or the tray flyout.
- [manual] The shutter sound plays and the PNG pastes into Outlook, Paint, and a browser.
Status: todo

### S-003 Capture an area
As the user, I press Ctrl+Alt+Shift+4 and drag a rectangle.
Screens: picker-area
Acceptance:
- [snap] The screen dims except the selection, which has a white border and a pixel size label.
- [unit] An area on a display left of the main one, at negative coordinates, crops the right pixels from the frozen bitmap.
- [manual] Escape returns focus to the app I was in.
Status: todo

### S-004 Capture a window
As the user, I press Ctrl+Alt+Shift+5 and click a window.
Screens: picker-window
Acceptance:
- [snap] The window under the pointer gets an accent tint and border.
- [self] The image is the window's `DWMWA_EXTENDED_FRAME_BOUNDS` size in pixels, with no shadow and no invisible border.
- [manual] A window half covered by another one comes out whole; windows on other virtual desktops are never offered.
Status: todo

### S-005 Use the corner card
As the user, I copy, save, or drag out a capture from the card in the corner.
Screens: card, card-hover
Acceptance:
- [snap] Hover shows Copy and Save; white text stays readable over a white screenshot.
- [manual] The card never takes focus from the app I am typing in.
- [manual] Dragging into File Explorer drops a PNG; the card hides after 8 s unless hovered.
Status: todo

### S-006 Change shortcuts
As the user, I click a shortcut box in Settings and press a new combination, or turn on the Print screen key.
Screens: settings-shortcuts
Acceptance:
- [manual] Escape cancels, Delete clears; all shortcuts pause while a box records.
- [snap] A combo another app owns shows "In use by another app" under the box.
- [snap] With Windows' Print screen setting on, the Print screen row says Snipping Tool gets the key and shows Open keyboard settings.
Status: todo

### S-007 Choose what happens after a capture
As the user, I turn copy, save, card, and sound on or off and pick a save folder.
Screens: settings-capture
Acceptance:
- [unit] Defaults: copy on, save off, card on, sound on, folder Screenshots.
- [self] With save on, the file is `Shot yyyy-MM-dd at HH.mm.ss.png` in the folder.
- [manual] When OneDrive has moved Pictures, the default folder is the moved Screenshots folder.
Status: todo

### S-008 Capture from the tray
As the user, I right-click the tray icon and pick a capture, Settings, or Quit.
Screens: tray-menu
Acceptance:
- [snap] The menu lists the three captures with their shortcuts, then Settings and Quit.
- [manual] Clicking outside the menu closes it.
Status: todo

## M2 Mark up

### S-009 Open a capture in the editor
As the user, I click Annotate on the card and the editor opens with my capture.
Screens: editor-empty
Depends: S-005
Acceptance:
- [snap] Toolbar: Select, Arrow, Rectangle, Text, Blur, eight colors, three sizes, Undo, Redo, Copy, Save.
- [self] Ctrl+S saves a PNG at the capture's pixel size with the annotations on it.
Status: todo

### S-010 Draw arrows and boxes
As the user, I drag with Arrow or Rectangle to draw in the current color and size.
Screens: editor-shapes
Acceptance:
- [snap] Arrows taper from tail to head; boxes have round corners and a soft shadow.
- [unit] The export matches the canvas; color and size per tool are remembered.
- [manual] Select drags a shape, Delete removes it, and Undo restores both (200 steps).
Status: todo

### S-011 Add text
As the user, I click with Text, type, and press Enter or click away.
Acceptance:
- [manual] Letter keys go to the text box while typing, not to the tool shortcuts.
- [unit] An empty text box is removed; one edit is one undo step.
Status: todo

### S-012 Blur private parts
As the user, I drag with Blur over anything private and set its style and strength.
Screens: editor-blur
Acceptance:
- [snap] Pixelate at 70% by default; Pixelate or Blur and a 1 to 100% slider in the toolbar.
- [unit] A new box stays selected, and its edges never fade to transparent in the export.
Status: todo

## Dropped
- Ellipse, step counter, highlighter, resize handles (later)
- OCR copy text (later, `Windows.Media.Ocr.OcrEngine`)
- Usage stats, activity log, debug bridge capture commands ([recipe](../skills/code/os-integrations/recipes.md#debug-bridge), later)
```

## Gotchas

- Freeze first, then show the picker. GDI copies the whole virtual screen in tens of
  milliseconds, so the shortcut still feels instant, and the picker can never be in its own
  picture. This is the reverse of the Mac order, where the capture is the slow part.
- Use GDI for screen and area, Windows.Graphics.Capture only for windows. GDI needs no consent
  and shows no border. Windows.Graphics.Capture is the only way to get a window that other
  windows cover, but it shows a yellow border unless the user allowed borderless capture.
- The docs tie `GraphicsCaptureAccessKind.Borderless` to the `graphicsCaptureWithoutBorder`
  package capability, and Shot is unpackaged. Check the returned `AppCapabilityAccessStatus` and
  read the setting back. When it is not `Allowed`, keep the border and say so in Settings. Guard
  `IsBorderRequired` with `ApiInformation.IsPropertyPresent`: it does not exist on Windows 10 19041.
- Size window captures from `DWMWA_EXTENDED_FRAME_BOUNDS`, not `GetWindowRect`. `GetWindowRect`
  includes the invisible resize borders, about 7 px a side at 100%. If a frame comes back larger
  than the visible bounds, crop the extra.
- Skip cloaked windows in the window picker. Apps on other virtual desktops and suspended Store
  apps are "visible" to `IsWindowVisible` but cloaked (`DWMWA_CLOAKED` is not 0). Skip your own
  windows, minimized windows, and windows smaller than 8 x 8 px too.
- Use one picker window per display. A window across displays with different scales gets one
  DPI and draws the size label and border at the wrong size on the others.
- PrtScn belongs to Snipping Tool by default on Windows 11. When that setting is on, the key goes
  to Snipping Tool, not to you. Default to Ctrl+Alt+Shift+3/4/5 and offer PrtScn as an opt-in
  that explains the Windows setting. Leave Win+Shift+S alone: Windows owns it.
- Find the save folder with `SHGetKnownFolderPath(FOLDERID_Screenshots)`. Building
  `%USERPROFILE%\Pictures\Screenshots` by hand breaks when OneDrive moved Pictures.
- Put the PNG on the clipboard twice: `SetBitmap` for Paint and Office, and a `"PNG"` format
  for browsers and chat apps, which keep transparency from it. Encode on a background thread;
  a 4K PNG takes long enough to freeze the UI. Call `Clipboard.Flush()` so the image survives
  Shot quitting.
- `Clipboard.SetContent` throws when another app holds the clipboard open. Retry three times,
  50 ms apart.
- The card must never take focus. Create it with `WS_EX_NOACTIVATE` and show it with
  `SW_SHOWNOACTIVATE`, or the app the user is typing in loses the caret. Give its buttons a dark
  fill with white text; hover makes fills darker, never lighter, or they vanish over a bright picture.
- Drag and drop from Shot into an app running as administrator fails without an error, because
  UIPI blocks it. Copy still works. See [elevation and UIPI](../skills/code/os-integrations/recipes.md#elevation-and-uipi).
- Blur with Win2D `GaussianBlurEffect` and `BorderMode = EffectBorderMode.Hard`. The soft
  default fades the edges of the blurred box to transparent, so the secret shows through at the rim.
- Pixelate in `Shot.Core` on the BGRA bytes and draw the result with
  `CanvasImageInterpolation.NearestNeighbor`. Linear scaling smears the block edges, and the
  result looks like a soft blur instead of blocks.
- Windows has no shutter sound. Ship a short WAV and play it with `PlaySound(SND_ASYNC)`, so the
  capture never waits on audio.

Done when all three shortcuts capture at full pixel size on every display, at every scale and
with displays at negative coordinates, the card copies, saves, and drags out, and the editor
saves a PNG with arrows, boxes, text, and blur.

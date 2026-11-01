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

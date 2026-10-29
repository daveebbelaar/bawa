---
name: app-icon
description: Windows app icon drawn as an SVG (Icon/AppIcon.svg), rendered by tools/make-icon.ps1 into a multi-size AppIcon.ico and AppIcon.png, and reviewed on light and dark taskbar and Start backgrounds, with hand-tuned small sizes. Use when the user wants an app icon, a better icon, or a taskbar or Start icon.
allowed-tools: PowerShell(tools/make-icon.ps1*) PowerShell(./build.ps1*) Read Edit Write
---

# App icon

The icon is one SVG in git: `Icon/AppIcon.svg`, on a square canvas
(256 x 256 in the template). You write it by hand. Windows adds nothing
to an unpackaged app's icon: no plate, no mask, no shadow. What you draw
is what the taskbar shows, at every size.

- `tools/make-icon.ps1` renders it with `tools/IconTool` (Svg.Skia) into
  `src/<App>/Assets/AppIcon.ico` (16, 20, 24, 32, 40, 48, 64, and 256 px)
  and `AppIcon.png` (256 px). `build.ps1` runs it when the SVG changed.
- `tools/make-icon.ps1 -Review` also writes one review sheet you read:
  every size on the light and dark taskbar and Start colors, small sizes
  enlarged 4 times with no smoothing, so you see their real pixels.
- The `.ico` is the exe's icon (`ApplicationIcon` in the csproj: Explorer,
  Start, the taskbar pin) and the window's icon (`AppWindow.SetIcon`). The
  `.png` is the icon in the `TitleBar` control.

Older app without these files: copy `Icon/`, `tools/make-icon.ps1`, and
`tools/IconTool/` from the `new-windows-app` template, the icon block of
its `build.ps1`, and the `ApplicationIcon` and `Content` lines of its
csproj.

Other cases: [more-cases.md](more-cases.md).
- A packaged (MSIX) app, which needs PNG assets per size and theme.
- Converting an existing icon (an `.ico` or PNGs) to an SVG.
- A window icon that changes with a theme or a state.

## Small sizes by hand

At 100% scale Windows shows the icon at 16 px in the title bar, context
menus, and the system tray, and at 24 px on the taskbar. A 256 px drawing
scaled to 16 px turns thin lines into gray blur. So the small sizes get
their own drawing:

- `Icon/AppIcon-16.svg` (and `-20`, `-24`, `-32` when needed) replaces
  the scaled main drawing at that size. `make-icon.ps1` prints
  `16 px from AppIcon-16.svg` for each one it uses. If it prints nothing,
  the app's `tools/IconTool/Program.cs` is older than this feature: copy
  it from the `new-windows-app` template.
- Draw on a canvas of that size (`viewBox="0 0 16 16"`), with edges on
  whole pixels and lines 1 or 2 px thick.
- Keep the silhouette and the colors of the big icon. Drop what does not
  survive: sheens, the third line of text, small details. One shape that
  reads beats three that blur.

## What makes it look like Windows made it

From Microsoft's app icon guidelines (Microsoft Learn, "Design guidelines
for Windows app icons" and "Construct your Windows app's icon"):

1. One idea. A single metaphor that says what the app does, two at most:
   an envelope for mail, a check for tasks. A literal metaphor beats an
   abstract one.
2. No text, no letters. The app's name shows next to the icon everywhere.
3. Front facing. Draw it straight on. Slight perspective only when the
   object does not read without it (a cylinder needs its top) or the app
   is about 3D.
4. Simple shapes, as few corners as possible, a clear silhouette, no
   extremes of thick and thin. Rounded corners: 2 px outside and 1 px
   inside on a 48 px grid.
5. Flat layers, few of them. A soft shadow between layers separates them.
   Detail goes only on the top layer.
6. A soft gradient, if any: one or two steps, about 120 degrees, light
   from the top left. Never a tight gradient that looks like a reflection.
7. Colors from dark, medium, and light, and at least half the icon at 3:1
   contrast on both a light and a dark background. Yellow fails on light,
   red is hard on dark.
8. A transparent background looks best. A colored tile (the template's)
   is fine for a brand; then check it on both backgrounds.
9. Your own drawing. Never a Segoe Fluent Icons glyph: it reads as a
   Windows control, not as an app.
10. Distinct from the apps next to it on the taskbar. List the pinned
    apps without a screenshot:
    `Get-ChildItem "$env:APPDATA\Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar"`.

## Steps

1. Read `docs/BRIEF.md`. Write three icon concepts in one line each: the
   object, the colors, the layers. Pick one with the user, or pick the
   one that is easiest to recognize at 16 px.

   Check: one concept is chosen and it uses no text and no system glyph.

2. Write `Icon/AppIcon.svg`: a square `viewBox`, filled shapes (`rect`
   with `rx`, `circle`, `path`), and `linearGradient` fills. Leave the
   background transparent unless the concept has a tile.

   Check: the `viewBox` is square. A wide one stretches the icon.

3. Render and review:

   ```powershell
   tools/make-icon.ps1 -Review
   # wrote src\<App>\Assets\AppIcon.ico and AppIcon.png
   # wrote %TEMP%\<App>-icon-review.png
   ```

   Read the PNG. The icon reads at 256 px on all four backgrounds, and at
   16 and 24 px it is still recognizable. If 16 or 24 px blurs, add
   `Icon/AppIcon-16.svg` or `-24.svg` (Small sizes by hand).

   Check: both "wrote" lines print. On an error, fix the file it names.

4. Iterate two or three times. Change one thing per round: the shape's
   size, a color, a small-size drawing.

5. Install and look at the taskbar and Start:

   ```powershell
   ./build.ps1 -Open
   ```

   If Start or a taskbar pin still shows the old icon, Windows cached it.
   Run `ie4uinit.exe -show` to refresh the icon cache. As a last resort,
   unpin and pin the app again.

## Gotchas

- `build.ps1` re-renders when an icon SVG is newer than the `.ico`. An
  older `build.ps1` checks only `AppIcon.svg`: after changing only an
  `AppIcon-<size>.svg`, run `tools/make-icon.ps1` yourself.
- The exe embeds the `.ico` when it builds. Rendering a new icon changes
  nothing until `./build.ps1` runs again.
- Text in an SVG renders with whatever fonts this PC has. Turn letters
  into paths, or better, leave them out.
- Rectangles, circles, paths, gradients, and opacity render reliably.
  After using filters, masks, or clip paths, check the review sheet.
- Windows picks the exact size from the `.ico`, else the next larger one
  scaled down. At 125% the taskbar wants 30 px and gets 32 scaled down;
  a hand-tuned 32 px drawing helps there too.

Done when `tools/make-icon.ps1 -Review` succeeds, the sheet shows a clear
icon on all four backgrounds and at 16 and 24 px, the icon has no text
and no system glyph, and the taskbar and Start show the new icon.

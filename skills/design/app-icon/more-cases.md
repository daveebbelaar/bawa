# App icon: more cases

The cases outside BAWA's default (an unpackaged app with one `.ico`).
Asset names and sizes come from Microsoft Learn, "Construct your Windows
app's icon".

## Contents

- A packaged (MSIX) app
- Convert an existing icon
- A window icon that changes (themes, states)
- Drawing tricks

## A packaged (MSIX) app

A packaged app takes PNG assets per size and per theme instead of one
`.ico`. The app list icon (taskbar, Start, search) needs all three sets,
even when they look the same:

- `AppList.targetsize-<n>.png`
- `AppList.targetsize-<n>_altform-unplated.png` (dark theme)
- `AppList.targetsize-<n>_altform-lightunplated.png` (light theme)

for n = 16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, and 256.
Without the unplated files, Windows draws the icon smaller, on a plate.
The tile and Store logos (`Square44x44Logo`, `Square150x150Logo`,
`StoreLogo`) use scale variants (`.scale-100`, `.scale-200`,
`.scale-400`) instead.

The light and dark sets are the one place a Windows icon can differ per
theme. Render them from `AppIcon.svg` (and an `AppIcon-dark.svg` if the
dark one differs) with the same `Render` function `tools/IconTool` uses:
loop over the sizes and write each PNG under its name.

Check: the package's `Assets` folder has all 42 `AppList` files.

## Convert an existing icon

An old `.ico` or a set of PNGs becomes one SVG:

1. Get the largest frame. Modern `.ico` files store PNGs; this saves them
   (`System.Drawing.Icon` cannot read PNG frames in Windows PowerShell):

   ```powershell
   $bytes = [IO.File]::ReadAllBytes('old.ico')
   for ($i = 0; $i -lt [BitConverter]::ToUInt16($bytes, 4); $i++) {
       $entry = 6 + 16 * $i
       $size = if ($bytes[$entry] -eq 0) { 256 } else { $bytes[$entry] }
       $length = [BitConverter]::ToInt32($bytes, $entry + 8)
       $offset = [BitConverter]::ToInt32($bytes, $entry + 12)
       if ($bytes[$offset] -eq 0x89) { [IO.File]::WriteAllBytes("$PWD\frame-$size.png", $bytes[$offset..($offset + $length - 1)]) }
   }
   ```

   A frame that is not a PNG (an old bitmap frame) is skipped; open the
   `.ico` in an image editor for those.

2. Redraw it in `Icon/AppIcon.svg` with the frame as a guide: put it in
   as an `<image href="frame-256.png" opacity="0.3"/>` layer while you
   draw, and delete that layer before you render.
3. Keep only the shapes: the object, its parts, the accent. Drop bevels,
   glows, glossy sheens, and heavy drop shadows. Windows 11 icons are flat
   layers with soft shadows.
4. Keep the old small frames as a guide for `AppIcon-16.svg` and
   `AppIcon-24.svg`: someone may already have tuned them by hand.

## A window icon that changes (themes, states)

The exe's icon (Explorer, Start, a taskbar pin) is fixed at build time.
The window's own icon can change while the app runs:

```csharp
// One .ico per variant, rendered ahead of time with tools/IconTool and shipped in Assets\Icons\.
AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Icons", $"{variant}.ico"));
```

- Render each variant from its own SVG:
  `dotnet run --project tools/IconTool/IconTool.csproj -c Release -- Icon/Variants/<name>.svg <out-dir>`,
  then rename the `AppIcon.ico` it writes to `<name>.ico`.
- Add `Assets\Icons\*.ico` to the csproj as `Content` with
  `CopyToOutputDirectory="PreserveNewest"`, as the template does for
  `AppIcon.ico`.
- The default variant calls `SetIcon` with `AppIcon.ico`, so the window
  and the pin match again.
- Show the same picture inside the app (an icon picker, an About page)
  from the variant's PNG.

Check: switch the variant, and the title bar shows the new icon at once.

## Drawing tricks

- A soft shadow between two layers without a filter: a copy of the upper
  shape, 1 to 2 px lower on the 48 px grid, in black at 10 to 20%
  opacity, drawn just under it.
- A light-from-top-left gradient: `x1="0" y1="0" x2="1" y2="1"` with a
  lighter stop first, one or two steps apart.
- An icon that vanishes on dark (a dark navy shape): give its top layer a
  lighter tint, so at least half of it passes 3:1 on both backgrounds.
- A tile icon on the taskbar sits next to transparent ones. Keep the
  tile's corners round and its edge inside the canvas by about 6% (16 px
  of 256 in the template), so it lines up with them.

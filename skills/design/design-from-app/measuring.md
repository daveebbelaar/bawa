# Measuring a screenshot

Recipes for step 3, and the traps found while measuring WinUI windows
with these scripts. The rules carry over from BAMA, where a task app's
inbox was rebuilt from one screenshot to 1.3% different pixels, with
every row within 1.5 pt.

## Contents

- Recipes
- Text sizes
- Traps
- A measured example

## Recipes

**Pane widths.** Run `hline` through an empty part of the window. Each
run of one color is a pane:

```text
measure.ps1 hline shot.png 400 0 400 -Tolerance 3
0.8..320  (320 epx)  #EFF4F9     <- sidebar
320.8..320.8  (0.8 epx)  #E2E6EA <- the content pane's border
321.6..356.8  (36 epx)  #F8FAFC  <- page margin
```

**Row pitch.** `rows` over a narrow column of icons and labels lists
every row's content band. The distance between band starts is the row
pitch. Start below the title bar, or the title bar counts as content.

**Left edges.** `cols` across one row lists where the checkbox, the icon,
the text, and the details start. Do it for two rows to confirm a shared
edge.

**Selection and hairlines.** Run `vline` and `hline` through the selected
row with `-Tolerance 4` to find the pill's edges. Card borders and
dividers show up as runs of one pixel (0.8 epx at 125%) in a light gray.

**Colors.** `ink` ignores the background and reports the strongest color
of the text or icon in a box. Sample each kind of text: primary,
secondary, accent, and each status color.

**Scale.** Every number is pixels divided by `-Scale`. At 125% one pixel
is 0.8 epx, so edges land on multiples of 0.8. Round to the nearest whole
epx when the kit's token is a whole number.

## Text sizes

A band from `rows` over a word is its cap or ascender height, not its font
size, and the letters change it. Do not convert it with a formula.
Instead:

1. Build the screen with a first guess (14 epx body, the WinUI default).
2. Measure the same word in both images: band height and width.
3. Scale the size by the ratio of the widths. Repeat once.

If every ratio comes out the same (0.9, say), the reference ran zoomed.
That is common for web and Electron apps. Either keep the zoomed sizes to
match the screenshot, or keep the reference's own sizes and multiply
every token by one factor, so the kit can run at native size later.

## Traps

- **The window border.** The outermost pixel row and column of a capture
  is the window's border (black or white). `rows`, `cols`, and `ink` take
  the first pixel of the region as the background, so start every region
  1 epx inside the window.
- **Display scale.** A reference taken at 150% and a snapshot taken at
  125% have different pixel sizes, and `compare.ps1` does not resize.
  Take the reference on the same PC, or at the same display scale.
- **Window size.** `SizeAndCenter(1100, 720)` gave a visible window of
  1087.2 x 713.6 epx at 125%: the size includes the invisible resize
  border on the left, right, and bottom. Measure the snapshot, then set
  the size so the visible window matches the reference.
- **Mica.** Mica takes its tint from the wallpaper, and an inactive
  window loses it. The same app shows a different sidebar color on
  another desktop. When the reference has a flat color, give the kit a
  solid surface brush. When it has Mica, compare with the same wallpaper
  and keep Mica out of the color tokens.
- **Focus visuals.** A control with keyboard focus draws the focus
  rectangle into the snapshot. Make sure the reference and the build have
  focus in the same place, or nowhere.
- **Selection indicator.** WinUI lists and the navigation pane draw a
  3 epx wide, 16 epx tall pill at the left of the selected row, in the
  accent color. A reference without it needs a kit row, not a setting.
- **Two kinds of detail line.** A date line and a description line looked
  the same but had different space below. When one row is off and the
  others match, look for a different part, not a different number.
- **Selected icons.** References often switch to filled icons on the
  selected row.
- **Compressed screenshots.** JPEG and WebP shift colors by a few steps.
  Round to the nearest clean value, and sample large flat areas.
- **Antialiasing.** Edges of text and icons blend with the background.
  ClearType adds colored fringes to text in some apps. Read sizes from
  `rows` and `cols` with `-Tolerance 30` or more, and colors from `ink`,
  not from single pixels.
- **A small shift is a big diff.** The same screenshot moved down 2
  pixels already differs by 1.7%. When the diff is high, check the window
  size and the first row's top before anything else.

## A measured example

The BAWA template's items page, light mode, 125%, measured with these
scripts:

| Token | Value |
| --- | --- |
| window | 1087.2 x 713.6 epx visible from `SizeAndCenter(1100, 720)` |
| content pane | starts at y 32.8, below the title bar |
| sidebar | 320 wide; rows 40 apart; pill 36 tall, inset 4; indicator 3.2 x 16 |
| page | margin 36; title band 20.8 tall at y 72.8 (28 epx semibold) |
| list | card at x 356.8, rows 40 apart, icon 16 at +18, text at +46 |
| colors | sidebar #EFF4F9 (Mica, varies), page #F8FAFC, card #FDFDFE, text #1A1A1A, selected row #F4F4F5 |

---
name: layout-grid
description: Grid and alignment rules for a WinUI 3 app, with three checks that catch misalignment (a lint for off-grid numbers in XAML and C#, a layout probe that measures real bounds in snapshot mode, and a grid overlay). Use when building or reviewing a custom page, card, row, form, or dialog, after every build that touched layout, or when the user says something looks misaligned, crooked, cramped, or unevenly spaced.
---

# Layout grid

Agents guess numbers. A guessed `Margin="13"` looks almost right and makes
the whole app feel slightly off. This skill replaces guessing with one
scale and three checks that fail when something is off. Windows itself
lays out on a 4 epx grid, so the scale matches every WinUI control.

The template has everything in place: `src/<App>/UI/Space.xaml`,
`UI/Grid.cs`, and `tools/`. For an app made before this skill, copy those
two files, `tools/grid-lint.ps1`, and `tools/check-layout.ps1` from the
`new-windows-app` skill's template, and the `GridProbes.Write` line from
its `Snapshot.cs`.

## The rules

1. Every margin, padding, spacing, and size comes from `Space.xaml` in
   XAML (`{StaticResource SpaceLg}`) and `Space` in code (`Space.Lg`):
   `Xs 4`, `Sm 8`, `Md 12`, `Lg 16`, `Xl 24`, `Xxl 36`, `Section 48`. Raw
   numbers are 0, 1, 2, or a multiple of 4.
2. Pages have 36 epx side margins (`PageMargin`). Cards pad 16
   (`CardPadding`). Related controls sit 8 apart, cards 16 apart in a row
   and 4 apart in a stack of settings cards, groups 24 to 32, sections 48.
3. Each column has one leading edge. Titles, text, cards, and buttons in a
   column start on the same x. Indent on purpose, by a step of the scale.
4. Let containers do the aligning:
   - Rows and columns of values: `Grid` with `RowSpacing` and
     `ColumnSpacing`.
   - A stack: `StackPanel` with `Spacing`, never a margin on each child.
   - A label above a control: the control's own `Header`.
   - A setting: `SettingsCard`, which aligns icon, text, and control.
   - Numbers in a column: `FontFeatures` tabular figures or a fixed
     column, right aligned.
   - An icon with text: a horizontal `StackPanel` with `Spacing` 8 or 12,
     both centered vertically.
5. Dialogs keep `ContentDialog`'s own width. Fixed widths for columns are
   multiples of 4 (`ListColumnWidth` 320). Controls take their height from
   WinUI, never a fixed `Height`.
6. Nested corners are concentric: inner radius = outer radius - padding.
7. `RenderTransform` with a `TranslateTransform` and `Canvas.Left` move a
   view after layout, so nothing lines up with it. Use them only for
   motion and drag previews, with `grid: ok <why>` in a comment on the
   line.
8. Long text trims with its full value in a tooltip. Nothing clips.

## Check 1: lint the code

Run it after every build that touched pages:

```powershell
tools/grid-lint.ps1
# src\Tasks\Pages\TodayPage.xaml:14: off grid (10): <StackPanel Margin="10,0,0,0">
# FAIL grid-lint: 1 lines off the grid
```

Fix each line with a token. Silence a line on purpose with
`<!-- grid: ok <why> -->` or `// grid: ok <why>` on the same line.

## Check 2: probe the real layout

Mark what you lay out yourself: the page, its sections, cards, rows in a
card, dialog content. `NavigationView`, `ListView` rows, `SettingsCard`,
and `CommandBar` align themselves and need no probes.

```xml
<StackPanel Padding="{StaticResource PageMargin}" Spacing="{StaticResource SpaceXl}"
            ui:GridArea.Name="page" ui:GridArea.Inset="{StaticResource SpaceXxl}">
    <Grid ui:GridProbe.Area="page" ui:GridProbe.Name="header"> ... </Grid>
    <local:StatsRow ui:GridProbe.Area="page" ui:GridProbe.Name="stats" />
    <Border Style="{StaticResource CardStyle}" ui:GridProbe.Area="page" ui:GridProbe.Name="card" />
</StackPanel>
```

`ui:GridArea` marks a container and its inset. `ui:GridProbe` marks a view
in it. Probes with the same `Edge` (default `lead`) must share one leading
edge. Set `ui:GridProbe.Edge=""` for views that sit side by side.

Probe a row, not the text in it. A title centered against a taller command
bar has a bottom edge that is not on any grid line, while the row is.

Snapshot mode writes a `<page>-<mode>-layout.json` next to every PNG,
with positions in epx and the display scale. Check them:

```powershell
tools/check-layout.ps1
# PASS all-light-layout.json: 2 probes in 1 areas
# FAIL today-light-layout.json
#   - edge page/lead has 2 leading edges: header 356.8, stats 360.8
```

It checks that every probe:

1. Starts on a whole physical pixel, so its edges stay sharp.
2. Starts a multiple of 4 epx from its area's edge.
3. Stays inside its area's inset on both sides.
4. Shares one leading edge with the others on its edge.
5. Sits a multiple of 4 epx below the probe above it.
6. Never overlaps another probe in its area.

At 125% or 150% display scale WinUI snaps edges to physical pixels, so a
36 epx margin can measure 36.8. Every comparison allows one physical
pixel; a real misalignment is larger.

## Check 3: look at the grid

When a check fails and the cause is not clear, draw the grid over the
window:

```powershell
tools/snap.ps1 -Grid -Pages today
```

The PNGs show an 8 epx grid, each area in blue with its inset dashed, and
each probe in pink with its name. Open them with the Read tool.

## Fix a failure

| Message | Usual cause | Fix |
| --- | --- | --- |
| starts 23 epx into page | a raw margin on one child | remove it, use the panel's `Spacing` or `Padding` token |
| 2 leading edges | a centered panel, or one child with extra margin | `HorizontalAlignment="Stretch"` or `Left` on the children |
| gap 10 epx | `Spacing="10"`, or a margin on top of `Spacing` | one token, on the panel |
| gap 29.2 epx between a title and a list | the probe sits on centered text | probe the row that holds the text |
| not inside its 36 epx inset | a missing `PageMargin`, or a fixed width wider than the parent | `Padding="{StaticResource PageMargin}"`, `MaxWidth` instead of `Width` |
| overlap | a `TranslateTransform`, `Canvas.Left`, or a fixed height | remove it, let the panel size the view |
| ScrollViewer's child sets MaxWidth | the page column sits straight in the `ScrollViewer`, which places it by its desired width: maximized, it slides off the right edge | a `<Grid>` between the `ScrollViewer` and the column |
| between physical pixels | a fractional number, or `UseLayoutRounding="False"` | a whole number from `Space`, layout rounding on |

Done when `tools/grid-lint.ps1` passes, `tools/check-layout.ps1` passes on
the layout files of every page you touched, and every custom page and
card has a `ui:GridArea` with probes on its main parts.

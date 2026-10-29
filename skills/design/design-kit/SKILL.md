---
name: design-kit
description: Custom design system for a WinUI 3 app with its own parts, not the stock Fluent ones, covering three token layers, type, an icon set, every part in every state, custom title bar chrome, a parts gallery, and checks. Use when the user wants the app to have its own look and feel, a brand, or custom buttons, rows, and sidebars, when design-from-app has measured a reference app, or when the user asks for a design system or component library.
---

# Design kit

A native look comes from WinUI's own parts. A custom look comes from your
own parts: your sidebar row, your button, your checkbox, your icons. XAML
draws all of them. A kit is those parts, the numbers behind them, and one
page that shows every part in every state.

Most apps keep WinUI's parts and only pick an accent color (`design-system`
skill). Build a kit when the parts themselves must change. To copy the
look of an existing app, call the Skill tool with `design-from-app` first:
it measures the app and then comes back here.

Everything lives in `src/<App>/DesignKit/`. Code for every step:
[parts.md](parts.md).

## 1. Write the spec

Write `design/KIT.md` from [kit-spec.md](kit-spec.md): three words for the
feel, the tokens, the type scale, the icon rules, and one section per part
with its sizes and states. Agents build from this page and review against it.

Check: every part the app's screens need has a section with its sizes and
its states.

## 2. Tokens in three layers

`DesignKit/Tokens.xaml`, merged in `App.xaml` after `XamlControlsResources`:

1. **Palette**: raw colors, named by hue and step (`PaletteGray50`,
   `PaletteBlue600`). Only this file reads it.
2. **Meaning**: `InkTextBrush`, `InkAccentBrush`, `InkSelectionBrush`,
   `InkDividerBrush`, in `ThemeDictionaries` with a `Light`, a `Dark`, and
   a `HighContrast` value each. The `HighContrast` values are the person's
   own system colors (`SystemColorWindowTextColor` and the rest). Parts
   read only this layer, always with `{ThemeResource}`.
3. **Scales**: sizes, radii, and opacities (`KitRowHeight`, `KitRadius`),
   motion (named intents like `KitMotionHover`), and the text styles.

A rebrand changes the palette. A dark mode fix changes the meaning layer.
Neither touches a part. `tools/grid-lint.ps1` skips `Tokens.xaml` and
`Tokens.cs`, so the kit's own numbers live there.

Check: this finds nothing.

```powershell
Get-ChildItem src -Recurse -Include *.xaml, *.cs | Where-Object Name -ne 'Tokens.xaml' |
    Select-String 'Palette[A-Z]'
```

## 3. Type

Name styles by role (`KitTitleText`, `KitBodyText`, `KitLabelText`,
`KitSectionText`, `KitMetaText`), never by size. Base them on WinUI's type
ramp styles (`BodyTextBlockStyle`), so they keep its line height and
trimming. Use the system font (Segoe UI Variable on Windows 11) unless the
brand needs its own; a bundled font goes in `Assets/Fonts/` as Content and
is named with `ms-appx:///Assets/Fonts/<file>.ttf#<Family>`. Fractional
sizes are fine when a reference needs them.

Check: views set text only through `Style="{StaticResource Kit...Text}"`,
with no `FontSize` outside `DesignKit/`.

## 4. Icons

Icons decide the feel more than color does. Views name a `KitIcon`
(`{kit:Icon Name=Inbox}`), never a glyph code or a path, so the whole set
can change in one place.

- Start from Segoe Fluent Icons at one size (16 epx). The font has one
  weight; for a lighter or heavier set, draw your own.
- For a set of your own, draw each icon as a `PathIcon` from its SVG path,
  on a 16 x 16 box, filled. Convert strokes to outlines first: a
  `PathIcon` fills its path and never strokes it.
- One size, one stroke weight, one corner style for the whole set.
- A selected row may switch to the filled form.

Check: no `Glyph=` and no `0xE...` code point outside `DesignKit/`.

## 5. Parts

One file per family in `DesignKit/Parts/`: buttons, fields, rows. Each
part:

- Is a real control. Style a `Button`, `CheckBox`, `TextBox`, or
  `ListViewItem`. Never a `Border` with a `PointerPressed` handler: it
  loses the keyboard, focus, and Narrator.
- Uses the lightest change that gets the look: WinUI's own resource keys
  first (lightweight styling, `CheckBoxCheckBackgroundFillChecked`), then
  a `Style` with setters, then a full `ControlTemplate`.
- Has every state: rest, hover, pressed, selected, focused, disabled, in
  light, dark, and high contrast. A template names them in its
  `VisualStateManager`: `Normal`, `PointerOver`, `Pressed`, `Disabled` in
  `CommonStates`, and `Focused`, `Unfocused`, `PointerFocused` in
  `FocusStates`.
- Takes a forced state for the gallery (`kit:KitState.Forced`), so hover,
  pressed, and focused show up in a snapshot.
- Reads only meaning tokens, scales, motion, the text styles, and
  `KitIcon`.

Check: the part matches its section in `design/KIT.md` in every state.

## 6. Title bar chrome

For a fully custom look, keep the `TitleBar` control and make it the
kit's: the kit background, a search box in `Content`, buttons in
`RightHeader`, and the caption buttons painted in kit colors. Build the
sidebar from kit rows (a `ListView` with `KitSidebarItemStyle`) instead of
`NavigationView`, or restyle `NavigationView` through its resource keys.
Keep the caption buttons, Snap layouts, menus (`MenuFlyout`), context
menus, `ContentDialog`, and the resize borders native: users depend on
them.

A custom sidebar matches Windows' own where people would notice: the
arrow keys move the selection, Tab enters and leaves the list once,
Narrator reads the selected row, and the selection shows by fill or by
the accent pill, never by color alone (parts.md, Rows).

Check: the window opens at its default size, the title bar drags the
window everywhere except its controls, the caption buttons read in light
and dark, and the custom sidebar moves with the arrow keys.

## 7. Gallery

`DesignKit/KitGallery.xaml` shows every part in every state, the way a
storybook does. The app shows it instead of its pages when
`APP_KIT_GALLERY=1` is set in a snapshot run, so the normal snapshot
command captures it in light and dark:

```powershell
$env:APP_KIT_GALLERY = '1'; tools/snap.ps1; Remove-Item Env:APP_KIT_GALLERY
# writes %TEMP%\<App>-shots\kit-light.png and kit-dark.png
```

Review a new part here before it goes into a screen. Check high contrast
by hand once: turn on a contrast theme (Settings > Accessibility >
Contrast themes) and open the gallery.

Check: the gallery snapshot shows every part from `design/KIT.md`, and the
user has seen it.

## 8. Checks

```powershell
tools/grid-lint.ps1
Get-ChildItem src -Recurse -Include *.xaml, *.cs |
    Where-Object { $_.FullName -notmatch '\\(DesignKit|UI|obj|bin)\\' } |
    Select-String '="#[0-9A-Fa-f]{6,8}"|Color\.FromArgb|FontSize=|Glyph=|0xE[0-9A-F]{3}'
```

Both come back clean. Then build, snapshot, and run the `layout-grid`
checks on the screens (`build-and-verify` skill).

Done when `design/KIT.md` covers every part, every part is a real control
with every state, the gallery shows them all in light and dark, high
contrast has been checked by hand, and the checks above come back clean.

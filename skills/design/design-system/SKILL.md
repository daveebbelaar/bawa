---
name: design-system
description: Accent color and design system for a Windows app, from the app's accent and the accent picker in Settings to XAML and C# tokens plus a design/DESIGN-SYSTEM.md that says which token to use when. Use when the user wants a brand or accent color, wants people to pick a color, when an app has more than a few screens, when screens drift apart or animations feel inconsistent, or when the user asks for themes, a style guide, or design tokens.
---

# Design system

A design system is a short list of **tokens** in code and one page that
says which token to use when. Every screen uses tokens, never raw values.
Then a change in one place changes the whole app, and an agent can review
a screen against one page.

Start from `windows-design`. WinUI already ships hundreds of theme
resources (`TextFillColorPrimaryBrush`, `ControlCornerRadius`). Tokens
cover only what WinUI does not: the accent, custom surfaces, spacing,
motion, overlays, and custom controls.

The app keeps the Fluent look. Only the accent changes. For parts of your
own (a custom navigation rail, buttons, icons), call the Skill tool with
`design-kit`. To copy the look of an app the user names, call it with
`design-from-app`.

The template already has the base in `src/<App>/UI/`:

- `Theme.cs`: the accent choices (`Theme.All`), `Theme.ApplyAccent`, which
  writes the seven `SystemAccentColor` shades WinUI derives every accent
  brush from, the app theme modes, and `Theme.Reapply`, which makes a new
  accent show at once.
- `Pages/SettingsPage`: Settings > Appearance, where people pick the app
  theme and the accent. "Use system setting" and "Windows default" are the
  defaults.
- `Space.xaml` and `Grid.cs`: spacing (see the `layout-grid` skill).

## 1. Pick the accent

Read `## Look` in `docs/BRIEF.md`. With no color named, keep `system`: it
follows the accent the person set in Windows Settings > Personalization >
Colors.

For a brand color, add an `AccentChoice` to `Theme.All` with its base
color. `ApplyAccent` derives the light and dark shades: light mode fills
with Dark1 and writes accent text with Dark2; dark mode fills with Light2
and writes with Light3. Check that white text on the base reads well; for
yellow, mint, or cyan pick a deeper base. Keep `Theme.DefaultId = "system"`
unless the owner asks for the brand by default: Windows people expect
their own accent, and the person can still pick the brand in Settings.

Use brand color sparingly: the app icon, an empty state, one hero element
in the content. Keep it off most controls.

To compare colors, take snapshots with `tools/snap.ps1 -Accent <id>` for
two or three of them and show the user the PNGs.

Check: `Theme.DefaultId` is "system" (or the owner chose otherwise in
writing), and the snapshots show the accent on the accent button, toggles,
and the selection pill in light and dark mode.

## 2. Collect

New app with no pages yet? Skip this step: write the token files with the
defaults below before the first page.

Existing app: read every page. List each raw value you find: hex colors,
opacities, corner radii, margins, paddings, widths, animation durations
and easing, custom styles. Group them. Most apps have 3 to 5 of each where
1 or 2 are meant.

## 3. Write the tokens

Accent values live in `Theme.cs`. The rest get one file per group in
`src/<App>/UI/`, merged in `App.xaml`. Add each token when the app first
needs it:

| File | Holds |
| --- | --- |
| `Theme.cs` | accents, app theme, `ApplyAccent`, `Reapply` |
| `Space.xaml` + `Grid.cs` | the spacing scale (`SpaceXs` to `SpaceSection`, `PageMargin`, `CardPadding`) and the layout probes |
| `Surfaces.xaml` | custom brushes in `ThemeDictionaries` (`Light`, `Dark`, `HighContrast`): `SurfaceSubtle`, `SurfaceHover`, `Hairline`, one warning color |
| `Radius.xaml` | only when WinUI's `ControlCornerRadius` (4) and `OverlayCornerRadius` (8) are not enough: chips and key caps 4, cards 8, a hero panel 12 |
| `Layout.xaml` | `ContentMaxWidth`, `ListColumnWidth` (a `GridLength`), fixed dialog widths |
| `Motion.cs` | durations and easing: `Fast` 83 ms, `Normal` 167 ms, `Slow` 250 ms (Fluent's timings), and `Animate` that returns zero when "Animation effects" is off |
| `Styles.xaml` | named styles: `CardStyle`, `EmptyStateGlyph`, `KeyCapStyle` |
| `Overlay.cs` | one builder for borderless panels: size, position, entrance, exit |

Brushes adapt to light, dark, and high contrast through
`ThemeDictionaries`. Pages read them with `{ThemeResource ...}`, never
`{StaticResource ...}`, so a theme change redraws them.

## 4. Write design/DESIGN-SYSTEM.md

One section per file above. Each section says which token to use when, and
the rules that go with it. Keep it under 100 lines. Rules that transfer to
most apps:

- Never hard-code a hex accent. Use `AccentFillColorDefaultBrush` and
  friends; they follow `Theme.ApplyAccent`.
- Every custom brush has three theme values: `Light`, `Dark`, and
  `HighContrast` (which points at a system color like
  `SystemColorWindowTextColor`).
- Never reuse a semantic brush for another job (a stroke brush as text, a
  text brush as a fill). Their values change between releases.
- `{ThemeResource}` for anything that changes with the theme;
  `{StaticResource}` only for numbers, thicknesses, and styles.
- Accent text uses `AccentTextFillColorPrimaryBrush`. Text on an accent
  fill uses `TextOnAccentFillColorPrimaryBrush`, never a fixed white.
- Style controls with `Style` and, when needed, a `ControlTemplate` copy
  with every visual state. Never rebuild a control from shapes: it loses
  the keyboard, focus, and Narrator.
- Inner corners are concentric: inner radius = outer radius - inset.
- Every animation goes through `Motion`, so "Animation effects" off and a
  per-feature "Full, Fast, Off" setting apply everywhere.
- Search results and switchers do not animate. Speed wins.
- Pointer hover never moves a selection. Only a click or a key does.
- Units ("KB/s", "%") never animate. Only the number does.
- Never animate a page's first layout. Overlays wait one frame, then fade
  and scale in over 167 ms.
- One accent button per page or dialog.

## 5. Migrate

Replace raw values screen by screen, one commit per screen. After each,
build and snapshot (`build-and-verify` skill). The screen looks the same
or better, never worse.

## 6. Point agents at it

Add one line to the app's `CLAUDE.md`: "Design rules:
`design/DESIGN-SYSTEM.md`. Use its tokens; never raw colors, radii, or
durations."

Done when this finds no hits outside the token files:

```powershell
Get-ChildItem src -Recurse -Include *.xaml,*.cs |
  Where-Object { $_.Name -notmatch '^(Theme\.cs|Space\.xaml|Grid\.cs|Surfaces\.xaml|Radius\.xaml|Motion\.cs|Tokens\.(xaml|cs))$' } |
  Select-String -Pattern '#[0-9A-Fa-f]{6,8}\b|Color\.FromArgb|Colors\.(White|Black|Gray)|CornerRadius="\d|Duration="0:0:'
```

(hex strings in data and comments are fine), and DESIGN-SYSTEM.md has a
section for every token file.

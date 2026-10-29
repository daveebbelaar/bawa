---
name: windows-design
description: Design rules that make a WinUI 3 app look and feel like a native Windows 11 app, from the Fluent design principles to Mica, the title bar, navigation, command bars, menus, keyboard, type, color, and dark mode. Use when building or reviewing any window, page, command bar, menu, flyout, dialog, or settings page, or when the user asks for a more native, Windows-like, or better designed app.
---

# Windows design

Microsoft already designed your app. Pick the right WinUI controls, arrange
them, and stay out of their way. Use the system first. Add custom design
only where WinUI has nothing.

- The full checklist: [design-rules.md](design-rules.md). Use it for
  reviews.
- Copyable XAML: [patterns.md](patterns.md).
- The Settings page: [settings.md](settings.md).
- Commands: command bars, menus, context menus, and shortcuts:
  [commands.md](commands.md).
- Microsoft's own text on one topic, when a rule here is not enough: the
  Windows app design pages on Microsoft Learn
  (`learn.microsoft.com/windows/apps/design/`), for example
  `controls/navigationview` or `signature-experiences/materials`. The WinUI
  Gallery app shows every control live.
- App-wide tokens (colors, radii, motion): call the Skill tool with
  `design-system`.
- How a feature works and what it says (names, flow, dialogs, errors,
  accessibility): call the Skill tool with `ux-design`.
- Spacing, alignment, and the checks that catch misalignment: call the
  Skill tool with `layout-grid`.
- Parts of your own instead of Fluent's: call the Skill tool with
  `design-kit`, or `design-from-app` to match an app the user names.

## Fluent principles

Use them as the review lens for every screen (Windows 11 design principles):

1. **Effortless**: faster and easier to do what people want, with focus and
   precision. Cut what does not earn its place.
2. **Calm**: quieter, less cluttered, less noisy. Soft materials, rounded
   corners, motion that informs and never shows off.
3. **Personal**: the app fits the person. It follows their theme, accent,
   text size, and contrast settings.
4. **Familiar**: things that look the same work the same. Standard
   controls, standard shortcuts, standard words.
5. **Complete and coherent**: the app feels like part of Windows on every
   screen, in light, dark, and high contrast, with mouse, keyboard, touch,
   and pen.

Every screen answers: Where am I? What can I do? Where can I go?

## Before you write pages

Write these under `## Design plan` in `docs/BRIEF.md`, or in
`docs/DESIGN.md` when the brief would pass one page:

1. **Archetype**: navigation window, single-page tool, tray app, or
   quick-capture panel. Most personal apps are a navigation window plus one
   quick-capture entry point. A tray utility is "tray icon + flyout or
   panels + Settings window": the navigation rules then apply to the
   Settings window only.
2. **Information architecture**: navigation pages, at most two levels.
3. **Command map**: every action, where it lives (command bar, context
   menu, tray menu), and its shortcut.
4. **Empty states**: what each page shows before there is data.
5. **Feeling**: the one feeling the app aims for (calm, quick, confident).
   Motion, words, and color all reinforce it.

## Entry points first

A Windows utility is used through its small entry points: a global
shortcut, the tray icon, Start, a quick panel, drag and drop, the
taskbar jump list. Design those first. A to-do app lives in its quick-add
panel.

- Quick capture: one small window with one input, opened by a global
  shortcut, closed by Esc or Enter.
- **Optimistic UI**: on Enter, close and show the result at once. Save in
  the background. Offer Undo, not a progress ring.

## Layout

- The window: Mica backdrop, `ExtendsContentIntoTitleBar` with the
  `TitleBar` control (app icon, app name, the pane toggle, a search box in
  the middle when the app has search), and a left `NavigationView` with
  Settings at the bottom. No NavigationView when the app has one page.
- `NavigationView` pane modes: `Left` on a wide window. It collapses to a
  compact rail and then to a hamburger on its own as the window narrows.
  Keep that behavior.
- Each page: a title in `TitleTextBlockStyle`, page commands in a
  `CommandBar` on the right of the title row, then content.
- Content sits on cards (`CardBackgroundFillColorDefaultBrush`, a 1 epx
  `CardStrokeColorDefaultBrush` border, `OverlayCornerRadius`) over the
  Mica layer. The window background itself stays Mica.
- List and detail: a list column (320 epx) and a detail card, or a
  `ListView` that opens a detail page on narrow windows.
- Progressive disclosure: an empty page shows one empty state with the
  first action. Filters appear once there is content.
- Spacing from `UI/Space.xaml`, on a 4 epx grid: 36 epx page margins on
  the sides, 8 between related controls, 16 between cards in a row, 24
  between a title and its content, 32 above a group header. Controls take
  their height from WinUI (32 epx buttons and boxes), never a fixed
  height.
- The minimum window size keeps every page usable. Test it in snapshots
  by setting the window to the minimum.

## Materials

- **Mica** on the main window. It tints with the desktop wallpaper and
  goes flat when the window is inactive. That is expected.
- **Mica Alt** (`MicaBackdrop Kind="BaseAlt"`) for windows with tabs in the
  title bar.
- **Acrylic** belongs to transient surfaces: flyouts, menus, tooltips,
  and a quick panel. WinUI gives flyouts and menus acrylic on their own.
  Never put acrylic on a page background.
- Content gets solid card fills, never a backdrop of its own.
- Never a material on a material: a card on Mica, yes; an acrylic card on
  Mica, no.

## Commands and menus

- Page commands live in a `CommandBar` with `DefaultLabelPosition="Right"`
  for up to four primary commands, the rest in the overflow. Icons from
  Segoe Fluent Icons, every one with a label.
- One accent button (`AccentButtonStyle`) per page or dialog, only for the
  main completing action: Save, Send, Create. Most pages have none.
- Every command also has a `KeyboardAccelerator`. WinUI shows it in the
  tooltip and the overflow menu. The self-test checks every command.
- Standard shortcuts keep their meaning: Ctrl+N, Ctrl+O, Ctrl+S, Ctrl+Z,
  Ctrl+Y, Ctrl+C, Ctrl+V, Ctrl+X, Ctrl+A, Ctrl+F, F2 (rename), Delete,
  Ctrl+W (close), F5 (refresh). Windows has no standard Settings shortcut:
  Settings opens from the navigation pane.
- Disable commands that do not apply; keep them visible.
- A `MenuBar` (File, Edit, View) only for document apps with many
  commands, like Notepad or Paint. Most apps need none.
- Window titles name the content: "Today - Tasks".
- Full rules: [commands.md](commands.md).

## Windows open calm

- Nothing grabs focus when a window or dialog opens: no blinking caret in
  a text box, no selected text. The one exception is a window whose only
  job is typing, like a search or quick-add panel: it focuses its field.
- WinUI shows focus rectangles only after keyboard use. Never turn them
  off: keyboard users need them.
- A `ContentDialog` sizes to its content. It scrolls only when the content
  is taller than the window allows.
- System scroll bars. They shrink to a thin line until the pointer comes
  near. Do not restyle them.

## Keyboard first

- Every frequent action has a shortcut, and every shortcut shows feedback
  in the same frame.
- Enter is the default action, Esc cancels, Delete removes, F2 renames,
  arrows move, Tab moves between groups.
- Scope keys that would break typing (Delete, arrows, Enter) to the list
  with `KeyboardAccelerator.ScopeOwner`, so they never fire in a text box.
- New items get focus (`Focus(FocusState.Programmatic)`) so the user can
  type at once.
- Access keys (`AccessKey="N"`) let people press Alt and a letter. Add them
  to the command bar in apps used all day.
- Teach shortcuts in tooltips, menus, and a list in Settings.

## Color, type, dark mode

- Text uses `TextFillColorPrimaryBrush`, `TextFillColorSecondaryBrush`,
  `TextFillColorTertiaryBrush`. Surfaces use the theme's fill brushes. No
  fixed white, black, or gray in chrome or text.
- One accent. Accent text uses `AccentTextFillColorPrimaryBrush`; text on
  an accent fill uses `TextOnAccentFillColorPrimaryBrush`, never a fixed
  white.
- Status color always comes with an icon or a word. `InfoBar` carries
  severity for you.
- The type ramp, in Segoe UI Variable: Caption 12, Body 14, Body Strong
  14 semibold, Subtitle 20, Title 28, Title Large 40, Display 68. Use the
  built-in styles (`BodyTextBlockStyle`, `TitleTextBlockStyle`). Nothing
  under 12.
- Sentence case for every label, button, title, and menu item: "New item",
  "Open data folder".
- Dark mode is its own palette, not inverted light mode. Check both in
  every snapshot. UI built in C# must get its colors through a XAML style
  with `{ThemeResource}` setters: a brush read from
  `Application.Current.Resources` in code ignores the panel's theme
  (`csharp-winui`, "UI built in code"). High contrast themes replace every brush: theme brushes
  handle it; hard-coded colors break it.
- Icons from Segoe Fluent Icons through `FontIcon` or `SymbolIcon`, the
  same set Windows uses.

## Interaction

- `ContextFlyout` with a `MenuFlyout` on everything a user can act on,
  with icons on the common commands (Copy, Delete) and the shortcut text.
- Hover states come with the controls. Custom controls need PointerOver,
  Pressed, and Disabled states (`design-kit`).
- Drag and drop in and out where it fits (`CanDragItems`,
  `AllowDrop`), with a real file when dragged to Explorer.
- Relaunch restores the window size, the page, and the selection. Closing
  never asks unless typed work would be lost.
- Undo for every destructive or bulk action. A one-click delete shows an
  `InfoBar` or a toast with Undo. Confirm with a `ContentDialog` only when
  undo is impossible.
- Settings is a page in the navigation pane (the gear at the bottom), laid
  out like Windows Settings: [settings.md](settings.md).
- Respect "Animation effects" off (`UISettings.AnimationsEnabled`) and
  "Transparency effects" off (Mica falls back on its own). Name icon-only
  buttons with `ToolTipService.ToolTip` and `AutomationProperties.Name`.

## When to customize

Custom chrome is fine when it makes the app clearly better at its one job,
like a quick-add field or a calendar grid. Write why in a code comment,
and match the system: `ControlCornerRadius` (4) for controls,
`OverlayCornerRadius` (8) for cards and flyouts, Segoe UI Variable, the
4 epx grid.

## Review set

Snapshot every surface in light and dark: each page, the tray menu, each
flyout and dialog, editors, and Settings (snapshot mode does pages and
Settings). Look for a caret nobody asked for, scroll bars with nothing to
scroll, empty bands under short content, title case labels, and empty
states over data that exists. Capture each window active: snapshot mode
brings it to the front first. Mica looks flat on an inactive window;
that capture does not count as reviewed.

Done when every rule in [design-rules.md](design-rules.md) holds on the
light and dark snapshots of every surface, or has a comment saying why
not.

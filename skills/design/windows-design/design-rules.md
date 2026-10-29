# Design rules

The review checklist. Check every surface against it in light and dark
snapshots. A rule that does not hold gets a fix or a code comment that
says why not.

## Window and navigation

1. The main window has a Mica backdrop. Pages do not paint a background
   over it.
2. The title bar is the `TitleBar` control with the app icon and name,
   extended into the content (`ExtendsContentIntoTitleBar`), and its
   caption buttons follow the app theme (`AppWindow.TitleBar.PreferredTheme`).
3. A window with two to seven pages uses a left `NavigationView`. One page
   uses none. More than seven pages group under expandable items, at most
   two levels.
4. Settings is the gear at the bottom of the pane (`IsSettingsVisible`).
5. Window titles name the content: "Starred - Tasks".
6. The window opens at a size that shows the main content without
   scrolling, centered on the screen the person used last, and remembers
   its size.
7. The minimum size keeps every page usable; nothing clips at it.

## Pages

8. A page starts with its title in `TitleTextBlockStyle`, 36 epx from the
   left edge of the content area.
9. Page commands sit in a `CommandBar` on the title row, right aligned.
10. Content sits on cards over Mica: `CardBackgroundFillColorDefaultBrush`,
    1 epx `CardStrokeColorDefaultBrush`, `OverlayCornerRadius`.
11. Every page has an empty state: a glyph, a one-line title, a line on
    what to do, and the first action as a button.
12. An empty state never shows over data that exists.
13. Long text trims with an ellipsis (`TextTrimming="CharacterEllipsis"`)
    and shows in full in a tooltip.
14. Lists use `ListView` (or `ItemsView`) with virtualization; rows are at
    least 40 epx tall for pointer and touch.

## Commands

15. Every action has a label, an icon from Segoe Fluent Icons, and a
    `KeyboardAccelerator`.
16. At most one `AccentButtonStyle` button per page or dialog.
17. Commands that do not apply are disabled, not hidden. Context menus are
    the exception: they hide what does not apply.
18. Everything a person can act on has a `ContextFlyout`.
19. Delete, arrows, and Enter on a list are scoped to the list with
    `ScopeOwner`.
20. Standard shortcuts keep their Windows meaning.

## Type and words

21. Text uses the built-in styles: Caption 12, Body 14, Body Strong 14,
    Subtitle 20, Title 28, Title Large 40, Display 68. Nothing under 12.
22. Sentence case for every label, title, button, and menu item.
23. Shortcut text is written as Windows writes it: Ctrl+Shift+N.
24. Buttons say what they do ("Delete item"), not "OK", except in a
    dialog that only informs.

## Color

25. No hex colors, fixed grays, white, or black in XAML or code outside
    the token files. Theme brushes only.
26. One accent, read through theme resources (`AccentFillColorDefaultBrush`,
    `AccentTextFillColorPrimaryBrush`).
27. Text on an accent fill uses `TextOnAccentFillColorPrimaryBrush`.
28. Status always has an icon or a word next to its color.
29. Light, dark, and high contrast all read well. High contrast replaces
    every theme brush; hard-coded colors stay and break it.

## Materials and shape

30. Mica on windows, acrylic only on transient surfaces (flyouts, menus,
    a quick panel), solid fills on content.
31. Never a material on a material.
32. Controls use `ControlCornerRadius` (4 epx); cards, flyouts, and dialogs
    use `OverlayCornerRadius` (8 epx). Nested corners are concentric.
33. Spacing is on the 4 epx grid and comes from `UI/Space.xaml`.

## Focus and keyboard

34. No text box has focus when a window or dialog opens, except in a
    window whose only job is typing.
35. Focus rectangles stay on for keyboard users.
36. Tab order follows reading order. Esc closes flyouts, dialogs, and
    panels. Enter runs the default action.
37. A new item focuses its title field.

## Accessibility

38. Every icon-only button has a tooltip and an `AutomationProperties.Name`.
39. Every list row reads as its content in UI Automation (the item's
    `ToString()` or `AutomationProperties.Name`), never a type name.
40. Text scales with Settings > Accessibility > Text size: no fixed
    heights on text containers.
41. "Animation effects" off turns off decorative motion.

## Feedback

42. Every action shows a result in the same frame: the row appears, the
    switch flips, an `InfoBar` explains.
43. Destructive actions have Undo. A `ContentDialog` confirms only what
    cannot be undone.
44. Errors say what happened and what to do next, in the app's words,
    never an exception message.

# Commands

Where every action lives on Windows, and how people find it.

## Surfaces

| Surface | Holds | Control |
| --- | --- | --- |
| Page command bar | The page's main actions: New, Edit, Delete, Share, Sort | `CommandBar` in the page header |
| Context menu | Actions on one item, right-click or Shift+F10 | `ContextFlyout` with `MenuFlyout` |
| Menu bar | Every command, in document apps only | `MenuBar` with File, Edit, View, Help |
| Tray menu | The tray app's actions, Settings, Exit | a native popup menu (`os-integrations`, Tray icon) |
| Title bar | Search, back, the pane toggle | `TitleBar` |
| Jump list | Recent items and "New" from the taskbar | `JumpList` (needs package identity) or the shell API |

## Command bar

- Up to four primary commands with labels on the right
  (`DefaultLabelPosition="Right"`); the rest go in `SecondaryCommands`.
- Order: create first (New), then actions on the selection (Edit, Star),
  then destructive ones (Delete) last.
- Commands on the selection are disabled with nothing selected.
- A toggle command (`AppBarToggleButton`) shows its state; a command whose
  label flips ("Star" and "Unstar") changes its label too.

## Context menus

- On every row, card, and object a person can act on.
- Hide items that do not apply (unlike the command bar, which disables).
- Common commands first, with icons: Open, Copy, Rename, Delete last after
  a separator.
- Show the shortcut text (`KeyboardAcceleratorTextOverride`, or a disabled
  accelerator) so the menu teaches it.
- Windows 11 puts Cut, Copy, Paste, Rename, Share, and Delete as an icon
  row at the top of Explorer's menu. Do this only for file-like items.

## Shortcuts

| Keys | Meaning on Windows |
| --- | --- |
| Ctrl+N | New item or window |
| Ctrl+O, Ctrl+S, Ctrl+Shift+S | Open, Save, Save as |
| Ctrl+Z, Ctrl+Y | Undo, Redo (Ctrl+Shift+Z also redoes in many apps) |
| Ctrl+X, Ctrl+C, Ctrl+V | Cut, Copy, Paste |
| Ctrl+A | Select all |
| Ctrl+F | Find in the page |
| Ctrl+P | Print |
| Ctrl+W, Ctrl+F4 | Close the tab or document |
| Alt+F4 | Close the window (the system handles it) |
| F2 | Rename |
| F5 | Refresh |
| Delete | Delete the selection |
| Enter | Open or run the default action |
| Esc | Cancel, close the flyout or dialog |
| Ctrl+Tab, Ctrl+Shift+Tab | Next and previous tab |
| Alt, then a letter | Access keys |
| Shift+F10, the Menu key | Context menu (WinUI does it for `ContextFlyout`) |

Global shortcuts (a quick panel from any app) use Ctrl+Alt or Win+Alt with
a letter, never a combo Windows owns: Win+V (clipboard), Win+Shift+S
(screenshot), Win+arrows (snap), Win+period (emoji), Alt+Tab. See
`os-integrations` for `RegisterHotKey` and what happens when another app
already owns the combo.

## Tooltips

WinUI adds the shortcut to a button's tooltip when it has a
`KeyboardAccelerator` and no tooltip of its own. Set your own tooltip only
for icon-only buttons, and include the shortcut: "New task (Ctrl+N)".

## Check

The template's self-test walks the visual tree and fails on any
`AppBarButton` without a label or a shortcut. `test-windows-app` has a UI
Automation script that lists every command with its shortcut from
outside, the way Narrator sees it.

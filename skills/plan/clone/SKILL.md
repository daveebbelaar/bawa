---
name: clone
description: Walk through any existing app (native Windows app with computer use, web app with a browser), screenshot every screen, menu, and dialog, write a screen index, and turn it into user stories. Use when the user wants to clone, rebuild, replace, or copy the design of an app, or says "screenshot every screen".
argument-hint: "<app process name or window title, or a URL>"
---

# Clone

Output: a folder of screenshots, one index that describes everything the
app shows, and a backlog built from it.

```text
docs/screens/
  INDEX.md
  01-inbox.png
  02-today.png
  ...
docs/STORIES.md      (from the user-stories skill)
```

## 1. Pick the tool

| The app is | Use |
| --- | --- |
| A native Windows app (WinUI, WPF, WinForms, Win32), or Electron | Computer use in the Claude desktop app |
| A web app | A browser tool: the built-in browser, Claude in Chrome, or Playwright |

Turn on computer use in the Claude desktop app's settings. Ask for access
to the reference app only. Browsers are view-only and terminals are
click-only for computer use.

## 2. Agree on privacy

Tell the user you will see everything in the app. Suggest a demo account or
sample data. Look, do not touch: open screens and menus, press Esc to
close dialogs. Type, create, change, or delete only when the user says so.

## 3. Save real files

Computer use screenshots come to you downscaled, not to disk, and they show
the whole desktop. Save each screen at full resolution, and only the app's
window, with the script in this skill:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File "${CLAUDE_SKILL_DIR}/scripts/window-shot.ps1" "<process or title>" docs/screens/01-inbox.png
```

It captures the largest visible window whose process name (without
`.exe`) or title matches, with `PrintWindow`, so a window behind another
still captures as it looks. `-List` prints every visible window with its
process name, size, and title: run it first when you are unsure of the
name. Never take a full-screen capture: it shows the user's other windows.

A menu, flyout, or tooltip that opens outside the window is not in the
capture. Describe it in the index from the computer use screenshot and mark
the section "no file". In a browser, use the tool's screenshot with a file
path.

## 4. Walk the app in this order

1. Main window, default page.
2. Every navigation item (the left pane or sidebar), top to bottom.
   Expand collapsed groups.
3. Every view mode (list, board, calendar), sort menu, filter menu.
4. One item opened in detail: pane, fields, sub-items.
5. Creation flows: add button, quick-add window (global shortcut), inline
   editor. Empty and half filled in, then Esc.
6. Context menus (right-click, or Shift+F10) on an item, a section, a
   navigation entry.
7. The menu bar or command bar: every menu once, and the "See more" (...)
   overflow. Press Alt to show access keys. Record every item, shortcut,
   and access key.
8. Settings: every page.
9. The tray icon in the notification area: its flyout and its right-click
   menu, if the app has one.
10. Empty states.
11. Dark mode: the two or three main pages again. Use the app's own theme
    setting. If it only follows Windows, ask the user to switch Settings >
    Personalization > Colors > Choose your mode.

Name files `NN-page-or-state.png` in capture order. The walk is
exhaustive when every navigation item and settings page has a file, and
every menu has an index section.

## Lessons from real walkthroughs

- Many "Windows apps" are Electron. Their menu bars are nearly empty or
  hidden, and the actions hide in hover toolbars, right-click menus, and
  command palettes (often Ctrl+K or Ctrl+Shift+P). Hover rows and search
  the palettes (typing in a palette search is safe; do not press Enter).
- Store apps such as Calculator draw inside `ApplicationFrameHost`, so
  their process name does not match. Match them by window title; `-List`
  shows it.
- A minimized window cannot be captured. Restore it first. Some windows
  (video players, protected content, some games) render black or blank:
  the script warns when the capture is one flat color.
- Preview fields test behavior without saving. A "Type a date" box in a
  to-do app shows how it parses a phrase before anything is stored. 30
  phrases there corrected 3 answers from the docs.
- Note any change in the user's data during the walk, even if you are not
  sure you caused it, and tell them.

## 5. Write docs/screens/INDEX.md

One section per screenshot:

```markdown
## 03-upcoming.png

Page: Upcoming. Reached from: navigation pane > Upcoming.
Layout: header with month picker and "Today" button; week strip; tasks
grouped by day, each day a section with a heading and an add button.
Elements: task row (checkbox, title, description line, date, project tag,
priority flag color).
Actions: complete, open, drag to another day, add to a day.
Shortcuts seen: none.
```

End with three lists:

- **Navigation map**: navigation items and where each leads.
- **Command map**: every menu and command bar item, its keyboard shortcut,
  and its access key.
- **Data model guess**: the nouns and fields you saw.

Describe structure and behavior. Take no text, logo, or icon from the app:
rebuilding for personal use is fine, copying a brand is not. Keep the PNGs
out of public repos (add `docs/screens/*.png` to `.gitignore`).

## 6. Turn it into stories

Call the Skill tool with `user-stories`. Every screen in the index ends up
linked from at least one story, or listed under `## Dropped`.

Done when INDEX.md has a section per PNG and per "no file" screen, the
three maps exist, and `docs/STORIES.md` covers every screen.

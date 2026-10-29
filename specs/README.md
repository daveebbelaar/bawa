# Specs

Build plans for small Windows apps you can build today with BAWA. Each spec
is one app. Give it to your agent and it builds the app for you.

These are the Windows versions of BAMA's specs, which I took apart from
Onyx, my own Mac menu bar app. Each spec keeps the same jobs and stories,
rewritten for Windows: its APIs, its shortcuts, its traps. Each one is cut
down to an app you can build in an afternoon of agent time.

## Build one

Install BAWA, copy a spec into an empty folder, open your agent there, and
tell it:

```text
Build the app in window-snapper.md with BAWA.
```

A link works too:
`https://github.com/daveebbelaar/bawa/blob/main/specs/window-snapper.md`.

The agent copies the spec's **Brief** into `docs\BRIEF.md` and its
**Stories** into `docs\STORIES.md`, then runs `new-windows-app` and
`improve-loop`. Change the name, the shortcuts, or the scope first if you
want. A spec is a starting point, not a contract.

## The specs

| Spec | What it does | Replaces |
| --- | --- | --- |
| [window-snapper.md](window-snapper.md) | Snap the front window to halves, wide sides, or full screen with a shortcut | Win+arrow cycling, FancyZones for simple layouts |
| [window-switcher.md](window-switcher.md) | Press a shortcut and pick any window from live previews | Alt+Tab, Task View |
| [launcher.md](launcher.md) | Open apps and files from one search box | PowerToys Run, Start search |
| [clipboard-history.md](clipboard-history.md) | Search and paste anything you copied | Win+V, Ditto |
| [text-snippets.md](text-snippets.md) | Type a short keyword and get the full text | PhraseExpress, AutoHotkey hotstrings |
| [color-picker.md](color-picker.md) | Pick a color anywhere on screen and save it | PowerToys Color Picker |
| [screenshots.md](screenshots.md) | Capture the screen, an area, or a window, then mark it up | Snipping Tool, ShareX, Greenshot |
| [screen-recorder.md](screen-recorder.md) | Record the screen to MP4 with mic, system audio, and a webcam bubble | Snipping Tool video, OBS, Loom |
| [tray-stats.md](tray-stats.md) | CPU, memory, network, disk, and battery in the notification area | Task Manager, TrafficMonitor |
| [toolbox.md](toolbox.md) | Keep the PC awake, show hidden files, switch audio devices | PowerToys Awake, Winaero Tweaker, SoundSwitch |
| [creator-tools.md](creator-tools.md) | Keystroke overlay, cursor highlight, teleprompter, chapter markers | Carnac, PowerToys Mouse Highlighter |
| [dev-toolkit.md](dev-toolkit.md) | Open projects, transform text (JSON, Base64, JWT), free a port | DevToys, `netstat` then `taskkill` |

Start with the window snapper. It's the smallest, and it teaches the two
things most of these apps need: a global shortcut and a tray icon. It is
also the one BAWA has already built end to end on a real PC: see
[../examples/Snap](../examples/Snap).

## What a spec holds

Every spec has the same parts, in this order:

1. **Brief**: the `app-brief` format, filled in. The agent skips the
   interview.
2. **How it works**: the Windows APIs, the permission or privacy setting
   each one touches, and the main types.
3. **Stories**: the `user-stories` format, in milestones. Every acceptance
   line says how to check it.
4. **Gotchas**: what goes wrong on Windows, so your agent gets it right the
   first time.
5. **Done when**: the check that the app is finished.

## Write your own

Copy a spec that's close to your idea and change it. Keep the five parts.
Keep version one small: one shape (tray icon, panel, or window), two to
five jobs, and 5 to 10 stories in the first milestone.

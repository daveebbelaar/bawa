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

## Build several specs as one app

The specs also combine into one tray app. Onyx for Windows holds all
twelve, and six agents built its features side by side. The pattern:

- **One tray host with a feature contract.** The host owns the single
  tray icon and its flyout, Settings, the shortcut table, and the test and
  snapshot modes. Each spec becomes a feature with an id, actions, a
  Settings page, a flyout block, `Start` and `Stop` (Stop undoes all of
  Start), self-tests, and snapshot surfaces.
- **One shared keyboard hook.** The switcher, snippets, and the keystroke
  overlay subscribe to one `WH_KEYBOARD_LL` hook in the host. The event
  carries the injected flag and `dwExtraInfo`, so each feature can tell its
  own keys from other synthetic input and filter for itself.
- **Default shortcuts that never collide.** Every default comes from one
  table, and two actions never share one:

  | Spec | Default |
  | --- | --- |
  | window-snapper | Ctrl+Alt+Left, Right, Up, Down, Enter, Backspace; Ctrl+Alt+Shift+Left, Right |
  | window-switcher | Alt+` through the keyboard hook |
  | launcher | Alt+Space |
  | clipboard-history | Win+Alt+V |
  | color-picker | Ctrl+Alt+Shift+C |
  | screenshots | Ctrl+Alt+Shift+3, 4, 5 |
  | text-snippets | none (keywords through the keyboard hook) |
  | screen-recorder, toolbox, creator-tools, tray-stats, dev-toolkit | none; the person sets them |

  Write the Windows key first: Win+Alt+V, not Alt+Win+V.
- **One agent per feature, in its own git worktree.** Each agent edits only
  its feature's folders and writes notes on what the spec got wrong. Merge
  one feature at a time. Give no feature a namespace named `Windows` or
  after a WinRT type (`Clipboard`, `Colors`, `Launcher`): it shadows that
  name in every other feature, and only the merge shows it.

Parallel test runs share one foreground and one screen, so a self-test uses
its own test windows, fakes the clipboard, and never adds a tray icon.
Where an agent hit one of these, the spec's acceptance lines say how. The
full guide: [suite.md](../skills/start/new-windows-app/suite.md).

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

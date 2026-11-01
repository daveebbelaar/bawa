---
name: build-and-verify
description: Build a WinUI 3 app from the command line, install it, and check light and dark window snapshots of every page before calling a change done. Use after any UI code change, when a build fails, or when the user asks to run, screenshot, or check the app.
allowed-tools: PowerShell(./build.ps1*) PowerShell(tools/*) PowerShell(dotnet *) Bash(dotnet *) Read
---

# Build and verify

A change is done when four things are true:

1. `./build.ps1` finishes with no errors and no warnings.
2. You looked at the light and dark snapshots of every page you touched.
3. The snapshots match what the user story or reference screenshot asks for.
4. The grid checks pass (`layout-grid` skill).

App not made from the `new-windows-app` template (a Visual Studio
solution, MSIX, WPF, WinForms)? Map the commands below with
[existing-app.md](existing-app.md) first.

Do not report a UI change as done from the code alone. XAML that compiles
can still crash at launch, render an empty list, clip text, or be
unreadable in dark mode.

## 1. Build

```powershell
./build.ps1          # release build, installs %LOCALAPPDATA%\Programs\<AppName>
./build.ps1 -Dev     # debug build, faster while iterating
./build.ps1 -Open    # also starts the new build so the user sees it
```

Read the errors, fix them, and build again. Fix the first error first:
later errors often come from it. `build.ps1` closes a running copy first,
because Windows locks the files of a running app.

## 2. Snapshot

```powershell
tools/snap.ps1
# SNAPSHOT C:\Users\<you>\AppData\Local\Temp\<AppName>-shots-<checkout>\all-light.png
# ...
# SNAPSHOT done C:\Users\<you>\AppData\Local\Temp\<AppName>-shots-<checkout>
```

The app opens on a fresh test store with sample data (`APP_SEED=1`),
switches to every navigation page and Settings in light and dark, saves one
PNG per page and mode, and quits. Each PNG gets a `-layout.json` for the
grid check.

`tools/snap.ps1 -Pages all,settings` limits the pages. `-Accent green`
shows another accent. `-Grid` draws the grid and the probes.

In every image, look for three bugs agents often miss: a text box with a
caret that nobody asked for, a scroll bar with nothing to scroll, and an
empty band under short content. The fixes are in `windows-design`,
"Windows open calm".

Always use `APP_STORE` with a path in `%TEMP%`. Never take snapshots
against the user's real database in `%LOCALAPPDATA%\<AppName>\`.

### How the capture works

Snapshot mode calls `PrintWindow` with `PW_RENDERFULLCONTENT` on the app's
own window, so Mica, the title bar, and every control look as on screen,
even when another window covers it. It crops the invisible resize border
with `DWMWA_EXTENDED_FRAME_BOUNDS`. No permission is needed. Before each
capture it brings the window to the front, so the title bar is active and
Mica is full strength. Never take full-screen captures to check your app:
they show the user's other windows.

### Windows that hide from captures

A window with `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` (a
recording capsule, a toast that must stay out of a video) comes out solid
black in `PrintWindow` too. In snapshot mode, set it to `WDA_NONE` before
the capture and back after.

A page taller than the window shows only its first screen. Add a
snapshot surface for the lower part (scroll the page first) when it
matters.

### Seed data

Empty pages only show empty states. `SeedData.cs` fills an empty test
store when `APP_SEED=1` is set. Grow it with the app: 10 to 20 items with
long and short titles, dates in the past and future, every priority and
status, and only what a person can create in the app.

### Missing snapshots

- `CRASH ...` in the output: the app died; the line names the exception.
  Fix it before anything else.
- `TIMEOUT`: the run hung, usually on an open `ContentDialog` or an
  `await` that never returns. The tool kills it after 180 s.
- An exit code like `0xC000027B` with no `CRASH` line: the crash came
  before the log was ready, in the `App` constructor. Look in Event Viewer
  > Windows Logs > Application for the faulting module.
- A black or blank PNG: the display was off. Snapshot mode keeps it on
  with `SetThreadExecutionState`; a locked PC still renders nothing. Unlock
  it and run again.

### Display scale

PNGs are in physical pixels, so at 125% a 1100 epx window is 1375 pixels
wide. That is expected. Compare layouts in epx (the layout files), not in
PNG pixels.

## 3. Check the grid

```powershell
tools/grid-lint.ps1
tools/check-layout.ps1
```

Both print PASS. On a FAIL, call the Skill tool with `layout-grid`: it has
the rules, the overlay, and a table of fixes.

## 4. Look

Open every PNG with the Read tool and check, in this order:

- Nothing is blank, clipped, or overlapping. Text fits or ends in an
  ellipsis with a tooltip. The navigation pane shows its items.
- Dark mode is readable. No fixed white or black surfaces.
- The page matches the story or the reference screenshot (layout, labels,
  order).
- The `windows-design` rules hold: Mica on the window, cards for content,
  one accent, Segoe Fluent Icons, the type ramp, sentence case, empty
  states that invite the first action.
- The data is right: a page that should list items lists them. An empty
  state over seeded data is a bug, not a style issue.

Describe what you see in one or two sentences per page in your report,
and name the PNG paths so the user can open them.

## 5. Interaction checks

Snapshots show state, not behavior. Run `tools/selftest.ps1` (exit 0 when
every check passes) and add a check for each new action. The
`test-windows-app` skill covers unit tests, the self-test, keyboard
accelerators, UI Automation from outside, and a debug bridge.

### Debug output

A WinUI app has no dependable stdout: a redirected handle often stays
empty, and a pipe can keep the reader waiting after the app quits. Write
agent output with `AgentLog.Line`, which also appends to the file in
`APP_LOG`. `tools/common.ps1` sets it and prints the file.

## 6. Commit

When the build is clean and the snapshots look right, commit with a
message that says what the user can now do: "Add tasks from the quick-add
panel", not "Update views".

Done when the four conditions at the top hold and the change is committed.

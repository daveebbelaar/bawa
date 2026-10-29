---
name: test-windows-app
description: Test a Windows app at four levels, from xUnit tests and an in-app self-test to UI Automation from outside and a debug bridge that lets an agent drive the running app, plus computer use for real clicks. Use when adding logic, fixing a bug, before a release, or when the user asks for tests, end-to-end checks, or "make sure it works".
---

# Test a Windows app

Four levels, cheapest first. Each new story gets the cheapest level that
can prove its acceptance criteria. A bug fix starts with a test that fails.

| Level | Proves | Speed |
| --- | --- | --- |
| Unit tests (xUnit) | Rules, parsing, data, the SQLite store | Seconds |
| Self-test (`--selftest`) | The app's real functions on a test store, every command has a shortcut | Seconds |
| UI Automation from outside (`scripts/uia.ps1`) | What Narrator reads, enabled states, buttons and rows that really work | Seconds |
| Debug bridge + computer use | Global shortcuts, tray, other apps, timing | Minutes |

Snapshots (`build-and-verify` skill) prove how it looks. This skill proves
how it behaves.

## 1. Unit tests

Logic lives in `<App>.Core` with no WinUI, so tests run without the app.

```csharp
public class DateParserTests
{
    [Theory]
    [InlineData("tomorrow 9am", 1, 9)]
    [InlineData("next fri", 5, null)]
    public void Parses(string phrase, int dayOffset, int? hour)
    {
        var result = DateParser.Parse(phrase, now: Fixed.Now);
        Assert.NotNull(result);
        Assert.Equal(dayOffset, result.DayOffset);
        Assert.Equal(hour, result.Hour);
    }
}
```

```powershell
dotnet test tests/<App>.Core.Tests
```

- Fix the clock (a `now:` parameter) and the store path in every test.
  Store tests get their own file in `%TEMP%` and delete it, with its
  `-wal` and `-shm` files, in `Dispose`.
- A parser that removes text from what the user typed needs at least 20
  negative rows: real titles that contain date or time words ("Watch
  Friday Night Lights"). Add consistency rows for related phrases ("next
  week" and "next friday"), and a tested way to undo a parsed part.
- **Table tests** (`[Theory]` with many `[InlineData]` rows) for anything
  with many cases. Keep the table in `docs/research/<topic>.md` too, and
  add a row there before changing behavior.
- What Narrator reads for a list row is the item's `ToString()`. Test it:
  a type name there ("Tasks.Core.Item") is a screen reader bug.

## 2. Self-test

A launch argument that runs the same functions the UI calls, prints PASS
or FAIL per step, and exits 0 or 1:

```powershell
tools/selftest.ps1
# PASS the first page shows the stored items at launch
# PASS New item adds one item
# PASS command 'New item' has a label and a shortcut
# SELFTEST PASS 13 checks
```

It runs on a fresh seeded store in `%TEMP%`. Never on the user's data.
Output goes through `AgentLog.Line`, so it reaches the tool even though a
WinUI app has no dependable stdout.

Checks worth adding to any self-test:

- The page open at launch shows what the store holds. A model that only
  fills its list on a page change shows an empty state at launch.
- For every page: New item creates a row that is visible and selected on
  that page. After an action removes the selected row, selection moves to
  the next row.
- Every `AppBarButton` and `MenuFlyoutItem` has a label and a
  `KeyboardAccelerator` (the template walks the visual tree for this).
- Every glyph renders: a `FontIcon` with an unknown code point shows an
  empty box, so keep glyphs in one place and check them against a list.
- Every URL protocol action round-trips.
- **Smoke step**: start every timer, hook, and watcher for a few seconds,
  then print PASS. It is the only check that catches crashes on background
  threads.

### Runs that never hang

- `tools/common.ps1` kills a run after 180 s and prints `TIMEOUT`.
- Quit with `AgentLog.Exit(code)`. An open `ContentDialog` blocks nothing
  at exit, but an `await` on one that never closes does: close dialogs
  before the last check.
- Read the process exit code only after touching `$process.Handle` once.
  Without it, `Start-Process -PassThru` reports an empty exit code.
- Delete the old test store and its `-wal` and `-shm` files before a
  seeded run. Seed data only fills an empty store.

### Key presses

The self-test can run an accelerator's action directly, but to prove the
key itself works, send it from outside (level 3 or 4). Do not use
`SendInput` from the app to itself: it goes to whatever window has focus,
which is often the agent's terminal.

For a global shortcut, post the message the key would cause:
`PostMessage(trayWindow, WM_HOTKEY, id, 0)`, with the action aimed at a
test window the self-test opened. That proves registration, dispatch, and
the action. An agent's sandboxed shell often cannot inject real key
presses or even find other processes' windows, so the physical key press
stays a `[manual]` line. A self-test must never act on the window in
front: that is the person's window.

### Self-tests that never touch the person's PC

A self-test runs the real code on a real desktop. These patterns kept
twelve features' tests safe in Onyx:

- **Test windows of your own**, opened and closed by the check, for
  anything that moves, captures, types into, or reads a window. Paint
  known colors in them and assert exact pixels. Put them in a corner, not
  the center, and turn off their transitions (a read mid-fade gave
  `#FE3B30` for `#FF3B30`).
- **Seams for the clipboard.** Reads and writes go through a small
  interface the self-test swaps for a fake; post `WM_CLIPBOARDUPDATE` to
  the feature's own listener so the real message path still runs. Never
  write and restore: a restore is never exact, and every write lands in
  Win+V history.
- **A test registry key** (`HKCU\Software\<App>SelfTest`) for anything
  that writes settings, then read the real value only, and compare it
  before and after the run.
- **Never full-screen captures**, never the real tray (Windows keeps a
  permanent `NotifyIconSettings` entry for every exe path that adds an
  icon), never the microphone, never a process you did not start.
- **Aim an area** instead: give capture code a seam that takes a
  rectangle, and point it at your test window.
- **Send input only to your own window, and check first.** Right before
  every `SendInput`, check that your test window is in front; otherwise
  send nothing, wait, and retry. Mark your own input (`dwExtraInfo`) so
  your hooks can tell it apart.

### Several agents on one desktop

When agents build in parallel (one per feature, each in its own git
worktree), their test windows fight for the foreground. Checks that read
"which window is in front" fail at random, while everything else passes.

- Read the front window right after the action, not later.
- Turn off "hide on deactivate" for a panel during its checks.
- Treat a lone foreground failure while other agents run as unproven, and
  run the full self-test again once the desktop is quiet before you call
  it a bug. In Onyx, two launcher checks failed with six agents running
  and passed alone.
- Give each checkout its own build folder, test store, and snapshot
  folder (the template names them after the checkout folder), and run
  the build with `tools/dev.ps1`, which never installs.

## 3. UI Automation from outside

Windows exposes every control to screen readers through UI Automation.
`scripts/uia.ps1` uses the same tree to read and drive the running app,
with no change to the app:

```powershell
$u = "${CLAUDE_SKILL_DIR}/scripts/uia.ps1"
& $u -Process Tasks                    # every named control: type, enabled, name, shortcut
& $u -Process Tasks -Invoke Delete     # FAIL 'Delete' is disabled (nothing selected)
& $u -Process Tasks -Select Groceries  # PASS selected 'Groceries'
& $u -Process Tasks -Invoke Delete     # PASS invoked 'Delete'
```

Run it on a seeded test copy started with `APP_STORE` in `%TEMP%`. The
listing is also an accessibility review: every button has a name and its
shortcut in brackets, and every row reads as its content.

Checks to write into the app's `CLAUDE.md`, so every agent runs them
after touching commands:

1. Nothing selected: Delete is reported disabled.
2. Select a row, invoke Delete: the row is gone and the next row is
   selected.
3. Every command in the listing shows its shortcut.

## 4. Debug bridge

For behavior the other levels cannot reach: global shortcuts, the tray
icon, moving other apps' windows, timing. The running app listens on a
named pipe for commands and answers with its state. Code shape:
[../../code/os-integrations/recipes.md](../../code/os-integrations/recipes.md#debug-bridge).

```powershell
tools/ctl.ps1 status            # permissions, flags, open windows, counts, last error
tools/ctl.ps1 snapLeft          # runs an action by name
tools/ctl.ps1 selftest          # the self-test inside an app that is already running
```

Commands worth adding:

- `status`: feature flags, registered hotkeys and the ones that failed,
  open windows, counts, last error.
- One command per action, by the same name as the command and the URL.
- `snapshot:<window>`: captures one of the app's windows with
  `PrintWindow`.
- `theme:light|dark|system`: switches the app theme for snapshots.
- `selftest`: the self-test as a command, for a single-instance app that
  is already running.
- `<feature>Probe`: runs a slow path and logs the time of each step (see
  the `performance` skill).

Compile the bridge into debug builds, or behind a launch flag in release.

## 5. Computer use

Last resort, for what only a person can do: a real key press in another
app, drag and drop from Explorer, a UAC prompt. Tell the user to keep off
the mouse while it runs. Capture your own window, never the full screen.

Done when every acceptance line of the story maps to a passing test,
self-test step, UI Automation check, bridge command, or a named manual
check, and the bug fix has a test that failed before the fix.

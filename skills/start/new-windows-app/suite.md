# Suites: several specs as one app

A suite is one tray app made of features, like PowerToys: a window
snapper, a launcher, clipboard history, and more, behind one tray icon,
one Settings window, and one set of shortcuts. Onyx for Windows was built
this way from all twelve of BAWA's specs: a shell first, then one agent
per feature in parallel, then a merge. This page is what worked.

## 1. Build the shell first, by hand

The shell is the part every feature shares. Build it from the template
before any feature, with one real feature in it as the worked example.

| Shell part | Why it is shared |
| --- | --- |
| One tray icon and a hidden window that receives `WM_HOTKEY` and tray clicks | Twelve tray icons is a mess; hotkeys need a window that exists (a handle of 0 "succeeds" and nothing ever arrives) |
| A hotkey registry keyed by action id (`windows.left`) | One place registers, reports failures, and suspends every hotkey while a shortcut is recorded |
| One low-level keyboard hook on its own thread, with subscribers | Windows drops a hook that takes over a second; ten hooks in one process is ten chances. Pass `Extended` and the injected flag in each event |
| A foreground tracker | Every feature acts on "the window you were using", which is never your own panel or the taskbar |
| A tray flyout with one block per feature | Windows 11's Quick Settings shape: the place for toggles, recent items, and live numbers |
| Settings: General, Features (on/off per feature), Shortcuts (every action), then one page per feature | Turning a feature off stops its shortcuts, its page, and its background work, and deletes nothing |
| `FeatureSettings<T>` (one JSON file per feature) and a data folder that is in `%TEMP%` during test runs | Features never edit a shared settings class, so parallel agents never conflict |
| An overlay window helper for panels | Borderless, topmost, out of Alt+Tab, hides on deactivate |
| Self-test and snapshot runners that walk every feature | A new feature gets checked and photographed with no shell change |

The feature contract (C#):

```csharp
public sealed record AppAction(string Id, string Title, int Glyph, KeyCombo? DefaultCombo,
    Func<Task> Run, Func<bool>? IsOn = null);
public sealed record SelfTestCheck(string Name, Func<Task<(bool Ok, string Detail)>> Run);
public sealed record SnapshotSurface(string Name, Func<Task<Window?>> Open, Action Close);

public interface IFeature
{
    string Id { get; }            // lower case, one word: folder name, settings file, action prefix
    string Title { get; }
    int Glyph { get; }            // Segoe Fluent Icons code point
    string Summary { get; }
    string? Replaces { get; }
    int Order { get; }
    IReadOnlyList<AppAction> Actions { get; }   // build once, cache
    Type? SettingsPage { get; }                  // gets the feature as the navigation parameter
    UIElement? CreateFlyoutBlock();
    void Start();                  // hooks, timers, watchers
    void Stop();                   // undo all of Start
    Task StopAsync();              // on quit: finish what must not be lost (an MP4's index)
    IEnumerable<SelfTestCheck> SelfTests();
    IEnumerable<SnapshotSurface> SnapshotSurfaces();
}
```

Find features by reflection, so adding one touches no shell file. Let a
`APP_FEATURES=a,b` variable load only some, so a test run of one feature
starts nothing else.

Shell self-test checks that pay off: ids are unique and lower case; every
action id starts with its feature id; titles are sentence case; **no two
actions share a default shortcut** (two specs both wanted Alt+Space);
the tray window exists; every bound shortcut is registered or reported;
turning a feature off and on removes and restores its shortcuts and page.

Check: the shell with one feature passes its self-test and snapshots.

## 2. Write the feature guide

One page (`docs/FEATURES.md`) every feature agent reads first:

- What the shell gives you, as a table (need, then API).
- The contract, and the files a feature owns: its folder in the app, its
  folder in Core, its tests, its notes, and a `Feature.props` for its
  NuGet packages (imported by a wildcard, so nobody edits the csproj).
- A table of reserved default shortcuts.
- Safety rules for self-tests (`test-windows-app`, "Self-tests that never
  touch the person's PC").
- The exact commands to check work, quoted for PowerShell.
- "Notes for BAWA": every wrong or missing thing in the skills, the specs,
  or the shell, with the fix. This is how the skills get better.

## 3. One agent per feature, in its own worktree

```powershell
git worktree add ..\<App>-wt\clipboard -b feature/clipboard
```

The template names its build folder, test store, and snapshot folder after
the checkout folder, so worktrees never share them. Agents build and test
with `tools/dev.ps1`, which installs nothing. Give each agent: its
checkout, its spec, the guide, the skills it needs, and the rule to commit
on its branch when its checks pass. Two features per agent works when they
share a shape (switcher and launcher are both panels).

Expect foreground checks to fail at random while several agents run test
windows at once. Rerun them on a quiet desktop before calling them bugs.

## 4. Merge one at a time

Merge each branch as it finishes, build, and run the self-test with that
feature loaded. Problems only a merge shows:

- **Namespace shadowing.** `MyApp.Features.Clipboard` hides the WinRT
  `Clipboard` class in every other feature; `MyApp.Features.Windows` hides
  `Windows.*`. Each branch compiled alone. Name namespaces so they never
  match a framework name (`csharp-winui`, "Names that collide").
- **Shared helpers in one feature's folder.** Colors needed Screenshots'
  screen freeze; Snippets needed Clipboard's writer. Move such code into
  the shell after the merge.
- **Shell fixes from the notes.** Fix the shell on the main branch as notes
  come in (theme-aware styles, async stop, extra hook fields); later
  branches pick them up at merge, since features never edit the shell.

## 5. Check the whole

On a quiet desktop: the full self-test, every unit test, every snapshot in
light and dark, both grid checks. The suite this guide came from, at this point: 12
features, 170 self-test checks, 439 unit tests, 88 snapshots, all passing,
built by six agents in about 40 minutes of wall time.

Then fold the notes into the skills and the specs, so the next suite
starts where this one ended.

Done when the merged app passes its full self-test on a quiet desktop,
every feature's notes are folded into BAWA or listed as open, and the
worktrees are removed.

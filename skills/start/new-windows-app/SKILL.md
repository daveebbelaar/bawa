---
name: new-windows-app
description: Create a native Windows app in WinUI 3 from a working template (C# and .NET 10, SQLite, build script, icon script, snapshot mode, self-test, git), with no Visual Studio. Use when the user wants to start a new Windows app or scaffold a WinUI project.
argument-hint: "<AppName> [parent-folder]"
allowed-tools: Bash(*/new-app.ps1 *) PowerShell(*/new-app.ps1 *) Bash(git *) Bash(dotnet *) Read Write Edit
---

# New Windows app

The template builds and runs as is. The first build proves the toolchain
works before you write feature code.

## What the template is

A .NET solution with a WinUI 3 app, a core library, and tests, plus a
script that installs the app like a real program. An agent can read and
edit every file. It builds with the .NET SDK alone.

```text
<AppName>/
  <AppName>.slnx            the solution
  Directory.Build.props     build output in %LOCALAPPDATA%\Dev\<AppName>, outside the repo and OneDrive
  build.ps1                 dotnet build -> %LOCALAPPDATA%\Programs\<AppName> + Start menu entry
  CLAUDE.md                 build, check, and design rules for agents
  Icon/AppIcon.svg          the app icon, rendered to .ico and .png by tools/make-icon.ps1
  src/<AppName>/
    App.xaml(.cs)           agent flags, accent, first window
    MainWindow.xaml(.cs)    Mica, TitleBar, NavigationView, page switching
    AppModel.cs             pages, selection, add and delete
    AppPaths.cs             where the database and settings.json live
    Pages/ItemsPage         list, detail, empty states, command bar with shortcuts
    Pages/SettingsPage      SettingsCards: app theme, accent, about
    Snapshot.cs             APP_SNAPSHOT mode: PNG and layout file per page, light and dark
    SelfTest.cs             --selftest: the app's real functions, PASS or FAIL per check
    SeedData.cs             APP_SEED=1: sample items for snapshots
    AgentLog.cs             agent output to the file in APP_LOG
    UI/Space.xaml           the 4 epx spacing scale for XAML
    UI/Theme.cs             accent choices, app theme, accent shades
    UI/Grid.cs              Space for code, layout probes, grid overlay
    UI/WindowBehavior.cs    DPI-correct size, center, minimum size
    UI/Win32.cs             the few Win32 calls WinUI lacks
  src/<AppName>.Core/       Item, ItemTitle, ItemStore (SQLite with migrations)
  tests/<AppName>.Core.Tests/  xUnit tests for the core
  tools/
    common.ps1              Invoke-App: runs the app with flags, prints APP_LOG, never hangs
    snap.ps1                snapshots on a fresh seeded store
    selftest.ps1            the self-test on a fresh seeded store
    grid-lint.ps1           flags spacing numbers off the 4 epx grid
    check-layout.ps1        checks the layout files from snapshot mode
    make-icon.ps1, IconTool/  SVG -> .ico (16 to 256 px) and a review sheet
```

## Steps

1. Check the toolchain:

   ```powershell
   dotnet --list-sdks     # 10.0 or later
   winver                 # Windows 11, or Windows 10 19041 or later
   ```

   No SDK? `winget install Microsoft.DotNet.SDK.10`, then open a new
   terminal so `dotnet` is on the PATH. Nothing else: no Visual Studio, no
   Windows App SDK installer. The app carries its runtime.

   Every tool in the template is a PowerShell script. Run them from
   PowerShell. From Git Bash, call them as
   `powershell -NoProfile -ExecutionPolicy Bypass -File ./build.ps1`. If
   PowerShell says "running scripts is disabled", the PC still has
   Windows' default policy: ask the user to run
   `Set-ExecutionPolicy -Scope CurrentUser RemoteSigned` once. Do not change
   it yourself.

2. Pick the name: one UpperCamelCase word. If `docs/BRIEF.md` exists, use
   its name.

3. Run the script with the parent folder. It copies the template, fills in
   the name, runs `git init`, and commits. It keeps a folder that holds
   only a brief.

   ```powershell
   & "${CLAUDE_SKILL_DIR}/scripts/new-app.ps1" -Name Tasks -Parent $HOME\Developer
   ```

   Keep the project out of a OneDrive folder if you can. The template
   already builds outside it.

4. Build the app, then open it:

   ```powershell
   cd $HOME\Developer\Tasks; ./build.ps1 -Open
   ```

   The first build restores packages and takes a minute or two.

5. Run the checks the template ships with, then look at the snapshots
   (`build-and-verify` skill):

   ```powershell
   dotnet test tests/Tasks.Core.Tests     # unit tests
   tools/selftest.ps1                     # PASS per check, exit 0
   tools/snap.ps1                         # sample data, light and dark PNGs
   ```

6. If `docs/BRIEF.md` exists, rename `Item` to the brief's main type and the
   `SidebarPage` cases to the brief's pages. Nothing else: features go
   through `improve-loop`, one story at a time. Build, snapshot, commit.

## Why unpackaged

A packaged (MSIX) app needs a signing certificate and an install step for
every run (Microsoft's `winapp` CLI can do both from the command line now).
An unpackaged app is an `.exe` in a folder, which an agent can rebuild and
relaunch in seconds. PowerToys ships its WinUI modules the same way. When a
feature needs package identity (a share target, a startup task, Explorer
context menus, the Windows AI APIs), add a sparse package instead of
switching: the `distribute` skill shows how.

## Several specs as one app

Building a suite (a tray app made of features, like PowerToys) from
several specs? Read [suite.md](suite.md) first: build the shell, then one
agent per feature in its own git worktree, then merge.

## A CLI for agents

To let Claude Code, Codex, or scripts read and change the app's data, call
the Skill tool with `agent-cli`. Commands go to the running app over a
named pipe, so the database keeps one writer and the window updates live.

## When the build fails

- `XamlParseException` or a crash with `0xC000027B` at launch: read the
  `CRASH` line the tools print. It names the property and the type, like
  "cannot assign Double to GridLength".
- A crash before any `CRASH` line: an exception in the `App` constructor.
  Keep the constructor to `InitializeComponent()`; do setup in
  `OnLaunched`. `Application.Current.Resources` throws there.
- `CS0104 'Path' is an ambiguous reference`: a page imports
  `Microsoft.UI.Xaml.Shapes`. Write `System.IO.Path`.
- The app starts and dies with no window after `dotnet publish`: publish
  leaves out the `.pri` and compiled XAML of an unpackaged WinUI app.
  Install with `build.ps1`, which builds and mirrors the bin folder.
- File in use: the app is running. `build.ps1` closes it first; another
  copy started from the bin folder needs closing by hand.
- C# errors: call the Skill tool with `csharp-winui`.

Done when the app opens from Start, the self-test passes, the snapshots
show every page in light and dark, and the first commit exists.

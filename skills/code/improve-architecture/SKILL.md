---
name: improve-architecture
description: Architecture check-in for a growing C# and WinUI codebase. Finds hot spots, proposes deeper modules, and refactors one step per commit with behavior proven unchanged. Use at the end of a milestone, before a big feature, when a file passes 600 lines, when one feature touches many files, when bugs repeat in one area, or when the user asks to refactor or clean up the code.
---

# Improve architecture

Features pile up, and an app that was easy to change gets hard to change.
A **check-in** catches that early: survey, propose, let the user pick, then
refactor in small steps that keep behavior the same. The target shape for
apps that grow is in [architecture.md](architecture.md).

## When to run

Any one signal is enough:

- The end of a milestone (the `improve-loop` skill calls this skill there).
- Before a big feature. Ask: "What change to the code would make this
  feature easy?"
- A C# file over 600 lines (act at 1,000), a class over 400 lines, or a
  page's code-behind (`.xaml.cs`) over 300 lines.
- One feature touched more than 6 files or more than 2 projects.
- The same area shows up in 3 fix-log entries, or the same kind of bug
  happens twice.
- State that changes more than 10 times a second raises `PropertyChanged`
  on a model that many bindings watch.
- A dependency cannot be replaced in a test.

## Vocabulary

Use these words exactly, so proposals stay comparable:

- **Module**: anything with an interface and an implementation (a class, a
  folder of files, a project).
- **Interface**: everything a caller must know: signatures, ordering,
  error modes, threading, performance.
- **Depth**: how much a module does per unit of interface. Deep modules hide
  a lot behind a small interface. Shallow modules are pass-throughs.
- **Seam**: a place where you can swap behavior without editing callers.
- **Adapter**: one implementation behind a seam. One adapter is a
  hypothetical seam. Two (production and test) make it real.
- **Locality**: a change touches one place. **Leverage**: callers get a lot
  for little.

## 1. Survey

Change no code in this step.

1. Read `CLAUDE.md`, `ARCHITECTURE.md`, `docs/adr/`, and `docs/FIXLOG.md`
   where they exist.
2. Find hot spots:

   ```powershell
   git log --since=30.days --name-only --format= | Where-Object { $_ } | Group-Object | Sort-Object Count -Descending | Select-Object -First 20 Count, Name
   Get-ChildItem src -Recurse -Include *.cs, *.xaml | Where-Object FullName -notmatch '\\(obj|bin)\\' | ForEach-Object { [pscustomobject]@{ Lines = (Get-Content $_.FullName).Count; File = $_.FullName } } | Sort-Object Lines | Select-Object -Last 20
   ```

3. Map the structure: projects and their references (`dotnet list
   src/<App>/<App>.csproj reference`), interfaces and their
   implementations, classes that touch the UI thread and also do I/O, and
   what runs on which thread.
4. Look for C# and WinUI friction:
   - Code-behind that grows: event handlers in `.xaml.cs` that hold rules,
     call the store, or build strings. A page's code-behind wires events
     and moves focus; the rules live in a model or in Core.
   - A view model that owns everything (a god model). The template's
     `AppModel` is fine for a few pages; past that, each page gets its own
     model and `AppModel` keeps only what pages share.
   - `{Binding}` where `{x:Bind}` works. `x:Bind` is compiled and checked
     at build time; `Binding` uses reflection and fails silently at run
     time.
   - Wrappers that only forward calls.
   - App code that reaches into a library's internals
     (`InternalsVisibleTo` for anything but the test project).
   - Test hooks outside the agent modes. `--selftest`, `APP_SNAPSHOT`, and
     `APP_SEED` are the agreed launch flags; anything else test-only goes
     in the test project.
   - Fast-changing state on a model that every page binds to.
   - Slow work on the UI thread (SQLite queries over large tables, file
     I/O, image decoding), or `.Result` and `.Wait()` on a task, which can
     deadlock it.
   - Interfaces with one implementation and no test adapter.
   - Drifted copies: the same private type defined in two files:

     ```powershell
     Get-ChildItem src -Recurse -Filter *.cs | Select-String -Pattern 'private (sealed )?(partial )?(class|record|struct) (\w+)' |
       ForEach-Object { $_.Matches[0].Groups[4].Value } | Group-Object | Where-Object Count -gt 1 | Select-Object Name, Count
     ```
   - A settings or detail page that lives far from its feature. Pages live
     in the feature's folder.
   - Pure logic inside the WinUI project, which `dotnet test` cannot reach.
     Move it into `<App>.Core` as a plain type that takes plain values. The
     template's `ItemTitle` is the pattern.
   - Core that references WinUI. `<App>.Core` targets `net10.0` with no
     `Microsoft.UI` namespace: `Select-String -Path src\<App>.Core\*.cs
     -Pattern 'Microsoft\.UI'` finds nothing.
5. **Deletion test** for each candidate: if you deleted this module, would
   its complexity concentrate in one place (keep it) or spread to every
   caller (it earns its place)? Pass-throughs fail the test.

## 2. Propose

Write `docs/architecture-review-<YYYY-MM-DD>.md`, one card per candidate:

```markdown
## <Title in domain nouns>  [Strong | Worth exploring | Speculative]

Files: src/App/Pages/EditorPage.xaml.cs (1,750 lines), ...
Signal: <which trigger above>
Problem: <what is hard to change today, with one example>
Proposal: <the new module and its interface, 15 lines of C# at most>
Benefits: <locality and leverage; which tests get simpler>
Proof of same behavior: <unit tests, self-test, snapshots, golden files, perf baseline>
Plan: <one commit per step>
```

End with a **top recommendation** and a short "considered, not
recommended" list. Saying "the code is fine" is a valid result: then
every card is `Speculative` or there are none.

Stop and ask the user which candidate to do. In an unattended loop, do the
top recommendation only if it is `Strong`. Leave the rest in the file.

## 3. Refactor

- **Before:** record the proof:
  - `dotnet test tests/<App>.Core.Tests` passes, with new tests at the
    interface you are about to change.
  - `tools/selftest.ps1` exits 0.
  - `tools/snap.ps1`, then copy `%TEMP%\<App>-shots` to
    `%TEMP%\<App>-shots-before`. After the refactor, snap again and
    compare each PNG pair (`Get-FileHash`; look at any pair whose hashes
    differ).
  - Golden outputs where they apply: exported files byte for byte, old
    databases and settings files that must still load.
  - A performance baseline where speed matters.
- One refactor per commit. Behavior stays the same. No features mixed in.
- New types go in new files that compile first. Then move callers over.
  The app builds (`./build.ps1 -Dev`) after every step.
- Replacing an engine: keep the old path behind a flag for one milestone,
  then delete it.
- Replace tests, do not layer them: delete the old shallow tests that the
  new interface tests cover.
- If a diff passes about 10 files, split it.
- If the interface is open, design it twice: sketch two or three
  interfaces under different constraints (fewest entry points, simplest
  common caller, ports and adapters) and pick one with a reason.

## 4. Record

- Update `ARCHITECTURE.md`: projects, layers, dependency direction, seams.
- Add an ADR in `docs/adr/` only when the decision is hard to reverse,
  surprising without context, and a real trade-off.
- Add a `docs/FIXLOG.md` entry for any bug the refactor found. Every entry
  ends with a `Rule:` line.

Done when the review file exists, the chosen refactor is committed in
steps, `dotnet test`, `tools/selftest.ps1`, and the snapshots give the same
results before and after, and `ARCHITECTURE.md` matches the code.

# Architecture for apps that grow

The template starts with two projects: the WinUI app and `<App>.Core`.
Move to this shape when the app gets a CLI, an engine (media, sync,
parsing), or a second contributor.

## Contents

- Projects and dependency direction
- Edits as pure operations
- Split state by update rate
- Seams with two adapters
- Engines report counters, not exceptions
- Safe saves
- Errors
- File size norms
- Gotchas

## Projects and dependency direction

```text
<App> (WinUI pages, windows, commands) -> feature and engine libraries -> <App>.Core
```

Never the reverse. Core knows nothing about WinUI, media APIs, or
Direct3D. It targets plain `net10.0`, so `dotnet test` runs it without the
app.

```text
src/
  Tasks.Core/      net10.0                    values, documents, rules, math
  Tasks.Store/     net10.0, refs Core         persistence (SQLite), sync
  Tasks.Engine/    net10.0-windows, refs Core media, rendering, heavy work
  Tasks/           WinUI app, refs Core, Store, Engine
  Tasks.Cli/       console app, refs Core, Store
tests/
  Tasks.Core.Tests/   xUnit v3, refs Core
```

Every project is in the `.slnx`. Split Store out of Core only when it grows
or a second store appears; the template keeps `ItemStore` in Core. A CLI
on the same core keeps the core free of UI by force. The app project stays
thin: pages, windows, and one observable model per page.

## Edits as pure operations

Every change to the user's document is an operation applied to a value.
One entry point serves the UI, Undo, the CLI, and tests.

```csharp
public abstract record EditOp
{
    public sealed record Rename(Guid Id, string To) : EditOp;
    public sealed record Move(Guid Id, int To) : EditOp;
}

public static class DocumentEdits
{
    // All or nothing. Returns a new value, or the same instance when nothing changed.
    public static Document Apply(this Document doc, IReadOnlyList<EditOp> ops) { ... }
}

public bool Perform(IReadOnlyList<EditOp> ops, string name)
{
    try
    {
        var next = Doc.Apply(ops);
        if (ReferenceEquals(next, Doc)) return false;
        PushUndo(name); Doc = next; ScheduleSave(); return true;
    }
    catch (EditException e) { ErrorMessage = e.Message; return false; }
}
```

## Split state by update rate

A value that changes many times a second (a playhead, a meter, a timer)
lives in its own small observable object. Only the controls that show it
bind to it. Putting it on the page model re-runs every binding that
listens to that model.

```csharp
public partial class PlayheadClock : ObservableObject
{
    [ObservableProperty]
    public partial int Frame { get; set; }
}
```

```xml
<TextBlock Text="{x:Bind Clock.Frame, Mode=OneWay}" FontFamily="Consolas" />
```

Engines own their real-time state. The UI reads snapshots at the rate a
person can see, with a `DispatcherQueueTimer`: 15 Hz for meters, 1 Hz for
device lists.

## Seams with two adapters

Add an interface only where you swap behavior: network, clock, file
system, an external service. Give it a production adapter and a test
adapter on day one.

```csharp
public interface ITransport
{
    Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct);
}
sealed class HttpTransport(HttpClient http) : ITransport { ... }
sealed class RecordedTransport : ITransport { ... }    // tests: replays saved responses
```

For time, take a `TimeProvider` (built into .NET) instead of reading
`DateTimeOffset.Now`; tests pass a fake one.

## Engines report counters, not exceptions

Real-time paths never throw. They count what went wrong and expose a
snapshot.

```csharp
public record struct WriterStats(int Appended, int Stalls, int Dropped, int Fallbacks);

private readonly Lock _gate = new();
private WriterStats _stats;
public WriterStats Snapshot() { lock (_gate) return _stats; }
```

The self-test (`SelfTest.cs`) prints the counters. A non-zero `Dropped`
fails it.

## Safe saves

SQLite: one transaction per edit, and schema changes as new migration
steps (the template's `ItemStore.Migrations`), never an edit to a step
that shipped. Back up with `VACUUM INTO`, never a file copy of a database
that is open (the `-wal` file holds recent writes).

Other files: write to a temp file in the same folder, then
`File.Move(temp, path, overwrite: true)`. Keep a revision number; when the
file changed under you, reload and replay unsaved operations. Never
overwrite an output another feature points at: write a new version.

## Errors

One exception type per module (`StoreException`, `ImportException`), with
a message people can read. The UI catches it at the command and shows it
inline or in an InfoBar. Media and real-time paths degrade (drop a preview
frame, fall back to the CPU) and count it.

## File size norms

Library files mostly 100 to 450 lines. A file over 600 lines is a check-in
signal. View models and code-behind over 1,000 lines are the first
refactor targets. Test hooks live in the test project or behind a launch
flag (`--selftest`, `APP_SNAPSHOT`), not in page code.

## Gotchas

- `System.Text.Json` changes the bytes of saved files when you add a
  property (it writes `"Tags": []`). Keep a test that loads and re-saves
  an old file byte for byte.
- `x:Bind` defaults to `OneTime`; `Binding` defaults to `OneWay`. A
  binding moved from `Binding` to `x:Bind` that stops updating needs
  `Mode=OneWay`.
- `PropertyChanged` or an `ObservableCollection` change raised off the UI
  thread throws (`RPC_E_WRONG_THREAD`) or crashes. Move the change onto
  the UI thread with `DispatcherQueue.TryEnqueue`.
- Record equality compares a `List<T>` member by reference, so two equal
  documents can compare unequal. Compare by `ReferenceEquals` after
  `Apply`, or use an equatable collection type.
- An exception in an `async void` event handler crashes the app. Catch
  inside every `async void` handler.
- A page that subscribes to a long-lived model's event stays in memory as
  long as the model does. Unsubscribe in `Unloaded`, or keep the page
  cached (`NavigationCacheMode="Required"`, as the template does) so only
  one instance exists.
- Two agents building one solution at once break each other: they share
  the build folder (`%LOCALAPPDATA%\Dev\<App>`). New types land in new
  files that compile on their own; never add an empty project.
- Async job queues: give each job a generation number and claim it before
  starting, so a retry never runs a paid job twice.

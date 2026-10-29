---
name: csharp-winui
description: C# 14 and WinUI 3 code rules for Windows apps, including threading, x:Bind, observable models, SQLite, Win32 interop, and fixes for common build and launch errors. Use when writing, reviewing, or debugging C# or XAML in a WinUI app, or on XamlParseException, 0xC000027B crashes, binding, or threading errors.
---

# C# and WinUI

The code is right when it builds with zero warnings, launches without a
`CRASH` line, and the app does what the story says. For how it should
look, call the Skill tool with `windows-design`. For copyable XAML, read
[../../design/windows-design/patterns.md](../../design/windows-design/patterns.md).

## Threads

WinUI has one UI thread per window. Every control and every object bound
to a control lives on it.

| Work | Put it in |
| --- | --- |
| Pages, view models, `ObservableCollection`s bound to lists | The UI thread (the default in event handlers and `OnLaunched`) |
| CPU-heavy pure work (parsing, image work, diffing) | `await Task.Run(() => ...)`, then set results back on the UI thread |
| File and database work | `async` methods; small SQLite calls on the UI thread are fine, anything over a few ms goes to `Task.Run` |
| A Win32 callback (hotkey, hook, timer from another thread) | Hop back with `DispatcherQueue.TryEnqueue(() => ...)` |

- `await` in an event handler returns to the UI thread on its own. Inside
  `Task.Run` you are on a pool thread: never touch a control there.
- `async void` only for event handlers. Everything else returns `Task`.
- Keep the `DispatcherQueue` of the window (`window.DispatcherQueue`) for
  code that runs outside a page.
- A crash on a background thread kills the app with no `CRASH` line from
  `UnhandledException`. Catch and log in every `Task.Run` body you start
  and forget, and run every background path once after changing it.

## State

- Models are `partial class X : ObservableObject` with
  `[ObservableProperty] public partial string Title { get; set; }`
  (CommunityToolkit.Mvvm 8.4 and C# 14). Initialize partial properties in
  the constructor: they cannot have initializers.
- `[NotifyPropertyChangedFor(nameof(DisplayTitle))]` for computed
  properties.
- Lists bind to an `ObservableCollection<T>`. Rebuild it in place
  (`Clear`, `Add`), never assign a new collection to a bound property.
- `x:Bind` is compiled and checked, so prefer it over `{Binding}`. Its
  default mode is **OneTime**: write `Mode=OneWay` for anything that
  changes and `Mode=TwoWay` for inputs, and
  `UpdateSourceTrigger=PropertyChanged` on a `TextBox` that saves as you
  type.
- After changing the object a page binds through (`Model.Selection`), call
  `Bindings.Update()` if the binding path starts above the changed object.
- JSON goes through source generation: add each saved type to `AppJson`
  (`[JsonSerializable(typeof(T))]`) and pass `AppJson.Default.T`. Reflection
  JSON breaks under NativeAOT and trimming (`IL2026`, `IL3050`).
- Small preferences go in `Prefs` (JSON next to the database). Unpackaged
  apps have no `ApplicationData.Current.LocalSettings`: it throws. Secrets
  go in the Windows Credential Locker (`PasswordVault`) or DPAPI
  (`ProtectedData`), never in the JSON.
- A property set to the value it already has fires no change. Fill lists
  in the constructor too, not only in `OnXChanged`.

## SQLite

- One `SqliteConnection` per app, opened once, WAL mode, in `<App>.Core`.
- Schema changes are new steps in `ItemStore.Migrations`, keyed by
  `PRAGMA user_version`. Never edit a step that shipped. A new column has
  a `DEFAULT`, so old files keep loading.
- Every row has a `Guid` id. Never use the row number or list index as an
  identity.
- `APP_STORE` overrides the path, and the app refuses test modes unless it
  points into `%TEMP%`. Tests never touch
  `%LOCALAPPDATA%\<App>\<App>.db`.
- Undo: keep the deleted model in memory and save it back. SQLite has no
  undo of its own across commits.
- Several processes on one file (a CLI, a second instance): send commands
  to the running app instead (`agent-cli`), so the file keeps one writer.

## Commands and keys

- Every command is a button or menu item with a `KeyboardAccelerator`.
  The accelerator works wherever focus is in the window.
- Keys that also mean something while typing (Delete, Backspace, arrows,
  Enter, plain letters) are scoped: `ScopeOwner="{x:Bind List}"`, and set
  `args.Handled = true` in `Invoked`.
- Flyout and context menu items are not in the visual tree: they get no
  `DataContext`. Pass the row with `Tag="{x:Bind}"`.

## Names that collide

`Window`, `Page`, `Frame`, `Layout`, `Path`, `Rectangle`, `Grid`, `Color`,
`Colors`, `Thickness`, `Point`, `Size`, and `Application` all exist in WinUI
namespaces. Do not name your own types after them. `Path` collides with
`System.IO.Path` as soon as a file imports `Microsoft.UI.Xaml.Shapes`:
write `System.IO.Path`. `Task` collides with `System.Threading.Tasks.Task`:
name a to-do `Todo` or `TaskItem`.

## Win32 interop

WinUI covers windows, pages, and controls. Win32 is still the answer for:

- The window handle: `WinRT.Interop.WindowNative.GetWindowHandle(window)`.
- Pickers and dialogs from WinRT (`FileOpenPicker`, `FolderPicker`):
  `WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd)`, or they
  throw.
- DPI: `GetDpiForWindow`. `AppWindow` sizes are physical pixels; XAML is in
  epx. The template's `WindowBehavior` scales between them.
- Global hotkeys, the tray icon, hooks, other apps' windows: call the Skill
  tool with `os-integrations`.

Declare calls with `[LibraryImport]` in one `Win32.cs`, with
`AllowUnsafeBlocks` on. Keep every P/Invoke in that file. Callbacks
(window procedures, hooks, `EnumDisplayMonitors`) are
`[UnmanagedCallersOnly]` static methods passed as `&Method`; catch every
exception inside them, because one that escapes ends the process.
Keep `unsafe` to the block that takes the address: a class marked
`unsafe` cannot `await` anywhere (`CS4004`).

## Glyphs in source

Segoe Fluent Icons glyphs are private-use characters that show as nothing
in an editor or a diff. Write them as code points:
`char.ConvertFromUtf32(0xE734)` in C#, `&#xE734;` in XAML. Never paste the
character itself.

## Build and launch errors

| Error | Fix |
| --- | --- |
| `XamlParseException: Failed to assign to property 'ColumnDefinition.Width' ... 'Double' cannot be assigned to 'GridLength'` | The resource is the wrong type: a `GridLength` resource for widths of rows and columns, `x:Double` for `Width` and `Spacing` |
| `0xC000027B` exit and a `CRASH ... XamlParseException` | Read the property and type in the message; the line number is often 0 |
| `0xC000027B` and no `CRASH` line at all | An exception in the `App` constructor, before the handler exists. Keep the constructor to `InitializeComponent()`; do setup in `OnLaunched` |
| `COMException 0x8000FFFF` from `Application.Current.Resources` | Called in the `App` constructor. Move it to `OnLaunched` |
| `CS0104 'Path' is an ambiguous reference` | `System.IO.Path` |
| `CS0104 'Layout' is an ambiguous reference` | Your type shares a WinUI name: rename it (`SnapLayout`) |
| `CS4004 Cannot await in an unsafe context` | Remove `unsafe` from the class; wrap only the pointer line in `unsafe { }` |
| App dies at once after `dotnet publish` | The publish folder lacks `<App>.pri` and the `.xbf` files. Install with `build.ps1` |
| `ContentDialog.ShowAsync` throws | Set `XamlRoot = this.XamlRoot` first |
| `The property 'X' was not found in type 'Y'` in `x:Bind` | Add `x:DataType` to the `DataTemplate`, and make the property public |
| A binding that never updates | `x:Bind` is OneTime by default: add `Mode=OneWay` |
| `ApplicationData.Current` throws `InvalidOperationException` | The app is unpackaged. Use `Prefs` or a file in `AppPaths.Folder` |
| Warning `CS8618` non-nullable field | Initialize it in the constructor, or make it nullable. Fix every warning |

Fix the first error first. Later errors often come from it.

Done when the app builds with zero warnings, the self-test passes, and
the story's acceptance checks pass.

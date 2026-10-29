# Performance recipes

Each recipe names the symptom, the cause, and the fix. The code compiled
in a template app; the watchdog, the frame probe, and the startup numbers
ran on Windows 11.

## Contents

- UI thread
- Show first, work after
- Drawing: let the GPU composite
- Images
- Lists and XAML updates
- SQLite
- Background work with async and await
- Timers, polling, and energy
- Startup
- Measuring

## UI thread

**Symptom:** the whole app freezes, the title bar says "Not responding".
**Cause:** a call on the UI thread that waits on something slow.

- `SendMessage` to another app's window waits until that app answers. A
  hung app hangs you. Use `SendMessageTimeout` with `SMTO_ABORTIFHUNG`, or
  `PostMessage`, and never in a loop on the UI thread.
- COM calls into other processes (shell, Office, audio devices) block the
  same way. Run them on a background thread.
- File, network, and SQLite reads in a page constructor, `Loaded`, or
  `OnNavigatedTo`. Fix: start the work with `await Task.Run(...)` and fill
  the page when it lands.
- `.Result` or `.Wait()` on a task from the UI thread. The task's
  continuation needs the UI thread, so the app deadlocks for good. Always
  `await`.

Find the frozen call while it is frozen:

```powershell
dotnet-stack report -p (Get-Process MyApp).Id > "$env:TEMP\MyApp-stacks.txt"
Select-String -Path "$env:TEMP\MyApp-stacks.txt" -Pattern 'Microsoft.UI.Xaml' -Context 30,0 | Select-Object -First 1
```

The UI thread is the one with `Microsoft.UI.Xaml` frames at the bottom
of its stack. The frames above them are your code, and the top frame is
the call that waits.

## Show first, work after

**Symptom:** a shortcut feels slow even when the work is fast.
**Cause:** the UI waits for the work before it appears.

Show the window, panel, or picker in the same frame as the key press. Do
the slow part after, and fill it in when it lands. A screenshot picker
shows at once, see-through, and captures every monitor in parallel behind
it. A drag that ends before the capture lands waits for it.

```csharp
picker.AppWindow.Show();                          // same frame as the key press
var shots = await Task.WhenAll(monitors.Select(m => Task.Run(() => CaptureAsync(m))));
for (var i = 0; i < shots.Length; i++) pickers[i].Freeze(shots[i]);   // back on the UI thread
```

Optimistic UI is the same rule for data: close the panel on Enter, show
the new row, save in the background, offer Undo.

Build windows that must open fast (launcher panels, pickers) a few
seconds after launch and keep them hidden. Creating a WinUI window and
its XAML on the first key press is the usual cause of a slow first open.

## Drawing: let the GPU composite

**Symptom:** dragging or moving the pointer over a big image stutters.
**Cause:** layout or a full redraw on every pointer event.

- Put the big picture in an `Image` once. On pointer move, change only a
  small element on top (selection box, crosshair, label).
- Move it with a `RenderTransform` or `UIElement.Translation`, not with
  `Margin`, `Canvas.Left`, or `Width`. A transform skips layout; those
  properties run measure and arrange each time.

  ```csharp
  private readonly TranslateTransform _move = new();   // selectionBox.RenderTransform = _move, once
  private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
  {
      var p = e.GetCurrentPoint(Surface).Position;
      _move.X = p.X;                                    // no layout pass
      _move.Y = p.Y;
  }
  ```

- Never draw a full-screen bitmap per pointer move into a
  `WriteableBitmap` or a Win2D `CanvasControl`. A 4K screen is 8 million
  pixels per frame.
- In an editor, keep the picture as one element and redraw only the
  shapes on top while the user drags. Win2D `CanvasControl.Invalidate()`
  redraws the whole control; split static and moving layers into two
  controls.
- Many `ThemeShadow`s and blurs (`AcrylicBrush`) on list rows cost a lot.
  Put one on the container, not one per row.
- Image filters: one Win2D `CanvasDevice` (`CanvasDevice.GetSharedDevice()`)
  for the app, which renders on the GPU. Never create a device per call.

## Images

**Symptom:** memory jumps and scrolling stalls in a grid of photos.
**Cause:** each image decoded at full size (a 24 MP photo is about
100 MB of pixels) to show at 160 epx.

Decode at the size you show:

```csharp
// Simple: BitmapImage decodes at the requested size. Set it before the source.
var bitmap = new BitmapImage { DecodePixelType = DecodePixelType.Logical, DecodePixelWidth = 160 };
await using var stream = File.OpenRead(path);
await bitmap.SetSourceAsync(stream.AsRandomAccessStream());
image.Source = bitmap;
```

Off the UI thread, for many images at once:

```csharp
using Windows.Graphics.Imaging;

public static async Task<SoftwareBitmap> DecodeAsync(string path, uint maxWidth)
{
    await using var stream = File.OpenRead(path);
    var decoder = await BitmapDecoder.CreateAsync(stream.AsRandomAccessStream());
    var scale = Math.Min(1.0, (double)maxWidth / decoder.PixelWidth);
    var transform = new BitmapTransform
    {
        ScaledWidth = (uint)Math.Max(1, decoder.PixelWidth * scale),
        ScaledHeight = (uint)Math.Max(1, decoder.PixelHeight * scale),
        InterpolationMode = BitmapInterpolationMode.Fant,
    };
    return await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform,
        ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage);
}

// On the UI thread: SoftwareBitmapSource needs Bgra8, premultiplied.
var bitmap = await Task.Run(() => DecodeAsync(path, 320));
var source = new SoftwareBitmapSource();
await source.SetBitmapAsync(bitmap);
image.Source = source;
```

- Cache decoded thumbnails keyed by path, size, and file write time, with
  a size limit.
- OCR (`Windows.Media.Ocr`) runs off the UI thread, after the picture is
  on screen.

## Lists and XAML updates

- `ListView`, `GridView`, and `ItemsRepeater` create rows only for what is
  on screen, until something gives them infinite height. A `ListView`
  inside a `ScrollViewer` or a vertical `StackPanel` builds every row.
  Give the list a `Grid` row with `*` height.
- `ItemsRepeater` without its own `ScrollViewer` also builds every item.
  Wrap it in one.
- Count how many rows get built before you trust it: log in
  `ListView.ContainerContentChanging` (or
  `ItemsRepeater.ElementPrepared`) and scroll once.
- Big rows: render the cheap parts first with `x:Phase` in the row
  template, or in `ContainerContentChanging` by `args.Phase`.
- `x:Load="False"` keeps a part of the page out of the tree until code or
  a binding loads it. Use it for panels most people never open. Hidden
  (`Visibility.Collapsed`) elements still cost creation time.
- Refilling an `ObservableCollection` with `Clear()` and thousands of
  `Add()` calls raises one change per item. For a full refill, assign a
  new collection to `ItemsSource` once.
- Live lists (search results, a switcher) do not animate. Speed wins
  over motion.
- Sort and filter in the model, not in a converter. Converters run per
  row and per change.
- `x:Bind` (compiled) over `Binding` (reflection), and `Mode=OneTime` for
  values that never change.

## SQLite

- Many inserts or updates: one transaction around them, and one prepared
  command whose parameter values change. Without a transaction, each
  statement commits and syncs to disk on its own.

  ```csharp
  using var tx = db.BeginTransaction();
  using var cmd = db.CreateCommand();
  cmd.CommandText = "INSERT INTO items (id, title, created_at) VALUES ($id, $title, $created);";
  var id = cmd.Parameters.Add("$id", SqliteType.Text);
  var title = cmd.Parameters.Add("$title", SqliteType.Text);
  var created = cmd.Parameters.Add("$created", SqliteType.Text);
  foreach (var row in rows)
  {
      id.Value = row.Id; title.Value = row.Title; created.Value = DateTimeOffset.Now.ToString("O");
      cmd.ExecuteNonQuery();
  }
  tx.Commit();
  ```

- An index for every `WHERE` and `ORDER BY` a page runs often. Check a
  slow query with `EXPLAIN QUERY PLAN`: `SCAN items` means no index.
- Min, max, and counts in SQL (`SELECT MAX(created_at) ...`), not by
  loading every row into objects.
- The template keeps WAL on (`PRAGMA journal_mode=WAL`), so reads do not
  wait for a write.
- A large read on the UI thread is a freeze. Load in `Task.Run` and hand
  plain objects back.

## Background work with async and await

- Heavy pure work: `await Task.Run(() => Work(input))`. The code after
  the `await` runs on the UI thread again, because WinUI installs a
  synchronization context on it.
- Library code in `<App>.Core` uses `ConfigureAwait(false)`, so it never
  needs the UI thread.
- From a background thread back to the UI: `window.DispatcherQueue.TryEnqueue(() => ...)`.
  Touching a XAML element from another thread throws
  `RPC_E_WRONG_THREAD`.
- Shared mutable state used off the UI thread: a `lock` or a `Channel<T>`
  with one reader.
- Cancel stale work. A search that runs per key press takes a
  `CancellationToken` and checks it, so old results never land on top of
  new ones.

## Timers, polling, and energy

- Poll fast only while it matters. Checks that only Settings shows run
  while Settings is open.
- `DispatcherQueueTimer` for UI timers; stop it in
  `Window.VisibilityChanged` when the window hides, and on
  `AppWindow.Changed` when it minimizes.
- Sample system stats only while someone looks at them (flyout open, page
  visible). Stop the timer when it closes.
- Prefer events to polling: `AddClipboardFormatListener` for the
  clipboard, `FileSystemWatcher` for folders, a named `EventWaitHandle`
  for pings from a CLI, `PowerManager.SystemSuspendStatusChanged`
  (Windows App SDK) for sleep and resume.
- A background helper (a tray app with no window open) can opt into
  efficiency mode: `SetProcessInformation` with `ProcessPowerThrottling`
  and `PROCESS_POWER_THROTTLING_EXECUTION_SPEED`. Turn it off again when
  the window opens.
- In a suite (many features in one process), throttle one thread, never
  the process: a throttled process slows the launcher and the switcher
  too. Give the sampler its own thread and mark it at the top of its loop:

  ```csharp
  [StructLayout(LayoutKind.Sequential)]
  struct THREAD_POWER_THROTTLING_STATE { public uint Version, ControlMask, StateMask; }

  [LibraryImport("kernel32.dll")] static partial nint GetCurrentThread();
  [LibraryImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
  static partial bool SetThreadInformation(nint thread, int infoClass, ref THREAD_POWER_THROTTLING_STATE info, uint size);

  /// EcoQoS for the calling thread only. 3 is ThreadPowerThrottling, 1 is EXECUTION_SPEED.
  static unsafe bool UseEcoQoS()
  {
      var state = new THREAD_POWER_THROTTLING_STATE { Version = 1, ControlMask = 1, StateMask = 1 };
      return SetThreadInformation(GetCurrentThread(), 3, ref state, (uint)sizeof(THREAD_POWER_THROTTLING_STATE));
  }

  var thread = new Thread(() => { UseEcoQoS(); Loop(); })
      { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "Stats sampler" };
  ```

  The same thread keeps a PDH query on one thread. Onyx's sampler read
  every stat in about 10 ms (Debug build), so a 2 s interval cost about
  0.5 percent of one core while something showed the numbers, and
  nothing while nothing did. Start the thread only while a flyout, page,
  or tray icon shows the numbers, and pause it while the display is off
  or the session is locked.

## Startup

**Measure** launch to first answer. With the agent CLI, the app's own
pipe gives a clean end point:

```powershell
$env:APP_STORE = "$env:TEMP\MyApp-start.db"
$sw = [Diagnostics.Stopwatch]::StartNew(); myapp status --json | Out-Null; $sw.ElapsedMilliseconds
```

Close the app between runs. Report the first run (cold) apart from the
rest. Without the CLI, log a line from the first `FrameProbe` in
`OnLaunched` and time it from `Process.Start`.

On the template app this measured about 1,000 ms per launch (including
the CLI's 300 ms check for a running copy).

**NativeAOT** compiles the whole app to native code: no JIT at launch.
`tools/publish.ps1 -Aot` makes it and runs the self-test on it.
Measured on the template (cold start to the main window, 3 runs each):
about 420 ms self-contained JIT, 170 ms NativeAOT. ReadyToRun only
precompiles the app's own code and gained about 10% (1,025 ms to 915 ms
to first CLI answer), so skip it. NativeAOT needs the MSVC linker and
source-generated JSON; the `distribute` skill has both.

Publish an unpackaged WinUI app with `-p:EnableMsixTooling=true`, or the
publish folder lacks the app's `.pri` (with the compiled XAML) and the
app dies in `InitializeComponent`. `tools/publish.ps1` sets it.

**Other startup costs to check first:**

- Work in the `App` constructor or `OnLaunched` before `Activate()`:
  database migrations, network checks, loading every row. Activate first
  and load after the first frame.
- Pages built eagerly. The template creates the first page only; keep it
  that way.
- Fonts, large images, and Win2D devices created at launch instead of on
  first use.

## Measuring

Measure the Release build: `./build.ps1`, not `-Dev`.

```powershell
# CPU and wall time: where the time goes (default profiles sample every thread's stack)
dotnet-trace collect -p (Get-Process MyApp).Id --duration 00:00:00:10 -o "$env:TEMP\MyApp.nettrace"
dotnet-trace report "$env:TEMP\MyApp.nettrace" topN -n 30 --inclusive
# Memory: live counters, then a heap snapshot by type
dotnet-counters monitor -p (Get-Process MyApp).Id --counters System.Runtime
dotnet-gcdump collect -p (Get-Process MyApp).Id -o "$env:TEMP\MyApp.gcdump"
dotnet-gcdump report "$env:TEMP\MyApp.gcdump"
# Native, GPU, and composition: Windows Performance Recorder (elevated terminal)
wpr -start CPU -start GPU -start DesktopComposition
wpr -stop "$env:TEMP\MyApp.etl"      # open in WPA
```

`dotnet-trace` sees .NET code only. When the time goes into native XAML,
DirectX, or another process, use WPR and WPA. Delete every `.nettrace`,
`.gcdump`, and `.etl` afterwards: they hold command lines, paths, and
memory contents.

Name your own intervals, so traces show them:

```csharp
using System.Diagnostics.Tracing;

[EventSource(Name = "MyApp")]
public sealed class AppEvents : EventSource
{
    public static readonly AppEvents Log = new();
    [Event(1)] public void PickerStart() => WriteEvent(1);
    [Event(2)] public void PickerStop() => WriteEvent(2);
}

AppEvents.Log.PickerStart();
// ...
AppEvents.Log.PickerStop();
```

Collect them with `dotnet-trace collect -p <pid> --providers MyApp`.

### UI thread watchdog

A background loop queues an empty item on the UI thread every 100 ms and
logs when it waited too long. It finds short freezes that a profiler
averages away.

```csharp
public static class UiWatchdog
{
    /// APP_WATCHDOG=1 turns it on. Lines go to APP_LOG (AgentLog).
    public static void StartIfRequested(DispatcherQueue queue, int thresholdMs = 50)
    {
        if (Environment.GetEnvironmentVariable("APP_WATCHDOG") != "1") return;
        _ = Task.Run(async () =>
        {
            while (true)
            {
                await Task.Delay(100);
                var sent = Stopwatch.GetTimestamp();
                var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                if (!queue.TryEnqueue(DispatcherQueuePriority.High, () => done.SetResult())) return;
                await done.Task;
                var ms = Stopwatch.GetElapsedTime(sent).TotalMilliseconds;
                if (ms > thresholdMs) AgentLog.Line($"HANG UI thread blocked {ms:F0} ms at {DateTime.Now:HH:mm:ss.fff}");
            }
        });
    }
}
```

A test with a 250 ms `Thread.Sleep` on the UI thread logged
`HANG UI thread blocked 203 ms`: the number is a lower bound, up to one
100 ms tick short. Use it to find when; use `dotnet-stack` during a long
freeze, or a trace, to find where.

### First frame from a script

"Feels slow" is a number once you log three times for an action:
blocked (how long the call took), visible (window on screen), and frame
(the first frame drawn after it).

```csharp
public static class FrameProbe
{
    /// Call right after the action. Logs when XAML has drawn the next frame.
    public static void Log(string label)
    {
        var start = Stopwatch.GetTimestamp();
        EventHandler<RenderedEventArgs>? handler = null;
        handler = (_, _) =>
        {
            CompositionTarget.Rendered -= handler;
            AgentLog.Line($"FRAME {label} {Stopwatch.GetElapsedTime(start).TotalMilliseconds:F1} ms");
        };
        CompositionTarget.Rendered += handler;
    }
}
```

`Rendered` fires only when XAML draws a frame. An action that changes
nothing on screen leaves the probe waiting for some later, unrelated
frame (measured: 1,364 ms until a window move drew one). Probe actions
that change what is shown.

Run it six times. The first run is the cold one; report it apart. When a
feature waits on purpose (a switcher that shows only after 90 ms, so a
quick tap never flashes), measure from the end of that wait.

### Probe commands

For an agent, the quickest loop is a probe command in the agent CLI or
the debug bridge (see the `test-windows-app` skill): it runs the action,
logs how long each step took, and returns the numbers as JSON. Then
compare before and after a change.

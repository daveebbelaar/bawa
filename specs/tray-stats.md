# Tray stats

Tray icons that show CPU, memory, and network as live numbers and meters, with a flyout for
graphs, per-core load, disk, and battery. Replaces: TrafficMonitor, opening Task Manager to
check one number. Ported from BAMA's menu bar stats spec.

## Brief

```markdown
# Stats

A notification area app for Windows users who want CPU, memory, network, disk, and battery at a glance.

## Replaces
TrafficMonitor, opening Task Manager to check one number

## Main jobs
1. Glance at CPU, memory, and network in the notification area.
2. Click an icon for graphs, per-core load, committed memory, disk space, and battery details.
3. Turn on a mini bar above the taskbar that shows the numbers as text.

## Data
- Sample: time, cpu, cpuUser, cpuPrivileged, cores, memoryUsed, memoryTotal, commitUsed, commitLimit, downRate, upRate, diskFree, diskTotal, battery. In memory only, the last 60.
- Core: index, efficiencyClass, load.
- BatteryInfo: percent, isCharging, onPower, isCharged, minutesToEmpty, minutesToFull, cycleCount, health, watts.
- Settings in settings.json under %LOCALAPPDATA%\Stats\: interval, which metrics get a tray icon, mini bar, CPU graph, mini bar position.

## Shape
Tray icons (one per metric) with a flyout, plus a Settings window. No taskbar button.
Windows 11 first, Windows 10 19041 or later runs. Unpackaged .exe. Local data. Works offline. No accounts.

## System hooks
Tray icons, launch at login, single instance.

## Reference
none

## Look
Native WinUI with the system accent. Tray digits match the taskbar theme, white on dark and
black on light. The flyout is Acrylic with soft cards and small graphs.

## Not in version one
- Storage scanner and cleanup
- Temperature sensors and fan speeds
- Per-app CPU and memory list
```

## How it works

| Part | API | Permission |
| --- | --- | --- |
| CPU, total and per core | PDH: `PdhOpenQuery`, `PdhAddEnglishCounter` on `\Processor Information(*)\% Processor Utility`, `% User Time`, `% Privileged Time`, `PdhCollectQueryData`, `PdhGetFormattedCounterArray` | None |
| Core types | `GetLogicalProcessorInformationEx(RelationProcessorCore)`, `PROCESSOR_RELATIONSHIP.EfficiencyClass` | None |
| CPU fallback | `GetSystemTimes`, tick deltas | None |
| Memory | `GlobalMemoryStatusEx` (`ullTotalPhys`, `ullAvailPhys`), `GetPerformanceInfo` (`CommitTotal`, `CommitLimit`, `PageSize`) | None |
| Network | `GetIfTable2`, `MIB_IF_ROW2.InOctets` and `OutOctets`, `FreeMibTable` | None |
| Disk | `DriveInfo` for the system drive (`GetDiskFreeSpaceEx`) | None |
| Battery | `GetSystemPowerStatus`, then `Windows.Devices.Power.Battery.AggregateBattery.GetReport()` | None |
| Cycle count | `IOCTL_BATTERY_QUERY_INFORMATION` with `BatteryInformation`, device from `SetupDiGetClassDevs(GUID_DEVCLASS_BATTERY)` | None |
| Tray icons | `Shell_NotifyIcon`, one per metric on the app's own hidden window, each with its own uID and no GUID, `NOTIFYICON_VERSION_4`, `NIF_SHOWTIP` ([recipe](../skills/code/os-integrations/recipes.md#tray-icon)) | None |
| Icon per sample | 32-bit `CreateDIBSection`, `CreateIconIndirect`, `NIM_MODIFY`, then `DestroyIcon` on the old one | None |
| Flyout | WinUI `Window`, `SetBorderAndTitleBar(false, false)`, `DesktopAcrylicBackdrop`, placed with `Shell_NotifyIconGetRect` ([recipe](../skills/code/os-integrations/recipes.md#move-a-window)) | None |
| Mini bar | WinUI `Window`, topmost, `WS_EX_TOOLWINDOW` and `WS_EX_NOACTIVATE` ([recipe](../skills/code/os-integrations/recipes.md#non-activating-panel)) | None |
| Timer | A dedicated sampler `Thread` that waits on the cancellation token's `WaitHandle`, results through `DispatcherQueue.TryEnqueue` | None |
| Pause while away | `RegisterPowerSettingNotification(GUID_CONSOLE_DISPLAY_STATE)`, `WTSRegisterSessionNotification` | None |
| Low power | `SetThreadInformation(ThreadPowerThrottling)` with `THREAD_POWER_THROTTLING_EXECUTION_SPEED` (EcoQoS) on the sampler thread only | None |
| Launch at login | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` ([recipe](../skills/code/os-integrations/recipes.md#launch-at-login)) | None |
| One copy running | `AppInstance.FindOrRegisterForKey` ([recipe](../skills/code/os-integrations/recipes.md#single-instance)) | None |

Stats needs no permission and no administrator rights. A stats app must sample cheaply: read
the `performance` skill, "Timers, polling, and energy".

Types to create:
- `StatsSample`: one reading as a record in `Stats.Core`, plus `MemoryFraction` and `CommitFraction`.
- `StatsReader`: owns the PDH query, the last network byte counts, and the CPU fallback ticks. Touched only on the sampler thread.
- `StatsSampler`: `ObservableObject` on the UI thread. Owns the sampler thread, `Latest`, `History` (60), and a set of demand keys.
- `StatsFormat` (`Stats.Core`): every number-to-text rule in one place.
- `SamplerGate` (`Stats.Core`): the demand keys, display off, and session lock in, running or stopped out. Unit tested.
- `TrayIcons` and `IconPainter`: one `Shell_NotifyIcon` per metric and one `HICON` per sample. `TrayIcons` takes a `live` flag: in test runs it draws and replaces the icons but never calls `Shell_NotifyIcon` (see Gotchas).
- `StatsFlyout` with `CpuCard`, `MemoryCard`, `DiskCard`, `NetworkCard`, `BatteryCard`.
- `MiniBar`: the text strip above the taskbar.
- `SettingsPage`: metric toggles with a live preview, interval, mini bar, battery details, launch at login.

Behaviors that matter:
- **Interval** 1, 2, or 5 seconds, default 2. One sampler thread reads everything. That thread
  runs with EcoQoS, so Windows can schedule the reads on efficiency cores, while the flyout and
  Settings on the UI thread stay at full speed. The one thread also keeps the PDH query on one
  thread. One read takes about 10 ms, so the 2 s interval costs about 0.5% of one core.
- **Baseline.** CPU and network need two readings. The first read only stores counters and is
  never published. The first sample comes 0.5 s after start, not a full interval.
- **Slow values.** Battery every 10 s, disk every 60 s. Carry the last value into the samples in between.
- **Demand.** The sampler runs while something shows numbers: the tray icons (while any metric
  icon is on), the flyout, the mini bar, the Settings page. Each adds a key while visible and
  removes it when gone. No keys left stops the timer. The display turning off or the session
  locking stops it too, whatever the keys say. A window counts as visible only while it is
  shown and not minimized. After a pause longer than 5 intervals, clear the history so the old
  graph is not squeezed against the new one.
- **CPU** is `\Processor Information(_Total)\% Processor Utility`, the number Task Manager shows,
  capped at 100. User and privileged come from `% User Time` and `% Privileged Time`. Per core
  comes from the `(*)` array, one value per logical processor, in Task Manager's order.
- **Memory used** is `ullTotalPhys - ullAvailPhys`, which matches Task Manager's "In use" within
  a few percent. Committed is `CommitTotal * PageSize` of `CommitLimit * PageSize`. The dot is
  green below 80% of the commit limit, yellow to 90%, red above.
- **Units.** Memory and disk use 1024-based units with KB, MB, and GB labels, like Task Manager
  and File Explorer. Rates are bytes per second, 1024-based: `KB/s`, `MB/s`. Compact rates are at
  most four characters: `0K`, `90K`, `999K`, `1.2M`, `12M`, `1.2G`.
- **Tray icons.** One icon per metric on the app's own hidden window, each with its own uID
  (1 for the app icon, 101 and up for the metrics), no GUID. CPU and Memory draw the percent as
  one or two bold digits filling the icon, with 99 as the top value (the tooltip says 100%).
  Network draws two bars, down in the accent color and up in the text color, scaled to the peak,
  never below 50 KB/s so background chatter stays flat. Battery draws its percent. Each icon is
  `GetSystemMetricsForDpi(SM_CXSMICON, dpi)` square for the taskbar's display: 16 px at 100%,
  24 px at 150%, 32 px at 200%.
- **Tooltips**, updated per sample: "CPU 12%", "Memory 61%, 9.8 GB of 16 GB", "Down 1.2 MB/s,
  up 40 KB/s", "Battery 84%, 3:20 left". Under 128 characters each.
- **Defaults:** CPU on, Memory on, Network on, Battery off (Windows already has a battery icon),
  mini bar off, CPU graph off. A PC without a battery leaves the battery icon and card out. With
  every metric off, show the app icon so the flyout stays reachable. Built into a tray app that
  already has its own icon and flyout, the metric icons start off, and the host's icon keeps the
  flyout reachable.
- **Redraw** only when a sample arrives, and only the icons whose text changed. The icons have no
  timer of their own.
- **Clicks.** Left-click (`NIN_SELECT`) on any icon toggles the flyout. Right-click
  (`WM_CONTEXTMENU`) shows Open Task Manager, Settings, Quit.
- **Flyout**, 340 epx wide, 12 epx above the taskbar, its right edge in line with the clicked
  icon and kept inside the work area: CPU card (60-sample sparkline, user and privileged, one bar
  per logical processor with efficiency cores lighter), Memory and Disk side by side, Network
  (sparkline scaled to the peak, never below 50 KB/s), Battery. Header buttons open Task Manager
  (`taskmgr.exe`) and Settings. Show "Reading…" until the first sample.
- **Mini bar** (M2): label over value for each metric (`CPU`, `MEM`, `BAT` or `CHG` at 10 epx
  semibold, the value at 14 epx). Network is two lines, `↑` and `↓`, at 12 epx. Each part's width
  is the widest of its label, `100%`, and the current value. Rates measure against `999K`, `9.9M`,
  `99M`, `999M`. So the bar never jiggles when numbers change. It sits just above the taskbar by
  the notification area, can be dragged, and remembers its spot. Its accessible name reads like
  "CPU 12%, memory 61%, download 1.2 MB/s, upload 40 KB/s".

The sampler, with the baseline and the thread it must stay off:

```csharp
// One thread of its own: off the UI thread (a collect can take tens of ms), the PDH query on one
// thread, and EcoQoS for this thread only.
var thread = new Thread(() =>
{
    try
    {
        UseEcoQoS();                                // SetThreadInformation(GetCurrentThread(), ThreadPowerThrottling, ...)
        using var reader = new StatsReader();       // PdhOpenQuery, PdhAddEnglishCounter: English names work in every language
        reader.Read();                              // baseline: rate counters and network bytes need two readings
        if (token.WaitHandle.WaitOne(500)) return;  // first sample after 0.5 s, not a full interval
        while (!token.IsCancellationRequested)
        {
            if (reader.Read() is { } sample)        // PdhCollectQueryData, GetIfTable2, GlobalMemoryStatusEx
                dispatcher.TryEnqueue(() => sampler.Publish(sample));
            if (token.WaitHandle.WaitOne(interval)) return;
        }
    }
    catch (Exception e) { AgentLog.Line($"CRASH stats sampler: {e}"); }   // or the app ends with no CRASH line
}) { IsBackground = true, Priority = ThreadPriority.BelowNormal };
thread.Start();
```

## Stories

```markdown
# Stories

## M1 Core loop

### S-001 See CPU, memory, and network in the notification area
As the user, I glance at the notification area and read CPU, memory, and network.
Screens: tray-icons.png
Acceptance:
- [snap] The CPU, memory, and network icons render at 16, 20, 24, and 32 px on a light and a dark taskbar.
- [unit] `StatsFormat.TrayDigits` gives "0", "7", "61", "99" for 0, 7, 61, and 100 percent.
- [self] After 100 samples, `GetGuiResources(GR_GDIOBJECTS)` and `GR_USEROBJECTS` are the same as after 10. The test run paints and replaces the icons without adding them to the notification area.
- [manual] In the installed build, the icons appear in the notification area.
Status: todo

### S-002 Open the flyout
As the user, I click an icon and see the cards; a click outside or on the icon closes it.
Screens: flyout.png
Depends: S-001
Acceptance:
- [snap] The flyout shows CPU with sparkline and core bars, Memory, Disk, and Network cards.
- [manual] Clicking the icon while the flyout is open closes it and does not open it again.
- [snap] Before the first sample the flyout shows "Reading…".
Status: todo

### S-003 Read committed memory and disk space
As the user, I see memory in use, committed memory with a status dot, and free disk space.
Screens: flyout.png
Depends: S-002
Acceptance:
- [snap] Memory shows a bar, "9.8 GB of 16 GB", and "Committed 18.2 of 40 GB" with a green, yellow, or red dot.
- [snap] Disk shows percent used and "N GB free of N GB" for the system drive.
Status: todo

### S-004 See battery charge and status
As the user, I see charge and time left in the flyout, and can add a battery icon.
Screens: flyout.png, tray-icons.png
Depends: S-002
Acceptance:
- [unit] `StatsFormat.BatteryStatus` gives "Charging, 1:05 until full", "Fully charged", "On power, not charging", "3:20 left", and "On battery" while the time is unknown.
- [snap] With Battery on, its icon shows the percent.
- [manual] On a desktop PC without a battery, no battery card and no battery toggle show.
Status: todo

### S-005 Choose the tray icons
As the user, I toggle CPU, Memory, Network, and Battery in Settings and see a live preview.
Screens: settings.png
Depends: S-001
Acceptance:
- [snap] Settings shows the four toggles, a preview of the icons, and "Refresh every" with 1, 2, 5 seconds.
- [self] Turning a metric off removes its icon within one sample, checked on the tray model's add and remove decisions; the test run never calls `Shell_NotifyIcon`.
- [self] With all metrics off, the model shows the app icon, and its click opens the flyout.
- [manual] In the installed build, turning a metric off removes its icon from the notification area.
Status: todo

### S-006 Sample only while numbers are visible
As the user, I trust that Stats costs almost nothing while I am not looking.
Depends: S-002
Acceptance:
- [unit] Removing the last demand key stops the timer; adding one starts it.
- [unit] Display off or session locked stops the timer with keys still present; unlocking starts it again.
- [manual] At the 2 second interval, Task Manager shows Stats under 1% CPU.
Status: todo

### S-007 No prompts and no admin
As the user, I install Stats and it never asks for anything.
Acceptance:
- [manual] A clean install as a standard user shows no UAC prompt and no other prompt, and every card fills in.
Status: todo

### S-008 Keep the icons in view
As the user, I am told how to keep the icons out of the overflow on Windows 11.
Screens: first-run.png
Acceptance:
- [snap] First run shows "Windows hides new tray icons. Turn on Stats in taskbar settings to keep it in view." with an Open taskbar settings button (`ms-settings:taskbar`).
- [manual] After turning the icons on there, they stay visible after Stats restarts and after Explorer restarts.
Status: todo

## M2 Mini bar and details

### S-009 Show numbers in a mini bar
As the user, I turn on the mini bar and read CPU, memory, and network as text above the taskbar.
Screens: minibar.png
Acceptance:
- [unit] `StatsFormat.CompactRate` gives 0K, 90K, 999K, 1.2M, 12M, 1.2G for matching byte rates.
- [self] Ten samples with changing values keep the mini bar the same width.
- [snap] The mini bar shows CPU and MEM as label over percent, then ↑ and ↓ rates, in light and dark.
Status: todo

### S-010 Read battery health in Settings
As the user, I see health, cycle count, and power draw.
Screens: settings.png
Acceptance:
- [snap] Each row shows only when the battery reports it.
- [unit] A negative `ChargeRateInMilliwatts` gives "Using 9.4 W"; a positive one gives "Charging at 45 W".
- [unit] Health is full charge capacity over design capacity, capped at 100%.
Status: todo

### S-011 Add a CPU graph to the mini bar
As the user, I turn on a tiny CPU history next to the CPU percent.
Screens: minibar.png
Acceptance:
- [snap] A 24-sample bar graph, one epx per bar, draws inside a thin rounded frame.
Status: todo

### S-012 Launch at login
As the user, I turn on "Open at login" in Settings.
Acceptance:
- [manual] After a restart, the icons are in the notification area.
- [snap] When Task Manager's Startup apps turned Stats off, the toggle is off and says "Turned off in Task Manager".
Status: todo

## Dropped
- Bluetooth device batteries: Windows has no one public API that matches what Settings shows, and the GATT Battery Service works only for some Bluetooth LE devices. Later.
- Storage scanner, "Free up space", and largest files: later, it is its own app.
- Top apps by CPU and memory: later.
- Temperatures and fan speeds: most PCs need a kernel driver and administrator rights for them.
- Ring gauges overview with rolling-number animations.
```

## Gotchas

- **Windows 11 hides new tray icons.** A new icon lands in the overflow behind the `^` until the
  user turns it on in Settings > Personalization > Taskbar > Other system tray icons. An app
  cannot turn it on itself. Say so on first run and open `ms-settings:taskbar`.
- **Use uIDs, not GUIDs.** A tray icon GUID is tied to the exe path, so a build run from another
  folder (`./build.ps1 -Dev` from the artifacts path, one Debug build per git worktree) fails
  `NIM_ADD` with the same GUID. Windows remembers the user's "show" choice by exe path and uID,
  not by window. Give each icon its own fixed uID on the app's own window: 101 and up for the
  metrics, so no icon shares the entry of a uID 1 icon from the same exe.
- **Test runs never add tray icons.** Windows 11 remembers every tray icon it has ever seen: each
  exe path and uID gets a permanent key under `HKCU\Control Panel\NotifyIconSettings`. A self-test
  or snapshot run that calls `NIM_ADD` leaves an entry for every dev build folder. In test runs,
  draw and replace the icons as usual and skip `Shell_NotifyIcon`. The real icons are a manual check.
- **Explorer restarts drop every icon.** Register `RegisterWindowMessage("TaskbarCreated")` and
  add all icons again when it arrives, or the icons are gone until Stats restarts.
- **Every sample makes a new HICON.** Call `DestroyIcon` on the old one after `NIM_MODIFY`, and
  `DeleteObject` on the color and mask bitmaps right after `CreateIconIndirect`, which copies them.
  Miss either and the process hits the 10,000 GDI object limit in a few hours, and icons stop drawing.
- **Drawing the digits.** Draw white text on black into a 32-bit DIB with `ANTIALIASED_QUALITY`,
  take the gray value as alpha, and tint it. Center by ink, not by the font's cell, or the digits
  sit high. The icon takes straight alpha (premultiply only for a XAML `WriteableBitmap`). Two bold
  digits do not fit 16 px in Segoe UI: "Bahnschrift SemiBold Condensed" at an em of 1.4 times the
  icon size, shrunk until `GetTextExtentPoint32` fits, fills 16 px.
- **`ICONINFO` with a `bool` field breaks `LibraryImport`** (SYSLIB1051, runtime marshalling must be
  disabled). Declare `fIcon` as `int`.
- **Draw at the real icon size.** Ask `GetSystemMetricsForDpi(SM_CXSMICON, dpi)` and draw at
  exactly that size. A 16 px icon on a 150% taskbar is scaled up and the digits blur. Redraw on
  a scale change.
- **The taskbar has its own theme.** Read `SystemUsesLightTheme` under
  `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize`, not `AppsUseLightTheme`.
  Dark taskbar with light apps is the Windows 11 default. Redraw on `WM_SETTINGCHANGE` with
  `"ImmersiveColorSet"`.
- **Use `PdhAddEnglishCounter`, not `PdhAddCounter`.** Counter names are translated, so
  `\Processor Information(*)` does not exist on a German or Japanese Windows.
- **Match Task Manager with `% Processor Utility`.** Task Manager shows utility, which counts
  boost clocks; `% Processor Time` reads lower on modern CPUs. PDH caps formatted values at 100
  unless you pass `PDH_FMT_NOCAP100`. Keep the cap, like Task Manager.
- **Skip the totals in the per-core array.** Instances are named `"0,0"`, `"0,1"`, and so on,
  plus `"0,_Total"` and `"_Total"`. Drop every name that ends in `_Total`, or the core bars show
  one or two extra full bars.
- **Performance counters can be broken.** On some PCs `PdhAddEnglishCounter` fails because the
  counter registry is damaged. Fall back to `GetSystemTimes` for the total and hide the core bars.
  Its kernel time includes idle time: busy is `(kernel - idle) + user` over `kernel + user`.
- **`GetIfTable2` lists the same adapter several times.** Filter drivers, VPN clients, and
  Hyper-V add their own rows with the same bytes. Count only rows with
  `InterfaceAndOperStatusFlags.HardwareInterface` set and `FilterInterface` clear, and
  `OperStatus` up. Free the table with `FreeMibTable` after every read, or memory grows each sample.
- **`MIB_IF_ROW2` has no managed definition.** On x64 and ARM64 a row is 1352 bytes, the rows
  start at offset 8 of the table, and the fields sit at: `Alias` 28, `InterfaceAndOperStatusFlags`
  (a byte) 1152, `OperStatus` 1156, `InOctets` 1208, `OutOctets` 1280. A self-test that reads the
  alias of each counted adapter ("Wi-Fi") proves the offsets.
- **`PROCESSOR_RELATIONSHIP` offsets.** In `SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX`,
  `EfficiencyClass` is at 9, `GroupCount` at 30, and the 16-byte `GROUP_AFFINITY` array at 32. On
  a hybrid CPU a self-test can check one class per logical processor.
- **The first PDH query in a process is slow,** several hundred ms. A snapshot taken soon after
  the Settings page opens catches "Reading…", so draw "Reading…" in the preview too, never an
  empty card. Replace the value, not the whole card, so the card never changes height.
- **The byte counters are 64 bits but can reset.** Unplugging an adapter starts its count at 0
  again. Treat a negative delta as 0 instead of a huge rate.
- **Task Manager shows network in bits.** "1.2 Mbps" in Task Manager is about 150 KB/s here.
  Compare after dividing by 8.
- **Windows runs out of commit, not RAM.** A PC with free RAM still fails to start apps when the
  commit charge reaches the limit. That is why the dot follows commit, not "In use".
- **Battery fields lie a little.** `GetSystemPowerStatus.BatteryLifeTime` is `-1` while Windows
  works it out and always on AC power: show "On battery", not a time. `BatteryLifePercent` is 255
  when unknown. `BatteryFlag` 128 means no battery. `BatteryReport` fields are nullable and
  often missing; show a row only when its value is there. Time to full is not reported: compute
  it from remaining and full capacity and the charge rate.
- **The flyout reopens itself.** Clicking the icon while the flyout is open deactivates the
  flyout, which hides it, and then the click's `NIN_SELECT` opens it again. Ignore `NIN_SELECT`
  within 0.3 s of the flyout hiding. Activate the flyout when you show it, or it never gets
  `Deactivated` and outside clicks do not close it.
- **Call `SetForegroundWindow` before `TrackPopupMenuEx`.** Without it, the right-click menu
  stays open when the user clicks somewhere else.
- **Do not assume the taskbar is at the bottom.** Windows 10 allows any edge. Place the flyout
  from `Shell_NotifyIconGetRect` and the work area of the icon's display, so it opens toward
  the screen.
- **Stay off the taskbar itself.** Windows 11 has no deskbands, and windows parented into
  `Shell_TrayWnd` break with Explorer updates. The mini bar sits in the work area, just above it.
- **Brushes read in code ignore the element's theme.** `Application.Current.Resources["CardBackgroundFillColorDefaultBrush"]`
  is the app theme's brush, so in a dark snapshot (or with an app theme unlike Windows') the
  cards stay light. Build colored parts from XAML with `{ThemeResource ...}`, for example one line
  through `XamlReader.Load`. A snapshot sets the theme after the surface opens: draw again on
  `ActualThemeChanged`.
- **Launch at login can be turned off behind your back.** Task Manager's Startup apps writes to
  `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run` and leaves the
  `Run` value in place. Read both, so the toggle tells the truth.

Done when the tray icons show CPU, memory, and network that match Task Manager within a few
percent (network after converting bits to bytes), the flyout shows every card including battery
on a laptop, the icons survive an Explorer restart, and Stats stays under 1% CPU at the 2 second interval.

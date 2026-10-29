# Launcher

Press Alt+Space, type a few letters, press Enter: the app, file, or web
search opens. Replaces: PowerToys Run, the Start menu search box, and
Everything for finding files by name. Ported from BAMA's launcher spec.

## Brief

```markdown
# Launch

A keyboard launcher for one person: press a shortcut, type, and open an app, a file, or a web search.

## Replaces
PowerToys Run and Start menu search, for opening apps and files.

## Main jobs
1. Open an app by typing part of its name or its initials ("vsc" for Visual Studio Code).
2. Find a file or folder by name, then open it or show it in File Explorer.
3. Run a keyword search: "g cats" searches Google, "code" lists workspace files.

## Data
- AppEntry: name, launch target (shortcut path or AppUserModelID), folded name, initials
  (rebuilt on every open, not saved)
- QuickSearch: keyword, name, kind (files or website), extensions, url with {query}, app path, enabled
- Usage: open count per target, and the last target picked for each query
- Launch.db (SQLite) and settings.json in %LOCALAPPDATA%\Launch

## Shape
Borderless, topmost panel opened with a global shortcut, a tray icon, and a Settings window.
Windows 11 first, Windows 10 19041 or later still runs. No taskbar button for the panel.
Local data. Works offline. No accounts.

## System hooks
Global shortcut (default Alt+Space), tray icon, Windows Search index for files.

## Reference
PowerToys Run, Start menu search

## Look
Native Fluent. One acrylic panel with rounded corners: the search box on top, results
below, the accent color on the selected row. Results never animate.

## Not in version one
- Calculator and Settings pages (ms-settings: links)
- Clipboard history, snippets, workflows, plugins
- GIF search and developer project search
```

## How it works

| Part | API | Permission |
| --- | --- | --- |
| Global shortcut | `RegisterHotKey` with `MOD_ALT \| MOD_NOREPEAT` and `VK_SPACE`, on the tray icon's window | None |
| Panel | WinUI `Window`, `OverlappedPresenter.SetBorderAndTitleBar(true, false)`, `IsAlwaysOnTop`, `AppWindow.IsShownInSwitchers = false`, `DesktopAcrylicBackdrop` | None |
| Desktop apps | `.lnk` files under `%ProgramData%\Microsoft\Windows\Start Menu\Programs` and `%APPDATA%\Microsoft\Windows\Start Menu\Programs` | None |
| Packaged apps (Calculator, Terminal, Settings) | `PackageManager.FindPackagesForUser("")`, `Package.GetAppListEntriesAsync()`, `AppListEntry.LaunchAsync()` | None |
| File search | Windows Search through `System.Data.OleDb`: provider `Search.CollatorDSO`, `SELECT ... FROM SystemIndex` | None (the Windows Search service must run) |
| Icons | `StorageFile.GetThumbnailAsync(ThumbnailMode.SingleItem, 64)`; `AppListEntry.DisplayInfo.GetLogo` for packaged apps | None |
| Open, reveal | `Process.Start` with `UseShellExecute = true`; `explorer.exe /select,"<path>"` | None |
| Give focus back | `GetForegroundWindow` before showing, `SetForegroundWindow` on Esc | None |
| Tray icon | `Shell_NotifyIcon`, `TrackPopupMenuEx` | None |

Launch needs no permission. The failure states are a shortcut that another
app already owns and a stopped Windows Search service. Recipes:
[global hotkey](../skills/code/os-integrations/recipes.md#global-hotkey-registerhotkey),
[tray icon](../skills/code/os-integrations/recipes.md#tray-icon),
[single instance](../skills/code/os-integrations/recipes.md#single-instance).

Types to create:

- `HotkeyCenter` and `KeyCombo`: register the shortcut, record failures, suspend while recording.
- `LauncherWindow`: the panel, `PreviewKeyDown` on the search box, `Present()` and `Hide()`.
- `LauncherModel` (`ObservableObject`): Query, Results, Selection, generation; `Search()` and `Activate()`.
- `FuzzyMatch` (Core): pure static class with `Fold`, `Initials`, `Score`. Unit tested.
- `AppIndex`, `FileSearch`, `QuickSearchStore` and `UsageStore` (SQLite, Core), `IconCache`.
- `SettingsPage`, `ShortcutRecorder`, `TrayIcon`.
- Snapshot mode (`APP_SNAPSHOT`) also renders the panel with seeded results as pages `launcher-empty` and `launcher-results`.

Fuzzy score: fold query and name (`Normalize(NormalizationForm.FormD)`, drop
`UnicodeCategory.NonSpacingMark`, `ToLowerInvariant()`),
`penalty = Math.Min(name.Length, 60)`, higher is better, null is no match. Exact
name 1000. Name prefix 900, initials prefix 800, word prefix 700, substring 500,
each minus penalty. Letters in order: `Math.Max(1, 300 - gaps * 8 - penalty)`,
gaps = skipped characters.

Initials: the first letter of each word, plus each capital after a lowercase letter:

```csharp
var result = new StringBuilder();
var previous = ' ';
foreach (var c in s)
{
    var word = char.IsLetterOrDigit(c);
    var prevWord = char.IsLetterOrDigit(previous);
    if (word && (!prevWord || (char.IsUpper(c) && char.IsLower(previous)))) result.Append(c);
    previous = c;
}
return Fold(result.ToString());   // "Visual Studio Code" -> "vsc", "WebStorm" -> "ws"
```

Ranking and limits:

- App score = fuzzy score + `Math.Min(200, opens * 20)` + 300 when this app was the last pick for this exact query.
- Keep the top 6 apps. When the best app scores 500 or more, hide apps under 450.
- Order: apps, then quick search hint rows, then files, then one "Search Google" row last. An app wins a tie.
- Desktop apps: every `.lnk` in both Start menu folders and their subfolders, named by the file name without `.lnk`. Drop names that start with "Uninstall". Add File Explorer (`explorer.exe`) when no shortcut has that name. Rescan on every open, on a background thread.
- Packaged apps: read at launch, and again in the background on each open; show the last list meanwhile. Skip framework and resource packages. Drop duplicate names; a desktop shortcut wins.
- Files: only when the query has 2 or more characters. Wait 80 ms after the last key, then run the query on a background thread.
- File query: one `CONTAINS(System.FileName, '"word*"')` per word, joined with `AND`, plus `System.FileExtension <> '.lnk'`. Double every `'`, strip `"` and `*`. Scope: one `SCOPE='file:<folder>'` per folder in Settings, joined with `OR`, default the user folder. `SELECT TOP 80 System.ItemPathDisplay`.
- Drop paths containing `\node_modules\`, `\.git\`, `\AppData\`, `\bin\`, `\obj\`, `\site-packages\`, `\.venv\`. Score by file name plus usage, keep 12.
- File subtitle: the last three folders, the user folder as `~`: "… › Projects › app › src".
- Quick search: "keyword rest" where the keyword matches an enabled, ready search. Ready means a keyword without spaces, and at least one extension (files) or a url with `{query}` (website). Defaults: `code` (files, `code-workspace`), `g` Google, `yt` YouTube, `gh` GitHub.
- Website kind: one row that opens the url with `Uri.EscapeDataString(text)`. Files kind: `System.FileExtension = '.ext'` for each extension (joined with `OR`), plus `System.FileName LIKE '%word%'` per word. `TOP 400`, `ORDER BY System.DateModified DESC`, show 60. Opens in the chosen app (`Process.Start(appPath, "\"" + path + "\"")`), or the default app.
- Hints: with no space typed, each keyword that starts with the query shows a hint row. An exact keyword goes on top. Enter or Tab on a hint fills in "keyword ".
- Keys: Up, Down, Enter opens, Ctrl+Enter shows it in File Explorer, Ctrl+1 to 9 opens that row, Tab completes, Esc closes. The panel closes when it loses activation (a click outside).
- Usage: on open, `opens[target] += 1` and `learned[foldedQuery] = target`, in Launch.db.
- Panel: 700 epx wide, search box 56 epx, rows 48 epx, up to 8 rows then scroll. The top sits 18% below the top of the work area of the monitor with the pointer. The panel takes focus; it saves the foreground window first and gives focus back to it on Esc.

## Stories

```markdown
# Stories

## M1 Core loop

### S-001 Open the launcher with the shortcut
As the user, I press Alt+Space in any app and the search panel appears.
Screens: launcher-empty
Acceptance:
- [snap] An acrylic panel with "Search apps and files" sits 18% below the top of the screen.
- [manual] The search box has focus and I can type at once.
- [manual] Esc, a click outside, or Alt+Space again closes it, and focus goes back to the app I came from.
Status: todo

### S-002 Find an app by name or initials
As the user, I type "calc" or "vsc" and the matching app is the first row.
Screens: launcher-results
Depends: S-001
Acceptance:
- [unit] FuzzyMatch scores exact > prefix > initials > word prefix > substring > letters in order.
- [unit] Initials("Visual Studio Code") is "vsc"; "cafe" matches "Café".
- [snap] Rows show icon, name, path, and Ctrl+1 to 9 key caps; the last row is "Search Google".
Status: todo

### S-003 Open, reveal, or search the web
As the user, I pick a row with the arrow keys or Ctrl+N and press Enter or Ctrl+Enter.
Screens: launcher-results
Depends: S-002
Acceptance:
- [manual] Enter opens the app or file and the panel closes; Ctrl+Enter shows it selected in File Explorer.
- [manual] Calculator, a packaged app, opens from its row.
- [manual] The "Search Google" row opens the browser; spaces and & in the query survive.
- [manual] Hovering a row selects it only after the mouse really moves.
Status: todo

### S-004 Learn from what I open
As the user, the app I picked for "s" last time is first the next time I type "s".
Depends: S-003
Acceptance:
- [unit] Opening adds Math.Min(200, opens * 20); the last pick for the exact query adds 300.
- [self] Usage survives a restart.
Status: todo

### S-005 Find files by name
As the user, I type part of a file name and files show up under the apps.
Screens: launcher-files
Depends: S-002
Acceptance:
- [manual] Files appear within about 150 ms, between apps and the web row; rows above them do not move.
- [unit] Paths in node_modules, .git, AppData, bin, obj, site-packages, .venv are dropped.
- [snap] The subtitle shows the last three folders, the user folder as ~.
- [manual] With the Windows Search service stopped, a row says "File search needs Windows Search" and apps still show.
Status: todo

### S-006 Use the tray icon
As the user, I right-click the tray icon and see Open Launch (with its shortcut), Settings, and Quit.
Acceptance:
- [unit] The menu model shows the current shortcut next to "Open Launch".
- [manual] A left click opens the launcher. No taskbar button while only the tray icon runs; closing Settings keeps the app running.
Status: todo

### S-007 Change the shortcut, and see when it is taken
As the user, I click the recorder in Settings and press a new combo.
Screens: settings-general
Acceptance:
- [manual] All shortcuts pause while recording. Esc cancels, Backspace clears, a combo without Ctrl, Alt, or Win (and not an F-key) is refused.
- [manual] When RegisterHotKey fails (PowerToys Run holds Alt+Space), an orange warning shows next to the shortcut.
Status: todo

## M2 Keyword searches

### S-008 Search a website with a keyword
As the user, I type "yt cats" and Enter opens YouTube results for cats.
Screens: launcher-quick-web
Acceptance:
- [snap] The search box shows a "YouTube" chip and a single row.
- [unit] Match("yt cats") returns YouTube and "cats"; disabled or not-ready searches never match.
- [manual] Typing "y" shows a YouTube hint row; Tab or Enter fills in "yt ".
Status: todo

### S-009 List files with a keyword
As the user, I type "code " and see my .code-workspace files, newest first.
Screens: launcher-quick-files
Depends: S-008
Acceptance:
- [manual] More words narrow the list; Enter opens the file in the chosen app.
- [snap] An empty result shows "No code workspaces found".
Status: todo

### S-010 Edit quick searches in Settings
As the user, I add, edit, turn off, and delete keyword searches.
Screens: settings-quick-searches
Depends: S-008
Acceptance:
- [snap] A search that is not ready shows an orange "Not ready" badge.
- [self] Quick searches survive a restart (Launch.db).
Status: todo

### S-011 Choose what and where to search
As the user, I turn apps, files, and the web row on or off, and pick the folders for file search.
Screens: settings-search
Acceptance:
- [manual] The last folder cannot be removed; a new folder is used on the next search.
- [snap] The footer says only folders in the Windows Search index show up, with a link to Indexing options.
Status: todo

## Dropped
- GIF search (needs an outside API key), developer projects, and text transforms (json, b64, jwt).
- Custom shell commands, port and process commands.
- Calculator row and Settings pages (good M3 candidates).
- "find" and "open" file-only keywords, URL protocol, activity stats.
```

## Gotchas

- A window with `WS_EX_NOACTIVATE` never gets keyboard focus, so a TextBox in it cannot be typed into. The launcher panel activates. Save `GetForegroundWindow()` before showing it, and on Esc hide it and call `SetForegroundWindow` on the saved window.
- `Activate()` from the hotkey handler can leave the panel behind the app in front with a flashing taskbar button, and typing then goes to the other app. Check `GetForegroundWindow()` after `Activate()` and call `Win32.ForceForeground` when it is not the panel.
- Set focus to the search box in the window's `Activated` handler, not before the show. Focus set on a hidden window is lost.
- The TextBox handles Up, Down, and Tab itself. Handle them in `PreviewKeyDown` on the TextBox and set `Handled`; by `KeyDown`, Tab has already moved focus.
- The first press built the window and its XAML tree, and the panel came up late. Build it 2 s after launch, show it off screen with `AppWindow.Show(false)`, let it draw one frame, and hide it.
- Files arrive later than apps. Insert them between the instant rows and the web row, and keep the selection by row id if the user already moved. Bump a generation counter on every search and drop any async result from an older one.
- Rows that change under a still mouse fire `PointerEntered` and steal the selection. Select on hover only when `GetCursorPos` changed since the last search.
- Alt+Space opens the window menu of the app in front. Launch takes it over on purpose, like PowerToys Run. The window menu stays one right-click on the title bar away. When PowerToys Run is running, the call fails and the warning shows.
- A typed `'` broke the SQL of the Windows Search query. Double every `'`, strip `"` and `*`. Folders outside the index never show up, so say that in the folder setting's footer.
- When the Windows Search service is stopped or disabled, `OleDbConnection.Open()` throws. Catch it, show one row that says so, and keep the app results.
- The index holds `.venv\Lib\site-packages` and `node_modules` folders under the user folder. Without the path filter they fill all 12 file rows.
- Calculator, Terminal, and Settings have no `.lnk` in the Start menu folders. Read packaged apps through `PackageManager`. That call is slow, so cache the list and refresh it in the background.
- Icons from `GetThumbnailAsync` are async shell work. Ask for 64 px once, show them at 32 epx, and keep them in a cache keyed by path, bounded to the current app list.
- On two monitors at different scales, the hidden panel has the DPI of the monitor it was on last. Size it with `GetDpiForMonitor` for the monitor under the pointer, then move it.
- Resizing the panel for each row made it jump. Resize once per finished search, with the top edge fixed, and only when the row count changed.
- A second copy fails `RegisterHotKey` with `ERROR_HOTKEY_ALREADY_REGISTERED` and shows a false "taken" warning. Make the app single instance (`AppInstance.FindOrRegisterForKey`) and send the second launch to the first copy's Settings.
- Closing the Settings window must not end the app. Cancel `AppWindow.Closing` and call `AppWindow.Hide()`. Show Settings only from the tray, a second launch, or the agent modes (`APP_SNAPSHOT`, `--selftest`).
- Changed from BAMA: the search box is 56 epx and rows are 48 epx, to sit on BAWA's 4 epx grid. The 60 pt shadow margin and the fixed transparent window are gone, because DWM draws the shadow outside a bordered WinUI window.

Done when Alt+Space opens the panel over any app, "vsc" or "calc" puts the right app first and Enter opens it, a file in the user folder shows up and opens, "yt cats" opens YouTube, and usage survives a restart.

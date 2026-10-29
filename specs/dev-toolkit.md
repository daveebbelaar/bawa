# Dev toolkit

A small search panel for developers: find a repo and open it in your editor, a terminal, File Explorer, or on GitHub; transform text from the clipboard; see what listens on a port and end it; run your own script commands by keyword. Replaces: DevToys, PowerToys Run, `netstat -ano` followed by `taskkill`. Ported from BAMA's dev-toolkit spec.

## Brief

```markdown
# DevKit

A keyboard panel for developers who live in repos, terminals, and the clipboard.

## Replaces
DevToys, PowerToys Run, `netstat -ano | findstr :3000` followed by `taskkill /pid`

## Main jobs
1. Find a repo by name and open it in the editor, a terminal, File Explorer, or its web remote.
2. Transform text (json, b64, url, uuid, ts, jwt, hash, case) and copy the result.
3. See what listens on a port and end it, or run a script command by keyword.

## Data
- Project: name, path. Cached in %LOCALAPPDATA%\DevKit\projects.json with the scan
  date and roots.
- ScriptCommand: file path (id), name, keywords, run, icon, cwd. One .toml file each in
  %USERPROFILE%\.config\devkit\commands.
- ListeningProcess: pid, name, program path, command line, port, address, start time.
  Never stored.
- Settings in %LOCALAPPDATA%\DevKit\settings.json: project roots, editor, commands
  folder, panel shortcut, open counts per project.

## Shape
Quick panel opened from the tray icon or a global shortcut the person sets, plus a WinUI Settings window.
Windows 11 first, Windows 10 19041 or later runs. Unpackaged .exe, no taskbar button
for the panel. Local data. Works offline. No accounts.

## System hooks
Global shortcut (none by default: Alt+Space belongs to the launcher), tray icon,
single instance. No elevation.

## Reference
PowerToys Run and Raycast (one search box, results below, key hints for the selected row)

## Look
Native WinUI panel with Acrylic and the system accent. Rows: Segoe Fluent icon, title, a
grey subtitle, key hints for the selected row.

## Not in version one
- App and file search
- Script output in a window
- Arguments for script commands
```

## How it works

| Part | API | Permission |
| --- | --- | --- |
| Search panel | Borderless WinUI window: `OverlappedPresenter.SetBorderAndTitleBar(false, false)`, `IsAlwaysOnTop`, `AppWindow.IsShownInSwitchers = false`, `DesktopAcrylicBackdrop` | None |
| Panel shortcut | `RegisterHotKey` ([recipe](../skills/code/os-integrations/recipes.md#global-hotkey-registerhotkey)), no default combo | None |
| Project scan | `Directory.EnumerateDirectories` with `EnumerationOptions` (`AttributesToSkip = Hidden \| System \| ReparsePoint`, `IgnoreInaccessible = true`), three levels deep, off the UI thread | None |
| Open in editor | `Process.Start` on the editor exe with the folder as the argument; Visual Studio found with `vswhere.exe -latest -property productPath` | None |
| Open in terminal | `wt.exe -d "<folder>"` (looked up on the PATH and in `%LOCALAPPDATA%\Microsoft\WindowsApps`), else `pwsh.exe` or `powershell.exe` with `WorkingDirectory` | None |
| Show in File Explorer | `explorer.exe "<folder>"`, and `explorer.exe /select,"<file>"` for a program | None |
| Web remote | Read `.git\config` as text, `Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })` | None |
| Clipboard | `Windows.ApplicationModel.DataTransfer.Clipboard.GetContent()`, `GetTextAsync`, `SetContent(DataPackage)` | None |
| Transforms | `System.Text.Json` `JsonDocument` (validate only), `Convert` with one Base64 decoder for both alphabets (see Gotchas), `Uri.EscapeDataString`, `SHA256.HashData`, `SHA1.HashData`, `MD5.HashData`, `Guid.NewGuid`, `RandomNumberGenerator` | None |
| Listening ports | `GetExtendedTcpTable` with `TCP_TABLE_OWNER_PID_LISTENER` for `AF_INET` and `AF_INET6` (iphlpapi.dll); `ProcessIdToSessionId(pid)` to drop session 0 | None |
| Process details | `Process.GetProcessById`, `QueryFullProcessImageName` with `PROCESS_QUERY_LIMITED_INFORMATION`, command line from `NtQueryInformationProcess(ProcessCommandLineInformation = 60)` (ntdll.dll, Windows 8.1 and later) with the same handle | None for your own processes |
| End a process | `Process.CloseMainWindow` when it has a window, else `Process.Kill()`; force is `Process.Kill(entireProcessTree: true)` | None for your own processes. Elevated or other users' processes need admin, so DevKit refuses them |
| Script commands | `Process` running `pwsh.exe` or `powershell.exe -NoProfile -NonInteractive -EncodedCommand`, `FileSystemWatcher` on the folder | None |

DevKit needs no elevation. Listeners in session 0 (services, found with `ProcessIdToSessionId(pid) == 0`) and processes it cannot open never show. Without that filter the list is mostly svchost, lsass, and spoolsv rows that cannot be ended.

Recipes: [tray icon](../skills/code/os-integrations/recipes.md#tray-icon), [single instance](../skills/code/os-integrations/recipes.md#single-instance), [move a window](../skills/code/os-integrations/recipes.md#move-a-window) (placing the panel), [elevation and UIPI](../skills/code/os-integrations/recipes.md#elevation-and-uipi).

Types to create:
- `PanelWindow`, `SearchModel` (query, results, selection, Enter with modifiers, Tab), and `Result` (kind: project, transform, process, command, note; title, subtitle, glyph, path, value).
- `FuzzyMatch`, `ProjectIndex`, `ProjectActions`, `GitRemote`, `CodeEditor`.
- `DevTransforms`, `JsonFormatter`, `Ports`: pure functions in `DevKit.Core`, plain values in, rows out. `Ports` takes the TCP table rows as input, so tests feed it fixtures.
- `ProcessGuard` (Core): the refusal list, by name and by pid (`Environment.ProcessId`). Unit tested.
- `CommandStore`, `ScriptCommand` (TOML subset parser), `CommandRunner`, and `SettingsWindow`.

Behaviors that matter:
- **Query.** The first word, lowercased, is a command name; the rest is the argument. Commands: `json`, `b64`/`base64`, `url`, `uuid`/`ulid`, `ts`/`time`, `jwt`, `hash`, `case`, `port`/`ports`, `kill`. An empty argument means the clipboard (not for `uuid` and `ts`).
- **Anything else** searches projects (2 or more characters, score 400 or more, top 5, plus 20 per past open up to 200) and script commands (exact keyword 2000, keyword prefix 1200, fuzzy name 450 or more).
- **Fuzzy score**, case and accents folded: exact 1000, prefix 900, initials ("vsc") 800, word prefix 700, substring 500, each minus the name length up to 60; letters in order 300 minus 8 per gap.
- **Project keys.** Enter opens the editor; Ctrl+Enter File Explorer, Alt+Enter a new terminal in the folder, Shift+Enter the web remote. The selected row shows these as key hints, or a footer shows the selected row's hints (one `TextBlock`, as in PowerToys Run and Raycast; hints on the row need a row template that knows it is selected). Up and Down move the selection, Esc closes, and losing focus closes.
- **Editor** choices: Cursor (`%LOCALAPPDATA%\Programs\cursor\Cursor.exe`), VS Code (`%LOCALAPPDATA%\Programs\Microsoft VS Code\Code.exe`, else under `%ProgramFiles%`), Visual Studio (from vswhere), File Explorer. Default is the first one installed. Visual Studio opens the `.slnx` or `.sln` in the folder root when there is one. Terminal is Windows Terminal when `wt.exe` is found, else pwsh, else Windows PowerShell.
- **Project index.** Default root is the first of `%USERPROFILE%\source\repos`, `%USERPROFILE%\Repositories`, `%USERPROFILE%\Projects`, `%USERPROFILE%\Code`, `%USERPROFILE%\src`, `%USERPROFILE%\dev` that exists. A folder is a project when it holds `.git` (a folder or a file). Cached on disk so the panel has projects at launch. Rescan when the panel opens and the list is older than 10 minutes or the roots changed. Serialize scans (one semaphore), so the last roots win; a self-test that scans a temp root swaps the roots for the test root and back.
- **Transforms copy, never paste.** Enter copies the value, closes the panel, and shows "Copied". A note row (bad input, empty clipboard) has an empty value: Enter plays the default beep (`MessageBeep`).
- **Big input.** Over 64 KB runs off the UI thread with a "Working…" row. Over 2 MB shows "The clipboard is too large".
- **Transform outputs.** `json`: pretty (2 spaces) and minified. `b64` and `url`: decoded (only if readable) and encoded. `uuid`: lowercase, uppercase, ULID. `ts`: now as seconds, milliseconds, ISO 8601 UTC, local; a number to dates (over 1e14 is microseconds, over 1e11 milliseconds, else seconds); a date to Unix time. `jwt`: payload, header, and exp, iat, nbf as relative times; a "Bearer " prefix is dropped. `hash`: SHA-256, SHA-1, MD5. `case`: camel, snake, kebab, Title, Pascal, CONSTANT.
- **Ports.** `port 30` filters by port prefix, name, or command line. Rows from `0.0.0.0` and `::` for the same pid and port merge into one. Old rows stay up while a new lookup runs, so the list does not flicker. **Ending takes two Enters.** The first arms the row ("Press again to end"); a second within 5 s ends it, Alt+Enter force ends the process and its children. "End" is the Windows word (Task Manager says "End task"); "quit" is the macOS one. Ctrl+Enter shows the program in File Explorer. The list refreshes 0.6 s later.
- **Script commands.** Enter runs the command in the background: a toast "Name…", then the last output line (120 characters max). Tab fills in the keyword. New command writes a starter file and opens it in the editor.

A command file. Parse only `key = "text"`, `key = ["a", "b"]`, and `#` comments. Use forward slashes in paths so no backslash escapes are needed:

```toml
name = "Morning light"
keywords = ["sun", "morning"]
run = "uv run --script $HOME/scripts/lights.py on"
icon = "E706"              # optional Segoe Fluent Icons code point, default E756
cwd = "~/Projects/lights"  # optional, ~ is %USERPROFILE%
```

Listening ports. The port is in network byte order:

```csharp
// After GetExtendedTcpTable(buffer, ref size, true, AF_INET, TCP_TABLE_OWNER_PID_LISTENER, 0) == 0
var count = MemoryMarshal.Read<int>(buffer);                                  // dwNumEntries
var rows = MemoryMarshal.Cast<byte, MIB_TCPROW_OWNER_PID>(buffer.AsSpan(4))[..count];
var listeners = new List<Listener>(count);
foreach (var row in rows)
{
    var port = (ushort)IPAddress.NetworkToHostOrder((short)row.dwLocalPort);
    var address = new IPAddress(row.dwLocalAddr);
    listeners.Add(new Listener(row.dwOwningPid, port, address, IPAddress.IsLoopback(address)));
}
```

## Stories

```markdown
# Stories

## M1 Core loop

### S-001 Open the panel
As the user, I click the tray icon or press the shortcut I set, and type at once; Esc closes the panel.
Screens: panel-empty.png
Acceptance:
- [snap] An Acrylic panel with one search box in the upper third of the display with the pointer.
- [self] The panel is on screen with the search box focused within 100 ms of the open command (the code path a shortcut runs), also on the first open after launch.
Status: todo

### S-002 Add project folders
As the user, I add the folder that holds my repos and see how many projects it found.
Screens: settings-projects.png
Acceptance:
- [snap] Settings lists roots with remove buttons, Add folder, a project count, and Rescan.
- [unit] The scan finds repos inside repos, skips the skip list, hidden folders, junctions and symlinks, and stops at depth 3.
Status: todo

### S-003 Open a project in my editor
As the user, I type part of a repo name and press Enter.
Screens: panel-projects.png
Depends: S-001, S-002
Acceptance:
- [snap] Rows show the repo name, a short parent path like "… › Code › clients", and key hints on the selected row.
- [unit] "on" finds "onyx" by prefix; an initials match beats a substring.
- [manual] Visual Studio opens the .slnx or .sln, not the bare folder, and VS Code opens with no console window flashing.
Status: todo

### S-004 Open a project in a terminal, File Explorer, or on GitHub
As the user, I hold Alt, Ctrl, or Shift with Enter.
Depends: S-003
Acceptance:
- [unit] `WebUrl` turns git@github.com:o/r.git, ssh://git@host:2222/g/r.git, and https://user:token@host/o/r.git into https URLs without credentials; a local path gives null.
- [unit] A worktree's `.git` file leads to the main repo's config through `gitdir` and `commondir`, with forward or back slashes.
- [manual] Alt+Enter opens Windows Terminal in the folder, also for a path with spaces.
Status: todo

### S-005 Format JSON from the clipboard
As the user, I copy JSON, type `json`, and copy the pretty or minified version.
Screens: panel-transform.png
Depends: S-001
Acceptance:
- [unit] `{"b":[1,2.50],"a":{},"c":"é<"}` keeps key order, keeps `2.50`, keeps `{}` on one line, and keeps `é<` unescaped.
- [snap] Invalid input shows "The clipboard has no valid JSON" and Enter beeps.
Status: todo

### S-006 Decode Base64, URL, and JWT text
As the user, I type `b64 aGVsbG8=`, `url a%20b`, or `jwt` with a token on the clipboard.
Depends: S-005
Acceptance:
- [unit] URL-safe Base64 without padding decodes; a length with remainder 1 is rejected.
- [unit] URL encoding keeps only letters, digits, and `-._~`.
- [unit] A "Bearer " prefix is dropped; a past exp reads "Expired" with a relative time.
- [snap] The JWT subtitle says "decoded locally, signature not checked".
Status: todo

### S-007 Convert a timestamp, make an ID, hash, or change case
As the user, I type `ts 1700000000`, `uuid`, `hash abc`, or `case parseHTTPResponse`.
Depends: S-005
Acceptance:
- [unit] 1700000000 reads as seconds, 1700000000000 as milliseconds, over 1e14 as microseconds; past year 10000 gives no rows, tested with the microseconds value 253402300800000000 (a seconds value never gets there).
- [snap] `ts` alone shows now in seconds, milliseconds, ISO 8601, and local time.
- [unit] A ULID is 26 Crockford Base32 characters; `parseHTTPResponse_v2` splits into parse, HTTP, Response, v2.
Status: todo

### S-008 Set the shortcut and editor
As the user, I record a new panel shortcut and pick my editor in Settings.
Screens: settings-general.png
Acceptance:
- [snap] A shortcut recorder, an editor picker with only installed editors, and a cheat sheet of every command.
- [snap] No shortcut is set at first. A combo another app owns shows "Already in use".
Status: todo

### S-009 Use the tray icon
As the user, I click the tray icon to open the panel, or right-click it for Settings and Quit.
Screens: tray-menu.png
Acceptance:
- [snap] The menu shows Open DevKit (with its shortcut, once one is set), Settings, and Quit.
Status: todo

## M2 Ports, processes, and commands

### S-010 See what listens on a port
As the user, I type `port`, `port 3000`, or `kill node`.
Screens: panel-ports.png
Acceptance:
- [snap] Rows read "3000 · uvicorn app:app" with pid, program path, and "localhost only" where it applies.
- [unit] Port bytes in network order decode: a raw dwLocalPort of 0xB80B is port 3000.
- [unit] `CommandLine` drops the interpreter path: "C:\…\python.exe" plus "C:\…\uvicorn.exe app:app" becomes "uvicorn app:app".
- [unit] `kill` matches only processes in my session, by name, pid, or program, at most 30.
- [snap] No match shows "Nothing is listening on 3000"; `kill` alone shows "Type a process name or pid, like kill node".
Status: todo

### S-011 End a process with two Enters
As the user, I press Enter twice on a row to end it, or Alt+Enter to force end.
Depends: S-010
Acceptance:
- [snap] After one Enter the hint reads "Press again to end"; after 5 s it arms again instead.
- [unit] System, Idle, smss, csrss, wininit, winlogon, services, lsass, svchost, dwm, and explorer are refused by name, and DevKit itself by pid (`Environment.ProcessId`), so a renamed or debug build is refused too.
- [self] The test starts a process of its own: `powershell.exe -NoProfile -Command` with a loopback `TcpListener` on port 0 that prints its port. The table lists it by pid and port, the real end path ends it, and the port is gone. A loopback listener raises no Windows Firewall prompt.
- [self] The same child, passed as a row with another start time, is left alone ("no longer running"). An in-process `TcpListener` shows in the list with DevKit's pid and is refused.
- [manual] Access denied shows "Not allowed to end it. It runs as administrator or another user."
Status: todo

### S-012 Run a script command
As the user, I type a keyword, press Tab to complete it, and Enter to run it.
Screens: panel-commands.png, settings-commands.png
Acceptance:
- [self] The toast shows the last output line; a line starting with ✗ counts as a failure even with exit code 0.
- [unit] A file without `run` reports "Needs a run line"; a missing name falls back to the file name.
- [manual] A new .toml file in the folder shows up without a restart, an edit in place shows up too, and parse problems list in Settings.
Status: todo

## Dropped
- App, file, and quick web search: DevKit is not a launcher.
- Learned query-to-project ranking (only open counts stay).
- The working folder of a process: Windows has no public API to read another process's current directory, so rows show the program path instead.
```

## Gotchas

- **No default shortcut.** Alt+Space belongs to the launcher (see `launcher.md`), and two specs built into one app must not share a default; the window menu note goes with whichever feature owns Alt+Space. `RegisterHotKey` returns `ERROR_HOTKEY_ALREADY_REGISTERED` when another app holds a combo the person records. PowerToys Run listens with a keyboard hook instead, so a combo it uses can still register and both open at once; tell the user to change one of them.
- **`Base64Url` is the wrong decoder.** `Base64Url.DecodeFromChars` rejects `+` and `/`, so it fails on standard Base64. One decoder that maps `-` and `_` to `+` and `/`, pads, and rejects a remainder of 1 handles both alphabets, for `b64` and for JWT parts.
- **Year 10000 needs microseconds.** A number over 1e11 already reads as milliseconds (year 5138 as seconds), so a seconds value never reaches year 10000. Test "past year 10000 gives no rows" with a microseconds value, like `ts 253402300800000000`.
- **Deserializing changes your JSON.** Turning it into objects or doubles drops `2.50` to `2.5`, and `Utf8JsonWriter` escapes `é` and `<` as `\u00E9` and `\u003C` with the default encoder. Use `JsonDocument.Parse` only to validate, then re-indent with a small character walker that tracks strings and escapes.
- **Remotes hold tokens.** `https://user:token@github.com/o/r.git` is common, and Git Credential Manager does not stop people pasting them. Drop everything before `@` when building the web URL. Keep a custom https port, drop an ssh port.
- **`.git` is not always a folder.** In worktrees and submodules it is a file with `gitdir:`, and a worktree's config lives in the main repo's folder named in `commondir`. On Windows the path in `gitdir:` uses forward slashes and may be relative. Read the config as text; no need to run git. Files have CRLF line ends.
- **The scan can explode.** Stay three levels deep, skip `node_modules`, `.venv`, `venv`, `build`, `dist`, `bin`, `obj`, `.vs`, `packages`, `target`, `vendor`, `__pycache__`, `site-packages`, `out`, plus hidden folders, junctions, and symlinks (`FileAttributes.ReparsePoint`). Run it off the UI thread. Repos inside OneDrive can be cloud-only placeholders; listing them is fine, but reading `.git\config` downloads the file.
- **Quoting a folder path for a command line.** `"C:\code\"` ends in a backslash that escapes the closing quote, so the program gets a broken path. Trim the trailing backslash, or use `ProcessStartInfo.ArgumentList`, which quotes for you. Windows Terminal also treats `;` as a command separator in its arguments.
- **Start the exe, not the `.cmd` shim.** `code` on the PATH is `code.cmd`, which flashes a console window. Start `Code.exe` or `Cursor.exe` directly with `UseShellExecute = false`.
- **Use `GetExtendedTcpTable`, not `netstat`.** One call returns every listener with its pid in a few milliseconds. Shelling out to `netstat -ano` is slower, localized, and leaves you parsing text. The port sits in the low 16 bits of `dwLocalPort` in network byte order; read it wrong and 3000 shows as 47115. `dwLocalAddr` is already in network order, so pass it to `new IPAddress(long)` as it is. The second call can still return `ERROR_INSUFFICIENT_BUFFER` (122) when a listener opens between the size call and the data call: retry with the new size.
- **Command lines without WMI.** WMI needs `System.Management`, a NuGet package, and takes 100 ms or more. `NtQueryInformationProcess(ProcessCommandLineInformation = 60)` on a `PROCESS_QUERY_LIMITED_INFORMATION` handle returns a `UNICODE_STRING` in microseconds for every process in the person's session, with no package. Elevated processes still return nothing when DevKit is not elevated; show the program name then.
- **Windows has no SIGTERM.** A console dev server has no window to close, so the first end is `Process.Kill()`, which is `TerminateProcess` and skips cleanup. That is fine for `npm run dev` and `uvicorn`. A process with a window gets `CloseMainWindow` first, so it can save. Force end also kills children, because `npm` and `uv` start the real server as a child.
- **Pids get reused.** Check that the process name and `StartTime` still match the row before ending it. `StartTime` throws for elevated processes; then compare the name only. `ArgumentException` from `GetProcessById` means it is gone; `Win32Exception` with error 5 means not allowed.
- **The clipboard is sometimes locked.** Another app can hold it open, and `Clipboard.GetContent` or `SetContent` throws a `COMException`. Retry 3 times, 50 ms apart, then show "The clipboard is busy".
- **GUI apps keep the PATH from sign-in.** A tool installed with winget after Explorer started is not on DevKit's PATH, so a script calling `uv` or `node` fails. Rebuild PATH before each run from `Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine)` and `User`, and add `%USERPROFILE%\.local\bin` and `%USERPROFILE%\.cargo\bin`.
- **A chatty script fills the pipe and hangs.** Read output with `BeginOutputReadLine` while it runs, redirect stdin and close it, set `CreateNoWindow = true`, and kill the process tree after 90 s. Set `StandardOutputEncoding` to UTF-8 and start the script with `[Console]::OutputEncoding = [Text.Encoding]::UTF8`, or `✗` arrives as `?`. Pass the script with `-EncodedCommand` so quotes survive.
- **`FileSystemWatcher` fires too often.** Unlike the macOS folder watcher, it does see in-place saves, but one save raises two or three `Changed` events and the file may still be locked. Wait 300 ms after the last event, retry a locked read, and reload the commands every time the panel opens too.
- **`wt.exe` is an app execution alias** in `%LOCALAPPDATA%\Microsoft\WindowsApps`, and that folder may be missing from the PATH a tray app inherited. Look there too before falling back to pwsh.
- **Panel speed.** Creating a WinUI window costs hundreds of milliseconds. Build the panel about 2 s after launch, hidden, and reuse it: `AppWindow.Hide()` on close, never `Close()`. On the shortcut, `AppWindow.Show()`, `SetForegroundWindow`, then focus the search box with `FocusState.Programmatic` in the `Activated` handler. Handle Enter, Up, and Down in `PreviewKeyDown` on the box. Look up installed editors and `wt.exe` once, not per row.

Done when the tray icon and a shortcut the person sets open the panel, a repo opens in the editor, a terminal, File Explorer, and on its web remote, every transform copies the right value, a dev server found with `port` ends after two Enters, and a script command runs by keyword.

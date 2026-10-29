# Rules for an agent CLI

The rules behind `Agent\` and `<App>.Cli\` in the template, and the ones
each new command must follow. From Anthropic's tool-writing guidance,
clig.dev, the CLIs of Optical Flow and Onyx, and a run of the template on
Windows 11.

## Contents

- How the parts talk
- Commands
- Output
- Errors and exit codes
- Safety
- Tests
- What not to build

## How the parts talk

- Two exes, one command list. `<App>.exe` is the WinUI app.
  `<cli>.exe` is a console app from `src\<App>.Cli`. The CLI project
  compiles three files from the app's `Agent\` folder (`AgentCore.cs`,
  `AgentCommands.cs`, `AgentPipe.cs`), so help, MCP tools, and the app
  read the same list. `AgentHandlers.cs` and `AgentHost.cs` are app only.
- Why not one exe: a WinUI app is a GUI program. cmd and PowerShell
  return to the prompt without waiting for it, so there is no exit code,
  and its stdout is not dependable. `AttachConsole` can print into the
  parent's console, but the shell has already moved on.
- The CLI sends one JSON line to the named pipe `<App>-agent-<session>`
  and reads one JSON line back. The pipe is created with
  `PipeOptions.CurrentUserOnly`: only this Windows user can open it, and
  the client checks that the app is owned by the same user.
- Pipe names are global to the PC. The session number keeps two people
  signed in apart. The app creates the first instance with
  `FirstPipeInstance`, so a second copy of the app logs
  `AGENT not listening ... another copy of the app owns it` instead of
  stealing requests.
- Commands run in the app on the UI thread (`DispatcherQueue`), through
  the same code as the buttons. The window updates live, and validation
  and side effects run once, in one place.
- When the app is not running, the CLI starts it with `--agent` and waits
  up to 20 seconds for the pipe. With `--agent` the window shows without
  taking the focus, so the agent's terminal keeps it. `--no-launch` fails
  with exit 4 instead.
- The CLI starts the app through the shell (`UseShellExecute = true`).
  The app gets the CLI's environment but none of its handles. An app that
  inherited the CLI's stdout would keep an agent's shell or MCP client
  waiting until the app quits.
- Never minimize the window at launch. In the template, a window
  minimized before its first frame crashed the app (COMException
  0x80040111).
- `APP_STORE=%TEMP%\x.db` moves the database and the pipe (its name gets a
  hash of the store path), so a test run starts its own copy of the app
  and never touches real data. `APP_AGENT_PIPE` moves the pipe alone.
  `APP_EXE` points the CLI at another app exe.
- The built-in request `_app` answers with the app's pid and build.
  `<cli> version` uses it to show both builds, and `check-agent-cli.ps1`
  uses the pid to close only its test copy.
- `build.ps1` closes the running app before it installs. A running
  `<cli> mcp` (Claude Desktop keeps one open) locks `<cli>.exe`; Windows
  cannot overwrite a running exe but can rename it, so `build.ps1` renames
  the old one to `.old` and deletes it on the next build.

## Commands

- Names are `<noun> <verb>`, lowercase: `items add`, `mail draft`,
  `projects list`. Same verbs everywhere: `list`, `show`, `add`, `update`,
  `delete`, `open`.
- One command per thing a person does. Merge steps an agent would always
  chain ("find and open" is one `open`).
- Option names say what they hold: `--title`, `--due`, `--project`, never
  `--t` or `--value`.
- Every option has a flag. No interactive prompts, ever: an agent cannot
  answer them.
- `status` shows what the app shows: running, open page, the page ids,
  selection, counts.
- Ids are the first 8 hex characters of the model's `Guid Id`
  (`Id.ToString("N")`). Any unique start works. Never print a row number.
- A command in `AgentCommands.cs` without an entry in `AgentHandlers.cs`
  makes the app log `AGENT no handler in AgentHandlers.cs for: <name>` at
  launch, and the command fails with that message.

## Output

- Without `--json`: short plain lines for people, one per item.
- With `--json`: exactly one JSON object on stdout, and nothing else.
  Notes and warnings go to stderr.
- JSON on stdout is plain ASCII: every non-ASCII character is written as
  `\uXXXX`. It reads right in any shell and code page, Windows PowerShell
  5.1 included. Human output goes to a console window in the console's
  code page, and to a pipe or file as UTF-8. Agents use `--json`.
- Results hold what an agent needs next: ids, titles, states, dates in ISO
  8601 with the offset. Not the whole model.
- Lists default to 50 items and return `total`. When more exist, they
  return `nextOffset`; the agent passes it back as `--offset`.
- `help` lists every command. `help <command>` shows params and an example.
  `help --json` returns every command as a JSON schema, for scripts and MCP.

## Errors and exit codes

| Exit | Code | Meaning |
| --- | --- | --- |
| 0 | | OK |
| 1 | `invalid`, `error` | The command ran and failed |
| 2 | `usage` | Unknown command or option, missing or bad value |
| 3 | `not_found` | No such id or page |
| 4 | `app_unavailable` | The app is not running and could not start |

- With `--json`, an error is `{"ok":false,"error":{"code":"...","message":"..."}}`
  on stdout. Without it, `<cli>: message` goes to stderr.
- A message says what to do next: "no item with id 3f2a. Run `tasks items list`",
  "missing --title. Example: tasks items add --title \"Buy milk\"".
- Add a code to `AgentError` when an agent should act differently on it
  (`stale`, `busy`). Keep the list short and in `help`.

## Safety

- Mark each command `ReadOnly` or `Destructive`. MCP clients show the
  marks and can skip the prompt for read-only tools.
- A destructive command (delete, send, empty, overwrite) without `--yes`
  changes nothing. It returns what it would do and the hint "add --yes".
- Sending anything outside the PC (mail, messages, posts) is two commands:
  `draft` returns an id and a preview, then `send <id> --yes`.
- Writes are idempotent where it is cheap: completing a done task returns
  `"changed": false`, not an error.
- Prefer the Recycle Bin and Undo over hard deletes. A command that
  deletes calls the same code as the window, so the app's Undo can bring
  it back.
- Never print secrets (tokens, passwords) in results or errors.

## Tests

- `check-agent-cli.ps1 <cli>` after every new command.
- Keep the parsing and result code in plain functions, and give each
  command one test in the app's tests with a test store.
- Before each release, let a fresh `claude -p` session do one real task
  with only `<cli> help` to go on. If it gets lost, the help text is wrong.

## What not to build

- **Direct database access from the CLI.** Two processes writing one
  SQLite file skip the app's checks, can run migrations from an older
  build, and the window does not see the change.
- **The WinUI exe as its own CLI** (`AttachConsole`, or switching the app
  to a console subsystem). The first has no exit code in a terminal; the
  second flashes a console window every time the app starts from Start.
- **A COM server or a Windows service in the app.** Registration,
  elevation, and a second process model for one job.
- **A URL protocol or window messages (`WM_COPYDATA`) as the main
  channel.** A URL launch returns no result, and window messages need a
  window handle and are blocked across elevation levels.
- **An MCP server without the CLI.** The CLI is what Claude Code and Codex
  use best, and the MCP server is a thin layer over the same commands.

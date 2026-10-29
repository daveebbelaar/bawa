---
name: agent-cli
description: Optional command line and MCP server for a Windows app, so Claude Code, Codex, and scripts can drive it (list, add, change, search, act) through the running app. Use when the user wants agents to work with their app or its data, asks for a CLI, an MCP server, or automation for the app, or says "Claude should be able to use my app".
argument-hint: "<cli-name, like tasks or mail>"
---

# Agent CLI

Give the app a command line, so an agent can do everything a person can
do in the window: `tasks items add --title "Call the dentist"`. A small
console exe is the CLI. Each command goes to the running app over a
private named pipe, runs there like a button press, and the window
updates live.

```text
Claude Code --shell--> tasks.exe items add ...  --named pipe--> Tasks.exe (runs the command, saves, redraws)
Claude Desktop --MCP--> tasks.exe mcp            --named pipe--> same commands, same app
```

A WinUI exe cannot be the CLI itself: it is a GUI program, so cmd and
PowerShell do not wait for it, there is no exit code to read, and its
stdout is not dependable. The CLI is a second project, `<App>.Cli`, that
compiles the same command list as the app.

## CLI, MCP, or URL protocol

| Way in | Who uses it | Ship it |
| --- | --- | --- |
| CLI | Claude Code, Codex, scripts, people in a terminal | Always. Agents learn it from `--help`, and it costs no context until used |
| MCP (`tasks mcp`) | Claude Desktop and other clients with no shell | Optional. The same commands, one tool each. One line to register |
| URL protocol (`tasks://`) | Links, Stream Deck, PowerToys Run | Separate work (`os-integrations` skill). It returns no result, so agents cannot use it |

The CLI talks to the running app, never to the SQLite file. Then there is
one writer, the app's checks and side effects run, and the window shows
the change at once. If the app is closed, the CLI starts it without
taking the focus. Rules and reasons: [rules.md](rules.md).

## 1. List the commands

Read the user stories (`docs/stories/`). Write each thing a person does as
a command, `<noun> <verb>`, and mark it:

- **read**: `items list`, `items show`, `status`. Safe to run any time.
- **write**: `items add`, `items update`, `open`. Changes data, can be undone.
- **destructive**: `items delete`, `mail send`. Only reports what it would
  do until `--yes` is given.

Add `status` (is it running, what is open, counts) and `open <page>` (show
a page in the window), so an agent can see and steer the app.

Check: every M1 story has its commands, and each command is marked.

## 2. Add the files

```powershell
& "${CLAUDE_SKILL_DIR}/scripts/add-agent-cli.ps1" <app-folder> <cli-name>
```

It copies `src\<App>\Agent\` (core, pipe, command list, handlers, host)
and the console project `src\<App>.Cli\` (CLI and MCP server), adds that
project to the `.slnx`, starts the pipe in `App.xaml.cs`, and makes
`build.ps1` install `%USERPROFILE%\.local\bin\<cli-name>.exe`. Apps not
made from the template: do the same by hand, following the script.

Check: the script printed "added the agent CLI", and `./build.ps1`
finishes with no errors or warnings.

## 3. Write the commands

Each command lives in two files under `src\<App>\Agent\`:

- `AgentCommands.cs`: the name, a one-line summary, the params, a full
  example, and the mark (`ReadOnly` or `Destructive`). The CLI project
  compiles this file too, so it holds no app types.
- `AgentHandlers.cs`: what the command does, under the same name. It runs
  on the UI thread with the app's `AppModel` and `MainWindow`, and calls
  the same model code as the window's buttons. Never copy logic: call the
  function the button calls.

Replace the examples with the app's own commands.

- Every model an agent touches has a `Guid Id`. Commands show the first 8
  hex characters as `id`, and accept any unique start of it.
- Results are small `JsonObject`s: the fields an agent needs, not the
  whole model. Lists take `--limit` and `--offset`, and return `total`
  and `nextOffset`.
- Errors throw `AgentError` with a message that says how to fix it:
  "no item with id 3f2a. Run `tasks items list`".

All the rules: [rules.md](rules.md).

Check: `<cli-name> help` lists every command from step 1,
`<cli-name> help <command>` shows its params and example, and
`tools/selftest.ps1` prints no `AGENT no handler` line.

## 4. Build and install

```powershell
./build.ps1
<cli-name> help
```

`build.ps1` installs the app and publishes the CLI as one exe to
`%USERPROFILE%\.local\bin\<cli-name>.exe`, so the CLI always matches the
app. Claude Code's native installer uses the same folder, so it is often
on PATH already. If the shell
cannot find the CLI, `build.ps1` says so; ask the user before you add the
folder to their user `Path`, then open a new terminal.

Check: `<cli-name> version` prints one build number for the CLI and the
running app.

## 5. Test

```powershell
& "${CLAUDE_SKILL_DIR}/scripts/check-agent-cli.ps1" <cli-name>
```

It starts a test copy of the app on a `%TEMP%` database, checks help,
JSON output, error codes, `--no-launch`, every read command, the version
match, and the MCP tool list and one tool call, then closes the test copy.
Then run each write command once by hand on a test store and look at the
window: the change shows without a reload.

```powershell
$env:APP_STORE = "$env:TEMP\<App>-test.db"
<cli-name> items add --title "Test" --json
Remove-Item Env:APP_STORE
```

Check: the script prints `PASS agent CLI`.

## 6. Teach agents

Write `.claude/skills/<cli-name>/SKILL.md` in the app's repo from
[teach-agents.md](teach-agents.md): what the app is, the safety rules,
the main commands, and "run `<cli-name> help` for the rest". To use the
app from any folder, also copy it to `%USERPROFILE%\.claude\skills\<cli-name>\`.
Add one line to the app's `CLAUDE.md`: "Drive the app with `<cli-name>`.
See `.claude/skills/<cli-name>/SKILL.md`."

Check: a fresh `claude -p "List my starred items with <cli-name>"` finds
the CLI and answers from real data.

## 7. MCP, when the user wants it

For Claude Desktop, or to get one approval prompt per tool:

```powershell
claude mcp add --scope user <cli-name> -- "$HOME\.local\bin\<cli-name>.exe" mcp
codex mcp add <cli-name> -- "$HOME\.local\bin\<cli-name>.exe" mcp
```

Claude Desktop: add it to its config file (Settings > Developer > Edit
Config) under `mcpServers`, with the full path to the exe and
`"args": ["mcp"]`. More in [teach-agents.md](teach-agents.md).

Check: `claude mcp list` shows the server as connected.

Done when every command from step 1 runs through the CLI against the
running app, `check-agent-cli.ps1` prints `PASS agent CLI`, the app's repo
has the agent skill from step 6, and a fresh Claude Code session can do a
real task with it.

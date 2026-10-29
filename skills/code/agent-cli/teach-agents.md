# Teach agents to drive the app

Agents learn a CLI from three places: a short skill that says when and how
to use it, `<cli> help`, and the errors. Write the skill; the CLI does the
rest.

## Contents

- The app's agent skill
- Where to put it
- Permissions
- MCP registration

## The app's agent skill

Save as `.claude/skills/<cli>/SKILL.md` in the app's repo. Replace the
angle brackets. Keep it under 60 lines: list the five commands used most,
and point at `help` for the rest.

```markdown
---
name: <cli>
description: Drive the <App> app on this PC from the shell with the `<cli>` command: <the main jobs, like "list, add, complete, and search tasks">. Use when the user asks about their <things> or wants to change them, or names <App>.
---

# <App>

`<cli>` runs commands in the <App> app. Changes show in its window at once.
If the app is closed, the command starts it without taking the focus.

## Rules

- Always add `--json` and read the JSON. Exit 0 is ok; otherwise read
  `error.code` and `error.message`, and do what the message says.
- Find ids with `<cli> <noun> list` or `--search`. An id is 8 characters.
- Destructive commands (<delete, send>) only report what they would do.
  Show the user that report and ask before you run it again with `--yes`.
- To try something without real data, point APP_STORE at a file in TEMP:
  `$env:APP_STORE = "$env:TEMP\<cli>-test.db"` in PowerShell (then
  `Remove-Item Env:APP_STORE`), or `APP_STORE="$TEMP/<cli>-test.db" <cli> ...`
  in bash.

## Main commands

| Command | Does |
| --- | --- |
| `<cli> status --json` | Is the app running, what is open, counts |
| `<cli> <noun> list [--search T] [--limit N] --json` | Find things |
| `<cli> <noun> add --title T --json` | Add one |
| `<cli> <noun> update <id> [--field value] --json` | Change one |
| `<cli> open <page>` | Show a page in the window |

Everything else: `<cli> help`, and `<cli> help <command>` for its options.
```

## Where to put it

| Place | Who sees it |
| --- | --- |
| `.claude/skills/<cli>/SKILL.md` in the app's repo | Agents working on the app's code |
| `%USERPROFILE%\.claude\skills\<cli>\SKILL.md` | Claude Code in any folder: "add milk to my tasks" works everywhere |
| `%USERPROFILE%\.codex\skills\<cli>\SKILL.md` (or `AGENTS.md`) | Codex |

Copy the same file to each place the user wants. Update all of them when a
command changes.

## Permissions

Claude Code asks before each shell command. Allow the read commands for
the user, in `.claude/settings.json` or `%USERPROFILE%\.claude\settings.json`:

```json
{ "permissions": { "allow": ["Bash(<cli> status:*)", "Bash(<cli> help:*)", "Bash(<cli> <noun> list:*)", "Bash(<cli> <noun> show:*)"] } }
```

Claude Code on Windows can also run commands through its PowerShell tool.
Allow one read command once in a session, then copy the rule that
`/permissions` shows for it. Leave write and destructive commands out, so
the user approves each one. Ask the user before changing their settings.

## MCP registration

Only when the user wants MCP (Claude Desktop, or one prompt per tool).
Always the full path to the exe: MCP clients do not read the shell's
`PATH`.

```powershell
claude mcp add --scope user <cli> -- "$HOME\.local\bin\<cli>.exe" mcp
claude mcp list            # shows <cli> as connected
codex mcp add <cli> -- "$HOME\.local\bin\<cli>.exe" mcp
```

Claude Desktop: open Settings > Developer > Edit Config, and add:

```json
{ "mcpServers": { "<cli>": { "command": "C:\\Users\\<you>\\.local\\bin\\<cli>.exe", "args": ["mcp"] } } }
```

Backslashes in JSON are doubled. Restart Claude Desktop after editing.

Tools are named like the commands with `_` for spaces (`items_add`), carry
`readOnlyHint` and `destructiveHint`, and take the same options as the CLI.

A running MCP server keeps `<cli>.exe` open. `build.ps1` renames the old
exe so the build can install the new one; restart the MCP client to pick
it up.

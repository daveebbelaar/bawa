# BAWA

Agent skills for building native Windows apps. Installed as a Claude Code and
Codex plugin from `.claude-plugin/`, or through `npx skills add`.
`AGENTS.md` at the root is a copy of this file (`.claude/CLAUDE.md`);
keep them identical.

BAWA is the Windows sibling of [BAMA](https://github.com/daveebbelaar/bama)
(Build Any Mac App). Same pipeline, same skill names where the job is the
same, same writing rules. Only the stack differs.

## Stack

- C# 14 on .NET 10, WinUI 3 (Windows App SDK 2.x), XAML with `x:Bind`.
- CommunityToolkit.Mvvm for observable models, CommunityToolkit.WinUI
  SettingsControls for the Settings page.
- SQLite through Microsoft.Data.Sqlite in a `<App>.Core` library (no WinUI),
  tested with xUnit v3.
- Unpackaged: a plain `.exe` with the Windows App SDK runtime in the app
  folder. No MSIX, no Store, no Visual Studio. The .NET SDK is the only
  install (`winget install Microsoft.DotNet.SDK.10`).
- Scripts are PowerShell that runs in Windows PowerShell 5.1 and in pwsh 7.
- Windows 11 first (Mica, the TitleBar control, rounded corners), Windows 10
  19041 or later still runs.

## Commands in a BAWA app

| Job | Command |
| --- | --- |
| Build and install to `%LOCALAPPDATA%\Programs\<App>` plus a Start menu entry | `./build.ps1` (`-Dev` debug, `-Open` start it) |
| Unit tests | `dotnet test tests/<App>.Core.Tests` |
| Self-test on a seeded `%TEMP%` store | `tools/selftest.ps1` |
| Snapshots of every page in light and dark | `tools/snap.ps1` (`-Pages all,settings`, `-Accent green`, `-Grid`) |
| Grid checks | `tools/grid-lint.ps1`, `tools/check-layout.ps1` |
| Icon from `Icon/AppIcon.svg` | `tools/make-icon.ps1 -Review` |
| A build for other PCs (no .NET needed), checked by the self-test | `tools/publish.ps1` (`-Aot` NativeAOT, `-Zip`) |

Environment flags the app reads: `APP_STORE` (a file in `%TEMP%`),
`APP_SEED=1`, `APP_SNAPSHOT=<dir>`, `APP_SNAPSHOT_PAGES`, `APP_ACCENT`,
`APP_GRID=1`, `APP_LOG=<file>`, and the `--selftest` argument. The tools run the installed
app, or the build in `APP_EXE`.

## Layout

- `skills/<stage>/<name>/SKILL.md`, stages in pipeline order: `start`, `plan`,
  `code`, `design`, `ship`.
- References sit next to `SKILL.md` (`recipes.md`, `patterns.md`) and are
  linked relatively. Scripts go in `scripts/`, templates in `template/`.
- `agents/openai.yaml` in every skill: Codex display name and short
  description. A user-invoked skill also sets
  `policy.allow_implicit_invocation: false`.
- `.claude-plugin/plugin.json` lists every shipped skill in `skills`.
- `specs/<app>.md`: build plans for small apps, the Windows versions of
  BAMA's specs. Each has Brief, How it works, Stories, Gotchas, and a
  "Done when" line, in that order. `specs/README.md` lists them.
- `examples/`: apps built with BAWA from a spec, kept as proof that the
  pipeline works on a real Windows PC.

## Writing skills

Follow Matt Pocock's `writing-for-agents` rules:

- The description is a **context pointer**: one short sentence on what it
  is, then "Use when" and one trigger per branch. Front-load the leading
  word.
- Inline what every branch needs. Push what only some branches need into a
  sibling file behind a link.
- Every step ends on a checkable condition. Every skill ends with a
  "Done when" line.
- Prompt the positive. Use a prohibition only as a hard guardrail, next to
  the positive target.
- Cache only what the environment cannot tell the agent: conventions,
  reasons, gotchas from real apps.
- One skill calls another with "Call the Skill tool with `<name>`".
- No em dashes. Plain words, short sentences, active voice.
- Windows words: "epx" (effective pixels) for sizes, Ctrl not Command,
  "the taskbar" and "Start" not "the Dock", sentence case for UI labels
  ("New item", not "New Item").

## Adding a skill

1. Create `skills/<stage>/<name>/SKILL.md` and `agents/openai.yaml`.
2. Add it to `plugin.json` `skills`, the README table, and the router
   (`skills/start/bawa/SKILL.md`).
3. Run `claude plugin validate . --strict`.
4. Keep `version` in `plugin.json` at 1.0.0 until the first release. After that, bump it, so installed copies update.

---
name: research
description: Online research for a Windows app before building it, run as parallel researcher subagents, saved to docs/research/. Use when the user asks what to build, which features matter, how the best apps in a category work, how a Windows API or integration works, or wants a feature comparison.
argument-hint: "<app category or question>"
---

# Research

Research answers three questions before code exists: what the best apps in
the category do, what users love and hate about them, and how Windows lets
you build it. The output feeds `app-brief` and `user-stories`.

## 1. Frame

Write the question in one line, and the decision it informs ("which
features go in version one", "RegisterHotKey or a low-level keyboard
hook"). If the user gave only a category, the question is "what makes the
best <category> app on Windows".

## 2. Fan out

Start one subagent per track, in parallel, with the brief below. Pick the
tracks that fit. Four is typical. Market and Reviews always run. Reviews
covers many apps: give each subagent 3 or 4 apps, or the 600 words run out.

| Track | Finds |
| --- | --- |
| Market | The 5 to 8 best apps in the category, their price, platform, and headline features |
| Reviews | Per app: what people like and what they dislike, from Microsoft Store reviews, Reddit (r/windows, r/Windows11), Hacker News, forums, and "switched from X" posts |
| Features | The full feature list of the 2 or 3 leaders, from docs, changelogs, and help pages |
| Design | Screens, interaction details, and keyboard shortcuts of the leaders, and how they use Windows 11 design (Mica, the title bar, navigation, the tray) |
| Platform | The Win32, WinRT, and Windows App SDK APIs, permissions, and limits each feature needs, and what other apps on this PC already use (global shortcuts, protocol handlers, file associations, startup entries) |

Subagent brief (fill in the brackets):

```text
Research [track] for a native Windows app that [one-line idea].
Question: [the question]. Decision it informs: [decision].
Search the web. Prefer primary sources: official docs, changelogs,
Microsoft Learn (learn.microsoft.com: Win32, WinRT, Windows App SDK, and
the Windows design guidance), Windows App SDK release notes, Microsoft
Build sessions, the app's own help pages. Use reviews and forums for
opinions only. If a source is blocked, say so and use another (Hacker
News, Microsoft Store reviews, Windows forums). Do not write any files.
Tag every claim: [V] verified in a source (give the URL), [K] from your
own knowledge, [U] unverified, must be tested.
Return at most 600 words: a findings list, a table where it helps, and
the 3 things that matter most for the decision. No intro, no summary.
```

The Platform track checks two things that trip Windows apps. Is the API
there for an unpackaged app (a plain `.exe` with no package identity)?
Some WinRT and Windows App SDK features need identity, and the BAWA
template ships without it. And which Windows version has it: the template
runs on Windows 10 19041 and later, Windows 11 first.

## 3. Merge

Write one file per question: `docs/research/<topic>.md`.

```markdown
# <Topic>

Research for <AppName>. Done <YYYY-MM-DD>.
[V] verified in a source, [K] from knowledge, [U] unverified, test first.

## Findings
- ... [V](url)

## Likes and dislikes
| App | People like | People dislike | Sources |
| --- | --- | --- | --- |
| App A | <3 to 5 points, most mentioned first> | <3 to 5 points> | <urls> |

Patterns: <likes that repeat across apps (must have), dislikes that repeat
(our chance to do better)>

## Feature matrix
| Feature | App A | App B | App C | Version one? |

## Decisions
1. <decision>, because <finding>. Planned test: <test, for a [U] finding>.

## Open questions
- <[U] claim and how to test it>
```

Keep the feature matrix: `user-stories` turns its "Version one?" column
into stories. Every "People like" point maps to a decision or to an
"accepted gap" line in the brief, so nothing loved is dropped by accident.
Check whether a service people already use gives sync and phone capture
for free: Microsoft To Do and Outlook calendars through Microsoft Graph,
or plain files in the OneDrive folder (export files, never the live
SQLite database: OneDrive and its `-wal` file clash). Turn each `[U]` that
a decision depends on into a test before you trust it (a probe script, a
self-test step in `tools/selftest.ps1`, or a preview field in the
reference app).

## 4. Hand off

Call the Skill tool with `clone` if one app stands out as the reference
and you can open it. If it is not installed, or computer use and a browser
are not available, link its public help pages and screenshots in the
research file instead. Then call the Skill tool with `app-brief`.

Done when every track returned, the file has a likes and dislikes table
for every app in the market list, a feature matrix, and a decision list,
and every decision cites a `[V]` or a planned test.

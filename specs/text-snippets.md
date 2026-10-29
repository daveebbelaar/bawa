# Text snippets

Type a short keyword like `;sig` in any app and it turns into the full text.
Replaces: PhraseExpress, and AutoHotkey hotstrings. Ported from BAMA's text
snippets spec.

## Brief

```markdown
# Snippets

A text expander for one person: type a keyword anywhere and get the full text, with today's date or the clipboard filled in.

## Replaces
PhraseExpress and AutoHotkey hotstrings, and typing the same lines over and over.

## Main jobs
1. Type a keyword like ;sig in any app and have it replaced with the text.
2. Add, edit, turn off, and delete snippets in one list.
3. Fill in placeholders: date, time, clipboard, a new UUID, and where the cursor ends up.

## Data
- Snippet: id, keyword, expansion, enabled. Snippets.db (SQLite) in %LOCALAPPDATA%\Snippets

## Shape
Tray icon plus a Settings window that holds the snippet list. No panel.
Windows 11 first, Windows 10 19041 or later still runs. Local data. Works offline. No accounts.

## System hooks
Low-level keyboard and mouse hooks, synthetic key events (SendInput), foreground change events, tray icon.

## Reference
PhraseExpress, AutoHotkey hotstrings

## Look
Native Fluent Settings page with Mica. Each keyword in a Cascadia Mono chip with an accent
tint, the text preview next to it, a toggle switch per snippet.

## Not in version one
- Groups and folders
- Fill-in forms and per-app rules
- Rich text and images in snippets
```

## How it works

| Part | API | Permission |
| --- | --- | --- |
| Watch typing | `SetWindowsHookEx(WH_KEYBOARD_LL)` on its own thread with a `GetMessage` loop | None |
| Read typed characters | `ToUnicodeEx` with flag 0x4 (keep the keyboard state), layout from `GetKeyboardLayout` of the foreground window's thread | None |
| Reset on clicks | `SetWindowsHookEx(WH_MOUSE_LL)` on the same thread | None |
| Reset on app switch | `SetWinEventHook(EVENT_SYSTEM_FOREGROUND, ..., WINEVENT_OUTOFCONTEXT)` | None |
| Notice a dropped hook | `GetLastInputInfo`, checked every 2 s | None |
| Delete the keyword | `SendInput` with `VK_BACK` | None (UIPI blocks apps running as administrator) |
| Type short text | `SendInput` with `KEYEVENTF_UNICODE` | Same |
| Paste long text | Clipboard plus Ctrl+V, then put the old clipboard back | Same |
| Storage | Snippets.db (SQLite, Core) | None |
| Tray icon | `Shell_NotifyIcon`, `TrackPopupMenuEx` | None |

Recipes: [low-level keyboard hook](../skills/code/os-integrations/recipes.md#low-level-keyboard-hook)
(listen only: the hook never swallows a key),
[paste into the front app](../skills/code/os-integrations/recipes.md#paste-into-the-front-app),
[tray icon](../skills/code/os-integrations/recipes.md#tray-icon),
[single instance](../skills/code/os-integrations/recipes.md#single-instance),
[elevation and UIPI](../skills/code/os-integrations/recipes.md#elevation-and-uipi).

Types to create:

- `Snippet` (Core; `IsReady` means a keyword of 2 or more characters and some text) and `SnippetStore` (Core, SQLite, saved on every change, three starters on first run).
- `SnippetMatcher` (Core): pure buffer logic; feed it characters and reset keys, it returns a match. Unit tested.
- `SnippetEngine`: owns the matcher, the pending expansion, and `Expand`.
- `KeyboardHook`: installs both hooks on a dedicated thread, skips the app's own events, reinstalls when Windows drops them.
- `SyntheticInput` (`Press`, `Type`, `Paste`, all marked) and `ClipboardSnapshot` (`Take`, `Restore`, `PasteTransient`).
- `Placeholders` (Core): pure `Resolve(text, now, clipboard)`. Unit tested.
- `SettingsPage`, `SnippetRow`, `SnippetDialog` (a `ContentDialog`), `TrayIcon`.

Exact behavior:

- Starters: `;date` gives `{date}`, `;time` gives `{time}`, `;shrug` gives `¯\_(ツ)_/¯`.
- Buffer: append the characters `ToUnicodeEx` returns for each key down. Trim it to the length of the longest ready keyword.
- Backspace removes the last buffer character. Ctrl or Win held resets the buffer, except AltGr (Right Alt, which Windows reports as Ctrl plus Alt), which types characters like @ on German layouts.
- Track Shift, Ctrl, Alt, Win, and Caps Lock from the hook's own events, and pass that state to `ToUnicodeEx`.
- Reset on: Enter, Tab, Esc, arrows, Home, End, Page Up, Page Down, Delete, any mouse button down, and a foreground window change.
- Match: of the enabled, ready snippets, the longest keyword that the buffer ends with wins, so `;dates` beats `;date`.
- On a match: clear the buffer, store it as pending, and expand 20 ms later on the UI thread, never inside the hook. The hook runs before the app gets the key, so the last typed character needs that time to land.
- While pending: collect typed characters as "extra". Backspace removes the last extra (or cancels when none). Ctrl, Win, or a reset key cancels.
- Expand: skip when the foreground app is elevated. Resolve placeholders, cut out `{cursor}` and count the text elements after it (`StringInfo.LengthInTextElements`). Press Backspace `keyword.Length + extra.Length` times.
- Text with a newline or over 60 characters: paste it. Shorter text: type it with `KEYEVENTF_UNICODE`, a down and an up per UTF-16 unit, all in one `SendInput` call.
- After that: type the extra characters again. With no extra and a `{cursor}`, press Left that many times, 50 ms later.
- Paste path: snapshot every clipboard format that holds an `HGLOBAL`, write the text as `CF_UNICODETEXT` plus the app's own format, `ExcludeClipboardContentFromMonitorProcessing`, and `CanIncludeInClipboardHistory` set to 0. Send Ctrl+V. Restore the snapshot 350 ms later, with the same marks.
- Placeholders: `{date}` is `yyyy-MM-dd`, `{time}` is `HH:mm`, `{date:PATTERN}` takes any .NET custom date format (`DateTime.ToString(pattern, CultureInfo.CurrentCulture)`), `{uuid}` is a new `Guid`, `{clipboard}` is the current clipboard text, `{cursor}` marks where the cursor ends up.
- Editor: Save is off until the snippet is ready and no other snippet has the same keyword (trimmed). Ctrl+S saves. Delete asks first.
- Watchdog: every 2 s, compare `GetLastInputInfo` with the last call of either hook. When input is newer by more than 1 s, Windows dropped the hooks: install them again.

The keyboard hook callback. Skip your own events and never swallow a key:

```csharp
[UnmanagedCallersOnly]
static unsafe nint OnKey(int code, nint wParam, nint lParam)
{
    if (code >= 0)
    {
        var key = *(KBDLLHOOKSTRUCT*)lParam;
        LastHookCall = (uint)Environment.TickCount;   // same clock as GetLastInputInfo
        if (key.dwExtraInfo != SyntheticInput.Marker)  // text this app typed comes back here too
            Engine.Post(key.vkCode, key.scanCode, wParam is WM_KEYDOWN or WM_SYSKEYDOWN);
    }
    return CallNextHookEx(0, code, wParam, lParam);   // listen only
}
```

## Stories

```markdown
# Stories

## M1 Core loop

### S-001 Start expanding at once
As the user, I open Snippets for the first time and it works without any setup.
Screens: settings-snippets
Acceptance:
- [manual] Right after the first launch, typing ;shrug in Notepad expands, with no prompt.
- [snap] Settings shows the three starters and the footer "Snippets cannot read or type into apps running as administrator."
- [manual] Starting Snippets a second time opens the first copy's Settings; ;shrug still expands once, not twice.
Status: todo

### S-002 Expand a snippet while typing
As the user, I type ;shrug in Notepad and it becomes ¯\_(ツ)_/¯.
Depends: S-001
Acceptance:
- [unit] SnippetMatcher fed ";shrug" returns the shrug snippet; fed ";shru" returns null.
- [unit] Backspace removes the last buffer character; Enter, Tab, arrows, and Ctrl combos reset it; AltGr characters do not.
- [manual] The keyword is gone and the text is in its place, in Notepad, Word, and Edge.
- [unit] With ;date and ;dates set, ";dates" picks ;dates; snippets that are off or not ready never match.
- [manual] The app's own typed text never triggers another expansion.
Status: todo

### S-003 Keep what I type during the expansion
As the user, I type ;shrug and keep typing fast; nothing I typed is lost.
Depends: S-002
Acceptance:
- [manual] Typing ";shrug ok" quickly gives "¯\_(ツ)_/¯ ok".
- [manual] Pressing an arrow key right after the keyword cancels the expansion.
Status: todo

### S-004 Paste long text and keep my clipboard
As the user, a three-line signature expands at once and my clipboard is unchanged after.
Depends: S-002
Acceptance:
- [manual] Text with a newline or over 60 characters arrives in one paste.
- [manual] An image on the clipboard before the expansion is still there 1 s after.
- [manual] Win+V history and Ditto do not save the expansion.
Status: todo

### S-005 Date and time placeholders
As the user, ;date gives today's date as 2026-10-24.
Depends: S-002
Acceptance:
- [unit] Resolve("{date}") is yyyy-MM-dd, "{time}" is HH:mm, "{date:dddd}" is the weekday name.
Status: todo

### S-006 Add a snippet
As the user, I click "Add snippet", type a keyword and text, and save.
Screens: settings-snippets, snippet-dialog
Acceptance:
- [snap] The dialog has Keyword (Cascadia Mono, placeholder ;sig), Text, an On switch, Cancel, and "Add snippet".
- [manual] "Add snippet" stays off until the keyword has 2 or more characters and the text is not empty.
- [snap] A keyword another snippet uses shows "Another snippet already uses this keyword."
- [self] Snippets survive a restart (Snippets.db); a first run has the three starters.
Status: todo

### S-007 Edit, turn off, or delete a snippet
As the user, I click a row to edit it, flip its switch, or delete it.
Screens: settings-snippets
Depends: S-006
Acceptance:
- [snap] A turned-off row is dimmed; a row that is not ready shows an orange "Not ready" badge.
- [manual] Delete asks "Delete ;sig?" first.
Status: todo

### S-008 Use the tray icon
As the user, I right-click the tray icon to turn expansion on or off, open Settings, or quit.
Acceptance:
- [unit] The menu model has "Expand snippets" with a check mark, Settings, and Quit.
- [manual] With expansion off, keywords stay as typed.
- [manual] Settings shows in the taskbar while open; closing it keeps the app running in the tray.
Status: todo

## M2 More placeholders

### S-009 Clipboard and UUID placeholders
As the user, {clipboard} inserts what I copied and {uuid} a new UUID.
Depends: S-005
Acceptance:
- [unit] Resolve with a clipboard string fills {clipboard}; two {uuid} resolves differ.
Status: todo

### S-010 Put the cursor inside the text
As the user, a snippet "Hi {cursor}," leaves the cursor before the comma.
Depends: S-005
Acceptance:
- [unit] Cutting {cursor} returns the text without it and the count of text elements after it.
- [manual] In Notepad the cursor lands where {cursor} was.
Status: todo

### S-011 Insert a placeholder from the editor
As the user, I click a placeholder button under the text field and it is added to the text.
Screens: snippet-dialog
Depends: S-006
Acceptance:
- [snap] Buttons for {date}, {time}, {date:dddd d MMMM}, {clipboard}, {uuid}, {cursor}.
Status: todo

## Dropped
- One shared keyboard hook that also feeds a window switcher and a keystroke overlay.
- Activity stats (expansion counter).
```

## Gotchas

- The app's own key events come back through its own hook and could match again. Set `dwExtraInfo` to one marker on every `INPUT` you send, and skip events with that marker in the callback.
- Windows silently removes a low-level hook whose callback is too slow (the `LowLevelHooksTimeout`, at most 1 s since Windows 10 1709), and nothing tells the app. Run the hooks on their own thread with a message loop, only post work from the callback, and run the `GetLastInputInfo` watchdog.
- A hook on the UI thread waits for the UI. Any slow layout or open dialog then delays every keystroke on the PC, and gets the hook removed. Keep it on the dedicated thread.
- A hook delegate that the garbage collector frees crashes the app on the next key. Use an `[UnmanagedCallersOnly]` static method and a function pointer, or keep the delegate in a static field.
- The hook runs before the app gets the key, so expanding inside it deleted the wrong characters. Wait 20 ms on the UI thread, count characters typed in that window into the Backspace presses, and type them again after the text.
- `GetAsyncKeyState` inside the callback reads the state from before this key, and `GetKeyboardState` reads your own thread's state. Track the modifiers yourself from the hook's events.
- `ToUnicodeEx` changes the kernel's dead-key state by default, so é typed as ´ then e came out as two characters in the target app. Pass flag 0x4 (Windows 10 1607 and later) so it leaves the state alone. Use the layout of the foreground window's thread, not your own.
- AltGr arrives as Ctrl plus Alt. Treating it as a Ctrl combo reset the buffer on every @ typed on a German keyboard.
- A one-letter keyword or empty text eats characters while you type. Only snippets with a keyword of 2 or more characters and some text fire. Tell users to start keywords with `;`.
- Without resets the buffer matched across contexts: half a keyword in one app plus the rest in another. Reset on clicks, foreground changes, arrows, Enter, Tab, Esc, and Ctrl or Win combos.
- One `SendInput` call is never interleaved with other input, so send the whole short text in one call. Keep both halves of a surrogate pair in it, or emoji break.
- `KEYEVENTF_UNICODE` for a line break gives different results per app. Text with `\n` or over 60 characters goes through the clipboard and Ctrl+V.
- `SendInput` with an `INPUT` struct that holds only `KEYBDINPUT` sends nothing and returns 0. Declare the full union, so `Marshal.SizeOf<INPUT>()` is 40 on 64-bit Windows.
- Snapshot every clipboard format that holds an `HGLOBAL`, not only the text, or an image the user had copied is gone after an expansion. Skip `CF_BITMAP`, `CF_PALETTE`, `CF_METAFILEPICT`, and `CF_ENHMETAFILE`: they are GDI handles, and Windows makes `CF_BITMAP` again from `CF_DIB`. Restore 350 ms after the paste.
- While an app running as administrator is in front, the hook sees no keys and `SendInput` into it is blocked by UIPI without an error. Expansion simply does not happen there. Say so in Settings.
- Two running copies means two hooks, and every keyword expands twice. Make the app single instance and send the second launch to the first copy's Settings.
- The buffer holds what the user types, passwords included. Never write it to `AgentLog` or a file; log lengths and snippet ids only.
- `{date:PATTERN}` uses .NET custom date formats, not the Unicode patterns of macOS: the weekday is `dddd`, not `EEEE`. `d MMMM` works the same in both.
- Closing the Settings window must not end the app. Cancel `AppWindow.Closing` and call `AppWindow.Hide()`; the tray icon keeps running.
- Changed from BAMA: the 20-unit chunks are gone, since `SendInput` has no such limit and one call is not interleaved. The cursor count uses text elements, so an emoji or `\r\n` after `{cursor}` is one Left press. There is no permission to grant, so the Accessibility story became "Start expanding at once".

Done when typing `;shrug` in Notepad gives `¯\_(ツ)_/¯`, `;date` gives today's date, a three-line snippet pastes and the old clipboard comes back, and snippets survive a restart.

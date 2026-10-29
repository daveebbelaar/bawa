# Writing patterns

Patterns for the words in a Windows app. Each one has a rule and a before
and after. Most rules come from the Windows design guidance on writing
style and the Microsoft Writing Style Guide, both on Microsoft Learn.

## Contents

- Voice
- Names
- Buttons and menu items
- Titles
- Empty states
- Errors
- Dialogs
- Confirmations
- Undo
- Settings
- Fields and placeholders
- Permissions
- Progress
- Tooltips
- Keyboard shortcuts
- Counts and plurals
- Words to cut

## Voice

Pick three words for the app's voice and keep them in `docs/GLOSSARY.md`
under `## Voice`, for example "calm, direct, brief". Tone changes with the
moment: plain and serious for errors, a little warmer for a finished goal.
Windows' own voice is warm, ready to help, and crisp.

- Talk to people as "you". Never "the user".
- Say "select", never "click" or "tap": it fits a mouse, a pen, a finger,
  and the keyboard.
- Use contractions where they sound natural: "can't", "don't", "you're".
- No idioms or jokes that do not translate. Explain a technical term the
  first time, or drop it.

## Names

A good name fits the app's other words, sets the right expectation, and
works in every language and market.

- Name a feature by what it does for the person, not by the technology:
  "Enhance dialogue", not "Isolate vocals" or "Voice separation".
- A toggle gets a verb phrase that answers "what" and "for whom".
- Avoid clever names that raise questions. A label must not sound like a
  judgment when its value is low or zero ("Spending power: $0").
- Where trust matters (money, health, privacy), use the industry word
  ("Balance", not "Current funds").
- Reuse words already in the app. A new name for an old idea is a bug.
- Test each name in sentences: "Turn on Enhance dialogue", "Search for
  it in Recent". If the sentence sounds odd, the name is wrong.

To find a name: write who it is for, then three lists of words, what the
feature makes people think, feel, and do. Group them into themes, cut what
fails the rules above, and try the rest in sentences.

## Buttons and menu items

Start with a verb. Name the object when the verb alone is unclear. Keep
it to a couple of short words.

| Before | After |
| --- | --- |
| OK | Save |
| Let's go! | Create project |
| New | New task |
| Export | Export… (it opens a save dialog first) |
| Click here to learn more | Learn about smart lists |

- Sentence case on buttons and menu items. Capitalize the first word and
  proper nouns only.
- No period at the end of a button, label, check box, or radio button.
- An ellipsis means "asks for more before it acts". Leave it off commands
  that act at once.
- Multi-step flows start with "Get started", move with "Next", and end
  with "Done". Keep the same words in every flow.

## Titles

Window titles name the content first, then the app: "Today - Tasks". The
template's `MainWindow` sets this on every page. Dialog titles name the
task.

| Before | After |
| --- | --- |
| Tasks | Today - Tasks |
| Edit | Rename project |
| Settings Window | Settings |

## Empty states

Say what the page is for and give the first action as a button.

| Before | After |
| --- | --- |
| Nothing here. | No tasks. Tasks you add appear here. [Add task] |
| Your inbox is empty! 🎉 | All done. New tasks land here first. |

Keep critical information out of empty states. They disappear.

## Errors

Say what happened and how to fix it, next to the problem, without blame.
Full sentences end with a period.

| Before | After |
| --- | --- |
| Oops! Something went wrong. | Can't save the task. The disk is full. |
| Invalid name. | Use letters and numbers only. |
| Password too short. | Choose a password with at least 8 characters. |
| Error 401 | Your session ended. Sign in again. |
| An error occurred while loading data. | Can't load tasks. Check your connection and try again. |

Say what to do, not what is forbidden: "Use only letters for your name",
not "Numbers are not allowed".

When the same error keeps happening to many people, change the design, not
the message.

## Dialogs

- The title and the buttons are a call and response: the buttons answer
  the title's question.
- A title is a short phrase in sentence case, with no period: "Can't
  export". A question ends with a question mark: "Delete 3 tasks?"
- At most two lines of content. Never "Error", "Warning", or an error code
  as the title.
- The content never explains the buttons. The buttons name the action.

## Confirmations

Ask only for rare actions that cannot be undone. The title is a question
with the verb. The button repeats the verb.

| Before | After |
| --- | --- |
| Are you sure? [Yes] [No] | Delete 3 tasks? [Delete] [Cancel] |
| Warning! This cannot be undone. [OK] | Empty the bin? You can't undo this action. [Empty bin] [Cancel] |

## Undo

Name every undoable action, so the Edit menu or the Undo button's tooltip
reads "Undo complete task", not "Undo". WinUI has no undo manager: the
app keeps its own stack, with one name per entry.

```csharp
undo.Push("Complete task", () => item.IsDone = false);
```

## Settings

Label the setting by what it does. Describe the on state only; people
infer the off state. Link to a setting instead of describing where it is.
The template's Settings page uses one `SettingsCard` per setting: a short
header and a one-line description.

| Before | After |
| --- | --- |
| Enable notifications for tasks that are due if the app is not open | Notify me when a task is due |
| Sync | Keep tasks on all my PCs |

## Fields and placeholders

Every field has a visible label (`Header` on a TextBox). The placeholder
shows an example or the format, never the only label.

| Before | After |
| --- | --- |
| (placeholder only) Email | Header "Email", placeholder "name@example.com" |
| Enter date | Header "Due date", placeholder "Tomorrow, Friday, Oct 30" |

## Permissions

Ask at the moment of first use, never at launch, unless the app cannot
work at all without it. The reason is one short, complete sentence in the
active voice, in sentence case, ending with a period. It says how and why.

| Before | After |
| --- | --- |
| This app needs access to improve your experience | Snap reads the screen to find the window under the pointer. |
| Microphone access required | Recordings use the microphone to capture your voice. |

Microphone, camera, and location for desktop apps follow switches in
Settings > Privacy & security. When access is off, the call fails. Show
the reason in the app's own row first, then one button, "Open settings",
that opens the right page (`ms-settings:privacy-microphone`,
`ms-settings:privacy-webcam`, `ms-settings:privacy-location`). Never send
people to Settings without saying why first.

## Progress

Say what is happening, with a count when there is one.

| Before | After |
| --- | --- |
| Loading... | Importing 12 photos... |
| Processing | Summarizing your notes... |
| Please wait | Exporting "Q3 report"... |

## Tooltips

These rules cover tooltips on controls. An information tooltip (the full
text of a trimmed value, a chart value, why a control is disabled) just
shows that information.

Say what the control does, starting with a verb, then add its shortcut in
parentheses: "(Ctrl+N)", "(Ctrl+Shift+Left)". Do not repeat the
control's name: the tooltip adds what the name does not say. Sentence
case, no period for a fragment, at most 60 to 75 characters. Skip
tooltips on buttons whose text label already says it all.

| Before | After |
| --- | --- |
| (none) | Add a task to this list (Ctrl+N) |
| New task (Ctrl+N) | Add a task to this list (Ctrl+N) |
| Click this button to create a new task in the current list | Add a task to this list (Ctrl+N) |

## Keyboard shortcuts

Write keys the way Windows does: "Ctrl", "Alt", "Shift", the Windows logo
key as "Windows", joined with a plus sign and no spaces: "Ctrl+Shift+N",
"Alt+F4", "Windows+V". Function keys are "F2". Use the standard
shortcuts for standard jobs: Ctrl+N new, F2 rename, Ctrl+F find, F5
refresh, Ctrl+Z undo, Ctrl+Y redo, Delete delete.

## Counts and plurals

Write both forms and pick one by the count. Never "1 task(s)".

```csharp
string Left(int count) => count == 1 ? "1 task left" : $"{count} tasks left";
```

An app that ships in other languages keeps both forms in its resources,
because some languages have more than two.

## Words to cut

- "we", "our": Windows' own guidance allows "we" for the app, but in a
  small app it is unclear who that is. "Can't sync", not "We couldn't
  sync".
- "Oops", "Uh-oh", "Whoops": sound insincere in an error.
- "Simply", "just", "easily": they blame people who find it hard.
- "Please": once in a while is fine, every message is noise.
- "Your", "my": "Favorites" already means yours.
- "Click", "tap": say "select".
- Code words: entity, record, payload, null, sync token, 404, HRESULT.

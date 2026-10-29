# AI features

Rules for a feature that uses a model: a summary, generated text, a
suggestion, a smart sort. From Microsoft's Guidelines for Human-AI
Interaction (the HAX Toolkit) and the Windows AI docs on Microsoft Learn
(October 2026). A model is sometimes wrong, so the design plans for that.

## Contents

- Say where AI is used
- Keep people in control
- Show progress
- Privacy and models
- When it fails
- Check

## Say where AI is used

- Mark generated content, for example a sparkle glyph and the word
  "Summary". Never pass it off as written by a person.
- Say what the feature does in plain words in its first view, not in a
  separate help page.

## Keep people in control

- Edit, Undo, and Retry sit next to every result. Accepting is one click;
  so is throwing it away.
- Show a preview before anything changes. Confirm before an action that
  cannot be undone, and never let a model start a destructive action on
  its own.
- Offer a few alternatives when people are choosing (three titles, not
  one).
- When people correct a result, use the correction and show that you did.
- Optional thumbs up or down for feedback, never a prompt that interrupts.

## Show progress

Say what the model is doing, specifically. Show partial results as they
stream in when the format allows.

| Before | After |
| --- | --- |
| Processing... | Summarizing key themes from 14 notes... |
| Thinking... | Drafting a reply to Anna... |

## Privacy and models

- Prefer an on-device model for personal data. The Windows AI APIs in the
  Windows App SDK (`Microsoft.Windows.AI.Text.LanguageModel`) run a local
  model on Copilot+ PCs and some GPUs. Call `LanguageModel.GetReadyState()`
  before showing the feature: `NotSupportedOnCurrentSystem` hides it,
  `NotReady` means a download.
- A model download can be several GB. Ask before calling
  `EnsureReadyAsync`, and say how big it is and where people can remove it
  later. Call it "the language model", not by its product name.
- The local model is changing: Phi Silica needs a Limited Access Feature
  token, and Aion Instruct, which replaces it from early 2027, does not.
  Check the Windows AI pages on Microsoft Learn for the current model,
  hardware, and whether an unpackaged app can call it, and run a probe on
  this PC before a story depends on it.
- When data goes to a server, say what goes, where, and why, before the
  first use.
- Ask how the feature could be misused and who it could harm. Drop it when
  the risk is bigger than the value.

## When it fails

- The model is unavailable (unsupported PC, not downloaded, no network):
  the feature explains why and the app still works without it. Every AI
  action has a manual path.
- A refused or blocked request (the content filter said no) says so
  plainly and shows an example of an input that works: "Can't use that
  description. Try: a short note about a meeting."
- A wrong result costs one click to undo, never a person's work.

## Check

Done when generated content is marked, every result has Edit, Undo, and
Retry, progress text names the task, the feature works or explains itself
with the model unavailable, and nothing destructive runs without a
confirmation.

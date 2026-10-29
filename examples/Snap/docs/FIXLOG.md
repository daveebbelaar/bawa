# Fix log

## A type named Layout breaks every page that uses it
Symptom: `CS0104 'Layout' is an ambiguous reference between 'Microsoft.UI.Xaml.Controls.Layout' and 'Snap.Core.Layout'`.
Cause: WinUI has its own `Layout` class, imported by every page.
Fix: renamed the core type to `SnapLayout`.
Rule: never name a type after a WinUI type (Layout, Window, Page, Path, Grid, Color); prefix it with the app's noun.

## await in an unsafe class
Symptom: `CS4004 Cannot await in an unsafe context` in `WindowManager`.
Cause: the whole class was `unsafe` for one function pointer.
Fix: the class is safe; only the `SetWinEventHook` line sits in an `unsafe { }` block.
Rule: keep `unsafe` to the smallest block that needs it.

## build.ps1 reported failure after a good build
Symptom: `./build.ps1` printed "installed ..." and exited 1.
Cause: robocopy returns 1 when it copied files, and that became the script's exit code.
Fix: `exit 0` at the end of the script.
Rule: end every tool script with an explicit exit code.

## Real key presses from an agent shell never reached the hotkeys
Symptom: Notepad did not move on an injected Ctrl+Alt+Left, while a posted WM_HOTKEY moved it at once.
Cause: the agent's sandboxed shell cannot inject input into the desktop or find other processes' windows. Snap was fine.
Fix: the self-test posts WM_HOTKEY to the tray window and aims it at its own test window; the physical key stays a manual check.
Rule: prove hotkeys with a posted WM_HOTKEY in the self-test, and leave the physical key press to a person.

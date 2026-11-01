# Stories

## M1 Core loop

### S-001 Snap the front window to the left half
As the user, I press Ctrl+Alt+Left and the front window fills the left half of its screen.
Acceptance:
- [unit] `Layout` returns x = area.X, width = area.Width / 2, full height for a 1920x1032 area.
- [unit] Converting a visible frame to the outer rect and back gives the original.
- [self] A test window snapped left has its visible frame within 2 px of the target.
- [manual] In Edge, Ctrl+Alt+Left fills the left half above the taskbar with no gap from the invisible border.
Status: done

### S-002 Snap the front window to the right half
As the user, I press Ctrl+Alt+Right and the front window fills the right half.
Depends: S-001
Acceptance:
- [unit] `Layout` returns x = area.X + area.Width / 2, and the two halves cover the area exactly on an odd width.
- [self] A test window snapped right lands on the target.
Status: done

### S-003 Maximize the front window
As the user, I press Ctrl+Alt+Enter and the front window fills the work area without entering the maximized state.
Acceptance:
- [unit] `Layout` returns the whole work area.
- [self] A maximized test window is restored first, then fills the area, and `IsZoomed` is false.
Status: done

### S-004 Snap to a wide side
As the user, I press Ctrl+Alt+Down or Ctrl+Alt+Up and the window takes two thirds of the screen on the left or right.
Depends: S-001
Acceptance:
- [unit] Right wide at 2/3 ends exactly at area.Right.
- [unit] A wide fraction of 0.95 is clamped to 0.9.
Status: done

### S-005 See which shortcuts work
As the user, I open Snap for the first time and Settings shows every shortcut, and warns about any that another app owns.
Screens: shortcuts-light.png
Acceptance:
- [snap] Each command is a card with its layout glyph, its name, and its shortcut.
- [snap] A shortcut that failed to register shows a warning glyph and "Another app uses this shortcut."
- [self] Every default shortcut is registered or reported as failed; none is silently missing.
Status: done

### S-006 Run in the tray
As the user, I close Settings and Snap keeps running in the tray, with no taskbar button.
Screens: tray menu (manual)
Acceptance:
- [self] Closing the Settings window hides it; the process keeps running.
- [manual] The tray menu lists every command with its shortcut, then Settings and Exit.
- [manual] Clicking Left half in the tray menu snaps the window I used before, not Snap's.
- [manual] Starting Snap again while it runs opens Settings instead of a second copy.
Status: done (manual lines need a person: the tray menu and a real key press)

### S-007 Press a shortcut on a window Snap cannot move
As the user, I press a shortcut on an app running as administrator and Snap tells me why nothing moved.
Acceptance:
- [manual] The PC beeps and the window does not move.
- [manual] The tray tooltip says "Snap can't move windows of apps running as administrator."
Status: done (built; needs a person with an elevated app to check)

## M2 Settings and displays

### S-008 Record a new shortcut
As the user, I select a shortcut in Settings and press a new combo.
Screens: shortcuts-light.png
Acceptance:
- [snap] While recording, the button says "Press a shortcut" in the accent style.
- [self] While recording, all global shortcuts are unregistered and come back after.
- [unit] Assigning a combo used by another command removes it from that command.
- [unit] A combo with only Shift, or no modifier, is refused unless the key is F1 to F12.
- [manual] Esc cancels, Backspace clears, a key without a modifier beeps.
Status: done

### S-009 Pick the wide width and the gap
As the user, I choose Two thirds, Three quarters, or Five sixths, and a gap from 0 to 40 epx.
Screens: layout-light.png
Acceptance:
- [unit] With a 10 px gap, two halves leave 10 px at each screen edge and 10 px between them.
- [snap] The Left wide card title changes to "Left wide (3/4)".
Status: done

### S-010 Move the window to the next display
As the user, I press Ctrl+Alt+Shift+Right and the window moves to the next display at the same relative spot.
Acceptance:
- [unit] A left-half window on a 1920 wide display becomes a left-half window on a 2560 wide display.
- [unit] With one display, the command returns no frame.
- [manual] Moving from a 150% display to a 100% display ends at the right size.
Status: done (unit tested; this PC has one display)

### S-011 Restore a snapped window
As the user, I press Ctrl+Alt+Backspace and the window goes back to where it was before I first snapped it.
Acceptance:
- [unit] Two snaps in a row keep the frame from before the first snap.
- [self] Restore puts a snapped test window back within 2 px.
- [manual] Restore with nothing stored beeps.
Status: done

### S-012 Launch at login
As the user, I turn on "Open at sign-in" in Settings.
Acceptance:
- [self] The switch writes and removes the Run key value, and reads it back.
Status: done

## Dropped
- Layout glyphs in the tray menu (a Win32 menu has no glyph font; the Settings cards show them)
- Center, thirds, quarters
- URL protocol and command line actions

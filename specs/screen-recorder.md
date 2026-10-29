# Screen recorder

Record the full screen, an area, or one window to MP4, with microphone, system audio, and a
webcam bubble. Replaces: Snipping Tool (video), Xbox Game Bar recording, OBS for quick clips,
Loom. Ported from BAMA's screen recorder spec.

## Brief

```markdown
# Rec

A tray screen recorder for quick demos and bug reports, saved as MP4 on this PC.

## Replaces
Snipping Tool screen recording, Xbox Game Bar capture, OBS for quick clips, Loom

## Main jobs
1. Record the screen, an area, or a window to MP4 with a shortcut.
2. Pause, resume, and stop from a small control at the top of the screen.
3. Add microphone, system audio, and a webcam bubble to a recording.

## Data
- Recording: file path, duration, poster frame
- Settings in settings.json under %LOCALAPPDATA%\Rec\: shortcuts, fps, codec, pointer, countdown, system audio, mic and mic device, camera, bubble frame, folder

## Shape
Tray icon plus overlays (picker, countdown, control, area outline, corner card, camera bubble).
Windows 11 first, Windows 10 19041 or later runs. Unpackaged .exe. Local data. Works offline. No accounts.

## System hooks
Global shortcuts, tray icon, single instance. Capture border consent, microphone and camera privacy settings.

## Reference
Snipping Tool screen recording

## Look
Native WinUI, system accent color. Acrylic capsule control with a red record dot.

## Not in version one
- Trimming, uploads, and share links
- GIF export
```

## How it works

| Part | API | Permission |
| --- | --- | --- |
| Shortcuts | `RegisterHotKey`, `WM_HOTKEY` ([recipe](../skills/code/os-integrations/recipes.md#global-hotkey-registerhotkey)) | None |
| Tray icon, no taskbar button | `Shell_NotifyIcon`, `NOTIFYICON_VERSION_4` ([recipe](../skills/code/os-integrations/recipes.md#tray-icon)) | None |
| Video frames | `GraphicsCaptureItem.TryCreateFromDisplayId` or `TryCreateFromWindowId` (`IGraphicsCaptureItemInterop.CreateForMonitor` or `CreateForWindow` on Windows 10), `Direct3D11CaptureFramePool.CreateFreeThreaded`, `GraphicsCaptureSession` ([recipe](../skills/code/os-integrations/recipes.md#screen-capture)) | Capture border unless the user allows borderless |
| Hide the border | `GraphicsCaptureAccess.RequestAccessAsync(GraphicsCaptureAccessKind.Borderless)`, `IsBorderRequired = false` (Windows 11) | One prompt |
| GPU device, crop | `D3D11CreateDevice` with `D3D11_CREATE_DEVICE_BGRA_SUPPORT`, `CreateDirect3D11DeviceFromDXGIDevice`, `ID3D11DeviceContext.CopySubresourceRegion` (Vortice.Direct3D11) | None |
| Write the MP4 | `MediaStreamSource` fed with `MediaStreamSample.CreateFromDirect3D11Surface`, `MediaTranscoder.PrepareMediaStreamSourceTranscodeAsync`, `MediaEncodingProfile.CreateMp4` or `CreateHevc` | None |
| HEVC available? | `CodecQuery.FindAllAsync(CodecKind.Video, CodecCategory.Encoder, CodecSubtypes.VideoFormatHevc)` | None |
| System audio | WASAPI loopback (`AUDCLNT_STREAMFLAGS_LOOPBACK`), through NAudio `WasapiLoopbackCapture` | None |
| Microphone | WASAPI capture, through NAudio `WasapiCapture` | Microphone privacy setting |
| Webcam bubble | `MediaCapture`, `MediaSource.CreateFromMediaFrameSource`, `MediaPlayerElement` in a round topmost window | Camera privacy setting |
| Keep own windows out | `SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE)` | None |
| Poster frame | `MediaComposition.GetThumbnailAsync` | None |
| One copy running | `AppInstance.FindOrRegisterForKey` ([recipe](../skills/code/os-integrations/recipes.md#single-instance)) | None |

Windows has no Screen Recording permission. Windows.Graphics.Capture draws a yellow border
around the recorded display or window. The border is not in the video. The user can allow
borderless capture once on Windows 11. The microphone and camera follow Settings > Privacy &
security, where "Let desktop apps access your microphone" (and camera) applies to Rec. The
recorder must never stutter or block the UI: read the `performance` skill before the encoder.
The app is PerMonitorV2 DPI aware, so every rect below is in physical pixels.

Types: `ScreenRecorder` (`ObservableObject`, phases `Idle`, `Selecting`, `Countdown`,
`Recording`, `Paused`, `Finishing`), `SelectionOverlay` (one window per display, returns a rect
or an HWND), `RecordingOptions` (`Rec.Core`), `OutputSize` (`Rec.Core`, codec limits),
`FrameEncoder` (GPU copy and crop, `MediaStreamSource`, `MediaTranscoder`), `AudioMixer`
(loopback plus mic to one 48 kHz stereo track), `RecordingControl`, `RecordingCountdown`,
`AreaOutline`, `CameraBubble`, `RecordingFiles` (`Rec.Core`: names, move, poster; off the UI
thread), `RecordingCard`, `CaptureAccess`.

Behavior that matters:

- No default shortcuts. The user sets Record screen, Record area, Record window, and Stop in
  Settings. A record shortcut pressed while recording stops it. Leave Win+Alt+R to Game Bar.
- Screen takes the display under the pointer. Area and window open the picker on every display.
- Start order: check mic and camera access if on, bubble, area outline, countdown, capture
  session, control.
- Defaults: 30 fps, H.264, pointer on, countdown on, system audio off, mic off, camera off.
- Session: `IsCursorCaptureEnabled` from the pointer option. Frame rate: `MinUpdateInterval =
  1 / fps` where it exists (Windows 11 24H2), and on older builds drop any frame that arrives
  sooner than 1 / fps after the last kept one.
- Output size: one scale factor so the long side fits 4096 (H.264) or 8192 (HEVC) and the area
  fits 4096 x 2304 or 8192 x 4320 pixels, never above 1, then each side rounded down to even.
  The encoding profile gets that size; the transcoder scales.
- Area: crop each display frame on the GPU with `CopySubresourceRegion`. The source box is
  relative to the display's top-left: `x = rect.Left - monitor.Left, y = rect.Top - monitor.Top`.
  Displays left of or above the main one have negative `monitor.Left` or `monitor.Top`.
- Window: the size is fixed at the start. When the window grows or shrinks, call
  `framePool.Recreate` with the new size and copy the overlap into the fixed frame, black around it.
- Pause stops feeding frames and audio and adds the paused time to an offset. Every timestamp
  is `frame.SystemRelativeTime - start - pausedTotal`. One file, no joining.
- The MP4 is written to `%TEMP%\Rec\`. On stop it becomes `Rec Recording yyyy-MM-dd 'at' HH.mm.ss.mp4`
  in the save folder (default: the Videos known folder, `FOLDERID_Videos`), with ` 2`, ` 3` added
  if the name exists.
- Control: top center of the recorded display, topmost, `WS_EX_TOOLWINDOW`, excluded from
  capture, time every 0.5 s as `00:12` or `1:02:03`, dragged by its time.
- Bubble: Small 140, Medium 200 (default), Large 300, range 96 to 480 epx, mirrored. Starts 32 epx
  in from the bottom-left of the work area, at most 40% of the short side; reuses its last frame.
  It is a real window, so it is in screen and area videos. None for window recordings.
- Card: poster frame at `min(0.5 s, duration / 2)`, max 640 pixels. Copy puts the file on the
  clipboard (`DataPackage.SetStorageItems`), Show in folder runs `explorer.exe /select,"<path>"`,
  drag the movie out. Hides after 8 s unless hovered.
- The done sound plays after the file is closed, so Rec's own sound is never in a recording.

The frame loop, with the pause offset and the one call that keeps the pool alive:

```csharp
var item = GraphicsCaptureItem.TryCreateFromDisplayId(new DisplayId { Value = area.DisplayId.Value });
var pool = Direct3D11CaptureFramePool.CreateFreeThreaded(device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, item.Size);
var session = pool.CreateCaptureSession(item);
session.IsCursorCaptureEnabled = options.ShowPointer;
if (ApiInformation.IsPropertyPresent(typeof(GraphicsCaptureSession).FullName, nameof(GraphicsCaptureSession.IsBorderRequired)))
    session.IsBorderRequired = false;                 // ignored unless borderless access was allowed

pool.FrameArrived += (p, _) =>
{
    using var frame = p.TryGetNextFrame();            // dispose fast, or the pool runs out of buffers
    if (recorder.IsPaused || frame is null) return;
    var time = frame.SystemRelativeTime - start - recorder.PausedTotal;
    encoder.Push(frame.Surface, time);                // copies (and crops) into its own texture first
};
session.StartCapture();
```

## Stories

```markdown
# Stories

## M1 Core loop

### S-001 Hide the capture border
As the user, I start my first recording and am asked once to allow capture without a border.
Screens: settings-permissions
Acceptance:
- [manual] On Windows 11 the first recording calls `RequestAccessAsync(Borderless)`; after Allow, no yellow border shows while recording.
- [snap] Settings shows Capture border, Microphone, and Camera as Allowed or Off, each with a button that opens the matching `ms-settings:` page.
- [manual] On Windows 10, or after Deny, the yellow border shows on screen and is not in the MP4.
Status: todo

### S-002 Record the full screen
As the user, I press Record screen, see 3, 2, 1, and recording starts.
Screens: countdown
Acceptance:
- [self] The MP4 has the display's pixel size, even, scaled down only past the codec limit, on a 100% and a 150% display.
- [manual] Escape cancels the countdown, and the app being recorded never gets the Escape.
Status: todo

### S-003 Stop and save
As the user, I click Stop on the capsule or press the shortcut again, and the movie is saved.
Screens: control
Depends: S-002
Acceptance:
- [snap] The capsule shows a pulsing red dot, the time, Pause, and a red Stop button.
- [self] The file plays in Media Player and has no capsule, countdown, outline, or card in it.
- [unit] A second file with the same name gets ` 2` added.
Status: todo

### S-004 Record an area
As the user, I press Record area and drag a rectangle.
Screens: picker-area, area-outline
Acceptance:
- [unit] The source box for an area on a display at negative coordinates is relative to that display's top-left.
- [snap] A dashed white line sits just outside the area while recording.
Status: todo

### S-005 Record a window
As the user, I press Record window and click a window.
Screens: picker-window
Acceptance:
- [self] The video is the window's visible size in pixels, with no shadow and no invisible border.
- [manual] Covering the window with another one does not cover it in the video.
Status: todo

### S-006 Use the corner card
As the user, I copy the file, show it in its folder, or drag it out from the card.
Screens: card
Acceptance:
- [snap] The card shows the poster frame and the duration.
- [manual] Copy, then Paste in File Explorer, gives the MP4; dragging into Outlook attaches it.
Status: todo

### S-007 Set record shortcuts
As the user, I set shortcuts for Record screen, Record area, Record window, and Stop in Settings.
Screens: settings-shortcuts
Acceptance:
- [manual] Escape cancels, Delete clears; all shortcuts pause while a box records.
- [snap] A combo another app owns shows "In use by another app" under the box.
Status: todo

### S-008 Record from the tray
As the user, I start a recording from the tray menu and stop it there.
Screens: tray-menu, tray-menu-recording
Acceptance:
- [snap] Idle: Record screen, Record area, Record window, Settings, Quit.
- [snap] Recording: a red "Recording 00:12" row with Stop at the top; the tray icon turns red.
Status: todo

## M2 Audio, camera, pause

### S-009 Pause and resume
As the user, I click Pause, then Resume, and get one movie without the paused part.
Acceptance:
- [self] Recording 3 s, pausing 2 s, and recording 3 s gives one MP4 of about 6 s, audio in sync.
- [manual] The time stops and the dot turns gray while paused.
Status: todo

### S-010 Record audio
As the user, I turn on the mic (and pick a device) or system audio, and the sound is in the video.
Screens: settings-audio
Acceptance:
- [manual] With "Let desktop apps access your microphone" off, the recording runs without the mic and the card says "Recorded without microphone".
- [manual] Music from another app is audible with system audio on, and stays in sync through a silent stretch.
Status: todo

### S-011 Show the webcam bubble
As the user, I turn on the camera and a round, mirrored bubble shows in my recording.
Screens: bubble
Acceptance:
- [manual] Drag moves it; the rim, Ctrl+scroll, or a pinch resizes it; right-click offers Small, Medium, Large, Hide camera.
- [self] The bubble is in screen and area videos; with camera access off it records without it and says so.
Status: todo

### S-012 Choose video settings
As the user, I pick frame rate, format, pointer, and countdown in Settings.
Screens: settings-video
Acceptance:
- [unit] Defaults: 30 fps, H.264, pointer on, countdown on.
- [self] HEVC is offered only if `CodecQuery` finds an HEVC encoder; otherwise the option is off with "Needs HEVC Video Extensions".
Status: todo

## Dropped
- Click rings (needs a low-level mouse hook and its own overlay; later)
- GIF export from the card: 10 fps, 640 pixels, first 30 seconds (later)
- Recording mode, keystroke overlay, cursor highlight, chapter markers
- Usage stats, activity log, debug bridge record commands ([recipe](../skills/code/os-integrations/recipes.md#debug-bridge), later)
```

## Gotchas

- Windows.Graphics.Capture sends a frame only when the picture changes. A still screen gives no
  frames for seconds. Use `SystemRelativeTime` for every timestamp instead of counting frames,
  or a still stretch plays back too short and the audio drifts.
- Dispose each `Direct3D11CaptureFrame` as soon as you have copied it. The pool has only a few
  buffers; holding one stalls the capture with no error.
- Copy the frame into your own texture before handing it to the encoder. The pool reuses the
  surface, and the encoder reads it later, so a passed-through surface shows a newer frame or garbage.
- Keep Rec's own windows out with `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` on the
  capsule, countdown, area outline, picker, and card. Leave the bubble without it, so it is
  recorded. Set it before the window is first shown.
- Guard Windows 11 properties. `IsBorderRequired` is missing on Windows 10 19041 and
  `MinUpdateInterval` before Windows 11 24H2. Check `ApiInformation.IsPropertyPresent` first.
- The docs tie `GraphicsCaptureAccessKind.Borderless` to a package capability, and Rec is
  unpackaged. Check the returned status. When it is not `Allowed`, record with the border and say so.
- H.264 encoders top out near 4096 x 2304, so a 5K display at full size fails to start. HEVC
  keeps 5K and 6K, but on many PCs it needs the HEVC Video Extensions from the Store. Check with
  `CodecQuery` before offering it, and fall back to H.264 with a scaled size.
- WASAPI loopback delivers no packets while nothing plays. Fill the gap with silence based on
  the clock, or the audio track is shorter than the video and drifts out of sync.
- Mix the system audio and the mic into one 48 kHz stereo track before the encoder. One audio
  stream keeps the `MediaStreamSource` simple and plays everywhere. Resample both first; a
  44.1 kHz mic added to 48 kHz samples as is plays too fast and runs ahead of the video.
- With the microphone privacy setting off, `IAudioClient.Initialize` fails with `E_ACCESSDENIED`
  (0x80070005). Catch it, record without the mic, and open `ms-settings:privacy-microphone` from
  Settings. With the camera setting off, `MediaCapture.InitializeAsync` throws
  `UnauthorizedAccessException`; open `ms-settings:privacy-webcam`.
- During the countdown, register Escape as a temporary `RegisterHotKey` with no modifiers, so the
  app being recorded never sees it. Unregister it when the countdown ends.
- Bump a `session` counter on every start and stop; late callbacks from an older recording check
  it and do nothing. Stop during startup closes that session and deletes its file.
- Wait for the transcode task to finish before moving the file, or the MP4 has no index and will
  not play. Give up after 5 seconds and keep the temp file, so stop never hangs.
- Pause by shifting timestamps, not by cutting files. Holding back samples and subtracting the
  paused time gives one file and no join step. Joining clips afterward means a second pass over
  the whole movie while the user waits.
- Round the bubble with `SetWindowRgn(CreateEllipticRgn(...))` and redo it when its size or DPI
  changes. Start the camera off the UI thread: `MediaCapture.InitializeAsync` can take a second.
- Windows on another display scale get the wrong size if you place them in epx from one display.
  Place the capsule, outline, and bubble with physical pixels from the target display's
  `DisplayArea`. See [move a window](../skills/code/os-integrations/recipes.md#move-a-window).

Done when screen, area, and window recordings each save as a playable MP4 at full pixel size,
with mic, system audio, and bubble when turned on, none of the app's own overlays in the video,
and audio in sync after a pause and a silent stretch.

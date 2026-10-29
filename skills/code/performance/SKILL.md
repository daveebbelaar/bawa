---
name: performance
description: Make a Windows app fast by measuring hangs, stutter, CPU, memory, and startup, then moving work off the UI thread and onto the GPU. Use when the app freezes or shows "Not responding", a shortcut feels slow, scrolling or dragging stutters, the fan spins, memory grows, or the user asks to optimize or profile.
---

# Performance

Fast means two numbers: the first frame after a key press shows in under
100 ms, and nothing on the UI thread blocks for more than 50 ms. Every
change in this skill starts and ends with a **measurement**, so you know it
helped. Symptom-by-symptom fixes are in [recipes.md](recipes.md). For
video, audio, camera, Direct3D, or large image pipelines, also read
[gpu-media.md](gpu-media.md).

## The loop

```text
reproduce -> measure (Release build) -> find the line -> fix -> measure again -> commit with the numbers
```

1. **Reproduce.** Write the exact steps, or add a probe command to the
   agent CLI or the debug bridge (`test-windows-app` skill) that runs the
   slow action and logs the time of each step.
2. **Measure the Release build.** `./build.ps1` builds Release by default;
   `-Dev` builds Debug, which turns off JIT optimizations and points at
   the wrong code.
3. **Find the line.** Pick the tool for the symptom:

   | Symptom | Tool |
   | --- | --- |
   | App freezes, "Not responding" | `dotnet-stack report -p <pid>`, then read the UI thread's stack (the one in `Microsoft.UI.Xaml`) |
   | Slow action | `dotnet-trace collect -p <pid> --duration 00:00:00:10`, then `dotnet-trace report <file> topN -n 30` |
   | Short freezes while typing or dragging | A UI-thread watchdog in the app (recipes.md, "Measuring"), or WPR with the `XAMLAppResponsiveness` profile |
   | Stutter while scrolling or animating | WPR `-start GPU -start DesktopComposition`, opened in WPA |
   | Memory grows | `dotnet-counters monitor -p <pid> --counters System.Runtime`, then `dotnet-gcdump collect -p <pid>` |
   | Fan, battery | `dotnet-counters` CPU usage while idle; look for timers and polling |
   | Slow launch | Time from `Process.Start` to the first answer (recipes.md, "Startup") |

   The .NET tools install once: `dotnet tool install --global dotnet-trace`
   (and `dotnet-counters`, `dotnet-stack`, `dotnet-gcdump`). Ask before
   installing. WPR ships with Windows; WPA comes with the Windows
   Performance Toolkit. WPR needs an elevated terminal.

   Read traces as text where you can: see "Measuring" in
   [recipes.md](recipes.md). Delete every `.nettrace`, `.gcdump`, and
   `.etl` after reading. They hold process command lines, file paths,
   and memory contents. Never commit or share one.

4. **Fix** with the matching recipe.
5. **Measure again** with the same steps. Put before and after in the
   commit message: "Picker opens in 40 ms, was 600 ms".

## The rules that fix most problems

- **The UI thread does UI only.** File, network, database, image
  decoding, cross-process Win32 calls, and audio go elsewhere. A UI-thread
  call that waits on another process (`SendMessage` to a hung window, a
  COM call into another app) freezes the app for as long as that process
  hangs.
- **Show first, work after.** Put the window or panel on screen in the same
  frame as the key press, then load. Never make the UI wait for the work.
- **Let the GPU composite.** XAML renders through the compositor. Large
  images go in an `Image` once; pointer movement changes only a small
  element's transform. Never redraw a full-screen bitmap per pointer
  event.
- **Decode at display size.** `BitmapImage.DecodePixelWidth`, or
  `BitmapDecoder` with a `BitmapTransform`, cached by path and size.
- **Do not animate what must be fast.** Search results and switchers
  update in place.
- **Virtualize long lists, defer hidden UI.** `ListView` or
  `ItemsRepeater` for lists, `x:Load="False"` for panels most people never
  open.
- **No polling without a reason.** Timers stop when nothing on screen
  needs them.

## Windows and .NET notes

- XAML layout, the compositor, Direct2D (Win2D), Direct3D, Media
  Foundation, and `Windows.Media.Ocr` already use the GPU or hardware
  blocks. Hand them the work instead of looping over pixels in C#.
- Write Direct3D or compute shaders only when a measurement shows XAML,
  composition, or Win2D is too slow. Most apps never need it.
- Keep frames as `IDirect3DSurface` or a D3D11 texture between capture,
  processing, and encoding. Converting to a `SoftwareBitmap` and back costs
  copies through CPU memory.
- For start time, NativeAOT beats everything else: `tools/publish.ps1 -Aot`.
  Measured on the template, cold start to the main window: about 420 ms
  self-contained JIT, 170 ms NativeAOT. ReadyToRun gained only about 10%.
  Use NativeAOT for tray utilities, launchers, and panels; the `distribute`
  skill has the setup (the MSVC linker) and the trim-warning fixes.

Done when the slow action has a measured before and after, the after meets
the two numbers at the top, and the commit message names both.

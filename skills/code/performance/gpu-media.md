# GPU and media recipes

For apps that capture, decode, process, or encode video, audio, or large
images. Each recipe: symptom, cause, fix. The lessons come from BAMA's
measured media apps on the Mac; the APIs here are their Windows
counterparts. Measure your own numbers on Windows before you trust any
gain.

## Contents

- Rules
- Zero-copy GPU input
- Keep the GPU off the capture callback
- Back-pressure without drops
- Bounded caches and pools
- Threads, stacks, and the garbage collector
- Playback
- Reading and encoding
- Overlays and filters
- XAML next to a media engine
- Lifetimes
- Measuring media work

## Rules

- One stage per thread: capture, decode, process, encode. Each hands off
  to the next and never waits on it.
- Pixels stay on the GPU. A Direct3D 11 texture (`IDirect3DSurface` in
  WinRT) passes between `Windows.Graphics.Capture`, Media Foundation,
  Win2D, and Direct3D without a copy. A `SoftwareBitmap` is a copy in CPU
  memory.
- Every new fast path gets an on-off switch (an environment variable or a
  setting), so you can compare and fall back.
- Prove the output is the same: hashes, `ffmpeg -f framemd5`, or VMAF.

## Zero-copy GPU input

**Symptom:** a core or more busy while recording, with time in pixel
copies and conversions.
**Cause:** each frame copied to the CPU (`SoftwareBitmap.CreateCopyFromSurfaceAsync`)
or converted between pixel formats on the CPU.
**Fix:** keep the frame as a texture and process it on the GPU.

```csharp
using System.Runtime.InteropServices.Marshalling;
using Windows.Graphics.DirectX.Direct3D11;

[GeneratedComInterface]
[Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
internal partial interface IDirect3DDxgiInterfaceAccess
{
    nint GetInterface(in Guid iid);
}

internal static class Surfaces
{
    private static readonly Guid ID3D11Texture2D = new("6F15AAF2-D208-4E89-9AB4-489535D34F9C");

    /// The frame's ID3D11Texture2D, without a copy. Marshal.Release it when done.
    public static nint Texture(IDirect3DSurface surface)
    {
        var unknown = WinRT.MarshalInterface<IDirect3DSurface>.FromManaged(surface);
        try
        {
            var access = (IDirect3DDxgiInterfaceAccess)new StrategyBasedComWrappers().GetOrCreateObjectForComInstance(unknown, CreateObjectFlags.None);
            return access.GetInterface(ID3D11Texture2D);
        }
        finally { Marshal.Release(unknown); }
    }
}
```

- Capture in the format the next stage wants. `Windows.Graphics.Capture`
  delivers `B8G8R8A8UIntNormalized`; hardware H.264 and HEVC encoders
  take NV12. Convert once on the GPU (the Media Foundation video
  processor, or a shader), not per pixel in C#.
- With Win2D, `CanvasBitmap.CreateFromDirect3D11Surface(device, frame.Surface)`
  wraps the frame without a copy. The Win2D device and the capture device
  must be the same device.

## Keep the GPU off the capture callback

**Symptom:** the capture drops frames when the GPU is busy.
**Cause:** heavy GPU work, or a wait for it, inside `FrameArrived`. The
frame pool has a fixed number of buffers; every frame you still hold is
one the capture cannot reuse.
**Fix:** a one-slot mailbox. The callback only hands off; the work skips
a frame when the previous one is still running, and counts the skip.

```csharp
internal sealed class GpuMailbox
{
    private readonly Lock _lock = new();
    private bool _busy;
    public int Skipped { get; private set; }

    /// Called from FrameArrived on a free-threaded frame pool.
    public void OnFrame(Direct3D11CaptureFramePool pool)
    {
        using var frame = pool.TryGetNextFrame();      // disposing returns the buffer to the pool
        if (frame is null) return;
        lock (_lock)
        {
            if (_busy) { Skipped++; return; }
            _busy = true;
        }
        var texture = Surfaces.Texture(frame.Surface);
        try { Encode(texture); }                       // copy into your own texture if the work outlives the frame
        finally
        {
            Marshal.Release(texture);
            lock (_lock) _busy = false;
        }
    }

    private void Encode(nint texture) { /* GPU work */ }
}
```

Use `Direct3D11CaptureFramePool.CreateFreeThreaded`, so `FrameArrived`
runs on a worker thread and not on the UI thread.

## Back-pressure without drops

**Symptom:** frames dropped when the encoder is not ready.
**Fix:** a bounded queue per track, drained on the encoder's thread.
Copy frames into your own pool of textures before you queue them, so
the capture pool gets its buffers back at once. Cap the queue (90 frames
is about 3 seconds at 30 fps) and count when you hit the cap. Trim your
texture pool back down when the queue empties, or memory stays high
after one stall.

## Bounded caches and pools

**Symptom:** memory grows for as long as the app runs.
**Cause:** a texture or decoded image cached per item, with no limit.
**Fix:** evict textures unused for N frames, an LRU cap on images, and
release COM objects (textures, samples) as soon as a stage is done with
them. Check that rebuilt textures are pixel-identical.

## Threads, stacks, and the garbage collector

- Per-frame allocations make garbage collections, and a collection can
  pause every thread. Reuse buffers: `ArrayPool<byte>.Shared`, one
  texture pool, no `new byte[]` per frame. Arrays of 85,000 bytes or more
  go to the large object heap, which is collected rarely and expensively.
- During a recording, `GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency`
  avoids blocking full collections. Set the old mode back afterwards.
- Deep recursion on a thread-pool thread can overflow the stack. Give the
  work its own thread with a bigger stack:
  `new Thread(Work, maxStackSize: 16 << 20)`.
- Long-running media loops get their own `Thread` (`IsBackground = true`),
  not a thread-pool task that blocks a pool thread for minutes.

## Playback

**Symptom:** play starts late, and late again with more tracks.
**Fix:** for plain playback, `MediaPlayerElement`. For frame-accurate
work (an editor, several synced tracks), take frames yourself:
`MediaPlayer.IsVideoFrameServerEnabled = true`, then on
`VideoFrameAvailable` call `CopyFrameToVideoSurface` into your own
texture. The clock is a `Stopwatch` timestamp
(`frame = anchor + (now - anchorTime) * fps * rate`), audio starts at the
anchor, and presenting happens in `CompositionTarget.Rendering`, the
display-linked callback. Keep a few frames decoded ahead.

## Reading and encoding

- Media Foundation picks hardware decoders and encoders when they exist.
  `Windows.Media.Transcoding.MediaTranscoder` (with
  `HardwareAccelerationEnabled = true`) is the short path; the Source
  Reader and Sink Writer are the full control path.
- Files on a network share: reading is latency-bound. Read large byte
  ranges ahead on a background thread, then decode from memory.
- No file system calls per frame. Compute paths, existence checks, and
  plans once.
- Encoder speed presets trade quality for frames per second. Measure both
  with the same quality score before you pick one.
- More parallel encode sessions do not scale linearly, and can get
  slower. Measure before you parallelize the encoder.
- Write to a network drive through a local file: write locally, copy to
  `.partial`, check the size, rename.

## Overlays and filters

- Prepare overlays for frame n+2 on worker threads while frame n renders.
- Blend only the rectangle that has content.
- A large Gaussian blur costs far more than a small one. A radial gradient
  often gives the same look for a fraction of the cost.
- One Win2D `CanvasDevice` for the whole app. Effects (`GaussianBlurEffect`,
  `ColorMatrixEffect`) run on the GPU; parallel copies of the same effect
  chain do not help.

## XAML next to a media engine

- A per-frame value on a model that many bindings read re-evaluates all of
  them every frame. Move it to its own small object that only the meter
  reads (see the `improve-architecture` skill).
- Scopes and meters: draw them in one Win2D `CanvasControl`, or update a
  composition visual, at most 30 times a second. Keep static parts as
  plain XAML. Refresh text at 2 Hz.
- Never subscribe to a media event from a `DataTemplate` that is recreated
  per row or per frame. It subscribes again every time.

## Lifetimes

- Dispose every `Direct3D11CaptureFrame` right after use, and the frame
  pool and `GraphicsCaptureSession` when capture stops.
- `MFStartup` once at launch and `MFShutdown` once at exit when you call
  Media Foundation directly.
- Release every COM pointer you get from `GetInterface` or
  `QueryInterface` (`Marshal.Release`). A leaked texture holds GPU memory
  that Task Manager shows under "Dedicated GPU memory".
- Never block the UI thread with `.Wait()` while a media callback needs
  it. The callback waits for you, you wait for it.

## Measuring media work

- A headless `--selftest` mode that processes N frames and prints
  counters: drops, timestamp gaps, max queue depth, fallbacks, ms per
  stage.
- Read memory inside the process: `GC.GetGCMemoryInfo()` for the managed
  heap, `Process.GetCurrentProcess().PrivateMemorySize64` for everything.
  `dotnet-counters` reads the same numbers from outside.
- GPU work: Task Manager's GPU engines (3D, Video Encode, Video Decode)
  per process for a quick look, `wpr -start GPU` and WPA for frames.
- Release builds only. Debug builds turn off JIT optimizations and move
  the time to the wrong code.
- One benchmark at a time, and write down the background load. Another
  busy process causes drops that look like an app bug.

# GPU and media recipes

For apps that capture, decode, process, or encode video, audio, or large
images. Each recipe: symptom, cause, fix. The lessons come from BAMA's
measured media apps on the Mac; the APIs here are their Windows
counterparts. The zero-copy, device, and screen recording parts ran in
Onyx for Windows's recorder on Windows 11 build 26200. Measure your own
numbers on Windows before you trust any gain.

## Contents

- Rules
- Zero-copy GPU input
- Own the device
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
**Fix:** keep the frame as a texture and process it on the GPU. This is
the shape Onyx's recorder ran, with a vtable call instead of a COM
interface declaration:

```csharp
using Windows.Graphics.DirectX.Direct3D11;

internal static unsafe class Surfaces
{
    private static readonly Guid IID_ID3D11Texture2D = new("6F15AAF2-D208-4E89-9AB4-489535D34F9C");
    private static readonly Guid IID_IDirect3DDxgiInterfaceAccess = new("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1");

    /// The frame's ID3D11Texture2D, without a copy. Marshal.Release it when done.
    public static nint Texture(IDirect3DSurface surface)
    {
        var abi = WinRT.MarshalInterface<IDirect3DSurface>.FromManaged(surface);
        try
        {
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(abi, in IID_IDirect3DDxgiInterfaceAccess, out var access));
            try
            {
                var iid = IID_ID3D11Texture2D;
                nint texture;
                // IDirect3DDxgiInterfaceAccess::GetInterface is slot 3 (IUnknown's three come first).
                Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)(*(void***)access)[3])(access, &iid, &texture));
                return texture;
            }
            finally { Marshal.Release(access); }
        }
        finally { Marshal.Release(abi); }
    }
}
```

- Capture in the format the next stage wants. `Windows.Graphics.Capture`
  delivers `B8G8R8A8UIntNormalized`. `MediaTranscoder` took the BGRA
  surfaces from a `MediaStreamSource` as they were and converted them;
  with the Sink Writer, hardware H.264 and HEVC encoders take NV12.
  Convert once on the GPU (the Media Foundation video processor, or a
  shader), not per pixel in C#.
- With Win2D, `CanvasBitmap.CreateFromDirect3D11Surface(device, frame.Surface)`
  wraps the frame without a copy. The Win2D device and the capture device
  must be the same device.

## Own the device

**Symptom:** a recorder that copies frames into its own textures crashes
or corrupts frames once Media Foundation starts reading them.
**Cause:** the screenshot recipe's device releases the `ID3D11Device` and
its immediate context, and nothing protects the context from Media
Foundation's threads.
**Fix:** one device for the app's lifetime that keeps both pointers, with
`ID3D10Multithread.SetMultithreadProtected(TRUE)` (slot 5) right after
`D3D11CreateDevice`, and a `Lock` around every use of the immediate
context. The vtable slots Onyx used, no package needed:
`ID3D11Device` 5 `CreateTexture2D`, 9 `CreateRenderTargetView`;
`ID3D11DeviceContext` 46 `CopySubresourceRegion`, 47 `CopyResource`, 50
`ClearRenderTargetView`. Wrap your own texture for WinRT through
`IDXGISurface` and `CreateDirect3D11SurfaceFromDXGISurface`. The whole
`GpuDevice` is in the os-integrations skill's
[capture.md](../os-integrations/capture.md), "Screen recording to MP4".

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

A plain GPU copy into a texture of your own is cheap enough to do in the
callback. Onyx's recorder copies each frame there (two pool buffers, the
frame disposed at once) and recorded a window that changed on every
refresh at 30 fps with none dropped.

## Back-pressure without drops

**Symptom:** frames dropped when the encoder is not ready.
**Fix:** a bounded queue per track, drained on the encoder's thread.
Copy frames into your own pool of textures before you queue them, so
the capture pool gets its buffers back at once. Cap the queue (90 frames
is about 3 seconds at 30 fps) and count when you hit the cap. Trim your
texture pool back down when the queue empties, or memory stays high
after one stall.

With `MediaStreamSource`, a `BlockingCollection` is the queue:
`SampleRequested` blocks its Media Foundation thread on `TryTake` until a
frame comes, frees each texture in `sample.Processed`, and returns a null
sample after `CompleteAdding`, which ends the transcode.

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

Screen recording with `MediaStreamSource` and `MediaTranscoder` (the
code is in the os-integrations skill's
[capture.md](../os-integrations/capture.md)):

- Source: `VideoEncodingProperties.CreateUncompressed(Bgra8, w, h)`,
  `BufferTime = TimeSpan.Zero`, and `Starting` calls
  `SetActualStartPosition(TimeSpan.Zero)`. Samples:
  `MediaStreamSample.CreateFromDirect3D11Surface(texture.Surface, time)`.
- Profile: `MediaEncodingProfile.CreateMp4(HD1080p)`, then set the
  video width, height, bitrate, and frame rate, and `profile.Audio = null`
  when the source has no audio stream.
- Output: `FileRandomAccessStream.OpenAsync(path, ReadWrite, None,
  CreateAlways)`. No `StorageFile`, works unpackaged.
- Frame rate: `GraphicsCaptureSession.MinUpdateInterval` (Windows 11
  24H2 and later) at 90 percent of the frame time. At exactly 1/fps on a
  60 Hz display, Onyx measured 26 fps for a 30 fps setting; at 90
  percent, 30.1. Older builds send frames at the display rate; drop the
  extra ones by timestamp.
- Time: `Direct3D11CaptureFrame.SystemRelativeTime` is QPC. Movie time is
  that minus the first frame's. Anything you add at "now" (a repeated
  last frame at stop) uses
  `Stopwatch.GetTimestamp() * TimeSpan.TicksPerSecond / Stopwatch.Frequency`.
- A still picture sends no frames. Repeat the last frame at the stop
  time, or the movie is short: a 2 s still recording had 1 frame.
- Prepare takes about 0.3 s after `StartCapture`, and the movie starts at
  the first frame.
- Stopping waits for the MP4 index (Onyx allowed 5 s). A synchronous quit
  loses a running recording.

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
  pool and `GraphicsCaptureSession` when capture stops. With a two-buffer
  pool, one held frame stalls the capture with no error.
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
- Measure the recorder, not the test source. A test window that changes
  on a 33 ms `DispatcherTimer` ticks about 20 times a second (the 15.6 ms
  system timer). Change it from `CompositionTarget.Rendering` instead.
- Read an MP4 back in a self-test with no ffprobe:
  `MediaClip.CreateFromFileAsync` gives `OriginalDuration` and
  `GetVideoEncodingProperties()` (subtype, width, height), and
  `MediaComposition.GetThumbnailAsync` gives frames. Works unpackaged.
- Print measured numbers (fps, drops, duration) on pass too, not only on
  failure, so they can be compared run to run.

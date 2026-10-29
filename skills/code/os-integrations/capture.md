# Capture recipes

Screenshots, pickers, recordings, and live previews. The order that
worked in a tray suite built with BAWA (Windows 11 build 26200, one 1920 x 1200
display at 125 percent): freeze the screen with GDI first, show the
picker over the frozen image, and use `Windows.Graphics.Capture` only for
one window or a recording. The window grab and the capture border state
are in "Screen capture" in [recipes.md](recipes.md).

## Contents

- Screen freeze
- Picker over the frozen image
- Screen recording to MP4
- Live window thumbnails
- Test captures safely

## Screen freeze

GDI reads the composed desktop with no prompt and no capture border. One
`BitBlt` of the virtual screen reads every display at once, at every
scale, in physical pixels. A 600 x 400 area took single-digit
milliseconds.

```csharp
internal static class ScreenFreeze
{
    /// Copies part of the screen (screen coordinates, physical pixels) as top-down BGRA.
    public static byte[] Capture(int x, int y, int width, int height)
    {
        var header = new Win32.BITMAPINFOHEADER
        {
            biSize = (uint)Marshal.SizeOf<Win32.BITMAPINFOHEADER>(),
            biWidth = width,
            biHeight = -height,   // negative: top-down rows; positive reads every row mirrored
            biPlanes = 1,
            biBitCount = 32,
        };
        var screen = Win32.GetDC(0);
        var memory = Win32.CreateCompatibleDC(screen);
        var bitmap = Win32.CreateDIBSection(screen, ref header, 0, out var bits, 0, 0);
        var old = Win32.SelectObject(memory, bitmap);
        try
        {
            // CAPTUREBLT keeps menus and tooltips (layered windows).
            if (bitmap == 0 || !BitBlt(memory, 0, 0, width, height, screen, x, y, 0x00CC0020 /* SRCCOPY */ | 0x40000000 /* CAPTUREBLT */))
                throw new InvalidOperationException("BitBlt failed");
            var pixels = new byte[width * height * 4];
            Marshal.Copy(bits, pixels, 0, pixels.Length);
            for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 255;   // GDI leaves alpha at 0
            return pixels;
        }
        finally
        {
            Win32.SelectObject(memory, old);
            if (bitmap != 0) Win32.DeleteObject(bitmap);
            Win32.DeleteDC(memory);
            Win32.ReleaseDC(0, screen);
        }
    }

    /// Every display together. Its origin is negative when a display sits left of or above the main one.
    public static (int X, int Y, int Width, int Height) VirtualScreen() =>
        (GetSystemMetrics(76), GetSystemMetrics(77), GetSystemMetrics(78), GetSystemMetrics(79));   // SM_X/Y/CX/CYVIRTUALSCREEN

    /// Hide the app's own flyout first. A window that just hid can still be on screen while its
    /// hide animation plays: wait that out, then for the next composed frame.
    public static async Task HideOwnPanelsAsync(OverlayWindow? flyout)
    {
        if (flyout is { IsOpen: true })
        {
            flyout.Hide();
            await Task.Delay(250);
        }
        DwmFlush();
    }

    [LibraryImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool BitBlt(nint dest, int x, int y, int w, int h, nint source, int sx, int sy, uint rop);
    [LibraryImport("user32.dll")] private static partial int GetSystemMetrics(int index);
    [LibraryImport("dwmapi.dll")] private static partial int DwmFlush();
}

// A shortcut press:
await ScreenFreeze.HideOwnPanelsAsync(App.Flyout);
var (x, y, w, h) = ScreenFreeze.VirtualScreen();
var frozen = ScreenFreeze.Capture(x, y, w, h);
```

Gotchas:

- GDI reads whatever is on top, unlike a window capture. That is what a
  screenshot of a display or an area wants.
- Alpha is 0 in every pixel. A `WriteableBitmap` or PNG treats that as
  transparent; set it to 255 after the copy.
- One `DwmFlush` is not enough to get a just-hidden flyout out of the
  picture: wait 250 ms for its hide animation when it was open.
- Encode to PNG with `BitmapEncoder.SetPixelData(Bgra8, Ignore, ...)`
  off the UI thread. For the clipboard, put a bitmap and a `PNG` format
  ("Clipboard listener" in recipes.md).
- Windows has no shutter sound. Build a short WAV in memory, keep the
  buffer for the app's lifetime, and play it with
  `PlaySound(SND_MEMORY | SND_ASYNC | SND_NODEFAULT)`. Test runs stay
  quiet.

## Picker over the frozen image

An area, window, or color picker shows the frozen image full screen and
draws on top of it. The picker can never be in its own picture, and the
screen holds still while the person aims.

- One borderless window per display ("Borderless overlay window" in
  [overlays.md](overlays.md)), made once and kept hidden between picks,
  so the shortcut opens at once. Measured: 4 to 9 ms to show a 600 x 400
  picker, 23 ms with window creation.
- Each window shows its display's part of the frozen image in an `Image`
  with `Stretch.Fill`, sized in epx from the window's own DPI. Drop the
  bitmap on hide: a 4K frame is 33 MB.
- The content is a focusable control, or keys never arrive: a
  `UserControl` with `IsTabStop = true` and
  `UseSystemFocusVisuals = false`, focused after `Activate`. A `Grid`
  cannot take focus. `ProtectedCursor` is protected, so the crosshair
  needs that subclass:
  `ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Cross)`.
- Read the pointer with `GetCursorPos` in every pointer event. That is
  screen pixels, the same unit as the frozen image, whichever display's
  window raised the event.
- The window under the pointer activates; the others show with
  `Show(activate: false)`.
- Pick on button release, not press: a pick on press hides the overlay,
  and the release lands on the app below. Escape and right-click cancel.
  Give the foreground back to the window the person was in.
- A window picker offers the windows from `EnumWindows`, front to back:
  visible, not minimized, not cloaked, not your own or the shell's, at
  least 8 x 8 px, not click-through (`WS_EX_TRANSPARENT`), and not an
  untitled tool window. Highlight each by its visible frame.
- A color loupe needs nearest-neighbor scaling, which XAML `Image` lacks.
  Build the zoomed pixels byte by byte, draw them at 1:1 physical pixels,
  and round the loupe's position to whole physical pixels. Arrow keys
  move the pointer one physical pixel with `SetCursorPos`.
- Window shot: try `TryCreateFromWindowId` ("Screen capture" in
  recipes.md); when it fails, crop the window's visible frame from the
  frozen image.
- Pickers built in code need theme-following brushes. A brush read from
  `Application.Current.Resources` in code keeps the app theme's color
  under an element with its own `RequestedTheme`. Load styles with
  `{ThemeResource}` setters from a `ResourceDictionary` file instead (the
  `csharp-winui` skill).

## Screen recording to MP4

A `Windows.Graphics.Capture` session feeds a `MediaStreamSource` that
`MediaTranscoder` encodes to H.264. Three threads, none of them the UI
thread: capture (`FrameArrived` on a free-threaded pool), Media
Foundation (`SampleRequested`), and the transcode task. This worked first
time in that suite, at 30 fps with none dropped for a window that changed on
every refresh.

The device: one for the app's lifetime, and unlike the screenshot
device, keep the `ID3D11Device` and its immediate context, and turn on
multithread protection, because Media Foundation reads your textures from
its own threads. Direct vtable calls avoid a Vortice or SharpDX package;
each slot is the method's position in the interface, `IUnknown`'s three
first.

```csharp
internal sealed unsafe partial class GpuDevice
{
    public static GpuDevice Shared { get; } = new();
    private readonly nint _device, _context;
    public IDirect3DDevice Device { get; }
    /// The immediate context is not thread safe: every use goes through Lock.
    public Lock Lock { get; } = new();

    private GpuDevice()
    {
        Marshal.ThrowExceptionForHR(D3D11CreateDevice(0, 1 /* HARDWARE */, 0, 0x20 /* BGRA_SUPPORT */, 0, 0, 7, out _device, out _, out _context));
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(_device, in IID_ID3D10Multithread, out var multithread));
        ((delegate* unmanaged[Stdcall]<nint, int, int>)Vtbl(multithread)[5])(multithread, 1);   // SetMultithreadProtected(TRUE)
        Marshal.Release(multithread);
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(_device, in IID_IDXGIDevice, out var dxgi));
        try
        {
            Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi, out var inspectable));
            try { Device = WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(inspectable); }
            finally { Marshal.Release(inspectable); }
        }
        finally { Marshal.Release(dxgi); }
    }

    /// A BGRA texture the encoder can read, wrapped as a WinRT surface. Dispose frees GPU memory.
    public GpuTexture CreateTexture(int width, int height)
    {
        var desc = new D3D11_TEXTURE2D_DESC
        {
            Width = (uint)width, Height = (uint)height, MipLevels = 1, ArraySize = 1,
            Format = 87 /* DXGI_FORMAT_B8G8R8A8_UNORM */, SampleCount = 1,
            BindFlags = 0x20 /* RENDER_TARGET */ | 0x8 /* SHADER_RESOURCE */,
        };
        nint texture;
        // ID3D11Device::CreateTexture2D is slot 5.
        Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, D3D11_TEXTURE2D_DESC*, nint, nint*, int>)Vtbl(_device)[5])(_device, &desc, 0, &texture));
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(texture, in IID_IDXGISurface, out var dxgi));
        try
        {
            Marshal.ThrowExceptionForHR(CreateDirect3D11SurfaceFromDXGISurface(dxgi, out var inspectable));
            try { return new GpuTexture(texture, WinRT.MarshalInterface<IDirect3DSurface>.FromAbi(inspectable)); }
            finally { Marshal.Release(inspectable); }
        }
        finally { Marshal.Release(dxgi); }
    }

    /// ID3D11DeviceContext::CopySubresourceRegion (slot 46): a box from (x, y) of source to target's top left. Inside Lock.
    public void CopyRegion(GpuTexture target, nint source, int x, int y, int width, int height)
    {
        var box = new D3D11_BOX { Left = (uint)x, Top = (uint)y, Right = (uint)(x + width), Bottom = (uint)(y + height), Back = 1 };
        ((delegate* unmanaged[Stdcall]<nint, nint, uint, uint, uint, uint, nint, uint, D3D11_BOX*, void>)Vtbl(_context)[46])(_context, target.Texture, 0, 0, 0, 0, source, 0, &box);
    }

    /// ID3D11DeviceContext::CopyResource (slot 47). Inside Lock.
    public void Copy(GpuTexture target, GpuTexture source) =>
        ((delegate* unmanaged[Stdcall]<nint, nint, nint, void>)Vtbl(_context)[47])(_context, target.Texture, source.Texture);

    /// Black. ID3D11Device::CreateRenderTargetView (slot 9), ID3D11DeviceContext::ClearRenderTargetView (slot 50). Inside Lock.
    public void Clear(GpuTexture target)
    {
        nint view;
        Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, nint, nint, nint*, int>)Vtbl(_device)[9])(_device, target.Texture, 0, &view));
        var black = stackalloc float[] { 0, 0, 0, 1 };
        ((delegate* unmanaged[Stdcall]<nint, nint, float*, void>)Vtbl(_context)[50])(_context, view, black);
        Marshal.Release(view);
    }

    /// The ID3D11Texture2D behind a capture frame, no copy. Release it when done.
    public static nint TextureOf(IDirect3DSurface surface)
    {
        var abi = WinRT.MarshalInterface<IDirect3DSurface>.FromManaged(surface);
        try
        {
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(abi, in IID_IDirect3DDxgiInterfaceAccess, out var access));
            try
            {
                var iid = IID_ID3D11Texture2D;
                nint texture;
                // IDirect3DDxgiInterfaceAccess::GetInterface is slot 3.
                Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)Vtbl(access)[3])(access, &iid, &texture));
                return texture;
            }
            finally { Marshal.Release(access); }
        }
        finally { Marshal.Release(abi); }
    }

    private static void** Vtbl(nint com) => *(void***)com;

    private static readonly Guid IID_IDXGIDevice = new("54EC77FA-1377-44E6-8C32-88FD5F44C84C");
    private static readonly Guid IID_IDXGISurface = new("CAFCB56C-6AC3-4889-BF47-9E23BBD260EC");
    private static readonly Guid IID_ID3D11Texture2D = new("6F15AAF2-D208-4E89-9AB4-489535D34F9C");
    private static readonly Guid IID_ID3D10Multithread = new("9B7E4E00-342C-4106-A19F-4F2704F689F0");
    private static readonly Guid IID_IDirect3DDxgiInterfaceAccess = new("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1");

    [StructLayout(LayoutKind.Sequential)]
    private struct D3D11_TEXTURE2D_DESC { public uint Width, Height, MipLevels, ArraySize, Format, SampleCount, SampleQuality, Usage, BindFlags, CPUAccessFlags, MiscFlags; }
    [StructLayout(LayoutKind.Sequential)] private struct D3D11_BOX { public uint Left, Top, Front, Right, Bottom, Back; }

    [LibraryImport("d3d11.dll")] private static partial int D3D11CreateDevice(nint adapter, int driverType, nint software, uint flags, nint levels, uint levelCount, uint sdkVersion, out nint device, out int level, out nint context);
    [LibraryImport("d3d11.dll")] private static partial int CreateDirect3D11DeviceFromDXGIDevice(nint dxgiDevice, out nint graphicsDevice);
    [LibraryImport("d3d11.dll")] private static partial int CreateDirect3D11SurfaceFromDXGISurface(nint dxgiSurface, out nint graphicsSurface);
}

internal sealed class GpuTexture(nint texture, IDirect3DSurface surface) : IDisposable
{
    private nint _texture = texture;
    public nint Texture => _texture;
    public IDirect3DSurface Surface { get; } = surface;

    public void Dispose()
    {
        var t = Interlocked.Exchange(ref _texture, 0);
        if (t == 0) return;
        Surface.Dispose();
        Marshal.Release(t);
    }
}
```

The encoder. `crop` is the visible frame inside a window's capture
(`visible - outer`, as in "Screen capture" in recipes.md); a display
needs none. Width and height are rounded down to even numbers.

```csharp
using System.Collections.Concurrent;
using System.Diagnostics;
using Windows.Foundation.Metadata;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;
using Windows.Storage.Streams;

internal sealed class CaptureEncoder(GraphicsCaptureItem item, int fps, uint bitrate, string path, int cropX, int cropY, int width, int height)
{
    private readonly GpuDevice _gpu = GpuDevice.Shared;
    private readonly BlockingCollection<(GpuTexture Texture, TimeSpan Time)> _queue = new(90);   // about 3 s at 30 fps
    private readonly FrameClock _clock = new(fps);
    private readonly Lock _gate = new();
    private Direct3D11CaptureFramePool? _pool;
    private GraphicsCaptureSession? _session;
    private GpuTexture? _canvas;   // the last frame, kept to repeat at stop
    private SizeInt32 _poolSize;
    private IRandomAccessStream? _stream;
    private Task? _transcode;
    private bool _stopping;

    public int Frames { get; private set; }
    public int Dropped { get; private set; }

    public async Task StartAsync(bool borderless)
    {
        _canvas = _gpu.CreateTexture(width, height);
        lock (_gpu.Lock) _gpu.Clear(_canvas);
        _poolSize = item.Size;
        _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(_gpu.Device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, _poolSize);
        _pool.FrameArrived += OnFrameArrived;
        _session = _pool.CreateCaptureSession(item);
        // borderless: only when CaptureBorder.State() is Allowed (recipes.md, "Screen capture").
        if (borderless && ApiInformation.IsPropertyPresent(typeof(GraphicsCaptureSession).FullName, "IsBorderRequired"))
            _session.IsBorderRequired = false;
        // Windows 11 24H2 and later. 90% of the frame time, not all of it: frames land on display
        // refreshes, and exactly 33.3 ms on a 60 Hz display recorded 26 fps for 30. At 90%: 30.1 fps.
        if (ApiInformation.IsPropertyPresent(typeof(GraphicsCaptureSession).FullName, "MinUpdateInterval"))
            _session.MinUpdateInterval = TimeSpan.FromTicks(TimeSpan.TicksPerSecond * 9 / 10 / fps);

        var input = VideoEncodingProperties.CreateUncompressed(MediaEncodingSubtypes.Bgra8, (uint)width, (uint)height);
        var source = new MediaStreamSource(new VideoStreamDescriptor(input)) { BufferTime = TimeSpan.Zero, CanSeek = false };
        source.Starting += (_, e) => e.Request.SetActualStartPosition(TimeSpan.Zero);
        source.SampleRequested += OnSampleRequested;

        var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD1080p);
        profile.Audio = null;                         // no audio stream in the source
        profile.Video.Width = (uint)width;
        profile.Video.Height = (uint)height;
        profile.Video.Bitrate = bitrate;              // your rule, from size and fps
        profile.Video.FrameRate.Numerator = (uint)fps;
        profile.Video.FrameRate.Denominator = 1;
        profile.Video.PixelAspectRatio.Numerator = 1;
        profile.Video.PixelAspectRatio.Denominator = 1;

        // No StorageFile needed: this works unpackaged.
        _stream = await FileRandomAccessStream.OpenAsync(path, FileAccessMode.ReadWrite, StorageOpenOptions.None, FileOpenDisposition.CreateAlways);
        _session.StartCapture();
        var prepared = await new MediaTranscoder { HardwareAccelerationEnabled = true }
            .PrepareMediaStreamSourceTranscodeAsync(source, _stream, profile);
        if (!prepared.CanTranscode)
        {
            _session.Dispose();
            _pool.Dispose();
            throw new InvalidOperationException($"the encoder refused ({prepared.FailureReason})");
        }
        _transcode = prepared.TranscodeAsync().AsTask();
    }

    private void OnFrameArrived(Direct3D11CaptureFramePool pool, object _)
    {
        try
        {
            // Dispose at once: the pool has two buffers, and a held frame stalls the capture with no error.
            using var frame = pool.TryGetNextFrame();
            if (frame is null) return;
            lock (_gate)
            {
                if (_stopping || _canvas is null) return;
                if (_clock.Keep(frame.SystemRelativeTime) is not { } time) return;
                // The canvas keeps the start size. A window that shrinks gets black around it; one that grows is cut.
                var content = frame.ContentSize;
                int w = Math.Min(width, content.Width - cropX), h = Math.Min(height, content.Height - cropY);
                // The pool reuses its surfaces and the encoder reads later: copy into a texture of our own.
                var texture = _gpu.CreateTexture(width, height);
                lock (_gpu.Lock)
                {
                    if (w < width || h < height) _gpu.Clear(_canvas);
                    if (w > 0 && h > 0)
                    {
                        var source = GpuDevice.TextureOf(frame.Surface);
                        try { _gpu.CopyRegion(_canvas, source, cropX, cropY, w, h); }
                        finally { Marshal.Release(source); }
                    }
                    _gpu.Copy(texture, _canvas);
                }
                if (_queue.TryAdd((texture, time))) Frames++;
                else { texture.Dispose(); Dropped++; }
            }
            // A window that changed size: the pool's buffers follow it.
            if (frame.ContentSize.Width != _poolSize.Width || frame.ContentSize.Height != _poolSize.Height)
            {
                _poolSize = frame.ContentSize;
                pool.Recreate(_gpu.Device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, _poolSize);
            }
        }
        catch (Exception e) { AgentLog.Line($"recorder frame: {e}"); }
    }

    private void OnSampleRequested(MediaStreamSource sender, MediaStreamSourceSampleRequestedEventArgs args)
    {
        try
        {
            // Blocks this Media Foundation thread until a frame comes. After Stop the queue is
            // complete, TryTake returns false, and a null sample ends the stream and the transcode.
            if (!_queue.TryTake(out var next, Timeout.Infinite)) { args.Request.Sample = null; return; }
            var sample = MediaStreamSample.CreateFromDirect3D11Surface(next.Texture.Surface, next.Time);
            sample.Processed += (_, _) => next.Texture.Dispose();
            args.Request.Sample = sample;
        }
        catch (Exception e)
        {
            AgentLog.Line($"recorder sample: {e}");
            args.Request.Sample = null;
        }
    }

    /// True when the MP4 is complete with its index.
    public async Task<bool> StopAsync()
    {
        lock (_gate)
        {
            if (_stopping) return false;
            _stopping = true;
            // A still window sends no frames. Repeat the last one at the stop time, or a still
            // ending makes the movie short. SystemRelativeTime is QPC: put "now" on that clock.
            var now = TimeSpan.FromTicks((long)(Stopwatch.GetTimestamp() * ((double)TimeSpan.TicksPerSecond / Stopwatch.Frequency)));
            if (_canvas is not null && _clock.End(now) is { } end)
            {
                var texture = _gpu.CreateTexture(width, height);
                lock (_gpu.Lock) _gpu.Copy(texture, _canvas);
                if (!_queue.TryAdd((texture, end))) texture.Dispose();
            }
            _queue.CompleteAdding();
        }
        _session?.Dispose();
        if (_pool is not null) { _pool.FrameArrived -= OnFrameArrived; _pool.Dispose(); }
        var done = false;
        try { if (_transcode is not null) { await _transcode.WaitAsync(TimeSpan.FromSeconds(5)); done = true; } }
        catch (Exception e) { AgentLog.Line($"recorder: the encoder did not finish: {e.Message}"); }
        _stream?.Dispose();
        while (_queue.TryTake(out var left)) left.Texture.Dispose();
        _canvas?.Dispose();
        return done;
    }
}

/// Movie time from capture time: the first frame is zero, frames closer than the frame time are dropped.
public sealed class FrameClock(int fps)
{
    private readonly TimeSpan _interval = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / fps);
    private TimeSpan? _start;
    private TimeSpan _lastKept = TimeSpan.MinValue;

    public TimeSpan? Keep(TimeSpan systemTime)
    {
        _start ??= systemTime;
        var time = systemTime - _start.Value;
        if (_lastKept != TimeSpan.MinValue && time - _lastKept < _interval - TimeSpan.FromMilliseconds(2)) return null;
        return _lastKept = time;
    }

    /// The time of a repeated last frame at stop, or null when it adds nothing.
    public TimeSpan? End(TimeSpan systemTime)
    {
        if (_start is null) return null;
        var time = systemTime - _start.Value;
        return time - _lastKept >= TimeSpan.FromMilliseconds(5) ? time : null;
    }
}
```

Gotchas:

- Keep the device and context. The screenshot recipe's `CreateDevice`
  releases both; a recorder needs them to copy frames.
- Without `SetMultithreadProtected(TRUE)` Media Foundation and your copies
  use the immediate context at the same time.
- A window capture is the window rect with its invisible border. Crop
  each frame with `CopySubresourceRegion` from `(visible.X - outer.X,
  visible.Y - outer.Y)`. A decoded frame then shows the window edge to
  edge, with no border and no shadow. Only crop when the item's size
  equals the window rect.
- A window that changes size: the pool follows `frame.ContentSize`
  (`pool.Recreate`); the canvas and the movie keep the start size.
- `GraphicsCaptureItem.Closed` fires when the recorded window closes. Stop
  and save from it, on the UI thread.
- A minimized window draws nothing. Restore it before you start.
- Prepare takes about 0.3 s after `StartCapture`. The movie starts at the
  first frame, so the file is that much longer than a clock started after
  prepare.
- Stopping waits for the encoder to write the MP4 index (up to 5 s). A
  synchronous shutdown loses a recording that runs at quit; give the app
  an async stop on quit.
- Write to `%TEMP%`, then move the finished file into Videos under a
  unique name. A 0-byte file means the encoder failed: delete it.
- Keep the recording capsule out of the video with
  `WDA_EXCLUDEFROMCAPTURE` ([overlays.md](overlays.md)).
- Not built yet: system audio and the microphone. Those need WASAPI
  loopback and capture, resampling to 48 kHz, silence fill, and a second
  stream in the `MediaStreamSource`. A safe self-test is hard: loopback
  records whatever the person plays.

More GPU and encoder rules are in the `performance` skill's
[gpu-media.md](../performance/gpu-media.md).

## Live window thumbnails

A switcher's previews: DWM draws a live copy of another window into a
rect of your window. No frames to copy, no capture border.

```csharp
[StructLayout(LayoutKind.Sequential)]
public struct DWM_THUMBNAIL_PROPERTIES
{
    public uint dwFlags;
    public RECT rcDestination, rcSource;
    public byte opacity;                         // a byte, then two BOOLs: easy to get wrong
    public int fVisible, fSourceClientAreaOnly;
}

const uint DWM_TNP_RECTDESTINATION = 1, DWM_TNP_RECTSOURCE = 2, DWM_TNP_OPACITY = 4, DWM_TNP_VISIBLE = 8, DWM_TNP_SOURCECLIENTAREAONLY = 0x10;

// Once per tile in view; keep the handle.
DwmRegisterThumbnail(panelHwnd, sourceHwnd, out var thumbnail);

// After layout, and on every scroll. Destination: the panel's client area, physical pixels.
var scale = previewArea.XamlRoot.RasterizationScale;
var origin = previewArea.TransformToVisual(panelRoot).TransformPoint(default);
var dest = new RECT
{
    Left = (int)Math.Round(origin.X * scale), Top = (int)Math.Round(origin.Y * scale),
    Right = (int)Math.Round((origin.X + previewArea.ActualWidth) * scale),
    Bottom = (int)Math.Round((origin.Y + previewArea.ActualHeight) * scale),
};
// Source: the visible frame inside the window rect, which cuts the invisible borders.
GetWindowRect(sourceHwnd, out var outer);
DwmGetWindowAttribute(sourceHwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT frame, Marshal.SizeOf<RECT>());
var src = new RECT { Left = frame.Left - outer.Left, Top = frame.Top - outer.Top, Right = frame.Right - outer.Left, Bottom = frame.Bottom - outer.Top };
var props = new DWM_THUMBNAIL_PROPERTIES
{
    dwFlags = DWM_TNP_RECTDESTINATION | DWM_TNP_RECTSOURCE | DWM_TNP_VISIBLE | DWM_TNP_OPACITY | DWM_TNP_SOURCECLIENTAREAONLY,
    rcDestination = FitAspect(dest, src.Right - src.Left, src.Bottom - src.Top),   // centered, source aspect ratio
    rcSource = src,
    opacity = 255,
    fVisible = 1,
};
DwmUpdateThumbnailProperties(thumbnail, ref props);

// On hide, and for tiles that scrolled out of view:
DwmUnregisterThumbnail(thumbnail);

[LibraryImport("dwmapi.dll")] static partial int DwmRegisterThumbnail(nint destination, nint source, out nint thumbnail);
[LibraryImport("dwmapi.dll")] static partial int DwmUnregisterThumbnail(nint thumbnail);
[LibraryImport("dwmapi.dll")] static partial int DwmUpdateThumbnailProperties(nint thumbnail, ref DWM_THUMBNAIL_PROPERTIES properties);
```

Gotchas:

- Confirmed on Windows 11: XAML position times `XamlRoot.RasterizationScale`
  lands the thumbnail exactly on the tile.
- Thumbnails draw over your XAML, not inside it. Place them again after
  every layout change and scroll, and once more at low dispatcher
  priority after the panel opens.
- Register only the tiles fully in view, with a cap (24 worked), and
  unregister all on hide.
- Skip your own window and handles that are gone (`IsWindow`).
- `PrintWindow(PW_RENDERFULLCONTENT)` captures DWM thumbnails, so snapshot
  mode can photograph a panel with previews.

## Test captures safely

A self-test never captures the person's screen, never posts a capture
hotkey, and never asks for borderless capture.

- Capture a test window of your own: borderless and square ("Borderless
  overlay window" in [overlays.md](overlays.md)), four known colors with
  no two channels equal, no transitions, topmost, near the bottom left of
  the screen (other test runs open windows in the middle), and brought to
  the front before every read. The full-display path is the same
  `Capture` call with a display's rect.
- Drive pickers through their own methods (move, press, release, key)
  with the test window's areas as the areas to cover. A capture hotkey
  always freezes the whole screen.
- A window that changes every refresh: animate it from
  `CompositionTarget.Rendering`. A 33 ms `DispatcherTimer` ticks about 20
  times a second (the 15.6 ms system timer), and the test then measures
  the test window, not the recorder.
- Prove an MP4 without ffprobe:
  `StorageFile.GetFileFromPathAsync`, then `MediaClip.CreateFromFileAsync`
  (`OriginalDuration`, and `GetVideoEncodingProperties()` for the subtype,
  width, and height), and `MediaComposition.GetThumbnailAsync` for frames
  to compare. Works unpackaged.
- Checks that held: the MP4 exists, is H.264, has the window's
  visible size, its duration is within 30 percent of the recording,
  frames change, and the capsule is excluded. A still window for 2 s
  still gives a movie of the right length.
- Never call `RequestAccessAsync` in a test run, so no prompt shows.
- Test runs never touch the clipboard: keep the package the capture would
  copy and check that instead.

Done when a screenshot of the test window matches its four colors byte
for byte, a window shot has no border, a recording of the test window
passes the MP4 checks, and no self-test read the person's screen.

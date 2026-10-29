# Saves a PNG of one window of a running app: the largest visible window whose process name
# or title matches. Other windows on screen never show up in the file.
# Usage: window-shot.ps1 <process name or window title> <out.png>
#   window-shot.ps1 notepad docs\screens\01-main.png
#   window-shot.ps1 "Settings" docs\screens\08-settings.png
#   window-shot.ps1 -List                     every visible window: process, size, title
# The name matches a process name (without .exe) first, then any part of a window title.
# Store apps such as Calculator draw inside ApplicationFrameHost: match them by title.
# Uses PrintWindow(PW_RENDERFULLCONTENT), so a covered window still captures as it looks.
# The invisible resize border is cut off with the DWM frame bounds.
param(
    [Parameter(Position = 0)][string]$App,
    [Parameter(Position = 1)][string]$Out,
    [switch]$List
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

if (-not ('BawaWindowShot' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public class BawaWindow
{
    public IntPtr Handle;
    public int ProcessId;
    public string Title;
    public int Width;
    public int Height;
}

public static class BawaWindowShot
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc proc, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out int pid);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
    [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int width, int height);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out RECT value, int size);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);

    const uint GW_OWNER = 4;
    const uint PW_RENDERFULLCONTENT = 2;
    const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    const int DWMWA_CLOAKED = 14;
    // Per-monitor aware: every rectangle is in physical pixels, on every display scale.
    static readonly IntPtr PerMonitorAwareV2 = new IntPtr(-4);

    static RECT Frame(IntPtr hwnd)
    {
        RECT r;
        if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out r, Marshal.SizeOf(typeof(RECT))) != 0)
            GetWindowRect(hwnd, out r);
        return r;
    }

    /// Visible, top-level, not minimized, not cloaked (hidden Store app frames, other desktops).
    public static List<BawaWindow> Windows()
    {
        var old = SetThreadDpiAwarenessContext(PerMonitorAwareV2);
        var found = new List<BawaWindow>();
        try
        {
            EnumWindows(delegate (IntPtr hwnd, IntPtr lParam)
            {
                if (!IsWindowVisible(hwnd) || IsIconic(hwnd) || GetWindow(hwnd, GW_OWNER) != IntPtr.Zero) return true;
                int cloaked;
                if (DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out cloaked, 4) == 0 && cloaked != 0) return true;
                var text = new StringBuilder(512);
                GetWindowText(hwnd, text, text.Capacity);
                var r = Frame(hwnd);
                int pid;
                GetWindowThreadProcessId(hwnd, out pid);
                if (r.Right - r.Left < 40 || r.Bottom - r.Top < 40) return true;
                found.Add(new BawaWindow { Handle = hwnd, ProcessId = pid, Title = text.ToString(), Width = r.Right - r.Left, Height = r.Bottom - r.Top });
                return true;
            }, IntPtr.Zero);
        }
        finally { SetThreadDpiAwarenessContext(old); }
        return found;
    }

    /// Renders the window into a bitmap and returns it with the visible frame inside it.
    /// The caller owns the HBITMAP and frees it with DeleteObject.
    public static IntPtr Capture(IntPtr hwnd, out int x, out int y, out int width, out int height)
    {
        var old = SetThreadDpiAwarenessContext(PerMonitorAwareV2);
        try
        {
            RECT outer;
            GetWindowRect(hwnd, out outer);
            var frame = Frame(hwnd);
            int w = outer.Right - outer.Left, h = outer.Bottom - outer.Top;
            x = Math.Max(0, frame.Left - outer.Left);
            y = Math.Max(0, frame.Top - outer.Top);
            width = Math.Min(frame.Right - frame.Left, w - x);
            height = Math.Min(frame.Bottom - frame.Top, h - y);
            var screen = GetDC(IntPtr.Zero);
            var dc = CreateCompatibleDC(screen);
            var bitmap = CreateCompatibleBitmap(screen, w, h);
            var previous = SelectObject(dc, bitmap);
            var ok = PrintWindow(hwnd, dc, PW_RENDERFULLCONTENT);
            SelectObject(dc, previous);
            DeleteDC(dc);
            ReleaseDC(IntPtr.Zero, screen);
            if (!ok) { DeleteObject(bitmap); return IntPtr.Zero; }
            return bitmap;
        }
        finally { SetThreadDpiAwarenessContext(old); }
    }
}
'@
}

function ProcessName([int]$id) {
    $p = Get-Process -Id $id -ErrorAction SilentlyContinue
    if ($p) { $p.ProcessName } else { '?' }
}

$windows = [BawaWindowShot]::Windows()

if ($List) {
    $windows | ForEach-Object {
        '{0,-24} {1,5} x {2,-5} {3}' -f (ProcessName $_.ProcessId), $_.Width, $_.Height, $_.Title
    }
    exit 0
}

if (-not $App -or -not $Out) { Write-Output 'usage: window-shot.ps1 <process name or window title> <out.png>   (or -List)'; exit 1 }
$name = $App -replace '\.exe$', ''
$matched = @($windows | Where-Object { (ProcessName $_.ProcessId) -eq $name })
if ($matched.Count -eq 0) { $matched = @($windows | Where-Object { $_.Title -like "*$App*" }) }
if ($matched.Count -eq 0) {
    Write-Output "no visible window for `"$App`" (is it open and not minimized? try -List)"
    exit 1
}
$window = $matched | Sort-Object { $_.Width * $_.Height } -Descending | Select-Object -First 1

$x = 0; $y = 0; $w = 0; $h = 0
$hbitmap = [BawaWindowShot]::Capture($window.Handle, [ref]$x, [ref]$y, [ref]$w, [ref]$h)
if ($hbitmap -eq [IntPtr]::Zero) { Write-Output "capture failed: PrintWindow returned false for `"$($window.Title)`""; exit 1 }
$path = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Out)
$folder = Split-Path $path -Parent
if ($folder) { New-Item -ItemType Directory -Force $folder | Out-Null }
$full = [System.Drawing.Image]::FromHbitmap($hbitmap)
try {
    $crop = $full.Clone((New-Object System.Drawing.Rectangle $x, $y, $w, $h), [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    try {
        # A window that draws nothing for PrintWindow comes out as one flat color.
        $first = $crop.GetPixel(0, 0)
        $flat = $true
        foreach ($p in @(@(0.5, 0.5), @(0.25, 0.75), @(0.75, 0.25), @(0.9, 0.9))) {
            if ($crop.GetPixel([int]($w * $p[0]), [int]($h * $p[1])) -ne $first) { $flat = $false; break }
        }
        $crop.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    } finally { $crop.Dispose() }
} finally {
    $full.Dispose()
    [void][BawaWindowShot]::DeleteObject($hbitmap)
}
if ($flat) { Write-Output "warning: the capture is one flat color; the window may draw nothing off screen. Look at it before you trust it." }
Write-Output "$path ($w x $h, $(ProcessName $window.ProcessId): $($window.Title))"

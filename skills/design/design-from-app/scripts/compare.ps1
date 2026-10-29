# Compares a rebuild with its reference screenshot.
#   compare.ps1 reference.png build.png out-folder [x y w h] [-Scale 1.25]
# Writes side-by-side.png, diff.png (red where pixels differ), and overlay.png
# (the build at 50% over the reference), and prints how many pixels differ.
# An optional x y w h (epx) compares one region, so you can fix one part at a time.
# Both images must have the same scale. -Scale defaults to the Scale in the build's
# <shot>-layout.json (a BAWA snapshot writes one), else 1.
# Runs in Windows PowerShell 5.1 and in pwsh 7.
param(
    [Parameter(Position = 0, Mandatory = $true)][string]$Reference,
    [Parameter(Position = 1, Mandatory = $true)][string]$Build,
    [Parameter(Position = 2, Mandatory = $true)][string]$Out,
    [double]$Scale = 0,
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$Region = @()
)
$ErrorActionPreference = 'Stop'

$source = @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

public static class BawaCompare
{
    // BGRA pixels of the image (or of a region of it), on a white canvas of width x height.
    static byte[] Pixels(Bitmap image, Rectangle region, int width, int height)
    {
        using (var canvas = new Bitmap(width, height, PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(canvas))
            {
                g.Clear(Color.White);
                g.DrawImage(image, new Rectangle(0, 0, region.Width, region.Height), region, GraphicsUnit.Pixel);
            }
            return Read(canvas);
        }
    }

    static byte[] Read(Bitmap bitmap)
    {
        var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var bytes = new byte[bitmap.Width * bitmap.Height * 4];
        for (int y = 0; y < bitmap.Height; y++)
            Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), bytes, y * bitmap.Width * 4, bitmap.Width * 4);
        bitmap.UnlockBits(data);
        return bytes;
    }

    static void Write(byte[] bytes, int width, int height, string path)
    {
        using (var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb))
        {
            var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            for (int y = 0; y < height; y++)
                Marshal.Copy(bytes, y * width * 4, IntPtr.Add(data.Scan0, y * data.Stride), width * 4);
            bitmap.UnlockBits(data);
            bitmap.Save(path, ImageFormat.Png);
        }
    }

    // region: x y w h in pixels, or null for the whole image.
    public static string[] Run(string referencePath, string buildPath, string outDir, int[] region)
    {
        Directory.CreateDirectory(outDir);
        using (var reference = new Bitmap(referencePath))
        using (var build = new Bitmap(buildPath))
        {
            var ra = region == null ? new Rectangle(0, 0, reference.Width, reference.Height) : new Rectangle(region[0], region[1], region[2], region[3]);
            var rb = region == null ? new Rectangle(0, 0, build.Width, build.Height) : ra;
            int width = Math.Max(ra.Width, rb.Width), height = Math.Max(ra.Height, rb.Height);
            var a = Pixels(reference, ra, width, height);
            var b = Pixels(build, rb, width, height);

            var diff = new byte[a.Length];
            var overlay = new byte[a.Length];
            int different = 0;
            for (int i = 0; i < a.Length; i += 4)
            {
                int delta = Math.Max(Math.Abs(a[i] - b[i]), Math.Max(Math.Abs(a[i + 1] - b[i + 1]), Math.Abs(a[i + 2] - b[i + 2])));
                int gray = (a[i] + a[i + 1] + a[i + 2]) / 3;
                if (delta > 24)
                {
                    different++;
                    diff[i] = 40; diff[i + 1] = 40; diff[i + 2] = 230;   // BGR: red
                }
                else
                {
                    // The reference, faded, so the red stands out.
                    byte faded = (byte)(255 - (255 - gray) / 4);
                    diff[i] = faded; diff[i + 1] = faded; diff[i + 2] = faded;
                }
                diff[i + 3] = 255;
                for (int c = 0; c < 3; c++) overlay[i + c] = (byte)((a[i + c] + b[i + c]) / 2);
                overlay[i + 3] = 255;
            }

            var side = new byte[width * 2 * height * 4];
            for (int y = 0; y < height; y++)
            {
                Buffer.BlockCopy(a, y * width * 4, side, y * width * 8, width * 4);
                Buffer.BlockCopy(b, y * width * 4, side, y * width * 8 + width * 4, width * 4);
            }

            Write(side, width * 2, height, Path.Combine(outDir, "side-by-side.png"));
            Write(diff, width, height, Path.Combine(outDir, "diff.png"));
            Write(overlay, width, height, Path.Combine(outDir, "overlay.png"));
            double percent = (double)different / ((double)width * height) * 100;
            return new[]
            {
                string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0.00}% of pixels differ ({1} of {2})", percent, different, width * height),
                "wrote " + Path.Combine(outDir, "side-by-side.png") + ", diff.png, overlay.png",
            };
        }
    }
}
'@

if (-not ('BawaCompare' -as [type])) {
    # Windows PowerShell has System.Drawing; pwsh 7 splits it into System.Drawing.Common and .Primitives.
    $refs = if ($PSVersionTable.PSEdition -eq 'Core') { 'System.Drawing.Common', 'System.Drawing.Primitives' } else { 'System.Drawing' }
    Add-Type -AssemblyName System.Drawing
    Add-Type -TypeDefinition $source -ReferencedAssemblies $refs
}

$referencePath = (Resolve-Path $Reference).Path
$buildPath = (Resolve-Path $Build).Path
# .NET resolves relative paths against the process folder, not the PowerShell location.
$outPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Out)
if ($Scale -le 0) {
    $layout = [IO.Path]::ChangeExtension($buildPath, $null).TrimEnd('.') + '-layout.json'
    $Scale = if (Test-Path $layout) { [double](Get-Content $layout -Raw | ConvertFrom-Json).Scale } else { 1 }
}
$pixels = $null
if ($Region.Count -ge 4) {
    $inv = [Globalization.CultureInfo]::InvariantCulture
    $pixels = [int[]]($Region[0..3] | ForEach-Object { [math]::Round([double]::Parse($_, $inv) * $Scale) })
}
[BawaCompare]::Run($referencePath, $buildPath, $outPath, $pixels)

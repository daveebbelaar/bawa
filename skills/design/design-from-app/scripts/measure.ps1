# Measures a screenshot so a design can be rebuilt from numbers, not guesses.
# Coordinates in and out are epx: pixels divided by -Scale.
#
#   measure.ps1 info    shot.png
#   measure.ps1 color   shot.png x y [x y ...]       hex color at each point
#   measure.ps1 hline   shot.png y [x0 x1]           color runs along a row
#   measure.ps1 vline   shot.png x [y0 y1]           color runs down a column
#   measure.ps1 rows    shot.png x0 x1 [y0 y1]       bands of content (text, icons) between x0 and x1
#   measure.ps1 cols    shot.png y0 y1 [x0 x1]       bands of content between y0 and y1
#   measure.ps1 palette shot.png [x y w h]           most used colors in a region
#   measure.ps1 ink     shot.png x y w h             text and icon colors: pixels far from the background
#   measure.ps1 crop    shot.png x y w h out.png [zoom]
# Options: -Scale 1.25     pixels per epx. Default: the Scale in <shot>-layout.json next to a
#                          BAWA snapshot, else 1. Windows Settings > System > Display > Scale: 125% is 1.25.
#          -Tolerance 12   0-255, how different a color must be to count
# Runs in Windows PowerShell 5.1 and in pwsh 7.
param(
    [Parameter(Position = 0, Mandatory = $true)][string]$Command,
    [Parameter(Position = 1, Mandatory = $true)][string]$Shot,
    [double]$Scale = 0,
    [double]$Tolerance = 12,
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$Rest = @()
)
$ErrorActionPreference = 'Stop'

$source = @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;

public static class BawaMeasure
{
    static int W, H;
    static byte[] Data;   // BGRA rows
    static double Scale, Tolerance;
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    static void Load(string path)
    {
        using (var bitmap = new Bitmap(path))
        {
            W = bitmap.Width; H = bitmap.Height;
            var data = bitmap.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            Data = new byte[W * H * 4];
            for (int y = 0; y < H; y++)
                Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), Data, y * W * 4, W * 4);
            bitmap.UnlockBits(data);
        }
    }

    static int[] Pixel(int x, int y)
    {
        x = Math.Min(Math.Max(x, 0), W - 1); y = Math.Min(Math.Max(y, 0), H - 1);
        int i = (y * W + x) * 4;
        return new[] { (int)Data[i + 2], (int)Data[i + 1], (int)Data[i] };
    }

    static string Hex(int[] c) { return string.Format("#{0:X2}{1:X2}{2:X2}", c[0], c[1], c[2]); }
    static string Bucket(int[] c) { return Hex(new[] { c[0] / 4 * 4, c[1] / 4 * 4, c[2] / 4 * 4 }); }
    static double Distance(int[] a, int[] b)
    {
        return Math.Max(Math.Abs(a[0] - b[0]), Math.Max(Math.Abs(a[1] - b[1]), Math.Abs(a[2] - b[2])));
    }
    static int Px(string epx) { return (int)Math.Round(double.Parse(epx, Inv) * Scale); }
    static string Ep(int pixels) { return (pixels / Scale).ToString("0.##", Inv); }

    public static string[] Run(string command, string path, double scale, double tolerance, string[] rest)
    {
        Scale = scale; Tolerance = tolerance;
        Load(path);
        var o = new List<string>();
        switch (command)
        {
            case "info":
                o.Add(string.Format("{0} x {1} px = {2} x {3} epx at scale {4}", W, H, Ep(W), Ep(H), Scale.ToString("0.##", Inv)));
                break;
            case "color":
                for (int i = 0; i + 1 < rest.Length; i += 2)
                    o.Add(rest[i] + "," + rest[i + 1] + "  " + Hex(Pixel(Px(rest[i]), Px(rest[i + 1]))));
                break;
            case "hline":
            {
                int y = Px(rest[0]);
                int x0 = rest.Length > 2 ? Px(rest[1]) : 0, x1 = rest.Length > 2 ? Px(rest[2]) : W - 1;
                Runs(o, Enumerable.Range(x0, x1 - x0 + 1).Select(x => Tuple.Create(x, Pixel(x, y))).ToList());
                break;
            }
            case "vline":
            {
                int x = Px(rest[0]);
                int y0 = rest.Length > 2 ? Px(rest[1]) : 0, y1 = rest.Length > 2 ? Px(rest[2]) : H - 1;
                Runs(o, Enumerable.Range(y0, y1 - y0 + 1).Select(y => Tuple.Create(y, Pixel(x, y))).ToList());
                break;
            }
            case "rows":
            {
                int x0 = Px(rest[0]), x1 = Px(rest[1]);
                int y0 = rest.Length > 3 ? Px(rest[2]) : 0, y1 = rest.Length > 3 ? Px(rest[3]) : H;
                var background = Pixel(x0, y0);
                o.Add("background " + Hex(background));
                Bands(o, y1 - y0, y0, i => { for (int x = x0; x < x1; x++) if (Distance(Pixel(x, y0 + i), background) > Tolerance) return true; return false; });
                break;
            }
            case "cols":
            {
                int y0 = Px(rest[0]), y1 = Px(rest[1]);
                int x0 = rest.Length > 3 ? Px(rest[2]) : 0, x1 = rest.Length > 3 ? Px(rest[3]) : W;
                var background = Pixel(x0, y0);
                o.Add("background " + Hex(background));
                Bands(o, x1 - x0, x0, i => { for (int y = y0; y < y1; y++) if (Distance(Pixel(x0 + i, y), background) > Tolerance) return true; return false; });
                break;
            }
            case "palette":
            {
                int x = rest.Length >= 4 ? Px(rest[0]) : 0, y = rest.Length >= 4 ? Px(rest[1]) : 0;
                int w = rest.Length >= 4 ? Px(rest[2]) : W, h = rest.Length >= 4 ? Px(rest[3]) : H;
                var counts = new Dictionary<string, int>();
                for (int yy = y; yy < y + h; yy++)
                    for (int xx = x; xx < x + w; xx++)
                    {
                        var key = Bucket(Pixel(xx, yy));
                        int n; counts.TryGetValue(key, out n); counts[key] = n + 1;
                    }
                double total = (double)w * h;
                foreach (var pair in counts.OrderByDescending(p => p.Value).Take(12))
                    o.Add(pair.Key + "  " + (pair.Value / total * 100).ToString("0.0", Inv) + "%");
                break;
            }
            case "ink":
            {
                if (rest.Length < 4) { o.Add("ink needs x y w h"); break; }
                int x = Px(rest[0]), y = Px(rest[1]), w = Px(rest[2]), h = Px(rest[3]);
                var background = Pixel(x, y);
                var counts = new Dictionary<string, int>();
                var strongest = background; double best = 0;
                for (int yy = y; yy < y + h; yy++)
                    for (int xx = x; xx < x + w; xx++)
                    {
                        var c = Pixel(xx, yy);
                        double d = Distance(c, background);
                        if (d <= 90) continue;
                        var key = Bucket(c);
                        int n; counts.TryGetValue(key, out n); counts[key] = n + 1;
                        if (d > best) { best = d; strongest = c; }
                    }
                o.Add("background " + Hex(background) + "  strongest " + Hex(strongest));
                foreach (var pair in counts.OrderByDescending(p => p.Value).Take(4)) o.Add(pair.Key + "  " + pair.Value + " px");
                break;
            }
            case "crop":
            {
                int x = Px(rest[0]), y = Px(rest[1]), w = Px(rest[2]), h = Px(rest[3]);
                string output = rest[4];
                double zoom = rest.Length > 5 ? double.Parse(rest[5], Inv) : 1;
                using (var input = new Bitmap(path))
                using (var result = new Bitmap((int)(w * zoom), (int)(h * zoom)))
                using (var g = Graphics.FromImage(result))
                {
                    // Nearest neighbor and half pixel offset: every source pixel becomes a clean square.
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.DrawImage(input, new Rectangle(0, 0, result.Width, result.Height), new Rectangle(x, y, w, h), GraphicsUnit.Pixel);
                    result.Save(output, ImageFormat.Png);
                }
                o.Add("wrote " + output);
                break;
            }
            default:
                o.Add("unknown command " + command);
                break;
        }
        return o.ToArray();
    }

    // Runs of similar color along a line of pixels.
    static void Runs(List<string> o, List<Tuple<int, int[]>> samples)
    {
        if (samples.Count == 0) return;
        var start = samples[0]; var last = start;
        foreach (var sample in samples.Skip(1))
        {
            if (Distance(sample.Item2, start.Item2) > Tolerance)
            {
                o.Add(Ep(start.Item1) + ".." + Ep(last.Item1) + "  (" + Ep(last.Item1 - start.Item1 + 1) + " epx)  " + Hex(start.Item2));
                start = sample;
            }
            last = sample;
        }
        o.Add(Ep(start.Item1) + ".." + Ep(last.Item1) + "  (" + Ep(last.Item1 - start.Item1 + 1) + " epx)  " + Hex(start.Item2));
    }

    // Content bands: lines where some pixel differs from the region's first pixel (the background).
    static void Bands(List<string> o, int count, int origin, Func<int, bool> isContent)
    {
        int start = -1, found = 0;
        for (int i = 0; i < count; i++)
        {
            if (isContent(i)) { if (start < 0) start = i; }
            else if (start >= 0)
            {
                o.Add(Ep(start + origin) + ".." + Ep(i + origin) + "  size " + Ep(i - start));
                start = -1; found++;
            }
        }
        if (start >= 0) o.Add(Ep(start + origin) + ".." + Ep(count + origin) + "  size " + Ep(count - start));
        else if (found == 0) o.Add("no content");
    }
}
'@

if (-not ('BawaMeasure' -as [type])) {
    # Windows PowerShell has System.Drawing; pwsh 7 splits it into System.Drawing.Common and .Primitives.
    $refs = if ($PSVersionTable.PSEdition -eq 'Core') { 'System.Drawing.Common', 'System.Drawing.Primitives' } else { 'System.Drawing' }
    Add-Type -AssemblyName System.Drawing
    Add-Type -TypeDefinition $source -ReferencedAssemblies $refs
}

$path = (Resolve-Path $Shot).Path
if ($Scale -le 0) {
    # A BAWA snapshot writes <page>-<mode>-layout.json with the display scale next to the PNG.
    $layout = [IO.Path]::ChangeExtension($path, $null).TrimEnd('.') + '-layout.json'
    $Scale = if (Test-Path $layout) { [double](Get-Content $layout -Raw | ConvertFrom-Json).Scale } else { 1 }
}
if ($Command -eq 'crop' -and $Rest.Count -ge 5) {
    # .NET resolves relative paths against the process folder, not the PowerShell location.
    $Rest[4] = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Rest[4])
}
[BawaMeasure]::Run($Command, $path, $Scale, $Tolerance, [string[]]$Rest)

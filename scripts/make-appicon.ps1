<#
Generate src/TransitShelf.App/Assets/TransitShelf.ico from vector drawing code.

Usage:  powershell -ExecutionPolicy Bypass -File scripts\make-appicon.ps1

Why a script instead of a checked-in drawing file: the icon is "rounded rose folder +
white arrow" in two flat colors, which is exactly the kind of shape that must be redrawn
per size to stay legible at 16 px. Drawing it here keeps the sizes consistent and the
source reviewable; the .ico itself is generated and committed.

The folder uses the fixed brand rose (#EA999C) on purpose: the app icon is the product
identity, not a themed surface, so it does not follow the palette switch.

IMPORTANT: keep this file ASCII-only (see the note in dev.ps1).
#>
param(
    [string] $OutFile = (Join-Path (Split-Path -Parent $PSScriptRoot) 'src\TransitShelf.App\Assets\TransitShelf.ico')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

Add-Type -Namespace ShelfIcon -Name Ico -ReferencedAssemblies System.Drawing -MemberDefinition @'
public static void Write(string path, System.Drawing.Bitmap[] frames)
{
    var images = new byte[frames.Length][];
    for (var i = 0; i < frames.Length; i++) { images[i] = ToDib(frames[i]); }

    using (var stream = System.IO.File.Create(path))
    using (var writer = new System.IO.BinaryWriter(stream))
    {
        writer.Write((short)0);
        writer.Write((short)1);
        writer.Write((short)frames.Length);

        var offset = 6 + 16 * frames.Length;
        for (var i = 0; i < frames.Length; i++)
        {
            var frame = frames[i];
            writer.Write((byte)(frame.Width >= 256 ? 0 : frame.Width));
            writer.Write((byte)(frame.Height >= 256 ? 0 : frame.Height));
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((short)1);
            writer.Write((short)32);
            writer.Write(images[i].Length);
            writer.Write(offset);
            offset += images[i].Length;
        }

        foreach (var image in images) { writer.Write(image); }
    }
}

// 32bpp DIB entry: BITMAPINFOHEADER + bottom-up BGRA rows + empty AND mask.
// DIB (not PNG) is used for every size so System.Drawing.Icon can load it everywhere.
private static byte[] ToDib(System.Drawing.Bitmap bitmap)
{
    int width = bitmap.Width, height = bitmap.Height;
    var xor = new byte[width * height * 4];
    var data = bitmap.LockBits(
        new System.Drawing.Rectangle(0, 0, width, height),
        System.Drawing.Imaging.ImageLockMode.ReadOnly,
        System.Drawing.Imaging.PixelFormat.Format32bppArgb);
    try
    {
        var row = new byte[width * 4];
        for (var y = 0; y < height; y++)
        {
            System.Runtime.InteropServices.Marshal.Copy(
                System.IntPtr.Add(data.Scan0, y * data.Stride), row, 0, width * 4);
            System.Buffer.BlockCopy(row, 0, xor, (height - 1 - y) * width * 4, width * 4);
        }
    }
    finally
    {
        bitmap.UnlockBits(data);
    }

    var andStride = ((width + 31) / 32) * 4;
    var mask = new byte[andStride * height];

    using (var memory = new System.IO.MemoryStream())
    using (var writer = new System.IO.BinaryWriter(memory))
    {
        writer.Write(40);
        writer.Write(width);
        writer.Write(height * 2);
        writer.Write((short)1);
        writer.Write((short)32);
        writer.Write(0);
        writer.Write(xor.Length + mask.Length);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(xor);
        writer.Write(mask);
        writer.Flush();
        return memory.ToArray();
    }
}
'@

function Add-RoundedRect($path, [float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $d = 2 * $r
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
}

# Draws one frame. All coordinates are fractions of the canvas so every size scales.
function New-Frame([int] $size) {
    $bitmap = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.Clear([System.Drawing.Color]::Transparent)

    $s = [float]$size
    $rose = [System.Drawing.Color]::FromArgb(255, 0xEA, 0x99, 0x9C)
    $white = [System.Drawing.Color]::FromArgb(255, 255, 255, 255)

    # Tab first, then body: the two rounded rects merge into one folder silhouette.
    $folder = New-Object System.Drawing.Drawing2D.GraphicsPath
    Add-RoundedRect $folder (0.055 * $s) (0.185 * $s) (0.415 * $s) (0.175 * $s) (0.075 * $s)
    Add-RoundedRect $folder (0.055 * $s) (0.300 * $s) (0.890 * $s) (0.575 * $s) (0.115 * $s)
    $brush = New-Object System.Drawing.SolidBrush $rose
    $graphics.FillPath($brush, $folder)

    # Arrow: a rounded shaft plus a triangular head, kept chunky enough for 16 px.
    $arrow = New-Object System.Drawing.Drawing2D.GraphicsPath
    Add-RoundedRect $arrow (0.290 * $s) (0.535 * $s) (0.320 * $s) (0.115 * $s) (0.055 * $s)
    $arrow.AddPolygon(@(
        (New-Object System.Drawing.PointF((0.560 * $s), (0.410 * $s))),
        (New-Object System.Drawing.PointF((0.560 * $s), (0.775 * $s))),
        (New-Object System.Drawing.PointF((0.820 * $s), (0.592 * $s)))
    ))
    $arrowBrush = New-Object System.Drawing.SolidBrush $white
    $graphics.FillPath($arrowBrush, $arrow)

    $arrowBrush.Dispose()
    $brush.Dispose()
    $arrow.Dispose()
    $folder.Dispose()
    $graphics.Dispose()
    return $bitmap
}

$sizes = @(16, 20, 24, 32, 48, 64, 128)
$frames = @()
foreach ($size in $sizes) { $frames += New-Frame $size }

$directory = Split-Path -Parent $OutFile
New-Item -ItemType Directory -Force -Path $directory | Out-Null
[ShelfIcon.Ico]::Write($OutFile, $frames)
foreach ($frame in $frames) { $frame.Dispose() }

$written = Get-Item -LiteralPath $OutFile
Write-Host "wrote $($written.FullName) ($($written.Length) bytes, sizes: $($sizes -join ', '))"

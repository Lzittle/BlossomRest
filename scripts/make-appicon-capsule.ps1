<#
Generate app icons in the shape of the collapsed capsule (PillPanel).

Usage:  powershell -ExecutionPolicy Bypass -File scripts\make-appicon-capsule.ps1

Why a script instead of a checked-in PNG: the icon is pure geometry (rounded tile,
circle, tray glyph, count badge). Vector drawing keeps every size crisp and lets the
stroke weight be adjusted per size, which a downscaled bitmap cannot do.

Two variants are produced:
  TransitShelf-capsule.ico       faithful to the capsule: cream tile + rose tray
  TransitShelf-capsule-rose.ico  same composition, brand rose tile + cream tray

The tray glyph is the same IconShelf geometry used by the capsule and the empty state
(src/TransitShelf.App/Resources/Icons.xaml), redrawn here on the same 16x16 grid.

IMPORTANT: keep this file ASCII-only (see the note in dev.ps1).
#>
param(
    [string] $OutDir = (Join-Path (Split-Path -Parent $PSScriptRoot) 'src\TransitShelf.App\Assets'),
    [string] $Glyph = '',
    [string] $Carve = '',
    [string] $Suffix = ''
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName PresentationFramework
Add-Type -AssemblyName WindowsBase

# Same ICO container writer as make-appicon.ps1: 32bpp DIB frames, so System.Drawing.Icon
# (used by the tray icon) can load every frame on every .NET version.
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

# The brand mark: a rose bloom with a leaf, plus a carved swirl that suggests the petals.
# Drawn on the same 16x16 grid as the other icons so it stays consistent with them.
$roseBloom = 'F1 M 7.7,2.4 A 4.9,4.9 0 1 1 7.7,12.2 A 4.9,4.9 0 1 1 7.7,2.4 Z M 10.9,12.1 C 11.7,11.1 12.9,10.8 13.7,11.0 C 13.3,12.2 12.2,13.1 10.9,13.3 Z'
$roseSwirl = 'M 8.0,4.7 A 3.1,3.1 0 1 1 4.7,7.8 M 7.5,6.5 A 1.5,1.5 0 1 1 6.1,7.9'
if ([string]::IsNullOrWhiteSpace($Glyph)) { $Glyph = $roseBloom }
if ([string]::IsNullOrWhiteSpace($Carve)) { $Carve = $roseSwirl }

function New-Brush([string] $hex, [double] $opacity = 1.0) {
    $brush = New-Object System.Windows.Media.SolidColorBrush ([System.Windows.Media.ColorConverter]::ConvertFromString($hex))
    $brush.Opacity = $opacity
    return $brush
}

# Draws one frame at its native size. Everything is laid out in a 1024-unit design space
# and scaled, so proportions are identical across sizes.
function New-Frame([int] $size, [hashtable] $palette) {
    $unit = $size / 1024.0

    $visual = New-Object System.Windows.Media.DrawingVisual
    $dc = $visual.RenderOpen()
    $dc.PushTransform((New-Object System.Windows.Media.ScaleTransform($unit, $unit)))

    # Rounded tile (the window body in its collapsed state)
    $tilePen = New-Object System.Windows.Media.Pen((New-Brush $palette.TileEdge), 14)
    $dc.DrawRoundedRectangle((New-Brush $palette.Tile), $tilePen, (New-Object System.Windows.Rect(70, 70, 884, 884)), 190, 190)

    # Circular handle (the pill itself)
    $circlePen = New-Object System.Windows.Media.Pen((New-Brush $palette.CircleEdge), 10)
    $dc.DrawEllipse((New-Brush $palette.Circle), $circlePen, (New-Object System.Windows.Point(512, 512)), 302, 302)

    # Tray glyph
    $scale = 38.0
    $group = New-Object System.Windows.Media.TransformGroup
    $group.Children.Add((New-Object System.Windows.Media.ScaleTransform($scale, $scale)))
    $group.Children.Add((New-Object System.Windows.Media.TranslateTransform((512 - 8 * $scale), (512 - 8 * $scale))))
    $dc.PushTransform($group)
    $dc.DrawGeometry((New-Brush $palette.Glyph), $null, ([System.Windows.Media.Geometry]::Parse($Glyph)))
    $dc.Pop()

    # Carved petal swirl, drawn in the circle colour so it reads as a groove.
    if (-not [string]::IsNullOrWhiteSpace($Carve)) {
        $carvePen = New-Object System.Windows.Media.Pen((New-Brush $palette.Circle), 1.15)
        $carvePen.StartLineCap = 'Round'
        $carvePen.EndLineCap = 'Round'
        $dc.PushTransform($group)
        $dc.DrawGeometry($null, $carvePen, ([System.Windows.Media.Geometry]::Parse($Carve)))
        $dc.Pop()
    }

    # Count badge. The digit is only drawn at 48 px and above: below that it renders as
    # a smudge, so small frames keep a clean dot instead.
    $dc.DrawEllipse((New-Brush $palette.Badge), $null, (New-Object System.Windows.Point(800, 224)), 118, 118)
    if ($size -ge 48) {
        $typeface = New-Object System.Windows.Media.Typeface('Segoe UI', [System.Windows.FontStyles]::Normal, [System.Windows.FontWeights]::SemiBold, [System.Windows.FontStretches]::Normal)
        $text = [System.Windows.Media.FormattedText]::new('0',
            [System.Globalization.CultureInfo]::InvariantCulture,
            [System.Windows.FlowDirection]::LeftToRight,
            $typeface,
            132, (New-Brush $palette.BadgeText), 96)
        $dc.DrawText($text, (New-Object System.Windows.Point((800 - $text.Width / 2), (224 - $text.Height / 2))))
    }

    $dc.Pop()
    $dc.Close()

    $rtb = New-Object System.Windows.Media.Imaging.RenderTargetBitmap($size, $size, 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32))
    $rtb.Render($visual)

    $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($rtb))
    $stream = New-Object System.IO.MemoryStream
    $encoder.Save($stream)
    $stream.Position = 0
    $loaded = [System.Drawing.Bitmap]::new($stream)
    $frame = [System.Drawing.Bitmap]::new($loaded)
    $loaded.Dispose()
    $stream.Dispose()
    return $frame
}

# Faithful to the capsule: the light-theme surface colour.
$lightPalette = @{
    Tile       = '#FAF4ED'
    TileEdge   = '#E6DCD2'
    Circle     = '#FFFCF7'
    CircleEdge = '#E6DCD2'
    Glyph      = '#B4637A'
    Badge      = '#B4637A'
    BadgeText  = '#FAF4ED'
}

# Same composition on the brand rose, so the icon still reads on a light taskbar.
$rosePalette = @{
    Tile       = '#EA999C'
    TileEdge   = '#DC8F92'
    Circle     = '#F5B7B9'
    CircleEdge = '#E5A0A3'
    Glyph      = '#FAF4ED'
    Badge      = '#B4637A'
    BadgeText  = '#FAF4ED'
}

$sizes = @(16, 20, 24, 32, 48, 64, 128, 256)
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

foreach ($variant in @(@("TransitShelf-capsule$Suffix.ico", $lightPalette), @("TransitShelf-capsule-rose$Suffix.ico", $rosePalette))) {
    $path = Join-Path $OutDir $variant[0]
    $frames = @()
    foreach ($size in $sizes) { $frames += New-Frame $size $variant[1] }
    [ShelfIcon.Ico]::Write($path, $frames)
    foreach ($frame in $frames) { $frame.Dispose() }
    $written = Get-Item -LiteralPath $path
    Write-Host "wrote $($written.FullName) ($($written.Length) bytes, sizes: $($sizes -join ', '))"
}

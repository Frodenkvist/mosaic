# Generates Mosaic.ico — a mosaic-tile app icon in the accent purple palette.
# Renders several sizes and packs them as DIB (BMP) entries into one .ico
# (DIB is the most broadly compatible icon encoding for the C# compiler + Windows shell).
Add-Type -AssemblyName System.Drawing

$root = Split-Path $PSScriptRoot -Parent
$outPath = Join-Path $root 'Mosaic.ico'
$pngPath = Join-Path $root 'Mosaic.png'        # high-res image used inside the app UI
$previewPath = Join-Path $env:TEMP 'mosaic_icon_preview.png'

$accent  = [System.Drawing.Color]::FromArgb(255, 0x6C, 0x5C, 0xE7)
$accentL = [System.Drawing.Color]::FromArgb(255, 0x8B, 0x7C, 0xF0)
$accentD = [System.Drawing.Color]::FromArgb(255, 0x5A, 0x4B, 0xD0)
$muted   = [System.Drawing.Color]::FromArgb(255, 0x3C, 0x3C, 0x4A)
$matrix = @(
    @($accent,  $accentD, $muted),
    @($accentL, $accent,  $accentD),
    @($muted,   $accentL, $accent)
)

function New-RoundedPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function Render-Bitmap([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $bgRect = New-Object System.Drawing.RectangleF(0, 0, $s, $s)
    $bgPath = New-RoundedPath 0 0 $s $s ($s * 0.22)
    $c1 = [System.Drawing.Color]::FromArgb(255, 0x23, 0x23, 0x30)
    $c2 = [System.Drawing.Color]::FromArgb(255, 0x14, 0x14, 0x1A)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($bgRect, $c1, $c2, 90)
    $g.FillPath($brush, $bgPath)
    $brush.Dispose(); $bgPath.Dispose()

    $margin = $s * 0.17
    $gap = $s * 0.045
    $area = $s - ($margin * 2)
    $tile = ($area - ($gap * 2)) / 3
    $radius = [Math]::Max(1.0, $tile * 0.24)
    for ($row = 0; $row -lt 3; $row++) {
        for ($col = 0; $col -lt 3; $col++) {
            $tx = $margin + $col * ($tile + $gap)
            $ty = $margin + $row * ($tile + $gap)
            $tp = New-RoundedPath $tx $ty $tile $tile $radius
            $tb = New-Object System.Drawing.SolidBrush($matrix[$row][$col])
            $g.FillPath($tb, $tp)
            $tb.Dispose(); $tp.Dispose()
        }
    }
    $g.Dispose()
    return $bmp
}

function Get-DibBytes([System.Drawing.Bitmap]$bmp) {
    $s = $bmp.Width
    $rect = New-Object System.Drawing.Rectangle(0, 0, $s, $s)
    $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $stride = $data.Stride
    $buffer = New-Object byte[] ($stride * $s)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $buffer, 0, $buffer.Length)
    $bmp.UnlockBits($data)

    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    # BITMAPINFOHEADER
    $bw.Write([UInt32]40)          # biSize
    $bw.Write([Int32]$s)           # biWidth
    $bw.Write([Int32]($s * 2))     # biHeight (XOR + AND)
    $bw.Write([UInt16]1)           # biPlanes
    $bw.Write([UInt16]32)          # biBitCount
    $bw.Write([UInt32]0)           # biCompression
    $bw.Write([UInt32]0)           # biSizeImage
    $bw.Write([Int32]0); $bw.Write([Int32]0)
    $bw.Write([UInt32]0); $bw.Write([UInt32]0)
    # XOR pixels, bottom-up (BGRA already from Format32bppArgb memory layout)
    for ($y = $s - 1; $y -ge 0; $y--) {
        $bw.Write($buffer, $y * $stride, $stride)
    }
    # AND mask (1bpp, rows padded to 4 bytes), all zero -> rely on alpha
    $maskRow = [int][Math]::Floor((($s + 31) / 32)) * 4
    $zeros = New-Object byte[] ($maskRow * $s)
    $bw.Write($zeros, 0, $zeros.Length)
    $bw.Flush()
    return ,$ms.ToArray()
}

$sizes = @(256, 128, 64, 48, 32, 24, 16)
$dibs = @{}
foreach ($sz in $sizes) {
    $bmp = Render-Bitmap $sz
    if ($sz -eq 256) { $bmp.Save($previewPath, [System.Drawing.Imaging.ImageFormat]::Png) }
    $dibs[$sz] = Get-DibBytes $bmp
    $bmp.Dispose()
}

# High-res PNG for crisp in-app rendering at any display scale.
$hi = Render-Bitmap 512
$hi.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)
$hi.Dispose()

$fs = New-Object System.IO.FileStream($outPath, [System.IO.FileMode]::Create)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + (16 * $sizes.Count)
foreach ($sz in $sizes) {
    $bytes = $dibs[$sz]
    $dim = if ($sz -ge 256) { 0 } else { $sz }
    $bw.Write([Byte]$dim); $bw.Write([Byte]$dim)
    $bw.Write([Byte]0); $bw.Write([Byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$bytes.Length); $bw.Write([UInt32]$offset)
    $offset += $bytes.Length
}
foreach ($sz in $sizes) { $bw.Write($dibs[$sz]) }
$bw.Flush(); $bw.Close(); $fs.Close()
Write-Output "Wrote $outPath ($((Get-Item $outPath).Length) bytes). Preview: $previewPath"

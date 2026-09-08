$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetDir = Join-Path (Split-Path $PSScriptRoot -Parent) 'src/SkuMaster.Desktop/Assets'
New-Item -ItemType Directory -Path $assetDir -Force | Out-Null
$images = @()
foreach ($size in @(16, 24, 32, 48, 64, 128, 256)) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.ScaleTransform($size / 256.0, $size / 256.0)
    $green = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#126A55'))
    $gold = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#F2CD81'))
    $white = [System.Drawing.Pen]::new([System.Drawing.Color]::White, 13)
    $white.StartCap = $white.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $g.FillEllipse($green, 4, 4, 248, 248)
    $g.FillRectangle($gold, 55, 65, 142, 132)
    $tape = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#FFF0C9'))
    $g.FillRectangle($tape, 111, 65, 30, 48)
    $g.FillEllipse($green, 121, 128, 104, 104)
    $g.DrawLines($white, [System.Drawing.PointF[]]@([System.Drawing.PointF]::new(146,177), [System.Drawing.PointF]::new(166,197), [System.Drawing.PointF]::new(204,157)))
    $stream = [System.IO.MemoryStream]::new()
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $images += ,$stream.ToArray()
    if ($size -eq 256) { $bitmap.Save((Join-Path $assetDir 'app.png'), [System.Drawing.Imaging.ImageFormat]::Png) }
    $stream.Dispose(); $g.Dispose(); $bitmap.Dispose(); $green.Dispose(); $gold.Dispose(); $tape.Dispose(); $white.Dispose()
}
$output = [System.IO.File]::Create((Join-Path $assetDir 'app.ico'))
$writer = [System.IO.BinaryWriter]::new($output)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$images.Count)
    $offset = 6 + 16 * $images.Count
    $sizes = @(16,24,32,48,64,128,256)
    for ($i=0; $i -lt $images.Count; $i++) {
        $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$images[$i].Length); $writer.Write([uint32]$offset)
        $offset += $images[$i].Length
    }
    foreach ($bytes in $images) { $writer.Write([byte[]]$bytes) }
} finally { $writer.Dispose(); $output.Dispose() }

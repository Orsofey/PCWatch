param([string]$OutputDirectory = (Join-Path $PSScriptRoot '..\src\PCWatch\Assets'))
Add-Type -AssemblyName System.Drawing
$size = 256
$bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$g.Clear([System.Drawing.Color]::Transparent)
function RoundRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}
$shadow = RoundRect 17 23 222 214 51
$shadowBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(110, 0, 0, 0))
$g.FillPath($shadowBrush, $shadow)
$shape = RoundRect 16 16 224 224 52
$gradient = New-Object System.Drawing.Drawing2D.LinearGradientBrush ([System.Drawing.RectangleF]::new(16,16,224,224)), ([System.Drawing.Color]::FromArgb(255,68,111,246)), ([System.Drawing.Color]::FromArgb(255,119,76,222)), 42
$g.FillPath($gradient, $shape)
$inner = RoundRect 30 30 196 196 40
$innerBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(42, 8, 18, 44))
$g.FillPath($innerBrush, $inner)
$monitor = RoundRect 49 62 158 116 20
$monitorPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(240,239,246,255)), 10
$monitorPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$monitorPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$g.DrawPath($monitorPen, $monitor)
$pulsePen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255,76,235,213)), 10
$pulsePen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$pulsePen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$pulsePen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
$points = [System.Drawing.PointF[]]@(
    [System.Drawing.PointF]::new(65,123), [System.Drawing.PointF]::new(91,123),
    [System.Drawing.PointF]::new(105,94), [System.Drawing.PointF]::new(124,148),
    [System.Drawing.PointF]::new(141,109), [System.Drawing.PointF]::new(154,123),
    [System.Drawing.PointF]::new(191,123))
$g.DrawLines($pulsePen, $points)
$standPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(235,239,246,255)), 10
$standPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$g.DrawLine($standPen, 128, 178, 128, 195)
$g.DrawLine($standPen, 97, 198, 159, 198)
$dotBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255,90,237,166))
$g.FillEllipse($dotBrush, 183, 45, 19, 19)
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$pngPath = Join-Path $OutputDirectory 'PCWatch.png'
$bmp.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)
$ms = New-Object System.IO.MemoryStream
$bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
$png = $ms.ToArray()
$icoPath = Join-Path $OutputDirectory 'PCWatch.ico'
$stream = [System.IO.File]::Create($icoPath)
$writer = New-Object System.IO.BinaryWriter $stream
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]1)
$writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([byte]0)
$writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$png.Length); $writer.Write([uint32]22)
$writer.Write($png); $writer.Flush(); $writer.Dispose(); $stream.Dispose(); $ms.Dispose()
$g.Dispose(); $bmp.Dispose()
$shadow.Dispose(); $shape.Dispose(); $inner.Dispose(); $monitor.Dispose()
$shadowBrush.Dispose(); $gradient.Dispose(); $innerBrush.Dispose(); $monitorPen.Dispose(); $pulsePen.Dispose(); $standPen.Dispose(); $dotBrush.Dispose()
Write-Output $icoPath

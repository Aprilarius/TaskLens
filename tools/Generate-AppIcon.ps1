param(
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\Assets\TaskLens.ico')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$directory = Split-Path -Parent $OutputPath
New-Item -ItemType Directory -Path $directory -Force | Out-Null

$bitmap = [System.Drawing.Bitmap]::new(256, 256)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.Clear([System.Drawing.Color]::Transparent)

$background = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(39, 100, 231))
$graphics.FillEllipse($background, 8, 8, 240, 240)

$lensPen = [System.Drawing.Pen]::new([System.Drawing.Color]::White, 18)
$lensPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$lensPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$graphics.DrawEllipse($lensPen, 55, 48, 112, 112)
$graphics.DrawLine($lensPen, 151, 145, 207, 201)

$chartPen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(183, 221, 255), 10)
$chartPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$chartPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$points = [System.Drawing.Point[]]@(
    [System.Drawing.Point]::new(76, 112),
    [System.Drawing.Point]::new(98, 91),
    [System.Drawing.Point]::new(116, 122),
    [System.Drawing.Point]::new(143, 79)
)
$graphics.DrawLines($chartPen, $points)

$handle = $bitmap.GetHicon()
$icon = [System.Drawing.Icon]::FromHandle($handle)
$stream = [System.IO.File]::Create([System.IO.Path]::GetFullPath($OutputPath))
$icon.Save($stream)
$stream.Dispose()

$icon.Dispose()
$chartPen.Dispose()
$lensPen.Dispose()
$background.Dispose()
$graphics.Dispose()
$bitmap.Dispose()

Write-Host "Created: $OutputPath"

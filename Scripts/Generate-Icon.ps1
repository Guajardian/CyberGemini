param(
    [Parameter(Mandatory = $true)]
    [string]$Output
)

Add-Type -AssemblyName System.Drawing

$size = 256
$bmp = New-Object System.Drawing.Bitmap $size, $size
$gfx = [System.Drawing.Graphics]::FromImage($bmp)
$gfx.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$gfx.Clear([System.Drawing.Color]::Transparent)

$pen1 = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(71, 194, 255)), 12
$pen2 = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(123, 97, 255)), 12
$pen3 = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(230, 234, 242)), 10

$gfx.DrawEllipse($pen1, 48, 48, 120, 120)
$gfx.DrawEllipse($pen2, 88, 48, 120, 120)

$path = New-Object System.Drawing.Drawing2D.GraphicsPath
$path.AddBezier(88, 120, 120, 60, 168, 60, 200, 120)
$gfx.DrawPath($pen3, $path)

$hicon = $bmp.GetHicon()
$icon = [System.Drawing.Icon]::FromHandle($hicon)

$dir = [System.IO.Path]::GetDirectoryName($Output)
if (-not [System.IO.Directory]::Exists($dir)) {
    [System.IO.Directory]::CreateDirectory($dir) | Out-Null
}

$stream = New-Object System.IO.FileStream($Output, [System.IO.FileMode]::Create)
$icon.Save($stream)
$stream.Close()

$gfx.Dispose()
$bmp.Dispose()
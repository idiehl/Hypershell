[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
if ([Threading.Thread]::CurrentThread.GetApartmentState() -ne 'STA') {
    throw 'Run with pwsh -STA -File scripts/package-brand-icon.ps1.'
}
Add-Type -AssemblyName PresentationCore, WindowsBase
$brandDirectory = Join-Path (Split-Path -Parent $PSScriptRoot) 'assets/brand'
$sourcePath = Join-Path $brandDirectory 'meridian.png'
$source = [Windows.Media.Imaging.BitmapImage]::new()
$source.BeginInit()
$source.CacheOption = [Windows.Media.Imaging.BitmapCacheOption]::OnLoad
$source.UriSource = [Uri]::new($sourcePath)
$source.EndInit()
$source.Freeze()
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$frames = [Collections.Generic.List[byte[]]]::new()
foreach ($size in $sizes) {
    $visual = [Windows.Media.DrawingVisual]::new()
    [Windows.Media.RenderOptions]::SetBitmapScalingMode($visual, [Windows.Media.BitmapScalingMode]::HighQuality)
    $drawing = $visual.RenderOpen()
    try {
        $scale = $size / [double][Math]::Max($source.PixelWidth, $source.PixelHeight)
        $width = $source.PixelWidth * $scale
        $height = $source.PixelHeight * $scale
        $drawing.DrawImage($source, [Windows.Rect]::new(($size - $width) / 2, ($size - $height) / 2, $width, $height))
    } finally { $drawing.Close() }
    $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new($size, $size, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $pixels = [byte[]]::new($size * $size * 4)
    $bitmap.CopyPixels($pixels, $size * 4, 0)
    foreach ($corner in @(0, ($size - 1), ($size * ($size - 1)), ($size * $size - 1))) {
        if ($pixels[$corner * 4 + 3] -ne 0) { throw 'Icon corners must remain transparent.' }
    }
    $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [IO.MemoryStream]::new()
    try { $encoder.Save($stream); $frames.Add($stream.ToArray()) }
    finally { $stream.Dispose() }
}
$iconPath = Join-Path $brandDirectory 'meridian.ico'
$writer = [IO.BinaryWriter]::new([IO.File]::Create($iconPath))
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    for ($i = 0; $i -lt $frames.Count; $i++) {
        $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$i].Length); $writer.Write([uint32]$offset)
        $offset += $frames[$i].Length
    }
    foreach ($frame in $frames) { $writer.Write($frame) }
} finally { $writer.Dispose() }
$stream = [IO.File]::OpenRead($iconPath)
try {
    $decoder = [Windows.Media.Imaging.IconBitmapDecoder]::new($stream,
        [Windows.Media.Imaging.BitmapCreateOptions]::PreservePixelFormat,
        [Windows.Media.Imaging.BitmapCacheOption]::OnLoad)
    if ($decoder.Frames.Count -ne $sizes.Count) { throw 'Missing icon frames.' }
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        if ($decoder.Frames[$i].PixelWidth -ne $sizes[$i] -or $decoder.Frames[$i].PixelHeight -ne $sizes[$i]) {
            throw 'Incorrect icon dimensions.'
        }
    }
} finally { $stream.Dispose() }
Write-Output "MERIDIAN_ICON_OK|frames=$($sizes -join ',')|transparent-corners|$iconPath"

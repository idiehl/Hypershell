param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase
if ([Threading.Thread]::CurrentThread.GetApartmentState() -ne 'STA') {
    throw 'Run this renderer in an STA PowerShell session.'
}
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repositoryRoot 'src/Hypershell/Assets/MyNet'
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)

function Get-Number($element, [string]$name, [double]$fallback = 0) {
    $value = $element.GetAttribute($name)
    if ([string]::IsNullOrEmpty($value)) { return $fallback }
    return [double]::Parse($value, [Globalization.CultureInfo]::InvariantCulture)
}
function Get-Brush([string]$value) {
    if ([string]::IsNullOrEmpty($value) -or $value -eq 'none') { return $null }
    return [Windows.Media.BrushConverter]::new().ConvertFromInvariantString($value)
}
function Render-Svg([xml]$svg, [int]$size) {
    $visual = [Windows.Media.DrawingVisual]::new()
    $drawing = $visual.RenderOpen()
    try {
        $drawing.PushTransform([Windows.Media.TranslateTransform]::new(1, 1))
        $drawing.PushTransform([Windows.Media.ScaleTransform]::new(($size - 2) / 64.0, ($size - 2) / 64.0))
        foreach ($element in $svg.DocumentElement.ChildNodes) {
            if ($element -isnot [Xml.XmlElement]) { continue }
            $fill = Get-Brush $element.GetAttribute('fill')
            $stroke = Get-Brush $element.GetAttribute('stroke')
            $pen = $null
            if ($null -ne $stroke) {
                $pen = [Windows.Media.Pen]::new($stroke, (Get-Number $element 'stroke-width' 1))
                if ($element.GetAttribute('stroke-linecap') -eq 'round') {
                    $pen.StartLineCap = $pen.EndLineCap = [Windows.Media.PenLineCap]::Round
                }
                if ($element.GetAttribute('stroke-linejoin') -eq 'round') {
                    $pen.LineJoin = [Windows.Media.PenLineJoin]::Round
                }
            }
            switch ($element.LocalName) {
                'rect' {
                    $rect = [Windows.Rect]::new((Get-Number $element 'x'), (Get-Number $element 'y'),
                        (Get-Number $element 'width'), (Get-Number $element 'height'))
                    $radius = Get-Number $element 'rx'
                    $drawing.DrawRoundedRectangle($fill, $pen, $rect, $radius, $radius)
                }
                'circle' {
                    $drawing.DrawEllipse($fill, $pen,
                        [Windows.Point]::new((Get-Number $element 'cx'), (Get-Number $element 'cy')),
                        (Get-Number $element 'r'), (Get-Number $element 'r'))
                }
                'path' { $drawing.DrawGeometry($fill, $pen, [Windows.Media.Geometry]::Parse($element.GetAttribute('d'))) }
                default { throw "Unsupported SVG element: $($element.LocalName)" }
            }
        }
        $drawing.Pop()
        $drawing.Pop()
    }
    finally { $drawing.Close() }
    $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new($size, $size, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    return $bitmap
}
function Get-PngBytes($bitmap) {
    $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [IO.MemoryStream]::new()
    try { $encoder.Save($stream); return ,$stream.ToArray() }
    finally { $stream.Dispose() }
}

foreach ($name in @('ping-lab', 'hyperlib')) {
    $source = Join-Path $repositoryRoot "src/Hypershell/Assets/MyNet/$name.svg"
    [xml]$svg = [IO.File]::ReadAllText($source)
    if ($svg.DocumentElement.GetAttribute('viewBox') -ne '0 0 64 64') { throw 'Expected a 64-unit SVG.' }
    $frames = [Collections.Generic.List[byte[]]]::new()
    foreach ($size in $sizes) {
        $bitmap = Render-Svg $svg $size
        $pixels = [byte[]]::new($size * $size * 4)
        $bitmap.CopyPixels($pixels, $size * 4, 0)
        foreach ($corner in @(0, ($size - 1), ($size * ($size - 1)), ($size * $size - 1))) {
            if ($pixels[$corner * 4 + 3] -ne 0) { throw 'Icon corners must be transparent.' }
        }
        $bytes = Get-PngBytes $bitmap
        $frames.Add($bytes)
        if ($size -eq 256) { [IO.File]::WriteAllBytes((Join-Path $OutputDirectory "$name.png"), $bytes) }
    }
    $iconPath = Join-Path $OutputDirectory "$name.ico"
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
    }
    finally { $writer.Dispose() }
    $stream = [IO.File]::OpenRead($iconPath)
    try {
        $decoder = [Windows.Media.Imaging.IconBitmapDecoder]::new($stream,
            [Windows.Media.Imaging.BitmapCreateOptions]::PreservePixelFormat,
            [Windows.Media.Imaging.BitmapCacheOption]::OnLoad)
        if ($decoder.Frames.Count -ne $sizes.Count) { throw 'Missing ICO frames.' }
        for ($i = 0; $i -lt $sizes.Count; $i++) {
            if ($decoder.Frames[$i].PixelWidth -ne $sizes[$i] -or $decoder.Frames[$i].PixelHeight -ne $sizes[$i]) {
                throw 'An ICO frame has the wrong dimensions.'
            }
        }
    }
    finally { $stream.Dispose() }
    Write-Output "MYNET_ICON_OK|$name|frames=$($sizes -join ',')|transparent-corners"
}

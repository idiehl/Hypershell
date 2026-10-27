param([string]$OutputDirectory = $PSScriptRoot)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase
$null = New-Item -ItemType Directory -Path $OutputDirectory -Force

# Exact shell/orbit/signal geometry from Controls/AiShellGlyph.cs, in a 24-unit view box.
$shell = 'M 12 3.8 C 7.4 3.8 4.8 7.1 4.8 11.9 C 4.8 16.7 7.5 20.2 12 20.2 C 16.5 20.2 19.2 16.7 19.2 11.9 C 19.2 7.1 16.6 3.8 12 3.8 Z'
$scutes = 'M 12 7.3 L 15.7 9.6 L 15.7 14.2 L 12 16.5 L 8.3 14.2 L 8.3 9.6 Z M 12 3.8 L 12 7.3 M 5.4 8.2 L 8.3 9.6 M 5.4 15.9 L 8.3 14.2 M 12 16.5 L 12 20.2 M 18.6 15.9 L 15.7 14.2 M 18.6 8.2 L 15.7 9.6'
$orbit = 'M 2 15.3 C 0.9 10.6 4.1 4.1 10.6 1.9 M 22 8.7 C 23.1 13.4 19.9 19.9 13.4 22.1'
$signal = 'M 20.1 1.6 L 20.8 3.7 L 22.9 4.4 L 20.8 5.1 L 20.1 7.2 L 19.4 5.1 L 17.3 4.4 L 19.4 3.7 Z'
$cubeOutline = 'M 7.9 11.9 L 9.95 8.35 L 14.05 8.35 L 16.1 11.9 L 14.05 15.45 L 9.95 15.45 Z'
$cubeSeams = 'M 7.9 11.9 L 11.6 11.9 L 14.05 8.35 M 11.6 11.9 L 14.05 15.45'
$cubeScutes = 'M 8.5 4.7 L 9.95 8.35 M 15.5 4.7 L 14.05 8.35 M 19.2 11.9 L 16.1 11.9 M 15.5 19.3 L 14.05 15.45 M 8.5 19.3 L 9.95 15.45 M 4.8 11.9 L 7.9 11.9'
$cubeTop = 'M 7.9 11.9 L 9.95 8.35 L 14.05 8.35 L 11.6 11.9 Z'
$cubeBottom = 'M 7.9 11.9 L 11.6 11.9 L 14.05 15.45 L 9.95 15.45 Z'
$cubeRight = 'M 11.6 11.9 L 14.05 8.35 L 16.1 11.9 L 14.05 15.45 Z'
$active = '#A3C7BD' # App.xaml SecondaryVividBrush; existing checked AI color.
$idle = '#A5B7BE' # MainWindow.xaml AiLiveToggleStyle foreground.

function Layers([bool]$Cube) {
    $result = @(@{ D=$shell; Fill=.12 }, @{ D=$shell; Stroke=1.0; Width=1.3 })
    if ($Cube) {
        $result += @(
            @{ D=$cubeScutes; Stroke=.68; Width=.95 },
            @{ D=$cubeTop; Fill=.62 },
            @{ D=$cubeBottom; Fill=.12 },
            @{ D=$cubeRight; Fill=.32 },
            @{ D=$cubeOutline; Stroke=.92; Width=.95 },
            @{ D=$cubeSeams; Stroke=.92; Width=.9 })
    } else { $result += @{ D=$scutes; Stroke=.68; Width=.95 } }
    $result += @(@{ D=$orbit; Stroke=.68; Width=1.1 }, @{ D=$signal; Fill=1.0 })
    return $result
}
function Brush([string]$Color,[double]$Opacity=1) {
    $brush = [System.Windows.Media.SolidColorBrush]::new([System.Windows.Media.ColorConverter]::ConvertFromString($Color))
    $brush.Opacity=$Opacity
    $brush.Freeze()
    return $brush
}
function Draw-Glyph($Context,[bool]$Cube,[double]$X,[double]$Y,[double]$Size,[string]$Color) {
    $Context.PushTransform([System.Windows.Media.MatrixTransform]::new($Size/24,0,0,$Size/24,$X,$Y))
    foreach($layer in (Layers $Cube)) {
        $fill=$null; $pen=$null
        if($layer.ContainsKey('Fill')) { $fill=Brush $Color $layer.Fill }
        if($layer.ContainsKey('Stroke')) {
            $pen=[System.Windows.Media.Pen]::new((Brush $Color $layer.Stroke),$layer.Width)
            $pen.StartLineCap=$pen.EndLineCap=[System.Windows.Media.PenLineCap]::Round
            $pen.LineJoin=[System.Windows.Media.PenLineJoin]::Round
        }
        $Context.DrawGeometry($fill,$pen,[System.Windows.Media.Geometry]::Parse($layer.D))
    }
    $Context.Pop()
}
function Save-Png($Visual,[int]$Width,[int]$Height,[string]$Path) {
    $bitmap=[System.Windows.Media.Imaging.RenderTargetBitmap]::new($Width,$Height,96,96,[System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($Visual)
    $encoder=[System.Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream=[IO.File]::Create($Path)
    try { $encoder.Save($stream) } finally { $stream.Dispose() }
}
function Draw-Text($Context,[string]$Text,[double]$X,[double]$Y,[double]$Size,[string]$Color,[bool]$Bold=$false) {
    $weight=if($Bold){[System.Windows.FontWeights]::SemiBold}else{[System.Windows.FontWeights]::Normal}
    $face=[System.Windows.Media.Typeface]::new([System.Windows.Media.FontFamily]::new('Segoe UI'),[System.Windows.FontStyles]::Normal,$weight,[System.Windows.FontStretches]::Normal)
    $formatted=[System.Windows.Media.FormattedText]::new($Text,[Globalization.CultureInfo]::InvariantCulture,[System.Windows.FlowDirection]::LeftToRight,$face,$Size,(Brush $Color),1.0)
    $Context.DrawText($formatted,[System.Windows.Point]::new($X,$Y))
}
function Draw-Surface($Context,[double]$X,[double]$Y,[double]$W,[double]$H,[string]$Fill,[string]$Stroke,[double]$Radius) {
    $pen=if($Stroke){[System.Windows.Media.Pen]::new((Brush $Stroke),1)}else{$null}
    $Context.DrawRoundedRectangle((Brush $Fill),$pen,[System.Windows.Rect]::new($X,$Y,$W,$H),$Radius,$Radius)
}

foreach($variant in @(@{Name='ai-shell-current';Cube=$false},@{Name='ai-shell-cube';Cube=$true})) {
    $paths=foreach($layer in (Layers $variant.Cube)) {
        $attrs='fill="none"'
        if($layer.ContainsKey('Fill')) { $attrs='fill="currentColor" fill-opacity="'+$layer.Fill+'"' }
        if($layer.ContainsKey('Stroke')) { $attrs+=' stroke="currentColor" stroke-opacity="'+$layer.Stroke+'" stroke-width="'+$layer.Width+'"' }
        '  <path d="'+$layer.D+'" '+$attrs+'/>'
    }
    $title=if($variant.Cube){'Hypershell AI shell - projected cube center concept'}else{'Hypershell AI shell - current glyph'}
    $svg='<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" width="24" height="24" style="color:'+ $active +'" stroke-linecap="round" stroke-linejoin="round" role="img" aria-labelledby="title">'+"`n  <title>$title</title>`n"+($paths -join "`n")+"`n</svg>`n"
    [IO.File]::WriteAllText((Join-Path $OutputDirectory ($variant.Name+'.svg')),$svg)
    foreach($size in @(20,24,192)) {
        $visual=[System.Windows.Media.DrawingVisual]::new(); $context=$visual.RenderOpen()
        Draw-Glyph $context $variant.Cube 0 0 $size $active
        $context.Close()
        Save-Png $visual $size $size (Join-Path $OutputDirectory ($variant.Name+'-'+$size+'.png'))
    }
}

$board=[System.Windows.Media.DrawingVisual]::new();$context=$board.RenderOpen()
Draw-Surface $context 0 0 1400 880 '#12161C' '' 0
Draw-Text $context 'AI shell / cube center' 52 40 32 '#E6EAF0' $true
Draw-Text $context 'The current round shell and orbital arms, with a projected three-face core.' 54 91 17 '#929AA6'
foreach($column in @(@{X=52;Cube=$false;Title='CURRENT';Subtitle='Original center scute'},@{X=728;Cube=$true;Title='CUBE CONCEPT';Subtitle='Comet-inspired outward projection'})) {
    $x=$column.X
    Draw-Surface $context $x 146 620 590 '#151A20' '#252C35' 18
    Draw-Text $context $column.Title ($x+30) 174 13 '#A3C7BD' $true
    Draw-Text $context $column.Subtitle ($x+30) 204 22 '#E6EAF0' $true
    Draw-Glyph $context $column.Cube ($x+214) 277 192 $active
    Draw-Text $context '192 px' ($x+286) 496 13 '#929AA6'
    Draw-Text $context 'SIDEBAR SCALE / 1:1' ($x+30) 551 11 '#929AA6' $true
    $samples=@(@{Size=20;Color=$idle;Fill='#1C222B';Label='20 / idle'},@{Size=20;Color=$active;Fill='#1B302E';Label='20 / active'},@{Size=24;Color=$idle;Fill='#1C222B';Label='24 / idle'},@{Size=24;Color=$active;Fill='#1B302E';Label='24 / active'})
    for($i=0;$i -lt $samples.Count;$i++) {
        $sample=$samples[$i];$sx=$x+58+$i*144
        Draw-Surface $context $sx 590 31 31 $sample.Fill '#29373E' 7
        Draw-Glyph $context $column.Cube ($sx+(31-$sample.Size)/2) (590+(31-$sample.Size)/2) $sample.Size $sample.Color
        Draw-Text $context $sample.Label ($sx-10) 641 12 '#929AA6'
    }
}
Draw-Text $context 'Mint from the existing AI control' 54 772 15 '#E6EAF0' $true
Draw-Text $context '#A3C7BD active  /  #A5B7BE idle  /  three face tones from one color' 54 801 14 '#929AA6'
Draw-Text $context 'Review concept only' 1136 780 15 '#A3C7BD' $true
$context.Close()
Save-Png $board 1400 880 (Join-Path $OutputDirectory 'ai-shell-cube-comparison.png')
Write-Output ('Created SVGs, transparent 20/24/192px PNGs, and comparison board: '+$OutputDirectory)

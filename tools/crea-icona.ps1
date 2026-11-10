# Crea l'icona dell'app (src\Anonimizzatore.App\Icona\anonimizzatore.ico): lo scudo con la spunta (Lucide "shield-check",
# lo stesso della barra laterale) in bianco su un quadrato blu arrotondato. Il file .ico sta nella repo: questo script serve
# solo se si vuole cambiare il disegno o il colore.
# Uso:   powershell -STA -File tools\crea-icona.ps1

$ErrorActionPreference = 'Stop'
if ([Threading.Thread]::CurrentThread.GetApartmentState() -ne 'STA') { throw 'Serve -STA: powershell -STA -File tools\crea-icona.ps1' }
Add-Type -AssemblyName PresentationCore, WindowsBase

$radice = Split-Path -Parent $PSScriptRoot
$destinazione = Join-Path $radice 'src\Anonimizzatore.App\Icona'
New-Item -ItemType Directory -Force $destinazione | Out-Null

$colore = [Windows.Media.ColorConverter]::ConvertFromString('#1d5bd6')   # accento di UI Top
$scudo = [Windows.Media.Geometry]::Parse('M20 13c0 5-3.5 7.5-7.66 8.95a1 1 0 0 1-.67-.01C7.5 20.5 4 18 4 13V6a1 1 0 0 1 1-1c2 0 4.5-1.2 6.24-2.72a1.17 1.17 0 0 1 1.52 0C14.51 3.81 17 5 19 5a1 1 0 0 1 1 1z')
$spunta = [Windows.Media.Geometry]::Parse('M9 12 L11 14 L15 10')

function Disegna([int]$lato) {
    $visual = New-Object Windows.Media.DrawingVisual
    $dc = $visual.RenderOpen()
    $sfondo = New-Object Windows.Media.SolidColorBrush $colore
    $raggio = $lato * 0.22
    $dc.DrawRoundedRectangle($sfondo, $null, (New-Object Windows.Rect 0, 0, $lato, $lato), $raggio, $raggio)

    # Lo scudo occupa ~70% del quadrato (nella griglia 24x24 di Lucide va da 4 a 20 in larghezza, da 2 a 22 in altezza).
    $scala = $lato * 0.70 / 20.0
    $tratto = New-Object Windows.Media.Pen ([Windows.Media.Brushes]::White), (2.0 * $scala)
    $tratto.StartLineCap = 'Round'; $tratto.EndLineCap = 'Round'; $tratto.LineJoin = 'Round'
    $trasf = New-Object Windows.Media.TransformGroup
    $trasf.Children.Add((New-Object Windows.Media.ScaleTransform $scala, $scala))
    $trasf.Children.Add((New-Object Windows.Media.TranslateTransform ($lato / 2 - 12 * $scala), ($lato / 2 - 12 * $scala)))
    $dc.PushTransform($trasf)
    $tratto.Thickness = 2.0
    $dc.DrawGeometry($null, $tratto, $scudo)
    $dc.DrawGeometry($null, $tratto, $spunta)
    $dc.Pop()
    $dc.Close()

    $bmp = New-Object Windows.Media.Imaging.RenderTargetBitmap $lato, $lato, 96, 96, ([Windows.Media.PixelFormats]::Pbgra32)
    $bmp.Render($visual)
    $enc = New-Object Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bmp))
    $ms = New-Object IO.MemoryStream
    $enc.Save($ms)
    return , $ms.ToArray()
}

$misure = 16, 24, 32, 48, 64, 128, 256
$png = @{}
foreach ($m in $misure) { $png[$m] = Disegna $m }

# Il file .ico: intestazione, una voce per misura, poi i PNG.
$out = New-Object IO.MemoryStream
$w = New-Object IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$misure.Count)
$offset = 6 + 16 * $misure.Count
foreach ($m in $misure) {
    $dim = if ($m -ge 256) { 0 } else { $m }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$png[$m].Length); $w.Write([uint32]$offset)
    $offset += $png[$m].Length
}
foreach ($m in $misure) { $w.Write($png[$m]) }
$w.Flush()

$ico = Join-Path $destinazione 'anonimizzatore.ico'
[IO.File]::WriteAllBytes($ico, $out.ToArray())
"{0}  ({1:N0} KB)" -f $ico, ((Get-Item $ico).Length / 1KB)

# Crea l'icona dell'app (src\Anonimizzatore.App\Icona\anonimizzatore.ico): scudo azzurro pieno con spunta bianca su un quadrato
# blu scuro arrotondato. Il file .ico sta nella repo: questo script serve solo se si vuole cambiare il disegno o i colori.
# Uso:   powershell -STA -File tools\crea-icona.ps1
#
# Il disegno è fatto su una griglia di 16 unità, con forme piene e spesse: così resta nitido anche a 16-32 px (le misure della
# barra delle applicazioni e della barra del titolo), dove il tratto sottile dello scudo a contorno si sgranava.

$ErrorActionPreference = 'Stop'
if ([Threading.Thread]::CurrentThread.GetApartmentState() -ne 'STA') { throw 'Serve -STA: powershell -STA -File tools\crea-icona.ps1' }
Add-Type -AssemblyName PresentationCore, WindowsBase

$radice = Split-Path -Parent $PSScriptRoot
$destinazione = Join-Path $radice 'src\Anonimizzatore.App\Icona'
New-Item -ItemType Directory -Force $destinazione | Out-Null

function Colore($esadecimale) { New-Object Windows.Media.SolidColorBrush ([Windows.Media.ColorConverter]::ConvertFromString($esadecimale)) }
$fondo = Colore '#0f2f78'     # blu scuro
$scudoColore = Colore '#5b92ff'   # azzurro
$bianco = [Windows.Media.Brushes]::White

$scudo = [Windows.Media.Geometry]::Parse('M8,2 L13,4 L13,8 C13,11 10.5,13 8,14 C5.5,13 3,11 3,8 L3,4 Z')
$spunta = [Windows.Media.Geometry]::Parse('M5.3,8 L7.3,10 L10.8,6')
$pennello = New-Object Windows.Media.Pen $bianco, 1.9
$pennello.StartLineCap = 'Round'; $pennello.EndLineCap = 'Round'; $pennello.LineJoin = 'Round'

function Disegna([int]$lato) {
    $visual = New-Object Windows.Media.DrawingVisual
    $dc = $visual.RenderOpen()
    $dc.PushTransform((New-Object Windows.Media.ScaleTransform ($lato / 16.0), ($lato / 16.0)))
    $dc.DrawRoundedRectangle($fondo, $null, (New-Object Windows.Rect 0, 0, 16, 16), 3.5, 3.5)
    $dc.DrawGeometry($scudoColore, $null, $scudo)
    $dc.DrawGeometry($null, $pennello, $spunta)
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

$misure = 16, 20, 24, 28, 32, 40, 48, 64, 96, 128, 256
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

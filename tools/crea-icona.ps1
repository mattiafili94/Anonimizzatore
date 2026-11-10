# Crea l'icona dell'app (src\Anonimizzatore.App\Icona\anonimizzatore.ico): lo scudo con la spunta (Lucide "shield-check",
# lo stesso della barra laterale) in bianco su un quadrato blu arrotondato. Il file .ico sta nella repo: questo script serve
# solo se si vuole cambiare il disegno o il colore.
# Uso:   powershell -STA -File tools\crea-icona.ps1  [-Anteprima percorso.png]
#   -Anteprima: salva anche una tavola con le misure piccole ingrandite, per controllarle a occhio.
#
# Le misure piccole (16-32 px, quelle della barra delle applicazioni e della barra del titolo) hanno scudo e tratto più grandi:
# con il tratto sottile del disegno originale sfocano. Ogni misura è disegnata a 8 volte e poi rimpicciolita con filtro di qualità.

param([string]$Anteprima = '')

$ErrorActionPreference = 'Stop'
if ([Threading.Thread]::CurrentThread.GetApartmentState() -ne 'STA') { throw 'Serve -STA: powershell -STA -File tools\crea-icona.ps1' }
Add-Type -AssemblyName PresentationCore, WindowsBase

$radice = Split-Path -Parent $PSScriptRoot
$destinazione = Join-Path $radice 'src\Anonimizzatore.App\Icona'
New-Item -ItemType Directory -Force $destinazione | Out-Null

$colore = [Windows.Media.ColorConverter]::ConvertFromString('#1d5bd6')   # accento di UI Top
$scudo = [Windows.Media.Geometry]::Parse('M20 13c0 5-3.5 7.5-7.66 8.95a1 1 0 0 1-.67-.01C7.5 20.5 4 18 4 13V6a1 1 0 0 1 1-1c2 0 4.5-1.2 6.24-2.72a1.17 1.17 0 0 1 1.52 0C14.51 3.81 17 5 19 5a1 1 0 0 1 1 1z')
$spunta = [Windows.Media.Geometry]::Parse('M9 12 L11 14 L15 10')

# Disegna a una scala alta ($lato px) e restituisce la bitmap.
function Disegna([int]$lato, [double]$frazioneScudo, [double]$spessore, [double]$fondo) {
    $visual = New-Object Windows.Media.DrawingVisual
    $dc = $visual.RenderOpen()
    $raggio = $lato * 0.22
    $dc.DrawRoundedRectangle((New-Object Windows.Media.SolidColorBrush $colore), $null, (New-Object Windows.Rect 0, 0, $lato, $lato), $raggio, $raggio)

    # Lo scudo Lucide è largo 16 e alto 20 (nella griglia 24x24): la frazione dice quanto del quadrato occupa in altezza.
    $scala = $lato * $frazioneScudo / 20.0
    $tratto = New-Object Windows.Media.Pen ([Windows.Media.Brushes]::White), $spessore
    $tratto.StartLineCap = 'Round'; $tratto.EndLineCap = 'Round'; $tratto.LineJoin = 'Round'
    $trasf = New-Object Windows.Media.TransformGroup
    $trasf.Children.Add((New-Object Windows.Media.ScaleTransform $scala, $scala))
    $trasf.Children.Add((New-Object Windows.Media.TranslateTransform ($lato / 2 - 12 * $scala), ($lato / 2 - 12 * $scala + $fondo * $scala)))
    $dc.PushTransform($trasf)
    $dc.DrawGeometry($null, $tratto, $scudo)
    $dc.DrawGeometry($null, $tratto, $spunta)
    $dc.Pop()
    $dc.Close()

    $bmp = New-Object Windows.Media.Imaging.RenderTargetBitmap $lato, $lato, 96, 96, ([Windows.Media.PixelFormats]::Pbgra32)
    $bmp.Render($visual)
    return $bmp
}

# Una misura finale: si disegna a 8x e si rimpicciolisce con il filtro di qualità migliore.
function Misura([int]$m) {
    if ($m -le 20) { $frazione = 0.76; $spessore = 2.9 }
    elseif ($m -le 24) { $frazione = 0.80; $spessore = 2.7 }
    elseif ($m -le 32) { $frazione = 0.76; $spessore = 2.4 }
    elseif ($m -le 64) { $frazione = 0.72; $spessore = 2.2 }
    else { $frazione = 0.70; $spessore = 2.0 }

    $grande = Disegna ($m * 8) $frazione $spessore 0
    # Il pennello è in unità del disegno (griglia 24): è già moltiplicato dalla scala dentro Disegna.
    $visual = New-Object Windows.Media.DrawingVisual
    [Windows.Media.RenderOptions]::SetBitmapScalingMode($visual, [Windows.Media.BitmapScalingMode]::Fant)
    $dc = $visual.RenderOpen()
    $dc.DrawImage($grande, (New-Object Windows.Rect 0, 0, $m, $m))
    $dc.Close()
    $piccola = New-Object Windows.Media.Imaging.RenderTargetBitmap $m, $m, 96, 96, ([Windows.Media.PixelFormats]::Pbgra32)
    $piccola.Render($visual)
    return $piccola
}

function InPng($bmp) {
    $enc = New-Object Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bmp))
    $ms = New-Object IO.MemoryStream
    $enc.Save($ms)
    return , $ms.ToArray()
}

$misure = 16, 20, 24, 28, 32, 40, 48, 64, 96, 128, 256
$bitmap = @{}
$png = @{}
foreach ($m in $misure) { $bitmap[$m] = Misura $m; $png[$m] = InPng $bitmap[$m] }

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

if ($Anteprima) {
    # Tavola di controllo: 16, 20, 24, 32, 48 ingrandite 8 volte senza sfumare i pixel, su fondo chiaro.
    $mostra = 16, 20, 24, 32, 48
    $altezza = 48 * 8 + 32
    $larghezza = ($mostra | ForEach-Object { $_ * 8 + 16 } | Measure-Object -Sum).Sum + 16
    $visual = New-Object Windows.Media.DrawingVisual
    $dc = $visual.RenderOpen()
    $dc.DrawRectangle([Windows.Media.Brushes]::WhiteSmoke, $null, (New-Object Windows.Rect 0, 0, $larghezza, $altezza))
    $x = 16
    foreach ($m in $mostra) {
        $img = $bitmap[$m]
        $vis2 = New-Object Windows.Media.DrawingVisual
        [Windows.Media.RenderOptions]::SetBitmapScalingMode($vis2, [Windows.Media.BitmapScalingMode]::NearestNeighbor)
        $d2 = $vis2.RenderOpen(); $d2.DrawImage($img, (New-Object Windows.Rect 0, 0, ($m * 8), ($m * 8))); $d2.Close()
        $tmp = New-Object Windows.Media.Imaging.RenderTargetBitmap ($m * 8), ($m * 8), 96, 96, ([Windows.Media.PixelFormats]::Pbgra32)
        $tmp.Render($vis2)
        $dc.DrawImage($tmp, (New-Object Windows.Rect $x, 16, ($m * 8), ($m * 8)))
        $x += $m * 8 + 16
    }
    $dc.Close()
    $tavola = New-Object Windows.Media.Imaging.RenderTargetBitmap ([int]$larghezza), $altezza, 96, 96, ([Windows.Media.PixelFormats]::Pbgra32)
    $tavola.Render($visual)
    [IO.File]::WriteAllBytes($Anteprima, (InPng $tavola))
}

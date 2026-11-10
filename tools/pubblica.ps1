# Prepara la cartella portabile da dare ai colleghi: un file zip con l'app e il runtime .NET dentro.
# Uso (dalla cartella principale del progetto):   powershell -File tools\pubblica.ps1
# Risultato: artifacts\Anonimizzatore-<versione>.zip   (la cartella artifacts non entra in git)

$ErrorActionPreference = 'Stop'
$radice = Split-Path -Parent $PSScriptRoot
Set-Location $radice

$csproj = 'src\Anonimizzatore.App\Anonimizzatore.App.csproj'
$versione = ([xml](Get-Content $csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $versione) { throw "Non trovo <Version> in $csproj" }

$cartella = Join-Path $radice 'artifacts\portabile\Anonimizzatore'
if (Test-Path (Join-Path $radice 'artifacts\portabile')) { Remove-Item (Join-Path $radice 'artifacts\portabile') -Recurse -Force }

dotnet publish $csproj -c Release -r win-x64 --self-contained true -o $cartella -nologo
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish è fallito' }

Copy-Item (Join-Path $PSScriptRoot 'LEGGIMI.txt') (Join-Path $cartella 'LEGGIMI.txt')

$zip = Join-Path $radice "artifacts\Anonimizzatore-$versione.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path $cartella -DestinationPath $zip

"{0}  ({1:N0} MB)" -f $zip, ((Get-Item $zip).Length / 1MB)

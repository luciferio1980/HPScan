# Publica el paquete Windows autocontenido (ejecutar en Windows).
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root "artifacts\win-x64"
dotnet publish (Join-Path $root "src\CanonScanStudio.App\CanonScanStudio.App.csproj") `
    -c Release -r win-x64 --self-contained true `
    -p:PublishReadyToRun=true -p:DebugType=embedded `
    -o $out
New-Item -ItemType Directory -Force -Path (Join-Path $out "tessdata") | Out-Null
if (Test-Path (Join-Path $root "tessdata\*.traineddata")) {
    Copy-Item (Join-Path $root "tessdata\*.traineddata") (Join-Path $out "tessdata") -Force
}
Copy-Item (Join-Path $root "dist\LEEME.txt") (Join-Path $out "LEEME.txt") -Force
Write-Host "Publicado en $out"
Write-Host "Compila installer/CanonScanStudio.iss con Inno Setup para obtener CanonScanStudio-Setup.exe"

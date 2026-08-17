# Publica la app y recuerda cómo generar el instalador (Windows).
param(
    [switch]$SelfContained
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root "artifacts\win-x64"
$self = $SelfContained.IsPresent
dotnet publish (Join-Path $root "src\CanonScanStudio.App\CanonScanStudio.App.csproj") `
    -c Release -r win-x64 --self-contained $self -o $out
Write-Host "Publicado en $out"
Write-Host "Compila installer/CanonScanStudio.iss con Inno Setup para obtener CanonScanStudio-Setup.exe"

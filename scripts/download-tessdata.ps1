$ErrorActionPreference = "Stop"
$dest = Join-Path $PSScriptRoot "..\tessdata"
New-Item -ItemType Directory -Force -Path $dest | Out-Null
$langs = @("spa", "eng", "fra", "deu", "ita", "por")
foreach ($lang in $langs) {
    $url = "https://github.com/tesseract-ocr/tessdata_fast/raw/main/$lang.traineddata"
    $file = Join-Path $dest "$lang.traineddata"
    Write-Host "Descargando $lang..."
    Invoke-WebRequest -Uri $url -OutFile $file
}
Write-Host "Listo: $dest"

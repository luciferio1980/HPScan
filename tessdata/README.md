Coloca aquí los archivos Tesseract `*.traineddata` (por ejemplo spa.traineddata y eng.traineddata).

Canon Scan Studio busca en esta carpeta junto al ejecutable y en %LocalAppData%\CanonScanStudio\tessdata.

Sin estos archivos el escaneo funciona; solo el PDF con texto seleccionable queda desactivado.

Usa scripts/download-tessdata.ps1 en Windows para descargar tessdata_fast.

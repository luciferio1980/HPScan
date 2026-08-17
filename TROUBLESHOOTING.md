# Solución de problemas — Canon Scan Studio y PIXMA TS5151

## El programa no se abre / se cierra al instante

1. Usa Windows **10 u 11 de 64 bits**. No funciona en 32 bits ni en macOS/Linux.
2. Si Windows muestra *Windows protegió tu PC* (SmartScreen): **Más información** → **Ejecutar de todas formas**. El ejecutable no está firmado.
3. Si descargaste el ZIP portable, descomprímelo completo (no ejecutes desde dentro del ZIP) y abre `CanonScanStudio.exe`.
4. Prueba el instalador (`CanonScanStudio-Setup.exe`) o, al contrario, la versión portable.
5. Si aparece un cuadro de error, anota el mensaje. El detalle se guarda en `%LocalAppData%\CanonScanStudio\logs` (archivos `crash-*.log`).
6. Desactiva temporalmente el antivirus si bloquea el `.exe` recién descargado.

## El programa no detecta el escáner / la impresora

Windows puede imprimir y aun así **no publicar el escáner**. Canon Scan Studio necesita el dispositivo de **escaneo** (WIA, TWAIN o Windows Scan), no solo la cola de impresión.

Driver oficial del PIXMA TS5151 (elige **MP Drivers** de la serie TS5100, no un paquete solo de impresora):

https://www.canon.es/support/consumer/products/printers/pixma/ts-series/pixma-ts5151.html?type=drivers&detailId=tcm:86-1604954&productTcmUri=tcm:86-1604881

1. La impresora debe estar **encendida**.
2. USB bien conectado, o el PC en la **misma red Wi-Fi**.
3. Instala el **MP Driver** de esa página (instalador completo de Canon). Esta app no lo incluye ni puede detectarlo sin él.
4. **Wi-Fi:** después del driver, abre **IJ Network Scanner Selector EX** (viene con el MP Driver), marca `Canon TS5100 series` y pulsa **OK**. Sin este paso Windows suele ver la impresora y no el escáner.
5. En Windows: *Configuración → Bluetooth e dispositivos → Impresoras y escáneres*. Debe verse un **escáner**, a menudo `Canon TS5100 series` o `TS5100 series_<MAC>`, no exactamente TS5151.
6. En Canon Scan Studio: **Elegir escáner de Windows**, o **F5** / *Actualizar dispositivos*.
7. Cierra IJ Scan Utility, Fax y Escáner u otra app que tenga abierto el dispositivo.
8. *Ajustes → Más* (diagnóstico): ahí se listan los dispositivos que Windows sí ve y si el MP Driver parece instalado.

## No se puede acceder al escáner

Causas habituales de WIA:

- El dispositivo está ocupado (`busy` / `locked`).
- Tapa abierta.
- Pérdida de comunicación USB/Wi-Fi.
- Controlador inestable tras un fallo anterior: apaga y enciende el Canon, espera 10 s.

Pulsa **Reintentar** (vuelve a buscar dispositivos). Las páginas ya escaneadas **no se pierden**.

## El escaneo por Wi-Fi no permite brillo o 75 dpi

Es una limitación del **controlador WIA de Canon en red**, no de esta aplicación. En red Canon documenta 150/300/600 dpi y no expone brillo/contraste. La app aplica exposición después, sobre la imagen recibida.

## 1200 DPI no aparece

El WIA de TS5100 suele topar en 600 dpi aunque el CIS óptico sea 1200×2400. Si el driver no publica 1200, no se ofrece. ScanGear/TWAIN a veces permite más: cambia la interfaz a TWAIN en Configuración si tienes ScanGear instalado.

## TWAIN no lista el dispositivo

- Falta `TWAINDSM.dll` o el origen ScanGear.
- Reinstala el MP Driver.
- Algunas fuentes TWAIN antiguas son de 32 bits; esta app es 64 bits y usa el DSM 64 bits cuando existe. Si TWAIN falla, usa WIA (recomendado).

## La imagen sale negra o en blanco

- Cierra la tapa.
- Coloca el original hacia el cristal, alineado con la marca.
- Prueba Color 300 dpi A4.
- En Diagnóstico, pulsa *Realizar escaneo de prueba*.

## OCR no funciona

El escaneo no depende de Internet ni de OCR. Si falta Tesseract/`tessdata`, el PDF de imagen sigue siendo válido. Ejecuta `scripts/download-tessdata.ps1` o copia `spa.traineddata` y `eng.traineddata`.

## Dónde están los registros

`%LocalAppData%\CanonScanStudio\logs`

Configuración → Diagnóstico → **Exportar registro**. No se muestran códigos del tipo `COMException 0x80210015` en la interfaz.

## La aplicación se cerró y perdí el trabajo

Si estaba activa la recuperación, al reabrir (con *Abrir automáticamente la última sesión*) se restauran las páginas originales de `%LocalAppData%\CanonScanStudio\recovery`.

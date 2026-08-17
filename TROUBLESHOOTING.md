# Solución de problemas — Canon Scan Studio y PIXMA TS5151

## El programa no se abre / se cierra al instante

1. Usa Windows **10 u 11 de 64 bits**. No funciona en 32 bits ni en macOS/Linux.
2. Si Windows muestra *Windows protegió tu PC* (SmartScreen): **Más información** → **Ejecutar de todas formas**. El ejecutable no está firmado.
3. Si descargaste el ZIP portable, descomprímelo completo (no ejecutes desde dentro del ZIP) y abre `CanonScanStudio.exe`.
4. Prueba el instalador (`CanonScanStudio-Setup.exe`) o, al contrario, la versión portable.
5. Si aparece un cuadro de error, anota el mensaje. El detalle se guarda en `%LocalAppData%\CanonScanStudio\logs` (archivos `crash-*.log`).
6. Desactiva temporalmente el antivirus si bloquea el `.exe` recién descargado.

## El programa no detecta el escáner / la impresora

Si el encabezado dice **Ningún escáner · Sin dispositivo · No disponible**, la app **no** tiene un dispositivo de escaneo. El Selector EX2 puede ver el TS5100 (MAC) y aun así Windows WIA no publicarlo.

1. En el Selector EX2 marca el TS5100 y pulsa **Aceptar** (no dejes el cuadro abierto).
2. En Canon Scan Studio pulsa **Reintentar**. A partir de 1.0.5 se busca también por **red eSCL** usando la IP de la impresora o la tabla ARP (p. ej. MAC `6C:F2:D8:…`).
3. El dispositivo puede aparecer como `Canon TS5100 series (Wi-Fi)`, no como TS5151.
4. Cierra IJ Scan Utility si está abierto.

Driver oficial: https://www.canon.es/support/consumer/products/printers/pixma/ts-series/pixma-ts5151.html?type=drivers

Windows puede imprimir y aun así no publicar WIA. Esta app necesita WIA, TWAIN, Windows Scan **o** eSCL.

## No se puede acceder al escáner

Causas habituales de WIA:

- El dispositivo está ocupado (`busy` / `locked`).
- Tapa abierta.
- Pérdida de comunicación USB/Wi-Fi.
- Controlador inestable tras un fallo anterior: apaga y enciende el Canon, espera 10 s.

Pulsa **Reintentar** (vuelve a buscar dispositivos). Las páginas ya escaneadas **no se pierden**.

## El escaneo por Wi-Fi no permite brillo o 75 dpi

Es una limitación del **controlador WIA de Canon en red**, no de esta aplicación. En red Canon documenta 150/300/600 dpi y no expone brillo/contraste. La app aplica exposición después, sobre la imagen recibida.

## 1200 DPI

El CIS óptico del TS5151 es 1200×2400, pero **la lista de la app solo muestra lo que anuncia esa conexión**.

- **Wi-Fi (eSCL):** el TS5100 suele publicar como máximo **600 DPI**. Elegir 1200 fallaba; ya no se ofrece si el escáner no lo lista.
- **USB / WIA:** 1200 aparece si el MP Driver lo declara.

Tras escanear, la etiqueta de la página muestra píxeles y DPI reales de la imagen (no el valor pedido si el aparato entregó otro).

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

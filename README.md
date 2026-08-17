# Canon Scan Studio

Aplicación de escritorio para Windows 10/11 que escanea, previsualiza, edita, organiza y guarda documentos usando un **Canon PIXMA TS5151** (serie TS5100).

## Descargar y usar

Paquete listo para Windows 10/11 **64 bits**. No hace falta instalar .NET:

**https://github.com/luciferio1980/HPScan/releases/latest**

Descarga la versión **1.0.7 o superior**:

1. **CanonScanStudio-Setup.exe** — instalador con acceso directo
2. **CanonScanStudio-Portable-win-x64.zip** — descomprime la carpeta y abre `CanonScanStudio.exe`

La primera vez Windows puede mostrar SmartScreen: *Más información* → *Ejecutar de todas formas*.

Si el programa no se abre, consulta [TROUBLESHOOTING.md](TROUBLESHOOTING.md).

Sigue haciendo falta el **MP Driver oficial de Canon** (serie TS5100) para que el escáner se vea en Windows. Esta aplicación no incluye controladores de Canon.

No copia software de HP ni usa APIs privadas de Canon. El escaneo real se hace con las interfaces estándar de Windows:

1. **WIA** (Windows Image Acquisition) — backend principal
2. **TWAIN 1.9** (ScanGear del MP Driver) — alternativa si WIA no publica el dispositivo

## Requisitos

- Windows 10 64 bits o Windows 11 64 bits
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (incluido si publicas self-contained)
- Canon PIXMA TS5151 encendido
- **MP Driver oficial de Canon** para la serie TS5100 (WIA y/o ScanGear)

Esta aplicación **no instala ni redistribuye** controladores de Canon.

## Cómo instalar el controlador de Canon

1. Abre la página oficial del PIXMA TS5151 y descarga el **MP Driver** (serie TS5100):
   https://www.canon.es/support/consumer/products/printers/pixma/ts-series/pixma-ts5151.html?type=drivers&detailId=tcm:86-1604954&productTcmUri=tcm:86-1604881
2. Instálalo con la impresora encendida. No basta con añadir solo la impresora en Windows.
3. **Wi-Fi:** abre **IJ Network Scanner Selector EX**, marca el TS5100 y pulsa OK.
4. En Windows, el escáner suele aparecer como:
   - USB: `Canon TS5100 series`
   - Red: `TS5100 series_<MAC>`

No hace falta que el nombre sea exactamente `Canon PIXMA TS5151`. Driver y Selector de red están en **Configuración**.

## Cómo conectar el TS5151

- **USB:** cable a un puerto USB del PC, impresora encendida.
- **Wi-Fi:** el Canon debe estar en la misma red. Windows tiene que verlo como dispositivo de escaneo WIA. No se implementa el protocolo propietario de Canon.

Comprueba que *Fax y Escáner de Windows* o el propio Canon Scan Studio detectan el dispositivo. Si otra aplicación (IJ Scan Utility) tiene el escáner abierto, ciérrala.

## Ejecutar

En un PC Windows con el SDK o el runtime:

```powershell
dotnet restore
dotnet run --project src/CanonScanStudio.App/CanonScanStudio.App.csproj -c Release
```

## Compilar

```powershell
dotnet build CanonScanStudio.sln -c Release
dotnet test tests/CanonScanStudio.Tests/CanonScanStudio.Tests.csproj
```

La biblioteca `CanonScanStudio.Core` (WIA/TWAIN, PDF, edición) se puede compilar también en Linux para tests. La interfaz WPF (`CanonScanStudio.App`) **solo se compila en Windows**.

## Publicar

Framework-dependent:

```powershell
dotnet publish src/CanonScanStudio.App/CanonScanStudio.App.csproj -c Release -r win-x64 --self-contained false -o artifacts/win-x64
```

Autocontenida (no requiere runtime .NET instalado):

```powershell
dotnet publish src/CanonScanStudio.App/CanonScanStudio.App.csproj -c Release -r win-x64 --self-contained true -o artifacts/win-x64
```

## Crear el instalador

1. Publica a `artifacts/win-x64` (paso anterior).
2. Instala [Inno Setup 6](https://jrsoftware.org/isinfo.php).
3. Compila `installer/CanonScanStudio.iss`.
4. El resultado es `installer/Output/CanonScanStudio-Setup.exe`.

El instalador crea acceso directo y permite desinstalar. No toca controladores de Canon.

## OCR

El OCR es opcional y local (Tesseract). Coloca los `*.traineddata` en `tessdata/` junto al ejecutable o en `%LocalAppData%\CanonScanStudio\tessdata`.

```powershell
./scripts/download-tessdata.ps1
```

Sin estos archivos el escaneo y el PDF de imagen siguen funcionando. El PDF con texto seleccionable requiere OCR.

## Arquitectura

Ver [ARCHITECTURE.md](ARCHITECTURE.md).

## Solución de problemas

Ver [TROUBLESHOOTING.md](TROUBLESHOOTING.md).

## Criterio de uso

1. Encender el TS5151.
2. Abrir Canon Scan Studio.
3. El programa detecta `Canon TS5100 series` (o similar).
4. A4, resolución de la lista (en Wi-Fi suele ser hasta 600 DPI), Color.
5. **Escanear** — el hardware realiza el escaneo (no hay simulación).
6. Recortar, girar, brillo/contraste.
7. Escanear otra página, reordenar miniaturas.
8. **Guardar** → PDF de varias páginas.

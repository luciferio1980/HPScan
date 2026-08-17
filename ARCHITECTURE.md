# Arquitectura de Canon Scan Studio

## Decisión de plataforma

- Sistema objetivo: **Windows 10/11 x64**.
- UI: **WPF + .NET 8**, MVVM (CommunityToolkit.Mvvm).
- WPF se eligió frente a WinUI 3 porque la integración con WIA (COM STA) es directa, estable y suficiente para un programa clásico de escaneo.

## Cómo se habla con el PIXMA TS5151

El TS5151 es un multifunción de platina CIS (serie TS5100). Canon documenta dos interfaces estándar en Windows:

| Interfaz | Origen | Nombre típico | Uso en esta app |
| --- | --- | --- | --- |
| WIA 2.0 | `wiaaut.dll` / DeviceManager | USB: `Canon TS5100 series`. Red: `TS5100 series_<MAC>` | Backend por defecto |
| TWAIN 1.9 | ScanGear del MP Driver + DSM del sistema | Nombre del origen TWAIN | Alternativa |

No existe SDK privado de Canon en este proyecto. No se envían comandos USB/Wi-Fi propios.

El controlador WIA de la serie TS5100:

- Resuelve resoluciones **50–600 dpi** por USB; en red suele limitar a **150 / 300 / 600**.
- **No publica tamaño de papel** como perfil: el área se fija con `XPOS/YPOS/XEXTENT/YEXTENT`.
- En red **no expone brillo/contraste**. La app aplica exposición por software (ImageSharp) y lo indica en la UI.
- La platina es la única fuente real. No se inventa un ADF.

La resolución óptica del hardware es 1200×2400. **La UI solo ofrece un DPI si el backend lo anuncia** (eSCL/WIA/TWAIN). En Wi-Fi eSCL el TS5100 suele listar como máximo 600; 1200 aparece en USB cuando el MP Driver lo declara.

## Capas

```
UI (WPF Views / ViewModels)
        ↓
Application services (sesión, undo, guardado, OCR)
        ↓
ScannerService
        ↓
IScannerBackend  →  WiaScannerBackend | TwainScannerBackend
        ↓
WIA COM  /  TWAIN DSM
        ↓
Canon PIXMA TS5151 (MP Driver)
```

`ScannerService` elige backend según Configuración (Automático / WIA / TWAIN). En automático se enumeran ambos y se puntúa el dispositivo (prioridad familia TS5100 + WIA).

## Proyectos

- `CanonScanStudio.Core` — modelos, WIA, TWAIN, ImageSharp, PDF, OCR, sesión, logs.
- `CanonScanStudio.App` — WPF, solo Windows.
- `CanonScanStudio.Tests` — pruebas con `MockScannerBackend` (solo tests; la app real nunca lo usa).

## Imágenes y sesiones

Cada página guarda:

- original PNG en `%LocalAppData%\CanonScanStudio\sessions\...` (no se destruye al editar)
- `PageEditState` (recorte, rotación, exposición, enderezado, filtros)

La vista previa usa una resolución reducida (máx. ~1600–2400 px). La exportación parte del original + ediciones.

Hay recuperación en `%LocalAppData%\CanonScanStudio\recovery\session.json`.

## PDF y OCR

- PDF: QuestPDF, una página por hoja escaneada, tamaño según DPI real.
- OCR: Tesseract local. PDF buscable = imagen + capa de texto invisible.
- Importación PDF: PdfPig extrae imágenes incrustadas (el caso típico de documentos escaneados).

## Hilos

WIA se ejecuta en un hilo STA dedicado (`WiaStaDispatcher`). La UI nunca llama a COM WIA en el hilo de WPF.

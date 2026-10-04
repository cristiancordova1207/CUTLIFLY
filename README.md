<p align="center"><img src="assets/logo.png" width="96" alt="CUTLIFY"></p>

# CUTLIFY

**Capture. Record. Share.** — Captura y grabación de pantalla para Windows 11 (compatible con Windows 10).

Atajo → seleccionas una zona → se copia sola → **Ctrl + V** donde quieras. Sin abrir carpetas, sin guardar a mano y sin llenar el disco.

CUTLIFY es **gratuito** para el usuario, **sin cuenta, sin publicidad, sin telemetría y sin nube**. Su código fuente es **privado**: solo se distribuyen los binarios oficiales.

## Características

- **Capturas**: rectángulo (con proporciones 1:1, 4:3, 16:9, 16:10, 21:9, mover con Espacio y ajuste previo opcional), forma libre (PNG con transparencia), ventana, monitor y todas las pantallas. Tamaño en vivo y lupa (Ctrl). Esc cancela.
- **Selector de color** en pantalla: HEX y RGB al portapapeles.
- **Portapapeles automático** con la imagen real (DIB + PNG) y **modo solo portapapeles**.
- **Retraso** de 1, 2, 3, 5 o 10 s con cuenta regresiva visual. **Repetir captura** (Alt + N).
- **Editor**: cursor, lápiz, resaltador, borrador, línea, flecha, rectángulo, elipse, texto, recorte, deshacer/rehacer, color + colores recientes, grosor, opacidad, copiar, guardar y guardar como.
- **OCR local** («Extraer texto») con el motor de Windows; sin servicios externos.
- **Fijar** capturas encima de todo (mover, redimensionar, cerrar).
- **Grabación** de región, ventana, monitor o todas las pantallas a **MP4** con **H.264, HEVC o AV1** (solo los codecs disponibles en tu equipo), resolución hasta 4K, 30–240 FPS (limitados a la frecuencia real de tus monitores), bitrate automático o manual, aceleración por hardware, cursor y resaltado de clics.
- **Audio**: sistema, micrófono o ambos **mezclados en una pista AAC sincronizada**, con selección de dispositivo, prueba y medidor de nivel. Consentimiento propio antes de usar el micrófono.
- **FPS honestos**: nunca se duplican fotogramas; las estadísticas muestran FPS reales y fotogramas perdidos.
- **Temporales con caducidad** (5 min – 3 días / nunca) que funcionan aunque la app estuviera cerrada.
- **Historial visual**: miniaturas independientes; una captura eliminada o caducada sigue visible como «Eliminada» / «Expirada». Filtros (todos, imágenes, vídeos, disponibles, eliminados, expirados) y búsqueda.
- Bandeja del sistema, pausa temporal de atajos, inicio con Windows (opcional), notificaciones, tema claro/oscuro/sistema, multimonitor y DPI por monitor.

## Carpetas

| Qué | Dónde |
|---|---|
| Capturas guardadas | `Documentos\CUTLIFY\Capturas` (configurable) |
| Grabaciones guardadas | `Documentos\CUTLIFY\Grabaciones` (configurable) |
| Temporales | `%LOCALAPPDATA%\CUTLIFY\Temporales` |
| Miniaturas del historial | `%LOCALAPPDATA%\CUTLIFY\Thumbnails` |
| Configuración / historial / logs | `%LOCALAPPDATA%\CUTLIFY` |

Los temporales viven en `%LOCALAPPDATA%` (no en Documentos) para no sincronizar cientos de capturas rápidas con OneDrive.

## Atajos

| Atajo | Acción |
|---|---|
| `Win + Shift + S` | Captura rápida |
| `Win + Shift + R` | Grabación rápida (iniciar / detener) |
| `Ctrl + Shift + X` | Abrir CUTLIFY |
| `Alt + N` | Repetir la última captura |
| `Ctrl + C` / `Ctrl + S` / `Ctrl + Shift + S` | Copiar / guardar / guardar como (editor) |
| `Ctrl + Z` / `Ctrl + Y` | Deshacer / rehacer |

Configurables en **Configuración → Atajos** (detecta conflictos).

## Instalación

Descarga `CUTLIFY-Setup.exe` (o el portable `CUTLIFY.exe`) desde las **Releases oficiales** y comprueba su SHA-256 con `SHA256SUMS.txt`. El instalador no requiere administrador, permite elegir la carpeta y, al desinstalar, pregunta si conservar configuración, historial y archivos.

## Distribución oficial y actualizaciones

- El código fuente está en un **repositorio privado**. Las versiones públicas se publican en un repositorio **público solo de binarios** (por defecto `cristiancordova1207/cutlify-releases`, configurable con la variable de repositorio `RELEASES_REPO`).
- Solo son oficiales los binarios generados por el pipeline de GitHub Actions y publicados en esas Releases.
- El actualizador integrado solo consulta ese repositorio, ignora las pre-releases, exige una versión mayor, comprueba tamaño, **SHA-256** y versión del archivo antes de instalar, y conserva configuración e historial.

## Firma digital

Las Releases públicas **solo se publican con `CUTLIFY.exe` y `CUTLIFY-Setup.exe` firmados** (Authenticode con timestamp). Si la firma no está configurada, el workflow muestra *“Code signing is not configured.”* y no publica la Release oficial (las builds quedan como artefactos de desarrollo).

Proveedores soportados (sin acoplar la arquitectura a uno): certificado **OV/EV** (`SIGNING_PROVIDER=pfx`) o **Azure Artifact Signing** (`SIGNING_PROVIDER=artifact-signing`). Los certificados y claves se guardan **solo como secretos de GitHub**, nunca en el repositorio. Ver `docs/SIGNING.md`.

> La firma no elimina inmediatamente los avisos de SmartScreen: la reputación se construye con el tiempo usando siempre la misma identidad de firma.

## Compilación (desarrollo)

Requisitos: Windows 10/11, [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0), opcional Inno Setup 6.

```powershell
./build.ps1                         # pruebas + releases/CUTLIFY.exe + CUTLIFY-Setup.exe + SHA256SUMS.txt (sin firma)
dotnet run --project src/CUTLIFY    # ejecutar en modo desarrollo
```

Estructura: `src/CUTLIFY/` (Capture, Clipboard, Recording, Storage, Hotkeys, Editor, Ocr, Notifications, Tray, Settings, Update, UI, Themes), `tests/CUTLIFY.Tests/`, `installer/`, `scripts/` (firma), `icons/`.

## Limitaciones conocidas

- **WEBP**: no incluido (Windows no trae codificador WebP).
- **Captura de página completa** con desplazamiento: no incluida; requiere integración con el navegador y no sería fiable de forma genérica.
- **Webcam** y **Replay Buffer**: previstos para una versión futura.
- **Recuperar desde la Papelera** desde CUTLIFY: no incluido (no es fiable de forma programática); se puede restaurar desde la Papelera de Windows.
- La grabación de **ventana** graba el área de la ventana al iniciar (no la sigue si se mueve).
- Una grabación interrumpida por un cierre inesperado no es recuperable (el MP4 no se finalizó): se registra como «Grabación incompleta».

## Licencia

Software propietario y gratuito. Ver [LICENSE.txt](LICENSE.txt) (borrador pendiente de revisión legal) y [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).

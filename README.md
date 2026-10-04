<p align="center"><img src="assets/logo.png" width="96" alt="CUTLIFLY"></p>

# CUTLIFLY

**Capture. Record. Share.** — Herramienta de captura y grabación de pantalla para Windows 10/11.

Atajo → seleccionas una zona → se copia sola → **Ctrl + V** donde quieras. Sin abrir carpetas, sin guardar a mano y sin llenar el disco: CUTLIFLY limpia sus temporales automáticamente.

Aplicación nativa en C# / .NET 8 (WPF + Win32). 100 % local: sin cuentas, sin telemetría, sin nube.

## Características

- **Capturas**: rectangular, ventana (resalta la ventana bajo el cursor) y pantalla completa (elige monitor). Pantalla congelada y oscurecida, tamaño en vivo (`1280 × 720`), Esc cancela.
- **Portapapeles automático** (imagen DIB + PNG): se pega en ChatGPT, Discord, WhatsApp Web, Telegram, Office…
- **Modo solo portapapeles**: desactiva «Guardado temporal» y no se crea ningún archivo hasta que pulses Guardar.
- **Temporizador**: 3, 5 o 10 segundos.
- **Grabación** de región, ventana o pantalla completa a **MP4 (H.264)** con Media Foundation y codificador por hardware si existe (NVENC, Quick Sync, AMF). 30/60 FPS, calidad baja/media/alta, barra flotante con pausa y detener (no aparece en el vídeo).
- **Vídeo al portapapeles como archivo**: Ctrl + V en el Explorador, Discord, Telegram, WhatsApp Desktop… (las apps que no aceptan archivos no pegan nada; no se simula).
- **Temporales con caducidad** en `%LOCALAPPDATA%\CUTLIFLY\Temp`: 5 min, 30 min, 1 h, 6 h, 12 h, 1 día, 3 días o nunca. Funciona aunque la app estuviera cerrada. Papelera opcional (activada por defecto).
- **Historial** con búsqueda por nombre, fecha o tipo, y filtros Todas / Capturas / Grabaciones.
- **Editor**: cursor, lápiz, resaltador, borrador, rectángulo, círculo, línea, flecha, texto, recorte, deshacer/rehacer, copiar y guardar.
- **Fijar** una captura siempre encima y **Capturar otra vez** (repite región/ventana/monitor).
- **Multimonitor y DPI** (100–200 %, PerMonitorV2).
- Bandeja del sistema, inicio con Windows, notificaciones, tema claro/oscuro/sistema, arrastrar y soltar.
- **Actualizaciones** desde GitHub Releases con verificación SHA-256.

## Instalación

1. Descarga `CUTLIFLY-Setup.exe` desde [Releases](https://github.com/cristiancordova1207/cutlifly/releases) (o el portable `CUTLIFLY.exe`).
2. Ejecuta el instalador (no requiere administrador; se instala en `%LOCALAPPDATA%\Programs\CUTLIFLY`).
3. Opcional: marca «Iniciar CUTLIFLY con Windows».

> El ejecutable no está firmado digitalmente: SmartScreen puede pedir confirmación la primera vez («Más información → Ejecutar de todas formas»).

## Uso y atajos

| Atajo | Acción |
|---|---|
| `Win + Shift + S` | Captura rápida (sin abrir la ventana) |
| `Win + Shift + R` | Grabación rápida: seleccionar región / detener |
| `Ctrl + Shift + X` | Abrir CUTLIFLY |
| `Esc` / clic derecho | Cancelar selección |
| `Ctrl + Z` / `Ctrl + Y` | Deshacer / rehacer en el editor |
| `Ctrl + C` / `Ctrl + S` | Copiar / guardar desde el editor |
| `Ctrl + N` | Nueva captura (ventana principal) |

Los atajos se cambian en **Configuración → Atajos** (detecta conflictos con CUTLIFLY y con otras apps). CUTLIFLY intercepta `Win + Shift + S` / `Win + Shift + R` antes que Windows, así que sustituye a la Herramienta Recortes mientras está abierto.

## Configuración

General · Capturas · Grabación · Portapapeles · Almacenamiento · Atajos · Apariencia · Avanzado · Actualizaciones.
Se guarda en `%LOCALAPPDATA%\CUTLIFLY\settings.json`; el historial en `history.json`; los logs en `Logs\`.

## Compilación

Requisitos: Windows 10/11, [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0), opcional [Inno Setup 6](https://jrsoftware.org/isinfo.php).

```powershell
./build.ps1            # pruebas + releases/CUTLIFLY.exe + releases/CUTLIFLY-Setup.exe + SHA256SUMS.txt
dotnet run --project src/CUTLIFLY   # ejecutar en modo desarrollo
```

## Desarrollo

```
src/CUTLIFLY/
  Capture/        captura GDI, capa de selección, ventanas
  Clipboard/      portapapeles (imagen y archivo)
  Recording/      Media Foundation, grabador, barra y marco
  Storage/        historial, temporales, limpieza
  Hotkeys/        atajos globales (hook de teclado)
  Editor/         editor de imágenes y vista de vídeo
  Notifications/  notificaciones propias
  Tray/           bandeja del sistema
  Settings/       configuración e inicio con Windows
  Update/         actualizador desde GitHub Releases
  UI/             ventanas (principal, historial, configuración, fijar, diálogos)
  Themes/         paleta clara/oscura y estilos
tests/CUTLIFLY.Tests/   pruebas xUnit (incluye una grabación real a MP4)
installer/              script de Inno Setup
icons/                  icono (.ico multi-tamaño) y generador
```

Flujo: código → pruebas → commit → push → **GitHub Actions** (build + tests + exe + instalador) → Release.

## Versiones y Releases

- La versión vive en el archivo `VERSION` (semver) y se incrusta en el exe (Propiedades → Detalles).
- Cada push compila y adjunta los artefactos al workflow.
- Al hacer push a `main` con una versión nueva en `VERSION` (o al crear un tag `vX.Y.Z`, o lanzando el workflow manualmente con «release»), GitHub Actions publica la Release `vX.Y.Z` con `CUTLIFLY.exe`, `CUTLIFLY-Setup.exe`, `SHA256SUMS.txt` y las notas de `CHANGELOG.md`.

## Sistema de actualizaciones

CUTLIFLY consulta `api.github.com/repos/cristiancordova1207/cutlifly/releases/latest` (al iniciar, a diario, semanalmente o nunca; configurable) y muestra **«CUTLIFLY X.Y.Z está disponible»** con *Ver cambios*, *Actualizar* y *Más tarde*. Al actualizar:

1. Solo acepta descargas de `github.com/cristiancordova1207/cutlifly/releases/download/`.
2. Exige que la versión sea mayor que la instalada.
3. Verifica tamaño y **SHA-256** contra `SHA256SUMS.txt`, y la versión del exe descargado.
4. Instalación con Setup → ejecuta el instalador en modo silencioso; portable → reemplaza el exe.
5. Reinicia CUTLIFLY. La configuración, el historial y los atajos (en `%LOCALAPPDATA%\CUTLIFLY`) se conservan.

## Privacidad

Nada sale de tu PC. La única conexión de red es la comprobación de actualizaciones contra GitHub.

## Licencia

[MIT](LICENSE)

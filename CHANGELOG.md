# Changelog

Todas las versiones siguen [Versionado Semántico](https://semver.org/lang/es/).

## [1.0.0] - 2026-10-04

### Initial release
- Captura rectangular, de ventana y de pantalla completa con capa de selección congelada (estilo Herramienta Recortes), dimensiones en vivo y puntos de control.
- Multimonitor y DPI por monitor (PerMonitorV2): la selección coincide con el cursor en píxeles físicos.
- Copia automática al portapapeles (CF_DIB + PNG) y modo **solo portapapeles** (sin crear archivos).
- Temporizador: sin retraso, 3, 5 y 10 segundos.
- Grabación de pantalla (región, ventana, pantalla completa) a MP4/H.264 con Media Foundation y aceleración por hardware cuando existe; barra flotante con pausa/detener excluida de la grabación.
- Grabaciones copiables como archivo (Ctrl + V en apps que aceptan archivos).
- Almacenamiento temporal en `%LOCALAPPDATA%\CUTLIFLY\Temp` con expiración configurable (5 min – 3 días / nunca) que funciona aunque la app haya estado cerrada; Papelera opcional.
- Historial con búsqueda (nombre, fecha, tipo) y filtros; capturas recientes con acciones rápidas.
- Editor: cursor/mover, lápiz, resaltador, borrador, rectángulo, círculo, línea, flecha, texto (tamaño, negrita, color, alineación), recorte con asas, deshacer/rehacer, copiar, guardar.
- Fijar capturas encima de todas las ventanas y «Capturar otra vez».
- Atajos globales configurables (Win + Shift + S, Win + Shift + R, Ctrl + Shift + X) con detección de conflictos.
- Bandeja del sistema, inicio con Windows, notificaciones propias, tema claro/oscuro/sistema.
- Arrastrar y soltar imágenes y vídeos.
- Instalador por usuario (Inno Setup), GitHub Actions (build, pruebas, Release) y actualizador desde GitHub Releases con verificación SHA-256.
- Logs locales en `%LOCALAPPDATA%\CUTLIFLY\Logs` sin contenido de capturas.

# Changelog

Todas las versiones siguen [Versionado Semántico](https://semver.org/lang/es/).

## [1.2.0] - 2026-10-14

### Added
- **Grabadora de audio multipista**: pistas de micrófono, escritorio (todo el sonido del PC) y aplicaciones concretas (process loopback de Windows 11: p. ej. solo Spotify). Pistas sincronizadas en WAV + mezcla en M4A (AAC), MP3 o WAV; volumen y silencio por pista en la mezcla; medidores de nivel; pausa.
- Atajo global `Win + Shift + A` para iniciar/detener la grabación de audio (configurable); acceso desde «Nuevo», el selector Captura/Grabación/Audio y la bandeja.
- Las grabaciones de audio aparecen en el historial con miniatura de forma de onda, reproducción, detalles de pistas y filtro «Audio»; guardar copia la mezcla y las pistas a `Documentos\CUTLIFY\Audio`.
- Arrastrar y soltar archivos de audio (MP3, M4A, WAV…).

### Changed
- Las Releases y el actualizador usan este repositorio (`cristiancordova1207/CUTLIFLY`).

## [1.1.0] - 2026-10-04

### Changed
- Nombre oficial: **CUTLIFY** (ejecutable `CUTLIFY.exe`, instalador `CUTLIFY-Setup.exe`, datos en `%LOCALAPPDATA%\CUTLIFY`).
- Capturas y grabaciones guardadas en `Documentos\CUTLIFY\Capturas` y `\Grabaciones` (configurables, con opción de mover los archivos existentes); temporales en `%LOCALAPPDATA%\CUTLIFY\Temporales`.
- «Guardar» guarda directamente con nombre organizado (`Captura_2026-10-04_14-32-18.png`); «Guardar como» pide formato y ubicación.
- Software propietario y gratuito: licencia propia (borrador pendiente de revisión legal) y avisos de terceros; el código fuente es privado.
- Las Releases oficiales se publican en un repositorio público solo de binarios y únicamente si están firmadas.

### Added
- Calidad de grabación: perfiles, resolución (hasta 4K), 30–240 FPS limitados a la frecuencia real del monitor, codecs H.264/HEVC/AV1 según disponibilidad, calidad y bitrate automático o manual. Predeterminado: 1080p · 30 FPS · H.264 · bitrate automático · audio activado.
- Audio del sistema y micrófono mezclados en una pista AAC sincronizada, con dispositivos, prueba de micrófono, medidor de nivel y consentimiento propio.
- Estadísticas reales de grabación (FPS reales, fotogramas perdidos) y resaltado de clics.
- Historial con estados: Disponible, Eliminada, Expirada, No guardada y Grabación incompleta; miniaturas independientes en `Thumbnails`; «Quitar del historial» y «Limpiar historial» (no borran archivos); filtros por estado.
- Captura de forma libre, todas las pantallas, selector de color HEX/RGB, lupa, bloqueo de proporciones, mover con Espacio y ajuste previo opcional.
- Retraso de 1 y 2 s con cuenta regresiva visual; repetir captura con Alt + N.
- Editor: guardar como, opacidad, colores recientes y selector de color; OCR local («Extraer texto»).
- Pausa temporal de atajos desde la bandeja; Configuración → Privacidad y Diagnóstico; detalles de cada archivo.
- Firma digital en GitHub Actions con proveedor intercambiable (OV/EV o Azure Artifact Signing), verificación de firmas y SHA-256 de los archivos firmados.
- Desinstalador que pregunta si conservar configuración, historial y archivos.

### Fixed
- Grabaciones interrumpidas por un cierre inesperado se detectan al iniciar y se registran como «Grabación incompleta».

## [1.0.0] - 2026-10-04

### Initial release
- Capturas, grabación MP4, portapapeles, temporales con caducidad, historial, editor, atajos globales, bandeja, instalador, GitHub Actions y actualizador con verificación SHA-256.

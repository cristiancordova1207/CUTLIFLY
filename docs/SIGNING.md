# Firma digital de CUTLIFY

El workflow `.github/workflows/build.yml` firma `CUTLIFY.exe` **antes** de empaquetarlo, crea el instalador (firmando también el desinstalador con el proveedor PFX), firma `CUTLIFY-Setup.exe` **al final**, verifica las firmas y solo entonces calcula `SHA256SUMS.txt` y publica la Release. Ningún binario se modifica después de firmarse.

## Configuración (Settings → Secrets and variables → Actions)

Variables de repositorio:

| Variable | Valor |
|---|---|
| `SIGNING_PROVIDER` | `pfx` o `artifact-signing` (vacío = sin firma → no hay Release oficial) |
| `SIGNING_PUBLISHER` | Texto que debe aparecer en el sujeto del certificado (p. ej. el nombre del editor). Si no coincide, la build falla. |
| `RELEASES_REPO` | Repositorio público de binarios, p. ej. `cristiancordova1207/cutlify-releases` |

Secretos:

| Proveedor | Secretos |
|---|---|
| `pfx` (OV/EV exportable) | `SIGNING_CERT_PFX_BASE64` (PFX en Base64), `SIGNING_CERT_PASSWORD` |
| `artifact-signing` (Azure) | `AZURE_TENANT_ID`, `AZURE_CLIENT_ID`, `AZURE_CLIENT_SECRET` + variables `ARTIFACT_SIGNING_ENDPOINT`, `ARTIFACT_SIGNING_ACCOUNT`, `ARTIFACT_SIGNING_PROFILE` |
| Publicación | `RELEASES_TOKEN`: token con permiso *contents: write* sobre `RELEASES_REPO` |

Reglas: nunca subir certificados ni claves al repositorio; nunca usar certificados autofirmados para Releases públicas; usar siempre la misma identidad de firma.

Con el proveedor `artifact-signing`, el desinstalador interno de Inno Setup no se firma (la acción de Azure no se integra con `SignTool` de Inno); `CUTLIFY.exe` y `CUTLIFY-Setup.exe` sí.

Para añadir otro proveedor basta con añadir sus pasos de firma condicionados a `SIGNING_PROVIDER`; la verificación (`scripts/verify-signature.ps1`) es común a todos.

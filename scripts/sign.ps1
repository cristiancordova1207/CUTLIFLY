<#
  Firma de código de CUTLIFY (proveedor intercambiable). Uso:
    ./scripts/sign.ps1 -Files releases/CUTLIFY.exe
  Proveedor elegido con la variable de entorno SIGNING_PROVIDER:
    pfx  -> certificado OV/EV exportable: SIGNING_CERT_PFX_BASE64 + SIGNING_CERT_PASSWORD (secretos de GitHub)
    (Azure Artifact Signing se firma con su acción oficial en el workflow; este script solo verifica)
  Nunca imprime secretos. El certificado temporal se borra al terminar.
#>
param(
  [Parameter(Mandatory = $true)][string[]]$Files,
  [string]$TimestampUrl = $(if ($env:SIGNING_TIMESTAMP_URL) { $env:SIGNING_TIMESTAMP_URL } else { 'http://timestamp.digicert.com' })
)
$ErrorActionPreference = 'Stop'

function Get-SignTool {
  $tool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue |
    Sort-Object FullName -Descending | Select-Object -First 1
  if (-not $tool) { throw 'signtool.exe no encontrado (Windows SDK).' }
  return $tool.FullName
}

switch ($env:SIGNING_PROVIDER) {
  'pfx' {
    if (-not $env:SIGNING_CERT_PFX_BASE64 -or -not $env:SIGNING_CERT_PASSWORD) { throw 'Faltan los secretos SIGNING_CERT_PFX_BASE64 / SIGNING_CERT_PASSWORD.' }
    $pfx = Join-Path $env:RUNNER_TEMP 'cutlify-signing.pfx'
    try {
      [IO.File]::WriteAllBytes($pfx, [Convert]::FromBase64String($env:SIGNING_CERT_PFX_BASE64))
      $signtool = Get-SignTool
      foreach ($f in $Files) {
        & $signtool sign /fd SHA256 /f $pfx /p $env:SIGNING_CERT_PASSWORD /tr $TimestampUrl /td SHA256 /d 'CUTLIFY' $f | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "La firma de $f falló." }
      }
    } finally { Remove-Item $pfx -Force -ErrorAction SilentlyContinue }
  }
  default { throw "Proveedor de firma no soportado por este script: '$($env:SIGNING_PROVIDER)'" }
}

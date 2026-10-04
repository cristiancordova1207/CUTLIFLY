<#
  Verifica firmas Authenticode: firma válida, cadena de confianza, timestamp y editor esperado.
  Falla (exit 1) si cualquier archivo no cumple. SIGNING_PUBLISHER = texto que debe aparecer en el sujeto del certificado.
#>
param([Parameter(Mandatory = $true)][string[]]$Files)
$ErrorActionPreference = 'Stop'
$failed = $false
foreach ($f in $Files) {
  $sig = Get-AuthenticodeSignature -FilePath $f
  $subject = $sig.SignerCertificate.Subject
  $problems = @()
  if ($sig.Status -ne 'Valid') { $problems += "estado $($sig.Status): $($sig.StatusMessage)" }
  if (-not $sig.TimeStamperCertificate) { $problems += 'sin timestamp' }
  if ($env:SIGNING_PUBLISHER -and ($subject -notlike "*$($env:SIGNING_PUBLISHER)*")) { $problems += "editor inesperado ($subject)" }
  if ($problems.Count -gt 0) { Write-Host "::error::$([IO.Path]::GetFileName($f)) NO firmado correctamente: $($problems -join '; ')"; $failed = $true }
  else { Write-Host "OK  $([IO.Path]::GetFileName($f))  firmado por: $subject  (timestamp: $($sig.TimeStamperCertificate.Subject))" }
}
if ($failed) { exit 1 }

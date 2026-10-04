# Compila CUTLIFLY localmente: tests + CUTLIFLY.exe + CUTLIFLY-Setup.exe (si Inno Setup está instalado).
param([switch]$SkipTests)
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
$version = (Get-Content VERSION -Raw).Trim()
Write-Host "CUTLIFLY $version" -ForegroundColor Magenta

if (-not $SkipTests) { dotnet test tests/CUTLIFLY.Tests/CUTLIFLY.Tests.csproj -c Release }

dotnet publish src/CUTLIFLY/CUTLIFLY.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
  -p:PublishReadyToRun=true -p:DebugType=none -o publish

New-Item -ItemType Directory -Force releases | Out-Null
Copy-Item publish/CUTLIFLY.exe releases/CUTLIFLY.exe -Force

$iscc = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
if (Test-Path $iscc) { & $iscc "/DAppVersion=$version" installer/CUTLIFLY.iss }
else { Write-Warning "Inno Setup 6 no encontrado: se omite CUTLIFLY-Setup.exe" }

Get-ChildItem releases -Filter *.exe | ForEach-Object {
  "{0}  {1}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower(), $_.Name
} | Set-Content releases/SHA256SUMS.txt -Encoding ascii
Get-ChildItem releases

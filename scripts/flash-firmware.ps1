param([string]$Port = 'COM5')
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/esp-env.ps1"
$projectRoot = Split-Path $PSScriptRoot -Parent
$backup = Join-Path $projectRoot ('backups/pre-flash-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.bin')
New-Item -ItemType Directory -Force (Split-Path $backup) | Out-Null
python -m esptool --chip esp32s3 --port $Port --baud 460800 read_flash 0 ALL $backup
if ($LASTEXITCODE) { throw 'Backup failed; flash cancelled' }
if ((Get-Item $backup).Length -ne 16777216) { throw 'Expected a 16 MiB N16R8 backup; flash cancelled' }
Push-Location "$projectRoot/firmware/build-esp32s3"
try {
    python -m esptool --chip esp32s3 --port $Port --baud 460800 write_flash '@flash_args'
    if ($LASTEXITCODE) { throw 'Firmware flash failed' }
} finally { Pop-Location }

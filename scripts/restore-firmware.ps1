param([Parameter(Mandatory)][string]$Backup, [string]$Port = 'COM5')
$ErrorActionPreference = 'Stop'
if ((Get-Item -LiteralPath $Backup).Length -ne 16777216) { throw 'Expected a complete 16 MiB Flash backup' }
$backupPath = (Resolve-Path -LiteralPath $Backup).Path
. "$PSScriptRoot/esp-env.ps1"
python -m esptool --chip esp32s3 --port $Port --baud 460800 write_flash --flash_size 16MB 0 $backupPath
if ($LASTEXITCODE) { throw 'Firmware restore failed' }

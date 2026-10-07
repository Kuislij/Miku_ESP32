param([string]$Port = 'COM5', [switch]$InitializeStorage)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/esp-env.ps1"
$projectRoot = Split-Path $PSScriptRoot -Parent
if ($InitializeStorage) {
    $image = Join-Path $projectRoot 'firmware/build-esp32s3/storage.bin'
    if (!(Test-Path -LiteralPath $image) -or (Get-Item -LiteralPath $image).Length -ne 12517376) { throw 'Build the FATFS image before initializing storage' }
    $partition = Get-Content -LiteralPath "$projectRoot/firmware/partitions.csv" | Where-Object { $_ -match '^storage,' }
    if ($partition -notmatch '^storage,\s*data,\s*fat,\s*0x410000,\s*0xBF0000,') { throw 'Storage partition differs from the validated N16R8 layout' }
}
$backup = Join-Path $projectRoot ('backups/pre-flash-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.bin')
New-Item -ItemType Directory -Force (Split-Path $backup) | Out-Null
python -m esptool --chip esp32s3 --port $Port --baud 460800 read_flash 0 ALL $backup
if ($LASTEXITCODE) { throw 'Backup failed; flash cancelled' }
if ((Get-Item $backup).Length -ne 16777216) { throw 'Expected a 16 MiB N16R8 backup; flash cancelled' }
if ($InitializeStorage) {
    # Detect a FAT boot sector anywhere in this partition, including WL relocation.
    $existingBytes = [IO.File]::ReadAllBytes($backup)
    for ($sectorOffset = 0x410000; $sectorOffset -lt 0x1000000; $sectorOffset += 4096) {
        if ($existingBytes[$sectorOffset + 510] -eq 0x55 -and $existingBytes[$sectorOffset + 511] -eq 0xAA -and ([Text.Encoding]::ASCII.GetString($existingBytes, $sectorOffset + 54, 3) -eq 'FAT' -or [Text.Encoding]::ASCII.GetString($existingBytes, $sectorOffset + 82, 3) -eq 'FAT')) {
            throw 'An existing FAT volume was detected. Storage initialization cancelled to preserve files; run without -InitializeStorage.'
        }
    }
}
Push-Location "$projectRoot/firmware/build-esp32s3"
try {
    python -m esptool --chip esp32s3 --port $Port --baud 460800 write_flash '@flash_args'
    if ($LASTEXITCODE) { throw 'Firmware flash failed' }
    if ($InitializeStorage) {
        python -m esptool --chip esp32s3 --port $Port --baud 460800 write_flash 0x410000 storage.bin
        if ($LASTEXITCODE) { throw 'Storage initialization failed; full backup is preserved' }
    }
} finally { Pop-Location }

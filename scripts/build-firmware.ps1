$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/esp-env.ps1"
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location "$projectRoot/firmware"
try {
    python "$env:IDF_PATH/tools/idf.py" -B build-esp32s3 -D IDF_TARGET=esp32s3 build
    if ($LASTEXITCODE) { throw 'Firmware build failed' }
} finally { Pop-Location }

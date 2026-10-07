$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$idfPath = Join-Path $projectRoot '.tools/esp-idf'
$env:IDF_TOOLS_PATH = Join-Path $projectRoot '.tools/espressif'
$env:IDF_PATH = $idfPath
New-Item -ItemType Directory -Force "$projectRoot/.tools" | Out-Null
if (!(Test-Path "$idfPath/tools/idf.py")) {
    git clone --branch v5.5.2 --depth 1 --shallow-submodules --recursive https://github.com/espressif/esp-idf.git $idfPath
    if ($LASTEXITCODE) { throw 'ESP-IDF download failed' }
}
py -3.11 "$idfPath/tools/idf_tools.py" install --targets=esp32s3
if ($LASTEXITCODE) { throw 'ESP-IDF tool install failed' }
py -3.11 "$idfPath/tools/idf_tools.py" install-python-env
if ($LASTEXITCODE) { throw 'ESP-IDF Python setup failed' }
Write-Output 'ESP-IDF 5.5.2 is ready. Run scripts/build-firmware.ps1.'

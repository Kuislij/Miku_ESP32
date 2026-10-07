$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$env:IDF_PATH = Join-Path $projectRoot '.tools/esp-idf'
$env:IDF_TOOLS_PATH = Join-Path $projectRoot '.tools/espressif'
$basePython = & py -3.11 -c 'import sys; print(sys.executable)'
if ($LASTEXITCODE) { throw 'Python 3.11 is required' }
$env:PATH = (Split-Path $basePython) + ';' + $env:PATH
. "$env:IDF_PATH/export.ps1"

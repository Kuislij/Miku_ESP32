$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$python = Join-Path $projectRoot '.tools/python/Scripts/python.exe'
if (!(Test-Path $python)) {
    py -3.11 -m venv "$projectRoot/.tools/python"
    if ($LASTEXITCODE) { throw 'Python environment setup failed' }
}
& $python -m pip install imageio-ffmpeg==0.6.0
if ($LASTEXITCODE) { throw 'FFmpeg dependency install failed' }
$ffmpeg = & $python -c 'import imageio_ffmpeg; print(imageio_ffmpeg.get_ffmpeg_exe())'
New-Item -ItemType Directory -Force "$projectRoot/.tools/media" | Out-Null
Copy-Item -LiteralPath $ffmpeg -Destination "$projectRoot/.tools/media/ffmpeg.exe"
Write-Output 'Media decoder installed. Rebuild Control Center to include ffmpeg.exe.'

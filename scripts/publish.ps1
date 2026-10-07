$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$dotnet = Join-Path $projectRoot '.tools/dotnet/dotnet.exe'
if (!(Test-Path $dotnet)) { $dotnet = 'dotnet' }
& $dotnet publish "$projectRoot/control-center/MikuOS.ControlCenter.csproj" -c Release -r win-x64 --self-contained true -o "$projectRoot/artifacts/ControlCenter"
if ($LASTEXITCODE) { throw 'Control Center publish failed' }
Write-Output 'Standalone Windows x64 app: artifacts/ControlCenter/MikuOS.ControlCenter.exe'

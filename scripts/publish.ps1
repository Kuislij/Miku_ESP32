$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$dotnet = Join-Path $projectRoot '.tools/dotnet/dotnet.exe'
if (!(Test-Path $dotnet)) { $dotnet = 'dotnet' }
& $dotnet publish "$projectRoot/control-center/MikuOS.ControlCenter.csproj" -c Release -r win-x64 --self-contained true -o "$projectRoot/artifacts/ControlCenter"
if ($LASTEXITCODE) { throw 'Control Center publish failed' }
$launcher = New-Object -ComObject WScript.Shell
$shortcut = $launcher.CreateShortcut((Join-Path $projectRoot 'MikuOS.lnk'))
$shortcut.TargetPath = Join-Path $projectRoot 'artifacts/ControlCenter/MikuOS.ControlCenter.exe'
$shortcut.WorkingDirectory = Join-Path $projectRoot 'artifacts/ControlCenter'
$shortcut.Arguments = '--auto'
$shortcut.Description = 'MikuOS — automatic ESP32 connection'
$shortcut.IconLocation = $shortcut.TargetPath + ',0'
$shortcut.Save()
Write-Output 'Standalone Windows x64 app: artifacts/ControlCenter/MikuOS.ControlCenter.exe'
Write-Output 'Double-click MikuOS.lnk in the project folder to launch with automatic device detection.'

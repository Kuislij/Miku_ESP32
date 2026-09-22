$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dotnet = Join-Path $root '.tools/dotnet/dotnet.exe'
if (!(Test-Path $dotnet)) { $dotnet = 'dotnet' }
& $dotnet build "$root/control-center/MikuOS.ControlCenter.csproj" -c Release
if ($LASTEXITCODE) { throw 'Control Center build failed' }
& $dotnet run --project "$root/tests/MikuOS.Tests.csproj" -c Release
if ($LASTEXITCODE) { throw 'Tests failed' }

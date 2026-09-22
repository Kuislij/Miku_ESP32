@echo off
set "DOTNET_ROOT=%~dp0.tools\dotnet"
"%DOTNET_ROOT%\dotnet.exe" run --project "%~dp0control-center\MikuOS.ControlCenter.csproj" -c Release
pause

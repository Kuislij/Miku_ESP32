@echo off
set "DOTNET_ROOT=%~dp0.tools\dotnet"
if exist "%~dp0artifacts\ControlCenter\MikuOS.ControlCenter.exe" (
    start "" "%~dp0artifacts\ControlCenter\MikuOS.ControlCenter.exe"
) else (
    "%DOTNET_ROOT%\dotnet.exe" run --project "%~dp0control-center\MikuOS.ControlCenter.csproj" -c Release
    pause
)

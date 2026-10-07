@echo off
set "DOTNET_ROOT=%~dp0.tools\dotnet"
set "MIKU_PORT=COM5"
if not "%~1"=="" set "MIKU_PORT=%~1"
if exist "%~dp0artifacts\ControlCenter\MikuOS.ControlCenter.exe" (
    start "" "%~dp0artifacts\ControlCenter\MikuOS.ControlCenter.exe" --serial "%MIKU_PORT%"
) else (
    "%DOTNET_ROOT%\dotnet.exe" run --project "%~dp0control-center\MikuOS.ControlCenter.csproj" -c Release -- --serial "%MIKU_PORT%"
)

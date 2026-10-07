@echo off
set "DOTNET_ROOT=%~dp0.tools\dotnet"
set "MIKU_CONNECTION=--auto"
if not "%~1"=="" set "MIKU_CONNECTION=--serial %~1"
if exist "%~dp0artifacts\ControlCenter\MikuOS.ControlCenter.exe" (
    start "" "%~dp0artifacts\ControlCenter\MikuOS.ControlCenter.exe" %MIKU_CONNECTION%
) else (
    "%DOTNET_ROOT%\dotnet.exe" run --project "%~dp0control-center\MikuOS.ControlCenter.csproj" -c Release -- %MIKU_CONNECTION%
)

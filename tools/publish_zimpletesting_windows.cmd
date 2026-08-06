@echo off
setlocal

set "ROOT=%~dp0.."
set "CONFIG=Release"
set "PUBLISH_DIR=C:\Users\Zeugma\Desktop\ZimpleTesting_installer\"

if /I "%~1"=="debug" set "CONFIG=Debug"

echo.
echo Publishing ZimpleTesting
echo Root:    %ROOT%
echo Config:  %CONFIG%
echo Publish: %PUBLISH_DIR%
echo.

set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" (
    echo Could not find vswhere.exe.
    pause
    exit /b 1
)

for /f "usebackq tokens=*" %%i in (`"%VSWHERE%" -latest -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe`) do set "MSBUILD=%%i"

if not exist "%MSBUILD%" (
    echo Could not find MSBuild.exe.
    pause
    exit /b 1
)

echo Using MSBuild:
echo %MSBUILD%
echo.

"%MSBUILD%" "%ROOT%\ZimpleTesting.sln" /t:Restore;Clean;Publish /p:Configuration=%CONFIG% /p:Platform="Any CPU" /p:PublishDir="%PUBLISH_DIR%" /p:PublishUrl="%PUBLISH_DIR%" /p:InstallUrl="%PUBLISH_DIR%" /p:UpdateEnabled=false /m /v:m
set "BUILD_EXIT=%errorlevel%"

echo.
if "%BUILD_EXIT%"=="0" (
    echo PUBLISH OK.
    echo Installer folder: %PUBLISH_DIR%
) else (
    echo PUBLISH FAILED with code %BUILD_EXIT%.
)

pause
exit /b %BUILD_EXIT%

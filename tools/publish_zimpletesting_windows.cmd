@echo off
setlocal

set "SOURCE_ROOT=%~dp0.."
set "ROOT=C:\zimple\ZimpleTesting_work_local"
set "CONFIG=Release"
set "PUBLISH_DIR=C:\zimple\ZimpleTesting_installer"

if /I "%~1"=="debug" set "CONFIG=Debug"

echo.
echo Publishing ZimpleTesting
echo Source:  %SOURCE_ROOT%
echo Root:    %ROOT%
echo Config:  %CONFIG%
echo Publish: %PUBLISH_DIR%
echo.

if not exist "C:\zimple" mkdir "C:\zimple"

echo Copying source to local Windows disk...
robocopy "%SOURCE_ROOT%" "%ROOT%" /MIR /XD .git bin obj /XF .DS_Store >nul
if errorlevel 8 (
    echo Robocopy failed with code %errorlevel%.
    pause
    exit /b %errorlevel%
)

if not exist "%PUBLISH_DIR%" mkdir "%PUBLISH_DIR%"

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

"%MSBUILD%" "%ROOT%\ZimpleTesting.sln" "/t:Restore;Clean;Publish" "/p:Configuration=%CONFIG%" "/p:Platform=Any CPU" "/p:PublishDir=%PUBLISH_DIR%\\" "/p:PublishUrl=%PUBLISH_DIR%\\" "/p:InstallUrl=%PUBLISH_DIR%\\" "/p:UpdateEnabled=false" /m /v:m
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

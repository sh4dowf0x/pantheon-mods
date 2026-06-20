@echo off
setlocal EnableExtensions EnableDelayedExpansion

set "REPO=sh4dowf0x/pantheon-mods"
set "PACKAGE=PantheonAllMods.zip"
set "DOWNLOAD_URL=https://github.com/%REPO%/releases/latest/download/%PACKAGE%"

echo Pantheon Mods latest-release installer
echo.

if not "%~1"=="" (
    set "TARGET=%~1"
    if not exist "!TARGET!\Pantheon.exe" (
        echo ERROR: The supplied folder does not look like a Pantheon game folder:
        echo !TARGET!
        echo.
        echo Usage: %~nx0 "C:\Path\To\Pantheon PTR"
        exit /b 1
    )
    goto :install
)

set "COUNT=0"
call :maybe_add "C:\PantheonPTR\App" "Standalone PTR"
call :maybe_add "%ProgramFiles(x86)%\Steam\steamapps\common\Pantheon Rise of the Fallen (PTR)" "Steam PTR"
call :maybe_add "%ProgramFiles%\Steam\steamapps\common\Pantheon Rise of the Fallen (PTR)" "Steam PTR"
call :maybe_add "D:\SteamLibrary\steamapps\common\Pantheon Rise of the Fallen (PTR)" "Steam PTR"
call :maybe_add "E:\SteamLibrary\steamapps\common\Pantheon Rise of the Fallen (PTR)" "Steam PTR"

if "%COUNT%"=="0" (
    echo ERROR: Could not auto-detect a PTR install folder.
    echo.
    echo Run this again with the PTR folder path:
    echo %~nx0 "C:\Path\To\Pantheon PTR"
    exit /b 1
)

if "%COUNT%"=="1" (
    set "TARGET=!CANDIDATE_1!"
    goto :install
)

echo Found multiple PTR installs:
echo.
for /L %%I in (1,1,%COUNT%) do (
    echo   %%I^) !CANDIDATE_LABEL_%%I! - !CANDIDATE_%%I!
)
echo.
set /P "CHOICE=Install to which folder? [1-%COUNT%]: "
if not defined CANDIDATE_%CHOICE% (
    echo ERROR: Invalid selection.
    exit /b 1
)
set "TARGET=!CANDIDATE_%CHOICE%!"
goto :install

:maybe_add
set "PATH_TO_CHECK=%~1"
set "LABEL=%~2"
if exist "%PATH_TO_CHECK%\Pantheon.exe" (
    set /A COUNT+=1
    set "CANDIDATE_!COUNT!=%PATH_TO_CHECK%"
    set "CANDIDATE_LABEL_!COUNT!=%LABEL%"
)
exit /b 0

:install
echo Installing latest Pantheon mods to:
echo %TARGET%
echo.

set "WORK=%TEMP%\PantheonModsLatest-%RANDOM%-%RANDOM%"
set "ZIP=%WORK%\%PACKAGE%"
set "EXTRACTED=%WORK%\extracted"

mkdir "%WORK%" >nul 2>nul
if errorlevel 1 (
    echo ERROR: Could not create temp folder:
    echo %WORK%
    exit /b 1
)

echo Downloading latest %PACKAGE%...
powershell -NoProfile -ExecutionPolicy Bypass -Command "try { [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; Invoke-WebRequest -Uri '%DOWNLOAD_URL%' -OutFile '%ZIP%' -UseBasicParsing } catch { Write-Error $_; exit 1 }"
if errorlevel 1 goto :fail

echo Extracting package...
powershell -NoProfile -ExecutionPolicy Bypass -Command "try { Expand-Archive -LiteralPath '%ZIP%' -DestinationPath '%EXTRACTED%' -Force } catch { Write-Error $_; exit 1 }"
if errorlevel 1 goto :fail

if not exist "%EXTRACTED%\GameFolder\Mods\PantheonAddonLoader.dll" (
    echo ERROR: Downloaded package did not contain the expected GameFolder layout.
    goto :fail
)

echo Creating mod folders...
mkdir "%TARGET%\Mods" >nul 2>nul
mkdir "%TARGET%\Mods\PantheonAddons" >nul 2>nul
mkdir "%TARGET%\UserLibs" >nul 2>nul

echo Installing loader and framework...
copy /Y "%EXTRACTED%\GameFolder\Mods\PantheonAddonLoader.dll" "%TARGET%\Mods\PantheonAddonLoader.dll" >nul
if errorlevel 1 goto :fail
copy /Y "%EXTRACTED%\GameFolder\UserLibs\PantheonAddonFramework.dll" "%TARGET%\UserLibs\PantheonAddonFramework.dll" >nul
if errorlevel 1 goto :fail

echo Installing addon DLLs...
for %%F in ("%EXTRACTED%\GameFolder\Mods\PantheonAddons\*.dll") do (
    copy /Y "%%~fF" "%TARGET%\Mods\PantheonAddons\%%~nxF" >nul
    if errorlevel 1 goto :fail
)

echo Installing missing default configs, preserving existing configs...
for %%F in ("%EXTRACTED%\GameFolder\Mods\PantheonAddons\*Config.json") do (
    if exist "%TARGET%\Mods\PantheonAddons\%%~nxF" (
        echo   Keeping existing %%~nxF
    ) else (
        copy "%%~fF" "%TARGET%\Mods\PantheonAddons\%%~nxF" >nul
        if errorlevel 1 goto :fail
        echo   Added %%~nxF
    )
)

echo.
echo Install complete.
echo Start Pantheon PTR and check MelonLoader logs if a mod does not appear.
goto :cleanup_success

:fail
echo.
echo Install failed. If Pantheon is running, close it and try again.
if exist "%WORK%" rmdir /S /Q "%WORK%" >nul 2>nul
exit /b 1

:cleanup_success
if exist "%WORK%" rmdir /S /Q "%WORK%" >nul 2>nul
exit /b 0

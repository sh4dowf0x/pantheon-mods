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
        goto :fail_no_work
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
    goto :fail_no_work
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
    goto :fail_no_work
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
    goto :fail_no_work
)

set "REPLACED_COUNT=0"
set "ADDED_COUNT=0"
set "PRESERVED_COUNT=0"

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
call :copy_file "%EXTRACTED%\GameFolder\Mods\PantheonAddonLoader.dll" "%TARGET%\Mods\PantheonAddonLoader.dll"
if errorlevel 1 goto :fail
call :copy_file "%EXTRACTED%\GameFolder\UserLibs\PantheonAddonFramework.dll" "%TARGET%\UserLibs\PantheonAddonFramework.dll"
if errorlevel 1 goto :fail

echo Installing addon DLLs...
for %%F in ("%EXTRACTED%\GameFolder\Mods\PantheonAddons\*.dll") do (
    call :copy_file "%%~fF" "%TARGET%\Mods\PantheonAddons\%%~nxF"
    if errorlevel 1 goto :fail
)

echo Installing missing default configs, preserving existing configs...
for %%F in ("%EXTRACTED%\GameFolder\Mods\PantheonAddons\*Config.json") do (
    if exist "%TARGET%\Mods\PantheonAddons\%%~nxF" (
        call :record_preserved "%TARGET%\Mods\PantheonAddons\%%~nxF"
    ) else (
        call :copy_file "%%~fF" "%TARGET%\Mods\PantheonAddons\%%~nxF"
        if errorlevel 1 goto :fail
    )
)

echo.
echo Install complete.
call :print_report
echo Start Pantheon PTR and check MelonLoader logs if a mod does not appear.
goto :cleanup_success

:copy_file
set "SRC_FILE=%~1"
set "DEST_FILE=%~2"
if exist "%DEST_FILE%" (
    set "ACTION=Replaced"
    set /A REPLACED_COUNT+=1
    set "REPORT_Replaced_!REPLACED_COUNT!=%DEST_FILE%"
) else (
    set "ACTION=Added"
    set /A ADDED_COUNT+=1
    set "REPORT_Added_!ADDED_COUNT!=%DEST_FILE%"
)
copy /Y "%SRC_FILE%" "%DEST_FILE%" >nul
if errorlevel 1 exit /b 1
echo   %ACTION% %~nx2
exit /b 0

:record_preserved
set /A PRESERVED_COUNT+=1
set "REPORT_Preserved_!PRESERVED_COUNT!=%~1"
echo   Keeping existing %~nx1
exit /b 0

:print_report
echo.
echo Install report:
echo   Replaced files: %REPLACED_COUNT%
for /L %%I in (1,1,%REPLACED_COUNT%) do echo     !REPORT_Replaced_%%I!
echo   Added files: %ADDED_COUNT%
for /L %%I in (1,1,%ADDED_COUNT%) do echo     !REPORT_Added_%%I!
echo   Preserved existing configs: %PRESERVED_COUNT%
for /L %%I in (1,1,%PRESERVED_COUNT%) do echo     !REPORT_Preserved_%%I!
echo.
exit /b 0

:wait_to_close
echo.
pause
exit /b %~1

:fail_no_work
call :wait_to_close 1

:fail
echo.
echo Install failed. If Pantheon is running, close it and try again.
if exist "%WORK%" rmdir /S /Q "%WORK%" >nul 2>nul
call :wait_to_close 1

:cleanup_success
if exist "%WORK%" rmdir /S /Q "%WORK%" >nul 2>nul
call :wait_to_close 0

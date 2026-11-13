@echo off
setlocal enabledelayedexpansion
title Parishram Hunt - Android APK Installer (ADB)

set "APK=%~dp0Builds\ParishramHunt.apk"
if not exist "%APK%" set "APK=%~dp0Builds\CoachingPropHunt.apk"
set "PKG=com.parishram.hunt"
set "LEGACY_PKG=com.coaching.prophunt"
set "ADB="

if exist "C:\Users\AAKASH\Unity\AndroidTools\SDK\platform-tools\adb.exe" (
    set "ADB=C:\Users\AAKASH\Unity\AndroidTools\SDK\platform-tools\adb.exe"
) else if exist "C:\Program Files\Unity 6000.3.28f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe" (
    set "ADB=C:\Program Files\Unity 6000.3.28f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe"
) else (
    for /f "delims=" %%i in ('where adb 2^>nul') do set "ADB=%%i"
)

if "%ADB%"=="" (
    echo [ERROR] adb.exe nahi mila!
    echo Check karein: C:\Users\AAKASH\Unity\AndroidTools\SDK\platform-tools\adb.exe
    pause
    exit /b 1
)

if not exist "%APK%" (
    echo [ERROR] APK file nahi mili: "%APK%"
    pause
    exit /b 1
)

echo =======================================================
echo   Parishram Hunt - Phone APK Installer
echo   APK: "%APK%"
echo   PKG: %PKG%
echo   ADB: "%ADB%"
echo =======================================================
echo.

"%ADB%" start-server >nul 2>&1
echo Connected Android devices check kar raha hoon...
"%ADB%" devices
echo.

set "HAS_DEVICE=0"
set "HAS_UNAUTH=0"
for /f "usebackq skip=1 tokens=1,2" %%a in (`""%ADB%" devices"`) do (
    if "%%b"=="device" set "HAS_DEVICE=1"
    if "%%b"=="unauthorized" set "HAS_UNAUTH=1"
)

if "%HAS_UNAUTH%"=="1" (
    echo [ACTION REQUIRED] Phone ki screen par "Allow USB debugging" popup ko Allow karein,
    echo phir is window mein kisi bhi key ko dabayein.
    pause
    set "HAS_DEVICE=0"
    for /f "usebackq skip=1 tokens=1,2" %%a in (`""%ADB%" devices"`) do (
        if "%%b"=="device" set "HAS_DEVICE=1"
    )
)

if "%HAS_DEVICE%"=="0" (
    echo [ERROR] Koi authorized Android device USB se connected nahi mila!
    echo  1. Phone ko USB cable se PC se lagayein.
    echo  2. Phone Settings ^> Developer Options ^> USB Debugging ON karein.
    echo  3. USB mode File Transfer MTP select karke retry karein.
    pause
    exit /b 1
)

"%ADB%" shell pm list packages %LEGACY_PKG% 2>nul | findstr /C:"%LEGACY_PKG%" >nul
if not errorlevel 1 (
    echo Purana app ^(%LEGACY_PKG%^) remove ho raha hai taaki naya icon/naam clean dikhe...
    "%ADB%" uninstall "%LEGACY_PKG%" >nul 2>&1
)

echo Phone par ParishramHunt.apk install ho raha hai...
"%ADB%" install -r "%APK%"
if errorlevel 1 (
    echo.
    echo [WARNING] Seedha update install fail hua - ho sakta hai purana signature conflict ho.
    choice /C YN /M "Kya purani app uninstall karke fresh install karein?"
    if errorlevel 2 (
        echo Install cancel kiya gaya.
        pause
        exit /b 1
    )
    "%ADB%" uninstall "%PKG%" >nul 2>&1
    "%ADB%" install "%APK%"
    if errorlevel 1 (
        echo [ERROR] APK install fail ho gaya! Upar error dekhein.
        pause
        exit /b 1
    )
)

echo.
echo [SUCCESS] Parishram Hunt phone par install ho gaya!
choice /C YN /T 8 /D Y /M "Kya game abhi phone par auto-launch karein?"
if not errorlevel 2 (
    "%ADB%" shell am start -n "%PKG%/com.unity3d.player.UnityPlayerGameActivity" >nul 2>&1
    if errorlevel 1 (
        "%ADB%" shell monkey -p "%PKG%" -c android.intent.category.LAUNCHER 1 >nul 2>&1
    )
)

echo Done!
pause
exit /b 0

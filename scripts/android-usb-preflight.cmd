@echo off
setlocal EnableExtensions EnableDelayedExpansion
chcp 65001 >nul
title MAX Desktop - USB Android preflight
where adb >nul 2>nul
if errorlevel 1 (
  echo ОШИБКА: команда adb не найдена. Установите Android SDK Platform-Tools.
  exit /b 2
)
set "SERIAL=%~1"
if not defined SERIAL (
  set /a COUNT=0
  for /f "skip=1 tokens=1,2" %%A in ('adb devices') do (
    if "%%B"=="device" (
      set /a COUNT+=1
      set "SERIAL=%%A"
    )
  )
  if not "!COUNT!"=="1" (
    echo ОШИБКА: нужен ровно один доступный Android.
    echo Укажите серийный номер аргументом: android-usb-preflight.cmd SERIAL
    adb devices
    exit /b 3
  )
)
adb -s "%SERIAL%" get-state 2>nul | findstr /X /C:"device" >nul
if errorlevel 1 (
  echo ОШИБКА: устройство недоступно для ADB.
  adb devices
  exit /b 4
)
echo.
echo ===== MAX Desktop: проверка USB / Android =====
echo Устройство ADB: %SERIAL%
for /f "delims=" %%V in ('adb -s "%SERIAL%" shell getprop ro.product.model') do echo Модель: %%V
for /f "delims=" %%V in ('adb -s "%SERIAL%" shell getprop ro.build.version.release') do echo Android: %%V
echo Приложение MAX:
adb -s "%SERIAL%" shell dumpsys package ru.oneme.app 2>nul | findstr /C:"versionName=" /C:"versionCode="
if errorlevel 1 (
  echo   Приложение ru.oneme.app не обнаружено.
  exit /b 5
)
set "MOBILE_DATA=неизвестно"
set "WIFI=неизвестно"
set "RADIO=неизвестно"
for /f "delims=" %%V in ('adb -s "%SERIAL%" shell settings get global mobile_data 2^>nul') do set "MOBILE_DATA=%%V"
for /f "delims=" %%V in ('adb -s "%SERIAL%" shell settings get global wifi_on 2^>nul') do set "WIFI=%%V"
for /f "delims=" %%V in ('adb -s "%SERIAL%" shell getprop gsm.network.type 2^>nul') do set "RADIO=%%V"
echo Признак разрешения мобильных данных: !MOBILE_DATA! [1=вкл, 0=выкл, null=нет сведений]
echo Признак включения Wi-Fi: !WIFI! [1=вкл, 0=выкл, null=нет сведений]
echo Тип мобильной сети по Android: !RADIO!
echo.
set "ROUNDTRIP=FAIL"
for /f "delims=" %%V in ('adb -s "%SERIAL%" shell echo MAX_USB_BRIDGE_SMOKE_OK 2^>nul') do (
  if "%%V"=="MAX_USB_BRIDGE_SMOKE_OK" set "ROUNDTRIP=PASS"
)
echo Проверка локального обмена тестовой строкой: !ROUNDTRIP!
if not "!ROUNDTRIP!"=="PASS" (
  echo ОШИБКА: тестовая строка не вернулась по USB.
  exit /b 6
)
echo.
echo ГОТОВО: USB работает. Настройки MAX, учётные записи и доверенная привязка не изменены.
echo ВНИМАНИЕ: мобильные данные и тип сети — индикаторы, не доказательство операторской Mobile ID.
exit /b 0

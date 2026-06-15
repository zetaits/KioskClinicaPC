@echo off
rem ============================================================================
rem  KioskClinicaPC - aplicador de actualizaciones (lo ejecuta la tarea SYSTEM).
rem  La app (usuario kiosko) ya descargo y VERIFICO (SHA256) el Setup en
rem  %ProgramData%\KioskClinicaPC\updates y dejo apply.flag. Aqui solo se aplica:
rem  cerrar kiosko -> instalar en silencio (upgrade in-place) -> reiniciar.
rem  El autostart (HKCU\Run) relanza el kiosko ya actualizado tras el reinicio.
rem
rem  Vive en %ProgramData% (no en Archivos de programa) y el instalador lo copia
rem  con flag onlyifdoesntexist: asi un upgrade NO lo sobrescribe mientras corre.
rem ============================================================================
setlocal
set "UPD=%ProgramData%\KioskClinicaPC\updates"
set "SETUP=%UPD%\Setup.exe"
set "FLAG=%UPD%\apply.flag"

if not exist "%FLAG%" exit /b 0
if not exist "%SETUP%" (
    del /f /q "%FLAG%" 2>nul
    exit /b 0
)

rem Consume el flag ANTES de instalar: si algo falla, no se reintenta en bucle.
del /f /q "%FLAG%" 2>nul

rem Cierra el kiosko para liberar los archivos de Archivos de programa.
taskkill /im KioskClinicaPC.exe /f >nul 2>&1

rem Instala en silencio. El Setup es admin; la tarea corre como SYSTEM (sin UAC).
rem /VERYSILENT salta el [Run] postinstall (skipifsilent) -> no se relanza en sesion 0.
"%SETUP%" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /NOCANCEL

del /f /q "%SETUP%" 2>nul

rem Reinicia siempre: el autostart relanza el kiosko (actualizado si fue OK, o el
rem anterior si fallo) en la sesion del usuario. Evita dejar la pantalla en negro.
shutdown /r /t 30 /c "Actualizacion de Kiosko Clinica PC. El equipo se reiniciara."
exit /b 0

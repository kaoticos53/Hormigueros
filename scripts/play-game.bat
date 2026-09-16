@echo off
REM ═══════════════════════════════════════════════════════════════════════════
REM  play-game.bat — Lanza Unity y abre el juego con la escena lista para Play.
REM
REM  USO:  play-game.bat
REM  CONTROLES: Click=inspeccionar · D=marcar drops · F=feromonas · I=importar
REM              J=saltar a alerta · Espacio=pausa · drag-drop=.antgenome
REM ═══════════════════════════════════════════════════════════════════════════
setlocal enabledelayedexpansion
set "ROOT=%~dp0.."
set "PROJECT=%ROOT%\src\App\AntSim.Unity"

echo.
echo  AntSim — Lanzar juego en Unity
echo ══════════════════════════════════════════════════════════════════════════

if not exist "%PROJECT%\Assets\Scripts\EditorTools\SceneBootstrapper.cs" (
    echo  X Proyecto Unity no encontrado
    exit /b 2
)

set "UNITY_BIN="

REM 1. Variable de entorno
if defined UNITY_CLI if exist "%UNITY_CLI%" (
    set "UNITY_BIN=%UNITY_CLI%"
    goto :found
)

REM 2. Buscar el editor real en Program Files (.getVersion.batstyle)
if exist "C:\Program Files\Unity\Hub\Editor\6000.6.0f1\Editor\Unity.exe" (
    set "UNITY_BIN=C:\Program Files\Unity\Hub\Editor\6000.6.0f1\Editor\Unity.exe"
    goto :found
)
if exist "C:\Program Files\Unity\Hub\Editor\6000.6.0f2\Editor\Unity.exe" (
    set "UNITY_BIN=C:\Program Files\Unity\Hub\Editor\6000.6.0f2\Editor\Unity.exe"
    goto :found
)
if exist "C:\Program Files\Unity\Hub\Editor\6000.5.0f1\Editor\Unity.exe" (
    set "UNITY_BIN=C:\Program Files\Unity\Hub\Editor\6000.5.0f1\Editor\Unity.exe"
    goto :found
)
if exist "C:\Program Files\Unity\Hub\Editor\6000.4.0f1\Editor\Unity.exe" (
    set "UNITY_BIN=C:\Program Files\Unity\Hub\Editor\6000.4.0f1\Editor\Unity.exe"
    goto :found
)
if exist "C:\Program Files\Unity\Hub\Editor\6000.3.0f1\Editor\Unity.exe" (
    set "UNITY_BIN=C:\Program Files\Unity\Hub\Editor\6000.3.0f1\Editor\Unity.exe"
    goto :found
)

REM 3. Buscar con dir /b (listar directorios de Hub\Editor)
for /f "tokens=*" %%d in ('dir /b /ad "C:\Program Files\Unity\Hub\Editor" 2^>nul') do (
    if exist "C:\Program Files\Unity\Hub\Editor\%%d\Editor\Unity.exe" (
        set "UNITY_BIN=C:\Program Files\Unity\Hub\Editor\%%d\Editor\Unity.exe"
        goto :found
    )
)

REM 4. LOCALAPPDATA
for /f "tokens=*" %%d in ('dir /b /ad "%LOCALAPPDATA%\Unity\Hub\Editor" 2^>nul') do (
    if exist "%LOCALAPPDATA%\Unity\Hub\Editor\%%d\Editor\Unity.exe" (
        set "UNITY_BIN=%LOCALAPPDATA%\Unity\Hub\Editor\%%d\Editor\Unity.exe"
        goto :found
    )
)

REM 5. Instalacion standalone
if exist "C:\Program Files\Unity\Editor\Unity.exe" (
    set "UNITY_BIN=C:\Program Files\Unity\Editor\Unity.exe"
    goto :found
)

REM 6. No usar el launcher del Hub: no acepta -projectPath como el Editor
REM    y deja al usuario creyendo que el juego se ha abierto correctamente.

echo  X No se encontro el Editor de Unity (no se usa el launcher del Hub)
echo    Instala Unity 6 desde https://unity.com/download
exit /b 3

:found
echo  Unity:    !UNITY_BIN!
echo  Proyecto: %PROJECT%
echo.
echo  Abriendo Unity...
start "" "!UNITY_BIN!" -projectPath "%PROJECT%"
echo.
echo  Cuando cargue el editor:
echo    1. Menu AntSim - Jugar
echo    2. Pulsa Play (triangle verde)
echo.
endlocal

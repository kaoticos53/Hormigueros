@echo off
REM ═══════════════════════════════════════════════════════════════════════════
REM  play-game.bat — Lanza Unity y abre el juego con la escena lista para Play.
REM
REM  USO:
REM    play-game.bat
REM
REM  Esto abre Unity con la escena de juego construida. Cuando el editor cargue,
REM  dale a Play para ver la simulacion en vivo.
REM
REM  CONTROLES EN JUEGO:
REM    Click     → inspeccionar hormiga (ver su cerebro)
REM    D         → modo marcar drops (click en suelo, Z deshace)
REM    F         → rotar capa de feromonas (home / food / alarm)
REM    G         → rotar colonia en la capa de feromonas
REM    I         → abrir dialogo de importar pool (.antgenome)
REM    J         → saltar camara a la alerta seleccionada
REM    Espacio   → pausar / reanudar
REM    Drag-drop → arrastrar .antgenome desde el explorador para importar
REM
REM  REQUISITO: Unity 6000.x instalado (con el Hub o standalone).
REM ═══════════════════════════════════════════════════════════════════════════
setlocal
set ROOT=%~dp0..
set PROJECT=%ROOT%\src\App\AntSim.Unity

echo ══════════════════════════════════════════════════════════════════════════
echo   AntSim — Lanzar juego en Unity
echo ══════════════════════════════════════════════════════════════════════════

REM Verificar que el proyecto existe
if not exist "%PROJECT%\Assets\Scripts\EditorTools\SceneBootstrapper.cs" (
    echo X Proyecto Unity no encontrado en %PROJECT%
    exit /b 2
)

REM Buscar Unity en rutas comunes
set UNITY_BIN=
if defined UNITY_CLI (
    if exist "%UNITY_CLI%" (
        set UNITY_BIN=%UNITY_CLI%
        goto :found
    )
)

REM Buscar en el Hub
for %%d in (
    "%ProgramFiles%\Unity\Hub\Editor\*"
    "%LOCALAPPDATA%\Unity\Hub\Editor\*"
) do (
    if exist "%%~d\Editor\Unity.exe" (
        set UNITY_BIN=%%~d\Editor\Unity.exe
        goto :found
    )
)

REM Buscar en la ruta del Hub comun
if exist "%LOCALAPPDATA%\Unity\bin\unity.exe" (
    set UNITY_BIN=%LOCALAPPDATA%\Unity\bin\unity.exe
    goto :found
)

REM Ultimo recurso: buscar en Program Files con wildcard
for /d %%d in "%ProgramFiles%\Unity\Hub\Editor\*" do (
    if exist "%%d\Editor\Unity.exe" (
        set UNITY_BIN=%%d\Editor\Unity.exe
        goto :found
    )
)

echo X No se encontro Unity CLI
echo.
echo   Opciones:
echo   1. Abre Unity Hub - Add - selecciona: %PROJECT%
echo   2. Anade Unity al PATH: set UNITY_CLI=C:\ruta\a\Unity.exe
echo   3. Abre el proyecto manualmente en el editor
echo.
echo   Una vez abierto el proyecto:
echo     Menu AntSim - Crear escena de juego (o AntSim - Jugar)
echo     Pulsa Play para ver la simulacion
echo.
exit /b 3

:found
echo Unity:  %UNITY_BIN%
echo Proyecto: %PROJECT%
echo.
echo Abriendo Unity... (puede tardar 30-60s la primera vez)
echo.

REM Abrir Unity con el proyecto
start "" "%UNITY_BIN%" -projectPath "%PROJECT%"

echo Unity lanzado. Sigue las instrucciones en el editor.
echo.
endlocal

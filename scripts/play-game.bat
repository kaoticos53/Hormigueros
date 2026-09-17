@echo off
REM ═══════════════════════════════════════════════════════════════════════════
REM  play-game.bat — lanzador Windows (delega en play-game.ps1).
REM
REM  La configuración se INYECTA en la escena antes de entrar en Play: grid y
REM  colonias dimensionan también suelo, cámara y marcadores de nido, así que
REM  ya no hay que tocar el inspector a mano.
REM
REM  USO:
REM    play-game.bat                                    warm-v2, grid 96, 2 colonias
REM    play-game.bat --pool pretrain-neat               otro pool
REM    play-game.bat --grid 256 --ticks 12000           mundo grande
REM    play-game.bat --species lasius,eciton            invasión
REM    play-game.bat --speed 6 --frame-every 2          más rápido, menos frames
REM    play-game.bat --dry-run                          muestra lo que aplicaría
REM
REM  Este bat NO parsea nada: pasa los argumentos CRUDOS (%*) al .ps1. Motivo:
REM  cmd parte los argumentos por comas al expandir %1..%9, así que un valor
REM  como `lasius,eciton` llegaba troceado («argumento no reconocido: eciton»).
REM
REM  CONTROLES: Click=inspeccionar · D=marcar drops · F/G=feromonas · I=importar
REM             J=saltar a alerta · Espacio=pausa · drag-drop=.antgenome
REM ═══════════════════════════════════════════════════════════════════════════
setlocal
set "ROOT=%~dp0.."
set "PROJECT=%ROOT%\src\App\AntSim.Unity"
set "PS1=%~dp0play-game.ps1"

if not exist "%PROJECT%\Assets\Scripts\EditorTools\SceneBootstrapper.cs" (
    echo  X Proyecto Unity no encontrado en %PROJECT%
    exit /b 2
)
if not exist "%PS1%" (
    echo  X No encuentro play-game.ps1 junto a este .bat
    exit /b 2
)

echo.
echo  AntSim — Lanzar juego en Unity
echo  ══════════════════════════════════════════════════════════════════════════
echo.

powershell -NoProfile -ExecutionPolicy Bypass -File "%PS1%" %*
set "RC=%ERRORLEVEL%"
if not "%RC%"=="0" (
    echo.
    echo  X El lanzador termino con codigo %RC%
    echo    Si el problema es la politica de ejecucion de PowerShell:
    echo      Set-ExecutionPolicy -Scope Process Bypass
)
endlocal & exit /b %RC%

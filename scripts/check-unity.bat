@echo off
REM ═══════════════════════════════════════════════════════════════════════════
REM  check-unity.bat — lanzador Windows de la puerta LOCAL de la capa de vista.
REM
REM  Ejecuta el MISMO script que el job `unity-compile` de CI
REM  (scripts/check-unity-compile.sh), con el editor licenciado de esta máquina.
REM  La suite headless NO compila los MonoBehaviours, así que un `error CS` en
REM  la capa de vista solo se ve aquí: este es el comando que lo ve.
REM
REM  USO:
REM    check-unity.bat                  comprueba el proyecto (segundos con la
REM                                     Library caliente; minutos si está fría)
REM    check-unity.bat --selftest       verifica el analizador, SIN arrancar Unity
REM    check-unity.bat --log FICHERO    solo analiza un log ya hecho
REM    check-unity.bat --fixture F      veredicto completo sobre un log falso
REM    check-unity.bat --install-hook   activa el hook pre-push (.githooks/)
REM
REM  CÓDIGOS: 0 ok · 1 errores CS · 2 uso o proyecto inválido · 3 editor ausente ·
REM           4 el editor no arrancó (licencia) · 5 la instancia no llegó a compilar
REM
REM  Necesita `bash` en el PATH: viene con Git for Windows (Git Bash).
REM ═══════════════════════════════════════════════════════════════════════════
setlocal
set "HERE=%~dp0"
set "SH=%HERE%check-unity-compile.sh"

REM  Sin `goto`ni etiquetas a propósito: este .bat viaja con fin de línea LF (como
REM  el resto del repo) y cmd es quisquilloso con las etiquetas en ese formato.
REM  Los argumentos crudos (%*) sí se pasan al .sh; aquí solo se intercepta
REM  --install-hook, que delega en install-hooks.sh.
if /I "%~1"=="--install-hook" (
    bash "%HERE%install-hooks.sh" %2 %3
    if errorlevel 1 (endlocal & exit /b 1)
    endlocal & exit /b 0
)

where bash >nul 2>&1
if errorlevel 1 (
    echo  X No encuentro bash en el PATH.
    echo    Este lanzador necesita Git Bash ^(Git for Windows^). Alternativas:
    echo      - abrir el proyecto en Unity y mirar la consola, o
    echo      - ejecutarlo desde Git Bash: bash scripts/check-unity-compile.sh
    endlocal & exit /b 3
)
if not exist "%SH%" (
    echo  X No encuentro check-unity-compile.sh junto a este .bat
    endlocal & exit /b 2
)

echo.
echo  AntSim — comprobar que la capa de vista compila
echo  ══════════════════════════════════════════════════════════════════════════
bash "%SH%" %*
set "RC=%ERRORLEVEL%"
if "%RC%"=="1" (
    echo.
    echo  X La capa de vista NO compila: hay errores CS arriba.
    echo    Eso es justo la rotura que la suite headless no ve.
    echo    Detalle completo en el log que el script imprime arriba.
)
endlocal & exit /b %RC%

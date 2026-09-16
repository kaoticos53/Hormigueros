# ═══════════════════════════════════════════════════════════════════════════
# play-game.ps1 — PowerShell: abre Unity con el juego listo para Play.
#
# USO:
#   .\scripts\play-game.ps1                    # Pool warm-v2 por defecto
#   .\scripts\play-game.ps1 -Pool pretrain-neat # Pool NEAT
#   .\scripts\play-game.ps1 -Grid 256 -Species "lasius,eciton"
#
# CONTROLES EN JUEGO:
#   Click     → inspeccionar hormiga (ver su cerebro)
#   D         → modo marcar drops (Z deshace)
#   F         → rotar capa de feromonas
#   I         → importar pool (.antgenome)
#   J         → saltar a la alerta
#   Espacio   → pausar
# ═══════════════════════════════════════════════════════════════════════════

param(
    [string]$Pool = "pretrain-warm-v2",
    [int]$Grid = 96,
    [int]$Colonies = 2,
    [int]$Ticks = 7200,
    [string]$Species = "lasius",
    [int]$Speed = 3
)

$ErrorActionPreference = "Stop"
$Root = Split-Path $PSScriptRoot -Parent
$Project = Join-Path $Root "src\App\AntSim.Unity"

Write-Host ""
Write-Host "  AntSim — Lanzar juego en Unity" -ForegroundColor Cyan
Write-Host "  ═══════════════════════════════════════════════════════════════" -ForegroundColor DarkGray

# ── Verificar proyecto ────────────────────────────────────────────────────
$bootstrapper = Join-Path $Project "Assets\Scripts\EditorTools\SceneBootstrapper.cs"
if (-not (Test-Path $bootstrapper)) {
    Write-Host "  X Proyecto Unity no encontrado en $Project" -ForegroundColor Red
    exit 2
}

# ── Resolver pool ─────────────────────────────────────────────────────────
$PoolPath = ""
if ($Pool -match "[/\\]|\.antgenome$") {
    # Ruta absoluta o relativa
    $PoolPath = if ([System.IO.Path]::IsPathRooted($Pool)) { $Pool } else { Join-Path $Root $Pool }
} else {
    # Nombre de pool: buscar en artifacts/ y tests/fixtures/
    $candidates = @(
        (Join-Path $Root "artifacts\$Pool.antgenome"),
        (Join-Path $Root "artifacts\$Pool"),
        (Join-Path $Root "tests\fixtures\$Pool.antgenome")
    )
    foreach ($c in $candidates) {
        if (Test-Path $c) { $PoolPath = $c; break }
    }
}

if (-not $PoolPath -or -not (Test-Path $PoolPath)) {
    Write-Host "  X Pool no encontrado: $Pool" -ForegroundColor Red
    Write-Host "    Pools disponibles:" -ForegroundColor Yellow
    Get-ChildItem (Join-Path $Root "artifacts\*.antgenome") -ErrorAction SilentlyContinue |
        ForEach-Object { Write-Host "      $($_.BaseName)" }
    exit 2
}

# ── Buscar Unity Editor ──────────────────────────────────────────────────
$UnityBin = $null

# 1. Variable de entorno
if ($env:UNITY_CLI -and (Test-Path $env:UNITY_CLI)) {
    $UnityBin = $env:UNITY_CLI
}

# 2. Unity Hub Editor (versiones conocidas)
if (-not $UnityBin) {
    $hubPaths = @(
        "$env:ProgramFiles\Unity\Hub\Editor",
        "$env:LOCALAPPDATA\Unity\Hub\Editor"
    )
    foreach ($hub in $hubPaths) {
        if (Test-Path $hub) {
            $versions = Get-ChildItem $hub -Directory | Sort-Object Name -Descending
            foreach ($v in $versions) {
                $editor = Join-Path $v.FullName "Editor\Unity.exe"
                if (Test-Path $editor) { $UnityBin = $editor; break }
            }
        }
        if ($UnityBin) { break }
    }
}

# 3. Instalacion standalone
if (-not $UnityBin) {
    $standalone = "C:\Program Files\Unity\Editor\Unity.exe"
    if (Test-Path $standalone) { $UnityBin = $standalone }
}

# 4. Launcher del Hub
if (-not $UnityBin) {
    $launcher = "$env:LOCALAPPDATA\Unity\bin\unity.exe"
    if (Test-Path $launcher) {
        $UnityBin = $launcher
        Write-Host "  (Usando launcher del Hub)" -ForegroundColor Yellow
    }
}

if (-not $UnityBin) {
    Write-Host "  X No se encontro Unity" -ForegroundColor Red
    Write-Host "    Descarga desde: https://unity.com/download" -ForegroundColor Yellow
    exit 3
}

# ── Mostrar configuracion ─────────────────────────────────────────────────
Write-Host ""
Write-Host "  Unity:    $UnityBin" -ForegroundColor Green
Write-Host "  Proyecto: $Project"
Write-Host "  Pool:     $(Split-Path $PoolPath -Leaf)"
Write-Host "  Grid:     $Grid ($($Grid * 8) u)"
Write-Host "  Colonias: $Colonies"
Write-Host "  Ticks:    $Ticks"
Write-Host "  Species:  $Species"
Write-Host "  Velocidad: ${Speed}x"
Write-Host ""

# ── Compilar el CLI si no existe ──────────────────────────────────────────
$cliDir = Join-Path $Root "build\antsim"
$cliExe = Join-Path $cliDir "antsim.exe"
if (-not (Test-Path $cliExe)) {
    Write-Host "  Compilando el CLI..." -ForegroundColor Yellow
    Push-Location $Root
    dotnet publish "src\Tools\AntSim.Cli" -c Release -o $cliDir 2>&1 | Out-Null
    Pop-Location
    if (-not (Test-Path $cliExe)) {
        Write-Host "  X No se pudo compilar el CLI" -ForegroundColor Red
        exit 1
    }
    Write-Host "  CLI compilado: $cliExe" -ForegroundColor Green
}

# ── Lanzar Unity ──────────────────────────────────────────────────────────
Write-Host "  Abriendo Unity..." -ForegroundColor Cyan
Write-Host ""

# Abrir Unity con el proyecto (sin -batchmode para que se abra el editor)
Start-Process -FilePath $UnityBin -ArgumentList "-projectPath `"$Project`""

Write-Host ""
Write-Host "  Unity esta abriendo. Cuando cargue el editor:" -ForegroundColor Green
Write-Host "    1. Menu AntSim → Crear escena de juego (o AntSim → Jugar)"
Write-Host "    2. En el inspector de SimPresenter, configura el pool si quieres"
Write-Host "    3. Pulsa Play (triangle verde)"
Write-Host ""
Write-Host "  Controles: Click=inspeccionar · D=marcar · F=feromonas · I=importar"
Write-Host ""

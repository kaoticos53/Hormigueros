# ===========================================================================
# play-game.ps1 - PowerShell: abre Unity con el juego YA configurado y en Play.
#
# La configuracion NO se teclea en el inspector: este script la escribe en un
# archivo y Unity la aplica al montar la escena (entrada del lanzador del
# bootstrapper), con geometria incluida - grid y colonias dimensionan tambien
# el suelo, la camara y los marcadores de nido.
#
# USO:
#   .\scripts\play-game.ps1                          pool por defecto, grid 96, 2 colonias
#   .\scripts\play-game.ps1 --pool pretrain-neat     otro pool
#   .\scripts\play-game.ps1 --grid 256 --ticks 12000 mundo grande
#   .\scripts\play-game.ps1 --species lasius,eciton  invasion
#   .\scripts\play-game.ps1 --speed 60                mas rapido
#   .\scripts\play-game.ps1 --clusters 2 --cluster-size 12:16   montones grandes
#   .\scripts\play-game.ps1 --trail-half-life 60 --trail-follow 0   rastro efimero y ciego
#   .\scripts\play-game.ps1 --pool pretrain-partida-200-gl-red --trail-by-brain --trail-gland 25 --trail-gland-refill 6
#                                                    EL PAR de la puerta (F11n)
#   .\scripts\play-game.ps1 --dry-run                solo muestra lo que aplica
#
# PERILLAS DEL RECLUTAMIENTO (F8): --clusters, --cluster-size MIN:MAX,
# --cluster-radius, --trail-deposit, --trail-half-life y --trail-follow dejan
# comparar configuraciones sin tocar codigo. Los defaults son los del mundo; un
# valor distinto viaja al CLI y el mundo lo aplica antes de sembrar la comida.
#
# NOTA: este archivo es ASCII puro a proposito. PowerShell 5.1 lee los .ps1
# como ANSI/Windows-1252 si no llevan BOM, y un guion largo o una tilde
# rompian el parseo con "Token inesperado".
#
# CONTROLES EN JUEGO:
#   Click     -> inspeccionar hormiga (ver su cerebro)
#   D         -> modo marcar drops (Z deshace)
#   F / G     -> capa de feromonas / colonia
#   I         -> importar pool (.antgenome)
#   J         -> saltar a la alerta
#   Espacio   -> pausar
# ===========================================================================

param(
    [string]$Pool = "pretrain-maze-los3",
    [int]$Grid = 96,
    [int]$Colonies = 2,
    [int]$Ticks = 216000,
    [string]$Species = "lasius",
    [float]$Speed = 120,
    [int]$FrameEvery = 1,
    # F8: perillas del reclutamiento (montones de comida y rastro de feromonas).
    [int]$Clusters = 8,
    [string]$ClusterSize = "6:10",
    [float]$ClusterRadius = 18,
    [float]$TrailDeposit = 1,
    [float]$TrailHalfLife = 2100,
    [float]$TrailFollow = 8,
    # F11n: el PAR de la puerta (puerta del cerebro + glandula con precio).
    # Apagado en el default: sin banderas la partida es la de fabrica.
    [switch]$TrailByBrain,
    [float]$TrailGland = 0,
    [float]$TrailGlandRefill = 3,
    [switch]$NoRecruitment,
    [switch]$DryRun,
    # play-game.bat delega el texto CRUDO (%*): cmd parte los argumentos por
    # comas al expandir %1..%9, asi que un valor como "lasius,eciton" llegaba
    # troceado. Aqui se parsean las banderas --clave valor sin esa trampa.
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$Flags
)

$script:DryRunFlag = $false

# ---- Banderas --clave valor / --clave=valor (lo que usa el .bat) ----------
# Un parametro con nombre explicito (-Grid 256) siempre gana sobre la bandera.
function Set-FromFlags {
    if (-not $Flags -or $Flags.Count -eq 0) { return }
    $i = 0
    while ($i -lt $Flags.Count) {
        $tok = $Flags[$i]
        if (-not $tok.StartsWith("--")) {
            Write-Host "  X argumento no reconocido: $tok" -ForegroundColor Red
            Write-Host "    Opciones: --pool --grid --colonies --ticks --species --speed --frame-every --no-recruitment --dry-run --help"
            exit 2
        }
        $body = $tok.Substring(2)
        $key = $body
        $val = $null
        $eq = $body.IndexOf('=')
        if ($eq -ge 0) {
            $key = $body.Substring(0, $eq)
            $val = $body.Substring($eq + 1)
        }
        $key = $key.ToLowerInvariant()

        if ($key -eq 'dry-run') { $script:DryRunFlag = $true; $i++; continue }
        if ($key -eq 'trail-by-brain') { $script:TrailByBrain = $true; $i++; continue }
        if ($key -eq 'no-recruitment') { $script:RecruitmentOff = $true; $i++; continue }
        if ($key -eq 'help' -or $key -eq 'h') { $script:HelpFlag = $true; $i++; continue }

        if ($null -eq $val) {
            if ($i + 1 -ge $Flags.Count) {
                Write-Host "  X falta el valor de --$key" -ForegroundColor Red
                exit 2
            }
            $val = $Flags[$i + 1]
            $i += 2
        } else {
            $i++
        }

        switch ($key) {
            'pool'        { if (-not $PSBoundParameters.ContainsKey('Pool'))       { $script:Pool = $val } }
            'grid'        { if (-not $PSBoundParameters.ContainsKey('Grid'))       { $script:Grid = [int]$val } }
            'colonies'    { if (-not $PSBoundParameters.ContainsKey('Colonies'))   { $script:Colonies = [int]$val } }
            'ticks'       { if (-not $PSBoundParameters.ContainsKey('Ticks'))      { $script:Ticks = [int]$val } }
            'species'     { if (-not $PSBoundParameters.ContainsKey('Species'))    { $script:Species = $val } }
            'speed'       { if (-not $PSBoundParameters.ContainsKey('Speed'))      { $script:Speed = [float]$val } }
            'frame-every' { if (-not $PSBoundParameters.ContainsKey('FrameEvery')) { $script:FrameEvery = [int]$val } }
            'clusters'       { if (-not $PSBoundParameters.ContainsKey('Clusters'))       { $script:Clusters = [int]$val } }
            'cluster-size'   { if (-not $PSBoundParameters.ContainsKey('ClusterSize'))    { $script:ClusterSize = $val } }
            'cluster-radius' { if (-not $PSBoundParameters.ContainsKey('ClusterRadius'))  { $script:ClusterRadius = [float]$val } }
            'trail-deposit'  { if (-not $PSBoundParameters.ContainsKey('TrailDeposit'))   { $script:TrailDeposit = [float]$val } }
            'trail-half-life'{ if (-not $PSBoundParameters.ContainsKey('TrailHalfLife'))  { $script:TrailHalfLife = [float]$val } }
            'trail-follow'   { if (-not $PSBoundParameters.ContainsKey('TrailFollow'))    { $script:TrailFollow = [float]$val } }
            'trail-gland'    { if (-not $PSBoundParameters.ContainsKey('TrailGland'))     { $script:TrailGland = [float]$val } }
            'trail-gland-refill' { if (-not $PSBoundParameters.ContainsKey('TrailGlandRefill')) { $script:TrailGlandRefill = [float]$val } }
            default {
                Write-Host "  X opcion desconocida: --$key" -ForegroundColor Red
                Write-Host "    Opciones: --pool --grid --colonies --ticks --species --speed --frame-every"
                Write-Host "              --clusters N --cluster-size MIN:MAX --cluster-radius U"
                Write-Host "              --trail-deposit F --trail-half-life S --trail-follow G"
                Write-Host "              --trail-by-brain --trail-gland S --trail-gland-refill S"
                Write-Host "              --no-recruitment --dry-run --help"
                exit 2
            }
        }
    }
}

$script:HelpFlag = $false
$script:RecruitmentOff = $false
Set-FromFlags
if ($DryRunFlag) { $DryRun = $true }
if ($NoRecruitment) { $script:RecruitmentOff = $true }
$Recruitment = -not $script:RecruitmentOff

if ($HelpFlag) {
    Write-Host ""
    Write-Host "  play-game.ps1 [--pool nombre] [--grid 96|256] [--colonies 1|2]"
    Write-Host "                [--ticks N] [--species lasius|atta|eciton] [--speed 10-160]"
    Write-Host "                [--frame-every N] [--no-recruitment] [--dry-run]"
    Write-Host "                [--clusters N] [--cluster-size MIN:MAX] [--cluster-radius U]"
    Write-Host "                [--trail-deposit F] [--trail-half-life S] [--trail-follow G]"
    Write-Host "                [--trail-by-brain] [--trail-gland S] [--trail-gland-refill S]"
    Write-Host ""
    Write-Host "  Reclutamiento (F8) - default entre parentesis:"
    Write-Host "    --clusters N          montones de comida que siembra el mundo (8)"
    Write-Host "    --cluster-size MIN:MAX  items por monton (6:10)"
    Write-Host "    --cluster-radius U    radio del monton en unidades (18)"
    Write-Host "    --trail-deposit F     rastro de comida de la cargada, x QMaxFood/s (1)"
    Write-Host "    --trail-half-life S   vida media del rastro en segundos (2100 = 7x la clasica)"
    Write-Host "    --trail-follow G      giro que provoca el rastro, rad/s (8; 0 = no lo sigue)"
    Write-Host ""
    Write-Host "  El par de la puerta (F11n) - default entre parentesis:"
    Write-Host "    --trail-by-brain        el cerebro abre el deposito (off = reflejo fijo)"
    Write-Host "    --trail-gland S         precio del rastro: segundos de glandula (0 = gratis)"
    Write-Host "    --trail-gland-refill S  segundos de vacia a llena en el nido (3)"
    Write-Host ""
    Write-Host "  Ejemplos:"
    Write-Host "    .\scripts\play-game.ps1"
    Write-Host "    .\scripts\play-game.ps1 --pool pretrain-neat --grid 256 --ticks 12000"
    Write-Host "    .\scripts\play-game.ps1 --clusters 2 --cluster-size 12:16 --cluster-radius 30"
    Write-Host "    .\scripts\play-game.ps1 --trail-half-life 60 --trail-follow 0"
    Write-Host "    .\scripts\play-game.ps1 --pool pretrain-partida-200-gl-red --trail-by-brain --trail-gland 25 --trail-gland-refill 6"
    Write-Host ""
    exit 0
}

# Variables de entorno (compatibilidad con lanzamientos previos).
if (-not $PSBoundParameters.ContainsKey('Pool')       -and $env:ANTSIM_POOL)        { $Pool = $env:ANTSIM_POOL }
if (-not $PSBoundParameters.ContainsKey('Grid')       -and $env:ANTSIM_GRID)        { $Grid = [int]$env:ANTSIM_GRID }
if (-not $PSBoundParameters.ContainsKey('Colonies')   -and $env:ANTSIM_COLONIES)    { $Colonies = [int]$env:ANTSIM_COLONIES }
if (-not $PSBoundParameters.ContainsKey('Ticks')      -and $env:ANTSIM_TICKS)       { $Ticks = [int]$env:ANTSIM_TICKS }
if (-not $PSBoundParameters.ContainsKey('Species')    -and $env:ANTSIM_SPECIES)     { $Species = $env:ANTSIM_SPECIES }
if (-not $PSBoundParameters.ContainsKey('Speed')      -and $env:ANTSIM_SPEED)       { $Speed = [float]$env:ANTSIM_SPEED }
if (-not $PSBoundParameters.ContainsKey('FrameEvery') -and $env:ANTSIM_FRAME_EVERY) { $FrameEvery = [int]$env:ANTSIM_FRAME_EVERY }
if ($env:ANTSIM_DRYRUN -eq "1") { $DryRun = $true }

$ErrorActionPreference = "Stop"
$Root = Split-Path $PSScriptRoot -Parent
$Project = Join-Path $Root "src\App\AntSim.Unity"

Write-Host ""
Write-Host "  AntSim - Lanzar juego en Unity" -ForegroundColor Cyan
Write-Host "  ==================================================================" -ForegroundColor DarkGray

# ---- Verificar proyecto ---------------------------------------------------
$bootstrapper = Join-Path $Project "Assets\Scripts\EditorTools\SceneBootstrapper.cs"
if (-not (Test-Path $bootstrapper)) {
    Write-Host "  X Proyecto Unity no encontrado en $Project" -ForegroundColor Red
    exit 2
}

# ---- Validacion temprana (las mismas reglas que aplica Unity) -------------
if ($Grid -lt 16 -or $Grid -gt 1024)      { Write-Host "  X --grid fuera de rango [16, 1024]: $Grid" -ForegroundColor Red; exit 2 }
if ($Colonies -lt 1 -or $Colonies -gt 2)  { Write-Host "  X --colonies fuera de rango [1, 2]: $Colonies" -ForegroundColor Red; exit 2 }
if ($Ticks -lt 30 -or $Ticks -gt 4000000) { Write-Host "  X --ticks fuera de rango [30, 4000000]: $Ticks" -ForegroundColor Red; exit 2 }
if ($Speed -lt 10 -or $Speed -gt 160)      { Write-Host "  X --speed fuera de rango [10, 160]: $Speed" -ForegroundColor Red; exit 2 }
if ($FrameEvery -lt 1 -or $FrameEvery -gt 60) { Write-Host "  X --frame-every fuera de rango [1, 60]: $FrameEvery" -ForegroundColor Red; exit 2 }

# F8: montones y rastro. Mismos rangos que RecruitmentTuning (la tabla unica
# que comparten el CLI, el lanzador de bash y el mundo).
if ($Clusters -lt 0 -or $Clusters -gt 64) { Write-Host "  X --clusters fuera de rango [0, 64]: $Clusters" -ForegroundColor Red; exit 2 }
if ($ClusterSize -notmatch '^[0-9]+:[0-9]+$') {
    Write-Host "  X --cluster-size debe ser MIN:MAX (p. ej. 6:10): '$ClusterSize'" -ForegroundColor Red
    exit 2
}
$sizeMin, $sizeMax = $ClusterSize.Split(':')
if ([int]$sizeMin -lt 1 -or [int]$sizeMin -gt 40) { Write-Host "  X --cluster-size minimo fuera de rango [1, 40]: $sizeMin" -ForegroundColor Red; exit 2 }
if ([int]$sizeMax -lt [int]$sizeMin -or [int]$sizeMax -gt 40) { Write-Host "  X --cluster-size maximo fuera de rango [$sizeMin, 40]: $sizeMax" -ForegroundColor Red; exit 2 }
if ($ClusterRadius -lt 1 -or $ClusterRadius -gt 120) { Write-Host "  X --cluster-radius fuera de rango [1, 120] u: $ClusterRadius" -ForegroundColor Red; exit 2 }
if ($TrailDeposit -lt 0 -or $TrailDeposit -gt 5) { Write-Host "  X --trail-deposit fuera de rango [0, 5]: $TrailDeposit" -ForegroundColor Red; exit 2 }
if ($TrailHalfLife -lt 1 -or $TrailHalfLife -gt 3600) { Write-Host "  X --trail-half-life fuera de rango [1, 3600] s: $TrailHalfLife" -ForegroundColor Red; exit 2 }
if ($TrailFollow -lt 0 -or $TrailFollow -gt 20) { Write-Host "  X --trail-follow fuera de rango [0, 20] rad/s: $TrailFollow" -ForegroundColor Red; exit 2 }
# F11n: la puerta y su precio. Los mismos rangos que RecruitmentTuning.
if ($TrailGland -lt 0 -or $TrailGland -gt 120) { Write-Host "  X --trail-gland fuera de rango [0, 120] s (0 = sin precio): $TrailGland" -ForegroundColor Red; exit 2 }
if ($TrailGlandRefill -lt 0.05 -or $TrailGlandRefill -gt 60) { Write-Host "  X --trail-gland-refill fuera de rango [0.05, 60] s: $TrailGlandRefill" -ForegroundColor Red; exit 2 }
if (-not $Species -or $Species -notmatch '^[A-Za-z]+(,[A-Za-z]+)*$') {
    Write-Host "  X --species invalida: '$Species' (p. ej. lasius o lasius,eciton)" -ForegroundColor Red
    exit 2
}

# ---- Resolver pool --------------------------------------------------------
$PoolPath = ""
if ($Pool -match "[/\\]|\.antgenome$") {
    $PoolPath = if ([System.IO.Path]::IsPathRooted($Pool)) { $Pool } else { Join-Path $Root $Pool }
} else {
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
$PoolPath = (Resolve-Path $PoolPath).Path

# ---- Archivo de configuracion que Unity leera al arrancar -----------------
$SettingsFile = Join-Path $env:TEMP "antsim-launch.txt"
$speedText = $Speed.ToString([System.Globalization.CultureInfo]::InvariantCulture)
$lines = @(
    "# Generado por scripts/play-game.ps1; lo aplica SceneBootstrapper.PlayFromLaunchSettings",
    "pool=$Pool",
    "poolPath=$PoolPath",
    "grid=$Grid",
    "maze=1",
    "recruitment=$(if ($Recruitment) { 1 } else { 0 })",
    "colonies=$Colonies",
    "ticks=$Ticks",
    "species=$Species",
    "speed=$speedText",
    "frameEvery=$FrameEvery",
    "clusters=$Clusters",
    "clusterSize=$ClusterSize",
    "clusterRadius=$($ClusterRadius.ToString([System.Globalization.CultureInfo]::InvariantCulture))",
    "trailDeposit=$($TrailDeposit.ToString([System.Globalization.CultureInfo]::InvariantCulture))",
    "trailHalfLife=$($TrailHalfLife.ToString([System.Globalization.CultureInfo]::InvariantCulture))",
    "trailFollow=$($TrailFollow.ToString([System.Globalization.CultureInfo]::InvariantCulture))",
    "trailByBrain=$(if ($TrailByBrain) { 1 } else { 0 })",
    "trailGland=$($TrailGland.ToString([System.Globalization.CultureInfo]::InvariantCulture))",
    "trailGlandRefill=$($TrailGlandRefill.ToString([System.Globalization.CultureInfo]::InvariantCulture))"
)
Set-Content -Path $SettingsFile -Value $lines -Encoding ASCII

# ---- Buscar Unity Editor --------------------------------------------------
$UnityBin = $null

if ($env:UNITY_CLI -and (Test-Path $env:UNITY_CLI)) {
    $UnityBin = $env:UNITY_CLI
}

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

if (-not $UnityBin) {
    $standalone = "C:\Program Files\Unity\Editor\Unity.exe"
    if (Test-Path $standalone) { $UnityBin = $standalone }
}

# No usar el launcher del Hub: no es un Editor valido para -projectPath.
if (-not $UnityBin) {
    Write-Host "  X No se encontro el Editor de Unity (no se usa el launcher del Hub)" -ForegroundColor Red
    Write-Host "    Descarga desde: https://unity.com/download" -ForegroundColor Yellow
    exit 3
}

# ---- Compilar el CLI si no existe ----------------------------------------
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

# ---- Mostrar configuracion ------------------------------------------------
$unityArgs = "-projectPath `"$Project`" " +
             "-executeMethod AntSim.Unity.Scripts.EditorTools.SceneBootstrapper.PlayFromLaunchSettings " +
             "-antsimSettings `"$SettingsFile`""

Write-Host ""
Write-Host "  Unity:        $UnityBin" -ForegroundColor Green
Write-Host "  Proyecto:     $Project"
Write-Host "  Pool:         $(Split-Path $PoolPath -Leaf)"
Write-Host "  Grid:         $Grid ($($Grid * 8) u)"
Write-Host "  Colonias:     $Colonies"
Write-Host "  Ticks:        $Ticks"
Write-Host "  Especie:      $Species"
Write-Host "  Velocidad:    $($speedText)x"
Write-Host "  Frame every:  $FrameEvery"
if ($Recruitment) {
    Write-Host "  Montones:     $Clusters ($sizeMin-$sizeMax items, radio $ClusterRadius u)"
    Write-Host "  Rastro:       $TrailDeposit x QMaxFood/s - vida media $TrailHalfLife s - sigue $TrailFollow rad/s"
    if ($TrailByBrain -or $TrailGland -ne 0) {
        $gate = if ($TrailByBrain) { "si (el cerebro abre el deposito)" } else { "no (reflejo fijo)" }
        Write-Host "  Puerta:       $gate - glandula $TrailGland s (recarga $TrailGlandRefill s)"
    }
} else {
    Write-Host "  Montones:     (reclutamiento apagado)"
}
Write-Host "  Ajustes:      $SettingsFile"
Write-Host ""

if ($DryRun) {
    Write-Host "  --dry-run: NO se lanza Unity. Comando que se ejecutaria:" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "    `"$UnityBin`" $unityArgs" -ForegroundColor DarkGray
    Write-Host ""
    exit 0
}

# ---- Aviso si el proyecto ya esta abierto ---------------------------------
# Unity rechaza una segunda instancia del MISMO proyecto, asi que el
# -executeMethod no llegaria a correr y el usuario veria su editor de siempre
# sin los ajustes nuevos.
$running = (Get-Process Unity -ErrorAction SilentlyContinue |
            Where-Object { $_.MainWindowTitle -ne "" }).Count
if ($running -gt 0) {
    Write-Host "  ! Hay una instancia de Unity abierta: si es ESTE proyecto, cierrala" -ForegroundColor Yellow
    Write-Host "    antes de lanzar (Unity no abre dos veces el mismo proyecto y los" -ForegroundColor Yellow
    Write-Host "    ajustes no se aplicarian)." -ForegroundColor Yellow
    Write-Host ""
}

# ---- Lanzar Unity con la entrada del lanzador -----------------------------
Write-Host "  Abriendo Unity y aplicando la configuracion..." -ForegroundColor Cyan
Start-Process -FilePath $UnityBin -ArgumentList $unityArgs

Write-Host ""
Write-Host "  Unity esta abriendo. Entrara en Play solo, con estos ajustes aplicados." -ForegroundColor Green
Write-Host "  (En la consola del editor veras 'Lanzador -> pool=... grid=...' al arrancar.)"
Write-Host ""
Write-Host "  Controles: Click=inspeccionar - D=marcar - F/G=feromonas - I=importar"
Write-Host ""

# Ejecutar AntSim en Windows

## Requisitos

- Unity **6000.6.0f1** (o una versión Unity 6 compatible), instalado mediante
  Unity Hub.
- .NET SDK para compilar el CLI.
- El repositorio abierto desde su raíz; el proyecto Unity real es
  `src/App/AntSim.Unity`, no la raíz del repositorio.

## Arranque recomendado

Desde PowerShell, situado en la raíz del repositorio:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\scripts\play-game.ps1                       # warm-v2, grid 96, 2 colonias
.\scripts\play-game.ps1 --pool pretrain-neat  # otro pool
.\scripts\play-game.ps1 --grid 256 --ticks 12000
.\scripts\play-game.ps1 --species lasius,eciton --speed 6
.\scripts\play-game.ps1 --dry-run             # solo muestra lo que aplicaría
```

También se puede hacer doble clic en `scripts\play-game.bat` (acepta las mismas
banderas: `play-game.bat --grid 256 --species lasius,eciton`). El script abre el
**Editor de Unity**, no `Unity Hub`, y no debe abrirse una segunda instancia del
mismo proyecto.

### Qué se inyecta en la escena

Los ajustes **no** se teclean en el inspector: el lanzador los escribe en un
archivo temporal y Unity los aplica al montar la escena, entrando en Play solo
(`-executeMethod …SceneBootstrapper.PlayFromLaunchSettings`). Grid y colonias
dimensionan también la geometría — suelo, cámara, marco y un marcador de nido
por colonia — así que un mundo de 256² se encuadra bien (antes el asistente
ajustaba el grid del presenter *después* de montar la escena con 96, y el mundo
quedaba 8× fuera de cámara).

| Bandera | Default | Rango |
|---|---|---|
| `--pool` | `pretrain-warm-v2` | nombre en `artifacts/` o ruta a un `.antgenome` |
| `--grid` | `96` | `16`–`1024` (el mundo mide grid × 8 u) |
| `--colonies` | `2` | `1`–`2` (la escena monta dos tarjetas) |
| `--ticks` | `36000` | `30`–`4000000` |
| `--species` | `lasius` | `lasius`, `atta`, `eciton` o combinaciones con coma |
| `--speed` | `3` | `1`–`100` |
| `--frame-every` | `1` | `1`–`60` |
| `--dry-run` | — | imprime los ajustes y el comando, sin abrir Unity |

La consola del editor imprime `Lanzador -> pool=… grid=…` al entrar; si algo no
cuadra, ahí se ve qué se aplicó de verdad.

Si Unity **ya está abierto con este proyecto**, ciérralo antes: Unity no abre
dos veces el mismo proyecto y el lanzamiento no aplicaría nada (el script lo
avisa). Con Unity abierto, la vía manual es:

1. Esperar a que termine la compilación de scripts.
2. Ejecutar `AntSim > Asistente de primera ejecucion`.
3. Elegir pool, especie y grid.
4. Pulsar `Crear escena y Jugar` (el asistente pasa por el mismo camino que el
   lanzador, así que la geometría también respeta el grid elegido).

Para una escena limpia, ejecutar primero `AntSim > Crear escena de juego` y
luego `AntSim > Jugar`.

El archivo de ajustes (`%TEMP%\antsim-launch.txt`) se parsea con
`LaunchSettings` (modelo puro verificado headless en
`src/Core/AntSim.Core.Tests/LaunchSettingsTests.cs`): claves `clave=valor`,
`#` comenta, claves desconocidas se ignoran y los valores fuera de rango se
rechazan con el motivo en la consola de Unity.

## Si aparecen errores de compilación

1. Cerrar Unity completamente.
2. Confirmar que Unity Hub no mantiene el proyecto abierto en segundo plano.
3. Abrir de nuevo `src/App/AntSim.Unity`.
4. Esperar a que desaparezca el indicador rojo de compilación.
5. Revisar la consola: un error `CS` de la capa Unity no lo detecta
   `dotnet test`.

El chequeo batch, ejecutado con Unity cerrado, es:

```powershell
bash scripts/check-unity-compile.sh
```

Git Bash solo se usa para los scripts históricos de CI; el juego se lanza con
`.ps1` o `.bat` en Windows.

## Si la escena se ve vacía o blanca

No abras la raíz del repositorio como proyecto Unity. La ruta correcta es:

```text
E:\Users\kaoti\Documentos\GitHub\Hormigueros\src\App\AntSim.Unity
```

Después de corregir la ruta, ejecuta `AntSim > Crear escena de juego` para que el
bootstrapper regenere cámara, suelo, materiales, hormigueros, hormigas y HUD.
La escena usa `artifacts/pretrain-warm-v2.antgenome` por defecto; si el pool no
existe, compílalo/exporta primero o selecciona otro pool en el asistente.

## Si no se encuentra el Editor

Define explícitamente el ejecutable real del Editor:

```powershell
$env:UNITY_CLI = "C:\Program Files\Unity\Hub\Editor\6000.6.0f1\Editor\Unity.exe"
.\scripts\play-game.ps1
```

No uses `C:\Users\<usuario>\AppData\Local\Unity\bin\unity.exe`:
es el launcher del Hub, no el ejecutable del Editor. Los launchers se rechazan
deliberadamente para evitar una falsa sensación de que el juego se ha abierto.

## Controles en Play

- Click: seleccionar una hormiga.
- `D`: colocar drops; `Z`: deshacer.
- `F`/`G`: cambiar capa y colonia de feromonas.
- `I`: importar un `.antgenome`.
- `J`: saltar a una alerta.
- `Espacio`: pausar/reanudar.
- `+`/`=`: cambiar la velocidad de reproducción.

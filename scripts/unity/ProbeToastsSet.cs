using System.Globalization;
using System.IO;
using AntSim.Unity.Scripts.Presenter;
using UnityEngine;

/// <summary>Config del Play pass: la escribe scripts/playpass-toast-dedupe.sh.</summary>
[System.Serializable]
public sealed class PlaypassConfig
{
    public int seed = 42;
    public int grid = 96;
    public int colonies = 2;
    public int ticks = 7200;
    public float speed = 3f;
    public string seedPool = "";
    public string replay = "";
}

/// <summary>
/// Probe del Play pass — configura el `SimPresenterBehaviour` de la escena desde
/// `<proyecto>/Temp/antsim-playpass.json` (lo escribe el script que lo invoca; el
/// intérprete de `run_script` no recibe argumentos, así que el config viaja en
/// archivo). Devuelve un resumen de lo aplicado.
///
/// Se ejecuta ANTES de `editor_play` para que `Start()` arranque el stream con
/// los parámetros de la verificación.
/// </summary>
public static class ProbeToastsSet
{
    public static string Run()
    {
        var behaviour = Object.FindAnyObjectByType<SimPresenterBehaviour>();
        if (behaviour == null) return "error|sin SimPresenterBehaviour en la escena";

        string path = Path.GetFullPath(
            Path.Combine(Application.dataPath, "..", "Temp", "antsim-playpass.json"));
        if (!File.Exists(path)) return "error|falta el config: " + path;

        var cfg = JsonUtility.FromJson<PlaypassConfig>(File.ReadAllText(path));
        behaviour.Seed = (ulong)cfg.seed;
        behaviour.Grid = cfg.grid;
        behaviour.Colonies = cfg.colonies;
        behaviour.Ticks = cfg.ticks;
        behaviour.Speed = cfg.speed;
        behaviour.SeedPoolPath = string.IsNullOrEmpty(cfg.seedPool) ? null : cfg.seedPool;
        behaviour.ReplayFile = string.IsNullOrEmpty(cfg.replay) ? null : cfg.replay;

        return "ok|seed=" + cfg.seed
             + "|grid=" + cfg.grid
             + "|ticks=" + cfg.ticks
             + "|speed=" + cfg.speed.ToString(CultureInfo.InvariantCulture)
             + "|seedPool=" + (behaviour.SeedPoolPath ?? "-")
             + "|replay=" + (behaviour.ReplayFile ?? "-");
    }
}

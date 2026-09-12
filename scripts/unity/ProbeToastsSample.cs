using System.Globalization;
using System.Text;
using AntSim.Unity.Scripts.Presenter;
using AntSim.Unity.Scripts.Streaming;
using UnityEngine;

/// <summary>
/// Probe del Play pass (bloques 3 y 5) — UNA muestra por invocación: el estado
/// VISIBLE del juego tal como lo ve el HUD EN VIVO.
///
/// Formato (una línea, campos por `|`, listas por `,`):
///
///     tick|speed|keys|children|world|relay|anim
///
/// `keys`     = claves del modelo PURO (HudToastsModel.Active), en orden de pila.
/// `children` = elementos VISIBLES de `ToastContainer` (render F5.1), sin prefijo.
/// `world`    = `ants=N,items=M,nests=K,oob=J` (oob = poses vivas fuera del mundo).
/// `relay`    = semáforo por colonia del canal D: `0:L,1:L` (0 gris · 1 ámbar · 2 verde).
/// `anim`     = suma truncada de las posiciones de las hormigas: dos muestras
///              consecutivas con el mismo valor ⇒ el mundo NO se mueve.
///
/// Ni las claves ni los nombres de elemento contienen `|` ni `,`, así que el
/// formato se parsea con `IFS='|' read` sin escapes. Lo consume
/// scripts/playpass-toast-dedupe.sh vía `unity command run_script`.
/// NO vive bajo Assets/: es herramienta del repo, no script del juego.
/// </summary>
public static class ProbeToastsSample
{
    public static string Run()
    {
        var hud = Object.FindAnyObjectByType<HudLayoutBehaviour>();
        if (hud == null) return "error|sin HudLayoutBehaviour en la escena";

        var behaviour = hud.Presenter;
        var presenter = behaviour != null ? behaviour.Presenter : null;
        var state = behaviour != null ? behaviour.CurrentState : null;
        int grid = behaviour != null ? behaviour.Grid : 0;

        var sb = new StringBuilder();
        sb.Append((presenter != null ? presenter.CurrentTick?.Tick ?? 0UL : 0UL)
                  .ToString(CultureInfo.InvariantCulture));
        sb.Append('|').Append((behaviour != null ? behaviour.Speed : 0f)
                              .ToString(CultureInfo.InvariantCulture));
        sb.Append('|');

        var toasts = hud.Toasts;
        for (int i = 0; i < toasts.Active.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(toasts.Active[i].Key);
        }
        sb.Append('|');

        var container = hud.ToastContainer;
        if (container != null)
        {
            bool first = true;
            foreach (Transform child in container)
            {
                string name = child.gameObject.name;
                if (!name.StartsWith("Toast_", System.StringComparison.Ordinal)) continue;
                // El contenedor es un POOL: expirar desactiva, no destruye. Solo
                // cuentan los elementos visibles (= activos), que es lo que ve el
                // jugador y lo que debe coincidir con el modelo.
                if (!child.gameObject.activeSelf) continue;
                if (!first) sb.Append(',');
                first = false;
                sb.Append(name.Substring("Toast_".Length));
            }
        }
        sb.Append('|');

        // — El mundo: poses que el presenter está dibujando (DrawMesh) —
        int ants = 0, items = 0, nests = 0, outOfWorld = 0;
        long anim = 0;
        if (state != null)
        {
            float size = WorldUnits.WorldSize(grid);
            foreach (var a in state.Ants)
            {
                ants++;
                if (!a.Alive) continue;
                if (a.X < 0f || a.Y < 0f || a.X > size || a.Y > size) outOfWorld++;
                anim += (long)a.X + (long)a.Y;
            }
            items = state.Items.Count;
            nests = state.Colonies.Count;
        }
        sb.Append("ants=").Append(ants)
          .Append(",items=").Append(items)
          .Append(",nests=").Append(nests)
          .Append(",oob=").Append(outOfWorld)
          .Append('|');

        // — Semáforo de relevo por colonia (canal D, ya calculado por el Core) —
        bool firstLight = true;
        foreach (var kv in hud.Cards.Cards)
        {
            if (!firstLight) sb.Append(',');
            firstLight = false;
            sb.Append(kv.Key.ToString(CultureInfo.InvariantCulture))
              .Append(':')
              .Append(kv.Value.Light.ToString(CultureInfo.InvariantCulture));
        }
        sb.Append('|').Append(anim.ToString(CultureInfo.InvariantCulture));
        return sb.ToString();
    }
}

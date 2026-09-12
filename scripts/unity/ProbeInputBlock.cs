using System.Globalization;
using System.IO;
using System.Text;
using AntSim.Unity.Scripts.EditorTools;
using AntSim.Unity.Scripts.Presenter;
using AntSim.Unity.Scripts.Streaming;
using UnityEngine;

/// <summary>
/// Probe del Play pass — BLOQUE 4 (interacción) verificado sin humano.
///
/// POR QUÉ EXISTE. Las cuatro acciones del bloque 4 se leían de `Input` dentro de
/// `Update()`, y `Input` no es inyectable desde el CLI: había que pulsar el teclado
/// a mano y el bloque quedaba siempre «pendiente humano». Los handlers exponen
/// ahora puntos de entrada SIN DISPOSITIVO (F5.2) y `Update()` solo los cablea a
/// `Input`. Esta sonda dispara esos mismos puntos de entrada —el rayo sale de la
/// cámara REAL, el plan es el REAL, la hormiga se elige del estado REAL— y
/// devuelve lo observable, de modo que el bloque 4 se comprueba y se repite.
///
/// El paso a ejecutar viaja en `<proyecto>/Temp/antsim-block4.step` (el intérprete
/// de `run_script` no recibe argumentos). El paso A2 recarga la escena, así que va
/// solo: si se mezclara con los demás, la respuesta se perdería en la recarga.
///
///     A   seleccionar · D · click en el suelo · Z · volver a marcar con horizonte
///         corto (el drop cae a los pocos ticks tras el reinicio)
///     P   lectura del estado tras un frame: tarjeta del inspector y resumen
///     A2  «reiniciar con plan» (destructivo: recarga la escena)
///     B   DESPUÉS del reinicio: el historial debe traer el CommandExecuted
///     C   con una alerta anclada: J · Alt+click sobre un toast · click fuera
///
/// Formato: campos `clave=valor` separados por `|`; ningún valor contiene `|`.
/// Lo consume scripts/playpass-live.sh. NO vive bajo Assets/: es herramienta del
/// repo, no script del juego.
/// </summary>
public static class ProbeInputBlock
{
    private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    private static string San(string s)
    {
        if (string.IsNullOrEmpty(s)) return "-";
        return s.Replace('|', '/').Replace('\n', ' ').Replace('\r', ' ').Trim();
    }

    private static string Pos(float x, float y) => F(x) + ":" + F(y);

    public static string Run()
    {
        string stepPath = Path.Combine(Application.dataPath, "..", "Temp", "antsim-block4.step");
        string step = File.Exists(stepPath) ? File.ReadAllText(stepPath).Trim() : "A";

        var hud = Object.FindAnyObjectByType<HudLayoutBehaviour>();
        if (hud == null) return "error|sin HudLayoutBehaviour en la escena";
        var presenter = hud.Presenter;
        if (presenter == null) return "error|el HUD no tiene presenter";

        switch (step)
        {
            case "P": return ReadState(hud, presenter);
            case "C": return JumpChecks(hud);
            case "B": return AfterRestart(hud, presenter);
            case "A2": return TriggerRestart(hud);
            default: return Actions(hud, presenter);
        }
    }

    // ── A: las acciones de teclado/ratón ────────────────────────────────────
    private static string Actions(HudLayoutBehaviour hud, SimPresenterBehaviour presenter)
    {
        var pick = Object.FindAnyObjectByType<AntPickClickHandler>();
        var drop = Object.FindAnyObjectByType<DropFoodClickHandler>();
        if (pick == null) return "error|sin AntPickClickHandler";
        if (drop == null) return "error|sin DropFoodClickHandler";

        var cam = Camera.main;
        if (cam == null) return "error|sin Camera.main";

        var state = presenter.CurrentState;
        if (state == null || state.Ants.Count == 0) return "error|sin hormigas en el estado actual";

        // Se elige una hormiga VIVA y se pulsa sobre SU punto de pantalla: el
        // mismo gesto que hace el jugador, resuelto por la cámara real.
        GameStreamParser.AntPose target = default;
        bool found = false;
        foreach (var a in state.Ants)
        {
            if (!a.Alive) continue;
            target = a;
            found = true;
            break;
        }
        if (!found) return "error|sin hormigas vivas";

        Vector3 world = new Vector3(target.X, 0f, target.Y); // sim (x,y) → mundo (x, z)
        Vector2 screen = cam.WorldToScreenPoint(world);

        var sb = new StringBuilder("A");
        sb.Append("|want=").Append(target.Id.ToString(CultureInfo.InvariantCulture));

        uint picked = pick.PickAtScreen(screen);
        sb.Append("|pick=").Append(picked.ToString(CultureInfo.InvariantCulture));

        // Distancia del cuerpo seleccionado al punto pulsado: si el raycast o el
        // mapeo pantalla→mundo se rompieran, el id saldría lejísimos.
        float dist = -1f;
        if (picked != 0)
        {
            foreach (var a in state.Ants)
            {
                if (a.Id != picked) continue;
                float dx = a.X - target.X, dy = a.Y - target.Y;
                dist = Mathf.Sqrt(dx * dx + dy * dy);
                break;
            }
        }
        sb.Append("|pickdist=").Append(F(dist));

        // — El modo manda: con la tecla D SIN pulsar, el click NO marca nada —
        var errOff = drop.PlaceAtScreen(screen);
        sb.Append("|doffmarks=").Append(drop.Plan.Marks.Count.ToString(CultureInfo.InvariantCulture));
        sb.Append("|dofferr=").Append(San(errOff));

        // — D → modo marcar; click en el suelo → un drop; Z → deshacer —
        bool mode = drop.TogglePlaceMode();
        sb.Append("|dmode=").Append(mode ? 1 : 0);
        var errOn = drop.PlaceAtScreen(screen);
        sb.Append("|dmarks=").Append(drop.Plan.Marks.Count.ToString(CultureInfo.InvariantCulture));
        sb.Append("|derr=").Append(San(errOn));

        bool undone = drop.UndoLastDrop();
        sb.Append("|dundook=").Append(undone ? 1 : 0);
        sb.Append("|dundo=").Append(drop.Plan.Marks.Count.ToString(CultureInfo.InvariantCulture));
        sb.Append("|dzerook=").Append(drop.UndoLastDrop() ? 1 : 0);

        // — Se vuelve a marcar con horizonte corto para el reinicio con plan —
        // El tick del drop es ABSOLUTO: con horizonte 2 el CommandExecuted cae a
        // los pocos ticks de la partida relanzada y se puede observar en el paso B
        // sin esperar minutos.
        drop.DropHorizon = 2;
        var err2 = drop.PlaceAtScreen(screen);
        var args = drop.Plan.BuildCliArgs();
        var joined = new StringBuilder();
        for (int i = 0; i < args.Count; i++)
        {
            if (i > 0) joined.Append(' ');
            joined.Append(args[i]);
        }
        sb.Append("|args=").Append(args.Count.ToString(CultureInfo.InvariantCulture));
        sb.Append("|argsline=").Append(San(joined.ToString()));
        sb.Append("|dmarks2=").Append(drop.Plan.Marks.Count.ToString(CultureInfo.InvariantCulture));
        sb.Append("|derr2=").Append(San(err2));
        return sb.ToString();
    }

    // ── P: lectura del estado tras un frame ────────────────────────────────
    private static string ReadState(HudLayoutBehaviour hud, SimPresenterBehaviour presenter)
    {
        var drop = hud.DropPlan;
        var inspector = hud.Inspector;
        var sb = new StringBuilder("P");

        // El id seleccionado se lee del MODELO del inspector (el click lo pone ahí;
        // el campo SelectAntId de la vista es solo la vía alternativa por id).
        uint sel = inspector != null ? (inspector.Model.SelectedId ?? 0) : 0;
        sb.Append("|sel=").Append(sel.ToString(CultureInfo.InvariantCulture));

        string card = inspector != null ? inspector.Card ?? "" : "";
        int lines = 0;
        foreach (string ln in card.Split('\n'))
            if (ln.Trim().Length > 0) lines++;
        sb.Append("|cardlines=").Append(lines.ToString(CultureInfo.InvariantCulture));
        // La tarjeta del canal A nombra al cuerpo elegido y trae la huella del cerebro.
        bool has = sel != 0 && card.Contains("#" + sel.ToString(CultureInfo.InvariantCulture));
        sb.Append("|cardhas=").Append(has ? 1 : 0);
        sb.Append("|cardbrain=").Append(card.Contains("cerebro") ? 1 : 0);
        // Campos del canal A presentes en la tarjeta (contrato del inspector).
        int fields = 0;
        foreach (string label in new[] { "estado", "posición", "carga", "vigor", "energía", "edad", "inmigrante", "cerebro" })
            if (card.Contains(label)) fields++;
        sb.Append("|cardfields=").Append(fields.ToString(CultureInfo.InvariantCulture));
        sb.Append("|card=").Append(San(card));

        sb.Append("|marks=").Append((drop != null ? drop.Plan.Marks.Count : 0)
            .ToString(CultureInfo.InvariantCulture));
        sb.Append("|summary=").Append(San(drop != null ? drop.Plan.RenderSummary() : "-"));
        sb.Append("|mode=").Append(drop != null && drop.PlaceMode ? 1 : 0);
        sb.Append("|tick=").Append((presenter.Presenter.CurrentTick?.Tick ?? 0UL)
            .ToString(CultureInfo.InvariantCulture));
        return sb.ToString();
    }

    // ── A2: el reinicio con plan (destructivo) ─────────────────────────────
    private static string TriggerRestart(HudLayoutBehaviour hud)
    {
        var drop = hud.DropPlan;
        if (drop == null) return "error|sin DropFoodClickHandler";
        if (drop.Plan.Marks.Count == 0) return "error|el plan está vacío: no hay nada que reiniciar";

        var args = drop.Plan.BuildCliArgs();
        try
        {
            drop.RestartWithPlan();
        }
        catch (System.Exception ex)
        {
            // Aquí cae el caso clásico: la escena NO está en build settings, así
            // que `LoadScene` no puede recargarla y el botón no hace nada.
            return "A2|args=" + args.Count.ToString(CultureInfo.InvariantCulture)
                 + "|reload=error:" + San(ex.Message);
        }
        return "A2|args=" + args.Count.ToString(CultureInfo.InvariantCulture) + "|reload=ok";
    }

    // ── B: después del reinicio, el drop debe aparecer en el historial ─────
    private static string AfterRestart(HudLayoutBehaviour hud, SimPresenterBehaviour presenter)
    {
        var history = hud.History;
        var sb = new StringBuilder("B");
        sb.Append("|rows=").Append(history.Rows.Count.ToString(CultureInfo.InvariantCulture));

        // Solo los CommandExecuted crean fila, y `Row.CommandKind` es el TIPO de
        // comando (0 DropFood · 1 SaveGame) — el 11 es el kind del EVENTO, no de
        // la fila. Aquí interesa el drop del plan, así que se cuenta el 0.
        int cmdRows = 0;
        ulong firstCmd = 0;
        float dx = -1f, dy = -1f;
        foreach (var row in history.Rows)
        {
            if (row.CommandKind != 0) continue; // 0 = DropFood
            cmdRows++;
            if (firstCmd == 0)
            {
                firstCmd = row.Tick;
                dx = row.X;
                dy = row.Y;
            }
        }
        sb.Append("|cmdrows=").Append(cmdRows.ToString(CultureInfo.InvariantCulture));
        sb.Append("|totalcmd=").Append(history.TotalCommands.ToString(CultureInfo.InvariantCulture));
        sb.Append("|firstcmd=").Append(firstCmd.ToString(CultureInfo.InvariantCulture));
        sb.Append("|dropxy=").Append(Pos(dx, dy));
        sb.Append("|tick=").Append((presenter.Presenter.CurrentTick?.Tick ?? 0UL)
            .ToString(CultureInfo.InvariantCulture));
        return sb.ToString();
    }

    // ── C: salto de cámara por toast (J y Alt+click) ───────────────────────
    private static string JumpChecks(HudLayoutBehaviour hud)
    {
        var jump = Object.FindAnyObjectByType<ToastClickCameraJump>();
        if (jump == null) return "error|sin ToastClickCameraJump";
        var cam = Camera.main;
        if (cam == null) return "error|sin Camera.main";

        var toasts = hud.Toasts.Active;
        var elements = HudElementLayoutModel.ToastElements(toasts);

        var sb = new StringBuilder("C");
        // Dónde estaba la partida al muestrear: sin esto, «no había alerta
        // anclada» es indistinguible de «la corrida no llegó» o «va lentísima».
        var pb = hud.Presenter;
        sb.Append("|tick=").Append(pb != null
            ? (pb.Presenter.CurrentTick?.Tick ?? 0UL).ToString(CultureInfo.InvariantCulture) : "-");
        sb.Append("|speed=").Append(pb != null
            ? pb.Speed.ToString("0.##", CultureInfo.InvariantCulture) : "-");
        var keys = new StringBuilder();
        foreach (var toast in toasts)
        {
            if (keys.Length > 0) keys.Append(',');
            keys.Append(toast.Key);
        }
        sb.Append("|keys=").Append(keys.Length > 0 ? keys.ToString() : "-");
        sb.Append("|toasts=").Append(elements.Count.ToString(CultureInfo.InvariantCulture));

        // Primer toast CON ancla: es al que debe saltar J (el más reciente con ancla).
        HudElementLayoutModel.ToastElement? anchored = null;
        foreach (var e in elements)
        {
            if (!e.HasAnchor) continue;
            anchored = e;
            break;
        }
        sb.Append("|anchored=").Append(anchored != null ? 1 : 0);

        var before = cam.transform.position;
        bool jnew = jump.JumpToNewestAnchored();
        sb.Append("|jnew=").Append(jnew ? 1 : 0);
        sb.Append("|jtarget=").Append(jump.LastJumpTarget is Vector3 t ? Pos(t.x, t.z) : "-");
        sb.Append("|janchor=").Append(anchored != null ? Pos(anchored.AnchorX, anchored.AnchorY) : "-");
        sb.Append("|jmoved=").Append((cam.transform.position - before).sqrMagnitude > 0.01f ? 1 : 0);

        // Alt+click EXACTO: py = centro de la franja de ese toast en el contenedor.
        if (anchored != null)
        {
            bool alt = jump.JumpToToastAtContainerY(anchored.Y + HudElementLayoutModel.ToastHeight * 0.5f);
            sb.Append("|jalt=").Append(alt ? 1 : 0);
            sb.Append("|jaltx=").Append(jump.LastJumpTarget is Vector3 t2 ? Pos(t2.x, t2.z) : "-");
            sb.Append("|jaltsel=").Append(anchored.Key);
        }
        else
        {
            sb.Append("|jalt=0|jaltx=-|jaltsel=-");
        }

        // Click FUERA de la pila: no hay toast, la cámara NO debe moverse.
        var beforeMiss = cam.transform.position;
        bool miss = jump.JumpToToastAtContainerY(elements.Count * (HudElementLayoutModel.ToastHeight + HudElementLayoutModel.ToastGap) + 500f);
        sb.Append("|joutside=").Append(!miss ? 1 : 0);
        sb.Append("|jmissflag=").Append(jump.LastJumpMiss ? 1 : 0);
        sb.Append("|joutmoved=").Append((cam.transform.position - beforeMiss).sqrMagnitude > 0.01f ? 1 : 0);
        return sb.ToString();
    }
}

#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using AntSim.Unity.Scripts.Presenter;
using AntSim.Unity.Scripts.Streaming;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Sonda del Play pass — LAS BARRAS DE DESCARGAS, MEDIDAS EN LOS PÍXELES DE LA
/// TARJETA (F5.1). Existe porque la gráfica se verificó headless sobre el modelo
/// puro (`ColonySparklineModel`): eso prueba que la textura sale bien, no que la
/// tarjeta la PINTE donde toca, con su color y dentro de su banda.
///
/// CÓMO, y por qué en DOS pasos. Los píxeles de la tarjeta son los del FRAME
/// renderizado, y lo que se escribe en una textura no está en el framebuffer
/// hasta que el motor dibuja OTRO frame. `run_script` corre entero dentro de un
/// frame, así que:
///
///   `prepare`  inyecta una serie CONOCIDA en el modelo real de cada tarjeta
///              (la API pública que alimenta el stream), la repinta y CONGELA la
///              alimentación (`HudLayoutBehaviour.enabled = false`) para que los
///              ticks de la partida no le añadan muestras entre los dos pasos.
///   `measure`  (otra invocación, ~2 s después ⇒ muchos frames) captura el frame,
///              recorta la banda de la gráfica, la guarda y publica la geometría;
///              después RESTAURA la serie real y vuelve a habilitar el reparto.
///
/// La serie es SINTÉTICA a propósito: con descargas orgánicas no se puede exigir
/// que haya barras en la ventana de 90 s de esta partida, y un paso que a veces
/// no tiene nada que medir es un paso que a veces pasa en verde sin medir. El
/// patrón deja cuatro ventanas con descargas (1, 3, 5 y 7) separadas por ceros,
/// así que el analizador puede exigir CUATRO barrones, que cada uno caiga en las
/// columnas de SU muestra y que la altura crezca con la cuenta.
///
/// ORIENTACIÓN, medida y no supuesta: `GetPixels32` de la captura empieza por la
/// fila de ABAJO de la pantalla, pero `EncodeToPNG` guarda el PNG del derecho
/// (fila 0 = ARRIBA), así que el volcado declara `row0=top` y el analizador
/// cuenta las alturas desde la ÚLTIMA fila de la banda (la base real). Se midió
/// al revés una vez: con la fila 0 del PNG como base, todas las barras salían al
/// alto de la banda y el área al 100 %. Si la convención cambiara, el paso falla
/// con un mensaje claro en vez de dar por buena una gráfica del revés.
///
/// Los números que publica son la GEOMETRÍA (dónde quedó la banda en pantalla y
/// en el recorte); la clasificación de color y el veredicto son de
/// `scripts/playpass-sparkline.py`, que mide sobre el PNG conservado. Así el
/// mismo análisis se repite sin editor.
///
/// El paso a ejecutar viaja en `<proyecto>/Temp/antsim-spark.step` (el intérprete
/// de `run_script` no recibe argumentos). NO vive bajo Assets/: es herramienta
/// del repo, no script del juego.
/// </summary>
public static class ProbeSparklinePixels
{
    /// <summary>Descargas por ventana que se inyectan: cuatro ventanas con
    /// descargas, separadas por ceros para que cada barra caiga en SU tramo de
    /// columnas y el analizador pueda contarlas por separado.</summary>
    private static readonly int[] PatternDrops = { 0, 3, 0, 0, 7, 0, 1, 0, 0, 5, 0, 0 };

    /// <summary>Reserva de cada muestra: una rampa, para que el ÁREA se vea subir
    /// y su borde superior se pueda comparar con la serie (nunca por debajo del
    /// 20 %, que pintaría de rojo y confundiría la lectura del color).</summary>
    private static readonly float[] PatternStock =
    {
        0.32f, 0.35f, 0.38f, 0.41f, 0.44f, 0.47f,
        0.50f, 0.53f, 0.56f, 0.59f, 0.62f, 0.66f
    };

    private const string StepName = "antsim-spark.step";
    private const string FrameFile = "antsim-spark-frame.png";
    private const string CropPrefix = "antsim-spark-card";
    private const int CropMargin = 3;

    /// <summary>Lo que hay que devolver al modelo cuando termine la medida: la
    /// serie REAL que la tarjeta tenía antes de inyectar el patrón.</summary>
    private sealed class Saved
    {
        public ColonySparklineBehaviour? Spark;
        public string Name = "";
        public int ColonyId;
        public float[] Values = Array.Empty<float>();
        public int[] Drops = Array.Empty<int>();
        public ulong[] Ticks = Array.Empty<ulong>();
    }

    /// <summary>El estado que `prepare` deja en DISCO para `measure`/`restore`.
    ///
    /// NO puede vivir en campos estáticos: `run_script` compila cada invocación
    /// en una ASAMBLEA NUEVA (medido: el nombre del ensamblado cambia en cada
    /// llamada y un contador estático vuelve a 1 siempre), así que los `static`
    /// se reinician entre los dos pasos — con ellos, `_injected` mentía y
    /// `measure` se plantaba sin capturar nada, aunque la serie inyectada seguía
    /// en su tarjeta. Lo que SÍ sobrevive es el estado de las INSTANCIAS de la
    /// escena; lo que no sobrevive (lo que había antes de inyectar, y si el HUD
    /// estaba congelado) se guarda aquí.</summary>
    private sealed class State
    {
        public bool HudWasEnabled;
        public bool Complete;
        public readonly List<Saved> Cards = new();
    }

    private const string StateName = "antsim-spark-state.txt";
    private const string StateHeader = "SPARKSTATE";
    private const string StateEnd = "END";

    private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    public static string Run()
    {
        string step = ReadStep();
        switch (step)
        {
            case "measure": return Measure();
            case "restore": return Restore();
            default: return Prepare();
        }
    }

    private static string ReadStep()
    {
        string path = Path.Combine(Application.dataPath, "..", "Temp", StepName);
        string step = File.Exists(path) ? File.ReadAllText(path).Trim() : "prepare";
        return step.Length == 0 ? "prepare" : step;
    }

    private static string TempPath(string file)
        => Path.Combine(Path.Combine(Application.dataPath, ".."), Path.Combine("Temp", file));

    // ── prepare: serie conocida en las tarjetas reales ──────────────────────
    private static string Prepare()
    {
        var sb = new StringBuilder("SPARKPREP");
        if (!Application.isPlaying) return sb.Append("|error=la sonda corre fuera de Play").ToString();

        var sparks = UnityEngine.Object.FindObjectsByType<ColonySparklineBehaviour>(FindObjectsInactive.Exclude);
        if (sparks.Length == 0)
            return sb.Append("|error=no hay ColonySparklineBehaviour en la escena (¿escena sin HUD?)").ToString();
        SortByColony(sparks);

        // La evidencia de una pasada ANTERIOR no puede sobrevivir en `Temp/`: si
        // `measure` fallara antes de guardar, `preserve_sparkline` copiaría PNG
        // viejos y el medidor estaría midiendo otra sesión sin decirlo.
        try
        {
            string temp = Path.Combine(Application.dataPath, "..", "Temp");
            if (Directory.Exists(temp))
                foreach (string old in Directory.GetFiles(temp, "antsim-spark-*.png"))
                    File.Delete(old);
        }
        catch (Exception) { /* sin permisos: el paso de medida fallará cerrado */ }

        var hud = UnityEngine.Object.FindAnyObjectByType<HudLayoutBehaviour>();
        if (hud == null)
            return sb.Append("|error=no hay HudLayoutBehaviour: nadie alimenta las gráficas").ToString();

        // Idempotente: si una pasada anterior dejó el patrón puesto (y su estado
        // en disco), se restaura antes de volver a inyectar (dos `prepare`
        // seguidos no encadenan series).
        if (File.Exists(StatePath())) RestoreFromDisk();

        // CONGELA la alimentación: el HUD reparte cada tick drenado, y a velocidad
        // 3 los ~2 s entre `prepare` y `measure` serían ~6 s de simulación (6
        // muestras reales) encima del patrón. Con el reparto apagado la serie que
        // se mide es EXACTAMENTE la inyectada y las columnas esperadas cuadran.
        //
        // El estado se escribe ANTES de tocar las tarjetas: si la sonda muere a
        // mitad, `restore` todavía sabe que congeló el HUD y a qué valor volver.
        bool hudWasEnabled = hud.enabled;
        WriteStateHeader(hudWasEnabled);
        hud.enabled = false;

        int savedCount = 0;
        sb.Append("|cards=").Append(sparks.Length)
          .Append("|samples=").Append(PatternDrops.Length)
          .Append("|pattern=").Append(Join(PatternDrops))
          .Append("|stock=").Append(Join(PatternStock))
          .Append("|frozen=").Append(hudWasEnabled ? 1 : 0)
          .Append("|hud=").Append(hud.gameObject.name);

        bool allPainted = true;
        for (int i = 0; i < sparks.Length; i++)
        {
            var spark = sparks[i];
            if (spark == null) continue;
            var target = spark.Target;
            if (target == null)
            {
                sb.Append("|t=").Append(spark.gameObject.name).Append("|painted=0|why=sin RawImage");
                allPainted = false;
                continue;
            }

            var saved = Snapshot(spark);
            saved.Name = spark.gameObject.name;
            saved.ColonyId = spark.ColonyId;
            AppendStateCard(saved);
            savedCount++;

            spark.Model.Clear();
            var series = spark.Model.For(spark.ColonyId);
            ulong baseTick = saved.Ticks.Length > 0 ? saved.Ticks[saved.Ticks.Length - 1] : 0UL;
            for (int s = 0; s < PatternDrops.Length; s++)
                series.Push(PatternStock[s], PatternDrops[s], baseTick + (ulong)((s + 1) * ColonySparklineModel.SampleEveryTicks));

            spark.Redraw();
            if (!spark.HasPainted) allPainted = false;

            sb.Append("|t=").Append(spark.gameObject.name)
              .Append("|path=").Append(target.rectTransform.parent != null
                  ? target.rectTransform.parent.name : "-")
              .Append("|colony=").Append(spark.ColonyId)
              .Append("|tex=").Append(Mathf.Max(1, spark.Width)).Append('x').Append(Mathf.Max(1, spark.Height))
              .Append("|count=").Append(series.Count)
              .Append("|dropsMax=").Append(series.DropsMax)
              .Append("|latest=").Append(series.DropsLatest)
              .Append("|low=").Append(series.Low ? 1 : 0)
              .Append("|painted=").Append(spark.HasPainted ? 1 : 0);
        }

        FinishState();
        sb.Append("|injected=").Append(savedCount > 0 ? 1 : 0);
        if (!allPainted) sb.Append("|warn=alguna gráfica no llegó a pintarse");
        return sb.ToString();
    }

    private static Saved Snapshot(ColonySparklineBehaviour spark)
    {
        var series = spark.Model.For(spark.ColonyId);
        int n = series.Count;
        var saved = new Saved
        {
            Spark = spark,
            ColonyId = spark.ColonyId,
            Values = new float[n],
            Drops = new int[n],
            Ticks = new ulong[n]
        };
        for (int i = 0; i < n; i++)
        {
            saved.Values[i] = series.At(i);
            saved.Drops[i] = series.DropsAt(i);
            // Los ticks no se conservan por muestra en el modelo (la serie guarda
            // el ÚLTIMO): se reconstruyen hacia atrás con la cadencia de muestreo,
            // que es lo único que se usa después (nada: el tick no pinta).
            saved.Ticks[i] = series.LastTick > (ulong)((n - 1 - i) * ColonySparklineModel.SampleEveryTicks)
                ? series.LastTick - (ulong)((n - 1 - i) * ColonySparklineModel.SampleEveryTicks) : 0UL;
        }
        return saved;
    }

    // ── measure: píxeles del frame real ─────────────────────────────────────
    private static string Measure()
    {
        var sb = new StringBuilder("SPARK");
        sb.Append("|stage=measure");
        var state = ReadState();
        if (state == null)
            return sb.Append("|error=prepare no dejó ningún estado en ").Append(StateName)
                     .Append(": sin la serie inyectada no hay nada que medir")
                     .Append("|injected=0").ToString();
        if (!state.Complete)
            return sb.Append("|error=prepare no terminó de inyectar (el estado no está cerrado)")
                     .Append("|injected=0").ToString();
        if (state.Cards.Count == 0)
            return sb.Append("|error=prepare no inyectó ninguna tarjeta")
                     .Append("|injected=0").ToString();

        // La serie inyectada vive en la INSTANCIA de cada tarjeta y sobrevive a
        // la asamblea nueva de `measure`; se comprueba tarjeta por tarjeta (no se
        // supone): una captura de una gráfica que ya no tiene el patrón sería
        // evidencia de otra cosa.
        var sparks = UnityEngine.Object.FindObjectsByType<ColonySparklineBehaviour>(FindObjectsInactive.Exclude);
        var byColony = new Dictionary<int, ColonySparklineBehaviour>();
        for (int i = 0; i < sparks.Length; i++)
            if (sparks[i] != null) byColony[sparks[i].ColonyId] = sparks[i];

        var cards = new List<Saved>();
        for (int i = 0; i < state.Cards.Count; i++)
        {
            var saved = state.Cards[i];
            if (!byColony.TryGetValue(saved.ColonyId, out var spark))
                return sb.Append("|error=la tarjeta de la colonia ").Append(saved.ColonyId)
                         .Append(" ya no está en la escena entre prepare y measure")
                         .Append("|injected=0").ToString();
            saved.Spark = spark;
            string? why = VerifyInjected(spark);
            if (why != null)
                return sb.Append("|error=").Append(why).Append("|injected=0").ToString();
            cards.Add(saved);
        }

        var frame = Capture();
        if (frame == null) return sb.Append("|error=sin captura del frame (¿ventana sin Game View?)").ToString();
        int W = frame.width, H = frame.height;
        var px = frame.GetPixels32();
        sb.Append("|frame=").Append(W).Append('x').Append(H);

        // La escala del canvas, en la CABECERA (los campos que van detrás de un
        // `t=` pertenecen a esa tarjeta y un `scale=` al final lo leería el
        // analizador como si fuera de la última). Todas las tarjetas viven en el
        // mismo canvas.
        float scale = 1f;
        for (int i = 0; i < cards.Count; i++)
        {
            var t = cards[i].Spark != null ? cards[i].Spark!.Target : null;
            if (t != null && t.canvas != null) { scale = t.canvas.scaleFactor; break; }
        }
        // `row0=top`: el PNG se guarda como lo ve el jugador (fila 0 = arriba).
        // `GetPixels32` sí empieza por la fila de ABAJO, pero `EncodeToPNG`
        // devuelve la imagen del derecho: medir desde la fila 0 del PNG como si
        // fuera la base leía la gráfica del revés (todas las barras al alto de
        // la banda). El analizador convierte y cuenta desde la base real.
        sb.Append("|row0=top|margin=").Append(CropMargin)
          .Append("|scale=").Append(F(scale))
          .Append("|pattern=").Append(Join(PatternDrops))
          .Append("|stock=").Append(Join(PatternStock))
          .Append("|cards=").Append(cards.Count);

        // El frame entero, para que el recorte se pueda comprobar como trozo suyo
        // (la misma prueba de procedencia del paso de iconos: lo medido en el
        // recorte está medido en la pantalla).
        SaveRaw(px, W, 0, 0, W, H, FrameFile);

        for (int i = 0; i < cards.Count; i++)
        {
            var saved = cards[i];
            var spark = saved.Spark;
            if (spark == null)
            {
                sb.Append("|t=").Append(saved.Name).Append("|error=la tarjeta ya no está en la escena|painted=0");
                continue;
            }
            var target = spark.Target;
            sb.Append("|t=").Append(spark != null ? spark.gameObject.name : "?")
              .Append("|colony=").Append(spark != null ? spark.ColonyId : -1)
              .Append("|tex=").Append(spark != null ? Mathf.Max(1, spark.Width) : 0)
              .Append('x').Append(spark != null ? Mathf.Max(1, spark.Height) : 0)
              .Append("|low=").Append(spark != null && spark.PaintedLow ? 1 : 0)
              .Append("|drops=").Append(spark != null ? spark.LatestDrops : 0)
              .Append("|fraction=").Append(F(spark != null ? spark.LatestFraction : 0f));
            if (target == null)
            {
                sb.Append("|error=la tarjeta no tiene RawImage|painted=0");
                continue;
            }
            var canvas = target.canvas;

            if (!TryScreenRect(target.rectTransform, canvas, out var raw))
            {
                sb.Append("|error=el rect de la gráfica no está en pantalla|painted=0");
                continue;
            }

            int bx0 = Mathf.Clamp(raw.X0 - CropMargin, 0, W - 1);
            int by0 = Mathf.Clamp(raw.Y0 - CropMargin, 0, H - 1);
            int bx1 = Mathf.Clamp(raw.X1 + CropMargin, 0, W - 1);
            int by1 = Mathf.Clamp(raw.Y1 + CropMargin, 0, H - 1);
            bool clipped = bx0 > raw.X0 - CropMargin || by0 > raw.Y0 - CropMargin
                        || bx1 < raw.X1 + CropMargin || by1 < raw.Y1 + CropMargin;

            string file = CropPrefix + i + ".png";
            SaveRaw(px, W, bx0, by0, bx1 - bx0 + 1, by1 - by0 + 1, file);

            // ¿La banda cae DENTRO de la tarjeta? Es la diferencia entre «la
            // gráfica se pintó» y «se pintó en la tarjeta», que es lo que se afirma.
            int inside = 0;
            string cardText = "-";
            var panel = target.rectTransform.parent as RectTransform;
            if (panel != null && TryScreenRect(panel, canvas, out var card))
            {
                inside = raw.X0 >= card.X0 && raw.X1 <= card.X1
                      && raw.Y0 >= card.Y0 && raw.Y1 <= card.Y1 ? 1 : 0;
                cardText = card.X0 + "," + card.Y0 + ","
                         + (card.X1 - card.X0 + 1) + "x" + (card.Y1 - card.Y0 + 1);
            }

            sb.Append("|box=").Append(bx0).Append(',').Append(by0).Append(',')
              .Append(bx1 - bx0 + 1).Append('x').Append(by1 - by0 + 1)
              .Append("|inner=").Append(raw.X0 - bx0).Append(',').Append(raw.Y0 - by0).Append(',')
              .Append(raw.X1 - raw.X0 + 1).Append('x').Append(raw.Y1 - raw.Y0 + 1)
              .Append("|cardrect=").Append(cardText)
              .Append("|inside=").Append(inside)
              .Append("|clipped=").Append(clipped ? 1 : 0)
              .Append("|file=").Append(file)              .Append("|painted=").Append(spark!.HasPainted ? 1 : 0);
        }


        UnityEngine.Object.Destroy(frame);

        // Se restaura AQUÍ (y no solo con el paso `restore`): la captura ya está
        // hecha, así que devolver la serie real no toca la evidencia, y el HUD no
        // se queda con una gráfica de mentira si el pass muere justo después.
        RestoreState(state);
        return sb.ToString();
    }

    /// <summary>Comprueba que la tarjeta sigue MOSTRANDO el patrón inyectado (no
    /// solo que el paso `prepare` dijo hacerlo): devuelve null si coincide y el
    /// motivo si no.</summary>
    private static string? VerifyInjected(ColonySparklineBehaviour spark)
    {
        var series = spark.Model.For(spark.ColonyId);
        string label = spark.gameObject.name + " (colonia " + spark.ColonyId + ")";
        if (series.Count != PatternDrops.Length)
            return "la serie inyectada no sobrevivió en " + label + ": count=" + series.Count
                 + ", esperado " + PatternDrops.Length;
        for (int i = 0; i < PatternDrops.Length; i++)
        {
            if (series.DropsAt(i) != PatternDrops[i])
                return "la serie inyectada cambió en " + label + ": descargas de la muestra "
                     + i + " = " + series.DropsAt(i) + ", esperado " + PatternDrops[i];
            if (Mathf.Abs(series.At(i) - PatternStock[i]) > 0.001f)
                return "la serie inyectada cambió en " + label + ": reserva de la muestra "
                     + i + " = " + F(series.At(i)) + ", esperado " + F(PatternStock[i]);
        }
        return null;
    }

    // ── restore: la serie real vuelve a su tarjeta ──────────────────────────
    private static string Restore()
    {
        int restored = RestoreFromDisk();
        return restored == 0
            ? "SPARKRESTORE|restored=0|why=nada-que-restaurar"
            : "SPARKRESTORE|restored=" + restored;
    }

    /// <summary>Devuelve la serie real de cada tarjeta y el reparto del HUD, y
    /// borra el estado. Si el fichero no existe, no hay nada que restaurar.</summary>
    private static int RestoreFromDisk()
    {
        var state = ReadState();
        return state == null ? 0 : RestoreState(state);
    }

    private static int RestoreState(State state)
    {
        int restored = 0;
        var sparks = UnityEngine.Object.FindObjectsByType<ColonySparklineBehaviour>(FindObjectsInactive.Exclude);
        var byColony = new Dictionary<int, ColonySparklineBehaviour>();
        for (int i = 0; i < sparks.Length; i++)
            if (sparks[i] != null) byColony[sparks[i].ColonyId] = sparks[i];

        foreach (var saved in state.Cards)
        {
            var spark = saved.Spark;
            if (spark == null && !byColony.TryGetValue(saved.ColonyId, out spark)) continue;
            spark.Model.Clear();
            var series = spark.Model.For(spark.ColonyId);
            for (int i = 0; i < saved.Values.Length; i++)
                series.Push(saved.Values[i], saved.Drops[i], i < saved.Ticks.Length ? saved.Ticks[i] : 0UL);
            spark.Redraw();
            restored++;
        }

        // La alimentación se devuelve SIEMPRE, también cuando no había tarjetas
        // que restaurar: `prepare` la congeló y un fallo a medio recorrido no
        // puede dejar el HUD sin actualizarse.
        var hud = UnityEngine.Object.FindAnyObjectByType<HudLayoutBehaviour>();
        if (hud != null) hud.enabled = state.HudWasEnabled;

        try { File.Delete(StatePath()); }
        catch (Exception) { /* el siguiente `restore` volverá a intentarlo */ }
        return restored;
    }

    // ── estado en disco (los `static` NO sobreviven entre invocaciones) ─────
    private static string StatePath() => TempPath(StateName);

    private static void WriteStateHeader(bool hudEnabled)
    {
        var sb = new StringBuilder(StateHeader);
        sb.Append("|hudEnabled=").Append(hudEnabled ? 1 : 0).Append('\n');
        File.WriteAllText(StatePath(), sb.ToString());
    }

    private static void AppendStateCard(Saved saved)
    {
        var sb = new StringBuilder("T|");
        sb.Append("colony=").Append(saved.ColonyId)
          .Append("|name=").Append(saved.Name)
          .Append("|values=").Append(Join(saved.Values))
          .Append("|drops=").Append(Join(saved.Drops))
          .Append("|ticks=").Append(Join(saved.Ticks))
          .Append('\n');
        File.AppendAllText(StatePath(), sb.ToString());
    }

    private static void FinishState() => File.AppendAllText(StatePath(), StateEnd + "\n");

    private static State? ReadState()
    {
        string path = StatePath();
        if (!File.Exists(path)) return null;
        var state = new State();
        foreach (string raw in File.ReadAllLines(path))
        {
            string line = raw.Trim();
            if (line.Length == 0) continue;
            if (line == StateEnd) { state.Complete = true; continue; }

            var fields = new Dictionary<string, string>();
            string[] parts = line.Split('|');
            for (int i = 1; i < parts.Length; i++)
            {
                int eq = parts[i].IndexOf('=');
                if (eq > 0) fields[parts[i].Substring(0, eq)] = parts[i].Substring(eq + 1);
            }

            if (line.StartsWith(StateHeader, StringComparison.Ordinal))
            {
                state.HudWasEnabled = fields.TryGetValue("hudEnabled", out string? h) && h == "1";
                continue;
            }
            if (!line.StartsWith("T|", StringComparison.Ordinal)) continue;
            if (!fields.TryGetValue("colony", out string? colonyText)
                || !int.TryParse(colonyText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int colony))
                continue;

            state.Cards.Add(new Saved
            {
                ColonyId = colony,
                Name = fields.TryGetValue("name", out string? name) ? name : "",
                Values = ParseFloats(fields, "values"),
                Drops = ParseInts(fields, "drops"),
                Ticks = ParseTicks(fields, "ticks")
            });
        }
        return state;
    }

    private static float[] ParseFloats(Dictionary<string, string> fields, string key)
    {
        if (!fields.TryGetValue(key, out string? raw) || raw.Length == 0) return Array.Empty<float>();
        string[] parts = raw.Split(',');
        var values = new float[parts.Length];
        for (int i = 0; i < parts.Length; i++)
            if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]))
                return Array.Empty<float>();
        return values;
    }

    private static int[] ParseInts(Dictionary<string, string> fields, string key)
    {
        if (!fields.TryGetValue(key, out string? raw) || raw.Length == 0) return Array.Empty<int>();
        string[] parts = raw.Split(',');
        var values = new int[parts.Length];
        for (int i = 0; i < parts.Length; i++)
            if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out values[i]))
                return Array.Empty<int>();
        return values;
    }

    private static ulong[] ParseTicks(Dictionary<string, string> fields, string key)
    {
        if (!fields.TryGetValue(key, out string? raw) || raw.Length == 0) return Array.Empty<ulong>();
        string[] parts = raw.Split(',');
        var values = new ulong[parts.Length];
        for (int i = 0; i < parts.Length; i++)
            if (!ulong.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out values[i]))
                return Array.Empty<ulong>();
        return values;
    }

    // ── utilidades de geometría y volcado ───────────────────────────────────
    private readonly struct ScreenRect
    {
        public readonly int X0, Y0, X1, Y1;
        public ScreenRect(int x0, int y0, int x1, int y1) { X0 = x0; Y0 = y0; X1 = x1; Y1 = y1; }
    }

    /// <summary>Rectángulo en píxeles de PANTALLA (origen abajo-izquierda, como
    /// `ScreenCapture`) de un `RectTransform` del canvas. `GetWorldCorners` ya
    /// devuelve espacio de mundo: se proyecta una sola vez (aplicarlo dos veces
    /// dejaba rects degenerados en la sonda de iconos).</summary>
    private static bool TryScreenRect(RectTransform rt, Canvas? canvas, out ScreenRect rect)
    {
        rect = default;
        var corners = new Vector3[4];
        rt.GetWorldCorners(corners);
        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;
        var cam = canvas != null ? canvas.worldCamera : null;
        for (int i = 0; i < corners.Length; i++)
        {
            var p = RectTransformUtility.WorldToScreenPoint(cam, corners[i]);
            if (p.x < minX) minX = p.x;
            if (p.y < minY) minY = p.y;
            if (p.x > maxX) maxX = p.x;
            if (p.y > maxY) maxY = p.y;
        }
        int x0 = Mathf.FloorToInt(minX), y0 = Mathf.FloorToInt(minY);
        int x1 = Mathf.CeilToInt(maxX) - 1, y1 = Mathf.CeilToInt(maxY) - 1;
        if (x1 < x0 || y1 < y0) return false;
        rect = new ScreenRect(x0, y0, x1, y1);
        return true;
    }

    private static void SortByColony(ColonySparklineBehaviour[] sparks)
    {
        for (int i = 1; i < sparks.Length; i++)
        {
            var cur = sparks[i];
            int j = i - 1;
            while (j >= 0 && sparks[j] != null && cur != null && sparks[j].ColonyId > cur.ColonyId)
            {
                sparks[j + 1] = sparks[j]!;
                j--;
            }
            sparks[j + 1] = cur!;
        }
    }

    private static string Join(int[] values)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < values.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(values[i].ToString(CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }

    private static string Join(float[] values)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < values.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(F(values[i]));
        }
        return sb.ToString();
    }

    private static string Join(ulong[] values)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < values.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(values[i].ToString(CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }

    private static Texture2D? Capture()
    {
        try { return ScreenCapture.CaptureScreenshotAsTexture(); }
        catch (Exception) { return null; }
    }

    /// <summary>Recorte NATIVO del framebuffer, SIN invertir filas al copiarlas
    /// (el `Texture2D` usa la misma fila 0 = abajo que `GetPixels32`); el PNG que
    /// sale de él queda del derecho, con la fila 0 = ARRIBA (`row0=top`).</summary>
    private static void SaveRaw(Color32[] px, int W, int x0, int y0, int w, int h, string file)
    {
        if (w < 1 || h < 1) return;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        var sub = new Color32[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                sub[y * w + x] = px[(y0 + y) * W + (x0 + x)];
        tex.SetPixels32(sub);
        tex.Apply();
        try { File.WriteAllBytes(TempPath(file), tex.EncodeToPNG()); }
        catch (Exception) { /* el volcado de texto sigue siendo la evidencia numérica */ }
        UnityEngine.Object.Destroy(tex);
    }
}

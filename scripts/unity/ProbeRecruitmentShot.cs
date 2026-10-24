#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using AntSim.Unity.Scripts.Presenter;
using AntSim.Unity.Scripts.Streaming;
using UnityEngine;

/// <summary>
/// Sonda del reclutamiento EN EL TABLERO VIVO (F8): ¿los montones sembrados y el
/// rastro de comida se VEN, o solo están en los datos?
///
/// POR QUÉ. El HUD ya escribe las cifras y la suite comprueba que caben, pero
/// «montones=8 (6–10 ítems, radio 18 u)» es una promesa sobre el mundo, y el
/// rastro podía estar vacío (canal E sin paquetes) o el quad sin pintar sin que
/// nada se pusiera rojo: el jugador leía ocho montones y veía comida suelta. Esta
/// sonda cierra el círculo en el FRAME real de Play:
///
///   · agrupa los ítems del estado dibujado por proximidad (el mismo radio de la
///     perilla) y publica tamaño, dispersión, centro y cuánto rastro hay alrededor
///     de cada grupo — un «montón» es un grupo apretado del tamaño que se siembra;
///   · elige el montón MEJOR CONECTADO (no el más grande): el que tiene rastro de
///     comida al lado es el que enseña el camino nido→montón en la captura;
///   · recorre la línea nido→montón en la capa de comida y publica qué fracción
///     tiene señal;
///   · proyecta cada celda con rastro con la cámara real y mira si el píxel es
///     ÁMBAR (el color del rastro pintado), probando las cuatro simetrías del
///     quad: la correcta gana de calle y así se detecta un mapa espejado sin
///     depender de suponer cómo están las UV del plane (fue exactamente el
///     defecto que destapó: 92 % en su sitio frente a 5 % sin girar);
///   · guarda el frame entero (`antsim-recruit-frame.png`, con HUD) y el recorte
///     NATIVO del montón (`antsim-recruit-pile.png`, y su ampliación ×3
///     `antsim-recruit-pile-big.png`) en `Temp/`, que es la evidencia que se mira.
///
/// NO vive bajo Assets/: es herramienta del repo.
public static class ProbeRecruitmentShot
{
    /// <summary>Radio (u) alrededor del montón donde se cuenta rastro «del montón».</summary>
    private const float TrailNearPile = 24f;

    /// <summary>Holgura (u) alrededor del montón en el recorte.</summary>
    private const float CropPadU = 12f;

    public static string Run()
    {
        var sb = new StringBuilder("RECRUIT");
        var presenter = UnityEngine.Object.FindAnyObjectByType<SimPresenterBehaviour>();
        if (presenter == null) return sb.Append("|error=sin-presenter").ToString();
        var state = presenter.CurrentState;
        if (state == null) return sb.Append("|error=sin-estado").ToString();

        var knobs = presenter.ActiveRecruitment;
        sb.Append("|enabled=").Append(presenter.Recruitment ? 1 : 0)
          .Append("|tick=").Append(state.Tick)
          .Append("|items=").Append(state.Items.Count)
          .Append("|knobs=").Append(knobs.Describe());

        // — El rastro que se está pintando (capa y colonia del selector) —
        var phero = UnityEngine.Object.FindAnyObjectByType<PheromoneTileBehaviour>();
        var model = phero != null ? phero.Model : null;
        int paintedColony = phero != null ? phero.Selector.Colony : 0;
        bool hasTrail = model != null && model.Width > 0;
        sb.Append("|layer=").Append(phero != null ? phero.LayerLabel : "sin-sonda")
          .Append("|paintedColony=").Append(paintedColony);

        int trailCells = 0, trailMax = 0, strongCells = 0;
        int maxX = 0, maxY = 0;
        // Histograma de la capa: el «humo» es la cola débil (2–40/255) alrededor
        // del depósito, que la curva de pantalla deja fuera a propósito; sin saber
        // qué parte del mapa es cola no se puede decidir un umbral sin apagar el
        // rastro entero.
        int b8 = 0, b24 = 0, b48 = 0, b96 = 0, bTop = 0;
        if (hasTrail)
        {
            for (int y = 0; y < model!.Height; y++)
                for (int x = 0; x < model.Width; x++)
                {
                    byte v = model.Cell(x, y);
                    if (v == 0) continue;
                    trailCells++;
                    if (v > trailMax) { trailMax = v; maxX = x; maxY = y; }
                    if (v >= 96) strongCells++;
                    if (v < 8) b8++;
                    else if (v < 24) b24++;
                    else if (v < 48) b48++;
                    else if (v < 96) b96++;
                    else bTop++;
                }
        }
        sb.Append("|trailCells=").Append(trailCells)
          .Append("|trailMax=").Append(trailMax)
          .Append("|strongCells=").Append(strongCells)
          .Append("|hist=").Append(b8).Append('/').Append(b24).Append('/')
          .Append(b48).Append('/').Append(b96).Append('/').Append(bTop);

        // — Ancho del RASTRO contra el ancho de la HORMIGA —
        // El ancho que se ve es lo que hay que medir: una celda son 8 u, la
        // hormiga `AntWorldFraction × lado` de largo y ~su 40 % de ancho. Se
        // recorre el corte que cruza la celda más fuerte a varios umbrales.
        if (hasTrail)
        {
            float antU = presenter.AntMesh != null
                ? Mathf.Max(presenter.AntMesh.bounds.size.x, presenter.AntMesh.bounds.size.z) * presenter.EffectiveAntScale
                : 0f;
            sb.Append("|antU=").Append(F(antU));
            foreach (int t in new[] { 8, 24, 48, 96 })
            {
                int wx = 1, wy = 1;
                for (int x = maxX - 1; x >= 0 && model!.Cell(x, maxY) >= t; x--) wx++;
                for (int x = maxX + 1; x < model.Width && model!.Cell(x, maxY) >= t; x++) wx++;
                for (int y = maxY - 1; y >= 0 && model!.Cell(maxX, y) >= t; y--) wy++;
                for (int y = maxY + 1; y < model.Height && model!.Cell(maxX, y) >= t; y++) wy++;
                sb.Append("|band@").Append(t).Append('=').Append(wx).Append('x').Append(wy)
                  .Append('(').Append(F(wx * 8f)).Append("u)");
            }
        }

        // — Los grupos de ítems, por proximidad (mismo radio que la perilla) —
        var groups = Groups(state.Items, Mathf.Max(1f, knobs.ClusterRadius));
        foreach (var g in groups) g.TrailNear = TrailNear(g.Cx, g.Cy, hasTrail ? model : null, 1);
        // Orden: tamaño, y entre los que tienen rastro al lado, primero.
        groups.Sort((a, b) =>
        {
            bool at = a.TrailNear > 0, bt = b.TrailNear > 0;
            if (at != bt) return bt ? 1 : -1;
            if (b.TrailNear != a.TrailNear) return b.TrailNear.CompareTo(a.TrailNear);
            return a.Count != b.Count ? b.Count.CompareTo(a.Count) : a.Span.CompareTo(b.Span);
        });

        int piles = 0;
        var rows = new StringBuilder();
        foreach (var g in groups)
        {
            if (g.Count < 3) continue;
            piles++;
            if (piles <= 10)
                rows.Append("|g=").Append(piles).Append(':').Append(g.Count)
                    .Append(",span=").Append(F(g.Span))
                    .Append(",c=").Append(F(g.Cx)).Append('/').Append(F(g.Cy))
                    .Append(",trail=").Append(g.TrailNear);
        }
        sb.Append("|groups=").Append(piles).Append(rows);

        if (piles == 0)
        {
            sb.Append("|error=sin-montones");
            SaveFrame(sb);
            return sb.ToString();
        }

        // El montón medido tiene que ser un MONTÓN (≥3 ítems): un ítem suelto con
        // rastro al lado encabezaba la lista y el recorte enseñaba una migaja.
        Group? pile = null;
        foreach (var g in groups)
        {
            if (g.Count < 3) continue;
            pile = g;
            break;
        }
        if (pile == null)
        {
            sb.Append("|error=sin-montones");
            SaveFrame(sb);
            return sb.ToString();
        }
        float pileRadius = 0f;
        foreach (var it in pile.Items)
            pileRadius = Mathf.Max(pileRadius, Mathf.Sqrt((it.X - pile.Cx) * (it.X - pile.Cx) + (it.Y - pile.Cy) * (it.Y - pile.Cy)));
        sb.Append("|pile=").Append(pile.Count)
          .Append(",c=").Append(F(pile.Cx)).Append('/').Append(F(pile.Cy))
          .Append(",span=").Append(F(pile.Span))
          .Append(",r=").Append(F(pileRadius))
          .Append(",trail=").Append(pile.TrailNear);

        // — El rastro en la línea nido(pintado)→montón —
        float nestX = 0f, nestY = 0f, nestDist = 0f;
        foreach (var c in state.Colonies)
        {
            if (c.Id != paintedColony) continue;
            nestX = c.NestX; nestY = c.NestY;
            nestDist = Mathf.Sqrt((nestX - pile.Cx) * (nestX - pile.Cx) + (nestY - pile.Cy) * (nestY - pile.Cy));
        }
        sb.Append("|nestDist=").Append(F(nestDist));

        if (hasTrail && nestDist > 0f)
        {
            int steps = Mathf.Max(8, Mathf.RoundToInt(nestDist / 8f));
            int hit = 0;
            for (int s = 0; s <= steps; s++)
            {
                float t = (float)s / steps;
                float wx = Mathf.Lerp(nestX, pile.Cx, t);
                float wy = Mathf.Lerp(nestY, pile.Cy, t);
                if (model!.Cell(model.CellX(wx), model.CellY(wy)) > 0) hit++;
            }
            sb.Append("|lineCover=").Append(F((float)hit / (steps + 1)));
        }
        else
        {
            sb.Append("|lineCover=0");
        }

        // — Los píxeles del frame real —
        var frame = Capture();
        if (frame == null)
        {
            sb.Append("|error=sin-captura");
            return sb.ToString();
        }
        int W = frame.width, H = frame.height;
        var px = frame.GetPixels32();
        sb.Append("|frame=").Append(W).Append('x').Append(H);

        var cam = Camera.main;
        if (cam == null)
        {
            foreach (var c in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude))
                if (c != null && c.isActiveAndEnabled) { cam = c; break; }
        }
        if (cam == null)
        {
            sb.Append("|error=sin-camara");
            UnityEngine.Object.Destroy(frame);
            return sb.ToString();
        }

        var center = cam.WorldToScreenPoint(new Vector3(pile.Cx, 0f, pile.Cy));
        float step = Mathf.Max(1f, knobs.ClusterRadius);
        var edge = cam.WorldToScreenPoint(new Vector3(pile.Cx + step, 0f, pile.Cy));
        // Píxeles por UNIDAD (el desplazamiento se midió sobre `step` unidades;
        // sin dividir quedaba 18 px/u en un tablero de 1 px/u y el recorte salía
        // con el HUD dentro).
        float pxPerU = Mathf.Abs(edge.x - center.x) / step;
        sb.Append("|centerPx=").Append(F(center.x)).Append('/').Append(F(center.y))
          .Append("|pxPerU=").Append(F(pxPerU));
        if (center.z <= 0f)
        {
            sb.Append("|error=monton-detras-de-camara");
            UnityEngine.Object.Destroy(frame);
            return sb.ToString();
        }

        int half = Mathf.Clamp(Mathf.RoundToInt((pileRadius + CropPadU) * pxPerU), 28, 200);
        int x0 = Mathf.Clamp(Mathf.RoundToInt(center.x) - half, 0, W - 1);
        int x1 = Mathf.Clamp(Mathf.RoundToInt(center.x) + half, 0, W - 1);
        int y0 = Mathf.Clamp(Mathf.RoundToInt(center.y) - half, 0, H - 1);
        int y1 = Mathf.Clamp(Mathf.RoundToInt(center.y) + half, 0, H - 1);
        int itemPx = 0;
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                var c = px[y * W + x];
                if (c.g > 80 && c.g > c.r + 15 && c.g > c.b + 15) itemPx++;
            }
        sb.Append("|pileBox=").Append(x0).Append(',').Append(y0).Append("..")
          .Append(x1).Append(',').Append(y1)
          .Append("|itemPx=").Append(itemPx);
        SaveCrop(px, W, x0, y0, x1, y1, 1, "antsim-recruit-pile.png");
        SaveCrop(px, W, x0, y0, x1, y1, 3, "antsim-recruit-pile-big.png");

        // — Los píxeles: ¿el quad pinta el canal E, y en su SITIO? —
        // Cada celda con rastro se proyecta con la cámara real y se mira si el
        // píxel es ámbar. Se prueban las cuatro simetrías del quad: la correcta
        // gana de calle (medido: 92 % en su sitio frente a 5 % sin girar) y así
        // el check no depende de que yo tenga razón sobre las UV del plane.
        if (hasTrail)
        {
            int bw = model!.Width, bh = model.Height;
            int tested = 0, hitDirect = 0, hitFlipX = 0, hitFlipY = 0, hitFlip180 = 0;
            int amberPx = 0;
            for (int y = 0; y < bh; y++)
                for (int x = 0; x < bw; x++)
                {
                    byte v = model.Cell(x, y);
                    if (v < 48) continue;
                    tested++;
                    float wx = (x + 0.5f) * 8f, wy = (y + 0.5f) * 8f;
                    for (int k = 0; k < 4; k++)
                    {
                        float pxw = (k == 1 || k == 3) ? bw * 8f - wx : wx;
                        float pyw = (k == 2 || k == 3) ? bh * 8f - wy : wy;
                        var sp = cam.WorldToScreenPoint(new Vector3(pxw, 0f, pyw));
                        int xi = Mathf.RoundToInt(sp.x), yi = Mathf.RoundToInt(sp.y);
                        if (xi < 0 || yi < 0 || xi >= W || yi >= H) continue;
                        var c = px[yi * W + xi];
                        // Ámbar del rastro (0.95,0.72,0.30) sobre el suelo: mucho
                        // más claro y cálido que el marrón (≈122,94,64).
                        if (c.r < 160 || c.r < c.g || c.r - c.b < 55 || c.g - c.b < 25) continue;
                        if (k == 0) hitDirect++;
                        else if (k == 1) hitFlipX++;
                        else if (k == 2) hitFlipY++;
                        else hitFlip180++;
                        if (k == 0) amberPx++;
                    }
                }
            sb.Append("|trailPxTested=").Append(tested)
              .Append("|pxDirect=").Append(tested > 0 ? F((float)hitDirect / tested) : "0")
              .Append("|pxFlipX=").Append(tested > 0 ? F((float)hitFlipX / tested) : "0")
              .Append("|pxFlipY=").Append(tested > 0 ? F((float)hitFlipY / tested) : "0")
              .Append("|pxFlip180=").Append(tested > 0 ? F((float)hitFlip180 / tested) : "0")
              .Append("|amberPx=").Append(amberPx);
        }

        // — El ANCHO PINTADO del rastro, en unidades de mundo —
        // Es la queja del tablero: una nube de humo de decenas de unidades frente
        // a una hormiga de `antU`. Se mide el corte de píxeles ámbar que pasa por
        // la celda más fuerte (fila y columna), no el ancho del dato.
        {
            var top = cam.WorldToScreenPoint(new Vector3((maxX + 0.5f) * 8f, 0f, (maxY + 0.5f) * 8f));
            int tx = Mathf.RoundToInt(top.x), ty = Mathf.RoundToInt(top.y);
            int runX = AmberRun(px, W, H, tx, ty, 1, 0);
            int runY = AmberRun(px, W, H, tx, ty, 0, 1);
            sb.Append("|amberBand=").Append(F(runX / pxPerU)).Append('x')
              .Append(F(runY / pxPerU)).Append("u");
        }

        SaveCrop(px, W, 0, 0, W - 1, H - 1, 1, "antsim-recruit-frame.png");
        UnityEngine.Object.Destroy(frame);
        return sb.ToString();
    }

    /// <summary>Píxeles ámbar consecutivos en una dirección desde (x,y), contando el
    /// centro si ya es ámbar.</summary>
    private static int AmberRun(Color32[] px, int W, int H, int x, int y, int dx, int dy)
    {
        bool Amber(int xi, int yi)
        {
            if (xi < 0 || yi < 0 || xi >= W || yi >= H) return false;
            var c = px[yi * W + xi];
            return c.r >= 160 && c.r >= c.g && c.r - c.b >= 55 && c.g - c.b >= 25;
        }

        int n = Amber(x, y) ? 1 : 0;
        for (int i = 1; i < 400 && Amber(x + dx * i, y + dy * i); i++) n++;
        for (int i = 1; i < 400 && Amber(x - dx * i, y - dy * i); i++) n++;
        return n;
    }

    private sealed class Group
    {
        public readonly List<GameStreamParser.ItemView> Items = new();
        public float Cx, Cy, Span;
        public int TrailNear;
        public int Count => Items.Count;
    }

    /// <summary>Componentes conexas por distancia (unión simple) con el radio del
    /// montón. La dispersión se mide desde el centroide: un grupo encadenado por
    /// el camino tiene el mismo tamaño y una dispersión mucho mayor que un montón.</summary>
    private static List<Group> Groups(List<GameStreamParser.ItemView> items, float radius)
    {
        int n = items.Count;
        var seen = new bool[n];
        var groups = new List<Group>();
        float r2 = radius * radius;
        for (int i = 0; i < n; i++)
        {
            if (seen[i]) continue;
            var g = new Group();
            var queue = new Queue<int>();
            queue.Enqueue(i); seen[i] = true;
            while (queue.Count > 0)
            {
                int a = queue.Dequeue();
                g.Items.Add(items[a]);
                for (int b = 0; b < n; b++)
                {
                    if (seen[b]) continue;
                    float dx = items[a].X - items[b].X, dy = items[a].Y - items[b].Y;
                    if (dx * dx + dy * dy > r2) continue;
                    seen[b] = true; queue.Enqueue(b);
                }
            }
            float sx = 0f, sy = 0f;
            foreach (var it in g.Items) { sx += it.X; sy += it.Y; }
            g.Cx = sx / g.Items.Count; g.Cy = sy / g.Items.Count;
            float maxD = 0f;
            foreach (var it in g.Items)
            {
                float dx = it.X - g.Cx, dy = it.Y - g.Cy;
                maxD = Mathf.Max(maxD, Mathf.Sqrt(dx * dx + dy * dy));
            }
            g.Span = maxD;
            groups.Add(g);
        }
        return groups;
    }

    /// <summary>Celdas de la capa con valor ≥ <paramref name="minValue"/> en un disco
    /// alrededor del punto (mundo).</summary>
    private static int TrailNear(float wx, float wy, PheromoneTileModel? model, int minValue)
    {
        if (model == null || model.Width == 0) return 0;
        int cells = Mathf.CeilToInt(TrailNearPile / 8f);
        int cx = model.CellX(wx), cy = model.CellY(wy);
        int n = 0;
        for (int dy = -cells; dy <= cells; dy++)
            for (int dx = -cells; dx <= cells; dx++)
            {
                int x = cx + dx, y = cy + dy;
                if (x < 0 || y < 0 || x >= model.Width || y >= model.Height) continue;
                float rx = (x + 0.5f) * 8f - wx, ry = (y + 0.5f) * 8f - wy;
                if (rx * rx + ry * ry > TrailNearPile * TrailNearPile) continue;
                if (model.Cell(x, y) >= minValue) n++;
            }
        return n;
    }

    private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    private static Texture2D? Capture()
    {
        try { return ScreenCapture.CaptureScreenshotAsTexture(); }
        catch (Exception) { return null; }
    }

    private static string TempPath(string file)
        => Path.Combine(Path.Combine(Application.dataPath, ".."), Path.Combine("Temp", file));

    private static void SaveCrop(Color32[] px, int W, int x0, int y0, int x1, int y1, int scale, string file)
    {
        int w = x1 - x0 + 1, h = y1 - y0 + 1;
        int ow = w * scale, oh = h * scale;
        var tex = new Texture2D(ow, oh, TextureFormat.RGB24, false);
        var sub = new Color32[ow * oh];
        for (int y = 0; y < oh; y++)
            for (int x = 0; x < ow; x++)
                sub[y * ow + x] = px[(y0 + y / scale) * W + (x0 + x / scale)];
        tex.SetPixels32(sub);
        tex.Apply();
        try { File.WriteAllBytes(TempPath(file), tex.EncodeToPNG()); }
        catch (Exception) { /* la evidencia de texto ya va por stdout */ }
        UnityEngine.Object.Destroy(tex);
    }

    private static void SaveFrame(StringBuilder sb)
    {
        var frame = Capture();
        if (frame == null) { sb.Append("|error=sin-captura"); return; }
        var px = frame.GetPixels32();
        SaveCrop(px, frame.width, 0, 0, frame.width - 1, frame.height - 1, 1, "antsim-recruit-frame.png");
        UnityEngine.Object.Destroy(frame);
    }
}

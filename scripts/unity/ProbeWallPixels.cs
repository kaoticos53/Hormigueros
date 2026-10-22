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
/// Sonda del Play pass — LAS PAREDES DEL LABERINTO, MEDIDAS EN LOS PÍXELES DEL
/// RENDER DEL JUEGO (F6).
///
/// POR QUÉ. `LastWallDraws` se publicaba y nadie lo leía: la escena podía tener
/// el `WallMesh` y el `WallMaterial` cableados, el contrato de escena en verde y
/// la partida corriendo con un laberinto INVISIBLE — mal alto, escala cero, capa
/// que la cámara no ve, material sin instancing. Eso es exactamente lo que
/// «terreno con obstáculos» no puede permitirse, y ningún check de estado lo ve:
/// entre la matriz de la pared y el jugador quedan la cámara, la proyección, el
/// rasterizado y el orden de dibujo.
///
/// CÓMO. Con la partida VIVA, renderiza la cámara del juego a una RenderTexture
/// (el MISMO render que alimenta la vista, sin HUD — el patrón de
/// `ProbeVisualSample`) y compara el frame con el MAPA de la cabecera del stream:
/// se proyecta el centro de CADA celda interior con la cámara real y se clasifica
/// su píxel contra el color del material de pared. Así el veredicto no es «hay
/// algo oscuro»: es «las celdas que el mapa marca como muro están pintadas de
/// muro y las que no, no» — sitio Y color, celda por celda, sin depender de la
/// suerte de la simulación (el laberinto sale de la semilla y es estático).
///
/// Además publica una muestra de paredes y un CONTROL por cada zona abierta (con
/// dos celdas de holgura), con su caja en píxeles: lo que el analizador usa para
/// exigir que la caja esté LLENA de color de muro (escala correcta), que su
/// tamaño corresponda a las celdas del rectángulo (escala) y que los controles no
/// tengan ni un píxel de muro (un polígono enorme tapando el tablero no pasa).
///
/// La evidencia que se mira queda en `Temp/`: el frame entero
/// (`antsim-wall-frame.png`) y una hoja con los rectángulos esperados en magenta
/// y los controles en cian (`antsim-wall-marked.png`). `scripts/playpass-live.sh`
/// las copia a `artifacts/` antes de que Unity barra `Temp/`, y
/// `scripts/playpass-walls.py` vuelve a medir sobre ellas sin editor.
///
/// La cámara se proyecta con el ASPECTO de la RenderTexture, no con el de la Game
/// view: `Camera.aspect` sigue al target, así que al soltar la RT las cajas
/// salían ~4 % más grandes y las paredes del borde se desplazaban una celda hacia
/// dentro (medido: la caja de la pared de x=0 caía en x=190 cuando la pared está
/// en 178). Una medida de sitio con esa deriva no vale.
///
/// Devuelve `WALL|…` (cabecera + una línea `R|` por pared y `C|` por control) o
/// `WALL|error=…`. NO vive bajo Assets/: es herramienta del repo.
public static class ProbeWallPixels
{
    /// <summary>Resolución del render de medida: 1,2 px por unidad de mundo (una
    /// celda mide ~9,6 px de canto). El tablero de 96 celdas (768 u) entra entero
    /// en el encuadre con margen para el marco.</summary>
    private const int FrameW = 1280;
    private const int FrameH = 960;

    /// <summary>Rectángulos que se muestrean: los más grandes, para que el centro
    /// de cada uno caiga lejos del canto (donde el antialias mezcla el color del
    /// suelo) y de una hormiga que pase por al lado.</summary>
    private const int MaxWalls = 32;

    /// <summary>Controles en zona abierta. Cada uno es una celda sin pared con
    /// dos celdas de holgura alrededor: el píxel de su centro no puede tener el
    /// color del muro.</summary>
    private const int MaxControls = 32;

    /// <summary>Holgura en celdas alrededor de un control (Chebyshev): con menos,
    /// el centro proyectado puede caer sobre el canto de una pared vecina y el
    /// control mediría el muro que estamos comprobando en otro sitio.</summary>
    private const int ControlClearance = 2;

    /// <summary>Tolerancia por canal contra el color del material. El muro es
    /// `Kind.Flat` (Unlit): el frame trae el color EXACTO del material, así que la
    /// tolerancia solo cubre el redondeo del framebuffer. El suelo está a 25-30
    /// unidades de cada canal, de modo que un muro que no se pinta (o pintado con
    /// otro material) no puede colarse.</summary>
    private const int ColorTol = 12;

    /// <summary>Anillo exterior que NO se evalúa celda a celda: el marco del
    /// tablero (`RimMat`) se dibuja encima de las paredes del borde (medido: la
    /// pared de x=0..1 queda tapada al 100 %). Las paredes del borde se siguen
    /// probando por el rectángulo, pero la medida de «el mapa está en pantalla» se
    /// hace sobre el interior, que es lo que el jugador ve del laberinto.</summary>
    private const int EdgeRing = 1;

    private const string FrameFile = "antsim-wall-frame.png";
    private const string MarkedFile = "antsim-wall-marked.png";

    private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    public static string Run()
    {
        var sb = new StringBuilder("WALL");
        if (!Application.isPlaying)
            return sb.Append("|error=fuera de Play: las paredes se miden sobre la partida viva").ToString();

        var presenter = UnityEngine.Object.FindAnyObjectByType<SimPresenterBehaviour>();
        if (presenter == null)
            return sb.Append("|error=no hay SimPresenterBehaviour en la escena: ¿abriste Game.unity?").ToString();

        var header = presenter.Presenter != null ? presenter.Presenter.Header : null;
        if (header == null)
            return sb.Append("|error=sin cabecera de stream: el CLI no arrancó (no hay terreno que medir)").ToString();
        if (!header.HasMaze)
            return sb.Append("|error=la cabecera NO trae terreno (maze=0): la partida no pidió laberinto").ToString();

        var cam = Camera.main;
        if (cam == null) return sb.Append("|error=no hay Camera.main").ToString();
        if (!cam.orthographic)
            return sb.Append("|error=la cámara del juego no es ortográfica: la proyección supuesta es la cenital").ToString();

        var mesh = presenter.WallMesh;
        var material = presenter.WallMaterial;
        if (mesh == null) return sb.Append("|error=el presenter NO tiene WallMesh cableado (la escena no dibuja paredes)").ToString();
        if (material == null) return sb.Append("|error=el presenter NO tiene WallMaterial cableado (la escena no dibuja paredes)").ToString();

        float cell = WorldUnits.PerCell;
        int grid = header.Grid > 0 ? header.Grid : presenter.Grid;
        if (grid <= 0) return sb.Append("|error=la cabecera no trae grid utilizable").ToString();

        var expected = (Color32)material.color;

        // ── El render de medida, con el aspecto de la RT ─────────────────────
        // `Camera.aspect` va con el target: se fija al de la RenderTexture para que
        // la proyección de las cajas y la del frame sean LA MISMA. Se restaura al
        // final aunque algo falle (la Game view no se queda deformada).
        float prevAspect = cam.aspect;
        var rt = RenderTexture.GetTemporary(FrameW, FrameH, 24, RenderTextureFormat.ARGB32);
        var prevTarget = cam.targetTexture;
        var prevActive = RenderTexture.active;
        Color32[] px;
        int W = FrameW, H = FrameH;
        try
        {
            cam.aspect = FrameW / (float)FrameH;
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(FrameW, FrameH, TextureFormat.RGB24, false);
            try
            {
                tex.ReadPixels(new Rect(0, 0, FrameW, FrameH), 0, 0);
                tex.Apply();
                px = tex.GetPixels32();
                W = tex.width; H = tex.height;
            }
            finally { UnityEngine.Object.Destroy(tex); }

            return Measure(sb, presenter, header, cam, mesh, material, expected, cell, grid, px, W, H);
        }
        catch (Exception ex)
        {
            return sb.Append("|error=no se pudo renderizar/medir la cámara: ").Append(ex.GetType().Name).ToString();
        }
        finally
        {
            cam.targetTexture = prevTarget;
            cam.aspect = prevAspect;
            RenderTexture.active = prevActive;
            RenderTexture.ReleaseTemporary(rt);
        }
    }

    /// <summary>La medida propiamente dicha. Corre con la cámara ya configurada
    /// para la RT (aspecto incluido), porque proyecta con ella.</summary>
    private static string Measure(StringBuilder sb, SimPresenterBehaviour presenter,
        GameStreamParser.HeaderView header, Camera cam, Mesh mesh, Material material,
        Color32 expected, float cell, int grid, Color32[] px, int W, int H)
    {
        int rects = header.Maze.Count;
        int draws = presenter.LastWallDraws;

        // Las celdas con pared, del propio terreno de la cabecera.
        var wallCells = new bool[grid * grid];
        for (int i = 0; i < header.Maze.Count; i++)
        {
            var r = header.Maze[i];
            for (int y = Mathf.Max(0, r.Y); y < Mathf.Min(grid, r.Y + r.H); y++)
                for (int x = Mathf.Max(0, r.X); x < Mathf.Min(grid, r.X + r.W); x++)
                    wallCells[y * grid + x] = true;
        }

        // ── 1. Celda a celda: ¿está el MAPA en pantalla? ─────────────────────
        // El corazón de la medida. Cada celda interior se proyecta con la cámara
        // real y su píxel se clasifica contra el color del muro; el veredicto es el
        // acuerdo con `wallCells`. Esto es lo que ningún check de estado puede dar:
        // una pared con la matriz correcta y la capa equivocada desaparece aquí.
        int cells = 0, agree = 0, wallCellsN = 0, miss = 0, open = 0;
        const float wallLift = 0.35f;   // el mismo del presenter (BuildWallsIfNeeded)
        for (int y = EdgeRing; y < grid - EdgeRing; y++)
        {
            for (int x = EdgeRing; x < grid - EdgeRing; x++)
            {
                bool isWall = wallCells[y * grid + x];
                float lift = isWall ? wallLift : 0f;
                if (!ToPixel(cam, new Vector3((x + 0.5f) * cell, lift, (y + 0.5f) * cell), W, H,
                             out int sx, out int sy))
                    continue;
                var c = MedianAt(px, W, H, sx, sy);
                bool painted = Near(c, expected, ColorTol);
                cells++;
                if (isWall) wallCellsN++;
                if (painted == isWall) agree++;
                else if (isWall) miss++;
                else open++;
            }
        }

        // ── 2. Muestra de paredes y controles, con su caja en píxeles ────────
        // Los rectángulos que tocan el borde se descartan: el marco del tablero se
        // pinta ENCIMA de ellos (medido: cobertura 0) y un centro tapado por el
        // marco diría «no hay pared» sobre un muro que sí está. Las paredes del
        // borde siguen contadas en `draws`/celdas; lo que se muestrea es el interior.
        var walls = new List<WallBox>();
        for (int i = 0; i < header.Maze.Count; i++)
        {
            var r = header.Maze[i];
            if (r.W <= 0 || r.H <= 0) continue;
            if (r.X < EdgeRing || r.Y < EdgeRing ||
                r.X + r.W > grid - EdgeRing || r.Y + r.H > grid - EdgeRing) continue;
            var box = Project(cam, r, cell, wallLift, W, H);
            if (!box.Visible) continue;
            if (box.W < 4 || box.H < 4 || box.W * box.H < 25) continue;
            walls.Add(new WallBox { Rect = r, Box = box });
        }
        if (walls.Count == 0)
            return sb.Append("|error=ninguna pared interior cae en el encuadre de la cámara (¿grid fuera de cuadro?)")
                     .Append("|rects=").Append(rects).Append("|draws=").Append(draws).ToString();

        walls.Sort((a, b) => (b.Box.W * b.Box.H).CompareTo(a.Box.W * a.Box.H));
        var selected = Spread(walls, MaxWalls);

        var controls = new List<Control>();
        for (int y = ControlClearance; y < grid - ControlClearance && controls.Count < MaxControls * 4; y += 3)
            for (int x = ControlClearance; x < grid - ControlClearance && controls.Count < MaxControls * 4; x += 3)
            {
                if (!Open(wallCells, grid, x, y, ControlClearance)) continue;
                var v = ProjectCell(cam, x, y, 1, 1, cell, 0f, W, H);
                if (!v.Visible || v.W < 3 || v.H < 3) continue;
                controls.Add(new Control { X = x, Y = y, Box = v });
            }
        var pickedControls = Spread(controls, MaxControls);

        // La evidencia se guarda ANTES de publicar: un volcado sin PNG no es una
        // medida de píxeles (el analizador falla cerrado si falta).
        SavePng(px, W, H, FrameFile);
        SaveMarked(px, W, H, selected, pickedControls);

        sb.Append("|stage=measure")
          .Append("|frame=").Append(W).Append('x').Append(H)
          .Append("|row0=top")
          .Append("|grid=").Append(grid)
          .Append("|cell=").Append(F(cell))
          .Append("|rects=").Append(rects)
          .Append("|draws=").Append(draws)
          .Append("|mesh=").Append(mesh.name)
          .Append("|mat=").Append(material.name)
          .Append("|shader=").Append(material.shader != null ? material.shader.name : "-")
          .Append("|wallrgb=").Append(expected.r).Append(',').Append(expected.g).Append(',').Append(expected.b)
          .Append("|colorspace=").Append(QualitySettings.activeColorSpace == ColorSpace.Linear ? "linear" : "gamma")
          .Append("|cells=").Append(cells)
          .Append("|wallcells=").Append(wallCellsN)
          .Append("|agree=").Append(agree)
          .Append("|miss=").Append(miss)
          .Append("|false=").Append(open)
          .Append("|selected=").Append(selected.Count)
          .Append("|controls=").Append(pickedControls.Count)
          .Append("|files=").Append(FrameFile).Append(',').Append(MarkedFile);

        // Se publica el color MEDIDO en el centro de cada caja: si mañana el
        // shader o el espacio de color cambian, el analizador tiene el número del
        // frame delante y el desvío contra el material se ve sin abrir el editor.
        for (int i = 0; i < selected.Count; i++)
        {
            var w = selected[i];
            var c = MedianAt(px, W, H, w.Box.X + w.Box.W / 2, w.Box.Y + w.Box.H / 2);
            sb.Append("|R|i=").Append(i)
              .Append("|cell=").Append(w.Rect.X).Append(',').Append(w.Rect.Y)
              .Append(',').Append(w.Rect.W).Append(',').Append(w.Rect.H)
              .Append("|box=").Append(w.Box.X).Append(',').Append(w.Box.Y)
              .Append(',').Append(w.Box.W).Append('x').Append(w.Box.H)
              .Append("|rgb=").Append(c.r).Append(',').Append(c.g).Append(',').Append(c.b);
        }
        for (int i = 0; i < pickedControls.Count; i++)
        {
            var ctl = pickedControls[i];
            var c = MedianAt(px, W, H, ctl.Box.X + ctl.Box.W / 2, ctl.Box.Y + ctl.Box.H / 2);
            sb.Append("|C|i=").Append(i)
              .Append("|cell=").Append(ctl.X).Append(',').Append(ctl.Y)
              .Append("|box=").Append(ctl.Box.X).Append(',').Append(ctl.Box.Y)
              .Append(',').Append(ctl.Box.W).Append('x').Append(ctl.Box.H)
              .Append("|rgb=").Append(c.r).Append(',').Append(c.g).Append(',').Append(c.b);
        }
        return sb.ToString();
    }

    // ── Proyección ───────────────────────────────────────────────────────────
    private struct Box
    {
        public int X, Y, W, H;   // píxeles con fila 0 = ARRIBA (como el PNG)
        public bool Visible;
    }

    private sealed class WallBox
    {
        public GameStreamParser.MazeRect Rect;
        public Box Box;
    }

    private sealed class Control
    {
        public int X, Y;
        public Box Box;
    }

    /// <summary>Caja en píxeles (fila 0 = ARRIBA) del rectángulo de una pared,
    /// proyectando sus cuatro esquinas con la cámara real. Ortográfica y cenital
    /// la caja es axis-aligned; se prueban las cuatro esquinas igual para no
    /// depender de eso.</summary>
    private static Box Project(Camera cam, GameStreamParser.MazeRect r, float cell, float lift, int W, int H)
        => ProjectCell(cam, r.X, r.Y, r.W, r.H, cell, lift, W, H);

    private static Box ProjectCell(Camera cam, int x, int y, int w, int h, float cell, float lift, int W, int H)
    {
        Vector3 a = new Vector3(x * cell, lift, y * cell);
        Vector3 b = new Vector3((x + w) * cell, lift, (y + h) * cell);
        var corners = new[]
        {
            new Vector3(a.x, a.y, a.z), new Vector3(b.x, a.y, a.z),
            new Vector3(a.x, a.y, b.z), new Vector3(b.x, a.y, b.z)
        };
        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;
        bool anyFront = false;
        for (int i = 0; i < corners.Length; i++)
        {
            var v = cam.WorldToViewportPoint(corners[i]);
            if (v.z <= 0f) continue;
            anyFront = true;
            float sx = v.x * W;
            float sy = (1f - v.y) * H;   // PNG: fila 0 arriba
            if (sx < minX) minX = sx;
            if (sx > maxX) maxX = sx;
            if (sy < minY) minY = sy;
            if (sy > maxY) maxY = sy;
        }
        if (!anyFront) return new Box { Visible = false };

        int x0 = Mathf.FloorToInt(minX), y0 = Mathf.FloorToInt(minY);
        int x1 = Mathf.CeilToInt(maxX) - 1, y1 = Mathf.CeilToInt(maxY) - 1;
        bool inside = x0 >= 1 && y0 >= 1 && x1 <= W - 2 && y1 <= H - 2 && x1 >= x0 && y1 >= y0;
        return new Box { X = x0, Y = y0, W = x1 - x0 + 1, H = y1 - y0 + 1, Visible = inside };
    }

    /// <summary>Un punto del mundo (a la altura `lift`) a píxel del PNG.</summary>
    private static bool ToPixel(Camera cam, Vector3 world, int W, int H, out int sx, out int sy)
    {
        var v = cam.WorldToViewportPoint(world);
        sx = sy = -1;
        if (v.z <= 0f) return false;
        sx = Mathf.FloorToInt(v.x * W);
        sy = Mathf.FloorToInt((1f - v.y) * H);
        return sx >= 1 && sy >= 1 && sx < W - 1 && sy < H - 1;
    }

    /// <summary>Elige hasta `max` elementos REPARTIDOS por la lista (ya ordenada
    /// por tamaño): quedarse con los primeros concentraría la medida en una zona
    /// y un material mal puesto en el resto del tablero pasaría.</summary>
    private static List<T> Spread<T>(List<T> all, int max)
    {
        var picked = new List<T>();
        if (all.Count == 0) return picked;
        if (all.Count <= max)
        {
            picked.AddRange(all);
            return picked;
        }
        for (int i = 0; i < max; i++)
        {
            int idx = (int)((long)i * all.Count / max);
            if (idx >= all.Count) idx = all.Count - 1;
            picked.Add(all[idx]);
        }
        return picked;
    }

    private static bool Open(bool[] cells, int grid, int x, int y, int r)
    {
        for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= grid || ny >= grid) return false;
                if (cells[ny * grid + nx]) return false;
            }
        return true;
    }

    // ── Píxeles ──────────────────────────────────────────────────────────────
    private static bool Near(Color32 a, Color32 b, int tol)
        => Mathf.Abs(a.r - b.r) <= tol && Mathf.Abs(a.g - b.g) <= tol && Mathf.Abs(a.b - b.b) <= tol;

    /// <summary>Color de un parche de 3×3 con la fila en coordenadas del PNG
    /// (0 = ARRIBA): mediana por canal. La mediana, y no la media, para que una
    /// hormiga o un ítem que cruce el punto no decida la muestra.</summary>
    private static Color32 MedianAt(Color32[] px, int W, int H, int xTop, int yTop)
    {
        var rs = new List<int>(); var gs = new List<int>(); var bs = new List<int>();
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int x = xTop + dx, y = H - 1 - (yTop + dy);   // `GetPixels32` empieza por ABAJO
                if (x < 0 || y < 0 || x >= W || y >= H) continue;
                var c = px[y * W + x];
                rs.Add(c.r); gs.Add(c.g); bs.Add(c.b);
            }
        return new Color32((byte)Median(rs), (byte)Median(gs), (byte)Median(bs), 255);
    }

    private static int Median(List<int> values)
    {
        if (values.Count == 0) return 0;
        values.Sort();
        return values[values.Count / 2];
    }

    // ── Evidencia en PNG ────────────────────────────────────────────────────
    private static void SavePng(Color32[] px, int W, int H, string file)
    {
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.SetPixels32(px);
        tex.Apply();
        try { File.WriteAllBytes(TempPath(file), tex.EncodeToPNG()); }
        catch (Exception) { /* el volcado de texto sigue siendo la evidencia numérica */ }
        UnityEngine.Object.Destroy(tex);
    }

    /// <summary>La hoja que se mira: el frame con el rectángulo ESPERADO de cada
    /// pared (magenta) y el punto de cada control (cian). Si las paredes no están,
    /// se ve el hueco donde deberían estar.</summary>
    private static void SaveMarked(Color32[] px, int W, int H,
                                   List<WallBox> walls, List<Control> controls)
    {
        var copy = (Color32[])px.Clone();
        for (int i = 0; i < walls.Count; i++)
            Rectangle(copy, W, H, walls[i].Box, new Color32(255, 0, 255, 255));
        for (int i = 0; i < controls.Count; i++)
        {
            var b = controls[i].Box;
            Cross(copy, W, H, b.X + b.W / 2, b.Y + b.H / 2, 3, new Color32(0, 255, 255, 255));
        }
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.SetPixels32(copy);
        tex.Apply();
        try { File.WriteAllBytes(TempPath(MarkedFile), tex.EncodeToPNG()); }
        catch (Exception) { }
        UnityEngine.Object.Destroy(tex);
    }

    private static void Rectangle(Color32[] px, int W, int H, Box b, Color32 c)
    {
        for (int x = b.X; x < b.X + b.W; x++)
        {
            Pinta(px, W, H, x, b.Y, c);
            Pinta(px, W, H, x, b.Y + b.H - 1, c);
        }
        for (int y = b.Y; y < b.Y + b.H; y++)
        {
            Pinta(px, W, H, b.X, y, c);
            Pinta(px, W, H, b.X + b.W - 1, y, c);
        }
    }

    private static void Cross(Color32[] px, int W, int H, int x, int y, int r, Color32 c)
    {
        for (int d = -r; d <= r; d++)
        {
            Pinta(px, W, H, x + d, y, c);
            Pinta(px, W, H, x, y + d, c);
        }
    }

    /// <summary>`GetPixels32` usa fila 0 = ABAJO; el PNG y las cajas usan fila 0 =
    /// ARRIBA. La conversión vive aquí y en `MedianAt`: una sola idea.</summary>
    private static void Pinta(Color32[] px, int W, int H, int x, int yTop, Color32 c)
    {
        int y = H - 1 - yTop;
        if (x < 0 || y < 0 || x >= W || y >= H) return;
        px[y * W + x] = c;
    }

    private static string TempPath(string file)
        => Path.Combine(Path.Combine(Application.dataPath, ".."), Path.Combine("Temp", file));
}

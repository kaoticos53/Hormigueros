using System;
using System.Collections.Generic;
using AntSim.Core.Sim;

namespace AntSim.Core.World;

/// <summary>
/// Terreno con obstáculos (F6): un laberinto de paredes de una celda sobre la
/// MISMA rejilla de las feromonas (celda = <see cref="SimConstants.CellSizeUnits"/>
/// unidades). Lo construye <see cref="WorldSim"/> desde la semilla y es la única
/// fuente de verdad: la colisión, los canales 11–13, el respawn de comida y los
/// rectángulos que la vista pinta salen de aquí.
///
/// <para><b>Por qué un laberinto CON BUCLES.</b> Un árbol de expansión perfecto
/// deja callejones sin salida: un cerebro MLP que no planifica se queda atrapado
/// y la colonia muere de hambre por construcción del terreno, no por su política.
/// El <em>braid</em> abre una fracción de los lados que el árbol no usó, así que
/// siempre hay una ruta alternativa y el laberinto sigue leyéndose como laberinto
/// (paredes con puertas, no un campo abierto).</para>
///
/// <para><b>Geometría.</b> Bloque de 8 celdas (64 u): 1 celda de pared (8 u) y
/// 7 de interior (56 u de corredor). Las puertas miden 2 celdas (16 u), que es
/// más ancho que una hormiga (≈14 u de largo, 4,6 de ancho). Los nidos caen justo
/// sobre una línea de pared (world·(i+1)/(colonies+1) es múltiplo de 64 en grid
/// 96), así que se despeja una plaza circular de <see cref="PlazaRadius"/> u
/// alrededor de cada uno: sin ella, las fundadoras nacerían dentro del muro.</para>
///
/// <para><b>Determinismo.</b> La generación usa un <see cref="DeterministicRandom"/>
/// PROPIO (semilla del mundo con una sal): no consume el RNG del mundo, así que
/// un mundo sin laberinto conserva su secuencia de ítems byte a byte. El bitmap
/// entra en <see cref="WorldSim.HashLine"/> y en los checkpoints, de modo que
/// cargar y re-ejecutar reproduce el mismo mundo.</para>
/// </summary>
public sealed class MazeLayout
{
    /// <summary>Celdas por bloque de sala (pared incluida).</summary>
    public const int BlockCells = 8;
    /// <summary>Celdas de interior por bloque (7).</summary>
    public const int RoomCells = BlockCells - 1;
    /// <summary>
    /// Ancho de puerta en celdas (3 ⇒ 24 u). Medido: con 2 celdas (16 u) el paso
    /// libre para el CENTRO de la hormiga queda en 8 u y una política sin memoria
    /// no lo encuentra — 0 pickups en 7.200 ticks. Con 24 u hay sitio para cruzar
    /// en ángulo sin clavar el morro en la jamba.
    /// </summary>
    public const int DoorCells = 3;
    /// <summary>Fracción de lados no usados por el árbol que se abren (bucles).</summary>
    public const float BraidFraction = 0.40f;
    /// <summary>Radio de la plaza despejada alrededor de cada nido (u).</summary>
    public const float PlazaRadius = 96f;
    /// <summary>Radio físico de la hormiga para la colisión (u): algo más que su
    /// semiancho (2,3 u) para que el cuerpo no roce la pared al girar.</summary>
    public const float AntRadius = 4f;
    /// <summary>Distancia mínima a una pared para que un ítem sea alcanzable (u).</summary>
    public const float ItemClearance = 6f;
    /// <summary>
    /// Distancia mínima del nido a la que nace un ítem en un mundo con laberinto
    /// (u). El mundo clásico usa 200 u porque el camino es recto; aquí el
    /// laberinto ya impone el rodeo, y con 200 u la comida caía fuera del anillo
    /// que una forrajera sin memoria peina al salir de la plaza (medido en su
    /// día: 0 pickups en 7.200 ticks). Con 80 u la comida aparece a la vista del
    /// nido, en la plaza o en la sala contigua.
    /// </summary>
    public const float ItemSpawnMinDistance = 80f;
    /// <summary>
    /// Factor de densidad de comida con laberinto. Solo ~85 % del área es
    /// transitable y las paredes alargan cada viaje, así que el objetivo por
    /// área del mundo clásico se queda corto: MEDIDO (semilla 42, grid 96, dos
    /// colonias, pool warm-v2, 20.000 ticks) con el objetivo x1 el laberinto
    /// daba 11 pickups y 3 descargas frente a 58/44 del mundo abierto, y las
    /// colonias se apagaban antes de 20.000 ticks. Con x4,5 son 35/12 y la
    /// demografía empata con el clásico (37 y 26 adultas frente a 40 y 27).
    /// No se sube más: la diferencia que queda es de NAVEGACIÓN (la vuelta con
    /// carga se pierde), no de comida disponible, y llenar el mapa de ítems no
    /// la arregla — la arregla la evolución por nacimientos.
    /// </summary>
    public const float ItemDensityFactor = 4.5f;

    private readonly bool[] _cells; // [y * Width + x] — true = pared
    private readonly float[] _dist; // distancia a la pared en u, por celda (centro)
    private readonly WallRect[] _rects;
    private readonly ulong _hash;

    /// <summary>Un rectángulo de pared en CELDAS (para pintar sin miles de quads).</summary>
    public readonly struct WallRect
    {
        public readonly int X, Y, W, H;
        public WallRect(int x, int y, int w, int h) { X = x; Y = y; W = w; H = h; }
        public int CellCount => W * H;
    }

    public int Width { get; }
    public int Height { get; }
    /// <summary>Lado del mapa en unidades de mundo.</summary>
    public float WorldWidth => Width * SimConstants.CellSizeUnits;
    public float WorldHeight => Height * SimConstants.CellSizeUnits;
    /// <summary>Huella estable del bitmap (FNV-1a de 64 bits).</summary>
    public ulong LayoutHash => _hash;
    /// <summary>Rectángulos maximales de pared (orden determinista).</summary>
    public IReadOnlyList<WallRect> Rects => _rects;
    /// <summary>Celdas de pared (para el checkpoint y el stream).</summary>
    public int WallCells { get; }

    private MazeLayout(bool[] cells, int width, int height)
    {
        _cells = cells;
        Width = width;
        Height = height;
        _dist = BuildDistanceField();
        _rects = BuildRects();
        _hash = HashCells();
        int walls = 0;
        for (int i = 0; i < cells.Length; i++) if (cells[i]) walls++;
        WallCells = walls;
    }

    /// <summary>Reconstruye un laberinto desde su bitmap (carga de checkpoints).</summary>
    public static MazeLayout FromCells(bool[] cells, int width, int height)
    {
        if (cells is null) throw new ArgumentNullException(nameof(cells));
        if (width < 4 || height < 4) throw new ArgumentOutOfRangeException(nameof(width));
        if (cells.Length != width * height)
            throw new ArgumentException($"el bitmap tiene {cells.Length} celdas y la rejilla {width}×{height}.", nameof(cells));
        return new MazeLayout((bool[])cells.Clone(), width, height);
    }

    /// <summary>
    /// Laberinto con bucles a partir de la semilla. Los nidos se colocan con la
    /// MISMA fórmula que <see cref="WorldSim"/> (world·(id+1)/(colonies+1),
    /// world·0,5) para despejar sus plazas.
    /// </summary>
    public static MazeLayout Braided(ulong seed, int cells, int colonyCount)
    {
        if (cells < 16) throw new ArgumentOutOfRangeException(nameof(cells));
        if (colonyCount < 1) throw new ArgumentOutOfRangeException(nameof(colonyCount));

        var rng = new DeterministicRandom(seed ^ 0x4D415A4535313241UL); // "MAZE512A"
        var walls = new bool[cells * cells];
        Array.Fill(walls, true);

        int rooms = (cells + BlockCells - 1) / BlockCells;
        bool[] visited = new bool[rooms * rooms];
        // Lados de cada arista: 0 = entre (i-1,j) y (i,j) — vertical; 1 = (i,j-1)/(i,j) — horizontal.
        var treeEdges = new List<(int I, int J, int Side)>();

        // — DFS iterativo sobre las salas, abriendo la puerta de cada arista del árbol —
        var stack = new Stack<(int I, int J)>();
        stack.Push((0, 0));
        visited[0] = true;
        while (stack.Count > 0)
        {
            var (ci, cj) = stack.Peek();
            var options = new List<(int Ni, int Nj, int Side)>(4);
            if (ci > 0 && !visited[(ci - 1) + cj * rooms]) options.Add((ci - 1, cj, 0));
            if (cj > 0 && !visited[ci + (cj - 1) * rooms]) options.Add((ci, cj - 1, 1));
            if (ci + 1 < rooms && !visited[(ci + 1) + cj * rooms]) options.Add((ci + 1, cj, 0));
            if (cj + 1 < rooms && !visited[ci + (cj + 1) * rooms]) options.Add((ci, cj + 1, 1));

            if (options.Count == 0) { stack.Pop(); continue; }

            var pick = options[rng.NextInt(0, options.Count)];
            // El lado se guarda por la sala de MENOR índice (i o j), que es donde
            // está la línea de pared compartida.
            int sideI = Math.Min(ci, pick.Ni);
            int sideJ = Math.Min(cj, pick.Nj);
            treeEdges.Add((sideI, sideJ, pick.Side));
            CarveDoor(walls, cells, sideI, sideJ, pick.Side, doorSlot: 0);
            visited[pick.Ni + pick.Nj * rooms] = true;
            stack.Push((pick.Ni, pick.Nj));
        }

        // — Interiores de sala (siempre abiertos) —
        for (int j = 0; j < rooms; j++)
            for (int i = 0; i < rooms; i++)
                CarveRoom(walls, cells, i, j);

        // — Braid: lados que el árbol NO usó, abiertos con probabilidad fija —
        for (int j = 0; j < rooms; j++)
        {
            for (int i = 0; i < rooms; i++)
            {
                if (i + 1 < rooms && !IsTreeEdge(treeEdges, i, j, 0))
                {
                    if (rng.NextDouble01() < BraidFraction)
                        CarveDoor(walls, cells, i, j, 0, doorSlot: (int)(rng.NextUInt64() % 2UL));
                }
                if (j + 1 < rooms && !IsTreeEdge(treeEdges, i, j, 1))
                {
                    if (rng.NextDouble01() < BraidFraction)
                        CarveDoor(walls, cells, i, j, 1, doorSlot: (int)(rng.NextUInt64() % 2UL));
                }
            }
        }

        // — Plazas de los nidos —
        float world = cells * SimConstants.CellSizeUnits;
        int plazaCells = Math.Max(2, (int)MathF.Ceiling(PlazaRadius / SimConstants.CellSizeUnits));
        for (int c = 0; c < colonyCount; c++)
        {
            float nx = world * (c + 1) / (colonyCount + 1);
            float ny = world * 0.5f;
            int cx = (int)(nx / SimConstants.CellSizeUnits);
            int cy = (int)(ny / SimConstants.CellSizeUnits);
            float r2 = PlazaRadius * PlazaRadius;
            for (int y = Math.Max(0, cy - plazaCells); y <= Math.Min(cells - 1, cy + plazaCells); y++)
                for (int x = Math.Max(0, cx - plazaCells); x <= Math.Min(cells - 1, cx + plazaCells); x++)
                {
                    float px = (x + 0.5f) * SimConstants.CellSizeUnits;
                    float py = (y + 0.5f) * SimConstants.CellSizeUnits;
                    float dx = px - nx, dy = py - ny;
                    if (dx * dx + dy * dy <= r2) walls[y * cells + x] = false;
                }
        }

        return new MazeLayout(walls, cells, cells);
    }

    private static bool IsTreeEdge(List<(int I, int J, int Side)> edges, int i, int j, int side)
    {
        for (int k = 0; k < edges.Count; k++)
            if (edges[k].I == i && edges[k].J == j && edges[k].Side == side) return true;
        return false;
    }

    private static void CarveRoom(bool[] walls, int n, int i, int j)
    {
        int x0 = i * BlockCells + 1;
        int y0 = j * BlockCells + 1;
        int x1 = Math.Min((i + 1) * BlockCells - 1, n - 1);
        int y1 = Math.Min((j + 1) * BlockCells - 1, n - 1);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                walls[y * n + x] = false;
    }

    /// <summary>
    /// Abre una puerta de <see cref="DoorCells"/> celdas en la línea de pared que
    /// separa (i,j) de (i+1,j) —lado 0— o de (i,j+1) —lado 1—. Hay dos ventanas
    /// posibles por lado (arriba/abajo del centro) para que el braid pueda abrir
    /// una segunda puerta sin pisar la del árbol.
    /// </summary>
    private static void CarveDoor(bool[] walls, int n, int i, int j, int side, int doorSlot)
    {
        // Interiores de la sala en la dirección perpendicular a la pared: 7
        // celdas [base+1 .. base+7]. La puerta ocupa el centro (3 celdas). Hay UNA
        // puerta por lado: si el braid abre el mismo lado que el árbol ya usó, no
        // se abre otra — los bucles vienen de abrir lados NUEVOS, y dos puertas
        // contiguas convertirían el lado en un hueco de 6 celdas (una sala sin
        // pared, que deja de leerse como laberinto).
        _ = doorSlot;
        int perpBase = (side == 0 ? j : i) * BlockCells;
        int a = perpBase + 3;
        int b = perpBase + 3 + (DoorCells - 1);
        if (b >= n) { a = perpBase + 1; b = Math.Min(n - 1, a + DoorCells - 1); }

        if (side == 0)
        {
            int x = (i + 1) * BlockCells; // pared entre i e i+1
            if (x >= n) return;
            for (int y = a; y <= b; y++)
                if (y >= 0 && y < n) walls[y * n + x] = false;
        }
        else
        {
            int y = (j + 1) * BlockCells; // pared entre j y j+1
            if (y >= n) return;
            for (int x = a; x <= b; x++)
                if (x >= 0 && x < n) walls[y * n + x] = false;
        }
    }

    // ── Consultas ────────────────────────────────────────────────────────────

    public bool IsWall(int cx, int cy)
        => cx < 0 || cy < 0 || cx >= Width || cy >= Height || _cells[cy * Width + cx];

    /// <summary>Copia del bitmap (checkpoints).</summary>
    public bool[] CopyCells() => (bool[])_cells.Clone();

    /// <summary>
    /// Distancia a la pared en unidades, leída con interpolación bilineal sobre
    /// el campo (centros de celda). Fuera del mapa se considera pared (0).
    /// </summary>
    public float DistanceAt(float x, float y)
    {
        float cell = SimConstants.CellSizeUnits;
        float fx = x / cell - 0.5f;
        float fy = y / cell - 0.5f;
        if (fx < 0f || fy < 0f || fx > Width - 1 || fy > Height - 1) return 0f;
        int x0 = (int)MathF.Floor(fx);
        int y0 = (int)MathF.Floor(fy);
        int x1 = Math.Min(x0 + 1, Width - 1);
        int y1 = Math.Min(y0 + 1, Height - 1);
        x0 = Math.Clamp(x0, 0, Width - 1);
        y0 = Math.Clamp(y0, 0, Height - 1);
        float tx = fx - MathF.Floor(fx);
        float ty = fy - MathF.Floor(fy);
        float v00 = _dist[y0 * Width + x0];
        float v10 = _dist[y0 * Width + x1];
        float v01 = _dist[y1 * Width + x0];
        float v11 = _dist[y1 * Width + x1];
        float top = v00 + (v10 - v00) * tx;
        float bottom = v01 + (v11 - v01) * tx;
        return top + (bottom - top) * ty;
    }

    /// <summary>
    /// Proximidad de obstáculo en [0,1] para los canales táctiles: 1 = pegado a
    /// la pared, 0 = más lejos que <paramref name="reach"/>.
    /// </summary>
    public float ProximityAt(float x, float y, float reach)
    {
        if (reach <= 1e-4f) return 0f;
        return Math.Clamp(1f - DistanceAt(x, y) / reach, 0f, 1f);
    }

    /// <summary>¿El círculo (x, y, radio) toca alguna celda de pared?</summary>
    public bool Blocked(float x, float y, float radius)
    {
        float cell = SimConstants.CellSizeUnits;
        int x0 = (int)MathF.Floor((x - radius) / cell);
        int x1 = (int)MathF.Floor((x + radius) / cell);
        int y0 = (int)MathF.Floor((y - radius) / cell);
        int y1 = (int)MathF.Floor((y + radius) / cell);
        for (int cy = y0; cy <= y1; cy++)
            for (int cx = x0; cx <= x1; cx++)
                if (IsWall(cx, cy)) return true;
        return false;
    }

    /// <summary>Punto donde un ítem puede nacer: abierto y con holgura de pared.</summary>
    public bool ItemFits(float x, float y) => DistanceAt(x, y) >= ItemClearance;

    /// <summary>
    /// Si (x, y) cae dentro de una pared, devuelve el centro de la celda abierta
    /// más cercana (búsqueda en anillos, determinista). Si ya es válido, devuelve
    /// el mismo punto. Lo usa el comando DropFood.
    /// </summary>
    public (float X, float Y) NearestOpen(float x, float y)
    {
        if (ItemFits(x, y)) return (x, y);
        float cell = SimConstants.CellSizeUnits;
        int cx = (int)MathF.Floor(x / cell);
        int cy = (int)MathF.Floor(y / cell);
        for (int ring = 1; ring < Math.Max(Width, Height); ring++)
        {
            for (int dy = -ring; dy <= ring; dy++)
                for (int dx = -ring; dx <= ring; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != ring) continue;
                    int nx = cx + dx, ny = cy + dy;
                    if (IsWall(nx, ny)) continue;
                    float px = (nx + 0.5f) * cell;
                    float py = (ny + 0.5f) * cell;
                    if (ItemFits(px, py)) return (px, py);
                }
        }
        return (x, y);
    }

    // ── Campos y agregados ───────────────────────────────────────────────────

    /// <summary>
    /// Campo de distancia (dos pasadas chamfer 3-4). En celdas de pared vale 0;
    /// en abierto, (d − 0,5)·celda, de modo que la celda contigua a un muro mide
    /// media celda y no una entera. El error del chamfer (&lt;2 %) es irrelevante
    /// para una proximidad normalizada.
    /// </summary>
    private float[] BuildDistanceField()
    {
        int n = Width;
        var d = new float[n * n];
        const float big = 1e9f;
        for (int i = 0; i < d.Length; i++) d[i] = _cells[i] ? 0f : big;
        const float straight = 1f;
        const float diagonal = 1.41421356f;

        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                int idx = y * n + x;
                if (d[idx] == 0f) continue;
                float best = d[idx];
                if (x > 0) best = MathF.Min(best, d[idx - 1] + straight);
                if (y > 0) best = MathF.Min(best, d[idx - n] + straight);
                if (x > 0 && y > 0) best = MathF.Min(best, d[idx - n - 1] + diagonal);
                if (x + 1 < n && y > 0) best = MathF.Min(best, d[idx - n + 1] + diagonal);
                d[idx] = best;
            }
        }
        for (int y = n - 1; y >= 0; y--)
        {
            for (int x = n - 1; x >= 0; x--)
            {
                int idx = y * n + x;
                if (d[idx] == 0f) continue;
                float best = d[idx];
                if (x + 1 < n) best = MathF.Min(best, d[idx + 1] + straight);
                if (y + 1 < n) best = MathF.Min(best, d[idx + n] + straight);
                if (x + 1 < n && y + 1 < n) best = MathF.Min(best, d[idx + n + 1] + diagonal);
                if (x > 0 && y + 1 < n) best = MathF.Min(best, d[idx + n - 1] + diagonal);
                d[idx] = best;
            }
        }

        float cell = SimConstants.CellSizeUnits;
        for (int i = 0; i < d.Length; i++)
            d[i] = d[i] >= big ? Width * cell : MathF.Max(0f, (d[i] - 0.5f) * cell);
        return d;
    }

    /// <summary>
    /// Funde celdas de pared en rectángulos maximales (fila a fila, extendiendo
    /// hacia abajo): la vista pinta ~300 quads en vez de una llamada por celda.
    /// Orden determinista (barrido por filas), así que el stream es estable.
    /// </summary>
    private WallRect[] BuildRects()
    {
        var used = new bool[_cells.Length];
        var rects = new List<WallRect>();
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                int idx = y * Width + x;
                if (!_cells[idx] || used[idx]) continue;
                int w = 1;
                while (x + w < Width && _cells[idx + w] && !used[idx + w]) w++;
                int h = 1;
                bool canGrow = true;
                while (canGrow && y + h < Height)
                {
                    int row = (y + h) * Width + x;
                    for (int k = 0; k < w; k++)
                        if (!_cells[row + k] || used[row + k]) { canGrow = false; break; }
                    if (canGrow) h++;
                }
                for (int yy = 0; yy < h; yy++)
                    for (int xx = 0; xx < w; xx++)
                        used[(y + yy) * Width + x + xx] = true;
                rects.Add(new WallRect(x, y, w, h));
            }
        }
        return rects.ToArray();
    }

    private ulong HashCells()
    {
        const ulong offset = 1469598103934665603UL;
        const ulong prime = 1099511628211UL;
        ulong h = offset;
        h = (h ^ (ulong)Width) * prime;
        h = (h ^ (ulong)Height) * prime;
        for (int i = 0; i < _cells.Length; i++)
            h = (h ^ (_cells[i] ? 1UL : 0UL)) * prime;
        return h;
    }
}

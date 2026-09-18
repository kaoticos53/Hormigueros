using System;
using AntSim.Core.Sim;

namespace AntSim.Core.Pheromone;

/// <summary>
/// Una capa de feromonas sobre un grid de celdas de <see cref="SimConstants.CellSizeUnits"/> u.
///
/// Determinismo:
/// - Depósito:   values[c] = min(values[c] + Q·dt, Cmax)        (Q = tasa en u/s)
/// - Evaporación: exponencial, independiente del dt discreto: factor = exp(−λ·dt)
/// - Difusión:   explícita de 4 vecinos, estable para k ≤ 0.25 por paso;
///               los bordes tratan los vecinos fuera del grid como 0 (pérdida
///               documentada). Nunca produce valores negativos ni overshoot.
///
/// Versionado para el render incremental: cada celda pertenece a un tile de
/// 64×64. Toda escritura que cambia una celda incrementa la versión de su tile
/// y el contador global de mutaciones (equivalente a LayerVersion del contrato).
///
/// **LOD de difusión (F5.3 rodaja 3)** — ver <see cref="LodEnabled"/>: las tres
/// operaciones visitan solo los BLOQUES con soporte (celdas no nulas) en vez del
/// grid completo. Es exacto, no aproximado: la difusión del proyecto nunca llena
/// una celda nula (el original dejaba `scr[i] = 0` donde `v ≤ 0`), así que las
/// celdas nulas no pueden cambiar y visitarlas era trabajo puro. Los pines de
/// hash de CI son el arnés de esta afirmación.
/// </summary>
public sealed class PheromoneLayer
{
    public const int TileSize = 64;

    /// <summary>Lado del bloque del LOD de difusión. Más fino que el tile de
    /// render (64) a propósito: el tile de render mide envíos, el bloque de LOD
    /// mide trabajo por tick, y con 16 un rastro fino activa pocos bloques.</summary>
    public const int LodBlockSize = 16;

    private readonly float[] _values;
    private readonly float[] _scratch;
    private readonly uint[] _tileVersions;
    private readonly float _cellMax;

    // — Estructura del LOD: soporte por bloque + regiones a visitar —
    private readonly int[] _blockNonZero;   // celdas no nulas por bloque
    private readonly int[] _activeBlocks;   // índices de bloque con soporte (ascendente)
    private int _activeBlockCount;
    private bool _blocksDirty = true;       // el soporte cambió: reconstruir la lista
    private int[] _regions = new int[4];    // x0,y0,x1,y1 por región
    private int _regionCount;

    private ulong _mutationCounter;
    private int _nonZeroCells;

    public int Width { get; }
    public int Height { get; }

    public int TilesX => (Width + TileSize - 1) / TileSize;
    public int TilesY => (Height + TileSize - 1) / TileSize;

    /// <summary>Bloques del LOD en cada eje.</summary>
    public int BlocksX => (Width + LodBlockSize - 1) / LodBlockSize;
    public int BlocksY => (Height + LodBlockSize - 1) / LodBlockSize;

    /// <summary>Contador total de cambios (no decrece; identifica "capa sucia").</summary>
    public ulong MutationCount => _mutationCounter;

    /// <summary>Celdas con valor no nulo (soporte real del rastro).</summary>
    public int NonZeroCells => _nonZeroCells;

    /// <summary>Número de bloques con soporte. Reconstruye la lista si hace
    /// falta: leerlo justo después de un depósito debe decir la verdad, no lo
    /// que quedó de la última operación del mundo (el primer intento de test
    /// tropezó con esa diferencia).</summary>
    public int ActiveBlocks
    {
        get
        {
            EnsureActiveBlocks();
            return _activeBlockCount;
        }
    }

    /// <summary>Celdas que la última operación visitó (telemetría del LOD: es la
    /// cifra que se compara contra <see cref="CellCount"/> para saber cuánto
    /// ahorra el LOD en un mundo real).</summary>
    public int ActiveRegionCells { get; private set; }

    /// <summary>
    /// Interruptor de verificación (F5.3 rodaja 3). Con <c>true</c> — por
    /// defecto y en todo el producto — evaporación y difusión visitan solo los
    /// bloques con soporte. Con <c>false</c> visitan el grid completo: es la
    /// implementación de referencia contra la que el test de equivalencia
    /// compara celda a celda. Apagarlo NO cambia el resultado del mundo (es la
    /// afirmación que el test demuestra), solo la velocidad; existe para poder
    /// demostrarlo, no para ajustar nada.
    /// </summary>
    public bool LodEnabled { get; set; } = true;

    public PheromoneLayer(int width, int height, float cellMax = SimConstants.PheromoneCellMax)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        Width = width;
        Height = height;
        _cellMax = cellMax;
        _values = new float[width * height];
        _scratch = new float[width * height];
        _tileVersions = new uint[TilesX * TilesY];
        _blockNonZero = new int[BlocksX * BlocksY];
        _activeBlocks = new int[BlocksX * BlocksY];
    }

    public bool InBounds(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height;

    public float this[int x, int y]
    {
        get => InBounds(x, y) ? _values[y * Width + x] : 0f;
    }

    /// <summary>Deposita amount (normalmente Q·dt) en una celda, con tope Cmax.</summary>
    public bool Deposit(int x, int y, float amount)
    {
        if (!InBounds(x, y) || amount <= 0f) return false;
        int i = y * Width + x;
        float old = _values[i];
        float v = old + amount;
        if (v > _cellMax) v = _cellMax;
        if (v == old) return false;
        _values[i] = v;
        if (old <= 0f && v > 0f)
        {
            // Celda nula → con soporte: el bloque entra en la lista de trabajo.
            _blockNonZero[BlockIndex(x, y)]++;
            _nonZeroCells++;
            _blocksDirty = true;
        }
        MarkDirty(x, y);
        return true;
    }

    /// <summary>Evaporación exponencial: values *= exp(−λ·dt) en celdas no nulas.
    /// CanonMath (F5.2c): exp cross-platform bit-exact — el factor alimenta el
    /// estado del mundo y un ULP diverge el hash.</summary>
    public void Evaporate(float dtSeconds, float lambdaPerSecond)
    {
        float factor = CanonMath.Exp(-lambdaPerSecond * dtSeconds);
        BuildRegions();
        float[] vals = _values;

        for (int r = 0; r < _regionCount; r++)
        {
            int x0 = _regions[r * 4], y0 = _regions[r * 4 + 1];
            int x1 = _regions[r * 4 + 2], y1 = _regions[r * 4 + 3];
            for (int y = y0; y < y1; y++)
            {
                int row = y * Width;
                for (int x = x0; x < x1; x++)
                {
                    int i = row + x;
                    float v = vals[i];
                    if (v <= 0f) continue;
                    float nv = v * factor;
                    if (nv == v) continue;
                    Commit(i, x, y, nv);
                }
            }
        }
    }

    /// <summary>
    /// Difusión explícita de 4 vecinos (un paso). k ≤ 0.25 garantiza estabilidad
    /// y ausencia de valores negativos. Vecinos fuera del grid aportan 0.
    ///
    /// Régimen del proyecto (no es una aproximación nuestra, es el original): una
    /// celda NULA se queda nula — no recibe de sus vecinas. Por eso el soporte
    /// nunca crece al difundir y el LOD puede visitar solo los bloques con
    /// soporte sin cambiar el resultado.
    /// </summary>
    public void Diffuse(float k)
    {
        if (k < 0f || k > 0.25f)
            throw new ArgumentOutOfRangeException(nameof(k), "k debe estar en (0, 0.25] para estabilidad.");
        if (k == 0f) return;

        BuildRegions();
        float[] vals = _values;
        float[] scr = _scratch;
        int w = Width, h = Height;

        // Paso 1: nuevo valor de cada celda CON soporte (lee vecinas de `vals`,
        // que nadie ha tocado todavía; las nulas no se computan: no cambian).
        for (int r = 0; r < _regionCount; r++)
        {
            int x0 = _regions[r * 4], y0 = _regions[r * 4 + 1];
            int x1 = _regions[r * 4 + 2], y1 = _regions[r * 4 + 3];
            for (int y = y0; y < y1; y++)
            {
                int row = y * w;
                for (int x = x0; x < x1; x++)
                {
                    int i = row + x;
                    float v = vals[i];
                    if (v <= 0f) continue;

                    float sum = 0f;
                    if (x > 0) sum += vals[i - 1];
                    if (x + 1 < w) sum += vals[i + 1];
                    if (y > 0) sum += vals[i - w];
                    if (y + 1 < h) sum += vals[i + w];

                    float nv = v * (1f - 4f * k) + k * sum;
                    if (nv < 0f) nv = 0f;
                    scr[i] = nv;
                }
            }
        }

        // Paso 2: escribir solo lo que cambia (idéntico al original celda a celda).
        for (int r = 0; r < _regionCount; r++)
        {
            int x0 = _regions[r * 4], y0 = _regions[r * 4 + 1];
            int x1 = _regions[r * 4 + 2], y1 = _regions[r * 4 + 3];
            for (int y = y0; y < y1; y++)
            {
                int row = y * w;
                for (int x = x0; x < x1; x++)
                {
                    int i = row + x;
                    float old = vals[i];
                    if (old <= 0f) continue; // nula: no cambia (y `scr` puede estar vieja)
                    float nv = scr[i];
                    if (nv == old) continue;
                    Commit(i, x, y, nv);
                }
            }
        }
    }

    /// <summary>Suma de todas las celdas en orden de índice (para hashes canónicos).
    /// NO usa el LOD a propósito: el orden de acumulación de floats forma parte
    /// del hash y saltarse ceros cambiaría el redondeo.</summary>
    public float SumOfValues()
    {
        float sum = 0f;
        for (int i = 0; i < _values.Length; i++) sum += _values[i];
        return sum;
    }

    /// <summary>Cuenta bruta de celdas no nulas (referencia del test de contadores).</summary>
    public int CountNonZeroCells()
    {
        int n = 0;
        for (int i = 0; i < _values.Length; i++) if (_values[i] > 0f) n++;
        return n;
    }

    /// <summary>
    /// Reconstruye la estructura de soporte del LOD desde los valores (contadores
    /// por bloque, celdas no nulas y lista de bloques activos). Obligatorio tras
    /// escribir valores por una vía que no sea Deposit/Evaporate/Diffuse — es lo
    /// que hace la carga de un checkpoint.
    /// </summary>
    public void RebuildSupport()
    {
        Array.Clear(_blockNonZero, 0, _blockNonZero.Length);
        _nonZeroCells = 0;
        int w = Width, h = Height;
        for (int y = 0; y < h; y++)
        {
            int row = y * w;
            int by = y / LodBlockSize;
            for (int x = 0; x < w; x++)
            {
                if (_values[row + x] > 0f)
                {
                    _blockNonZero[by * BlocksX + x / LodBlockSize]++;
                    _nonZeroCells++;
                }
            }
        }
        _blocksDirty = true;
        BuildRegions();
    }

    public uint TileVersion(int tx, int ty)
    {
        if ((uint)tx >= (uint)TilesX || (uint)ty >= (uint)TilesY) return 0;
        return _tileVersions[ty * TilesX + tx];
    }

    /// <summary>Copia completa de valores en orden canónico (para checkpoints/hashes).</summary>
    public void CopyValuesTo(float[] destination)
    {
        if (destination is null || destination.Length < _values.Length)
            throw new ArgumentException("Destino demasiado pequeño.", nameof(destination));
        Array.Copy(_values, destination, _values.Length);
    }

    /// <summary>Versión cruda de un tile por índice lineal (serialización de checkpoints).</summary>
    internal uint RawTileVersion(int index) => _tileVersions[index];

    /// <summary>
    /// Restaura el estado completo de la capa desde un checkpoint .antsave:
    /// valores celda a celda, versiones por tile y contador global de mutaciones.
    /// </summary>
    internal void LoadState(float[] values, uint[] tileVersions, ulong mutationCount)
    {
        if (values is null || values.Length != _values.Length)
            throw new ArgumentException("Los valores no coinciden con el tamaño de la capa.", nameof(values));
        if (tileVersions is null || tileVersions.Length != _tileVersions.Length)
            throw new ArgumentException("Las versiones de tile no coinciden con la teselación.", nameof(tileVersions));
        Array.Copy(values, _values, values.Length);
        Array.Copy(tileVersions, _tileVersions, tileVersions.Length);
        _mutationCounter = mutationCount;
        RebuildSupport(); // el soporte del LOD no viaja en el checkpoint: se deriva
    }

    public int CellCount => _values.Length;

    private int BlockIndex(int x, int y) => (y / LodBlockSize) * BlocksX + (x / LodBlockSize);

    /// <summary>Escribe un valor nuevo y mantiene la contabilidad del soporte.</summary>
    private void Commit(int i, int x, int y, float nv)
    {
        _values[i] = nv;
        if (nv == 0f)
        {
            _blockNonZero[BlockIndex(x, y)]--;
            _nonZeroCells--;
            _blocksDirty = true;
        }
        MarkDirty(x, y);
    }

    /// <summary>
    /// Regiones a visitar. Con el LOD encendido, una por bloque con soporte; con
    /// el LOD apagado (verificación), una sola que cubre el grid entero.
    /// </summary>
    private void BuildRegions()
    {
        if (!LodEnabled)
        {
            if (_regions.Length < 4) _regions = new int[4];
            _regions[0] = 0;
            _regions[1] = 0;
            _regions[2] = Width;
            _regions[3] = Height;
            _regionCount = 1;
            ActiveRegionCells = Width * Height;
            return;
        }

        EnsureActiveBlocks();
        int need = _activeBlockCount * 4;
        if (_regions.Length < need) _regions = new int[Math.Max(4, need)];

        int n = 0;
        int cells = 0;
        for (int b = 0; b < _activeBlockCount; b++)
        {
            int block = _activeBlocks[b];
            int bx = block % BlocksX;
            int by = block / BlocksX;
            int x0 = bx * LodBlockSize, y0 = by * LodBlockSize;
            int x1 = Math.Min(x0 + LodBlockSize, Width);
            int y1 = Math.Min(y0 + LodBlockSize, Height);
            _regions[n++] = x0;
            _regions[n++] = y0;
            _regions[n++] = x1;
            _regions[n++] = y1;
            cells += (x1 - x0) * (y1 - y0);
        }
        _regionCount = _activeBlockCount;
        ActiveRegionCells = cells;
    }

    private void EnsureActiveBlocks()
    {
        if (!_blocksDirty) return;
        int count = 0;
        for (int b = 0; b < _blockNonZero.Length; b++)
            if (_blockNonZero[b] > 0) _activeBlocks[count++] = b;
        _activeBlockCount = count;
        _blocksDirty = false;
    }

    private void MarkDirty(int x, int y)
    {
        int tx = x / TileSize;
        int ty = y / TileSize;
        _tileVersions[ty * TilesX + tx]++;
        _mutationCounter++;
    }
}

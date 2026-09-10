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
/// </summary>
public sealed class PheromoneLayer
{
    public const int TileSize = 64;

    private readonly float[] _values;
    private readonly float[] _scratch;
    private readonly uint[] _tileVersions;
    private ulong _mutationCounter;
    private readonly float _cellMax;

    public int Width { get; }
    public int Height { get; }

    public int TilesX => (Width + TileSize - 1) / TileSize;
    public int TilesY => (Height + TileSize - 1) / TileSize;

    /// <summary>Contador total de cambios (no decrece; identifica "capa sucia").</summary>
    public ulong MutationCount => _mutationCounter;

    public PheromoneLayer(int width, int height, float cellMax = SimConstants.PheromoneCellMax)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        Width = width;
        Height = height;
        _cellMax = cellMax;
        _values = new float[width * height];
        _scratch = new float[width * height];
        _tileVersions = new uint[TilesX * TilesY];
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
        float v = _values[i] + amount;
        if (v > _cellMax) v = _cellMax;
        if (v == _values[i]) return false;
        _values[i] = v;
        MarkDirty(x, y);
        return true;
    }

    /// <summary>Evaporación exponencial: values *= exp(−λ·dt) en celdas no nulas.</summary>
    public void Evaporate(float dtSeconds, float lambdaPerSecond)
    {
        float factor = MathF.Exp(-lambdaPerSecond * dtSeconds);
        for (int y = 0; y < Height; y++)
        {
            int row = y * Width;
            for (int x = 0; x < Width; x++)
            {
                int i = row + x;
                float v = _values[i];
                if (v <= 0f) continue;
                float nv = v * factor;
                if (nv == v) continue;
                _values[i] = nv;
                MarkDirty(x, y);
            }
        }
    }

    /// <summary>
    /// Difusión explícita de 4 vecinos (un paso). k ≤ 0.25 garantiza estabilidad
    /// y ausencia de valores negativos. Vecinos fuera del grid aportan 0.
    /// </summary>
    public void Diffuse(float k)
    {
        if (k < 0f || k > 0.25f)
            throw new ArgumentOutOfRangeException(nameof(k), "k debe estar en (0, 0.25] para estabilidad.");
        if (k == 0f) return;

        float[] vals = _values;
        float[] scr = _scratch;
        int w = Width, h = Height;

        for (int y = 0; y < h; y++)
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                int i = row + x;
                float v = vals[i];
                if (v <= 0f) { scr[i] = 0f; continue; }

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

        for (int y = 0; y < h; y++)
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                int i = row + x;
                float nv = scr[i];
                if (nv == vals[i]) continue;
                vals[i] = nv;
                MarkDirty(x, y);
            }
        }
    }

    /// <summary>Suma de todas las celdas en orden de índice (para hashes canónicos).</summary>
    public float SumOfValues()
    {
        float sum = 0f;
        for (int i = 0; i < _values.Length; i++) sum += _values[i];
        return sum;
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
    }

    public int CellCount => _values.Length;

    private void MarkDirty(int x, int y)
    {
        int tx = x / TileSize;
        int ty = y / TileSize;
        _tileVersions[ty * TilesX + tx]++;
        _mutationCounter++;
    }
}

using System;
using System.Collections.Generic;

namespace AntSim.Core.Training;

/// <summary>
/// Escalera de CAPACIDAD del cerebro (F7). El genoma MLP tiene una capa oculta de
/// ancho fijo; cuando el currículo de obstáculos llega a su dificultad máxima y el
/// mejor fitness deja de mejorar, la capa se ensancha un peldaño (8 → 16 → 24)
/// expandiendo los pesos existentes — misma función, más sitio para aprender.
///
/// <para><b>Por qué una clase aparte.</b> La decisión de crecer es pura: depende
/// del mejor fitness observado y de si el terreno ya está en su tope. Sacarla del
/// trainer la hace verificable sin entrenar 30 generaciones de verdad.</para>
///
/// <para><b>Histéresis.</b> Cualquier mejora &gt; 2 % reinicia la ventana; una racha
/// plana de <see cref="GrowthWindow"/> generaciones con el terreno al máximo es la
/// única señal de crecimiento. Un bache corto no ensancha la red.</para>
/// </summary>
public sealed class CapacityLadder
{
    /// <summary>Generaciones planas necesarias para ensanchar la capa oculta.</summary>
    public const int GrowthWindow = 30;
    /// <summary>Mejora relativa del mejor fitness que cuenta como progreso.</summary>
    public const double GrowthImprovement = 0.02;

    private readonly int[] _widths;
    private int _index;
    private int _stagnant;
    private double _best = double.MinValue;

    public CapacityLadder(IReadOnlyList<int> widths)
    {
        if (widths is null || widths.Count == 0)
            throw new ArgumentException("La escalera necesita al menos un ancho.", nameof(widths));
        var copy = new int[widths.Count];
        for (int i = 0; i < widths.Count; i++)
        {
            if (widths[i] < 1) throw new ArgumentOutOfRangeException(nameof(widths), "Los anchos deben ser ≥ 1.");
            if (i > 0 && widths[i] <= widths[i - 1])
                throw new ArgumentOutOfRangeException(nameof(widths), "Los anchos deben ser estrictamente crecientes.");
            copy[i] = widths[i];
        }
        _widths = copy;
    }

    /// <summary>Ancho oculto actual.</summary>
    public int Hidden => _widths[_index];

    /// <summary>Peldaño actual (0 = el inicial).</summary>
    public int Index => _index;

    /// <summary>Ya no quedan peldaños por subir.</summary>
    public bool Exhausted => _index + 1 >= _widths.Length;

    /// <summary>Generaciones planas acumuladas (diagnóstico y tests).</summary>
    public int StagnantGenerations => _stagnant;

    /// <summary>
    /// Registra una generación. Devuelve el ancho NUEVO si toca crecer (0 si no).
    /// <paramref name="atMaxDifficulty"/> apaga la ventana fuera del terreno
    /// máximo: la escalera mide estancamiento en el obstáculo real, no en campo
    /// abierto.
    /// </summary>
    public int Observe(double best, bool atMaxDifficulty)
    {
        if (!atMaxDifficulty)
        {
            _best = double.MinValue;
            _stagnant = 0;
            return 0;
        }

        bool improved = _best == double.MinValue
            || best - _best > Math.Abs(_best) * GrowthImprovement;
        if (improved)
        {
            _best = Math.Max(_best, best);
            _stagnant = 0;
            return 0;
        }

        _stagnant++;
        if (_stagnant < GrowthWindow || Exhausted) return 0;

        _index++;
        _stagnant = 0;
        _best = double.MinValue;
        return Hidden;
    }
}

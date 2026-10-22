using System;

namespace AntSim.Core.Training;

/// <summary>
/// Currículo de OBSTÁCULOS del pre-entrenamiento (F7): la dificultad del terreno
/// no sigue un calendario sino lo que la colonia DEMUESTRA. Arranca en campo
/// abierto y sube cuando la mejor evaluación de la generación cierra ciclos
/// completos (descargas reales) durante toda la ventana; baja cuando se queda sin
/// ninguna. Entre ambos extremos no se mueve — la histéresis evita que una
/// generación mala haga retroceder el terreno y otra buena lo devuelva (ese
/// balancín gastaría generaciones sin enseñar nada).
///
/// <para><b>Por qué descargas y no fitness.</b> El fitness lleva densados
/// (exploración, homing, carry-leg): un genoma puede subirlo paseando y el
/// currículo subiría la dificultad sin que la política sepa volver al nido. La
/// descarga es el ciclo completo del juego y no se puede farmear.</para>
///
/// <para><b>Dificultad en décimas ENTERAS.</b> El estado se guarda como entero
/// (0..10): sumar 0,1 en coma flotante acumula error y la misma receta dejaría
/// de dar el mismo `.antgenome` byte a byte.</para>
///
/// <para>Es determinista y puro respecto a su estado: la secuencia de
/// dificultades es función de las observaciones, no del reloj ni del azar.</para>
/// </summary>
public sealed class ObstacleCurriculum
{
    /// <summary>Peldaño de dificultad (0,1).</summary>
    public const int StepTenths = 1;
    /// <summary>Generaciones seguidas que hay que observar para mover la dificultad.</summary>
    public const int Window = 3;
    /// <summary>Descargas de la mejor colonia que cuentan como «competente».</summary>
    public const int CompetenceUnloads = 3;
    /// <summary>Techo de dificultad (el laberinto del juego).</summary>
    public const int MaxTenths = 10;

    /// <summary>Horizonte corto mientras el terreno es fácil (ticks).</summary>
    public const int EasyTickBudget = 1800;
    /// <summary>Horizonte medio (ticks).</summary>
    public const int MidTickBudget = 2700;
    /// <summary>Horizonte completo del juego (ticks).</summary>
    public const int HardTickBudget = 3600;
    /// <summary>Dificultad a partir de la cual el horizonte es el medio (décimas).</summary>
    public const int MidFromTenths = 5;
    /// <summary>Dificultad a partir de la cual el horizonte es el completo (décimas).</summary>
    public const int HardFromTenths = 9;

    private readonly int[] _window = new int[Window];
    private int _filled;

    /// <summary>Dificultad actual en [0,1].</summary>
    public float Difficulty => _tenths / (float)MaxTenths;

    /// <summary>Dificultad actual en décimas (0..10) — el estado exacto.</summary>
    public int Tenths => _tenths;

    private int _tenths;

    /// <summary>Veces que la dificultad ha subido / bajado (para el reporte).</summary>
    public int Escalations { get; private set; }
    public int Descalations { get; private set; }

    /// <summary>
    /// Con <paramref name="adaptive"/> false la dificultad queda FIJA en
    /// <paramref name="start"/> (validación en un nivel concreto: una pasada final
    /// en el laberinto completo sin que el controlador lo mueva).
    /// </summary>
    public ObstacleCurriculum(float start = 0f, bool adaptive = true)
    {
        if (float.IsNaN(start) || start < 0f || start > 1f)
            throw new ArgumentOutOfRangeException(nameof(start), "La dificultad inicial debe estar en [0,1].");
        _tenths = (int)MathF.Round(start * MaxTenths);
        Adaptive = adaptive;
    }

    /// <summary>Si false, la dificultad no se mueve (nivel fijo de validación).</summary>
    public bool Adaptive { get; }

    /// <summary>
    /// Horizonte de la evaluación para la dificultad ACTUAL: las generaciones
    /// fáciles son baratas (campo abierto no necesita el horizonte de un
    /// laberinto para verse) y el presupuesto crece con el terreno.
    /// </summary>
    public int TickBudget => _tenths < MidFromTenths
        ? EasyTickBudget
        : _tenths < HardFromTenths ? MidTickBudget : HardTickBudget;

    /// <summary>
    /// Registra el resultado de una generación y, si la ventana está completa,
    /// mueve la dificultad un peldaño. Devuelve true si cambió.
    /// </summary>
    public bool Observe(int bestUnloads)
    {
        if (!Adaptive) return false;
        _window[_filled++] = Math.Max(0, bestUnloads);
        if (_filled < Window) return false;

        bool allCompetent = true;
        bool allStuck = true;
        for (int i = 0; i < Window; i++)
        {
            if (_window[i] < CompetenceUnloads) allCompetent = false;
            if (_window[i] != 0) allStuck = false;
        }
        _filled = 0;

        if (allCompetent && _tenths < MaxTenths)
        {
            _tenths += StepTenths;
            Escalations++;
            return true;
        }
        if (allStuck && _tenths > 0)
        {
            _tenths -= StepTenths;
            Descalations++;
            return true;
        }
        return false;
    }
}

using System;

namespace AntSim.Core.Sim;

/// <summary>
/// Matemática canónica multiplataforma (F5.2c): las funciones trascendentes de
/// <see cref="MathF"/> (Tanh/Exp/Log/Sin/Cos) NO están especificadas bit a bit
/// en .NET — cada runtime las delega en la libm del sistema (UCRT en Windows,
/// glibc en Linux) y difieren en 1 ULP. Con sensores que alimentan el cerebro,
/// ese ULP cambia la decisión de una hormiga y el mundo diverge: el hash
/// canónico de Windows y Linux nunca coincide (destapado al reproducir en WSL
/// la CI de ubuntu, que llevaba roja desde el 2026-09-12).
///
/// CanonMath implementa las trascendentes con aritmética IEEE-754 básica
/// (+, −, ×, ÷, sqrt — exactas por el estándar) y series de precisión controlada
/// en DOUBLE interno, reduciendo a float al final: mismo resultado bit a bit en
/// cualquier plataforma. El coste es mayor que la libm nativa (2–5×), pero las
/// llamadas por tick son decenas, no miles.
///
/// Sin estado, sin RNG, determinista por construcción.
/// </summary>
public static class CanonMath
{
    // ————— Exp (float) —————
    // Serie de Taylor con argumento reducido: exp(x) = 2^k · exp(r), r ∈ [−ln2/2, ln2/2].
    // 2^k por manipulación de bits (exacta); exp(r) por serie con 8 términos:
    // el error de truncamiento queda bajo 0.5 ULP de double → float exacto tras
    // el redondeo (los términos caen por debajo del bit 24 mucho antes).

    /// <summary>ln(2) en double (los 53 bits que caben).</summary>
    private const double Ln2 = 0.693147180559945309417232121458176568;

    /// <summary>exp(x) canónico para x ∈ [−87, 87] aprox; fuera, satura a 0/+inf.</summary>
    public static float Exp(float x) => (float)ExpCore(x);

    /// <summary>
    /// Núcleo exp en DOUBLE — interno para Tanh (1 − 2/(e²ˣ+1) cancela en float:
    /// la resta debe ocurrir en double para quedar bajo 1 ULP de float).
    /// </summary>
    private static double ExpCore(double x)
    {
        if (x > 709.0) return double.PositiveInfinity;
        if (x < -745.0) return 0.0;
        if (Math.Abs(x) < 1e-12) return 1.0 + x;

        // k = round(x / ln2); r = x − k·ln2 ∈ [−ln2/2, ln2/2].
        double kd = Math.Round(x / Ln2);            // Math.Round: exacto (half-even)
        double r = x - kd * Ln2;                    // 2 redondeos double: error ≤ 1 ULP double

        // Serie: 1 + r + r²/2! + ... (r ≤ 0.3466 → r⁹/9! < 4e-11, sobra para double
        // tras la escala; el término 9 domina el residuo bajo 0.5 ULP de double).
        double term = 1.0;
        double sum = 1.0;
        term *= r; sum += term;                     // r
        term *= r / 2; sum += term;                 // r²/2
        term *= r / 3; sum += term;                 // r³/6
        term *= r / 4; sum += term;
        term *= r / 5; sum += term;
        term *= r / 6; sum += term;
        term *= r / 7; sum += term;
        term *= r / 8; sum += term;
        sum += term * r / 9;                        // r⁹/9!

        // 2^k por bits: (k + 1023) << 52 en el exponente de un double. Exacto
        // para |k| ≤ 1023 (holgado: x ≤ 709 ⇒ k ≤ 1023 justo — clamp de seguridad).
        if (kd < -1000) return 0.0;
        if (kd > 1000) return double.PositiveInfinity;
        double scale = BitConverter.Int64BitsToDouble((long)(kd + 1023) << 52);
        return sum * scale;
    }

    // ————— Log (float, x > 0) —————
    // log(x) = m·ln2 + log(1+r), con x = m·2^e normalizada a m ∈ [√2/2, √2]
    // (reducción estándar) y serie de Atanh: log((1+r)/(1−r)) = 2·atanh(r),
    // r ∈ [−0.1716, 0.1716] — convergencia cuadrática, 7 términos alcanzan
    // precisión double.

    public static float Log(float x)
    {
        if (float.IsNaN(x) || x < 0f) return float.NaN;
        if (x == 0f) return float.NegativeInfinity;
        if (float.IsPositiveInfinity(x)) return float.PositiveInfinity;

        // Descomposición bit a bit: e = exponente IEEE, m = mantisa en [1,2).
        // Reducción a m ∈ [√2/2, √2): si m > √2, m/2 y e+1 (misma representación
        // exacta de la mantisa: dividir por 2 es restar 1 al exponente).
        int bits = BitConverter.SingleToInt32Bits(x);
        int e = ((bits >> 23) & 0xFF) - 127;
        double m = BitConverter.Int32BitsToSingle((bits & ~(0xFF << 23)) | (127 << 23));
        if (m > 1.41421356237309504880)
        {
            m *= 0.5;
            e += 1;
        }
        // r = (m−1)/(m+1) ∈ [−0.1716, 0.1716].
        double r = (m - 1.0) / (m + 1.0);
        double r2 = r * r;
        // atanh(r) = r·(1 + r²/3 + r⁴/5 + r⁶/7 + r⁸/9 + r¹⁰/11 + r¹²/13 + r¹⁴/15):
        // Horner DESDE el coeficiente más interno (1/15) — el residuo cae
        // r¹⁶·c < 3e-14, precisión double de sobra.
        double at = 1.0 / 15;
        at = 1.0 / 13 + r2 * at;
        at = 1.0 / 11 + r2 * at;
        at = 1.0 / 9 + r2 * at;
        at = 1.0 / 7 + r2 * at;
        at = 1.0 / 5 + r2 * at;
        at = 1.0 / 3 + r2 * at;
        at = 1.0 + r2 * at;
        double logm = 2.0 * r * at;
        return (float)(logm + e * Ln2);              // e·ln2 exacto en double para |e| ≤ 126
    }

    // ————— Tanh (float) —————
    // tanh(x) = 1 − 2/(exp(2x)+1) para x ≥ 0 (simetría impar para x < 0).
    // Con |x| ≥ 9 el resultado es 1f exacto en float (2/(e^18+1) < 2^-24).

    public static float Tanh(float x)
    {
        if (MathF.Abs(x) < 1e-8f) return x;          // tanh(x) ≈ x bajo ruido
        if (x >= 9f) return 1f;
        if (x <= -9f) return -1f;
        // Núcleo en DOUBLE: la resta 1 − t cancela cifras y en float amplificaría
        // el redondeo a >10 ULP (medido). En double el error queda bajo 1 ULP float.
        double e = ExpCore(2.0 * Math.Abs(x));
        double t = 2.0 / (e + 1.0);                  // en (0, 1]
        double pos = 1.0 - t;
        return (float)(x >= 0f ? pos : -pos);
    }

    // ————— Sin/Cos (float) —————
    // Reducción de argumento por múltiplos de π/2 (4 cuadrantes) con la
    // constante TWO_OVER_PI en double + el redondeo exacto de Math.Round, y
    // serie de Taylor de grado 7/6 sobre el residuo |r| ≤ π/4. El error de
    // reducción con π/2 en double es < 1e-16 rad para |x| ≤ 4π — sobra para
    // headings acotados por el mundo.

    private const double TwoOverPi = 0.636619772367581343075535053490057448; // 2/π

    public static float Sin(float x)
    {
        // Reducción: n = round(x · 2/π), r = x − n·(π/2). n par → sin(r);
        // n impar → cos(r) con signo (−1)^((n−1)/2).
        double xd = x;
        double nd = Math.Round(xd * TwoOverPi);
        double r = xd - nd * (Math.PI / 2);          // π/2 en double: error < 1 ULP double
        int n = (int)nd;
        return (float)SinCosCore(r, n);
    }

    public static float Cos(float x)
    {
        double xd = x;
        double nd = Math.Round(xd * TwoOverPi);      // cos: desplazado un cuadrante
        double r = xd - nd * (Math.PI / 2);
        int n = (int)nd;
        return (float)SinCosCore(r, n + 1);
    }

    /// <summary>sin(x + n·π/2) vía serie sobre r ∈ [−π/4, π/4].</summary>
    private static double SinCosCore(double r, int n)
    {
        // Signo por cuadrante: (−1)^((n mod 4 − ... )) — tabla de 4 casos.
        bool flip = ((n >> 1) & 1) == 1;             // cada 2 cuadrantes cambia el signo
        bool swap = (n & 1) == 1;                    // cuadrante impar: sin↔cos
        double r2 = r * r;
        // sin(r) y cos(r) por serie (r ≤ π/4 ≈ 0.7854: r⁹/9! < 2e-6 en float,
        // r¹¹/11! < 2e-8 en double — 6 términos por rama alcanzan double).
        double s = r * (1.0 + r2 * (-1.0 / 6 + r2 * (1.0 / 120 + r2 * (-1.0 / 5040 + r2 / 362880))));
        double c = 1.0 + r2 * (-0.5 + r2 * (1.0 / 24 + r2 * (-1.0 / 720 + r2 / 40320)));
        double v = swap ? c : s;
        return flip ? -v : v;
    }

    /// <summary>Hipotenusa canónica: sqrt es IEEE-exact (correctamente redondeado),
    /// así que hypot directo con desbordamiento evitado basta.</summary>
    public static float Sqrt(float v) => MathF.Sqrt(v); // IEEE 754: exacto en toda plataforma
}

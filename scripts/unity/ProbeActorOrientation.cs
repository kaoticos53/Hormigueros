#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using AntSim.Unity.Scripts.Presenter;
using AntSim.Unity.Scripts.UI;
using UnityEngine;

/// <summary>
/// Probe: ¿cada silueta apunta por donde debe?
///
/// POR QUÉ. La caja del actor (`GameSpriteModel`) es X ancho · **Y largo = el eje de
/// dibujo** · Z grosor, y el arte se extruye en Z (cada pieza es un sólido plano
/// visto desde ARRIBA). De ahí salen dos formas de dibujar mal una pieza, y ninguna
/// se ve en un check de estado —las piezas siguen ahí, el mundo sigue apareciendo,
/// los conteos no cambian—:
///
///   · **de lado** (hormiga): la cabeza va hacia +Y, así que si el giro del presenter
///     no la lleva al rumbo del Core, el bicho anda con las patas perpendiculares al
///     avance (medido el 2026-10-10: 90° justos de desvío con `-rumbo` en vez de
///     `90 − rumbo`);
///   · **de canto** (ítem, hoja y sus mordiscos): sin acostar la silueta
///     (`Quaternion.identity` es lo que había) el arte mira al CIELO y la cámara de
///     arriba sólo ve el CANTO de la pieza, así que el dibujo no se lee: la hoja
///     salía como un rectángulo de 0,66 s × 1,10 s —su largo de 1,24 u caía en el eje
///     vertical y el 1,1 iba al grosor— sin punta, sin nervio y sin apuntar a nada;
///   · **las muescas fuera del limbo**: el borde de la hoja es una ELIPSE
///     (0,30 s × 0,62 s), así que repartirlas con un radio único de 0,55 s las deja
///     fuera por los costados y dentro por las puntas —no comen de la hoja—.
///
/// QUÉ MIDE, y por qué así. Toma la rotación/escala del PROPIO presenter
/// (<see cref="SimPresenterBehaviour.ActorRotation"/> para el morro,
/// <see cref="SimPresenterBehaviour.FlatTRS"/> para las siluetas acostadas) y
/// transforma con ella los VÉRTICES DEL ARTE del modelo puro
/// (<see cref="GameSpriteModel"/>), que es lo que el editor extruye en la malla: el
/// vector que sale tiene que caer sobre el rumbo del Core (el mundo mapea `X→X` e
/// `Y→Z`), y el eje de dibujo de un ítem o una hoja tiene que quedar EN EL PLANO del
/// tablero. No reimplementa la convención de `Quaternion.Euler` en ningún sitio:
/// replicarla es justo cómo se cuela el giro que este probe busca.
///
/// Y comprueba que la comprobación TIENE DIENTES: el mismo cálculo con la
/// configuración que se coló (`identity` y las escalas viejas) tiene que FALLAR; si
/// pasara, es que las aserciones no miran lo que dicen mirar.
///
/// Devuelve `ok|resumen | tablas` (por debajo de la tolerancia) o `error|…`. NO vive
/// bajo Assets/: es herramienta del repo.
/// </summary>
public static class ProbeActorOrientation
{
    /// <summary>Tolerancia del desvío, en grados. El morro tiene que caer sobre el
    /// rumbo y el eje de una silueta acostada en el plano; 2° es holgura de redondeo,
    /// no margen de diseño.</summary>
    private const float ToleranceDegrees = 2f;

    /// <summary>Tolerancia de las longitudes, en unidades de la caja normalizada
    /// (las de `GameSpriteModel`): aquí no hay redondeo que valga, es aritmética.</summary>
    private const float ToleranceUnits = 1e-3f;

    /// <summary>Tolerancia del desvío de una muesca respecto al borde elíptico del
    /// limbo, en unidades de la caja normalizada: r = 1 es el borde exacto. 0,02 es
    /// holgura de redondeo, no margen de diseño —el reparto sobre la elipse da r = 1
    /// exacto, y el radio viejo de 0,55 s se desvía hasta 0,83—.</summary>
    private const float BiteTolerance = 0.02f;

    /// <summary>Rumbos de prueba, en grados (el 45 y el 135 para que un cruce de
    /// ejes —que acertaría en los cardinales— no pase por bueno).</summary>
    private static readonly float[] HeadingsDegrees = { 0f, 45f, 90f, 135f, 180f, 225f, 270f, 315f };

    public static string Run()
    {
        var sb = new StringBuilder();
        var fallos = new List<string>();

        // ── El morro de la hormiga, contra el rumbo del Core ────────────────
        float worst = 0f;
        float worstHeading = 0f;

        foreach (float headingDeg in HeadingsDegrees)
        {
            float heading = headingDeg * Mathf.Deg2Rad;
            // El rumbo del Core, en el mundo de Unity (mapa X→X, mapa Y→Z).
            var want = new Vector3(Mathf.Cos(heading), 0f, Mathf.Sin(heading));
            // El morro de la malla: su +Y, el eje de avance de la caja del juego.
            var nose = SimPresenterBehaviour.ActorRotation(heading) * Vector3.up;
            float desvio = Vector3.Angle(want, nose);
            if (desvio > worst) { worst = desvio; worstHeading = headingDeg; }

            string linea = "h=" + headingDeg.ToString("0.#")
                + "° morro=(" + nose.x.ToString("0.###") + "," + nose.z.ToString("0.###") + ")"
                + " rumbo=(" + want.x.ToString("0.###") + "," + want.z.ToString("0.###") + ")"
                + " desvío=" + desvio.ToString("0.#") + "°";
            sb.Append(linea).Append(" | ");
            if (desvio > ToleranceDegrees) fallos.Add(linea);
        }

        // ── El eje de las siluetas ACOSTADAS: ítem y hoja ───────────────────
        // La escala vieja de la hoja se transcribe aquí sólo como CONTRASTE: es la
        // que hace que este bloque tenga dientes (y su fallo se exige más abajo).
        var siluetas = new (string Nombre, GameSpriteModel.Sprite Sprite, Matrix4x4 Descartada)[]
        {
            ("ítem", GameSpriteModel.Sprite.Load,
                Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one)),
            ("hoja", GameSpriteModel.Sprite.Leaf,
                Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(1.1f, 0.5f, 1.1f))),
        };

        float grosor = SimPresenterBehaviour.FlatScale(1f).z * 2f * GameSpriteModel.HalfThickness;
        foreach (var (nombre, sprite, descartada) in siluetas)
        {
            var arte = GameSpriteModel.Build(sprite);

            // La que de verdad dibuja el presenter...
            var (ext, eje) = Medir(arte, SimPresenterBehaviour.FlatTRS(Vector3.zero, 1f));
            string linea = nombre + ": plano=" + ext.x.ToString("0.00") + "×" + ext.z.ToString("0.00")
                + " u · arriba=" + ext.y.ToString("0.00") + " u (grosor " + grosor.ToString("0.00") + ")"
                + " · eje: " + Desvio(eje, ext, grosor);
            sb.Append(linea).Append(" | ");
            if (!Acostada(eje, ext, grosor)) fallos.Add(linea);

            // ...y la que se coló, para exigir que este bloque no pase en verde con ella.
            var (extMal, ejeMal) = Medir(arte, descartada);
            string contraste = nombre + " de canto (identity, " + descartada.lossyScale.x.ToString("0.##")
                + "/" + descartada.lossyScale.y.ToString("0.##") + "/" + descartada.lossyScale.z.ToString("0.##")
                + "): plano=" + extMal.x.ToString("0.00") + "×" + extMal.z.ToString("0.00")
                + " u · arriba=" + extMal.y.ToString("0.00") + " u · eje: " + Desvio(ejeMal, extMal, grosor);
            sb.Append(contraste).Append(" | ");
            // Si la mala pasara las mismas aserciones, este bloque sería decorativo.
            if (Acostada(ejeMal, extMal, grosor)) fallos.Add("la comprobación NO tiene dientes: " + contraste);
        }

        // ── Las MUESCAS comen del BORDE del limbo ───────────────────────────
        // La hoja tiene borde ELÍPTICO (semiejes 0,30 · 0,62 en la caja
        // normalizada, que el presenter escala por `s`), así que repartir las muescas
        // con un radio ÚNICO no las deja en el borde en ningún sitio: fuera por los
        // costados y dentro por las puntas. Se mide con la MISMA función que usa el
        // presenter (`BiteOffset`) contra la elipse implícita (r = 1 sobre el borde).
        float a = GameSpriteModel.LeafHalfWidth;
        float largo = GameSpriteModel.LeafHalfLength;
        float peorMuesca = 0f;
        foreach (int total in new[] { 4, 6, 9, 12 })
        {
            for (int b = 0; b < total; b++)
            {
                var off = SimPresenterBehaviour.BiteOffset(b, total, 1f);
                float r = Mathf.Sqrt((off.x / a) * (off.x / a) + (off.y / largo) * (off.y / largo));
                float desvio = Mathf.Abs(r - 1f);
                if (desvio > peorMuesca) peorMuesca = desvio;
                if (desvio > BiteTolerance)
                    fallos.Add("muesca " + b + "/" + total + ": fuera del borde del limbo (r=" + r.ToString("0.###") + ")");
            }
        }
        sb.Append("muescas: peor desvío del borde=" + peorMuesca.ToString("0.###")
            + " (tolerancia " + BiteTolerance.ToString("0.##") + ") | ");

        // Dientes: el radio viejo de 0,55 s NO cae en el borde; si aquí pasara, el
        // bloque de arriba sería decorativo (daría verde con el defecto puesto).
        bool circuloEnElBorde = true;
        foreach (int total in new[] { 4, 6, 9, 12 })
            for (int b = 0; b < total; b++)
            {
                float ang = (b / (float)total) * Mathf.PI * 2f;
                float x = Mathf.Cos(ang) * 0.55f, z = Mathf.Sin(ang) * 0.55f;
                float r = Mathf.Sqrt((x / a) * (x / a) + (z / largo) * (z / largo));
                if (Mathf.Abs(r - 1f) > BiteTolerance) circuloEnElBorde = false;
            }
        if (circuloEnElBorde)
            fallos.Add("la comprobación NO tiene dientes: el círculo de 0,55 s también cae en el borde del limbo");

        if (fallos.Count > 0)
        {
            return "error|las siluetas NO apuntan a su eje: el morro se desvía "
                 + worst.ToString("0.#") + "° en h=" + worstHeading.ToString("0.#")
                 + "° (tolerancia " + ToleranceDegrees.ToString("0.#") + "°), hay silueta de canto"
                 + " y/o hay muescas fuera del borde del limbo (peor " + peorMuesca.ToString("0.###")
                 + ", tolerancia " + BiteTolerance.ToString("0.##") + ")"
                 + " | " + sb;
        }
        return "ok|el morro cae sobre el rumbo en los 8 rumbos (desvío máximo "
             + worst.ToString("0.#") + "°, tolerancia " + ToleranceDegrees.ToString("0.#")
             + "°), el ítem y la hoja acuestan su eje en el plano del tablero, y las muescas"
             + " comen del borde del limbo (elipse 0,30 × 0,62 s, desvío máximo "
             + peorMuesca.ToString("0.###") + ", tolerancia " + BiteTolerance.ToString("0.##")
             + "; el círculo viejo de 0,55 s falla) | " + sb;
    }

    /// <summary>¿Está la silueta ACOSTADA en el plano del tablero? Las tres
    /// condiciones, y ninguna es adorno: el eje de dibujo fuera del plano (0° = en el
    /// plano, 90° = de pie), mirando al norte (0°) y con lo que sube siendo el GROSOR
    /// y el eje más corto de los tres.</summary>
    private static bool Acostada(Vector3 eje, Vector3 ext, float grosor)
        => FueraDelPlano(eje) <= ToleranceDegrees
        && Vector3.Angle(eje, Vector3.forward) <= ToleranceDegrees
        && Mathf.Abs(ext.y - grosor) <= ToleranceUnits
        && ext.y < ext.x && ext.y < ext.z;

    /// <summary>Cuánto se levanta el eje de dibujo respecto al plano (0° = acostado).</summary>
    private static float FueraDelPlano(Vector3 eje)
        => 90f - Vector3.Angle(eje, Vector3.up);

    private static string Desvio(Vector3 eje, Vector3 ext, float grosor)
        => FueraDelPlano(eje).ToString("0.#") + "° del plano, "
         + Vector3.Angle(eje, Vector3.forward).ToString("0.#") + "° al norte, arriba "
         + ext.y.ToString("0.00") + " u";

    /// <summary>Dibuja el arte con una matriz y devuelve (extensión por eje del mundo,
    /// dirección del eje de dibujo). Se recorren los vértices de las DOS caras de la
    /// extrusión —el arte es un sólido plano— y se mide el arte real, no la caja.</summary>
    private static (Vector3 Ext, Vector3 Eje) Medir(IReadOnlyList<Stamp> arte, Matrix4x4 m)
    {
        var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        foreach (var stamp in arte)
        {
            foreach (var v in stamp.P)
            {
                for (int cara = -1; cara <= 1; cara += 2)
                {
                    var p = m.MultiplyPoint3x4(
                        new Vector3(v.X, v.Y, cara * GameSpriteModel.HalfThickness));
                    min = Vector3.Min(min, p);
                    max = Vector3.Max(max, p);
                }
            }
        }
        return (max - min, m.MultiplyVector(Vector3.up).normalized);
    }
}
#endif

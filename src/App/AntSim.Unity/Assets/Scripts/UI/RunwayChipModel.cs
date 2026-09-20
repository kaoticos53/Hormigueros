using System.Globalization;

namespace AntSim.Unity.Scripts.Streaming
{
    /// <summary>
    /// F5.1 — chip de estado por RUNWAY de la tarjeta de colonia (contrato HUD
    /// §2), modelo PURO. Regla dura del contrato: el chip no decide nada; el
    /// tramo de la escalera llega calculado por el Core en el canal A
    /// (<c>runwayLevel</c>, RunwayVerdict) y aquí solo se PINTA — glifo, rótulo y
    /// color. Sin UnityEngine: compilado en la suite headless.
    ///
    /// Glifos ASCII a propósito: la fuente por defecto de uGUI (LegacyRuntime) no
    /// tiene emoji y los pintaba como cajas vacías (defecto de F5.1), así que la
    /// severidad se dice con signos —cuantos más, más grave— y con el RÓTULO, que
    /// es lo que de verdad informa: «[!] OOFAGIA 22s» no admite duda.
    /// </summary>
    public static class RunwayChipModel
    {
        /// <summary>Sin datos (0): no se pinta chip. La tarjeta queda como antes.</summary>
        public static string Glyph(byte state) => state switch
        {
            1 => "[!!!]", // crítico: raciona adultas y pupas
            2 => "[!!]",  // canibalismo larval
            3 => "[!]",   // oofagia: reabsorbe huevos
            4 => "[+]",   // reserva: pone con ρ_res < 1
            5 => "[++]",  // desahogo: puesta a tope
            _ => "",
        };

        public static string Label(byte state) => state switch
        {
            1 => "CRÍTICO",
            2 => "CANÍBAL",
            3 => "OOFAGIA",
            4 => "RESERVA",
            5 => "DESAHOGO",
            _ => "",
        };

        /// <summary>Índice de COLOR para la vista (0 gris · 1 rojo · 2 naranja ·
        /// 3 ámbar · 4 verde claro · 5 verde). La paleta la fija la vista; el
        /// modelo solo ordena la severidad.</summary>
        public static byte ColorIndex(byte state) => state <= 5 ? state : (byte)0;

        /// <summary>
        /// Color del chip en hex, del más grave al más holgado. Vive en el modelo
        /// (y no en el MonoBehaviour) para poder verificarlo headless: el chip se
        /// pinta con rich text de uGUI — la tarjeta ya usa <c>&lt;b&gt;</c> — y así el
        /// rojo de «crítico» se ve de un vistazo sin depender de la fuente.
        /// </summary>
        public static string ColorHex(byte state) => state switch
        {
            1 => "#E03A3A", // crítico
            2 => "#E07A2A", // caníbal
            3 => "#D8B02A", // oofagia
            4 => "#7ED07E", // reserva
            5 => "#4CAF50", // desahogo
            _ => "#9E9E9E", // sin datos (gris): no se pinta chip, pero la paleta es total
        };

        /// <summary>Texto del chip envuelto en su color (rich text de uGUI). Cadena
        /// vacía cuando no hay chip: nunca se emite una etiqueta de color sola.</summary>
        public static string RichText(byte state, float runwaySeconds)
        {
            string s = Text(state, runwaySeconds);
            return s.Length == 0 ? "" : "<color=" + ColorHex(state) + ">" + s + "</color>";
        }

        /// <summary>Igual que <see cref="RichText"/> en la forma corta del multi-visor.</summary>
        public static string RichShort(byte state, float runwaySeconds)
        {
            string s = Short(state, runwaySeconds);
            return s.Length == 0 ? "" : "<color=" + ColorHex(state) + ">" + s + "</color>";
        }

        /// <summary>
        /// Texto del chip: «[!] OOFAGIA 22s». Cadena VACÍA cuando no hay medida
        /// (estado 0): el contrato pide chip ausente, no chip en gris, para no
        /// ocupar la línea del título con un hueco.
        /// </summary>
        public static string Text(byte state, float runwaySeconds)
        {
            string glyph = Glyph(state);
            if (glyph.Length == 0) return "";
            string label = Label(state);
            string secs = Seconds(runwaySeconds);
            return secs.Length == 0 ? glyph + " " + label : glyph + " " + label + " " + secs;
        }

        /// <summary>Forma corta para las vistas del multi-visor: «[!]22s».</summary>
        public static string Short(byte state, float runwaySeconds)
        {
            string glyph = Glyph(state);
            if (glyph.Length == 0) return "";
            string secs = Seconds(runwaySeconds);
            return secs.Length == 0 ? glyph : glyph + secs;
        }

        /// <summary>Segundos con una decimal y sufijo «s»; vacío si no hay medida
        /// (negativo = sin EMA, el centinela que emite el canal A).</summary>
        public static string Seconds(float runwaySeconds)
        {
            if (float.IsNaN(runwaySeconds) || runwaySeconds < 0f) return "";
            return runwaySeconds.ToString("0.#", CultureInfo.InvariantCulture) + "s";
        }
    }
}

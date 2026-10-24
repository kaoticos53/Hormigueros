using System;
using System.Collections.Generic;

namespace AntSim.Unity.Scripts.Streaming
{
    /// <summary>
    /// F8 — COMPOSICIÓN DE TODAS LAS CAPAS DE FEROMONA EN UNA SOLA TEXTURA.
    /// Modelo PURO (sin UnityEngine) para poder fijar la mezcla en la suite
    /// headless; el MonoBehaviour solo sube el resultado a la RenderTexture.
    ///
    /// POR QUÉ. El canal E ya trae varias capas por tick (food, home, alarm y
    /// huella de cada colonia) pero el tablero pintaba UNA cada vez y había que
    /// pulsar F para descubrir las demás: el espectador no podía ver de un
    /// vistazo que el camino de comida y el de casa comparten pasillo, ni dónde
    /// está el peligro. Aquí cada tipo se pinta con SU color y la celda donde dos
    /// tipos se solapan enseña la mezcla — aditivo en color, realzado en alfa —
    /// sin tocar el dato ni el shader: una textura RGBA y el quad de siempre.
    ///
    /// La opacidad de cada capa sale de <see cref="PheromoneRamp"/>, la misma
    /// curva de pantalla que usa el render de una sola capa: el rastro sigue sin
    /// la cola de «humo» y el núcleo sigue opaco.
    /// </summary>
    public static class PheromoneCompositeModel
    {
        /// <summary>Una capa decodificada que se va a pintar (una colonia, un tipo).</summary>
        public readonly struct Layer
        {
            public readonly PheromoneSelectorModel.Layer Kind;
            public readonly byte[] Cells;

            public Layer(PheromoneSelectorModel.Layer kind, byte[] cells)
            {
                Kind = kind;
                Cells = cells ?? Array.Empty<byte>();
            }
        }

        /// <summary>Color (0..1) del tipo <paramref name="kind"/> — la paleta del selector.</summary>
        public static PheromoneSelectorModel.Rgb ColorOf(PheromoneSelectorModel.Layer kind)
            => PheromoneSelectorModel.PaletteOf(kind);

        /// <summary>
        /// PESO DE CADA TIPO EN PANTALLA (0..1). No todos los rastros son iguales:
        /// el de comida es UN CAMINO —medido en la partida: 1,5 % del tablero—
        /// mientras que la huella CHC (31 %) y el de casa (22 %) los deposita
        /// CADA hormiga que anda, así que cubren el suelo pisado entero. Pintarlos
        /// a la misma opacidad enterraba el camino ámbar bajo un velo verde y
        /// violeta —el «humo» que se quería quitar— por mucho que el depósito sea
        /// de una celda. Con estos pesos el camino manda (alfa pleno), el peligro
        /// se lee casi entero y las dos capas de fondo quedan como SUSTRATO: se
        /// ven, pero por debajo y sin teñir el camino.
        /// </summary>
        public static float WeightOf(PheromoneSelectorModel.Layer kind) => kind switch
        {
            PheromoneSelectorModel.Layer.Food => 1.00f,
            PheromoneSelectorModel.Layer.Alarm => 0.85f,
            PheromoneSelectorModel.Layer.Home => 0.55f,
            PheromoneSelectorModel.Layer.Footprint => 0.25f,
            _ => 1.00f
        };

        /// <summary>
        /// Compone las capas en dos arrays planos (RGB y alfa) del tamaño w×h.
        /// El color de cada celda es la media ponderada por opacidad de las capas
        /// presentes, y el alfa es la suma de opacidades saturada a 1: una celda
        /// con un solo rastro sale exactamente del color de ese rastro; una celda
        /// donde se cruzan dos, de la mezcla.
        ///
        /// La opacidad de cada capa se multiplica por su <see cref="WeightOf"/> ANTES
        /// de mezclar: así el camino de comida, que es estrecho, gana el color de
        /// la celda frente al velo de casa/huella que cubre el suelo pisado, y las
        /// capas de fondo se leen como sustrato y no como niebla.
        ///
        /// No lee fuera de la rejilla: las capas de otro tamaño se ignoran (un
        /// paquete viejo no puede corromper el frame actual).
        /// </summary>
        public static void Compose(IReadOnlyList<Layer> layers, int width, int height,
            byte[] rgb, byte[] alpha)
        {
            if (layers == null) throw new ArgumentNullException(nameof(layers));
            if (width <= 0 || height <= 0) return;
            int cells = width * height;
            if (rgb == null || rgb.Length < cells * 3)
                throw new ArgumentException("rgb demasiado pequeño", nameof(rgb));
            if (alpha == null || alpha.Length < cells)
                throw new ArgumentException("alpha demasiado pequeño", nameof(alpha));

            Array.Clear(rgb, 0, cells * 3);
            Array.Clear(alpha, 0, cells);

            // El máximo de cada capa se calcula una vez: la curva de pantalla es
            // relativa a la capa y recalcularla por celda cambiaría el umbral.
            var maxes = new byte[layers.Count];
            for (int L = 0; L < layers.Count; L++)
            {
                var cellsOf = layers[L].Cells;
                if (cellsOf.Length < cells) { maxes[L] = 0; continue; }
                maxes[L] = PheromoneRamp.LayerMax(cellsOf);
            }

            for (int i = 0; i < cells; i++)
            {
                float sumA = 0f, sumR = 0f, sumG = 0f, sumB = 0f;
                for (int L = 0; L < layers.Count; L++)
                {
                    byte max = maxes[L];
                    if (max == 0) continue;
                    float a = PheromoneRamp.Alpha(layers[L].Cells[i], max) * WeightOf(layers[L].Kind);
                    if (a <= 0f) continue;
                    var c = ColorOf(layers[L].Kind);
                    sumA += a;
                    sumR += c.R * a;
                    sumG += c.G * a;
                    sumB += c.B * a;
                }
                if (sumA <= 0f) continue;

                float inv = 255f / sumA;
                rgb[i * 3] = (byte)Math.Clamp((int)MathF.Round(sumR * inv), 0, 255);
                rgb[i * 3 + 1] = (byte)Math.Clamp((int)MathF.Round(sumG * inv), 0, 255);
                rgb[i * 3 + 2] = (byte)Math.Clamp((int)MathF.Round(sumB * inv), 0, 255);
                alpha[i] = (byte)Math.Clamp((int)MathF.Round(Math.Min(1f, sumA) * 255f), 0, 255);
            }
        }
    }
}

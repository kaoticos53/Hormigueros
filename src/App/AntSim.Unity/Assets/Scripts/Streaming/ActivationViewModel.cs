using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace AntSim.Unity.Scripts.Streaming
{
    /// <summary>
    /// F5.0 — Modelo PURO del mini-grafo MLP del inspector (canal F). Decodifica
    /// el paquete base64 del stream ([total u16 LE][activación s8 × total],
    /// v = byte/128) y produce el texto del grafo. Compilado en la suite headless
    /// (como el resto de modelos puros de Unity) para verificarlo contra el Core
    /// sin arrancar el editor. Sin UnityEngine.
    /// </summary>
    public static class ActivationViewModel
    {
        /// <summary>Tamaños canónicos del cerebro del contrato v1 (19·8·6 = 33 nodos).</summary>
        public static readonly int[] DefaultSizes = { 19, 8, 6 };

        /// <summary>Nombres canónicos de las 6 salidas (contrato v1).</summary>
        public static readonly string[] OutputNames =
            { "Steer", "Speed", "DepositFood", "DepositHome", "DepositAlarm", "Interact" };

        /// <summary>
        /// Decodifica el paquete del canal F a floats. Devuelve false si el
        /// paquete está vacío (hormiga muerta/ausente — la vista no muestra nada),
        /// true en caso contrario. Lanza solo ante payloads corruptos de longitud
        /// inconsistente (defensivo: el Core no los emite).
        /// </summary>
        public static bool TryDecode(string? base64Payload, int[] sizes, out float[] activations)
        {
            activations = Array.Empty<float>();
            if (string.IsNullOrEmpty(base64Payload)) return false;

            byte[] bytes;
            try { bytes = Convert.FromBase64String(base64Payload); }
            catch (FormatException) { return false; }

            if (bytes.Length < 2) return false;
            int total = bytes[0] | (bytes[1] << 8);
            int expected = 0;
            for (int l = 0; l < sizes.Length; l++) expected += sizes[l];
            if (total != expected || bytes.Length != 2 + total) return false;

            activations = new float[total];
            for (int i = 0; i < total; i++)
            {
                int b = bytes[2 + i];
                if (b > 127) b -= 256; // s8 con signo
                activations[i] = b / 128f;
            }
            return true;
        }

        /// <summary>
        /// F5.2c rodaja 6 — render NEAT: decodifica el canal F con el total de
        /// nodos del grafo y lo explica con la topología que el propio stream
        /// emite en el campo "graph" (n/h/c + profundidad por oculto).
        /// Devuelve null si no hay paquete o la topología no cuadra.
        /// </summary>
        /// <summary>Constante local: primer id de nodo oculto (contrato v1).</summary>
        private const int FirstHiddenId = 25; // FirstOutputId(19) + DecisionCount(6)
        private const int FirstOutputId = 19;
        private const int InputCount = 19;

        /// <summary>
        /// F5.2c rodaja 6 — render NEAT sin dependencias de Core: decodifica el
        /// canal F con el total de nodos del grafo y lo explica con la topología
        /// que el propio stream emite en el campo "graph" (n/h/c + profundidad
        /// por oculto). Produce una tabla ASCII por profundidad.
        /// </summary>
        public static string? RenderNeat(string? base64Payload,
            GameStreamParser.GraphView? graph, string? title = null)
        {
            if (graph == null || graph.H == null) return null;
            if (!TryDecodeRaw(base64Payload, graph.N, out float[] acts)) return null;

            int hidden = graph.H.Length;
            int outputs = OutputNames.Length;
            if (graph.N != InputCount + hidden + outputs) return null;

            // Calcular la profundidad máxima (las salidas viven en MaxDepth+1).
            int maxDepth = 0;
            foreach (int d in graph.H)
                if (d > maxDepth) maxDepth = d;

            var sb = new StringBuilder();
            sb.Append("NEAT ").Append(hidden).Append("h/").Append(graph.C)
              .Append("c (").Append(graph.N).Append(" nodos)");
            if (title != null) sb.Append(" — ").Append(title);
            sb.AppendLine();

            // Agrupar nodos por capa (profundidad): entradas=0, ocultos=1+d, salidas=maxDepth+1.
            var layers = new List<(int slot, string name)>[maxDepth + 2];
            for (int d = 0; d < layers.Length; d++) layers[d] = new();

            // Entradas (ids 0..18).
            for (int i = 0; i < InputCount; i++)
                layers[0].Add((i, i < SensorNames.Length ? SensorNames[i] : $"ch{i}"));

            // Ocultos (ids 25.., ordenados por profundidad).
            for (int i = 0; i < hidden; i++)
            {
                int depth = graph.H[i];
                int slot = 1 + Math.Max(0, depth);
                if (slot >= layers.Length) slot = layers.Length - 1;
                layers[slot].Add((InputCount + i, $"h{i + FirstHiddenId}"));
            }

            // Salidas (ids 19..24).
            for (int i = 0; i < outputs; i++)
                layers[maxDepth + 1].Add((InputCount + hidden + i, OutputNames[i]));

            // Renderizar cada capa.
            for (int d = 0; d < layers.Length; d++)
            {
                if (layers[d].Count == 0) continue;
                sb.Append("== capa ").Append(d).Append(" ==").AppendLine();
                foreach (var (slot, name) in layers[d])
                {
                    float v = acts[slot];
                    sb.Append(' ').Append(name.PadRight(15))
                      .Append(FormatValue(v)).Append(' ').Append(Bar(v))
                      .AppendLine();
                }
            }
            return sb.ToString();
        }

        /// <summary>Decode SIN validar contra tamaños de capa (NEAT: el total
        /// lo manda el paquete). False si el paquete está vacío o corrupto.</summary>
        public static bool TryDecodeRaw(string? base64Payload, int expectedTotal, out float[] activations)
        {
            activations = Array.Empty<float>();
            if (string.IsNullOrEmpty(base64Payload)) return false;

            byte[] bytes;
            try { bytes = Convert.FromBase64String(base64Payload); }
            catch (FormatException) { return false; }
            if (bytes.Length < 2) return false;
            int total = bytes[0] | (bytes[1] << 8);
            if (total != expectedTotal || bytes.Length != 2 + total) return false;

            activations = new float[total];
            for (int i = 0; i < total; i++)
            {
                int b = bytes[2 + i];
                if (b > 127) b -= 256;
                activations[i] = b / 128f;
            }
            return true;
        }

        /// <summary>
        /// Decodifica y renderiza el grafo del canal F con los tamaños canónicos.
        /// Devuelve null si no hay paquete (hormiga sin datos).
        /// </summary>
        public static string? Render(string? base64Payload, string? title = null)
        {
            if (!TryDecode(base64Payload, DefaultSizes, out float[] acts)) return null;

            var sb = new StringBuilder();
            sb.Append("MLP 19·8·6 (33 nodos)");
            if (title != null) sb.Append(" — ").Append(title);
            sb.AppendLine();

            int off = 0;
            for (int l = 0; l < DefaultSizes.Length; l++)
            {
                int n = DefaultSizes[l];
                string tag = l == 0 ? "in" : (l == 1 ? "hid" : "out");
                sb.Append("== capa ").Append(l).Append(" ==").AppendLine();
                for (int i = 0; i < n; i++)
                {
                    float v = acts[off + i];
                    sb.Append(' ').Append(tag).Append('[')
                      .Append(i.ToString("00", CultureInfo.InvariantCulture)).Append("] ")
                      .Append(NodeName(l, i, n).PadRight(15))
                      .Append(FormatValue(v)).Append(' ').Append(Bar(v))
                      .AppendLine();
                }
                off += n;
            }
            return sb.ToString();
        }

        private static string NodeName(int layer, int index, int layerSize)
        {
            // Nombres de sensores y salidas canónicos del contrato v1 (los mismos
            // textos que MlpAsciiGraph en Core) — la vista no inventa semántica.
            if (layer == 0 && index < SensorNames.Length) return SensorNames[index];
            if (layerSize == 6 && index < OutputNames.Length) return OutputNames[index];
            return $"h{index}";
        }

        /// <summary>Nombres de los 19 canales de sensor (contrato v1, orden fijo).</summary>
        public static readonly string[] SensorNames =
        {
            "FoodTrailCenter", "HomeTrailCenter", "AlarmCenter",
            "FoodTrailDiff", "HomeTrailDiff", "AlarmDiff",
            "FoodDx", "FoodDy", "FoodSize",
            "HomeDx", "HomeDy",
            "ProxLeft", "ProxFront", "ProxRight",
            "HasLoad", "LoadFraction", "Energy", "AgeNormalized",
            "ColonyFoodRatio",
        };

        private static string FormatValue(float v)
            => (v >= 0 ? "+" : "") + v.ToString("0.00", CultureInfo.InvariantCulture);

        /// <summary>Barra ASCII con signo: v en [−1,1] → hasta 10 celdas.</summary>
        private static string Bar(float v)
        {
            float c = Math.Max(-1f, Math.Min(1f, v));
            int cells = (int)Math.Round(Math.Abs(c) * 10f);
            var sb = new StringBuilder("[");
            if (c < 0) sb.Append(' ', 10 - cells).Append('#', cells).Append('|');
            else sb.Append('|').Append('#', cells).Append(' ', 10 - cells);
            sb.Append(']');
            return sb.ToString();
        }
    }
}

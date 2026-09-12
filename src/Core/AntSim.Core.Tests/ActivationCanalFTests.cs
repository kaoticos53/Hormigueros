using System;
using System.Collections.Generic;
using System.Globalization;
using AntSim.Core.Brain;
using AntSim.Core.Scenario;
using AntSim.Unity.Scripts.Streaming;
using Xunit;

namespace AntSim.Core.Tests
{
    /// <summary>
    /// F5.0 — canal F de activaciones (opt-in) del cerebro inspeccionado:
    ///   1. La emisión es telemetría pura: el hash final con y sin canal es
    ///      idéntico, y el stream sigue siendo JSONL válido línea a línea.
    ///   2. El paquete decodifica a 33 nodos con rangos correctos y conserva
    ///      la decisión (re-evaluación determinista).
    ///   3. El renderizador ASCII produce texto por capas con nombres canónicos.
    /// </summary>
    public class ActivationCanalFTests
    {
        // ————— helpers —————

        private static string ExtractHash(string stream)
        {
            foreach (var line in stream.Split('\n'))
            {
                var t = line.TrimEnd('\r');
                if (t.StartsWith("{\"end\"", StringComparison.Ordinal))
                {
                    int i = t.IndexOf("\"hash\":\"", StringComparison.Ordinal) + 8;
                    return t.Substring(i, 64);
                }
            }
            throw new InvalidOperationException("stream sin línea end");
        }

        private static string RunGame(ulong seed, int ticks, int grid, int frameEvery,
            uint inspectId = 0, int activEvery = 0)
            => GameScenario.Run(seed, ticks, colonies: 2, grid, frameEvery,
                inspectId: inspectId, activEvery: activEvery);

        // ————— 1. invariancia del hash y JSONL válido —————

        [Fact]
        public void CanalF_HashInvariante()
        {
            string sin = RunGame(42, 400, 96, 1);
            string con = RunGame(42, 400, 96, 1, inspectId: 1, activEvery: 30);
            Assert.Equal(ExtractHash(sin), ExtractHash(con));
        }

        [Fact]
        public void CanalF_Espera0_IgualQueCanalE_HashInvariante()
        {
            // El fix del cierre de línea (F5.0) no cambia el stream sin canales.
            string baseStream = RunGame(42, 200, 96, 10);
            string conPhero = GameScenario.Run(42, 200, colonies: 2, 96, 10, pheroEvery: 10);
            Assert.Equal(ExtractHash(baseStream), ExtractHash(conPhero));
        }

        [Fact]
        public void CanalF_CadaLineaEsJsonValido()
        {
            string stream = RunGame(42, 120, 96, 1, inspectId: 1, activEvery: 1);
            foreach (var line in stream.Split('\n'))
            {
                var t = line.TrimEnd('\r');
                if (t.Length == 0) continue;
                // Mínima validación estructural: abre { y cierra } sin pegados.
                Assert.Equal('{', t[0]);
                Assert.Equal('}', t[t.Length - 1]);
            }
        }

        // ————— 2. decodificación del paquete —————

        private static (ulong tick, float[] acts, bool has) DecodeLast(string stream)
        {
            (ulong, float[], bool) last = default;
            foreach (var line in stream.Split('\n'))
            {
                var t = line.TrimEnd('\r');
                int ai = t.IndexOf("\"activ\":\"", StringComparison.Ordinal);
                if (ai < 0) continue;
                int start = ai + 9;
                int end = t.IndexOf('"', start);
                string b64 = t.Substring(start, end - start);
                int tickIdx = t.IndexOf("\"tick\":", StringComparison.Ordinal) + 7;
                int tickEnd = t.IndexOf(',', tickIdx);
                ulong tick = ulong.Parse(t.Substring(tickIdx, tickEnd - tickIdx), CultureInfo.InvariantCulture);

                bool has = ActivationViewModel.TryDecode(b64, ActivationViewModel.DefaultSizes, out var acts);
                last = (tick, acts, has);
            }
            return last;
        }

        [Fact]
        public void CanalF_PaqueteDecodifica_33Nodos()
        {
            string stream = RunGame(42, 150, 96, 1, inspectId: 1, activEvery: 50);
            var (tick, acts, has) = DecodeLast(stream);
            Assert.True(has);
            Assert.Equal(150ul, tick);
            Assert.Equal(33, acts.Length); // 19 + 8 + 6

            // Rangos: sensores y ocultas en [−1,1]; salidas tanh/sigmoid en [−1,1]
            // y la cuantización s8 /128 es exactamente representable.
            for (int i = 0; i < acts.Length; i++)
                Assert.InRange(acts[i], -1.0001f, 1.0001f);

            // Las entradas del contrato son finitas y HasLoad {0,1} se cuantiza
            // a sí mismo con paso 1/128 (banda gruesa pero determinista).
            Assert.All(acts, a => Assert.False(float.IsNaN(a)));
        }

        [Fact]
        public void CanalF_Determinista_MismoStreamByteABit()
        {
            string a = RunGame(42, 150, 96, 1, inspectId: 1, activEvery: 50);
            string b = RunGame(42, 150, 96, 1, inspectId: 1, activEvery: 50);
            Assert.Equal(a, b);
        }

        [Fact]
        public void CanalF_HormigaAusente_PaqueteVacio()
        {
            string stream = RunGame(42, 30, 96, 1, inspectId: 999999, activEvery: 10);
            Assert.Contains("\"activ\":\"\"", stream, StringComparison.Ordinal);
            var (_, acts, has) = DecodeLast(stream);
            Assert.False(has);
            Assert.Empty(acts);
        }

        [Fact]
        public void CanalF_SinInspect_ConActivEvery_Lanza()
        {
            Assert.ThrowsAny<ArgumentException>(
                () => GameScenario.Run(42, 10, colonies: 2, 96, 1, activEvery: 10));
        }

        // ————— 3. renderizador ASCII —————

        [Fact]
        public void AsciiGraph_Renderiza_CapasYNombres()
        {
            string stream = RunGame(42, 150, 96, 1, inspectId: 1, activEvery: 50);
            var (_, acts, has) = DecodeLast(stream);
            Assert.True(has);

            string text = ActivationViewModel.Render(
                Convert.ToBase64String(Pack(acts)),
                title: "tick 150 · hormiga 1");

            Assert.Contains("MLP 19·8·6", text);
            Assert.Contains("FoodTrailCenter", text);   // nombre canónico de sensor
            Assert.Contains("ColonyFoodRatio", text);   // último canal de entrada
            Assert.Contains("DepositHome", text);       // salida del contrato
            Assert.Contains("Interact", text);
            Assert.Equal(19 + 8 + 6 + 3 + 1, CountLines(text)); // título + nodos + 3 cabeceras
            Assert.Contains("tick 150 · hormiga 1", text);
        }

        [Fact]
        public void AsciiGraph_LongitudIncorrecta_Lanza()
        {
            Assert.ThrowsAny<ArgumentException>(() =>
                MlpAsciiGraph.Render(new[] { 19, 8, 6 }, new float[10]));
        }

        [Fact]
        public void AsciiGraph_BarraConSigno()
        {
            float[] acts = new float[33];
            acts[0] = 0.5f;    // entrada positiva
            acts[1] = -0.75f;  // entrada negativa
            string text = MlpAsciiGraph.Render(new[] { 19, 8, 6 }, acts);
            Assert.Contains("+0.50", text);
            Assert.Contains("-0.75", text);
            Assert.Contains("#####", text); // celdas activas de la barra
        }

        // ————— utils —————

        private static byte[] Pack(float[] acts)
        {
            var bytes = new byte[2 + acts.Length];
            bytes[0] = (byte)acts.Length;
            bytes[1] = (byte)(acts.Length >> 8);
            for (int i = 0; i < acts.Length; i++)
            {
                int q = (int)MathF.Round(acts[i] * 128f);
                bytes[2 + i] = (byte)Math.Clamp(q, -128, 127);
            }
            return bytes;
        }

        private static int CountLines(string s)
        {
            int n = 0;
            foreach (var line in s.Split('\n'))
                if (line.TrimEnd('\r').Length > 0) n++;
            return n;
        }
    }
}

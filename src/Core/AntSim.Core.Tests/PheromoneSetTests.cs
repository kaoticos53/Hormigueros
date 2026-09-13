using System;
using System.Collections.Generic;
using System.Text.Json;
using AntSim.Core.Pheromone;
using AntSim.Core.Scenario;
using Xunit;

namespace AntSim.Core.Tests
{
    /// <summary>
    /// F5.1 — canal E múltiple: varias capas (colonia × tipo) en el mismo tick.
    ///
    /// POR QUÉ. Las capas de feromona son POR COLONIA: una hormiga solo lee las
    /// suyas. El canal E clásico emitía una sola (home de la colonia 0), así que
    /// con dos colonias compitiendo el espectador veía media partida. El modo
    /// nuevo emite lo que se le pida, pero no sustituye al clásico: los fixtures
    /// de stream y los pines de CI siguen contando con "phero" y no pueden
    /// cambiar de forma.
    ///
    /// Lo que se fija aquí: el hash NO cambia (telemetría pura), el JSONL sigue
    /// siendo válido, el array trae exactamente las capas pedidas y en orden, y
    /// cada colonia llega con SUS datos (no es la misma capa copiada).
    /// </summary>
    public class PheromoneSetTests
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

        private static List<GameScenario.PheroRequest> DosColonias() => new()
        {
            new GameScenario.PheroRequest(0, PheromoneKind.Home),
            new GameScenario.PheroRequest(1, PheromoneKind.Home),
            new GameScenario.PheroRequest(1, PheromoneKind.Alarm),
        };

        /// <summary>Decodifica el paquete RLE canónico: [w,h u16 LE] + filas no vacías
        /// [y u16 LE + (valor, run)] hasta llenar w celdas por fila. Las filas
        /// ausentes son cero — así se puede distinguir «vacío» de «no emitido».</summary>
        private static byte[,] Decode(string base64)
        {
            byte[] raw = Convert.FromBase64String(base64);
            int w = raw[0] | (raw[1] << 8);
            int h = raw[2] | (raw[3] << 8);
            var grid = new byte[w, h];
            int p = 4;
            while (p < raw.Length)
            {
                int y = raw[p] | (raw[p + 1] << 8);
                p += 2;
                int x = 0;
                while (x < w)
                {
                    byte v = raw[p];
                    int run = raw[p + 1];
                    p += 2;
                    for (int k = 0; k < run; k++) grid[x + k, y] = v;
                    x += run;
                }
            }
            return grid;
        }

        private static JsonElement PheroSetOf(string stream, ulong tick)
        {
            foreach (var line in stream.Split('\n'))
            {
                var t = line.TrimEnd('\r');
                if (t.Length == 0) continue;
                using var doc = JsonDocument.Parse(t);
                var root = doc.RootElement;
                if (root.TryGetProperty("tick", out var tk) && tk.GetUInt64() == tick)
                    return root.GetProperty("pheroSet").Clone();
            }
            throw new InvalidOperationException($"no hay tick {tick} en el stream");
        }

        // ————— 1. telemetría pura —————

        [Fact]
        public void CanalEMultiple_HashInvariante()
        {
            string conCapas = GameScenario.Run(42, ticks: 900, colonies: 2, grid: 64,
                frameEvery: 30, pheroEvery: 300, pheroLayers: DosColonias());
            string clasico = GameScenario.Run(42, ticks: 900, colonies: 2, grid: 64,
                frameEvery: 30, pheroEvery: 300);
            string sinCanal = GameScenario.Run(42, ticks: 900, colonies: 2, grid: 64,
                frameEvery: 30);

            string h = ExtractHash(conCapas);
            Assert.Equal(h, ExtractHash(clasico));
            Assert.Equal(h, ExtractHash(sinCanal));
        }

        [Fact]
        public void CanalEMultiple_MismaSemilla_StreamByteAByte()
        {
            string a = GameScenario.Run(7, ticks: 300, colonies: 2, grid: 32,
                frameEvery: 30, pheroEvery: 150, pheroLayers: DosColonias());
            string b = GameScenario.Run(7, ticks: 300, colonies: 2, grid: 32,
                frameEvery: 30, pheroEvery: 150, pheroLayers: DosColonias());
            Assert.Equal(a, b);
        }

        [Fact]
        public void CanalEMultiple_JSONLValidoLineaALinea()
        {
            string stream = GameScenario.Run(42, ticks: 400, colonies: 2, grid: 32,
                frameEvery: 1, pheroEvery: 200, pheroLayers: DosColonias());

            int ticks = 0;
            foreach (var line in stream.Split('\n'))
            {
                var t = line.TrimEnd('\r');
                if (t.Length == 0) continue;
                using var doc = JsonDocument.Parse(t); // lanza si una línea no es JSON
                // La cabecera y la línea end también son JSON (y el end trae
                // "tick"): solo cuentan las líneas de tick.
                if (t.StartsWith("{\"tick\"", StringComparison.Ordinal)) ticks++;
            }
            Assert.Equal(400, ticks);
        }

        // ————— 2. contrato del array —————

        [Fact]
        public void CanalEMultiple_DeclaraElModoYNoEmiteElCanalClasico()
        {
            string stream = GameScenario.Run(42, ticks: 200, colonies: 2, grid: 32,
                frameEvery: 30, pheroEvery: 100, pheroLayers: DosColonias());

            string header = stream.Split('\n')[0];
            Assert.Contains("\"pheroEvery\":100", header);
            Assert.Contains("\"pheroSet\":true", header);

            var set = PheroSetOf(stream, 100);
            Assert.Equal(3, set.GetArrayLength());
            // El modo múltiple SUSTITUYE al clásico: emitir los dos sería el mismo
            // dato dos veces por tick y duplicaría el tamaño del stream.
            Assert.DoesNotContain("\"phero\":\"", stream);
        }

        [Fact]
        public void CanalEMultiple_ArrayTraeLasCapasPedidasYEnOrden()
        {
            var set = PheroSetOf(GameScenario.Run(42, ticks: 200, colonies: 2, grid: 32,
                frameEvery: 30, pheroEvery: 100, pheroLayers: DosColonias()), 100);

            var esperado = new[] { (0, 1), (1, 1), (1, 2) }; // (colonia, kind)
            for (int i = 0; i < esperado.Length; i++)
            {
                Assert.Equal(esperado[i].Item1, set[i].GetProperty("c").GetInt32());
                Assert.Equal(esperado[i].Item2, set[i].GetProperty("k").GetInt32());
            }
        }

        [Fact]
        public void CanalEMultiple_CadaPaqueteDecodificaConElTamanoDelMundo()
        {
            var set = PheroSetOf(GameScenario.Run(42, ticks: 200, colonies: 2, grid: 32,
                frameEvery: 30, pheroEvery: 100, pheroLayers: DosColonias()), 100);

            for (int i = 0; i < set.GetArrayLength(); i++)
            {
                byte[] raw = Convert.FromBase64String(set[i].GetProperty("d").GetString()!);
                int w = raw[0] | (raw[1] << 8);
                int h = raw[2] | (raw[3] << 8);
                Assert.Equal(32, w);
                Assert.Equal(32, h);
            }
        }

        [Fact]
        public void CanalEMultiple_LasColoniasTraenSusPropiosDatos()
        {
            // Con dos colonias fundadas en puntos distintos, sus rastros home no
            // pueden ser iguales: si lo fueran, estaríamos emitiendo la misma capa
            // dos veces (el fallo silencioso que este modo viene a arreglar).
            var set = PheroSetOf(GameScenario.Run(42, ticks: 900, colonies: 2, grid: 32,
                frameEvery: 30, pheroEvery: 900, pheroLayers: DosColonias()), 900);

            byte[,] c0 = Decode(set[0].GetProperty("d").GetString()!);
            byte[,] c1 = Decode(set[1].GetProperty("d").GetString()!);
            bool distintas = false, c0TieneDatos = false, c1TieneDatos = false;
            for (int x = 0; x < 32 && !(distintas && c0TieneDatos && c1TieneDatos); x++)
            {
                for (int y = 0; y < 32; y++)
                {
                    if (c0[x, y] != 0) c0TieneDatos = true;
                    if (c1[x, y] != 0) c1TieneDatos = true;
                    if (c0[x, y] != c1[x, y]) distintas = true;
                }
            }
            Assert.True(c0TieneDatos, "la colonia 0 debería haber depositado home");
            Assert.True(c1TieneDatos, "la colonia 1 debería haber depositado home");
            Assert.True(distintas, "las capas de las dos colonias son idénticas");
        }

        [Fact]
        public void CanalEMultiple_EmiteEnLosMultiplosDeLaCadencia()
        {
            string stream = GameScenario.Run(42, ticks: 305, colonies: 2, grid: 32,
                frameEvery: 1, pheroEvery: 100, pheroLayers: DosColonias());

            var conSet = new List<ulong>();
            var sinSet = new List<ulong>();
            foreach (var line in stream.Split('\n'))
            {
                var t = line.TrimEnd('\r');
                if (t.Length == 0 || t.StartsWith("{\"header\"", StringComparison.Ordinal)) continue;
                if (!t.StartsWith("{\"tick\"", StringComparison.Ordinal)) continue;
                int c = t.IndexOf("\"tick\":", StringComparison.Ordinal) + 7;
                int e = c;
                while (e < t.Length && char.IsDigit(t[e])) e++;
                ulong tick = ulong.Parse(t.Substring(c, e - c));
                (t.Contains("\"pheroSet\"") ? conSet : sinSet).Add(tick);
            }

            Assert.Equal(new ulong[] { 100, 200, 300 }, conSet);
            Assert.Equal(302, sinSet.Count); // 305 ticks − los 3 con paquete
        }

        // ————— 3. validación: los fallos silenciosos no pasan —————

        [Fact]
        public void CanalEMultiple_SinCadencia_EsError()
        {
            var ex = Assert.Throws<ArgumentException>(() =>
                GameScenario.Run(1, ticks: 10, pheroLayers: DosColonias()));
            Assert.Contains("pheroEvery", ex.Message);
        }

        [Fact]
        public void CanalEMultiple_ColoniaFueraDeRango_EsError()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                GameScenario.Run(1, ticks: 10, colonies: 2, pheroEvery: 5,
                    pheroLayers: new List<GameScenario.PheroRequest>
                    {
                        new(2, PheromoneKind.Home)
                    }));
        }

        [Fact]
        public void CanalEMultiple_ListaVacia_EsError()
        {
            Assert.Throws<ArgumentException>(() =>
                GameScenario.Run(1, ticks: 10, pheroEvery: 5,
                    pheroLayers: new List<GameScenario.PheroRequest>()));
        }

        [Fact]
        public void CanalEMultiple_Territory_EsError()
        {
            // Territory existe en el enum pero todavía no se deposita: emitirla
            // daría un paquete siempre vacío y la UI lo leería como «aquí no pasa
            // nada», que es peor que un error.
            var ex = Assert.Throws<ArgumentException>(() =>
                GameScenario.Run(1, ticks: 10, pheroEvery: 5,
                    pheroLayers: new List<GameScenario.PheroRequest>
                    {
                        new(0, PheromoneKind.Territory)
                    }));
            Assert.Contains("Territory", ex.Message);
        }
    }
}

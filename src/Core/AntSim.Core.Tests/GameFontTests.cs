using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using AntSim.Unity.Scripts.UI;
using Xunit;

namespace AntSim.Core.Tests
{
    /// <summary>
    /// La fuente PROPIA del juego: que cubra el texto que el HUD escribe de
    /// verdad y que los bytes que salen sean un TrueType válido.
    ///
    /// El test que más vale de todos es <see cref="Fuente_CubreLasCadenasDelHud"/>:
    /// lee las propias fuentes del HUD, saca de ellas los literales de texto y
    /// exige que la fuente tenga glifo para CADA carácter. Un carácter sin glifo
    /// no avisa en ningún sitio (uGUI simplemente no pinta nada), así que la
    /// única forma de que no se cuele en silencio es comprobarlo contra el
    /// código. Si mañana alguien escribe «→» en un toast, esto falla aquí y no
    /// en la cara del jugador.
    /// </summary>
    public class GameFontTests
    {
        /// <summary>Los signos que el HUD usa y que NO son ASCII (medidos sobre el
        /// código): signos de castellano, símbolos del contrato y flechas.</summary>
        private const string ExtraSigns =
            "¡§ª«²·»ÁÍÓ×áéíñóúα–—†…→↻−≈⏸─■▶○●◐✓✗⬇";

        [Fact]
        public void Fuente_CubreElAlfabetoDelHud()
        {
            for (int cp = 0x20; cp <= 0x7E; cp++)
                Assert.True(GameFontModel.Covers(cp), $"falta el ASCII U+{cp:X4} «{(char)cp}»");
            foreach (char c in ExtraSigns)
                Assert.True(GameFontModel.Covers(c), $"falta el signo «{c}» U+{(int)c:X4}");

            // Los iconos del juego (uso privado) y las vocales acentuadas que
            // pide el castellano en mayúscula y con diéresis.
            foreach (int cp in new[] { GameFontModel.AntGlyph, GameFontModel.LoadGlyph, GameFontModel.EggGlyph })
                Assert.True(GameFontModel.Covers(cp), $"falta el icono U+{cp:X4}");
            foreach (char c in "¿ÉÚÑÜü")
                Assert.True(GameFontModel.Covers(c), $"falta la letra «{c}»");
        }

        [Fact]
        public void Fuente_CubreLasCadenasDelHud()
        {
            string root = RepoRoot();
            var missing = new SortedDictionary<int, string>();
            int scanned = 0;
            foreach (string file in Sources(root))
            {
                scanned++;
                foreach (char c in Literals(File.ReadAllText(file)))
                {
                    if (c < 0x20 || c == 0x7F) continue;   // escapes de control: no son tinta
                    if (!GameFontModel.Covers(c))
                        missing[(int)c] = $"U+{(int)c:X4} «{c}» en {Path.GetFileName(file)}";
                }
            }
            Assert.True(scanned > 20, $"el escaneo no encontró fuentes del HUD (¿raíz mal? {root})");
            Assert.True(missing.Count == 0,
                "el HUD escribe caracteres sin glifo en la fuente propia:\n  " +
                string.Join("\n  ", missing.Values));
        }

        [Fact]
        public void Glifos_TienenPoligonosVálidosYEnLaCaja()
        {
            foreach (int cp in GameFontModel.Codepoints)
            {
                var stamps = GameFontModel.Stamps(cp);
                if (cp == 0x20) { Assert.Empty(stamps); continue; }   // el espacio no pinta
                Assert.True(stamps.Count > 0, $"glifo U+{cp:X4} sin polígonos");
                foreach (var s in stamps)
                {
                    Assert.True(s.P.Length >= 3, $"glifo U+{cp:X4}: polígono de {s.P.Length} puntos");
                    Assert.True(Stamp.SignedArea(s.P) > 0f,
                        $"glifo U+{cp:X4}: polígono en sentido horario (el relleno non-zero lo restaría)");
                }
                // La caja de TINTA: por debajo del descendente o por encima de la
                // banda de acentos no hay nada que pintar (el icono «─» sí sale a
                // sangre a los lados: su avance se solapa a propósito).
                foreach (var s in stamps)
                {
                    Assert.True(s.MinY >= -260f && s.MaxY <= 1010f,
                        $"glifo U+{cp:X4}: fuera de la caja vertical ({s.MinY}…{s.MaxY})");
                }
            }
        }

        [Fact]
        public void Glifos_MonorrielSalvoLosIconos()
        {
            foreach (int cp in GameFontModel.Codepoints)
            {
                int expected = GameFontModel.IsIcon(cp) ? GameFontModel.IconAdvance : GameFontModel.Advance;
                Assert.Equal(expected, GameFontModel.AdvanceOf(cp));
            }
            // Monorriel de verdad: el truco del ancho de la tarjeta de colonia
            // (7 px por carácter a 14 px) supone que TODA letra avanza 0,5 em.
            Assert.Equal(GameFontModel.UnitsPerEm / 2, GameFontModel.Advance);
        }

        /// <summary>
        /// LOS AVANCES DEL FICHERO SON LOS DEL MODELO, leídos de la tabla `hmtx`
        /// —de la tabla, no de <see cref="GameFontModel"/>—.
        ///
        /// POR QUÉ HACE FALTA. <see cref="Glifos_MonorrielSalvoLosIconos"/> comprueba
        /// el MODELO (`AdvanceOf`): 500 para toda letra y 720 para los tres iconos.
        /// Lo que el HUD mide al dibujar no sale de ahí, sale de la tabla `hmtx`
        /// del `.ttf`, y ese avance lo escribe <see cref="TtfWriter"/> con su
        /// propio `U16` por glifo. Si el escritor y el modelo se separan —un
        /// `(ushort)` que trunca, un índice desplazado, un `hmtx` comprimido por
        /// cola que nadie declaró— el modelo sigue diciendo 500/720 y el juego
        /// pinta otra cosa: desajuste que no se ve hasta que alguien mira un
        /// texto mal alineado. Esto se comprueba glifo a glifo y por el MISMO
        /// camino que usa el juego para llegar al glifo (la `cmap`).
        ///
        /// POR QUÉ SOBRE LOS BYTES DEL GENERADOR. Es la entrada que el HUD va a
        /// usar y está siempre disponible; el `.ttf` de disco está atado a estos
        /// mismos bytes por
        /// <c>GeneratedAssetContractTests.FuenteVersionada_EsLaQueProduceElGenerador</c>,
        /// así que comprobando el generador queda comprobado el artefacto, sin
        /// depender de que la fuente esté o no versionada.
        /// </summary>
        [Fact]
        public void Fuente_LosAvancesDeHmtxSonLosDelModelo()
        {
            var f = new Sfnt(TtfWriter.BuildFromModel("AntSim"));

            // Una entrada de 4 B por glifo (U16 avance + I16 lsb) y sin
            // compresión de cola: el lector de abajo da por supuesto justo eso,
            // así que lo primero es que el fichero lo cumpla.
            Assert.Equal(f.NumGlyphs, f.NumberOfHMetrics);
            Assert.Equal(f.NumGlyphs * 4, f.Tables.Find(t => t.Tag == "hmtx")!.Length);
            Assert.Equal(GameFontModel.Codepoints.Count + 1, f.NumGlyphs);

            // .notdef: vacío, pero avanza como una letra (es lo que le da el escritor).
            Assert.Equal(GameFontModel.Advance, f.Advance(0));

            var vistos = new HashSet<int>();
            foreach (int cp in GameFontModel.Codepoints)
            {
                int gid = f.GlyphOf(cp);
                Assert.True(gid > 0, $"la cmap no resuelve U+{cp:X4} «{(char)cp}»");
                int esperado = GameFontModel.AdvanceOf(cp);
                Assert.Equal(esperado, f.Advance(gid));
                vistos.Add(esperado);
            }

            // Los iconos son el caso que más se puede torcer (avance distinto del
            // de las letras), así que se nombran: 720 en los tres PUA, 500 en las
            // letras, y NADA más (si todo midiera igual, lo de arriba pasaría por
            // casualidad y este test no probaría nada).
            foreach (int icon in new[] { GameFontModel.AntGlyph, GameFontModel.LoadGlyph, GameFontModel.EggGlyph })
                Assert.Equal(GameFontModel.IconAdvance, f.Advance(f.GlyphOf(icon)));
            Assert.Equal(720, f.Advance(f.GlyphOf(GameFontModel.AntGlyph)));
            Assert.Equal(500, f.Advance(f.GlyphOf('A')));
            Assert.Equal(2, vistos.Count);
            Assert.Contains(GameFontModel.Advance, vistos);
            Assert.Contains(GameFontModel.IconAdvance, vistos);
        }

        /// <summary>
        /// El test de arriba solo vale si el lector lee el FICHERO: si en vez de
        /// leer `hmtx` devolviera <see cref="GameFontModel.AdvanceOf"/> estaría
        /// comparando el modelo consigo mismo y pasaría siempre. Aquí se genera un
        /// `.ttf` con un avance cambiado y se exige que el lector lo VEA — es la
        /// comprobación negativa del lector, en la forma de mutación del dato.
        /// </summary>
        [Fact]
        public void LectorDeHmtx_LeeElFicheroYNoElModelo()
        {
            const int torcido = GameFontModel.Advance + 123;   // «A» con otro avance
            var glyphs = new List<TtfWriter.Glyph>
            {
                new TtfWriter.Glyph(-1, GameFontModel.Advance, Array.Empty<Stamp>()),
            };
            foreach (int cp in GameFontModel.Codepoints)
                glyphs.Add(new TtfWriter.Glyph(cp, cp == 'A' ? torcido : GameFontModel.AdvanceOf(cp),
                    GameFontModel.Stamps(cp)));

            var f = new Sfnt(TtfWriter.Build("AntSim", glyphs, GameFontModel.UnitsPerEm,
                GameFontModel.Ascender, GameFontModel.Descender, GameFontModel.LineGap));

            Assert.Equal(torcido, f.Advance(f.GlyphOf('A')));
            Assert.Equal(GameFontModel.Advance, GameFontModel.AdvanceOf('A'));   // el modelo no se entera
        }

        [Fact]
        public void Fuente_ElFicheroEsUnTrueTypeVálido()
        {
            byte[] ttf = TtfWriter.BuildFromModel("AntSim");
            var f = new Sfnt(ttf);

            Assert.Equal(0x00010000u, f.SfntVersion);
            Assert.Equal(10, f.Tables.Count);
            Assert.Equal(0x5F0F3CF5u, f.Head("magic"));
            Assert.Equal((ushort)GameFontModel.UnitsPerEm, (ushort)f.Head("unitsPerEm"));
            Assert.Equal(GameFontModel.Codepoints.Count + 1, f.NumGlyphs);

            // El ajuste de checksum está bien si y solo si la suma del fichero
            // ENTERO da la constante del formato: es la comprobación de todos los
            // checksums de tabla de una vez.
            Assert.Equal(0xB1B0AFBau, TtfWriter.Checksum(ttf));
            foreach (var table in f.Tables)
            {
                var data = f.Data(table);
                // El checksum de `head` se define con checkSumAdjustment a 0 (el
                // formato lo calcula así justamente para que no dependa de sí mismo).
                if (table.Tag == "head") data[8] = data[9] = data[10] = data[11] = 0;
                Assert.Equal(table.Checksum, TtfWriter.Checksum(data));
            }

            // loca monótona y coherente con el tamaño de glyf.
            Assert.Equal(0u, f.Loca(0));
            Assert.Equal(0u, f.Loca(1));       // .notdef vacío
            var glyf = f.Data(f.Tables.Find(t => t.Tag == "glyf")!);
            for (int i = 1; i <= f.NumGlyphs; i++)
                Assert.True(f.Loca(i) >= f.Loca(i - 1), $"loca no monótona en {i}");
            Assert.Equal((uint)glyf.Length, f.Loca(f.NumGlyphs));

            // La cmap tiene que resolver el texto del HUD: se prueban las letras,
            // los dígitos, el punto medio y los tres iconos.
            foreach (char c in "AaZz09·─○●α")
                Assert.True(f.GlyphOf(c) > 0, $"la cmap no resuelve «{c}»");
            foreach (int cp in new[] { GameFontModel.AntGlyph, GameFontModel.LoadGlyph, GameFontModel.EggGlyph })
                Assert.True(f.GlyphOf(cp) > 0, $"la cmap no resuelve el icono U+{cp:X4}");
        }

        [Fact]
        public void Fuente_EsDeterminista()
        {
            // Regenerar no puede ensuciar el repo: mismos glifos ⇒ mismos bytes.
            byte[] a = TtfWriter.BuildFromModel("AntSim");
            byte[] b = TtfWriter.BuildFromModel("AntSim");
            Assert.Equal(a.Length, b.Length);
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) Assert.Fail($"el byte {i} cambia entre dos generaciones");
        }

        // ── herramientas del test ──────────────────────────────────────────

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AntSim.slnx")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return dir!.FullName;
        }

        private static IEnumerable<string> Sources(string root)
        {
            string scripts = Path.Combine(root, "src", "App", "AntSim.Unity", "Assets", "Scripts");
            Assert.True(Directory.Exists(scripts), "no existe " + scripts);
            foreach (string p in Directory.EnumerateFiles(scripts, "*.cs", SearchOption.AllDirectories))
                yield return p;
        }

        /// <summary>Los literales de texto de un fuente C#, con los comentarios
        /// fuera y los escapes <c>\\uXXXX</c> ya convertidos a su carácter.</summary>
        internal static string Literals(string source)
        {
            var sb = new StringBuilder();
            int n = source.Length, i = 0;
            while (i < n)
            {
                if (i + 1 < n && source[i] == '/' && source[i + 1] == '/')
                {
                    int j = source.IndexOf('\n', i);
                    i = j < 0 ? n : j;
                }
                else if (i + 1 < n && source[i] == '/' && source[i + 1] == '*')
                {
                    int j = source.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    i = j < 0 ? n : j + 2;
                }
                else if (source[i] == '"')
                {
                    i++;
                    while (i < n && source[i] != '"')
                    {
                        if (source[i] == '\\' && i + 1 < n)
                        {
                            char esc = source[i + 1];
                            if (esc == 'u' && i + 5 < n)
                            {
                                sb.Append((char)Convert.ToInt32(source.Substring(i + 2, 4), 16));
                                i += 6;
                                continue;
                            }
                            sb.Append(esc switch
                            {
                                'n' => '\n', 't' => '\t', 'r' => '\r', '0' => '\0',
                                '\\' => '\\', '"' => '"', _ => esc,
                            });
                            i += 2;
                            continue;
                        }
                        sb.Append(source[i]);
                        i++;
                    }
                    i++;
                }
                else
                {
                    i++;
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Lector mínimo de sfnt para el test: cabecera, registros de tabla y la
        /// cmap formato 4. Vive aquí (y no en el modelo) porque solo sirve para
        /// verificar, igual que el verificador de assets de Unity es herramienta
        /// del repo y no código del juego.
        /// </summary>
        private sealed class Sfnt
        {
            public sealed class Table
            {
                public string Tag = "";
                public uint Checksum;
                public int Offset;
                public int Length;
            }

            private readonly byte[] _d;
            public uint SfntVersion { get; }
            public List<Table> Tables { get; } = new();

            public Sfnt(byte[] data)
            {
                _d = data;
                SfntVersion = U32(0);
                int n = U16(4);
                for (int i = 0; i < n; i++)
                {
                    int at = 12 + i * 16;
                    Tables.Add(new Table
                    {
                        Tag = Encoding.ASCII.GetString(data, at, 4),
                        Checksum = U32(at + 4),
                        Offset = (int)U32(at + 8),
                        Length = (int)U32(at + 12),
                    });
                }
            }

            public byte[] Data(Table t)
            {
                var r = new byte[t.Length];
                Array.Copy(_d, t.Offset, r, 0, t.Length);
                return r;
            }

            public uint Head(string field)
            {
                var head = Tables.Find(t => t.Tag == "head")!;
                return field switch
                {
                    "magic" => U32(head.Offset + 12),
                    "unitsPerEm" => (uint)U16(head.Offset + 18),
                    _ => throw new ArgumentException(field),
                };
            }

            public int NumGlyphs
            {
                get
                {
                    var maxp = Tables.Find(t => t.Tag == "maxp")!;
                    return U16(maxp.Offset + 4);
                }
            }

            public uint Loca(int i)
            {
                var loca = Tables.Find(t => t.Tag == "loca")!;
                return U32(loca.Offset + i * 4);
            }

            /// <summary>`numberOfHMetrics` de `hhea` (byte 34). Con este valor
            /// igual a numGlyphs, `hmtx` trae una entrada de 4 B por glifo.</summary>
            public int NumberOfHMetrics
            {
                get
                {
                    var hhea = Tables.Find(t => t.Tag == "hhea")!;
                    return U16(hhea.Offset + 34);
                }
            }

            /// <summary>Avance del glifo en unidades de la em, LEÍDO de `hmtx`.
            /// El formato deja el último avance repetido en la cola cuando
            /// numberOfHMetrics &lt; numGlyphs; aquí no pasa, pero se respeta por
            /// si el escritor cambiara.</summary>
            public int Advance(int glyphId)
            {
                var hmtx = Tables.Find(t => t.Tag == "hmtx")!;
                int i = Math.Min(glyphId, NumberOfHMetrics - 1);
                return U16(hmtx.Offset + i * 4);
            }

            /// <summary>Id de glifo de un código, por la cmap formato 4.</summary>
            public int GlyphOf(int codepoint)
            {
                var cmap = Tables.Find(t => t.Tag == "cmap")!;
                int sub = cmap.Offset + (int)U32(cmap.Offset + 8);
                Assert.Equal(4, U16(sub));   // formato 4: el único que emitimos
                int segX2 = U16(sub + 6);
                int segCount = segX2 / 2;
                int endBase = sub + 14;
                int startBase = endBase + segX2 + 2;
                int deltaBase = startBase + segX2;
                int rangeBase = deltaBase + segX2;
                for (int s = 0; s < segCount; s++)
                {
                    int end = U16(endBase + s * 2);
                    int start = U16(startBase + s * 2);
                    if (codepoint < start || codepoint > end) continue;
                    int range = U16(rangeBase + s * 2);
                    int delta = (short)U16(deltaBase + s * 2);
                    if (range == 0) return (codepoint + delta) & 0xFFFF;
                    int at = rangeBase + s * 2 + range + (codepoint - start) * 2;
                    int g = U16(at);
                    return g == 0 ? 0 : (g + delta) & 0xFFFF;
                }
                return 0;
            }

            private int U16(int at) => (_d[at] << 8) | _d[at + 1];
            private uint U32(int at) => ((uint)_d[at] << 24) | ((uint)_d[at + 1] << 16)
                                      | ((uint)_d[at + 2] << 8) | (uint)_d[at + 3];
        }
    }
}

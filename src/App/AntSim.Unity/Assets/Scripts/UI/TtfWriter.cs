using System;
using System.Collections.Generic;
using System.Text;

namespace AntSim.Unity.Scripts.UI
{
    /// <summary>
    /// ESCRITOR TrueType (sfnt) PURO — sin UnityEngine. Convierte los glifos del
    /// juego (<see cref="GameFontModel"/>) en un fichero <c>.ttf</c> de verdad,
    /// que Unity importa con su importador de fuentes y uGUI consume por el
    /// camino NORMAL de una fuente dinámica.
    ///
    /// POR QUÉ ASÍ (y no una fuente de mapa de bits): una fuente no dinámica de
    /// uGUI tiene UN tamaño de atlas y uGUI la escala para los demás, así que el
    /// HUD —que escribe a 13, 14, 15, 16, 17, 19 y 24 px— saldría borroso en la
    /// mitad de sus textos. Un TTF es una fuente VECTORIAL: cada tamaño se
    /// rasteriza a su medida, exactamente como con la fuente de relleno que
    /// sustituimos, y por eso el aspecto del HUD no se degrada al cambiar de
    /// tipografía. Además el fichero es determinista byte a byte (no se sellan
    /// fechas de «ahora»), así que regenerarlo no ensucia el repo.
    ///
    /// El formato que se emite es el mínimo que FreeType (el rasterizador que
    /// Unity usa) necesita, y los glifos son polígonos SIN curvas: la gramática
    /// del modelo ya aproxima las curvas con vértices, así que no hay puntos de
    /// control cuadráticos que codificar (ni sorpresas al orientarlos).
    ///
    /// Tablas: head · hhea · maxp · hmtx · cmap(format 4) · loca(larga) · glyf ·
    /// name · OS/2(v4) · post(v3).
    /// </summary>
    public static class TtfWriter
    {
        private const int TableCount = 10;
        private const int GlyphHeaderBytes = 12; // 5 int16 + instructionLength delante de flags

        /// <summary>Un glifo listo para volcar: código, avance en unidades de la em y contornos (convexos, antihorarios).</summary>
        public sealed class Glyph
        {
            public readonly int Codepoint;      // −1 = .notdef
            public readonly int Advance;
            public readonly IReadOnlyList<Stamp> Contours;

            public Glyph(int codepoint, int advance, IReadOnlyList<Stamp> contours)
            {
                Codepoint = codepoint;
                Advance = advance;
                Contours = contours ?? Array.Empty<Stamp>();
            }
        }

        /// <summary>
        /// Construye el fichero a partir de TODOS los glifos del modelo, en el
        /// orden de <see cref="GameFontModel.Codepoints"/> (que es lo que hace
        /// que la cmap se pueda describir por tramos contiguos).
        /// </summary>
        public static byte[] BuildFromModel(string familyName)
        {
            var cps = GameFontModel.Codepoints;
            var glyphs = new List<Glyph>(cps.Count + 1)
            {
                // Glifo 0 (.notdef): VACÍO a propósito. Un hueco es preferible a
                // una caja inventada si algún día se cuela un carácter sin
                // glifo — y la cobertura se comprueba en los tests.
                new Glyph(-1, GameFontModel.Advance, Array.Empty<Stamp>()),
            };
            foreach (int cp in cps)
                glyphs.Add(new Glyph(cp, GameFontModel.AdvanceOf(cp), GameFontModel.Stamps(cp)));

            return Build(familyName, glyphs, GameFontModel.UnitsPerEm,
                GameFontModel.Ascender, GameFontModel.Descender, GameFontModel.LineGap);
        }

        /// <summary>El fichero completo. Determinista: los mismos glifos dan los mismos bytes.</summary>
        public static byte[] Build(string familyName, IReadOnlyList<Glyph> glyphs,
            int unitsPerEm, int ascender, int descender, int lineGap)
        {
            if (glyphs.Count < 1) throw new ArgumentException("hace falta el glifo .notdef", nameof(glyphs));
            if (glyphs[0].Codepoint != -1) throw new ArgumentException("el glifo 0 debe ser .notdef", nameof(glyphs));

            var glyf = new List<byte[]>(glyphs.Count);
            var offsets = new uint[glyphs.Count + 1];
            int totalPoints = 0, maxPoints = 0, maxContours = 0;
            int gxMin = int.MaxValue, gyMin = int.MaxValue, gxMax = int.MinValue, gyMax = int.MinValue;
            var xs = new int[glyphs.Count];
            var lsbs = new int[glyphs.Count];

            for (int i = 0; i < glyphs.Count; i++)
            {
                offsets[i] = (uint)Sum(glyf);
                var (bytes, pts, contours, xMin, yMin, xMax, yMax) = EncodeGlyph(glyphs[i].Contours);
                glyf.Add(bytes);
                totalPoints += pts;
                if (pts > maxPoints) maxPoints = pts;
                if (contours > maxContours) maxContours = contours;
                xs[i] = glyphs[i].Advance;
                lsbs[i] = contours > 0 ? xMin : 0;
                if (contours > 0)
                {
                    if (xMin < gxMin) gxMin = xMin;
                    if (yMin < gyMin) gyMin = yMin;
                    if (xMax > gxMax) gxMax = xMax;
                    if (yMax > gyMax) gyMax = yMax;
                }
            }
            offsets[glyphs.Count] = (uint)Sum(glyf);
            if (gxMin == int.MaxValue) { gxMin = gyMin = 0; gxMax = gyMax = 0; }

            byte[] glyfTable = Flatten(glyf);

            // — hmtx ————————————————————————————————————————————————
            var hmtx = new Buf(glyphs.Count * 4);
            int advanceMax = 0, minLsb = int.MaxValue, minRsb = int.MaxValue, xMaxExtent = int.MinValue;
            for (int i = 0; i < glyphs.Count; i++)
            {
                hmtx.U16((ushort)xs[i]);
                hmtx.I16(lsbs[i]);
                if (xs[i] > advanceMax) advanceMax = xs[i];
                if (lsbs[i] < minLsb) minLsb = lsbs[i];
                int rsb = xs[i] - lsbs[i] - (glyphs[i].Contours.Count > 0 ? GlyphWidth(glyphs[i]) : 0);
                if (rsb < minRsb) minRsb = rsb;
                int extent = lsbs[i] + (glyphs[i].Contours.Count > 0 ? GlyphWidth(glyphs[i]) : 0);
                if (extent > xMaxExtent) xMaxExtent = extent;
            }
            if (minLsb == int.MaxValue) minLsb = 0;
            if (minRsb == int.MaxValue) minRsb = 0;
            if (xMaxExtent == int.MinValue) xMaxExtent = 0;

            // — loca — (formato LARGO: sin la restricción de offsets pares)
            var loca = new Buf(offsets.Length * 4);
            foreach (uint o in offsets) loca.U32(o);

            // — head ————————————————————————————————————————————————
            // Fechas FIJAS (no `DateTime.Now`): un generador que sella la hora
            // reescribe el asset en cada pasada y ensucia el repo sin cambiar el
            // dibujo. Misma fecha ⇒ mismos bytes ⇒ regenerar es idempotente.
            long epoch = (long)(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                                - new DateTime(1904, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
            var head = new Buf(54);
            head.U32(0x00010000);       // version
            head.U32(0x00010000);       // fontRevision
            head.U32(0);                // checkSumAdjustment (se rellena al final)
            head.U32(0x5F0F3CF5);       // magicNumber
            head.U16(0x000B);           // flags: baseline y=0, lsb x=0, instrucciones dependen del tamaño
            head.U16((ushort)unitsPerEm);
            head.I64(epoch); head.I64(epoch);
            head.I16(gxMin); head.I16(gyMin); head.I16(gxMax); head.I16(gyMax);
            head.U16(0);                // macStyle (regular)
            head.U16(8);                // lowestRecPPEM
            head.I16(2);                // fontDirectionHint
            head.I16(1);                // indexToLocFormat = largo
            head.I16(0);                // glyphDataFormat

            // — hhea ————————————————————————————————————————————————
            var hhea = new Buf(36);
            hhea.U32(0x00010000);
            hhea.I16(ascender); hhea.I16(descender); hhea.I16(lineGap);
            hhea.U16((ushort)advanceMax);
            hhea.I16(minLsb); hhea.I16(minRsb); hhea.I16(xMaxExtent);
            hhea.I16(1); hhea.I16(0); hhea.I16(0);      // caretSlopeRise/Run/Offset
            hhea.I16(0); hhea.I16(0); hhea.I16(0); hhea.I16(0); // reservado
            hhea.I16(0);                                 // metricDataFormat
            hhea.U16((ushort)glyphs.Count);              // numberOfHMetrics

            // — maxp ————————————————————————————————————————————————
            var maxp = new Buf(32);
            maxp.U32(0x00010000);
            maxp.U16((ushort)glyphs.Count);
            maxp.U16((ushort)maxPoints);
            maxp.U16((ushort)maxContours);
            maxp.U16(0); maxp.U16(0);   // maxCompositePoints / maxCompositeContours
            maxp.U16(1);                // maxZones
            maxp.U16(0);                // maxTwilightPoints
            maxp.U16(0); maxp.U16(0); maxp.U16(0);   // maxStorage/FunctionDefs/InstructionDefs
            maxp.U16(0);                // maxStackElements
            maxp.U16(0);                // maxSizeOfInstructions
            maxp.U16(0);                // maxComponentElements
            maxp.U16(0);                // maxComponentDepth

            // — cmap ————————————————————————————————————————————————
            byte[] cmap = BuildCmap(glyphs);

            // — name ————————————————————————————————————————————————
            byte[] name = BuildName(familyName);

            // — OS/2 v4 ——————————————————————————————————————————————
            var os2 = new Buf(96);
            os2.U16(4);                 // version
            os2.I16(AdvanceAverage(glyphs));    // xAvgCharWidth
            os2.U16(400);               // usWeightClass (regular)
            os2.U16(5);                 // usWidthClass (medio)
            os2.U16(0);                 // fsType (instalable)
            os2.I16(650); os2.I16(600); os2.I16(0); os2.I16(75);      // subscript
            os2.I16(650); os2.I16(600); os2.I16(0); os2.I16(350);     // superscript
            os2.I16(50); os2.I16(260);  // strikeout
            os2.I16(0);                 // sFamilyClass
            for (int i = 0; i < 10; i++) os2.U8(0);   // panose
            // ulUnicodeRange: L basic latin (bit 0), latin-1 (bit 1), griego (bit 7),
            // símbolos varios + flechas (bits 4/14/12) — informativo, FreeType no lo mira.
            os2.U32(0x0000400B); os2.U32(0x04000000); os2.U32(0x10000000); os2.U32(0);
            os2.Ascii("ANTR");          // achVendID
            os2.U16(0x0040);            // fsSelection: REGULAR
            os2.U16((ushort)FirstCode(glyphs));
            os2.U16((ushort)LastCode(glyphs));
            os2.I16(ascender); os2.I16(descender); os2.I16(0);
            os2.U16((ushort)ascender); os2.U16((ushort)-descender);
            os2.U32(1); os2.U32(0);     // ulCodePageRange (latin-1)
            os2.I16(500);               // sxHeight
            os2.I16(700);               // sCapHeight
            os2.U16(0x0020);            // usDefaultChar (espacio)
            os2.U16(0x0020);            // usBreakChar
            os2.U16(1);                 // usMaxContext

            // — post v3 ——————————————————————————————————————————————
            var post = new Buf(32);
            post.U32(0x00030000);
            post.U32(0);                // italicAngle
            post.I16(-100); post.I16(50);
            post.U32(1);                // isFixedPitch (monorriel)
            post.U32(0); post.U32(0); post.U32(0); post.U32(0);

            // — Ensamblado ————————————————————————————————————————————
            var tables = new List<(string Tag, byte[] Data)>
            {
                ("OS/2", os2.Bytes), ("cmap", cmap), ("glyf", glyfTable),
                ("head", head.Bytes), ("hhea", hhea.Bytes), ("hmtx", hmtx.Bytes),
                ("loca", loca.Bytes), ("maxp", maxp.Bytes), ("name", name),
                ("post", post.Bytes),
            };
            if (tables.Count != TableCount)
                throw new InvalidOperationException($"se esperaban {TableCount} tablas y hay {tables.Count}");
            tables.Sort((a, b) => string.CompareOrdinal(a.Tag, b.Tag));

            int n = tables.Count;
            int searchRange = 1, entrySelector = 0;
            while (searchRange * 2 <= n) { searchRange *= 2; entrySelector++; }
            searchRange *= 16;
            int tableDir = 12 + 16 * n;

            var dir = new Buf(12 + 16 * n);
            dir.U32(0x00010000);
            dir.U16((ushort)n);
            dir.U16((ushort)searchRange);
            dir.U16((ushort)entrySelector);
            dir.U16((ushort)(n * 16 - searchRange));

            var body = new Buf(1 << 16);
            var record = new Dictionary<string, (int Offset, int Length, uint Sum)>(n);
            int cursor = tableDir;
            foreach (var (tag, data) in tables)
            {
                // Los offsets de tabla van alineados a 4 bytes (lo pide el formato).
                while (cursor % 4 != 0) { body.U8(0); cursor++; }
                record[tag] = (cursor, data.Length, Checksum(data));
                body.Append(data);
                while (body.Count % 4 != 0) body.U8(0);
                cursor = tableDir + body.Count;
            }
            foreach (var (tag, data) in tables)
            {
                var (offset, length, sum) = record[tag];
                dir.Tag(tag);
                dir.U32(sum);
                dir.U32((uint)offset);
                dir.U32((uint)length);
            }

            var file = new Buf(tableDir + body.Count);
            file.Append(dir.Bytes);
            file.Append(body.Bytes);

            // checksumAdjustment: 0xB1B0AFBA − suma del fichero ENTERO, con el
            // campo a 0 (que es como está ahora). Se escribe en su sitio: head
            // empieza en su offset y el campo es el tercer uint32 (byte 8).
            var (headOffset, _, _) = record["head"];
            uint whole = Checksum(file.Bytes);
            uint adjustment = 0xB1B0AFBA - whole;
            var outBytes = file.Bytes;
            outBytes[headOffset + 8] = (byte)(adjustment >> 24);
            outBytes[headOffset + 9] = (byte)(adjustment >> 16);
            outBytes[headOffset + 10] = (byte)(adjustment >> 8);
            outBytes[headOffset + 11] = (byte)adjustment;
            return outBytes;
        }

        // ── glyf: un glifo = contornos de polígonos, todos con puntos ON-CURVE ──
        private static (byte[] Bytes, int Points, int Contours, int XMin, int YMin, int XMax, int YMax)
            EncodeGlyph(IReadOnlyList<Stamp> contours)
        {
            if (contours.Count == 0)
                return (Array.Empty<byte>(), 0, 0, 0, 0, 0, 0);

            var pts = new List<(int X, int Y)>();
            var ends = new List<ushort>(contours.Count);
            int xMin = int.MaxValue, yMin = int.MaxValue, xMax = int.MinValue, yMax = int.MinValue;
            foreach (var stamp in contours)
            {
                foreach (var p in stamp.P)
                {
                    int x = (int)Math.Round(p.X, MidpointRounding.AwayFromZero);
                    int y = (int)Math.Round(p.Y, MidpointRounding.AwayFromZero);
                    pts.Add((x, y));
                    if (x < xMin) xMin = x;
                    if (y < yMin) yMin = y;
                    if (x > xMax) xMax = x;
                    if (y > yMax) yMax = y;
                }
                ends.Add((ushort)(pts.Count - 1));
            }

            var b = new Buf(GlyphHeaderBytes + ends.Count * 2 + pts.Count * 5);
            b.I16(contours.Count);
            b.I16(xMin); b.I16(yMin); b.I16(xMax); b.I16(yMax);
            foreach (ushort e in ends) b.U16(e);
            b.U16(0); // instructionLength
            // flags: 0x01 = punto ON-CURVE, sin atajos ⇒ x e y van como int16
            // desplazados. Es la codificación más simple y no tiene casos raros
            // (los atajos de 1 byte solo ahorran tamaño).
            foreach (var _ in pts) b.U8(0x01);
            int px = 0, py = 0;
            foreach (var (x, _) in pts) { b.I16(x - px); px = x; }
            foreach (var (_, y) in pts) { b.I16(y - py); py = y; }

            byte[] bytes = b.Bytes;
            if (bytes.Length % 2 != 0) Array.Resize(ref bytes, bytes.Length + 1);
            return (bytes, pts.Count, contours.Count, xMin, yMin, xMax, yMax);
        }

        private static int GlyphWidth(Glyph g)
        {
            int min = int.MaxValue, max = int.MinValue;
            foreach (var s in g.Contours)
                foreach (var p in s.P)
                {
                    int x = (int)Math.Round(p.X, MidpointRounding.AwayFromZero);
                    if (x < min) min = x;
                    if (x > max) max = x;
                }
            return min == int.MaxValue ? 0 : max - min;
        }

        /// <summary>cmap formato 4 (BMP): un tramo por cada grupo de códigos
        /// consecutivos, con idDelta constante porque los ids se asignan en orden
        /// de código. Cierra con el tramo obligatorio 0xFFFF→0xFFFF.</summary>
        private static byte[] BuildCmap(IReadOnlyList<Glyph> glyphs)
        {
            var segs = new List<(int Start, int End, int Delta)>();
            for (int i = 1; i < glyphs.Count; i++)
            {
                int cp = glyphs[i].Codepoint;
                if (cp < 0) continue;
                int delta = (i - cp) & 0xFFFF;
                if (segs.Count > 0 && cp == segs[segs.Count - 1].End + 1
                    && delta == segs[segs.Count - 1].Delta)
                {
                    var last = segs[segs.Count - 1];
                    segs[segs.Count - 1] = (last.Start, cp, delta);
                }
                else
                {
                    segs.Add((cp, cp, delta));
                }
            }
            segs.Add((0xFFFF, 0xFFFF, 1)); // terminador del formato

            int segCount = segs.Count;
            int subLen = 14 + 2 + 8 * segCount;
            var sub = new Buf(subLen);
            sub.U16(4);                 // format
            sub.U16((ushort)subLen);    // length
            sub.U16(0);                 // language
            sub.U16((ushort)(segCount * 2));
            int sr = 1, es = 0;
            while (sr * 2 <= segCount) { sr *= 2; es++; }
            sub.U16((ushort)(sr * 2));
            sub.U16((ushort)es);
            sub.U16((ushort)(segCount * 2 - sr * 2));
            foreach (var s in segs) sub.U16((ushort)s.End);
            sub.U16(0);                 // reservedPad
            foreach (var s in segs) sub.U16((ushort)s.Start);
            foreach (var s in segs) sub.I16((short)s.Delta);
            for (int i = 0; i < segCount; i++) sub.U16(0); // idRangeOffset

            var cmap = new Buf(4 + 8 + subLen);
            cmap.U16(0);                // version
            cmap.U16(1);                // numTables
            cmap.U16(3); cmap.U16(1);   // Windows / Unicode BMP
            cmap.U32(12);               // offset de la subtabla
            cmap.Append(sub.Bytes);
            return cmap.Bytes;
        }

        private static byte[] BuildName(string family)
        {
            // Windows (3,1) + Macintosh (1,0): FreeType toma la de Windows, pero
            // algunos lectores miran la de Mac; las dos van en ASCII/UTF-16BE.
            (int Platform, int Encoding, int Language, int NameId, string Value)[] names =
            {
                (3, 1, 0x0409, 1, family), (3, 1, 0x0409, 2, "Regular"),
                (3, 1, 0x0409, 4, family), (3, 1, 0x0409, 6, family),
                (1, 0, 0, 1, family), (1, 0, 0, 2, "Regular"),
                (1, 0, 0, 4, family), (1, 0, 0, 6, family),
            };

            var storage = new Buf(256);
            var recs = new List<(int Platform, int Encoding, int Language, int NameId, int Offset, int Length)>();
            foreach (var (platform, encoding, language, nameId, value) in names)
            {
                int offset = storage.Count;
                byte[] encoded = platform == 3
                    ? Encoding.BigEndianUnicode.GetBytes(value)
                    : Encoding.ASCII.GetBytes(value);
                storage.Append(encoded);
                recs.Add((platform, encoding, language, nameId, offset, encoded.Length));
            }

            var b = new Buf(6 + recs.Count * 12 + storage.Count);
            b.U16(0);                       // format
            b.U16((ushort)recs.Count);
            b.U16((ushort)(6 + recs.Count * 12)); // stringOffset
            foreach (var r in recs)
            {
                b.U16((ushort)r.Platform);
                b.U16((ushort)r.Encoding);
                b.U16((ushort)r.Language);
                b.U16((ushort)r.NameId);
                b.U16((ushort)r.Length);
                b.U16((ushort)r.Offset);
            }
            b.Append(storage.Bytes);
            return b.Bytes;
        }

        private static int AdvanceAverage(IReadOnlyList<Glyph> glyphs)
        {
            long sum = 0;
            foreach (var g in glyphs) sum += g.Advance;
            return (int)(sum / Math.Max(1, glyphs.Count));
        }

        private static int FirstCode(IReadOnlyList<Glyph> glyphs)
        {
            foreach (var g in glyphs) if (g.Codepoint >= 0) return g.Codepoint;
            return 0x20;
        }

        private static int LastCode(IReadOnlyList<Glyph> glyphs)
        {
            int last = 0x20;
            foreach (var g in glyphs) if (g.Codepoint > last) last = g.Codepoint;
            return last;
        }

        private static int Sum(List<byte[]> parts)
        {
            int n = 0;
            foreach (var p in parts) n += p.Length;
            return n;
        }

        private static byte[] Flatten(List<byte[]> parts)
        {
            var b = new Buf(Sum(parts));
            foreach (var p in parts) b.Append(p);
            return b.Bytes;
        }

        /// <summary>Suma de comprobación estándar: los datos como palabras de 32 bits big-endian, rellenando con ceros.</summary>
        internal static uint Checksum(byte[] data)
        {
            uint sum = 0;
            int i = 0;
            for (; i + 3 < data.Length; i += 4)
                sum += (uint)((data[i] << 24) | (data[i + 1] << 16) | (data[i + 2] << 8) | data[i + 3]);
            if (i < data.Length)
            {
                uint last = 0;
                for (int k = 0; k < 4; k++)
                    last |= (uint)(k < data.Length - i ? data[i + k] : 0) << (24 - 8 * k);
                sum += last;
            }
            return sum;
        }

        /// <summary>Escritor big-endian (el TTF es big-endian de punta a punta).</summary>
        private sealed class Buf
        {
            private byte[] _data;
            public int Count { get; private set; }

            public Buf(int capacity) { _data = new byte[Math.Max(16, capacity)]; }

            private void Ensure(int extra)
            {
                if (Count + extra <= _data.Length) return;
                int cap = _data.Length * 2;
                while (cap < Count + extra) cap *= 2;
                Array.Resize(ref _data, cap);
            }

            public void U8(byte v) { Ensure(1); _data[Count++] = v; }
            public void U16(ushort v) { Ensure(2); _data[Count++] = (byte)(v >> 8); _data[Count++] = (byte)v; }
            public void I16(int v) => U16((ushort)(short)v);
            public void U32(uint v)
            {
                Ensure(4);
                _data[Count++] = (byte)(v >> 24); _data[Count++] = (byte)(v >> 16);
                _data[Count++] = (byte)(v >> 8); _data[Count++] = (byte)v;
            }
            public void I64(long v)
            {
                U32((uint)(v >> 32));
                U32((uint)v);
            }
            public void Ascii(string s) { foreach (char c in s) U8((byte)c); }
            public void Append(byte[] b) { Ensure(b.Length); Array.Copy(b, 0, _data, Count, b.Length); Count += b.Length; }
            /// <summary>Etiqueta de 4 caracteres de la tabla (el TTF la quiere ASCII de 4).</summary>
            public void Tag(string tag)
            {
                for (int i = 0; i < 4; i++) U8((byte)(i < tag.Length ? tag[i] : ' '));
            }

            public byte[] Bytes
            {
                get
                {
                    if (Count == _data.Length) return _data;
                    var r = new byte[Count];
                    Array.Copy(_data, r, Count);
                    return r;
                }
            }
        }
    }
}

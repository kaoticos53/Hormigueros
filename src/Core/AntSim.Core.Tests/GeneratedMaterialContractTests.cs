using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using AntSim.Unity.Scripts.Streaming;
using AntSim.Unity.Scripts.UI;
using Xunit;

namespace AntSim.Core.Tests
{
    /// <summary>
    /// CONTRATO DE LOS MATERIALES Y LAS TEXTURAS GENERADOS: cada `.mat` y cada
    /// `.asset` de `Assets/Materials` que el repo versiona tiene que ser lo que
    /// dice <see cref="GameMaterialModel"/> — el color, el tipo (plano / rejilla /
    /// transparente), la textura a la que apunta y el píxel de esa textura.
    ///
    /// POR QUÉ EXISTE. Es la mitad que faltaba de
    /// <c>GeneratedAssetContractTests</c>: aquel compara el `.ttf` y las mallas
    /// contra sus modelos, pero los MATERIALES son el otro conjunto de artefactos
    /// del bootstrapper —los que PINTAN el mundo— y sus colores vivían como
    /// literales dentro de los montadores de editor, que no compilan en la suite.
    /// Un color que se mueve en el modelo y no se regenera, o un `.mat` tocado a
    /// mano, o ese material que «se regenera» y pierde su textura
    /// (`_BaseMap: {fileID: 0}` = quad blanco opaco encima del tablero: el defecto
    /// del Play pass de F5.1), se ven aquí en milisegundos y sin abrir Unity.
    ///
    /// QUÉ SE COMPRUEBA, y por qué así:
    ///   · el catálogo es una BIYECCIÓN —los `.mat` de disco son exactamente los
    ///     del modelo, ni uno de más ni uno de menos—: si el bootstrapper estrena
    ///     un material y no entra en el modelo, esto lo dice en vez de dejarlo sin
    ///     contrato; y al revés, un nombre del modelo sin fichero es un artefacto
    ///     que falta.
    ///   · el COLOR se lee del `.mat` (no del modelo) y se compara con el modelo,
    ///     en `_BaseColor` y en `_Color` (los dos, que el montador escribe los
    ///     dos), en la misma escala en la que Unity lo serializa: MEDIDO, guarda el
    ///     valor AUTORADO tal cual, sin pasarlo a espacio lineal.
    ///   · el TIPO decide el resto de la receta y se comprueba entera: los planos
    ///     llevan instancing y son opacos; la rejilla apunta a SU textura con las
    ///     repeticiones del modelo; la capa de feromonas es transparente
    ///     (`_Surface`, `_SrcBlend`/`_DstBlend`, `_ZWrite`, `renderQueue` y la
    ///     palabra clave `_SURFACE_TYPE_TRANSPARENT`) y apunta a la textura de
    ///     reposo.
    ///   · los PÍXELES de las texturas se decodifican del blob `_typelessdata` y se
    ///     comparan uno a uno con el patrón del modelo: la rejilla con su línea en
    ///     el borde superior e izquierdo, y el reposo blanco con alpha 0 (alpha 255
    ///     volvería a tapar el tablero).
    ///
    /// LA REPARACIÓN QUE PIDE UN FALLO de aquí es siempre la misma que en el
    /// contrato hermano: abrir el proyecto y dejar que el bootstrapper regenere (o
    /// arreglar el modelo, si el que está mal es el dato). El mensaje nombra el
    /// fichero, el campo y, cuando aplica, el píxel.
    /// </summary>
    public class GeneratedMaterialContractTests
    {
        private const string MaterialsDir = "src/App/AntSim.Unity/Assets/Materials";

        [Fact]
        public void MaterialesGenerados_SonLosDelModelo()
        {
            string dir = Path.Combine(RepoRoot(), MaterialsDir);
            Assert.True(Directory.Exists(dir), "no existe el directorio de materiales: " + dir);

            var esperados = CatalogoDelModelo();

            // Biyección: ni falta ninguno ni sobra ninguno.
            foreach (string name in esperados.Keys)
                Assert.True(File.Exists(Path.Combine(dir, name + ".mat")),
                    $"falta {name}.mat: lo escribe el bootstrapper desde GameMaterialModel " +
                    "(abre el proyecto y regenera la escena).");
            foreach (string file in Directory.GetFiles(dir, "*.mat"))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                Assert.True(esperados.ContainsKey(name),
                    $"{name}.mat no está en GameMaterialModel: añádelo al modelo (o bórralo del disco " +
                    "si el bootstrapper ya no lo genera).");
            }
            Assert.True(esperados.Count >= 70,
                $"el catálogo del modelo se quedó corto ({esperados.Count} materiales): ¿se dejó de " +
                "generar la familia de una vista?");

            foreach (var (name, entry) in esperados)
            {
                var spec = entry.Spec;
                var mat = MatYaml.Read(Path.Combine(dir, name + ".mat"));
                string id = name + ".mat";

                Assert.Equal(name, mat.Name);
                Assert.Equal(GameMaterialModel.ShaderGuid, mat.ShaderGuid);
                Assert.Equal(spec.Instancing, mat.Instancing);
                AssertColor(spec.Color, mat.BaseColor, id + " (_BaseColor)");
                AssertColor(spec.Color, mat.Color, id + " (_Color)");

                switch (spec.MaterialKind)
                {
                    case GameMaterialModel.Kind.Flat:
                        // Plano: sin textura en NINGÚN slot (un `fileID` colado aquí
                        // sería un material que pinta una textura que nadie pidió),
                        // opaco y con instancing (que ya se comprobó arriba).
                        Assert.Equal(0, mat.BaseMapFileId);
                        Assert.Equal(0, mat.MainTexFileId);
                        AssertOpaque(mat, id);
                        break;

                    case GameMaterialModel.Kind.Grid:
                    {
                        string tex = GameMaterialModel.GridTextureName(entry.View);
                        string guid = TextureGuid(dir, tex);
                        Assert.Equal(guid, mat.BaseMapGuid);
                        Assert.Equal(guid, mat.MainTexGuid);
                        Assert.Equal(GameMaterialModel.GridTiles, mat.BaseMapScaleX, 6);
                        Assert.Equal(GameMaterialModel.GridTiles, mat.BaseMapScaleY, 6);
                        Assert.Equal(0f, mat.BaseMapOffsetX, 6);
                        Assert.Equal(0f, mat.BaseMapOffsetY, 6);
                        AssertOpaque(mat, id);
                        break;
                    }

                    case GameMaterialModel.Kind.Transparent:
                    {
                        string tex = GameMaterialModel.ReposeTextureName(entry.View);
                        string guid = TextureGuid(dir, tex);
                        Assert.Equal(guid, mat.BaseMapGuid);
                        Assert.Equal(guid, mat.MainTexGuid);
                        // La receta transparente entera: sin ella el quad de
                        // feromonas vuelve a tapar el tablero.
                        Assert.True(mat.SurfaceTransparent,
                            $"{id}: le falta la palabra clave _SURFACE_TYPE_TRANSPARENT");
                        Assert.Equal(1f, mat.Surface, 6);
                        Assert.Equal(0f, mat.Blend, 6);
                        Assert.Equal(5f, mat.SrcBlend, 6);      // BlendMode.SrcAlpha
                        Assert.Equal(10f, mat.DstBlend, 6);     // BlendMode.OneMinusSrcAlpha
                        Assert.Equal(0f, mat.ZWrite, 6);
                        Assert.Equal(3000, mat.RenderQueue);    // RenderQueue.Transparent
                        Assert.Equal("Transparent", mat.RenderType);
                        break;
                    }

                    default:
                        Assert.Fail($"{id}: el modelo pide un tipo de material que este test no conoce ({spec.MaterialKind})");
                        break;
                }
            }
        }

        [Fact]
        public void TexturasGeneradas_TienenLosPixelesYElFormatoDelModelo()
        {
            string dir = Path.Combine(RepoRoot(), MaterialsDir);

            // — Rejillas: la del juego y la de cada vista del multi-visor —
            var rejillas = new List<string> { GameMaterialModel.GridTextureName(-1) };
            for (int v = 0; v < MultiViewportModel.MaxViews; v++)
                rejillas.Add(GameMaterialModel.GridTextureName(v));
            foreach (string name in rejillas)
            {
                string id = name + ".asset";
                var tex = TexYaml.Read(Path.Combine(dir, id));
                Assert.Equal(name, tex.Name);
                AssertTexture(tex, GameMaterialModel.GridSize, GameMaterialModel.GridSize, id);
                for (int y = 0; y < tex.Height; y++)
                    for (int x = 0; x < tex.Width; x++)
                    {
                        var esperado = GameMaterialModel.IsGridLine(x, y)
                            ? GameMaterialModel.GridLine : GameMaterialModel.GridFill;
                        Assert.True(PixelEquals(esperado, tex.Pixel(x, y)),
                            $"{id}: el píxel ({x}, {y}) es {Fmt(tex.Pixel(x, y))} y el modelo pide " +
                            $"{Fmt(esperado)} (línea en los bordes, relleno en el resto).");
                    }
            }

            // — Reposo: 1×1 blanco con alpha 0 (invisible) —
            var reposo = new List<string> { GameMaterialModel.ReposeTextureName(-1) };
            for (int v = 0; v < MultiViewportModel.MaxViews; v++)
                reposo.Add(GameMaterialModel.ReposeTextureName(v));
            foreach (string name in reposo)
            {
                string id = name + ".asset";
                var tex = TexYaml.Read(Path.Combine(dir, id));
                Assert.Equal(name, tex.Name);
                AssertTexture(tex, 1, 1, id);
                Assert.True(PixelEquals(GameMaterialModel.ReposePixel, tex.Pixel(0, 0)),
                    $"{id}: el píxel es {Fmt(tex.Pixel(0, 0))} y el modelo pide " +
                    $"{Fmt(GameMaterialModel.ReposePixel)}: alpha 255 convierte el reposo en un " +
                    "quad BLANCO OPACO que tapa el tablero.");
            }
        }

        /// <summary>
        /// Los lectores LEEN: si devolvieran el modelo en vez del fichero, el test
        /// de arriba estaría comparando el modelo consigo mismo y pasaría siempre.
        /// Aquí se escriben un `.mat` con el color cambiado y una textura con el
        /// alpha cambiado, y se exige que el lector lo VEA — la comprobación
        /// negativa del contrato, en la forma de una mutación del artefacto.
        /// </summary>
        [Fact]
        public void LectoresDeMaterialYTextura_LeenElFicheroYNoElModelo()
        {
            string real = Path.Combine(RepoRoot(), MaterialsDir);
            string tmp = Path.Combine(Path.GetTempPath(), "antsim-material-contract-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            try
            {
                // El material: el mismo fichero con el rojo de AntMat movido.
                string matPath = Path.Combine(tmp, "AntMat.mat");
                string matText = File.ReadAllText(Path.Combine(real, "AntMat.mat"));
                Assert.Contains("r: 0.35", matText);
                File.WriteAllText(matPath, matText.Replace("r: 0.35", "r: 0.99"));

                var mat = MatYaml.Read(matPath);
                Assert.Equal(0.99f, mat.BaseColor.R, 6);
                Assert.Equal(0.35f, GameMaterialModel.ByName("AntMat").Color.R, 6);   // el modelo no se entera

                // La textura: el mismo activo con el alpha del píxel de reposo a 255.
                string texPath = Path.Combine(tmp, "PheromoneEmpty.asset");
                string texText = File.ReadAllText(Path.Combine(real, "PheromoneEmpty.asset"));
                Assert.Contains("ffffff00", texText);
                File.WriteAllText(texPath, texText.Replace("ffffff00", "ffffffff"));

                var tex = TexYaml.Read(texPath);
                Assert.Equal(255, tex.Pixel(0, 0).A);
                Assert.Equal(0, GameMaterialModel.ReposePixel.A);                      // idem
            }
            finally
            {
                Directory.Delete(tmp, true);
            }
        }

        // ── herramientas del test ──────────────────────────────────────────

        /// <summary>Todo `.mat` que el bootstrapper debe haber dejado: el catálogo
        /// del modelo (escena simple) más la variante por vista de cada material
        /// que el multi-visor construye.</summary>
        private static SortedDictionary<string, (GameMaterialModel.Spec Spec, int View)> CatalogoDelModelo()
        {
            var map = new SortedDictionary<string, (GameMaterialModel.Spec, int)>(StringComparer.Ordinal);
            foreach (var spec in GameMaterialModel.Specs)
            {
                map[spec.Name] = (spec, -1);
                if (!spec.InViews) continue;
                for (int v = 0; v < MultiViewportModel.MaxViews; v++)
                    map[spec.ViewName(v)] = (spec, v);
            }
            return map;
        }

        private static string TextureGuid(string dir, string textureName)
        {
            string meta = Path.Combine(dir, textureName + ".asset.meta");
            Assert.True(File.Exists(meta), $"falta el .meta de {textureName} (¿se borró el asset?)");
            var m = Regex.Match(File.ReadAllText(meta), @"guid: ([0-9a-f]{32})");
            Assert.True(m.Success, $"{textureName}.asset.meta no declara un guid");
            return m.Groups[1].Value;
        }

        private static void AssertOpaque(MatYaml mat, string id)
        {
            Assert.False(mat.SurfaceTransparent, $"{id}: no debería llevar _SURFACE_TYPE_TRANSPARENT");
            Assert.Equal(0f, mat.Surface, 6);
            Assert.Equal(0f, mat.Blend, 6);
            Assert.Equal(1f, mat.SrcBlend, 6);     // BlendMode.One
            Assert.Equal(0f, mat.DstBlend, 6);     // BlendMode.Zero
            Assert.Equal(1f, mat.ZWrite, 6);
            Assert.Equal(-1, mat.RenderQueue);      // el de la cola del shader
            Assert.Equal("Opaque", mat.RenderType);
        }

        private static void AssertTexture(TexYaml tex, int w, int h, string id)
        {
            Assert.Equal(w, tex.Width);
            Assert.Equal(h, tex.Height);
            Assert.Equal(GameMaterialModel.Rgba32Format, tex.Format);   // RGBA32
            Assert.Equal(1, tex.Mips);                                  // sin mipmaps
            Assert.Equal(GameMaterialModel.WrapRepeat, tex.WrapU);
            Assert.Equal(GameMaterialModel.WrapRepeat, tex.WrapV);
            Assert.Equal(GameMaterialModel.FilterBilinear, tex.Filter);
            Assert.Equal(w * h * 4, tex.Pixels.Length);
        }

        private static bool PixelEquals(GameMaterialModel.Rgba32 a, GameMaterialModel.Rgba32 b)
            => a.R == b.R && a.G == b.G && a.B == b.B && a.A == b.A;

        private static string Fmt(GameMaterialModel.Rgba32 c) => $"({c.R}, {c.G}, {c.B}, {c.A})";

        private static void AssertColor(GameMaterialModel.Rgba expected, GameMaterialModel.Rgba actual, string what)
        {
            Assert.True(Close(expected.R, actual.R) && Close(expected.G, actual.G)
                && Close(expected.B, actual.B) && Close(expected.A, actual.A),
                $"{what}: el .mat dice ({actual.R}, {actual.G}, {actual.B}, {actual.A}) y el modelo " +
                $"({expected.R}, {expected.G}, {expected.B}, {expected.A}).");
        }

        private static bool Close(float a, float b) => Math.Abs(a - b) <= 1e-6f;

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AntSim.slnx")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return dir!.FullName;
        }

        /// <summary>
        /// Lo imprescindible de un `.mat` serializado en YAML: identidad (nombre y
        /// shader), estado (instancing, cola, palabras clave) y los valores que el
        /// montador escribe en `m_Floats`, `m_Colors` y `m_TexEnvs`. El lector es de
        /// LÍNEAS y no un parser de YAML: lo que se lee es lo que el escritor de
        /// Unity produce, y si algún día cambia de forma, esto falla nombrando el
        /// fichero en vez de leer basura.
        /// </summary>
        private sealed class MatYaml
        {
            public string Name = "";
            public string ShaderGuid = "";
            public bool Instancing;
            public int RenderQueue;
            public bool SurfaceTransparent;
            public string RenderType = "";

            public float Surface, Blend, ZWrite, SrcBlend, DstBlend;
            public GameMaterialModel.Rgba BaseColor, Color;

            public int BaseMapFileId, MainTexFileId;
            public string? BaseMapGuid, MainTexGuid;
            public float BaseMapScaleX, BaseMapScaleY, BaseMapOffsetX, BaseMapOffsetY;

            public static MatYaml Read(string path)
            {
                string text = File.ReadAllText(path).Replace("\r", string.Empty);
                if (!text.Contains("--- !u!21"))
                    throw new InvalidOperationException(
                        $"{Path.GetFileName(path)} no parece un material de Unity (falta «--- !u!21»)");

                var m = new MatYaml();
                string section = "", slot = "";
                foreach (string raw in text.Split('\n'))
                {
                    string line = raw.Trim();
                    if (line.Length == 0) continue;

                    if (line == "m_TexEnvs:") { section = "tex"; slot = ""; continue; }
                    if (line == "m_Floats:") { section = "floats"; slot = ""; continue; }
                    if (line == "m_Colors:") { section = "colors"; slot = ""; continue; }
                    if (line.StartsWith("m_Ints:") || line.StartsWith("m_SavedProperties:")
                        || line.StartsWith("m_BuildTextureStacks:")) { section = ""; slot = ""; continue; }

                    if (line.StartsWith("m_Name: ")) { m.Name = line.Substring("m_Name: ".Length).Trim(); continue; }
                    if (line.StartsWith("m_Shader: {")) { m.ShaderGuid = TypedValue(line); continue; }
                    if (line.StartsWith("m_EnableInstancingVariants: ")) { m.Instancing = line.EndsWith("1"); continue; }
                    if (line.StartsWith("m_CustomRenderQueue: "))
                    { m.RenderQueue = int.Parse(line.Substring("m_CustomRenderQueue: ".Length).Trim(), CultureInfo.InvariantCulture); continue; }
                    if (line == "RenderType: Opaque") { m.RenderType = "Opaque"; continue; }
                    if (line == "RenderType: Transparent") { m.RenderType = "Transparent"; continue; }
                    if (line.StartsWith("- _SURFACE_TYPE_TRANSPARENT")) { m.SurfaceTransparent = true; continue; }

                    if (section == "tex")
                    {
                        if (line.StartsWith("- ") && line.EndsWith(":"))
                        { slot = line.Substring(2).TrimEnd(':'); continue; }
                        if (slot.Length == 0) continue;
                        if (line.StartsWith("m_Texture:"))
                        {
                            int fileId = FileId(line);
                            string? guid = GuidOrNull(line);
                            if (slot == "_BaseMap") { m.BaseMapFileId = fileId; m.BaseMapGuid = guid; }
                            else if (slot == "_MainTex") { m.MainTexFileId = fileId; m.MainTexGuid = guid; }
                            continue;
                        }
                        if (line.StartsWith("m_Scale:"))
                        {
                            var (x, y) = Vector(line);
                            if (slot == "_BaseMap") { m.BaseMapScaleX = x; m.BaseMapScaleY = y; }
                            continue;
                        }
                        if (line.StartsWith("m_Offset:"))
                        {
                            var (x, y) = Vector(line);
                            if (slot == "_BaseMap") { m.BaseMapOffsetX = x; m.BaseMapOffsetY = y; }
                            continue;
                        }
                        continue;
                    }

                    if (section == "floats" && line.StartsWith("- "))
                    {
                        string body = line.Substring(2);
                        int colon = body.IndexOf(':');
                        if (colon < 0) continue;
                        string key = body.Substring(0, colon).Trim();
                        float value = Parse(body.Substring(colon + 1));
                        switch (key)
                        {
                            case "_Surface": m.Surface = value; break;
                            case "_Blend": m.Blend = value; break;
                            case "_ZWrite": m.ZWrite = value; break;
                            case "_SrcBlend": m.SrcBlend = value; break;
                            case "_DstBlend": m.DstBlend = value; break;
                        }
                        continue;
                    }

                    if (section == "colors" && line.StartsWith("- "))
                    {
                        var match = Regex.Match(line, @"^- (\S+): \{r: ([^,]+), g: ([^,]+), b: ([^,]+), a: ([^}]+)\}");
                        if (!match.Success) continue;
                        var c = new GameMaterialModel.Rgba(
                            Parse(match.Groups[2].Value), Parse(match.Groups[3].Value),
                            Parse(match.Groups[4].Value), Parse(match.Groups[5].Value));
                        if (match.Groups[1].Value == "_BaseColor") m.BaseColor = c;
                        else if (match.Groups[1].Value == "_Color") m.Color = c;
                        continue;
                    }
                }

                if (m.Name.Length == 0 || m.ShaderGuid.Length == 0 || m.BaseColor.A == 0f || m.Color.A == 0f)
                    throw new InvalidOperationException(
                        $"{Path.GetFileName(path)}: sin nombre, sin shader o sin color legible " +
                        $"({m.Name.Length} de nombre, {m.ShaderGuid.Length} de guía, alphas " +
                        $"{m.BaseColor.A}/{m.Color.A})");
                return m;
            }

            private static float Parse(string s) => float.Parse(s.Trim(), CultureInfo.InvariantCulture);

            private static int FileId(string line)
            {
                var m = Regex.Match(line, @"fileID: (\d+)");
                return m.Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : -1;
            }

            private static string? GuidOrNull(string line)
            {
                var m = Regex.Match(line, @"guid: ([0-9a-f]{32})");
                return m.Success ? m.Groups[1].Value : null;
            }

            private static string TypedValue(string line)
            {
                var m = Regex.Match(line, @"guid: ([0-9a-f]{32})");
                return m.Success ? m.Groups[1].Value : "";
            }

            private static (float X, float Y) Vector(string line)
            {
                var m = Regex.Match(line, @"\{x: ([^,]+), y: ([^}]+)\}");
                if (!m.Success) throw new InvalidOperationException($"no pude leer un vector de «{line}»");
                return (Parse(m.Groups[1].Value), Parse(m.Groups[2].Value));
            }
        }

        /// <summary>
        /// Lo imprescindible de una `Texture2D` serializada: forma (lado, formato,
        /// mipmaps, repetición, filtro) y los PÍXELES del blob `_typelessdata`.
        ///
        /// El orden de los píxeles en el blob es una MEDICIÓN, no una suposición:
        /// Unity los guarda tal como se escribieron —fila por fila y la fila 0
        /// primero, x ascendente—, y así lo volvió a medir el contrato (la primera
        /// fila de la rejilla es la de la línea completa). Si el serializador
        /// cambiara de orden, esto falla en el primer píxel con las coordenadas
        /// delante.
        /// </summary>
        private sealed class TexYaml
        {
            public string Name = "";
            public int Width, Height, Format, Mips, WrapU, WrapV, Filter;
            public byte[] Pixels = Array.Empty<byte>();

            public GameMaterialModel.Rgba32 Pixel(int x, int y)
            {
                if (x < 0 || y < 0 || x >= Width || y >= Height)
                    throw new InvalidOperationException($"píxel ({x}, {y}) fuera de la textura {Width}×{Height}");
                int at = (y * Width + x) * 4;
                if (at + 4 > Pixels.Length)
                    throw new InvalidOperationException($"el blob no llega al píxel ({x}, {y}) de {Name}");
                return new GameMaterialModel.Rgba32(Pixels[at], Pixels[at + 1], Pixels[at + 2], Pixels[at + 3]);
            }

            public static TexYaml Read(string path)
            {
                string text = File.ReadAllText(path).Replace("\r", string.Empty);
                var tex = new TexYaml();
                string hex = "";
                foreach (string raw in text.Split('\n'))
                {
                    string line = raw.Trim();
                    if (line.StartsWith("_typelessdata:"))
                        hex = line.Substring("_typelessdata:".Length).Replace(" ", string.Empty);
                    else if (line.StartsWith("m_Name: ")) tex.Name = line.Substring("m_Name: ".Length).Trim();
                    else if (line.StartsWith("m_Width: ")) tex.Width = Int(line);
                    else if (line.StartsWith("m_Height: ")) tex.Height = Int(line);
                    else if (line.StartsWith("m_TextureFormat: ")) tex.Format = Int(line);
                    else if (line.StartsWith("m_MipCount: ")) tex.Mips = Int(line);
                    else if (line.StartsWith("m_WrapU: ")) tex.WrapU = Int(line);
                    else if (line.StartsWith("m_WrapV: ")) tex.WrapV = Int(line);
                    else if (line.StartsWith("m_FilterMode: ")) tex.Filter = Int(line);
                }

                if (tex.Name.Length == 0 || tex.Width <= 0 || tex.Height <= 0 || hex.Length == 0)
                    throw new InvalidOperationException(
                        $"{Path.GetFileName(path)} no parece una textura legible " +
                        $"({tex.Name.Length} de nombre, {tex.Width}×{tex.Height}, {hex.Length} dígitos de blob)");

                tex.Pixels = Convert.FromHexString(hex);
                return tex;
            }

            private static int Int(string line)
                => int.Parse(line.Substring(line.IndexOf(':') + 1).Trim(), CultureInfo.InvariantCulture);
        }
    }
}

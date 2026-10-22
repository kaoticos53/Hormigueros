using System;
using System.Collections.Generic;
using System.IO;
using AntSim.Unity.Scripts.Presenter;
using AntSim.Unity.Scripts.UI;
using Xunit;

namespace AntSim.Core.Tests
{
    /// <summary>
    /// CONTRATO DE LOS ASSETS GENERADOS: la fuente y las mallas que el repo tiene
    /// VERSIONADAS tienen que ser lo que produce el generador desde los modelos
    /// puros — la fuente byte a byte, cada malla vértice a vértice.
    ///
    /// POR QUÉ EXISTE. Los modelos (<see cref="GameFontModel"/>,
    /// <see cref="GameSpriteModel"/>, <see cref="ActorMeshModel"/>,
    /// <see cref="TtfWriter"/>) son la fuente de verdad, pero el HUD y los actores
    /// se dibujan con los ARTEFACTOS: el `.ttf` y las mallas `.asset` que el
    /// bootstrapper deja en `Assets/`. Editar el modelo y no volver a generar deja
    /// los dos lados separados, y no lo nota nadie: la suite comprueba los modelos
    /// (que están bien), el Play pass comprueba lo pintado (que se pinta con el
    /// artefacto viejo, así que «está bien»), y el desajuste sólo aparece al abrir
    /// el editor y regenerar. Esta es la comprobación que faltaba, y va aquí, en
    /// headless y sin editor, para que la vea el mismo `dotnet test` que ve lo
    /// demás.
    ///
    /// POR QUÉ SE PUEDE COMPARAR BYTE A BYTE. Porque el escritor de la fuente es
    /// determinista por diseño: sella la fecha en `head` y recalcula el checksum,
    /// así que regenerar dos veces produce el mismo fichero (el md5 repetido está
    /// comprobado en el registro de la entrega). Si eso dejara de ser cierto, este
    /// test fallaría por otro motivo y el mensaje lo dice.
    ///
    /// LA REPARACIÓN QUE PIDE UN FALLO de aquí es siempre la misma: abrir el
    /// proyecto y dejar que el bootstrapper regenere (`SceneBootstrapper`/
    /// `MultiSimBootstrapper` llaman a `GameAssetBuilder`, que sólo reescribe si el
    /// contenido cambia). El mensaje nombra el fichero y, cuando aplica, el vértice,
    /// para que se pueda leer desde el log de CI.
    /// </summary>
    public class GeneratedAssetContractTests
    {
        /// <summary>Las mallas de actor que el juego versiona y la silueta de la que
        /// sale cada una. Las variantes `_V&lt;n&gt;` las genera el multi-visor (una
        /// por vista) con la MISMA geometría; el test las DESCUBRE en disco en vez
        /// de fijar el número de vistas, así que si mañana hay seis entran solas.</summary>
        private static readonly (string Stem, GameSpriteModel.Sprite Shape)[] ActorMeshes =
        {
            ("AntMesh", GameSpriteModel.Sprite.Ant),
            ("ItemMesh", GameSpriteModel.Sprite.Load),
            ("LeafMesh", GameSpriteModel.Sprite.Leaf),
        };

        [Fact]
        public void FuenteVersionada_EsLaQueProduceElGenerador()
        {
            string root = RepoRoot();
            string path = Path.Combine(root, "src", "App", "AntSim.Unity", "Assets", "Fonts", "AntSim.ttf");
            Assert.True(File.Exists(path),
                $"falta la fuente versionada {path}: la genera el bootstrapper " +
                "(GameAssetBuilder.EnsureFont) al abrir el proyecto.");

            byte[] generado = TtfWriter.BuildFromModel("AntSim");
            byte[] versionado = File.ReadAllBytes(path);

            int dif = FirstDifference(generado, versionado);
            Assert.True(dif < 0,
                $"el .ttf versionado NO es el del modelo: {versionado.Length} B en disco frente a " +
                $"{generado.Length} B del generador" +
                (dif >= 0 ? $" (primer byte distinto: {dif})" : "") +
                ". Editar GameFontModel (glifos, avances, métrica) y no regenerar deja el HUD " +
                "pintando la fuente vieja: abre el proyecto y deja que el bootstrapper la reescriba.");
        }

        [Fact]
        public void MallasVersionadas_SonLaGeometriaDelModelo()
        {
            string materials = Path.Combine(RepoRoot(), "src", "App", "AntSim.Unity", "Assets", "Materials");
            Assert.True(Directory.Exists(materials), "no existe " + materials);

            int revisadas = 0, variantes = 0;
            foreach (var (stem, shape) in ActorMeshes)
            {
                var data = ActorMeshModel.Build(GameSpriteModel.Build(shape));
                string[] files = Directory.GetFiles(materials, stem + "*.asset");
                bool basePresente = false;
                foreach (string file in files)
                {
                    string name = Path.GetFileName(file);
                    if (name == stem + ".asset") basePresente = true;
                    else variantes++;
                    revisadas++;
                    CompararMallaConModelo(file, data);
                }
                Assert.True(basePresente,
                    $"falta la malla base {stem}.asset en {materials}: la genera el bootstrapper " +
                    "(GameAssetBuilder.EnsureActorMesh) al abrir el proyecto.");
            }

            Assert.True(revisadas >= ActorMeshes.Length, "no se encontró ninguna malla de actor versionada");
            // Las del multi-visor: si el montaje dejara de generarlas, el test se
            // daría cuenta aquí en vez de dejar de comprobarlas en silencio.
            Assert.True(variantes > 0,
                "no hay ninguna variante _V* (mallas del multi-visor): ¿se dejó de generar?");
        }

        private static void CompararMallaConModelo(string file, ActorMeshModel.Data data)
        {
            string name = Path.GetFileName(file);
            MeshYaml mesh = MeshYaml.Read(file);

            Assert.True(mesh.VertexCount == data.VertexCount,
                $"{name}: el asset tiene {mesh.VertexCount} vértices y el modelo {data.VertexCount}. " +
                "Editar GameSpriteModel/ActorMeshModel y no regenerar deja los actores con la silueta vieja.");
            Assert.True(mesh.IndexCount == data.Triangles.Length,
                $"{name}: el asset indexa {mesh.IndexCount} índices y el modelo {data.Triangles.Length}.");
            Assert.True(mesh.Positions.Length >= data.VertexCount * 3,
                $"{name}: el asset trae {mesh.Positions.Length / 3} posiciones decodificadas y el modelo " +
                $"{data.VertexCount} vértices.");

            for (int v = 0; v < data.VertexCount; v++)
            {
                for (int k = 0; k < 3; k++)
                {
                    float esperado = data.Vertices[v * 3 + k];
                    float real = mesh.Positions[v * 3 + k];
                    Assert.True(Math.Abs(esperado - real) <= 1e-5f,
                        $"{name}: el vértice {v} ({("xyz")[k]}) del asset es {real} y el modelo dice " +
                        $"{esperado}. Regenera las mallas del bootstrapper (GameAssetBuilder.EnsureActorMesh).");
                }
            }
        }

        private static int FirstDifference(byte[] a, byte[] b)
        {
            int n = Math.Min(a.Length, b.Length);
            for (int i = 0; i < n; i++) if (a[i] != b[i]) return i;
            return a.Length == b.Length ? -1 : n;
        }

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AntSim.slnx")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return dir!.FullName;
        }

        /// <summary>
        /// Lo imprescindible de una malla serializada en YAML (texto, que es como
        /// el repo tiene las suyas): cuenta de vértices e índices y las POSICIONES,
        /// decodificadas del blob `_typelessdata` con el desfase que declara el
        /// primer canal de `m_Channels`.
        ///
        /// El paso entre vértices se DEDUCE del propio blob (bytes / vértices) en
        /// vez de escribirse a mano: así este lector no hay que mantenerlo si Unity
        /// cambia el orden o el número de atributos, y si el blob no cuadra con la
        /// cuenta de vértices lo dice en vez de leer basura.
        /// </summary>
        private sealed class MeshYaml
        {
            public int VertexCount { get; private set; }
            public int IndexCount { get; private set; }
            public float[] Positions { get; private set; } = Array.Empty<float>();

            public static MeshYaml Read(string path)
            {
                string text = File.ReadAllText(path);
                if (!text.Contains("--- !u!43"))
                    throw new InvalidOperationException(
                        $"{Path.GetFileName(path)} no parece una malla en YAML " +
                        "(¿serialización binaria? ¿el asset está roto?)");

                var mesh = new MeshYaml();
                var channels = new List<(int Offset, int Format, int Dimension)>();
                string hex = string.Empty;
                bool enCanales = false;
                (int Offset, int Format, int Dimension) actual = (-1, -1, 0);
                bool enCanal = false;

                foreach (string rawLine in text.Replace("\r", string.Empty).Split('\n'))
                {
                    string line = rawLine.Trim();
                    if (line.StartsWith("m_VertexCount:", StringComparison.Ordinal))
                        mesh.VertexCount = Num(line);
                    else if (line.StartsWith("m_Channels:", StringComparison.Ordinal))
                        enCanales = true;
                    else if (line.StartsWith("_typelessdata:", StringComparison.Ordinal))
                        hex = line.Substring(line.IndexOf(':') + 1).Replace(" ", string.Empty);
                    else if (line.StartsWith("indexCount:", StringComparison.Ordinal) && mesh.IndexCount == 0)
                        mesh.IndexCount = Num(line);
                    else if (enCanales && line.StartsWith("- stream:", StringComparison.Ordinal))
                    {
                        if (enCanal) channels.Add(actual);
                        actual = (0, 0, 0);
                        enCanal = true;
                    }
                    else if (enCanales && enCanal && line.StartsWith("offset:", StringComparison.Ordinal))
                        actual.Offset = Num(line);
                    else if (enCanales && enCanal && line.StartsWith("format:", StringComparison.Ordinal))
                        actual.Format = Num(line);
                    else if (enCanales && enCanal && line.StartsWith("dimension:", StringComparison.Ordinal))
                        actual.Dimension = Num(line);
                    else if (enCanales && line.StartsWith("m_DataSize:", StringComparison.Ordinal))
                        enCanales = false;   // fin del bloque de canales
                    else if (enCanales && line.Length > 0 && !line.StartsWith("-") && !line.Contains(":"))
                        break;
                }
                if (enCanal && enCanales) channels.Add(actual);

                if (mesh.VertexCount <= 0 || hex.Length == 0 || channels.Count == 0)
                    throw new InvalidOperationException(
                        $"{Path.GetFileName(path)}: sin vértices, sin _typelessdata o sin m_Channels " +
                        $"({mesh.VertexCount} vértices, {hex.Length} dígitos hex, {channels.Count} canales)");

                var posicion = channels[0];
                if (posicion.Format != 0 || posicion.Dimension != 3)
                    throw new InvalidOperationException(
                        $"{Path.GetFileName(path)}: el primer canal no es posición float32×3 " +
                        $"(format {posicion.Format}, dimension {posicion.Dimension})");

                byte[] blob = Convert.FromHexString(hex);
                if (blob.Length % mesh.VertexCount != 0)
                    throw new InvalidOperationException(
                        $"{Path.GetFileName(path)}: el blob tiene {blob.Length} bytes y hay " +
                        $"{mesh.VertexCount} vértices: no hay paso constante.");

                int paso = blob.Length / mesh.VertexCount;
                var pos = new float[mesh.VertexCount * 3];
                for (int v = 0; v < mesh.VertexCount; v++)
                    for (int k = 0; k < 3; k++)
                        pos[v * 3 + k] = FloatAt(blob, v * paso + posicion.Offset + k * 4);
                mesh.Positions = pos;
                return mesh;
            }

            private static int Num(string line)
                => int.Parse(line.Substring(line.IndexOf(':') + 1).Trim());

            /// <summary>El blob de Unity viene en little-endian; en una máquina
            /// big-endian hay que darle la vuelta para leerlo igual.</summary>
            private static float FloatAt(byte[] blob, int at)
            {
                if (at + 4 > blob.Length)
                    throw new InvalidOperationException($"lectura fuera del blob en {at}");
                if (BitConverter.IsLittleEndian) return BitConverter.ToSingle(blob, at);
                var tmp = new[] { blob[at + 3], blob[at + 2], blob[at + 1], blob[at] };
                return BitConverter.ToSingle(tmp, 0);
            }
        }
    }
}

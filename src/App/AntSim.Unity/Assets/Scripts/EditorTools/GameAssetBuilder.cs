#if UNITY_EDITOR
using System;
using System.IO;
using AntSim.Unity.Scripts.Presenter;
using AntSim.Unity.Scripts.UI;
using UnityEditor;
using UnityEngine;

namespace AntSim.Unity.Scripts.EditorTools
{
    /// <summary>
    /// GENERA LOS ASSETS DE LA IDENTIDAD DEL JUEGO: la tipografía propia y las
    /// siluetas de los actores (hormiga, carga, hoja, huevo).
    ///
    /// El dato del que salen está en los modelos PUROS
    /// (<see cref="GameFontModel"/>, <see cref="TtfWriter"/>,
    /// <see cref="GameSpriteModel"/>), que la suite headless comprueba sin
    /// abrir Unity: aquí solo queda el trámite de editor —escribir el fichero,
    /// importarlo, montar el <c>Mesh</c>— y la IDEMPOTENCIA, que es lo que
    /// mantiene el repo limpio:
    ///
    ///   · el .ttf se reescribe SOLO si los bytes cambian (el escritor no sella
    ///     fechas, así que regenerar produce exactamente el mismo fichero);
    ///   · una malla que ya existe y cuya geometría coincide con el modelo se
    ///     deja como está, y si difiere se actualiza con <c>CopySerialized</c>
    ///     para NO cambiarle el GUID — un GUID nuevo rompería en silencio las
    ///     referencias de las escenas ya guardadas (el fallo clásico del
    ///     generador de assets: se regenera el material y la escena que lo
    ///     apuntaba se queda sin él).
    /// </summary>
    public static class GameAssetBuilder
    {
        public const string FontFolder = "Assets/Fonts";
        public const string FontPath = FontFolder + "/AntSim.ttf";
        public const string FontFamily = "AntSim";
        private const string MaterialsFolder = "Assets/Materials";

        /// <summary>La fuente del juego, importada y lista para el HUD. La crea
        /// si falta; si existe y no cambió, devuelve la de siempre sin tocar
        /// nada.</summary>
        public static Font EnsureFont()
        {
            byte[] bytes = TtfWriter.BuildFromModel(FontFamily);
            string full = ProjectPath(FontPath);
            bool upToDate = File.Exists(full) && SameBytes(File.ReadAllBytes(full), bytes);
            if (!upToDate)
            {
                EnsureFolder(FontFolder);
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                File.WriteAllBytes(full, bytes);
                AssetDatabase.ImportAsset(FontPath,
                    ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            }
            // El IMPORTADOR se revisa SIEMPRE, no sólo al reescribir el .ttf: sus
            // ajustes son parte del contrato de la fuente, y si se pierden el
            // fichero sigue idéntico y el HUD cambia de métrica en silencio.
            // `ConfigureFontImporter` es idempotente (si todo está en su sitio no
            // reimporta), así que mirarlo cada vez no ensucia el repo.
            ConfigureFontImporter();

            var font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
            if (font == null)
            {
                // No es un fallo recuperable: sin esto el HUD saldría con la
                // fuente de relleno y el cambio de aspecto no se vería.
                throw new InvalidOperationException(
                    $"[AntSim] no se pudo importar la fuente propia en {FontPath}. " +
                    "Revisa la consola de Unity: el .ttf no compiló como fuente.");
            }
            return font;
        }

        /// <summary>
        /// La malla de una silueta, con el nombre de asset pedido. Presente y
        /// con la geometría al día ⇒ se devuelve tal cual.
        /// </summary>
        public static Mesh EnsureActorMesh(GameSpriteModel.Sprite sprite, string name)
        {
            var data = ActorMeshModel.Build(GameSpriteModel.Build(sprite));
            string path = $"{MaterialsFolder}/{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null && SameGeometry(existing, data)) return existing;

            var mesh = new Mesh { name = name };
            var verts = new Vector3[data.VertexCount];
            for (int i = 0; i < verts.Length; i++)
                verts[i] = new Vector3(data.Vertices[i * 3], data.Vertices[i * 3 + 1],
                    data.Vertices[i * 3 + 2]);
            mesh.vertices = verts;
            mesh.triangles = data.Triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            EnsureMaterialsFolder();
            if (existing != null)
            {
                // En su sitio: misma referencia, mismo GUID, geometría nueva.
                EditorUtility.CopySerialized(mesh, existing);
                UnityEngine.Object.DestroyImmediate(mesh);
                EditorUtility.SetDirty(existing);
                AssetDatabase.SaveAssetIfDirty(existing);
                return existing;
            }
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        // ── font importer ──────────────────────────────────────────────────
        private static void ConfigureFontImporter()
        {
            if (AssetImporter.GetAtPath(FontPath) is not TrueTypeFontImporter importer) return;
            // Si todo está en su sitio NO se reimporta: `SaveAndReimport` en cada
            // pasada reescribiría el asset y ensuciaría el repo. Misma
            // idempotencia que el .ttf (por bytes) y las mallas (por geometría).
            if (importer.fontTextureCase == FontTextureCase.Dynamic
                && importer.fontRenderingMode == FontRenderingMode.Smooth
                && importer.includeFontData
                && importer.fontSize == 16
                && importer.characterSpacing == 0
                && importer.shouldRoundAdvanceValue) return;
            // DINÁMICA: uGUI rasteriza cada tamaño que el HUD pide (13…24 px). Es
            // la razón de generar un TTF vectorial en vez de un atlas de mapa de
            // bits, que saldría borroso en todos los tamaños menos uno.
            importer.fontTextureCase = FontTextureCase.Dynamic;
            importer.fontRenderingMode = FontRenderingMode.Smooth;
            importer.includeFontData = true;   // el dato viaja al build (no se busca en el SO)
            importer.fontSize = 16;
            importer.characterSpacing = 0;
            // El redondeo de avance del importador se deja en su valor por
            // defecto, y conviene saber por qué: NO es lo que gobierna el avance
            // que reserva uGUI. Medido con la fuente cargada por el motor
            // (`FontEngine`) y con el `TextGenerator`, el ajuste da EXACTAMENTE
            // los mismos avances en los dos estados, porque el redondeo que se ve
            // lo hace el HINTING de FreeType al cargar el glifo, no esta opción.
            //
            // Los números de la medida, porque esta es la duda que vuelve —«la
            // fuente declara 720 y el HUD reserva otra cosa»—: sin hinting
            // (`LOAD_NO_HINTING`) el icono da 719,76-719,87 unidades a TODOS los
            // tamaños (0,72 em) y una letra 500,00 clavadas; con hinting —lo que
            // usa uGUI— el avance cae al píxel entero: 714 unidades a 14 px y a
            // 28 px, 733 a 15, 724 a 29, 710 a 31, y una LETRA pasa de 500 a 533
            // unidades a 15 px. O sea: el desajuste es real pero es del tamaño,
            // no de la fuente.
            //
            // Y el número 643 que se atribuye al icono es su CAJA DE TINTA
            // (0,643 em: 9 px a 14 px, 18 px a 28 px), no un avance: el avance es
            // 720 y la tinta cabe dentro.
            importer.shouldRoundAdvanceValue = true;
            importer.SaveAndReimport();
        }

        // ── utilidades ─────────────────────────────────────────────────────
        private static string ProjectPath(string assetPath)
            => Path.Combine(Path.GetDirectoryName(Application.dataPath)!, assetPath);

        private static bool SameBytes(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        private static bool SameGeometry(Mesh mesh, ActorMeshModel.Data data)
        {
            var verts = mesh.vertices;
            if (verts.Length != data.VertexCount) return false;
            for (int i = 0; i < verts.Length; i++)
            {
                if (Mathf.Abs(verts[i].x - data.Vertices[i * 3]) > 1e-4f) return false;
                if (Mathf.Abs(verts[i].y - data.Vertices[i * 3 + 1]) > 1e-4f) return false;
                if (Mathf.Abs(verts[i].z - data.Vertices[i * 3 + 2]) > 1e-4f) return false;
            }
            var tris = mesh.triangles;
            if (tris.Length != data.Triangles.Length) return false;
            for (int i = 0; i < tris.Length; i++) if (tris[i] != data.Triangles[i]) return false;
            return true;
        }

        private static void EnsureFolder(string folder)
        {
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
        }

        private static void EnsureMaterialsFolder() => EnsureFolder(MaterialsFolder);
    }
}
#endif

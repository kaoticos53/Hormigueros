using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace AntSim.Core.Tests
{
    /// <summary>
    /// F5.1bis — GUARDA de los materiales de feromonas commiteados (sin editor).
    ///
    /// QUÉ VIGILA. El quad de feromonas es un material TRANSPARENTE cuyo color lo
    /// pone en runtime la RenderTexture del canal E. Mientras no llega ningún frame
    /// de feromonas —escena recién abierta, canal E apagado, primer tick— lo que se
    /// ve es SU textura de reposo, y ahí está el defecto que esta guarda impide:
    ///
    ///   · `_BaseMap` con `{fileID: 0}` = sin textura. El shader muestrea el blanco
    ///     por defecto, y como el material es transparente con `_BaseColor` blanco y
    ///     alpha 1, el quad se pinta BLANCO OPACO y tapa suelo y hormigas. Es el
    ///     defecto del Play pass de F5.1 («el terreno es blanco»), y el sintoma con
    ///     el que el 17-09 apareció el material sin su textura de reposo tras una
    ///     pasada del generador (referencia muerta por delete+create, ver
    ///     `docs/fase5-3-escala.md` §4.7).
    ///   · una guía que no existe en el proyecto = puntero muerto: igual de malo y
    ///     más difícil de ver, porque el YAML parece tener textura.
    ///
    /// POR QUÉ UN TEST Y NO UNA NOTA. Los materiales son artefactos del
    /// bootstrapper: se regeneran, se restauran a HEAD para no ensuciar el árbol…
    /// y es exactamente ahí donde una referencia se pierde sin que nadie mire. Esto
    /// se comprueba en milisegundos y sin abrir Unity.
    /// </summary>
    public class PheromoneMaterialGuardTests
    {
        private const string MaterialsDir = "src/App/AntSim.Unity/Assets/Materials";

        /// <summary>Textura de reposo: 1×1 blanca con alpha 0 = invisible. Es la
        /// lección del Play pass (unlit-transparente sin textura es blanco OPACO).</summary>
        private const string EmptyPixels = "ffffff00";

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AntSim.slnx")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return dir!.FullName;
        }

        private static string[] PheromoneMaterials()
        {
            string dir = Path.Combine(RepoRoot(), MaterialsDir);
            Assert.True(Directory.Exists(dir), $"no existe el directorio de materiales: {dir}");
            var files = Directory.GetFiles(dir, "Pheromone*.mat");
            Assert.NotEmpty(files);   // si desaparecen, esta guarda se está apagando sola
            Array.Sort(files, StringComparer.Ordinal);
            return files;
        }

        /// <summary>Guía de la textura en el slot pedido, o null si no la tiene.</summary>
        private static string? TextureGuid(string yaml, string slot)
        {
            var m = Regex.Match(yaml, "- " + slot + @":\s*\r?\n\s+m_Texture: \{fileID: \d+(?:, guid: ([0-9a-f]{32}))?");
            if (!m.Success) return null;
            return m.Groups[1].Success ? m.Groups[1].Value : null;
        }

        private static string GuidOwner(string guid, IEnumerable<string> metas)
        {
            foreach (string meta in metas)
                if (File.ReadAllText(meta).Contains("guid: " + guid)) return meta;
            return "";
        }

        [Fact]
        public void CadaMaterialDeFeromonas_TieneSuTexturaDeReposo()
        {
            foreach (string path in PheromoneMaterials())
            {
                string yaml = File.ReadAllText(path);
                string name = Path.GetFileName(path);

                // Es transparente: si no lo fuera, la guarda de abajo no significaría
                // lo que dice (un opaco con textura no es el defecto que se vigila).
                Assert.Contains("_SURFACE_TYPE_TRANSPARENT", yaml);

                foreach (string slot in new[] { "_BaseMap", "_MainTex" })
                {
                    string? guid = TextureGuid(yaml, slot);
                    Assert.True(guid != null,
                        $"{name}: {slot} sin textura ({slot} con fileID 0 = quad BLANCO OPACO " +
                        "tapando el tablero en reposo). Lo escribe el bootstrapper: regenera la escena.");
                }
            }
        }

        [Fact]
        public void LaTexturaDeReposo_ExisteYEsInvisible()
        {
            string root = RepoRoot();
            string materials = Path.Combine(root, MaterialsDir);
            var metas = Directory.GetFiles(root, "*.meta", SearchOption.AllDirectories);

            foreach (string path in PheromoneMaterials())
            {
                string yaml = File.ReadAllText(path);
                string name = Path.GetFileName(path);
                string? guid = TextureGuid(yaml, "_BaseMap");
                if (guid == null) continue;   // el otro test lo reporta

                string owner = GuidOwner(guid, metas);
                Assert.True(owner.Length > 0, $"{name}: la guía {guid} de _BaseMap no existe (puntero muerto)");

                // La textura de reposo tiene que ser 1×1, RGBA32 (formato 4) y blanca
                // con alpha 0: invisible. Alpha 255 convertiría el reposo en un quad
                // blanco opaco — el mismo defecto por otra puerta.
                string asset = Path.ChangeExtension(owner, null)!;
                string tex = File.ReadAllText(asset);
                Assert.Contains("m_Width: 1", tex);
                Assert.Contains("m_Height: 1", tex);
                Assert.Contains("m_TextureFormat: 4", tex);
                Assert.Contains("_typelessdata: " + EmptyPixels, tex);
            }
        }
    }
}

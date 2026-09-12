using System;
using System.IO;
using AntSim.Unity.Scripts.Streaming;
using Xunit;

namespace AntSim.Core.Tests
{
    /// <summary>
    /// Play-pass F5.1 — resolución de rutas relativas al repo root: el cwd del
    /// editor es la carpeta del proyecto, NO la del repo, así que build/ y
    /// artifacts/ no resuelven sin este ancla. Puro, sin UnityEngine.
    /// </summary>
    public class RepoPathResolverTests : IDisposable
    {
        private readonly string _tmp;
        private readonly string _repo;   // "repo" simulado
        private readonly string _proj;   // "repo/src/App/AntSim.Unity"

        public RepoPathResolverTests()
        {
            _tmp = Path.Combine(Path.GetTempPath(), "antsim-repopath-" + Guid.NewGuid().ToString("N")[..8]);
            _repo = Path.Combine(_tmp, "repo");
            _proj = Path.Combine(_repo, "src", "App", "AntSim.Unity");
            Directory.CreateDirectory(_proj);
            Directory.CreateDirectory(Path.Combine(_repo, "build", "antsim"));
            Directory.CreateDirectory(Path.Combine(_repo, "artifacts"));
        }

        public void Dispose()
        {
            try { Directory.Delete(_tmp, recursive: true); } catch { }
        }

        [Fact]
        public void RepoRootFromProjectPath_DataPath_SubeTresNiveles()
        {
            // Application.dataPath = <proj>/Assets
            string dataPath = Path.Combine(_proj, "Assets");
            Assert.Equal(_repo, RepoPathResolver.RepoRootFromProjectPath(dataPath));
        }

        [Fact]
        public void RepoRootFromProjectPath_ProjectPathDirecto_TambienFunciona()
        {
            Assert.Equal(_repo, RepoPathResolver.RepoRootFromProjectPath(_proj));
        }

        [Fact]
        public void RepoRootFromProjectPath_LayoutDesconocido_Null()
        {
            Assert.Null(RepoPathResolver.RepoRootFromProjectPath(
                Path.Combine(_tmp, "otro-lugar", "Assets")));
            Assert.Null(RepoPathResolver.RepoRootFromProjectPath(null));
            Assert.Null(RepoPathResolver.RepoRootFromProjectPath(""));
        }

        [Fact]
        public void Resolve_Absoluta_PasaIntacta()
        {
            string abs = Path.Combine(_repo, "build", "antsim", "antsim");
            Assert.Equal(abs, RepoPathResolver.Resolve(abs, baseDir: null, repoRoot: _repo));
        }

        [Fact]
        public void Resolve_Relativa_CwdPrimero_RepoSegundo()
        {
            // Existe SOLO en el repo → resuelve contra el repo root aunque el
            // cwd (baseDir) sea la carpeta del proyecto.
            string rel = Path.Combine("artifacts", "pool.antgenome");
            File.WriteAllText(Path.Combine(_repo, "artifacts", "pool.antgenome"), "x");
            string r = RepoPathResolver.Resolve(rel, baseDir: _proj, repoRoot: _repo);
            Assert.Equal(Path.Combine(_repo, "artifacts", "pool.antgenome"), r);

            // Existe en AMBOS → gana el cwd (permite overrides locales).
            File.WriteAllText(Path.Combine(_proj, "pool.antgenome"), "local");
            r = RepoPathResolver.Resolve("pool.antgenome", baseDir: _proj, repoRoot: _repo);
            Assert.Equal(Path.Combine(_proj, "pool.antgenome"), r);
        }

        [Fact]
        public void Resolve_NoExisteEnNingunSitio_RutaCanonicaDelRepo()
        {
            string r = RepoPathResolver.Resolve("artifacts/falta.antgenome", baseDir: _proj, repoRoot: _repo);
            Assert.Equal(Path.Combine(_repo, "artifacts", "falta.antgenome"), r);
        }

        [Fact]
        public void Resolve_SinRepoRoot_ComoSiempre()
        {
            File.WriteAllText(Path.Combine(_proj, "local.bin"), "x");
            Assert.Equal(Path.Combine(_proj, "local.bin"),
                RepoPathResolver.Resolve("local.bin", baseDir: _proj, repoRoot: null));
        }

        [Fact]
        public void ResolveExecutable_SufijoExe_EnWindows()
        {
            string rel = Path.Combine("build", "antsim");
            File.WriteAllText(Path.Combine(_repo, "build", "antsim", "antsim.exe"), "x");
            string r = RepoPathResolver.ResolveExecutable(rel, baseDir: _proj, repoRoot: _repo);
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                    System.Runtime.InteropServices.OSPlatform.Windows))
                Assert.Equal(Path.Combine(_repo, "build", "antsim", "antsim.exe"), r);
            else
                Assert.Equal(Path.Combine(_repo, "build", "antsim", "antsim"), r);
        }
    }
}

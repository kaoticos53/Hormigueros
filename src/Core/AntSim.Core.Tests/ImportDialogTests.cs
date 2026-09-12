using AntSim.Core.Brain;
using AntSim.Core.Scenario;
using AntSim.Unity.Scripts.Streaming;
using Xunit;

namespace AntSim.Core.Tests
{
    /// <summary>
    /// F4.3 — diálogo de importación con cuarentena (diseño UX §2.1). El
    /// oráculo (<c>GenomeImportInfo</c> en Core, expuesto como
    /// <c>--mode genome-info</c>) produce la tarjeta canónica; el modelo puro
    /// de Unity la parsea y gestiona el flujo inspección→confirmar→sembrar.
    /// Cancelar no toca nada; confirmar entrega la ruta para --seed-pool.
    /// </summary>
    public sealed class ImportDialogTests
    {
        private const string PoolPath = "artifacts/pretrain-warm-v2.antgenome";

        [Fact]
        public void Oraculo_InspeccionaPoolReal()
        {
            var info = GenomeImportInfo.Inspect(
                TestPaths.RepoPath(PoolPath), BrainContract.CurrentVersion);

            Assert.True(info.Ok);
            Assert.Equal("pretrain-warm-v2.antgenome", info.FileName);
            Assert.Equal(24, info.GenomeCount);
            Assert.Equal(64, info.Sha256.Length);
            Assert.False(string.IsNullOrEmpty(info.Card));
            Assert.Contains("cuarentena", info.QuarantineNote);
            // La tarjeta canónica lleva sha corto y fitness del pool.
            Assert.Contains(info.Sha256.Substring(0, 8), info.Card);
        }

        [Fact]
        public void Oraculo_Fallos_SinLanzar()
        {
            var missing = GenomeImportInfo.Inspect(
                TestPaths.RepoPath("artifacts/no-existe.antgenome"), BrainContract.CurrentVersion);
            Assert.False(missing.Ok);
            Assert.Contains("no encontrado", missing.Error);

            var corrupt = GenomeImportInfo.Inspect(
                TestPaths.RepoPath("README.md"), BrainContract.CurrentVersion);
            Assert.False(corrupt.Ok);
            Assert.Contains("formato inválido", corrupt.Error);
        }

        [Fact]
        public void FlujoCompleto_Inspeccionar_Confirmar_Cancelar()
        {
            var info = GenomeImportInfo.Inspect(
                TestPaths.RepoPath(PoolPath), BrainContract.CurrentVersion);
            var model = new ImportDialogModel();

            // Confirmar antes de revisar: nada.
            Assert.Null(model.Confirm());

            // Inspección ok → fase Reviewing con tarjeta + reglas.
            string? card = model.Inspect(PoolPath, info.ToJson());
            Assert.NotNull(card);
            Assert.Equal(ImportDialogModel.Phase.Reviewing, model.CurrentPhase);
            Assert.Contains("24 genomas", model.RenderDialog());
            Assert.Contains("cuarentena", model.RenderDialog());

            // Cancelar: no toca nada y permite re-inspeccionar.
            model.Cancel();
            Assert.Equal(ImportDialogModel.Phase.Closed, model.CurrentPhase);
            Assert.Null(model.RenderDialog());

            // Inspección → confirmación entrega la ruta para --seed-pool.
            model.Inspect(PoolPath, info.ToJson());
            Assert.Equal(PoolPath, model.Confirm());
            Assert.Equal(ImportDialogModel.Phase.Confirmed, model.CurrentPhase);
        }

        [Fact]
        public void Parser_RechazaRespuestasRotas()
        {
            var model = new ImportDialogModel();

            // JSON que no es del oráculo.
            Assert.Null(model.Inspect("x", "no soy json"));
            Assert.Equal(ImportDialogModel.Phase.Closed, model.CurrentPhase);
            Assert.NotNull(model.Error);

            // Fallo del oráculo (archivo inexistente): error visible, sin fase.
            var missing = GenomeImportInfo.Inspect(
                TestPaths.RepoPath("artifacts/no-existe.antgenome"), BrainContract.CurrentVersion);
            Assert.Null(model.Inspect("artifacts/no-existe.antgenome", missing.ToJson()));
            Assert.Equal(ImportDialogModel.Phase.Closed, model.CurrentPhase);
            Assert.Contains("no encontrado", model.Error);
        }

        [Fact]
        public void Integracion_OraculoReal_Determinista()
        {
            // El sha de la tarjeta es estable entre llamadas (mismo archivo).
            string json1 = GenomeImportInfo.Inspect(
                TestPaths.RepoPath(PoolPath), BrainContract.CurrentVersion).ToJson();
            string json2 = GenomeImportInfo.Inspect(
                TestPaths.RepoPath(PoolPath), BrainContract.CurrentVersion).ToJson();
            Assert.Equal(json1, json2);
        }
    }
}

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
        // Fixture TRACKEADO, no el artefacto de ejecución: en un clon limpio
        // artifacts/pretrain-warm-v2.antgenome no existe (ver TestPaths.WarmV2Pool).
        private const string PoolPath = TestPaths.WarmV2Pool;

        [Fact]
        public void Oraculo_InspeccionaPoolReal()
        {
            var info = GenomeImportInfo.Inspect(
                TestPaths.RepoPath(PoolPath), BrainContract.CurrentVersion);

            Assert.True(info.Ok);
            Assert.Equal("warm-v2.antgenome", info.FileName);
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
        public void Modal_SeAbreSinGenoma_YNoConfirmaNada()
        {
            // F5.1: abrir el modal sin haber inspeccionado nada. Es la regla que la
            // UI consulta para enseñar el panel (antes los botones del modal se
            // pintaban SIEMPRE, flotando sobre el mundo, y el diálogo no tenía
            // manera de abrirse: no había dónde escribir la ruta).
            var model = new ImportDialogModel();
            Assert.False(model.ModalVisible);            // arranca cerrado

            model.Open();
            Assert.True(model.ModalVisible);
            Assert.Equal(ImportDialogModel.Phase.Reviewing, model.CurrentPhase);
            Assert.Contains("Inspeccionar", model.RenderDialog());  // guía del paso que falta
            Assert.Null(model.Confirm());                // sin tarjeta no hay nada que confirmar

            model.Close();
            Assert.False(model.ModalVisible);
            Assert.Null(model.RenderDialog());

            // Inspeccionar y reabrir: la cuarentena se enseña.
            var info = GenomeImportInfo.Inspect(
                TestPaths.RepoPath(PoolPath), BrainContract.CurrentVersion);
            model.Inspect(PoolPath, info.ToJson());
            model.Open();
            Assert.Contains("cuarentena", model.RenderDialog());

            // Cerrar DESCARTA la revisión (cancelar es 0 riesgo, contrato §2.1):
            // reabrir vuelve a pedir la ruta en vez de arrastrar una tarjeta vieja.
            model.Close();
            model.Open();
            Assert.Contains("Inspeccionar", model.RenderDialog());
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

using AntSim.Core.Scenario;
using AntSim.Unity.Scripts.Streaming;
using Xunit;

namespace AntSim.Core.Tests
{
    /// <summary>
    /// F4.4 «Intervenir» — el plan de DropFood del jugador (Fase 4, contrato
    /// HUD §4). Modelo PURO (compilado desde el esqueleto Unity): acumula
    /// marcas click-to-place, valida cuota/limites/causalidad y produce los
    /// argumentos CLI exactos. La inyección real la ejecuta el oráculo (CLI
    /// <c>--drop tick:x:y</c>) — se verifica con una partida real.
    /// </summary>
    public sealed class DropFoodPlanTests
    {
        [Fact]
        public void Cuota_Y_Validacion_De_Posicion()
        {
            var plan = new DropFoodPlanModel();
            plan.ObserveTick(100);

            // Fuera del mundo: rechazado con motivo.
            Assert.NotNull(plan.TryPlace(700, x: -1f, y: 50f, grid: 96));
            Assert.NotNull(plan.TryPlace(700, x: 50f, y: 96f, grid: 96));

            // Pasado/presente: la causalidad F4.0 exige tick futuro.
            Assert.NotNull(plan.TryPlace(100, 50f, 50f, 96));
            Assert.Empty(plan.Marks);

            // Cuota: MaxDrops aceptados, el siguiente rechazado.
            for (int i = 0; i < DropFoodPlanModel.MaxDrops; i++)
                Assert.Null(plan.TryPlace(700 + (ulong)i, 30f + i, 40f, 96));
            Assert.NotNull(plan.TryPlace(9999, 50f, 50f, 96));
            Assert.Equal(DropFoodPlanModel.MaxDrops, plan.Marks.Count);
        }

        [Fact]
        public void Undo_Y_Clear()
        {
            var plan = new DropFoodPlanModel();
            plan.ObserveTick(50);
            Assert.Null(plan.TryPlace(650, 10f, 20f, 96));
            Assert.Null(plan.TryPlace(660, 15f, 25f, 96));
            Assert.Equal(2, plan.Marks.Count);

            Assert.True(plan.Undo());
            Assert.Single(plan.Marks);
            Assert.True(plan.Undo());
            Assert.False(plan.Undo()); // ya vacío

            Assert.Null(plan.TryPlace(650, 10f, 20f, 96));
            plan.Clear();
            Assert.Empty(plan.Marks);
        }

        [Fact]
        public void Args_CLI_Cultura_Invariante()
        {
            var plan = new DropFoodPlanModel();
            plan.ObserveTick(0);
            Assert.Null(plan.TryPlace(300, 200.5123f, 240.2499f, 256));
            Assert.Null(plan.TryPlace(450, 12f, 13f, 256));

            var args = plan.BuildCliArgs();
            Assert.Equal(4, args.Count);
            Assert.Equal("--drop", args[0]);
            Assert.Equal("300:200.51:240.25", args[1]);
            Assert.Equal("--drop", args[2]);
            Assert.Equal("450:12:13", args[3]);

            // El resumen del HUD siempre muestra la cuota.
            Assert.Contains("2/5", plan.RenderSummary());
            var vacio = new DropFoodPlanModel().RenderSummary();
            Assert.Contains("0/5", vacio);
        }

        [Fact]
        public void Integracion_DropReal_CambiaElMundo()
        {
            // La verificación de fondo: con la MISMA semilla, un --drop cambia
            // el mundo (el comando se ejecuta y el hash final difiere).
            string hA = GameScenario.Run(42, ticks: 800, colonies: 1, grid: 96,
                frameEvery: 30, seedPoolPath: null, drops: null);
            var drops = new System.Collections.Generic.List<(int, float, float)> { (400, 60f, 60f) };
            string hB = GameScenario.Run(42, ticks: 800, colonies: 1, grid: 96,
                frameEvery: 30, seedPoolPath: null, drops: drops);

            Assert.NotEqual(hA, hB);
        }

        [Fact]
        public void Integracion_MismoDrop_MismoMundo()
        {
            // Determinismo F4.0 de la intervención: mismos comandos ⇒ bit a bit.
            var drops = new System.Collections.Generic.List<(int, float, float)> { (400, 60f, 60f) };
            string hA = GameScenario.Run(42, ticks: 800, colonies: 1, grid: 96,
                frameEvery: 30, seedPoolPath: null, drops: drops);
            string hB = GameScenario.Run(42, ticks: 800, colonies: 1, grid: 96,
                frameEvery: 30, seedPoolPath: null, drops: drops);
            Assert.Equal(hA, hB);
        }
    }
}

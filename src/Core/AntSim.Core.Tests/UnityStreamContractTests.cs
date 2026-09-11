using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AntSim.Core.Scenario;
using AntSim.Core.World;
using Xunit;

namespace AntSim.Core.Tests
{
    /// <summary>
    /// F4.1 — contrato del esqueleto Unity verificado headless. Los scripts puros
    /// del proyecto Unity (GameStreamParser, GameStreamPresenter, PoolPickerModel,
    /// en src/App/AntSim.Unity/Assets/Scripts, sin UnityEngine) se compilan aquí
    /// y se verifican contra la salida REAL del Core: si el stream o el JSON de
    /// presets cambia de contrato, estos tests rompen ANTES de que lo haga la UI.
    /// </summary>
    public sealed class UnityStreamContractTests
    {
        private static string ProjectRoot => FindRoot();
        private static string UnityScripts => Path.Combine(ProjectRoot,
            "src", "App", "AntSim.Unity", "Assets", "Scripts");

        private static string FindRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AntSim.slnx"))
                   && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
                dir = dir.Parent!;
            return dir!.FullName;
        }

        [Fact]
        public void ScriptsPurosExisten_YNoReferencianUnity()
        {
            foreach (var rel in new[] { "Streaming/GameStreamParser.cs", "Presenter/GameStreamPresenter.cs", "UI/PoolPickerModel.cs" })
            {
                string path = Path.Combine(UnityScripts, rel);
                Assert.True(File.Exists(path), "Falta el script puro: " + rel);
                string src = File.ReadAllText(path);
                Assert.DoesNotContain("using UnityEngine", src); // regla del proyecto: puro (sin dependencia de Unity)
            }
        }

        [Fact]
        public void Parser_ReconstruyeElStreamReal()
        {
            string stream = GameScenario.Run(42, ticks: 400, colonies: 1, grid: 96,
                frameEvery: 1, seedPoolPath: null, drops: null);

            var parser = new AntSim.Unity.Scripts.Streaming.GameStreamParser();
            var views = new List<AntSim.Unity.Scripts.Streaming.GameStreamParser.TickView>();
            foreach (var line in stream.Split('\n'))
            {
                var v = parser.ParseLine(line);
                if (v != null) views.Add(v);
            }

            Assert.NotNull(parser.Header);
            Assert.Equal(42UL, parser.Header!.Seed);
            Assert.Equal(96, parser.Header.Grid);
            Assert.NotNull(parser.FinalHash);
            Assert.Equal(400UL, parser.FinalTick);

            // Todos los ticks del stream, con poses, items y colonia.
            Assert.Equal(400, views.Count);
            var last = views[^1];
            Assert.Equal(400UL, last.Tick);
            Assert.Equal(10, last.Ants.Count);     // fundadoras vivas
            Assert.All(last.Ants, a => Assert.True(a.Alive));
            Assert.Single(last.Colonies);
            Assert.True(last.Colonies[0].Adults >= 10);

            // Los eventos del canal B llegan (nacimiento/initial spawn del tick 1).
            Assert.Contains(views, v => v.Events.Count > 0);

            // Las métricas de 1 s cierran periódicamente (30 ticks).
            Assert.Contains(views, v => v.Metrics != null);
        }

        [Fact]
        public void Parser_TrazaUnHormigaConMovimientoCoherente()
        {
            string stream = GameScenario.Run(42, ticks: 60, colonies: 1, grid: 96,
                frameEvery: 1, seedPoolPath: null, drops: null);

            var parser = new AntSim.Unity.Scripts.Streaming.GameStreamParser();
            var views = new List<AntSim.Unity.Scripts.Streaming.GameStreamParser.TickView>();
            foreach (var line in stream.Split('\n'))
            {
                var v = parser.ParseLine(line);
                if (v != null) views.Add(v);
            }

            // La hormiga 1 existe en todos los ticks y no salta más de VMax*dt*2 por tick.
            uint id1 = 1;
            for (int i = 1; i < views.Count; i++)
            {
                var a0 = views[i - 1].Ants.First(a => a.Id == id1);
                var a1 = views[i].Ants.First(a => a.Id == id1);
                float dx = a1.X - a0.X, dy = a1.Y - a0.Y;
                Assert.True(dx * dx + dy * dy < 4f, $"salto imposible en tick {views[i].Tick}");
            }
        }

        [Fact]
        public void Presenter_InterpolaEntreTicksConsiguientes()
        {
            string stream = GameScenario.Run(42, ticks: 30, colonies: 1, grid: 96,
                frameEvery: 1, seedPoolPath: null, drops: null);

            var presenter = new AntSim.Unity.Scripts.Streaming.GameStreamPresenter();
            foreach (var line in stream.Split('\n')) presenter.Feed(line);

            var mid = presenter.Sample(0.5f);
            var end = presenter.Sample(1f); // clamp natural: usa el tick actual

            Assert.True(mid.Ants.Count > 0);
            Assert.Equal(30UL, mid.Tick);

            // A mitad de tick, la pose está entre el tick 29 y el 30 (si hubo tick 29).
            // Verificación débil pero real: ninguna pose NaN ni fuera del mundo.
            foreach (var a in mid.Ants)
            {
                Assert.False(float.IsNaN(a.X) || float.IsNaN(a.Y));
                Assert.InRange(a.X, 0f, 96f * 8f);
                Assert.InRange(a.Y, 0f, 96f * 8f);
            }
        }

        [Fact]
        public void Picker_ParseaElJsonCanonical_YSeparaTiers()
        {
            string json = PresetScenario.RenderCards(json: true);
            var model = AntSim.Unity.Scripts.Streaming.PoolPickerModel.ParseJson(json);

            Assert.Equal(6, model.Presets.Count);

            var rec = model.Recommended().ToList();
            var spec = model.Specialists().ToList();
            Assert.Equal(4, rec.Count);
            Assert.Equal(2, spec.Count);

            // Nivel 1 (recomendados): los 4 del diseño de UX, en orden canónico.
            Assert.Equal(new[] { "naturalista", "warm-v2", "warm-4", "warm3" },
                rec.Select(p => p.Id).ToArray());

            // Nivel 2 (especialistas): la cadena completa con su contrapartida.
            Assert.Equal(new[] { "warm3-v2", "warm-5" }, spec.Select(p => p.Id).ToArray());

            var warmv2 = rec.First(p => p.Id == "warm-v2");
            Assert.Equal(99, warmv2.Pickups);
            Assert.Equal(167.9f, warmv2.DropAvg!.Value, 1);
            Assert.Equal("artifacts/pretrain-warm-v2.antgenome", warmv2.GenomeFile);

            var warm5 = spec.First(p => p.Id == "warm-5");
            Assert.Equal(118, warm5.Pickups);
            Assert.Equal(2, warm5.GameSeedsWithUnload); // el ⚠ tiene datos detrás
        }
    }
}

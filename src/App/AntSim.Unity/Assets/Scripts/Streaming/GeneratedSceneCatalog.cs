using System.Collections.Generic;

namespace AntSim.Unity.Scripts.Streaming
{
    /// <summary>
    /// REGISTRO DE LAS ESCENAS GENERADAS (F5.3): qué ENTRADA y qué CONFIGURACIÓN
    /// produjo cada `.unity` que el repo versiona, para que regenerarlas sea
    /// reproducible sin depender de recordar los valores del Play pass ni los del
    /// medidor de rendimiento.
    ///
    /// POR QUÉ EXISTE. Las dos escenas del proyecto NO salen de los valores por
    /// defecto de su montador:
    ///
    ///   · `Assets/Scenes/Game.unity` pide ticks 7200 y velocidad 2 —la
    ///     configuración del Play pass (`scripts/playpass-live.sh`: `TICKS=7200`,
    ///     `B4_SPEED=2`)—, mientras `SceneBootstrapper.CreateGameScene` monta con
    ///     `LaunchSettings.Defaults()`: ticks 36000 y velocidad 3.
    ///   · `Assets/Scenes/MultiSim.unity` pide grid 256, 12000 ticks, frameEvery 5
    ///     y velocidad 10 —la escena del MEDIDOR (`CreateMultiSimPerfScene`)—,
    ///     mientras el menú «Crear escena multi-visor» monta grid 96 / 36000 ticks
    ///     / frameEvery 2.
    ///
    /// Eso vivía sólo en la cabeza de quien lo ejecutó: el YAML guarda los campos,
    /// no su procedencia, y el montador dice otra cosa. Regenerar «con el
    /// bootstrapper» devolvía una escena DISTINTA sin que nada avisara —la clase
    /// de fallo que este registro cierra—. Lo lee `GeneratedSceneContractTests`
    /// (headless, sin editor) y lo contrasta con los campos SERIALIZADOS de cada
    /// `.unity`, así que un pase que deje otra configuración falla nombrando el
    /// campo, la escena y el valor declarado.
    ///
    /// MEDIDO (2026-10-07, editor 6000.6.0f1, siete pases headless): la
    /// configuración de aquí es EXACTAMENTE la que reproduce el `.unity`
    /// versionado —la salida de `CreateGameScene` con `Ticks 7200 / Speed 2` es
    /// equivalente al `Game.unity` del árbol (425 documentos) y
    /// `CreateMultiSimPerfScene` reproduce `MultiSim.unity` (353), y repetir el
    /// pase no cambia la equivalencia—. El BYTE no es estable (Unity renumera
    /// anclas y `fileID` al recrear los assets, ver `CreateOrReplaceAsset`): lo
    /// que este registro fija es la equivalencia estructural, que es lo que
    /// significa «la misma escena».
    ///
    /// Vive junto a `LaunchSettings` porque es su misma cosa —la configuración de
    /// una escena— más la procedencia que el montador no guarda.
    /// </summary>
    public static class GeneratedSceneCatalog
    {
        /// <summary>Rutas versionadas, relativas a la raíz del proyecto Unity.</summary>
        public static class Paths
        {
            public const string GameScene = "Assets/Scenes/Game.unity";
            public const string MultiSimScene = "Assets/Scenes/MultiSim.unity";
        }

        /// <summary>
        /// Una vista de una escena: su capa de render, su semilla y su pool. La
        /// escena simple tiene UNA (capa 0, la Default); el multi-visor tiene una
        /// por vista, con la capa que dicta <see cref="MultiViewportModel"/> y una
        /// semilla por vista.
        /// </summary>
        public sealed class ViewSpec
        {
            public int Index;
            public int RenderLayer;
            public ulong Seed;
            /// <summary>Ruta relativa al repo root; «» = la vista arranca sin sembrar.</summary>
            public string PoolPath = "";

            public ViewSpec() { }

            public ViewSpec(int index, int renderLayer, ulong seed, string poolPath)
            {
                Index = index;
                RenderLayer = renderLayer;
                Seed = seed;
                PoolPath = poolPath;
            }

            /// <summary>
            /// Vista del multi-visor: la capa y la semilla salen del MODELO
            /// (<see cref="MultiViewportModel.RenderLayer"/> y `baseSeed + índice`),
            /// no de números escritos a mano, así que el registro no puede divergir
            /// del montador que las aplica.
            /// </summary>
            public static ViewSpec MultiView(int index, ulong baseSeed, string poolPath)
                => new ViewSpec(index, MultiViewportModel.RenderLayer(index), baseSeed + (ulong)index, poolPath);
        }

        /// <summary>
        /// Qué produjo una escena: la entrada que la escribió de verdad, quién la
        /// ejecuta, la configuración que quedó serializada y las OTRAS entradas que
        /// escriben la misma ruta (para que «regenerar» no sea ambiguo).
        /// </summary>
        public sealed class SceneSpec
        {
            public string Path = "";
            /// <summary>Entrada real (comando de menú o método de `-executeMethod`).</summary>
            public string EntryPoint = "";
            /// <summary>Quién la ejecuta en el flujo del proyecto.</summary>
            public string Producer = "";
            /// <summary>Comando reproducible de la entrada de arriba.</summary>
            public string Regenerate = "";
            /// <summary>Otras entradas que escriben la MISMA ruta (pisan la escena).</summary>
            public IReadOnlyList<string> OtherWriters = System.Array.Empty<string>();

            // Configuración serializada en el `SimPresenterBehaviour` de la escena
            // (la misma en todas las vistas de una escena multi-visor).
            public int Grid;
            public int Colonies;
            public int Ticks;
            public int FrameEvery;
            public float Speed;
            public string Species = "";

            public IReadOnlyList<ViewSpec> Views = System.Array.Empty<ViewSpec>();
        }

        /// <summary>Semilla base del multi-visor: `CreateMultiSimPerfScene` monta
        /// con `baseSeed: 42`, y cada vista toma `baseSeed + índice` (42, 43, 44,
        /// 45) — el mismo reparto que usa el medidor y el smoke.</summary>
        public const ulong MultiViewBaseSeed = 42;

        /// <summary>La escena simple, tal como está versionada.</summary>
        public static readonly SceneSpec Game = new SceneSpec
        {
            Path = Paths.GameScene,
            EntryPoint = "AntSim/Crear escena de juego (SceneBootstrapper.CreateGameScene)",
            Producer = "Play pass: scripts/playpass-live.sh (bloque 4 reconstruye la escena con el menú " +
                       "y la sonda ProbeToastsSet aplica Temp/antsim-playpass.json antes de guardarla)",
            Regenerate = "bash scripts/playpass-live.sh   (editor abierto + com.unity.pipeline; " +
                         "el bloque 4 es el que deja la escena guardada con esta configuración)",
            OtherWriters = new[]
            {
                "SceneBootstrapper.CreateGameScene + guardar en el editor: monta con los defaults " +
                "(ticks 36000, velocidad 3) — NO reproduce este fichero",
                "SceneBootstrapper.PlayFromLaunchSettings (-antsimSettings <archivo>): monta con lo que " +
                "traiga el lanzador y entra en Play sin guardar",
                "asistente «Crear escena y Jugar» y «Configuración rápida» (menús de SceneBootstrapper)",
            },
            Grid = 96,
            Colonies = 2,
            Ticks = 7200,
            FrameEvery = 1,
            Speed = 2f,
            Species = "lasius",
            Views = new[]
            {
                // La escena simple va en la capa Default (0), no en una Sim<n>: solo
                // hay un mundo, así que no hay mundos que separar.
                new ViewSpec(0, 0, 42, "artifacts/pretrain-warm-v2.antgenome"),
            },
        };

        /// <summary>El multi-visor del medidor, tal como está versionado.</summary>
        public static readonly SceneSpec MultiSim = new SceneSpec
        {
            Path = Paths.MultiSimScene,
            EntryPoint = "AntSim.Unity.Scripts.EditorTools.MultiSimBootstrapper.CreateMultiSimPerfScene",
            Producer = "el MEDIDOR de rendimiento (F5.3): 4 vistas con los pools de la cadena Fase 3ter, " +
                       "cada una con su CLI en vivo (grid 256, 12000 ticks, canal A cada 5 ticks, " +
                       "2 colonias por vista = 8 colonias en pantalla)",
            Regenerate = "Unity.exe -batchmode -nographics -quit -projectPath <proyecto> " +
                         "-executeMethod AntSim.Unity.Scripts.EditorTools.MultiSimBootstrapper.CreateMultiSimPerfScene " +
                         "-logFile <log>",
            OtherWriters = new[]
            {
                "menú «Crear escena multi-visor» (CreateMultiSimScene): grid 96, 36000 ticks, " +
                "frameEvery 2 y pools por autodetección — NO reproduce este fichero",
                "CreateMultiSimSmokeScene / …SmokeScene4 / …SmokeSceneAtta: escriben TAMBIÉN " +
                "Assets/Scenes/MultiSim.unity (vistas de replay, sin CLI en vivo)",
                "PerfSceneProbe.RunPerf (scripts/perf-scene.sh) y PlayerBuild.BuildMultiViewPlayer " +
                "(scripts/player-perf.sh, scripts/system-sweep.sh): CreateMultiSimPerfScene(grid, ticks, " +
                "coloniesPerView) con el grid y las colonias del barrido",
            },
            Grid = 256,
            Colonies = 2,
            Ticks = 12000,
            FrameEvery = 5,
            // Velocidad 10 = el default serializado de SimPresenterBehaviour: el
            // montador no la fija y el medidor no la toca (el boost lo aplica la sonda).
            Speed = 10f,
            Species = "lasius",
            Views = new[]
            {
                ViewSpec.MultiView(0, MultiViewBaseSeed, "artifacts/pretrain-warm-v2.antgenome"),
                ViewSpec.MultiView(1, MultiViewBaseSeed, "artifacts/pretrain-warm-4.antgenome"),
                ViewSpec.MultiView(2, MultiViewBaseSeed, "artifacts/pretrain-warm3.antgenome"),
                ViewSpec.MultiView(3, MultiViewBaseSeed, "artifacts/pretrain-warm2.antgenome"),
            },
        };

        /// <summary>Las escenas generadas y versionadas, en orden de ruta.</summary>
        public static IReadOnlyList<SceneSpec> Scenes { get; } = new[] { Game, MultiSim };

        /// <summary>La entrada del catálogo con esta ruta, o null si no está registrada.</summary>
        public static SceneSpec? ByPath(string path)
        {
            foreach (var scene in Scenes)
                if (scene.Path == path) return scene;
            return null;
        }
    }
}

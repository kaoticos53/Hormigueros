using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace AntSim.Unity.Scripts.Presenter
{
    /// <summary>
    /// Ancla Unity del presenter (F4.1 — esqueleto). Todo el trabajo real vive en
    /// clases puras (GameStreamParser/GameStreamPresenter): este componente solo
    /// bombea el stream, muestrea el estado interpolado cada frame y lo dibuja
    /// (pool implícito, sin GameObject por hormiga).
    ///
    /// DIBUJO INSTANCIADO (F5.3 rodaja 3): las hormigas y los ítems se agrupan por
    /// material y se dibujan con <c>Graphics.DrawMeshInstanced</c> en lotes de
    /// hasta 1023 instancias (<see cref="InstancedDrawPlan"/>, el límite del
    /// motor). Antes era una llamada por hormiga: con el multi-visor (4 vistas) y
    /// cientos de hormigas por vista, el coste de CPU lo dominaba el envío, no el
    /// mundo. Si la plataforma no soporta instancing se cae al camino de una
    /// llamada por objeto — el mismo dibujo, más lento.
    /// Pausa/velocidad escalan el consumo del stream, nunca la física (fija a 30 Hz).
    /// </summary>
    public sealed class SimPresenterBehaviour : MonoBehaviour
    {
        [Header("Fuente del stream (CLI construido del Core)")]
        public string CliPath = "build/antsim";
        public ulong Seed = 42;
        public int Ticks = 36000;        // 20 min de sim
        public int Grid = 96;
        public int Colonies = 2;
        public int FrameEvery = 1;
        [Tooltip("Especies del mundo, separadas por coma (p. ej. lasius o lasius,eciton). Vacío = default del CLI.")]
        public string Species = "lasius";
        public string? SeedPoolPath;     // p. ej. artifacts/pretrain-warm-v2.antgenome (relativa al repo root)

        [Header("Terreno (F6)")]
        [Tooltip("Laberinto con obstáculos: la CLI lo genera desde la semilla y la cabecera del stream trae los rectángulos que se pintan aquí. Sin él, el tablero es el plano clásico.")]
        public bool Maze = false;

        [Header("Reclutamiento por rastro (F8)")]
        [Tooltip("Siembra CÚMULOS de comida (6–10 ítems apilados, además de lo que pida el mundo) y da a las hormigas dos reflejos innatos: la que vuelve cargada deja rastro de comida hacia el montón y de casa hacia el nido, y la que busca gira hacia el lado con más rastro de comida. Es lo que hace visible y útil el canal E; con esto apagado el mundo es el clásico.")]
        public bool Recruitment = true;
        [Tooltip("Al terminar el horizonte, guarda la élite evolucionada en artifacts/evolved-<semilla>.antgenome (la evolución es por nacimientos; no hay botón de generación).")]
        public bool ExportElite = false;

        // — F8: PERILLAS del reclutamiento (las rellena el lanzador) —
        // Centinela -1 = «default del mundo». Tiene que ser NEGATIVO y no 0: una
        // escena guardada antes de F8 no trae estos campos y Unity los deserializa
        // a 0, que en --clusters es un valor VÁLIDO (0 montones, control A/B). Con
        // 0 como centinela el tablero nacería sin montones sin avisar.
        [Header("Montones y rastro (F8)")]
        [Tooltip("Montones de comida sembrados además de los ítems sueltos. -1 = default del mundo (8); 0 = ninguno (control A/B).")]
        public int ClusterCount = -1;
        [Tooltip("Ítems por montón, mínimo. -1 = default del mundo (6).")]
        public int ClusterMin = -1;
        [Tooltip("Ítems por montón, máximo. -1 = default del mundo (10).")]
        public int ClusterMax = -1;
        [Tooltip("Radio del montón en unidades de mundo. -1 = default del mundo (18).")]
        public float ClusterRadius = -1f;
        [Tooltip("Rastro de comida que deja la hormiga cargada (fracción de QMaxFood por segundo). -1 = default (1).")]
        public float TrailDeposit = -1f;
        [Tooltip("Vida media del rastro de comida, en segundos. -1 = default (2100).")]
        public float TrailHalfLife = -1f;
        [Tooltip("Ganancia del reflejo que sigue el rastro (rad/s a plena asimetría). -1 = default (8).")]
        public float TrailFollow = -1f;

        // — F11n: el PAR de la puerta (puerta del cerebro + glándula con precio) —
        // Entra como CONFIGURACIÓN, no como default: una escena sin estos campos
        // (la de fábrica) sigue jugando el rastro fijo y gratis de F8.
        [Tooltip("F11: el CEREBRO abre el depósito (DepositFood/DepositHome) en vez del reflejo fijo. false = juego de fábrica.")]
        public bool TrailByBrain = false;
        [Tooltip("F11b: precio del rastro, segundos de depósito a plena tasa que caben en la glándula. -1 = default (0 = sin precio).")]
        public float TrailGland = -1f;
        [Tooltip("F11b: segundos que tarda la glándula en llenarse en el nido. -1 = default (3).")]
        public float TrailGlandRefill = -1f;

        [Header("Canales opt-in del stream (telemetría pura: no cambian el hash)")]
        [Tooltip("Canal E (F4.5): feromonas de la colonia 0 cada N ticks. 0 = desactivado.")]
        public int PheroEvery = 0;
        [Tooltip("Canal F (F5.0): activaciones del MLP cada N ticks. 0 = desactivado (requiere InspectId > 0).")]
        public int ActivEvery = 0;
        [Tooltip("Canal F: id de la hormiga inspeccionada (el mismo que viaja en el canal A). 0 = sin inspección.")]
        public uint InspectId = 0;

        [Header("Intervención (F4.4): plan de drops acumulado en partida")]
        [Tooltip("Drops pendientes (tick:x:y) para el relanzamiento con --drop.")]
        public string[] PendingDropArgs = System.Array.Empty<string>();

        /// <summary>
        /// Args del plan que debe consumir la PRÓXIMA instancia del presenter.
        ///
        /// El «reiniciar con plan» (y el confirmar del diálogo de importación)
        /// recargan la escena para volver a empezar la partida: la instancia del
        /// componente se destruye, así que un campo de instancia no sobreviviría
        /// y el CLI arrancaría SIN los drops. El plan viaja aquí —una sola vez—
        /// y <see cref="Start"/> lo recoge y lo limpia.
        /// </summary>
        private static string[] _pendingArgsForNextScene = System.Array.Empty<string>();

        /// <summary>Pool que debe sembrar la PRÓXIMA instancia del presenter.
        /// Mismo motivo que los args: el confirmar del diálogo de importación
        /// recarga la escena y un campo de instancia se perdería al recargarla.</summary>
        private static string? _seedPoolForNextScene;

        /// <summary>F8 — semilla de la ronda que debe arrancar la PRÓXIMA escena.
        /// 0 = no hay ronda armada (se usa el campo <see cref="Seed"/>).</summary>
        private static ulong _seedForNextScene;

        /// <summary>F8 — número de ronda (0 = primera). Viaja en estático por el
        /// mismo motivo que el pool: la ronda nueva recarga la escena.</summary>
        private static int _roundForNextScene;

        /// <summary>Arma los args que consumirá la próxima escena (reinicio/import).</summary>
        public static void ArmPendingArgsForNextScene(string[] args)
            => _pendingArgsForNextScene = args ?? System.Array.Empty<string>();

        /// <summary>Arma el pool que sembrará la próxima escena (import F4.3).</summary>
        public static void ArmSeedPoolForNextScene(string poolPath)
            => _seedPoolForNextScene = poolPath;

        /// <summary>F8: arma la semilla (y con ella el laberinto) de la próxima ronda.</summary>
        public static void ArmSeedForNextScene(ulong seed)
            => _seedForNextScene = seed == 0UL ? 1UL : seed;

        /// <summary>F8: arma el número de ronda de la próxima escena.</summary>
        public static void ArmRoundForNextScene(int round) => _roundForNextScene = round;

        /// <summary>
        /// Los estáticos de arriba sobreviven a la recarga de escena (es lo que
        /// buscan), pero NO deben sobrevivir a un Play nuevo: al volver a pulsar
        /// Play se espera la ronda 1 con la semilla de la escena, no la ronda 7 de
        /// la sesión anterior. SubsystemRegistration corre una vez por sesión de
        /// juego, antes del primer Start.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ClearRoundArming()
        {
            _pendingArgsForNextScene = System.Array.Empty<string>();
            _seedPoolForNextScene = null;
            _seedForNextScene = 0UL;
            _roundForNextScene = 0;
        }
        [Header("Replay (debug sin CLI): prioridad sobre StreamGame si no está vacío")]
        [Tooltip("Ruta de un stream volcado a archivo (p. ej. artifacts/stream-fixture-256.jsonl). Vacío = lanza el CLI.")]
        public string? ReplayFile;

        [Header("Render")]
        // Asignados por la ESCENA (bootstrapper) o por el inspector, no en código:
        // `null!` silencia CS8618 sin volverlos nullable — el Draw ya los guarda
        // con `!= null`, y marcarlos `?` propagaría CS8602 a cada uso.
        public Mesh AntMesh = null!;
        public Material AntMaterial = null!;
        public Material CarrierMaterial = null!;
        [Tooltip("Materiales de hormigas diferenciados por índice de colonia [0..N-1].")]
        public Material[]? ColonyAntMaterials;
        [Tooltip("Materiales de portadoras diferenciados por índice de colonia [0..N-1].")]
        public Material[]? ColonyCarrierMaterials;
        public Mesh ItemMesh = null!;
        [Tooltip("Malla de la HOJA (F5.2a): silueta propia generada por el bootstrapper. Si falta, la hoja se dibuja con ItemMesh — el aspecto de antes, sin fallo.")]
        public Mesh? LeafMesh;
        public Material ItemMaterial = null!;

        [Header("Terreno (F6)")]
        [Tooltip("Quad unitario en XZ para las paredes del laberinto (assets generados por el bootstrapper).")]
        public Mesh? WallMesh;
        [Tooltip("Material de las paredes (unlit, piedra oscura).")]
        public Material? WallMaterial;

        private readonly System.Collections.Generic.List<Matrix4x4> _wallMatrices = new();
        private bool _wallsBuilt;

        /// <summary>Paredes pintadas en la última presentación (0 = sin laberinto).</summary>
        public int LastWallDraws { get; private set; }

        [Header("F5.2a: hojas con mordiscos (CutsLeft/CutsInitial del canal A)")]
        [Tooltip("Material de la hoja (verde oscuro, distinto del ítem simple).")]
        public Material? LeafMaterial;
        [Tooltip("Material de los mordiscos (marrón oscuro, se dibuja en la superficie de la hoja).")]
        public Material? BiteMaterial;

        [Tooltip("Capa de render de ESTA vista (F5.1bis multi-visor): los DrawMesh van a esa capa y la cámara con la máscara correspondiente solo ve su mundo. 0 = Default (escena de una vista, compatible con todo lo anterior). Los suelos/nidos del multi-visor se crean en la MISMA capa, así que la máscara de la cámara completa la separación.")]
        public int RenderLayer = 0;

        [Tooltip("Dibujo INSTANCIADO (F5.3 rodaja 3): agrupa por material y dibuja por lotes con Graphics.DrawMeshInstanced. Apágalo para volver a una llamada por objeto (depuración, o una plataforma sin instancing — en ese caso también se cae solo).")]
        public bool UseInstancing = true;

        /// <summary>Llamadas de dibujo de la última presentación (hormigas + ítems).
        /// Es la cifra que verifica el ahorro del instancing en vivo, sin espejo ni
        /// píxeles: la sonda de smoke la lee del componente.
        /// <see cref="DrawingBeyondBudget"/> avisa si el presupuesto se sale de lo
        /// esperado para 60 fps.</summary>
        public int LastDrawCalls { get; private set; }

        /// <summary>Presupuesto de llamadas de dibujo por vista y frame. Con el
        /// instancing, dos colonias con cuatro materiales y varioscientos de
        /// hormigas caben en menos de una docena; el aviso salta si el camino
        /// instanciado deja de estar en uso (o si la escena creció sin que nadie
        /// lo mirara).</summary>
        public const int DrawCallBudget = 32;

        public bool DrawingBeyondBudget => LastDrawCalls > DrawCallBudget;

        /// <summary>Llamadas emitidas por el camino INSTANCIADO en la última
        /// presentación. Con hormigas en pantalla, 0 significa que el dibujo NO
        /// está pasando por instancing — el medidor de rendimiento lo usa como
        /// aserción para no dar por buena una escena que no pintó nada.</summary>
        public int LastInstancedCalls { get; private set; }

        /// <summary>Llamadas emitidas por el camino de una por objeto (materiales
        /// cuyo shader no soporta instancing, o plataformas sin soporte).</summary>
        public int LastFallbackCalls { get; private set; }

        private readonly InstanceSlotDrawer _drawer = new();

        /// <summary>
        /// LONGITUD de la hormiga en unidades de mundo (no un factor de escala:
        /// el valor histórico —0.6— no significaba nada visible). 0 = automática
        /// (<see cref="AntWorldFraction"/> del lado del mundo).
        ///
        /// El valor histórico venía de un mundo de ~96 u: en el mundo real de
        /// 768 u dejaba hormigas de ~1 u de ancho, es decir **sub-píxel** —
        /// invisibles por pequeñas, no por tapadas. Como la vista no puede
        /// depender de que alguien ajuste un campo a mano, el default se DERIVA
        /// del grid.
        /// </summary>
        public float AntScale = 0f;

        [Tooltip("Longitud automática de la hormiga como fracción del lado del mundo " +
                 "(0.018 de 768 u ≈ 14 u ≈ 12 px a 720p con el tablero encuadrado).")]
        public float AntWorldFraction = 0.018f;

        [Tooltip("Diámetro automático del ítem como fracción del lado del mundo.")]
        public float ItemWorldFraction = 0.010f;

        /// <summary>Altura sobre el suelo a la que se dibujan hormigas, portadores
        /// e ítems: por encima del plano de feromonas (y≈0.02) para que la capa
        /// transparente no los tape y para no hacer z-fighting con el suelo.</summary>
        public float ActorLift = 0.6f;

        [Header("Tiempo")]
        [Tooltip("Velocidad de simulación/reproducción en rango 10× a 160×. 120× = base normal (40 × los 3× del lanzador). 0 = pausa.")]
        [Range(10f, 160f)] public float Speed = 120f;
        [Tooltip("Multiplicador adicional (1 por defecto).")]
        public float SpeedBoost = 1f;
        [Tooltip("Tecla para aumentar la velocidad (+ / = / Keypad +).")]
        public KeyCode IncreaseSpeedKey = KeyCode.KeypadPlus;
        public KeyCode IncreaseSpeedKey2 = KeyCode.Equals;
        [Tooltip("Tecla para reducir la velocidad (- / _ / Keypad -).")]
        public KeyCode DecreaseSpeedKey = KeyCode.KeypadMinus;
        public KeyCode DecreaseSpeedKey2 = KeyCode.Minus;
        [Tooltip("Tecla para pausar / reanudar la simulación.")]
        public KeyCode PauseKey = KeyCode.Space;

        private float _pausedSpeed = 120f;

        private readonly Streaming.GameStreamPresenter _presenter = new();
        private Streaming.StreamSource? _source;

        /// <summary>Presenter puro subyacente (F4.2): el HUD lo consume para
        /// repartir el último TickView a tarjetas/toasts/inspector.</summary>
        public Streaming.GameStreamPresenter Presenter => _presenter;
        private float _simTime;           // segundos de sim consumidos
        private const float Dt = 1f / 30f; // tick fijo de la arquitectura
        private volatile bool _streaming;
        private Streaming.RenderState? _lastState; // último estado muestreado (p. ej. para el inspector)

        // Arranque DIFERIDO del hilo del stream (ver StartPump): Start deja aquí
        // lo resuelto y el primer Update crea el proceso/hilo.
        private bool _pumpPending;
        private string? _repoRoot;
        private string _seedPoolResolved = "";
        private string? _exportPath;

        // — F8: ronda encadenada ————————————————————————————————
        // Cuando la ronda se cierra (extinción o horizonte) se recarga la escena
        // con la semilla y el pool de la ronda siguiente; los estáticos de arriba
        // son el único estado que sobrevive a esa recarga.
        private int _round;             // 0 = primera ronda de la sesión
        private bool _roundBegun;       // el hilo del stream se lanzó al menos una vez
        private bool _roundArmed;       // la ronda terminó y espera su recarga
        private float _roundStartedAt;
        private float _roundRestartAt;
        /// <summary>Margen mínimo desde el arranque para dar la ronda por terminada:
        /// un CLI que no existe deja el hilo en false sin haber emitido nada, y sin
        /// este margen se encadenarían rondas vacías en bucle.</summary>
        private const float RoundMinSeconds = 2f;
        /// <summary>Lo que se ve la colonia ya vacía antes de recargar la escena.</summary>
        private const float RoundPauseSeconds = 2.5f;

        /// <summary>Indica si el worker en segundo plano está transmitiendo datos.</summary>
        public bool IsStreaming => _streaming;

        /// <summary>Último estado muestreado (poses del tick en curso) — lo consumen
        /// p. ej. el raycast de selección de la tarjeta de inspección.</summary>
        public Streaming.RenderState? CurrentState => _lastState;

        /// <summary>Velocidad efectiva actual de simulación (Speed * SpeedBoost).</summary>
        public float EffectiveSpeed => Speed * SpeedBoost;

        /// <summary>
        /// Ticks que el reloj de simulación pide por SEGUNDO con la velocidad actual
        /// (F5.3, coste del sistema): es el ritmo al que el mundo TIENE que avanzar
        /// para que la reproducción no se quede atrás, y sale del propio reloj del
        /// motor (Dt), no de una constante aparte.
        /// </summary>
        public float ClockTicksPerSecond => Dt > 0f ? Speed * SpeedBoost / Dt : 0f;

        /// <summary>
        /// CPU (ms, todos sus hilos) que lleva gastada el CLI de ESTA vista (F5.3,
        /// coste del SISTEMA). El mundo se simula en otro proceso, así que su trabajo
        /// no aparece en ningún tiempo de frame del player: esto es lo que permite
        /// sumarlo. Vale 0 si esta vista no lanza CLI (p. ej. reproduciendo un
        /// archivo) — y entonces el coste del sistema no está medido, no es gratis.
        /// </summary>
        public double CliCpuMs => _source?.CliCpuMs ?? 0.0;

        /// <summary>Indica si la simulación está pausada (Speed == 0).</summary>
        public bool IsPaused => Speed <= 0f;

        /// <summary>Fija la velocidad con clamp entre 1.0x (10% base) y 100.0x (1000% base).</summary>
        public void SetSpeed(float newSpeed)
        {
            if (newSpeed <= 0f)
            {
                if (Speed > 0f) _pausedSpeed = Speed;
                Speed = 0f;
            }
            else
            {
                Speed = Streaming.SpeedControlModel.ClampSpeed(newSpeed, allowPause: false);
                _pausedSpeed = Speed;
            }
        }

        /// <summary>Fija la velocidad a partir de un porcentaje relativo a la base (10% a 1000%).</summary>
        public void SetSpeedPercent(float percent)
        {
            SetSpeed(Streaming.SpeedControlModel.PercentToSpeed(percent));
        }

        /// <summary>Aumenta la velocidad al siguiente preset lógico o +10%.</summary>
        public void StepSpeedUp()
        {
            if (Speed <= 0f)
            {
                Speed = _pausedSpeed > 0f ? _pausedSpeed : Streaming.SpeedControlModel.DefaultSpeed;
                return;
            }
            SetSpeed(Streaming.SpeedControlModel.StepUp(Speed));
        }

        /// <summary>Reduce la velocidad al preset lógico inferior o -10%.</summary>
        public void StepSpeedDown()
        {
            if (Speed <= 0f) return;
            SetSpeed(Streaming.SpeedControlModel.StepDown(Speed));
        }

        /// <summary>Alterna entre pausa y la velocidad previa.</summary>
        public void TogglePause()
        {
            if (Speed > 0f)
            {
                _pausedSpeed = Speed;
                Speed = 0f;
            }
            else
            {
                Speed = _pausedSpeed > 0f ? _pausedSpeed : Streaming.SpeedControlModel.DefaultSpeed;
            }
        }

        /// <summary>
        /// F7 — topología del pool sembrado, leída del propio CLI
        /// (`--mode genome-info --json`). Devuelve null sin pool o si el oráculo no
        /// contesta: el inspector cae entonces al contrato clásico 19→8→6.
        /// </summary>
        public int[]? SeedPoolBrainSizes()
        {
            if (string.IsNullOrEmpty(SeedPoolPath)) return null;
            try
            {
                string? repoRoot = Streaming.RepoPathResolver.RepoRootFromDataPath(Application.dataPath);
                string pool = repoRoot != null
                    ? Streaming.RepoPathResolver.Resolve(SeedPoolPath!, baseDir: null, repoRoot)
                    : SeedPoolPath!;
                if (!System.IO.File.Exists(pool)) return null;
                var source = new Streaming.StreamSource(CliPath, repoRoot);
                string json = source.FetchGenomeInfoJson(pool);
                return Streaming.ImportDialogModel.TryParseSizes(json, out int[] sizes) ? sizes : null;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[SimPresenter] no pude leer la topología del pool: {ex.Message}");
                return null;
            }
        }

        private void Start()
        {
            // Play-pass: sin esto el bucle del jugador se CONGELA al perder el foco
            // el editor (Application.runInBackground = false por defecto): el
            // Update deja de correr a los ~2 frames y ni el mundo ni el HUD
            // avanzan. Un pass automático (el CLI guiando el editor sin foco) es
            // imposible sin esta línea; para un simulador-espectáculo también es
            // lo deseable.
            Application.runInBackground = true;

            // F8: si la escena se recarga para abrir otra ronda, la semilla y el
            // número vienen armados por el presentador anterior (el pool se recoge
            // más abajo, con las rutas resueltas).
            if (_seedForNextScene != 0UL)
            {
                Seed = _seedForNextScene;
                _seedForNextScene = 0UL;
            }
            _round = _roundForNextScene;
            _roundStartedAt = Time.time;

            // Un reinicio de escena (reiniciar-con-plan / importar pool) deja aquí
            // el plan armado: se recoge y se limpia para que no se aplique dos veces.
            if (_pendingArgsForNextScene.Length > 0)
            {
                PendingDropArgs = _pendingArgsForNextScene;
                _pendingArgsForNextScene = System.Array.Empty<string>();
            }
            if (_seedPoolForNextScene != null)
            {
                SeedPoolPath = _seedPoolForNextScene;
                _seedPoolForNextScene = null;
            }

            // Play-pass: el CLI escribe el JSONL entero de una vez, así que sin
            // buffer el presenter saltaría al último tick al primer frame (nunca
            // se verían ni la partida ni las alertas del canal D). Con el buffer,
            // Update avanza la reproducción al ritmo de simulación.
            _presenter.Buffered = true;

            // Play-pass: el cwd del editor es la carpeta del proyecto, no la del
            // repo — las rutas relativas (build/antsim, artifacts/…) se resuelven
            // contra el repo root derivado de Application.dataPath. En un PLAYER
            // empaquetado dataPath es <build>/<Nombre>_Data, así que el ancla sale
            // del marcador del repo (build dentro del repo): el mismo binario
            // funciona lanzado desde el editor y desde el build.
            string? repoRoot = Streaming.RepoPathResolver.RepoRootFromDataPath(Application.dataPath);
            string seedPool = SeedPoolPath != null && repoRoot != null
                ? Streaming.RepoPathResolver.Resolve(SeedPoolPath, baseDir: null, repoRoot)
                : SeedPoolPath ?? "";
            _source = new Streaming.StreamSource(CliPath, repoRoot);
            // Contrapresión (F6): el stream de 2 h se presenta a 120×, y el CLI
            // produce más rápido de lo que se consume. Sin tope, la cola acumula
            // ticks durante una hora. 600 ticks = 20 s de simulación de colchón.
            _presenter.MaxPending = 600;

            // Los valores que necesita el hilo del stream se guardan aquí: el
            // hilo no arranca en Start (ver `_pumpPending`).
            _repoRoot = repoRoot;
            _seedPoolResolved = seedPool;
            _pumpPending = true;

            // La élite se guarda al terminar la ronda (evolución por nacimientos:
            // no hay reinicio de generación que la conserve). El nombre lleva la
            // semilla de ESTA ronda: es la que siembra la siguiente, y no se pisa
            // con la de la anterior.
            _exportPath = null;
            if (ExportElite && string.IsNullOrEmpty(ReplayFile))
            {
                _exportPath = repoRoot != null
                    ? System.IO.Path.Combine(repoRoot, "artifacts", $"evolved-{Seed}.antgenome")
                    : null;
            }

            Debug.Log($"[AntSim] Ronda {_round + 1} · semilla {Seed} · grid {Grid}" +
                      (_round > 0 ? " (encadenada: laberinto nuevo)" : ""));
        }

        /// <summary>
        /// F8 — las perillas de montones y rastro que ESTA partida manda al CLI,
        /// con los defaults del mundo en los campos sin rellenar. El HUD las
        /// publica para que se vean las cifras con las que se siembra.
        /// </summary>
        public Streaming.RecruitmentKnobs ActiveRecruitment => RecruitmentKnobsOrDefault();

        /// <summary>
        /// F8 — lo que viaja al CLI como cúmulos/rastro. El lanzador rellena los
        /// campos; con el centinela -1 se queda el default del mundo, así que una
        /// escena vieja (o un Play directo desde el inspector) sigue jugando la
        /// partida de siempre y no un mundo a medio configurar.
        /// </summary>
        private Streaming.RecruitmentKnobs RecruitmentKnobsOrDefault()
        {
            var tuning = Streaming.RecruitmentKnobs.Default;
            if (ClusterCount >= 0) tuning.Clusters = ClusterCount;
            if (ClusterMin >= 0) tuning.ClusterMin = ClusterMin;
            if (ClusterMax >= 0) tuning.ClusterMax = ClusterMax;
            if (ClusterRadius >= 0f) tuning.ClusterRadius = ClusterRadius;
            if (TrailDeposit >= 0f) tuning.TrailDeposit = TrailDeposit;
            if (TrailHalfLife >= 0f) tuning.TrailHalfLife = TrailHalfLife;
            if (TrailFollow >= 0f) tuning.TrailFollow = TrailFollow;
            if (TrailByBrain) tuning.TrailByBrain = true;
            if (TrailGland >= 0f) tuning.TrailGlandSeconds = TrailGland;
            if (TrailGlandRefill >= 0f) tuning.TrailGlandRefillSeconds = TrailGlandRefill;
            return tuning;
        }

        /// <summary>
        /// Arranca el hilo que bombea el stream (CLI o replay). Lo llama el PRIMER
        /// <see cref="Update"/>, no <see cref="Start"/>: crear el proceso del CLI
        /// (<c>Process.Start</c>) dentro de la carga con la que el editor entra en
        /// Play deja el hilo principal del editor bloqueado — sin un solo Update,
        /// ventana "no responde" y el CLI escribiendo a una tubería que nadie lee.
        /// Medido con la partida del laberinto (seed 42, grid 96, 216k ticks): en
        /// Start se cuelga siempre; diferido un frame, la partida corre. No cambia
        /// la partida: el replay ya presenta con un tick de retardo.
        /// </summary>
        private void StartPump()
        {
            _streaming = true;
            _roundBegun = true;
            // Hilo DEDICADO (no del pool): una partida de 2 h bloquea este hilo
            // esperando la contrapresión; ocupar un hilo del pool una hora no es
            // aceptable.
            var pump = new System.Threading.Thread(() =>
            {
                try
                {
                    if (!string.IsNullOrEmpty(ReplayFile))
                    {
                        string replay = _repoRoot != null
                            ? Streaming.RepoPathResolver.Resolve(ReplayFile, baseDir: null, _repoRoot)
                            : ReplayFile;
                        _source.StreamFile(replay, line => _presenter.Feed(line));
                    }
                    else
                        _source.StreamGame(Seed, Ticks, Grid, Colonies, FrameEvery,
                            _seedPoolResolved.Length > 0 ? _seedPoolResolved : null,
                            line => _presenter.Feed(line), PendingDropArgs,
                            PheroEvery, ActivEvery, InspectId, Species,
                            maze: Maze, exportPath: _exportPath,
                            recruitment: Recruitment,
                            recruitmentKnobs: RecruitmentKnobsOrDefault(),
                            // F8: la ronda acaba cuando se mueren todas — no al
                            // agotar el horizonte de 2 h. El CLI exporta la élite en
                            // ese tick y es lo que siembra la ronda siguiente.
                            untilExtinct: true);
                }
                catch (Exception ex)
                {
                    // Sin excepciones cruzando el hilo: el fallo se ve en el log de
                    // Unity y en el HUD (el mundo simplemente no llega).
                    UnityEngine.Debug.LogError($"[SimPresenter] stream falló: {ex.Message}");
                }
                finally
                {
                    _streaming = false;
                }
            })
            { IsBackground = true, Name = "AntSimStream" };
            pump.Start();
        }

        /// <summary>
        /// F6 — Parar Play cierra el CLI y despierta al hilo atascado en la
        /// contrapresión. Sin esto el hijo queda huérfano bloqueado en la tubería
        /// (el caso no existía con un horizonte de 7.200 ticks: el CLI terminaba
        /// solo en un segundo).
        /// </summary>
        private void OnDestroy()
        {
            _source?.Stop();
            _presenter.Close();
            _streaming = false;
        }

        private void OnApplicationQuit()
        {
            _source?.Stop();
            _presenter.Close();
        }

        private void Update()
        {
            if (_pumpPending)
            {
                _pumpPending = false;
                StartPump();
            }
            // Atajos de teclado: + / = aumentan velocidad, - / _ reducen velocidad, Espacio pausa/reanuda
            if (Input.GetKeyDown(IncreaseSpeedKey) || Input.GetKeyDown(IncreaseSpeedKey2))
            {
                StepSpeedUp();
            }
            else if (Input.GetKeyDown(DecreaseSpeedKey) || Input.GetKeyDown(DecreaseSpeedKey2))
            {
                StepSpeedDown();
            }
            else if (Input.GetKeyDown(PauseKey))
            {
                TogglePause();
            }

            // Pausa (Speed 0): congela el reloj de simulación, pero SIGUE dibujando
            // — si no, pausar dejaba la pantalla sin mundo.
            if (Speed > 0f) _simTime += Time.deltaTime * Speed * SpeedBoost;

            // Reproducción: el tick k+1 es el "actual" para interpolar desde k con
            // fracción frac(simTime/Dt) (retraso de 1 tick de la arquitectura).
            _presenter.AdvanceTo((ulong)(_simTime / Dt) + 1);

            // Dibuja el tick muestreado (la interpolación vive en Sample).
            var state = _presenter.Sample(Frac());
            _lastState = state;
            Draw(state);

            MaybeAdvanceRound();
        }

        /// <summary>
        /// F8 — cierra la ronda cuando el stream termina y abre la siguiente.
        ///
        /// El final de ronda NO se decide aquí mirando la extinción: el CLI ya
        /// corta en ese tick (`--until-extinct`) y exporta la élite, así que basta
        /// con esperar a que el hilo del stream acabe. Se exige que la ronda haya
        /// empezado y que haya llegado la cabecera —un CLI ausente también deja el
        /// hilo en false— y un margen mínimo, para no encadenar rondas en bucle si
        /// el arranque falla. Un replay es UNA corrida: no encadena.
        /// </summary>
        private void MaybeAdvanceRound()
        {
            if (!string.IsNullOrEmpty(ReplayFile)) return;

            if (!_roundArmed)
            {
                if (!_roundBegun || _streaming || _presenter.Header == null) return;
                if (Time.time - _roundStartedAt < RoundMinSeconds) return;

                _roundArmed = true;
                _roundRestartAt = Time.time + RoundPauseSeconds;
                Debug.Log($"[AntSim] Ronda {_round + 1} terminada en el tick {_presenter.FinalTick}" +
                          (_presenter.Extinct ? " (extinción)" : " (horizonte)") +
                          $" · nueva ronda en {RoundPauseSeconds:0.#} s");
                return;
            }

            if (Time.time >= _roundRestartAt) StartNextRound();
        }

        /// <summary>
        /// F8 — abre la ronda siguiente: semilla derivada de la anterior (el mundo y
        /// su laberinto salen de ella) y cerebros de la élite que acaba de exportar
        /// la ronda muerta. La escena se recarga —el mecanismo que ya usa el
        /// reinicio con plan— porque así el HUD, las gráficas y el mundo arrancan
        /// limpios; el estado viaja en los estáticos armados.
        /// </summary>
        private void StartNextRound()
        {
            int next = _round + 1;
            ulong nextSeed = Streaming.RoundModel.NextSeed(Seed, next);
            string elite = _exportPath != null && System.IO.File.Exists(_exportPath) ? _exportPath : "";
            string pool = Streaming.RoundModel.NextSeedPool(elite, _seedPoolResolved);

            ArmSeedForNextScene(nextSeed);
            ArmSeedPoolForNextScene(pool);
            ArmRoundForNextScene(next);
            ArmPendingArgsForNextScene(System.Array.Empty<string>());

            Debug.Log($"[AntSim] Ronda {next + 1}: semilla {nextSeed} (laberinto nuevo) · " +
                      (elite.Length > 0
                          ? $"cerebros de la élite evolucionada ({System.IO.Path.GetFileName(elite)})"
                          : $"mismo pool ({System.IO.Path.GetFileName(pool)}): no hubo élite exportada"));

            UnityEngine.SceneManagement.SceneManager.LoadScene(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
        }

        /// <summary>
        /// Construye las matrices de las paredes una sola vez, cuando llega la
        /// cabecera. Las celdas vienen en coordenadas de rejilla (celda = 8 u) y
        /// el quad unitario se escala al rectángulo; Y del mundo va a Z de Unity
        /// (el tablero se ve en cenital), como el resto de la vista.
        /// </summary>
        private void BuildWallsIfNeeded()
        {
            if (_wallsBuilt) return;
            var header = Presenter.Header;
            if (header == null) return; // aún no llegó la cabecera
            _wallsBuilt = true;
            _wallMatrices.Clear();
            float cell = Streaming.WorldUnits.PerCell;
            const float wallLift = 0.35f; // sobre el quad de feromonas (0,25), bajo los actores
            for (int i = 0; i < header.Maze.Count; i++)
            {
                var r = header.Maze[i];
                float w = r.W * cell;
                float h = r.H * cell;
                var pos = new Vector3((r.X * cell) + w * 0.5f, wallLift, (r.Y * cell) + h * 0.5f);
                _wallMatrices.Add(Matrix4x4.TRS(pos, Quaternion.identity, new Vector3(w, 1f, h)));
            }
        }

        private float Frac()
        {
            float t = _simTime / Dt;
            return t - Mathf.Floor(t);
        }

        /// <summary>Punto de entrada SIN DISPOSITIVO (F5.2, como PickNearest/
        /// EnqueueDrop) para smokes batch: avanza el reloj de reproducción UN
        /// tick y presenta. El bucle de jugador de un editor batch sobre un
        /// Library recién importado puede quedar congelado (frameCount clavado,
        /// dt=0 — la sonda del multi-visor lo detecta); esta entrada permite
        /// verificar la reproducción determinista del stream sin bucle.
        /// En interactivo NO se usa: el reloj lo empuja Update.</summary>
        public void PumpOneTick()
        {
            _simTime += Dt;
            _presenter.AdvanceTo((ulong)(_simTime / Dt) + 1);
            _lastState = _presenter.Sample(0f);
            Draw(_lastState);
        }

        /// <summary>Lado del mundo en unidades (grid × celdas): la referencia de
        /// toda la escala visual.</summary>
        public float WorldSide => Streaming.WorldUnits.WorldSize(Grid);

        /// <summary>Longitud efectiva de la hormiga en unidades (la del campo o la
        /// automática, derivada del lado del mundo).</summary>
        public float EffectiveAntScale => AntScale > 0f ? AntScale : WorldSide * AntWorldFraction;

        /// <summary>Escala efectiva del ítem.</summary>
        public float EffectiveItemScale => WorldSide * ItemWorldFraction;

        /// <summary>
        /// Rotación con la que se dibuja un actor a partir del RUMBO del stream
        /// (radianes; el mismo del Core: `X += cos(rumbo)·v`, `Y += sin(rumbo)·v`).
        ///
        /// Está aquí, y en un solo sitio, a propósito: el bucle de dibujo no repite
        /// la fórmula y la orientación se puede MEDIR desde fuera con la
        /// matemática de Unity de verdad (`scripts/unity/ProbeActorOrientation.cs`,
        /// que la ejecuta dentro del editor) — replicar a mano la convención de
        /// `Euler` es justo cómo se cuela un giro de 90°.
        ///
        /// `Rx(90)` tumba la malla sobre el tablero: su +Y —el EJE DE AVANCE de la
        /// caja del juego, la cabeza de la hormiga— pasa al +Z del mundo, que con la
        /// cámara de arriba es «norte de pantalla». El mundo mapea `X→X` e `Y→Z`, así
        /// que el giro sobre Y tiene que llevar ese morro del norte de pantalla al
        /// rumbo, y eso son **90° menos el rumbo**: rumbo 0 (mapa +X, este de
        /// pantalla) → 90° de giro; rumbo 90° (mapa +Y, norte) → 0°.
        ///
        /// MEDIDO (2026-10-10, proyecto real vía `ProbeActorOrientation`): con
        /// `-rumbo` en vez de `90 − rumbo` el morro salía a **90° justos** del rumbo
        /// en los ocho rumbos de prueba —la hormiga andaba de lado, que es lo que se
        /// veía en pantalla—, y ni los conteos ni los píxeles de «hay hormigas» lo
        /// decían.
        /// </summary>
        public static Quaternion ActorRotation(float headingRadians)
            => Quaternion.Euler(90f, 90f - headingRadians * Mathf.Rad2Deg, 0f);

        /// <summary>
        /// Rotación de una silueta ACOSTADA sobre el tablero: `ActorRotation` con el
        /// rumbo del Core que apunta al NORTE (+Y del mapa), no una segunda idea de
        /// «cómo se acuesta una pieza». Los ítems y las hojas no traen rumbo —el
        /// `FoodItem` del Core no guarda ángulo y el stream no lo lleva—, así que su
        /// eje de dibujo (el +Y de la caja: la punta del limbo, el eje del grumo) se
        /// acuesta y se queda al norte; inventarle un giro por pieza sería una
        /// decisión que el dato no tiene.
        /// </summary>
        public static Quaternion FlatRotation => ActorRotation(Mathf.PI * 0.5f);

        /// <summary>
        /// Escala de una silueta acostada: 1:1 en el plano del tablero y el GROSOR a la
        /// mitad. Con <see cref="FlatRotation"/> la caja del juego queda x = ancho,
        /// y = largo, z = grosor —la misma convención que la hormiga—, así que escalar
        /// `z` y no `y` es toda la diferencia entre una hoja y un canto.
        ///
        /// MEDIDO (2026-10-10, aritmética de la caja, sin editor) con `Quaternion.identity`
        /// y las escalas que había: la hoja se dibujaba DE PIE —el eje de dibujo, su +Y,
        /// caía en el eje VERTICAL, así que desde la cámara de arriba se veía su CANTO—
        /// y el 1,1 iba al grosor (1,0 u) mientras el largo (1,24 u) recibía el 0,5: en el
        /// plano salía un canto de 0,66 s × 1,10 s, el limbo no apuntaba a nada y no se
        /// veía ni la punta ni el nervio. Lo mismo con la carga (0,68 s × 1,00 s de canto
        /// y 0,80 s de alto) y con los mordiscos.
        /// </summary>
        public static Vector3 FlatScale(float s) => new Vector3(s, s, s * 0.5f);

        /// <summary>
        /// Matriz de una silueta acostada (posición + <see cref="FlatRotation"/> +
        /// <see cref="FlatScale"/>), en UN solo sitio: la usan el ítem, la hoja y sus
        /// mordiscos, y la sonda `scripts/unity/ProbeActorOrientation.cs` mide ESTA
        /// matriz, no una copia de la fórmula.
        /// </summary>
        public static Matrix4x4 FlatTRS(Vector3 pos, float s)
            => Matrix4x4.TRS(pos, FlatRotation, FlatScale(s));

        /// <summary>
        /// Centro de la muesca `b` (de `total`) sobre el BORDE del limbo: se reparte
        /// sobre la ELIPSE del arte —semiejes 0,30·s (ancho, X) × 0,62·s (largo, Z),
        /// los extremos de `GameSpriteModel.Leaf()` tras `FlatRotation`—, no sobre un
        /// círculo. Un radio único no cae en el borde en ningún sitio: con 0,55 s, por
        /// los costados queda FUERA del limbo (0,55 &gt; 0,30) y por las puntas DENTRO
        /// (0,55 &lt; 0,62), así que las muescas flotaban en el vacío junto a la hoja en
        /// vez de comer de ella. Devuelve el desplazamiento en el PLANO del tablero
        /// (X, Z) respecto al centro de la hoja; el pequeño levantamiento que alterna
        /// arriba/abajo para que se lean en cenital es de `AddBites`.
        ///
        /// Está aquí, y en un solo sitio, por lo mismo que `FlatTRS`: el paso del Play
        /// pass que lo comprueba (`scripts/unity/ProbeActorOrientation.cs`) llama a
        /// ESTA función, no a una copia de la fórmula —replicarla es cómo se cuela una
        /// muesca que no toca la hoja.
        /// </summary>
        public static Vector2 BiteOffset(int b, int total, float s)
        {
            float angle = (b / (float)(total > 0 ? total : 1)) * Mathf.PI * 2f;
            return new Vector2(
                Mathf.Cos(angle) * (UI.GameSpriteModel.LeafHalfWidth * s),
                Mathf.Sin(angle) * (UI.GameSpriteModel.LeafHalfLength * s));
        }

        /// <summary>
        /// Escala de la CAJA del actor (la del modelo, `GameSpriteModel`: 2 u de largo
        /// × 1 u de ancho × 1 u de grosor) a partir del LARGO en unidades que quiere
        /// el dibujo — el de una hormiga es <see cref="EffectiveAntScale"/>.
        ///
        /// Está aquí, y en un solo sitio, por lo mismo que <see cref="ActorRotation"/>:
        /// la sonda que mide la silueta EN PÍXELES
        /// (`scripts/unity/ProbeAntHeadingPixels.cs`) dibuja la hormiga con esta
        /// escala y con la malla real, y replicar la fórmula a mano es cómo se cuela
        /// una hormiga de otro tamaño —que también es un defecto visual que ninguna
        /// muestra de estado mira.
        /// </summary>
        public static Vector3 ActorBoxScale(float length)
            => new Vector3(length * 0.33f, length * 0.5f, length * 0.16f);

        private void Draw(Streaming.RenderState state)
        {
            float world = WorldSide;
            float antS = EffectiveAntScale;
            float itemS = EffectiveItemScale;
            float lift = ActorLift > 0f ? ActorLift : world * 0.0008f;
            bool instancing = UseInstancing && SystemInfo.supportsInstancing;
            LastDrawCalls = 0;
            LastInstancedCalls = 0;
            LastFallbackCalls = 0;
            LastWallDraws = 0;

            // — Terreno (F6): ANTES de actores e ítems, para que éstos se pinten
            //   encima. Una sola pasada instanciada con los rectángulos de la
            //   cabecera: las paredes son las mismas celdas que colisionan. —
            if (WallMesh != null && WallMaterial != null)
            {
                BuildWallsIfNeeded();
                if (_wallMatrices.Count > 0)
                {
                    int wallLayer = RenderLayer > 0 ? RenderLayer : 0;
                    _drawer.Clear();
                    for (int i = 0; i < _wallMatrices.Count; i++)
                        _drawer.Add(WallMaterial, _wallMatrices[i]);
                    LastDrawCalls += _drawer.Draw(WallMesh, wallLayer, instancing);
                    LastWallDraws = _wallMatrices.Count;
                }
            }

            if (AntMesh != null && AntMaterial != null)
            {
                // Hormiga TUMBADA y alargada en la dirección de avance. El defecto
                // visible que destapó la sonda de píxeles: la cápsula se dibujaba
                // vertical (0, -heading, 0), así que el bicho era un poste de ~1 u
                // —a cualquier zoom, «no se ven hormigas»— y ni siquiera se leía
                // hacia dónde iba. Ahora: Rx(90) acuesta la cápsula (su eje Y pasa
                // a apuntar al avance) y la escala es asimétrica — largo, ancho y
                // grosor derivados del LARGO, con la malla natural (2 u × 1 u).
                // Las portadoras son algo mayores: el relevo se lee sin HUD.
                const float carrierBoost = 1.25f;
                int layer = RenderLayer > 0 ? RenderLayer : 0;
                _drawer.Clear();
                foreach (var a in state.Ants)
                {
                    if (!a.Alive) continue;
                    float len = a.HasLoad ? antS * carrierBoost : antS;
                    var scale = ActorBoxScale(len);
                    var pos = new Vector3(a.X, lift, a.Y);
                    var rot = ActorRotation(a.Heading);

                    Material mat;
                    if (a.HasLoad)
                    {
                        if (ColonyCarrierMaterials != null && a.ColonyId >= 0 && a.ColonyId < ColonyCarrierMaterials.Length && ColonyCarrierMaterials[a.ColonyId] != null)
                            mat = ColonyCarrierMaterials[a.ColonyId];
                        else
                            mat = CarrierMaterial ?? AntMaterial;
                    }
                    else
                    {
                        if (ColonyAntMaterials != null && a.ColonyId >= 0 && a.ColonyId < ColonyAntMaterials.Length && ColonyAntMaterials[a.ColonyId] != null)
                            mat = ColonyAntMaterials[a.ColonyId];
                        else
                            mat = AntMaterial;
                    }
                    if (mat == null) mat = AntMaterial;

                    // Se ACUMULA por material; el envío (lotes instanciados o una
                    // llamada por hormiga) lo decide el agrupador al final.
                    _drawer.Add(mat, Matrix4x4.TRS(pos, rot, scale));
                }
                LastDrawCalls += _drawer.Draw(AntMesh, layer, instancing);
            }

            if (ItemMesh != null && ItemMaterial != null)
            {
                // Ítem: silueta propia (la esfera la sustituye la CARGA generada
                // por el bootstrapper) escalada por el diámetro querido; el tamaño
                // insinúa la cantidad restante.
                //
                // F5.2a: las hojas (IsLeaf) van en su propia PASADA porque el
                // agrupador de lotes dibuja TODAS las matrices de una pasada con
                // UNA malla: con la hoja en la misma pasada que el ítem simple no
                // podría tener silueta propia. Los mordiscos (cortes consumidos,
                // CutsInitial − CutsLeft) siguen siendo muescas pequeñas en el
                // borde, con la malla del ítem cuando la hoja ya tiene la suya.
                int layer = RenderLayer > 0 ? RenderLayer : 0;
                _drawer.Clear();
                foreach (var it in state.Items)
                {
                    if (it.IsLeaf && LeafMaterial != null) continue;
                    var pos = new Vector3(it.X, lift * 0.7f, it.Y);
                    float s = itemS * (0.55f + 0.03f * it.Amount);
                    _drawer.Add(ItemMaterial, FlatTRS(pos, s));
                }
                LastDrawCalls += _drawer.Draw(ItemMesh, layer, instancing);

                if (LeafMaterial != null)
                {
                    bool ownLeafMesh = LeafMesh != null;
                    _drawer.Clear();
                    foreach (var it in state.Items)
                    {
                        if (!it.IsLeaf) continue;
                        var pos = new Vector3(it.X, lift * 0.7f, it.Y);
                        float s = itemS * (0.55f + 0.03f * it.Amount);
                        // La hoja con la MISMA matriz que el ítem: acostada, el limbo
                        // 1:1 en el plano (su largo, 1,24 u de la caja, va al norte) y el
                        // grosor a la mitad. Antes se dibujaba de canto y su escala
                        // alargaba justo el eje equivocado.
                        _drawer.Add(LeafMaterial, FlatTRS(pos, s));
                        // Sin malla propia los mordiscos comparten pasada (materiales
                        // distintos, misma malla); con ella van aparte, más abajo.
                        if (!ownLeafMesh && BiteMaterial != null)
                            AddBites(it, pos, s);
                    }
                    LastDrawCalls += _drawer.Draw(ownLeafMesh ? LeafMesh! : ItemMesh, layer, instancing);

                    if (ownLeafMesh && BiteMaterial != null)
                    {
                        _drawer.Clear();
                        foreach (var it in state.Items)
                        {
                            if (!it.IsLeaf) continue;
                            float s = itemS * (0.55f + 0.03f * it.Amount);
                            AddBites(it, new Vector3(it.X, lift * 0.7f, it.Y), s);
                        }
                        LastDrawCalls += _drawer.Draw(ItemMesh, layer, instancing);
                    }
                }
            }

            LastInstancedCalls = _drawer.InstancedCalls;
            LastFallbackCalls = _drawer.FallbackCalls;
        }

        /// <summary>Las muescas de una hoja: un corte consumido por mordisco, sobre el
        /// BORDE del limbo (la elipse de `BiteOffset`, no un círculo) y alternando
        /// arriba/abajo para que se lean en vista cenital.</summary>
        private void AddBites(Streaming.GameStreamParser.ItemView it, Vector3 pos, float s)
        {
            int bitesEaten = it.CutsInitial - it.CutsLeft;
            int totalBites = it.CutsInitial > 0 ? it.CutsInitial : 1;
            float biteRadius = s * 0.18f;
            for (int b = 0; b < bitesEaten; b++)
            {
                var off = BiteOffset(b, totalBites, s);
                var bitePos = new Vector3(
                    pos.x + off.x,
                    pos.y + (b % 2 == 0 ? 0.05f : -0.05f),
                    pos.z + off.y);
                // El mordisco también es una silueta acostada: de canto no se veía.
                _drawer.Add(BiteMaterial!, FlatTRS(bitePos, biteRadius));
            }
        }

        /// <summary>
        /// Agrupa matrices POR MATERIAL y las envía por lotes. Reutiliza todos sus
        /// búferes (el camino de render no debe generar basura por frame) y el orden
        /// de los materiales —y por tanto el de los lotes— es el de primera
        /// aparición en el frame, que es determinista porque el orden de las
        /// hormigas sale del stream.
        /// </summary>
        private sealed class InstanceSlotDrawer
        {
            private readonly List<Material> _materials = new();
            private readonly List<List<Matrix4x4>> _matrices = new();
            private readonly Dictionary<Material, int> _slotOf = new();
            private readonly Matrix4x4[] _buffer = new Matrix4x4[Streaming.InstancedDrawPlan.BatchLimit];
            private readonly HashSet<Material> _cannotInstance = new();

            /// <summary>Llamadas por lotes emitidas (camino instanciado).</summary>
            public int InstancedCalls { get; private set; }

            /// <summary>Llamadas por objeto emitidas (camino de respaldo).</summary>
            public int FallbackCalls { get; private set; }

            public void Clear()
            {
                for (int i = 0; i < _matrices.Count; i++) _matrices[i].Clear();
                _materials.Clear();
                _matrices.Clear();
                _slotOf.Clear();
            }

            public void Add(Material material, in Matrix4x4 matrix)
            {
                if (!_slotOf.TryGetValue(material, out int slot))
                {
                    slot = _materials.Count;
                    _slotOf[material] = slot;
                    _materials.Add(material);
                    _matrices.Add(new List<Matrix4x4>());
                }
                _matrices[slot].Add(matrix);
            }

            /// <summary>Número de llamadas de dibujo emitidas.</summary>
            public int Draw(Mesh mesh, int layer, bool instancing)
            {
                int calls = 0;
                for (int slot = 0; slot < _materials.Count; slot++)
                {
                    var material = _materials[slot];
                    var matrices = _matrices[slot];
                    int total = matrices.Count;
                    if (total == 0) continue;

                    // Un material solo entra al camino instanciado si de verdad
                    // puede: `DrawMeshInstanced` LANZA si `enableInstancing` está
                    // apagado, y esa excepción aborta el resto del dibujo (el
                    // tablero se queda sin hormigas). Fue un defecto REAL,
                    // destapado por el medidor de rendimiento.
                    if (!instancing || _cannotInstance.Contains(material) || !EnsureInstancing(material))
                    {
                        for (int i = 0; i < total; i++)
                        {
                            Graphics.DrawMesh(mesh, matrices[i], material, layer);
                            calls++;
                            FallbackCalls++;
                        }
                        continue;
                    }

                    int batches = Streaming.InstancedDrawPlan.Batches(total);
                    int callsBefore = calls;
                    int instancedBefore = InstancedCalls;
                    try
                    {
                        for (int b = 0; b < batches; b++)
                        {
                            int start = Streaming.InstancedDrawPlan.BatchStart(b);
                            int size = Streaming.InstancedDrawPlan.BatchSize(total, b);
                            for (int i = 0; i < size; i++) _buffer[i] = matrices[start + i];
                            Graphics.DrawMeshInstanced(mesh, 0, material, _buffer, size, null,
                                ShadowCastingMode.Off, false, layer);
                            calls++;
                            InstancedCalls++;
                        }
                    }
                    catch (Exception ex)
                    {
                        // Shader sin soporte de instancing (o un driver que lo
                        // rechaza): ese material queda en la lista negra y se
                        // dibuja por objeto. El dibujo CORRECTO manda sobre la
                        // optimización, y el aviso sale una sola vez por material.
                        // Los contadores vuelven atrás: un lote que lanzó no se
                        // cuenta como dibujado (la Contabilidad tiene que decir la
                        // verdad, o el medidor daría por bueno un frame a medias).
                        _cannotInstance.Add(material);
                        calls = callsBefore;
                        InstancedCalls = instancedBefore;
                        Debug.LogWarning($"[presenter] {material.name}: DrawMeshInstanced rechazado ({ex.Message}); " +
                                         "se dibuja por objeto");
                        for (int i = 0; i < total; i++)
                        {
                            Graphics.DrawMesh(mesh, matrices[i], material, layer);
                            calls++;
                            FallbackCalls++;
                        }
                    }
                }
                return calls;
            }

            /// <summary>
            /// Activa `enableInstancing` en el material la primera vez (los
            /// materiales de la escena ya lo traen activado por construcción: ver
            /// `NewFlatMat` de los bootstrappers). Devuelve false si el material
            /// no lo soporta, para que su lote caiga al camino por objeto.
            /// </summary>
            private static bool EnsureInstancing(Material material)
            {
                if (material.enableInstancing) return true;
                material.enableInstancing = true;
                return material.enableInstancing;
            }
        }
    }
}

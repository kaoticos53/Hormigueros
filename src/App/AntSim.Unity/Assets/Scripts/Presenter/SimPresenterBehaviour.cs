using System;
using UnityEngine;

namespace AntSim.Unity.Scripts.Presenter
{
    /// <summary>
    /// Ancla Unity del presenter (F4.1 — esqueleto). Todo el trabajo real vive en
    /// clases puras (GameStreamParser/GameStreamPresenter): este componente solo
    /// bombea el stream, muestrea el estado interpolado cada frame y lo dibuja
    /// con <c>Graphics.DrawMesh</c> (pool implícito, sin GameObject por hormiga).
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

        /// <summary>Arma los args que consumirá la próxima escena (reinicio/import).</summary>
        public static void ArmPendingArgsForNextScene(string[] args)
            => _pendingArgsForNextScene = args ?? System.Array.Empty<string>();

        /// <summary>Arma el pool que sembrará la próxima escena (import F4.3).</summary>
        public static void ArmSeedPoolForNextScene(string poolPath)
            => _seedPoolForNextScene = poolPath;
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
        public Material ItemMaterial = null!;

        [Header("F5.2a: hojas con mordiscos (CutsLeft/CutsInitial del canal A)")]
        [Tooltip("Material de la hoja (verde oscuro, distinto del ítem simple).")]
        public Material? LeafMaterial;
        [Tooltip("Material de los mordiscos (marrón oscuro, se dibuja en la superficie de la hoja).")]
        public Material? BiteMaterial;

        [Tooltip("Capa de render de ESTA vista (F5.1bis multi-visor): los DrawMesh van a esa capa y la cámara con la máscara correspondiente solo ve su mundo. 0 = Default (escena de una vista, compatible con todo lo anterior). Los suelos/nidos del multi-visor se crean en la MISMA capa, así que la máscara de la cámara completa la separación.")]
        public int RenderLayer = 0;

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
        [Tooltip("Velocidad de simulación/reproducción en rango 1.0× (10% base) a 100.0× (1000% base). 10.0× = base normal. 0 = pausa.")]
        [Range(1f, 100f)] public float Speed = 10f;
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

        private float _pausedSpeed = 10f;

        private readonly Streaming.GameStreamPresenter _presenter = new();
        private Streaming.StreamSource? _source;

        /// <summary>Presenter puro subyacente (F4.2): el HUD lo consume para
        /// repartir el último TickView a tarjetas/toasts/inspector.</summary>
        public Streaming.GameStreamPresenter Presenter => _presenter;
        private float _simTime;           // segundos de sim consumidos
        private const float Dt = 1f / 30f; // tick fijo de la arquitectura
        private volatile bool _streaming;
        private Streaming.RenderState? _lastState; // último estado muestreado (p. ej. para el inspector)

        /// <summary>Indica si el worker en segundo plano está transmitiendo datos.</summary>
        public bool IsStreaming => _streaming;

        /// <summary>Último estado muestreado (poses del tick en curso) — lo consumen
        /// p. ej. el raycast de selección de la tarjeta de inspección.</summary>
        public Streaming.RenderState? CurrentState => _lastState;

        /// <summary>Velocidad efectiva actual de simulación (Speed * SpeedBoost).</summary>
        public float EffectiveSpeed => Speed * SpeedBoost;

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

        /// <summary>Generación evolutiva actual acumulada en la sesión.</summary>
        public static int GenerationCount = 1;

        /// <summary>Récord histórico de supervivencia en segundos.</summary>
        public static float AllTimeBestSurvivalSeconds = 0f;

        /// <summary>Generación actual del presentador.</summary>
        public int Generation => GenerationCount;

        /// <summary>Tiempo de supervivencia transcurrido en la generación actual (s).</summary>
        public float CurrentSurvivalSeconds => _simTime;

        /// <summary>Mejor tiempo histórico de supervivencia (s).</summary>
        public float BestSurvivalSeconds => AllTimeBestSurvivalSeconds;

        /// <summary>
        /// Avanza inmediatamente a la siguiente generación evolutiva: registra el récord
        /// de supervivencia, incrementa la generación y relanza el escenario sembrado con la élite acumulada.
        /// </summary>
        public void TriggerNextGeneration()
        {
            if (_simTime > AllTimeBestSurvivalSeconds)
                AllTimeBestSurvivalSeconds = _simTime;

            GenerationCount++;
            UnityEngine.SceneManagement.SceneManager.LoadScene(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
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

        private void Start()
        {
            // Play-pass: sin esto el bucle del jugador se CONGELA al perder el foco
            // el editor (Application.runInBackground = false por defecto): el
            // Update deja de correr a los ~2 frames y ni el mundo ni el HUD
            // avanzan. Un pass automático (el CLI guiando el editor sin foco) es
            // imposible sin esta línea; para un simulador-espectáculo también es
            // lo deseable.
            Application.runInBackground = true;

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
            // contra el repo root derivado de Application.dataPath.
            string? repoRoot = Streaming.RepoPathResolver.RepoRootFromProjectPath(Application.dataPath);
            string seedPool = SeedPoolPath != null && repoRoot != null
                ? Streaming.RepoPathResolver.Resolve(SeedPoolPath, baseDir: null, repoRoot)
                : SeedPoolPath ?? "";
            _source = new Streaming.StreamSource(CliPath, repoRoot);
            _streaming = true;
            // El stream se bombea en un hilo: el juego no se congela mientras el CLI corre.
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    if (!string.IsNullOrEmpty(ReplayFile))
                    {
                        string replay = repoRoot != null
                            ? Streaming.RepoPathResolver.Resolve(ReplayFile, baseDir: null, repoRoot)
                            : ReplayFile;
                        _source.StreamFile(replay, line => _presenter.Feed(line));
                    }
                    else
                        _source.StreamGame(Seed, Ticks, Grid, Colonies, FrameEvery,
                            seedPool.Length > 0 ? seedPool : null,
                            line => _presenter.Feed(line), PendingDropArgs,
                            PheroEvery, ActivEvery, InspectId, Species);
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
            });
        }

        private void Update()
        {
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

        private void Draw(Streaming.RenderState state)
        {
            float world = WorldSide;
            float antS = EffectiveAntScale;
            float itemS = EffectiveItemScale;
            float lift = ActorLift > 0f ? ActorLift : world * 0.0008f;

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
                foreach (var a in state.Ants)
                {
                    if (!a.Alive) continue;
                    float len = a.HasLoad ? antS * carrierBoost : antS;
                    var scale = new Vector3(len * 0.33f, len * 0.5f, len * 0.16f);
                    var pos = new Vector3(a.X, lift, a.Y);
                    var rot = Quaternion.Euler(90f, -a.Heading * Mathf.Rad2Deg, 0f);

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

                    var mtx = Matrix4x4.TRS(pos, rot, scale);
                    Graphics.DrawMesh(AntMesh, mtx, mat, layer);
                }
            }

            if (ItemMesh != null && ItemMaterial != null)
            {
                // Ítem: esfera natural (1 u de diámetro) escalada por el diámetro
                // querido; el tamaño insinúa la cantidad restante.
                // F5.2a: las hojas (IsLeaf) se dibujan con material verde oscuro
                // y mordiscos visibles — pequeñas esferas marrones en la superficie
                // que representan los cortes consumidos (CutsInitial - CutsLeft).
                int layer = RenderLayer > 0 ? RenderLayer : 0;
                foreach (var it in state.Items)
                {
                    var pos = new Vector3(it.X, lift * 0.7f, it.Y);
                    float s = itemS * (0.55f + 0.03f * it.Amount);

                    if (it.IsLeaf && LeafMaterial != null)
                    {
                        // Hoja: esfera achatada (más plana que un ítem simple)
                        // para insinuar la forma de hoja.
                        var leafScale = new Vector3(s * 1.1f, s * 0.5f, s * 1.1f);
                        var mtx = Matrix4x4.TRS(pos, Quaternion.identity, leafScale);
                        Graphics.DrawMesh(ItemMesh, mtx, LeafMaterial, layer);

                        // Mordiscos: pequeñas esferas marrones en los bordes.
                        // Cada corte consumido (CutsInitial - CutsLeft) se representa
                        // como una muesca en la superficie, distribuida uniformemente
                        // alrededor del perímetro de la hoja.
                        if (BiteMaterial != null)
                        {
                            int bitesEaten = it.CutsInitial - it.CutsLeft;
                            int totalBites = it.CutsInitial > 0 ? it.CutsInitial : 1;
                            float biteRadius = s * 0.18f; // tamaño de cada mordisco
                            float leafRadius = s * 0.55f; // radio de la hoja
                            for (int b = 0; b < bitesEaten; b++)
                            {
                                float angle = (b / (float)totalBites) * Mathf.PI * 2f;
                                float bx = pos.x + Mathf.Cos(angle) * leafRadius;
                                float bz = pos.z + Mathf.Sin(angle) * leafRadius;
                                float by = pos.y + (b % 2 == 0 ? 0.05f : -0.05f); // alternar arriba/abajo
                                var bitePos = new Vector3(bx, by, bz);
                                var biteMtx = Matrix4x4.TRS(bitePos, Quaternion.identity,
                                    Vector3.one * biteRadius);
                                Graphics.DrawMesh(ItemMesh, biteMtx, BiteMaterial, layer);
                            }
                        }
                    }
                    else
                    {
                        // Ítem simple: esfera tal cual.
                        var mtx = Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one * s);
                        Graphics.DrawMesh(ItemMesh, mtx, ItemMaterial, layer);
                    }
                }
            }
        }
    }
}

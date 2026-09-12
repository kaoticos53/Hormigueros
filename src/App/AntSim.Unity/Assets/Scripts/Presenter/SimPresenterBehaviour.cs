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
        public int Ticks = 7200;         // 4 min de sim
        public int Grid = 96;
        public int Colonies = 2;
        public int FrameEvery = 1;
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
        [Header("Replay (debug sin CLI): prioridad sobre StreamGame si no está vacío")]
        [Tooltip("Ruta de un stream volcado a archivo (p. ej. artifacts/stream-fixture-256.jsonl). Vacío = lanza el CLI.")]
        public string? ReplayFile;

        [Header("Render")]
        public Mesh AntMesh;
        public Material AntMaterial;
        public Material CarrierMaterial;
        public Mesh ItemMesh;
        public Material ItemMaterial;
        public float AntScale = 0.6f;

        [Header("Tiempo")]
        [Range(0f, 16f)] public float Speed = 1f; // 0 = pausa

        private readonly Streaming.GameStreamPresenter _presenter = new();
        private Streaming.StreamSource? _source;

        /// <summary>Presenter puro subyacente (F4.2): el HUD lo consume para
        /// repartir el último TickView a tarjetas/toasts/inspector.</summary>
        public Streaming.GameStreamPresenter Presenter => _presenter;
        private float _simTime;           // segundos de sim consumidos
        private const float Dt = 1f / 30f; // tick fijo de la arquitectura
        private bool _streaming;
        private Streaming.RenderState? _lastState; // último estado muestreado (p. ej. para el inspector)

        /// <summary>Último estado muestreado (poses del tick en curso) — lo consumen
        /// p. ej. el raycast de selección de la tarjeta de inspección.</summary>
        public Streaming.RenderState? CurrentState => _lastState;

        private void Start()
        {
            // Play-pass: sin esto el bucle del jugador se CONGELA al perder el foco
            // el editor (Application.runInBackground = false por defecto): el
            // Update deja de correr a los ~2 frames y ni el mundo ni el HUD
            // avanzan. Un pass automático (el CLI guiando el editor sin foco) es
            // imposible sin esta línea; para un simulador-espectáculo también es
            // lo deseable.
            Application.runInBackground = true;

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
                            PheroEvery, ActivEvery, InspectId);
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
            if (Speed <= 0f) return; // pausa: no consume stream
            _simTime += Time.deltaTime * Speed;

            // Dibuja el último tick muestreado (la interpolación vive en Sample).
            var state = _presenter.Sample(Frac());
            _lastState = state;
            Draw(state);
        }

        private float Frac()
        {
            float t = _simTime / Dt;
            return t - Mathf.Floor(t);
        }

        private void Draw(Streaming.RenderState state)
        {
            if (AntMesh != null && AntMaterial != null)
            {
                foreach (var a in state.Ants)
                {
                    var pos = new Vector3(a.X, 0f, a.Y);
                    var rot = Quaternion.Euler(0f, -a.Heading * Mathf.Rad2Deg, 0f);
                    var mat = a.HasLoad ? CarrierMaterial : AntMaterial;
                    if (mat == null) mat = AntMaterial;
                    var mtx = Matrix4x4.TRS(pos, rot, Vector3.one * AntScale);
                    Graphics.DrawMesh(AntMesh, mtx, mat, 0);
                }
            }

            if (ItemMesh != null && ItemMaterial != null)
            {
                foreach (var it in state.Items)
                {
                    var pos = new Vector3(it.X, 0f, it.Y);
                    var mtx = Matrix4x4.TRS(pos, Quaternion.identity,
                        Vector3.one * (0.4f + 0.06f * it.Amount));
                    Graphics.DrawMesh(ItemMesh, mtx, ItemMaterial, 0);
                }
            }

            // Los nidos ( colonies ) los dibuja un marker estático por colonia — v1.
        }
    }
}

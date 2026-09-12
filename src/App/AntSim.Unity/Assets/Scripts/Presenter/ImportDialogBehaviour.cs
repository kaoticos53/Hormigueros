using UnityEngine;

namespace AntSim.Unity.Scripts.Presenter
{
    /// <summary>
    /// Diálogo de importación (F4.3) — wire-up FINO sobre el modelo puro
    /// <see cref="Streaming.ImportDialogModel"/>. Flujo: el jugador da una ruta
    /// de .antgenome (campo de texto en v1; drag&drop cuando la escena tenga
    /// EventSystem de archivos) → este componente consulta el oráculo
    /// (<c>antsim --mode genome-info --import f</c>) → el HUD muestra la
    /// tarjeta de cuarentena → «Confirmar» siembra la partida (asigna
    /// <c>SimPresenterBehaviour.SeedPoolPath</c> y recarga la escena; el
    /// arranque con <c>--seed-pool</c> es el mismo camino del picker).
    /// </summary>
    public sealed class ImportDialogBehaviour : MonoBehaviour
    {
        [Tooltip("Presenter al que se asigna el pool confirmado.")]
        public SimPresenterBehaviour? Presenter;

        [Tooltip("Ruta del CLI (la misma del presenter).")]
        public string CliPath = "build/antsim";

        [Tooltip("Ruta .antgenome propuesta por el jugador (v1: campo de texto).")]
        public string GenomePath = "";

        /// <summary>Modelo puro del diálogo (tests y HUDs alternativos).</summary>
        public readonly Streaming.ImportDialogModel Model = new();

        /// <summary>Texto a renderizar en el HUD (tarjeta + reglas o error).</summary>
        public string? DialogText { get; private set; }

        /// <summary>Inspecciona la ruta propuesta vía el oráculo. Bloqueante
        /// (proceso corto); el flujo del diálogo es igual de simple.</summary>
        public void Inspect()
        {
            if (string.IsNullOrEmpty(GenomePath))
            {
                DialogText = "✗ indica la ruta de un .antgenome";
                return;
            }
            // Play-pass: misma resolución de rutas que el presenter — el CLI
            // relativo al repo root y el .antgenome también (cwd del editor ≠ repo).
            string? repoRoot = Streaming.RepoPathResolver.RepoRootFromProjectPath(
                UnityEngine.Application.dataPath);
            var source = new Streaming.StreamSource(CliPath, repoRoot);
            string importPath = Streaming.RepoPathResolver.Resolve(GenomePath, baseDir: null, repoRoot);
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = source.CliPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("--mode"); psi.ArgumentList.Add("genome-info");
            psi.ArgumentList.Add("--import"); psi.ArgumentList.Add(importPath);

            string json;
            using (var proc = System.Diagnostics.Process.Start(psi)!)
            {
                json = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit();
            }

            string? card = Model.Inspect(GenomePath, json);
            DialogText = card ?? $"✗ {Model.Error}";
        }

        /// <summary>Confirmar: siembra la próxima partida con el pool. Asigna el
        /// SeedPoolPath y recarga la escena (Start() relanza el CLI sembrado).</summary>
        public void Confirm()
        {
            string? path = Model.Confirm();
            if (path == null || Presenter == null) return;
            Presenter.SeedPoolPath = path;
            UnityEngine.SceneManagement.SceneManager.LoadScene(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
        }

        /// <summary>Cancelar: no toca nada (0 riesgo).</summary>
        public void Cancel() => Model.Cancel();

        private void Update()
        {
            // El texto del HUD se refresca aquí (mismo patrón que HudLayout).
            string? t = Model.CurrentPhase == Streaming.ImportDialogModel.Phase.Reviewing
                ? Model.RenderDialog()
                : DialogText;
            if (t != null && t != _lastText)
            {
                _lastText = t;
                OnDialogChanged?.Invoke(t);
            }
        }

        private string? _lastText;

        /// <summary>Hook para la UI (el bootstrapper conecta un setter de Text).</summary>
        public event System.Action<string>? OnDialogChanged;
    }
}

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

        [Tooltip("Ruta .antgenome propuesta por el jugador (la escribe el campo del modal).")]
        public string GenomePath = "";

        [Tooltip("Tecla que abre/cierra el modal de importación (F5.1). Sin esto el " +
                 "modal no tenía entrada: era UI inalcanzable.")]
        public KeyCode OpenKey = KeyCode.I;

        /// <summary>Modelo puro del diálogo (tests y HUDs alternativos).</summary>
        public readonly Streaming.ImportDialogModel Model = new();

        /// <summary>Texto a renderizar en el HUD (tarjeta + reglas o error).</summary>
        public string? DialogText { get; private set; }

        /// <summary>Abre/cierra el modal (F5.1). La visibilidad real la pinta el
        /// HUD desde <see cref="Streaming.ImportDialogModel.ModalVisible"/>.</summary>
        public void ToggleOpen()
        {
            if (Model.ModalVisible) Close(); else Open();
        }

        /// <summary>Muestra el modal con el campo de ruta listo para escribir.</summary>
        public void Open()
        {
            Model.Open();
            DialogText = null;
            OnDialogChanged?.Invoke(Model.RenderDialog() ?? "");
        }

        /// <summary>Cierra el modal sin confirmar (0 riesgo, contrato §2.1).</summary>
        public void Close()
        {
            Model.Cancel();
            DialogText = null;
        }

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
            // La escena se recarga para arrancar la partida sembrada: el pool tiene
            // que viajar a la PRÓXIMA instancia del presenter (esta se destruye), o
            // el botón aceptaría el genoma y arrancaría una partida sin sembrar.
            SimPresenterBehaviour.ArmSeedPoolForNextScene(path);
            UnityEngine.SceneManagement.SceneManager.LoadScene(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
        }

        /// <summary>Cancelar: no toca nada (0 riesgo).</summary>
        public void Cancel() => Model.Cancel();

        private void Update()
        {
            // F5.1: entrada de teclado del modal. Si el jugador está escribiendo
            // la ruta en el campo, no se secuestra la tecla (el campo tiene el foco
            // y `I` debe poder teclearse dentro de una ruta).
            if (Input.GetKeyDown(OpenKey) && !FieldFocused)
                ToggleOpen();

            // El texto del HUD se refresca aquí (mismo patrón que HudLayout).
            string? t = Model.ModalVisible ? Model.RenderDialog() : DialogText;
            if (t != null && t != _lastText)
            {
                _lastText = t;
                OnDialogChanged?.Invoke(t);
            }
        }

        private string? _lastText;

        /// <summary>¿El foco está en un campo de texto (el jugador está tecleando)?</summary>
        private static bool FieldFocused
        {
            get
            {
                var es = UnityEngine.EventSystems.EventSystem.current;
                return es != null && es.currentSelectedGameObject != null
                    && es.currentSelectedGameObject.GetComponent<UnityEngine.UI.InputField>() != null;
            }
        }

        // ─── F5.1 deuda: drag & drop de .antgenome ─────────────────────
        // Unity expone los eventos de drag via OnGUI (IMGUI layer), incluso
        // en escenas uGUI. Cuando el jugador arrastra un archivo .antgenome
        // sobre la ventana, se acepta el drag y al soltar se carga la ruta
        // en el campo de texto + se lanza Inspect() automáticamente.
        // Si el modal NO está abierto, se abre primero (el jugador puede
        // soltar en cualquier momento, no solo dentro del modal).

        /// <summary>¿Hay un archivo .antgenome arrastrándose sobre la ventana?</summary>
        public bool IsDragHovering { get; private set; }

        private void OnGUI()
        {
            var e = Event.current;
            if (e == null) return;

            switch (e.type)
            {
                case EventType.DragUpdated:
                {
                    // ¿El drag contiene al menos un .antgenome?
                    if (DragAndDrop.objectReferences.Length > 0)
                    {
                        bool hasGenome = false;
                        foreach (var obj in DragAndDrop.objectReferences)
                        {
                            if (obj is UnityEngine.TextAsset ta != null && ta.name.EndsWith(".antgenome"))
                            {
                                hasGenome = true; break;
                            }
                        }
                        // También acepta paths de arrastre del explorador
                        // (cuando Unity no puede resolver el asset)
                        if (!hasGenome && DragAndDrop.paths.Length > 0)
                        {
                            foreach (var p in DragAndDrop.paths)
                            {
                                if (p != null && p.EndsWith(".antgenome", System.StringComparison.OrdinalIgnoreCase))
                                {
                                    hasGenome = true; break;
                                }
                            }
                        }

                        if (hasGenome)
                        {
                            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                            IsDragHovering = true;
                            e.Use();
                        }
                    }
                    break;
                }

                case EventType.DragPerform:
                {
                    DragAndDrop.AcceptDrag();
                    IsDragHovering = false;

                    string? droppedPath = null;

                    // Prioridad 1: paths del explorador (arrastre desde el filesystem)
                    if (DragAndDrop.paths.Length > 0)
                    {
                        foreach (var p in DragAndDrop.paths)
                        {
                            if (p != null && p.EndsWith(".antgenome", System.StringComparison.OrdinalIgnoreCase))
                            {
                                droppedPath = p; break;
                            }
                        }
                    }

                    // Prioridad 2: TextAsset del proyecto Unity
                    if (droppedPath == null)
                    {
                        foreach (var obj in DragAndDrop.objectReferences)
                        {
                            if (obj is TextAsset ta && ta.name.EndsWith(".antgenome"))
                            {
                                // En el editor, la ruta del asset es relativa al proyecto
                                droppedPath = UnityEditor.AssetDatabase.GetAssetPath(ta);
                                break;
                            }
                        }
                    }

                    if (droppedPath != null)
                    {
                        GenomePath = droppedPath;
                        // Abrir el modal si no estaba abierto
                        if (!Model.ModalVisible) Open();
                        // Auto-inspeccionar el archivo soltado
                        Inspect();
                        Debug.Log($"[ImportDialog] .antgenome arrastrado: {droppedPath}");
                    }

                    e.Use();
                    break;
                }

                case EventType.DragExited:
                {
                    if (IsDragHovering)
                    {
                        IsDragHovering = false;
                        e.Use();
                    }
                    break;
                }
            }
        }

        /// <summary>Hook para la UI (el bootstrapper conecta un setter de Text).</summary>
        public event System.Action<string>? OnDialogChanged;
    }
}

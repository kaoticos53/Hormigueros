#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using AntSim.Unity.Scripts.Streaming;

namespace AntSim.Unity.Scripts.EditorTools
{
    /// <summary>
    /// EL CIERRE DE UNA ESCRITURA DEL GENERADOR: lo que el bootstrapper acaba de
    /// escribir queda en forma CANÓNICA, sin depender de que el Play pass lo
    /// arregle después.
    ///
    /// POR QUÉ EXISTE. El montaje se escribe con la API de Unity —`CreateAsset`
    /// para materiales, texturas y mallas, `SaveScene` para la escena— y Unity
    /// numera los objetos con un contador de base ALEATORIA por sesión, así que
    /// el MISMO montaje salía con 17 assets de bytes distintos en cada pasada
    /// (16 `.mat` y `Game.unity`). El ruido lo quitaba el Play pass al final
    /// (`scripts/unity-canonicalize-assets.py`), o sea que los assets estaban
    /// bien por cortesía de otro paso; aquí el generador cierra lo suyo.
    ///
    /// QUÉ HACE, y en este orden:
    ///   1. `AssetDatabase.SaveAssets()`: los materiales, mallas y texturas se
    ///      escriben con `CreateAsset` y la escena con `SaveScene` (el llamador la
    ///      guarda antes de llegar aquí), pero hay que asegurar que TODO está en
    ///      disco antes de mirarlo.
    ///   2. Corre el canonicalizador del repo —la única definición de la forma
    ///      canónica; ver <see cref="AssetCanonicalizer"/>— sobre el proyecto.
    ///      Su salida va al log del editor línea a línea: los `REESCRITO` dicen
    ///      qué se estabilizó y los `DERIVARÍA` qué no.
    ///   3. Falla CERRADO si algún asset derivaría o si la herramienta no pudo dar
    ///      veredicto: un generador que deja el árbol con anclas de sesión está
    ///      roto, y el error tiene que salir aquí, no tres pasos después.
    ///   4. Recarga la escena si es la que está abierta. Es lo que hace que un
    ///      guardado POSTERIOR del editor (el Play pass guarda la escena justo
    ///      después de configurar el presenter) escriba las anclas canónicas en
    ///      vez de las de sesión que tiene en memoria: sin esto, el paso 2 se
    ///      deshace solo y la pasada tendría que volver a arreglarlo.
    ///
    /// «No medido» no es «roto»: si no hay Python 3, o la herramienta, o el ancla
    /// del repo, esto AVISA (y el Play pass lo comprobará igual). Con
    /// `ANTSIM_CANONICALIZE=0` no hace nada, que es como se mide el antes/después
    /// de este paso.
    /// </summary>
    internal static class GeneratedAssets
    {
        /// <summary>Cierra una escritura del generador sobre <paramref name="scenePath"/>
        /// (ruta relativa al proyecto: `Assets/Scenes/Game.unity`).</summary>
        public static void CloseWithCanonicalForm(string scenePath)
        {
            if (!AssetCanonicalizer.Enabled(Environment.GetEnvironmentVariable(AssetCanonicalizer.SwitchEnvVar)))
            {
                Debug.Log($"[GeneratedAssets] canonicalización saltada ({AssetCanonicalizer.SwitchEnvVar}=0): " +
                          "los assets pueden quedar con anclas de sesión.");
                return;
            }

            // 1. Todo escrito, antes de mirarlo.
            AssetDatabase.SaveAssets();

            string project = ProjectPath();
            string? repo = RepoPathResolver.RepoRootFromDataPath(Application.dataPath);
            var resultado = AssetCanonicalizer.Run(repo, project, check: false);

            foreach (string linea in resultado.Lines()) Debug.Log("[canónico] " + linea);

            switch (resultado.Outcome)
            {
                case CanonicalizeOutcome.Canonical:
                    Debug.Log("[GeneratedAssets] los assets generados quedaron en forma canónica " +
                              "(anclas derivadas del contenido).");
                    break;

                case CanonicalizeOutcome.Derived:
                case CanonicalizeOutcome.NotAnalyzable:
                    throw new InvalidOperationException(
                        "[GeneratedAssets] los assets generados NO quedaron en forma canónica " +
                        $"({resultado.Outcome}): {resultado.Detail}\n" +
                        $"  el veredicto es de {AssetCanonicalizer.ScriptRelativePath}; el generador tiene " +
                        "que escribir canónico, no dejarlo para el Play pass.\n" +
                        "  si el árbol no viene de este generador, canonicaliza a mano " +
                        "(python scripts/unity-canonicalize-assets.py) y repite.");

                default:
                    Debug.LogWarning(
                        "[GeneratedAssets] no se pudo comprobar la forma canónica: " + resultado.Detail +
                        ". Los assets pueden quedar con anclas de sesión — el veredicto no se dio, no es que " +
                        "estén bien. " + (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(
                            AssetCanonicalizer.PythonEnvVar))
                            ? $"Si tu Python 3 no está en el PATH del editor, apúntalo en {AssetCanonicalizer.PythonEnvVar}."
                            : "Revisa el valor de " + AssetCanonicalizer.PythonEnvVar + "."));
                    break;
            }

            // 4. El editor, al fichero canónico: si no, el próximo guardado de la
            //    escena (el Play pass guarda después) reintroduce las anclas de
            //    sesión que tiene en memoria.
            ReloadSceneIfOpen(scenePath);
        }

        /// <summary>La carpeta del proyecto Unity (Application.dataPath es su `Assets`).</summary>
        private static string ProjectPath()
            => Directory.GetParent(Application.dataPath)!.FullName;

        /// <summary>Recarga la escena si la canonicalización acaba de reescribir la
        /// que está abierta. No hace nada si el editor tiene otra escena (el
        /// generador no es dueño de lo que el usuario tenga abierto).</summary>
        private static void ReloadSceneIfOpen(string scenePath)
        {
            if (string.IsNullOrEmpty(scenePath)) return;

            Scene activa = SceneManager.GetActiveScene();
            if (!string.Equals(activa.path, scenePath, StringComparison.OrdinalIgnoreCase)) return;
            if (!File.Exists(Path.Combine(ProjectPath(), scenePath.Replace('/', Path.DirectorySeparatorChar))))
            {
                Debug.LogWarning($"[GeneratedAssets] la escena {scenePath} no está en disco: no la recargo.");
                return;
            }

            // Se acaba de guardar: no hay nada sin guardar que perder. OpenScene no
            // pregunta, así que esto tampoco puede quedarse esperando en batch.
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            Debug.Log($"[GeneratedAssets] {scenePath} recargada desde el fichero canónico: " +
                      "un guardado posterior escribe las anclas canónicas, no las de sesión.");
        }
    }
}
#endif

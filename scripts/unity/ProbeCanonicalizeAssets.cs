#if UNITY_EDITOR
using System;
using System.IO;
using AntSim.Unity.Scripts.Streaming;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Probe del Play pass: CIERRA LA FORMA de lo que acaba de guardar el pase, desde
/// DENTRO del editor.
///
/// POR QUÉ. El pase guarda la escena con `save_scene` —esa configuración es la
/// que se COMMITEA en `Game.unity`— y a quien escribe no le basta con
/// canonicalizar desde fuera: Unity numera las anclas con un contador de base
/// aleatoria por sesión, así que un `save_scene` sobre una escena YA canónica la
/// deja DERIVANDO sin cambiarle el contenido (medido 2026-10-10: 425 documentos,
/// sólo anclas y orden). Y escribirla desde FUERA, con la escena abierta, es lo
/// que levanta el modal «The open scene(s) have been modified externally» y deja
/// el pipeline sin respuesta (medido: `Main thread operation timed out after
/// 30000ms`, y el editor entero sordo a los comandos).
///
/// QUÉ HACE, y en este orden —el mismo que `GeneratedAssets` para la escritura
/// del generador, ahora para la del pase:
///   1. `AssetDatabase.SaveAssets()`: nada se mira sin estar en disco.
///   2. El canonicalizador del repo (`AssetCanonicalizer`, la única definición de
///      la forma canónica) en modo escritura.
///   3. Si no da veredicto bueno, NO se sigue: `error|…`.
///   4. La escena, del fichero canónico a la memoria. Sin esto, el siguiente
///      guardado del editor repone las anclas de sesión que tiene en memoria y
///      el paso 2 se deshace solo.
///
/// Devuelve una línea: `ok|outcome=…|detalle=…|escena=recargada|no-abierta`, o
/// `aviso|…` cuando no se pudo medir (sin Python 3, sin la herramienta: «no
/// medido» no es «roto», el mismo criterio que las puertas del pre-push), o
/// `error|…` cuando la herramienta corrió y dijo que algún asset deriva.
/// NO vive bajo Assets/: es herramienta del repo.
/// </summary>
public static class ProbeCanonicalizeAssets
{
    /// <summary>La escena del pase (relativa al proyecto): la que guarda
    /// `save_scene` y la que hay que recargar para que un guardado posterior no
    /// reponga las anclas de sesión.</summary>
    private const string GameScene = "Assets/Scenes/Game.unity";

    public static string Run()
    {
        if (!AssetCanonicalizer.Enabled(Environment.GetEnvironmentVariable(AssetCanonicalizer.SwitchEnvVar)))
        {
            return "aviso|saltado (" + AssetCanonicalizer.SwitchEnvVar + "=0): los assets pueden quedar con anclas de sesión";
        }

        AssetDatabase.SaveAssets();

        string project = Directory.GetParent(Application.dataPath)!.FullName;
        string? repo = RepoPathResolver.RepoRootFromDataPath(Application.dataPath);

        CanonicalizeResult r;
        try
        {
            r = AssetCanonicalizer.Run(repo, project, check: false);
        }
        catch (Exception ex)
        {
            return "error|el canonicalizador lanzó " + ex.GetType().Name + ": " + ex.Message;
        }

        // La salida entera al log del editor: los `REESCRITO` dicen qué se
        // estabilizó y los `DERIVARÍA` qué no. El veredicto es de la herramienta.
        foreach (string linea in r.Lines()) Debug.Log("[canónico] " + linea);

        if (!r.Measured)
            return "aviso|no se pudo medir: " + r.Detail + " (" + AssetCanonicalizer.ScriptRelativePath + ")";

        if (!r.Ok)
            return "error|outcome=" + r.Outcome + "|" + r.Detail;

        string escena = ReloadSceneIfOpen() ? "recargada" : "no-abierta";
        return "ok|outcome=" + r.Outcome + "|detalle=" + r.Detail + "|escena=" + escena;
    }

    /// <summary>Recarga la escena del pase si es la que está abierta, para que el
    /// editor quede mirando el fichero canónico y no el que tenía en memoria. No
    /// toca nada si el editor tiene otra escena: el pase no es dueño de lo que
    /// haya abierto el usuario.</summary>
    private static bool ReloadSceneIfOpen()
    {
        Scene activa = SceneManager.GetActiveScene();
        if (!string.Equals(activa.path, GameScene, StringComparison.OrdinalIgnoreCase)) return false;

        string project = Directory.GetParent(Application.dataPath)!.FullName;
        if (!File.Exists(Path.Combine(project, GameScene.Replace('/', Path.DirectorySeparatorChar))))
        {
            Debug.LogWarning("[ProbeCanonicalizeAssets] " + GameScene + " no está en disco: no la recargo.");
            return false;
        }

        // Se acaba de cerrar la escritura: no hay nada sin guardar que perder.
        EditorSceneManager.OpenScene(GameScene, OpenSceneMode.Single);
        Debug.Log("[ProbeCanonicalizeAssets] " + GameScene + " recargada del fichero canónico: " +
                  "un guardado posterior escribe las anclas canónicas, no las de sesión.");
        return true;
    }
}
#endif

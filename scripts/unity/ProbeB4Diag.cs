using System.IO;
using System.Text;
using AntSim.Unity.Scripts.Presenter;
using UnityEngine;

/// <summary>Diagnóstico puntual del bloque 4: ¿recargó la escena el reinicio?</summary>
public static class ProbeB4Diag
{
    public static string Run()
    {
        var sb = new StringBuilder("DIAG");
        sb.Append("|playing=").Append(Application.isPlaying ? 1 : 0);
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        sb.Append("|scene=").Append(scene.name);
        sb.Append("|path=").Append(scene.path);
        sb.Append("|buildIndex=").Append(scene.buildIndex.ToString());

        var scenes = UnityEditor.EditorBuildSettings.scenes;
        sb.Append("|buildScenes=").Append(scenes.Length.ToString());
        var names = new StringBuilder();
        foreach (var s in scenes)
        {
            if (names.Length > 0) names.Append('/');
            names.Append(Path.GetFileName(s.path)).Append(s.enabled ? "" : "(off)");
        }
        sb.Append("|buildList=").Append(names.Length > 0 ? names.ToString() : "-");

        var hud = Object.FindAnyObjectByType<HudLayoutBehaviour>();
        var presenter = hud != null ? hud.Presenter : null;
        sb.Append("|tick=").Append(presenter != null
            ? (presenter.Presenter.CurrentTick?.Tick ?? 0UL).ToString() : "-");
        sb.Append("|finalTick=").Append(presenter != null
            ? presenter.Presenter.FinalTick.ToString() : "-");
        sb.Append("|rows=").Append(hud != null ? hud.History.Rows.Count.ToString() : "-");
        sb.Append("|toasts=").Append(hud != null ? hud.Toasts.Active.Count.ToString() : "-");
        sb.Append("|speed=").Append(presenter != null
            ? presenter.Speed.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) : "-");
        sb.Append("|pending=").Append(presenter != null ? presenter.PendingDropArgs.Length.ToString() : "-");
        sb.Append("|seedPool=").Append(presenter != null ? (presenter.SeedPoolPath ?? "-") : "-");
        sb.Append("|buffer=").Append(presenter != null ? presenter.Presenter.BufferedTicks.ToString() : "-");
        return sb.ToString();
    }
}

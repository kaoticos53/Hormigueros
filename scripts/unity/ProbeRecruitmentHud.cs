#nullable enable
using System;
using System.Globalization;
using System.IO;
using System.Text;
using AntSim.Unity.Scripts.Presenter;
using AntSim.Unity.Scripts.Streaming;
using UnityEngine;

/// <summary>
/// Sonda del HUD de reclutamiento EN LA PARTIDA VIVA: ¿las dos líneas que el HUD
/// publica dicen lo mismo que hay en el tablero?
///
/// POR QUÉ. La línea de configuración y la de cifras del tablero son texto: la
/// suite fija que caben y que el modelo cuenta bien, pero nada comprueba que el
/// HUD VIVO las pinte, ni que la cifra de celdas coincida con lo que el quad
/// está pintando. Esta sonda lee los dos `Text` reales, los contrasta con el
/// modelo del render y con el estado presentado, y guarda el frame completo en
/// `Temp/antsim-hud-recruit.png` (la captura que se mira).
///
/// No vive bajo Assets/: es herramienta del repo.
public static class ProbeRecruitmentHud
{
    public static string Run()
    {
        var sb = new StringBuilder("HUD");
        var presenter = UnityEngine.Object.FindAnyObjectByType<SimPresenterBehaviour>();
        if (presenter == null) return sb.Append("|error=sin-presenter").ToString();
        var hud = UnityEngine.Object.FindAnyObjectByType<HudLayoutBehaviour>();
        if (hud == null) return sb.Append("|error=sin-hud").ToString();

        string config = hud.RecruitmentText != null ? hud.RecruitmentText.text : "<sin-texto>";
        string live = hud.RecruitmentLiveText != null ? hud.RecruitmentLiveText.text : "<sin-texto>";
        sb.Append("|config=").Append(config)
          .Append("|live=").Append(live)
          .Append("|lineaViva=").Append(live.Length > 0 ? 1 : 0);

        // — Lo que el modelo del HUD debería estar publicando —
        var model = hud.RecruitmentLive;
        sb.Append("|modelo=").Append(model.Items).Append("i/").Append(model.Piles).Append("m/")
          .Append(model.TrailCells).Append("c/").Append(model.TrailMax)
          .Append("/banda=").Append(model.BandWidthUnits).Append('x').Append(model.BandHeightUnits);

        var state = presenter.CurrentState;
        if (state != null)
        {
            int adults = 0;
            foreach (var c in state.Colonies) adults += c.Adults;
            sb.Append("|tick=").Append(state.Tick)
              .Append("|items=").Append(state.Items.Count)
              .Append("|adults=").Append(adults);
        }

        // — Lo que el quad pinta de verdad (misma curva que el render) —
        var phero = UnityEngine.Object.FindAnyObjectByType<PheromoneTileBehaviour>();
        if (phero != null)
        {
            var grid = phero.Model;
            byte max = PheromoneRamp.LayerMax(grid.Cells);
            int painted = 0, nonzero = 0;
            foreach (byte v in grid.Cells)
            {
                if (v == 0) continue;
                nonzero++;
                if (PheromoneRamp.AlphaByte(v, max) > 0) painted++;
            }
            sb.Append("|capa=").Append(phero.LayerLabel)
              .Append("|cuadricula=").Append(grid.Width).Append('x').Append(grid.Height)
              .Append("|datos=").Append(nonzero).Append("/").Append(max)
              .Append("|pintadas=").Append(painted);
        }

        SaveFrame(sb);
        return sb.ToString();
    }

    private static void SaveFrame(StringBuilder sb)
    {
        Texture2D? frame = null;
        try { frame = ScreenCapture.CaptureScreenshotAsTexture(); }
        catch (Exception) { }
        if (frame == null)
        {
            sb.Append("|error=sin-captura");
            return;
        }
        try
        {
            string path = Path.Combine(
                Path.Combine(Application.dataPath, ".."), Path.Combine("Temp", "antsim-hud-recruit.png"));
            File.WriteAllBytes(path, frame.EncodeToPNG());
            sb.Append("|frame=").Append(frame.width).Append('x').Append(frame.height);
        }
        catch (Exception e)
        {
            sb.Append("|error=captura:").Append(e.Message);
        }
        UnityEngine.Object.Destroy(frame);
    }

    private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}

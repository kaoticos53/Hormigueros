#nullable enable
using System.Text;
using System.Text.RegularExpressions;
using AntSim.Unity.Scripts.Presenter;
using AntSim.Unity.Scripts.Streaming;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Sonda del TECHO DE NODRIZAS en el HUD VIVO: ¿alguna tarjeta pinta más pupas de
/// las que el mundo puede sostener?
///
/// POR QUÉ. La puesta de la ronda se para cuando la cola de cría cubre lo que
/// crían las nodrizas (`adultas · NurseShare`), así que `pupae` tiene un techo
/// duro: 6 con la tropa llena. El HUD pinta el número del stream tal cual, sin
/// recortarlo, y por eso el techo se puede vigilar desde fuera. La suite ya fija
/// el lado del Core (`NurseCeilingTests`), pero eso no prueba lo que la PARTIDA
/// enseña: entre el mundo y el píxel están el stream, el parser, el modelo de la
/// tarjeta y el `Text` que lo pinta.
///
/// QUÉ HACE. Lee los `Text` de las tarjetas, saca `colonia N`, `adultas/40` y
/// `pupas N` del texto REAL, y compara con `NurseCeilingModel.Hard` (el espejo
/// del Core, fijado por prueba). Devuelve una línea:
///
///   HUDPUPAE|tick=104542|techo=6|c0=40/5/ok|c1=40/6/ok|pico=6|excede=0
///
/// `excede=1` si alguna tarjeta pasa el techo. La sonda NO decide: informa. El
/// guard `scripts/check-hud-pupae.sh` muestrea, acumula y devuelve el código de
/// salida — una sola foto no puede afirmar «nunca».
///
/// No vive bajo Assets/: es herramienta del repo (se ejecuta con
/// `unity command run_script --file`).
public static class ProbeHudPupae
{
    private static readonly Regex ColonyRe = new Regex("colonia ([0-9]+)");
    private static readonly Regex PupaeRe = new Regex("pupas ([0-9]+)");
    private static readonly Regex AdultsRe =
        new Regex(" ([0-9]+)/" + NurseCeilingModel.MaxAdults.ToString());

    public static string Run()
    {
        var presenter = UnityEngine.Object.FindAnyObjectByType<SimPresenterBehaviour>();
        if (presenter == null) return "HUDPUPAE|error=sin-presenter";

        var state = presenter.CurrentState;
        var sb = new StringBuilder("HUDPUPAE");
        sb.Append("|tick=").Append(state != null ? ((int)state.Tick).ToString() : "-1");
        sb.Append("|techo=").Append(NurseCeilingModel.Hard);

        int techo = NurseCeilingModel.Hard;
        int tarjetas = 0, pico = 0, excede = 0;

        foreach (var text in UnityEngine.Object.FindObjectsByType<Text>(FindObjectsInactive.Include))
        {
            if (text.text == null || !text.text.Contains("pupas")) continue;

            var mc = ColonyRe.Match(text.text);
            var mp = PupaeRe.Match(text.text);
            if (!mc.Success || !mp.Success) continue;

            var ma = AdultsRe.Match(text.text);
            int adultas = ma.Success ? int.Parse(ma.Groups[1].Value) : -1;
            int pupas = int.Parse(mp.Groups[1].Value);
            bool pasa = pupas > techo;

            tarjetas++;
            pico = Mathf.Max(pico, pupas);
            if (pasa) excede = 1;

            sb.Append("|c").Append(mc.Groups[1].Value)
              .Append('=').Append(adultas).Append('/').Append(pupas)
              .Append(pasa ? "/EXCEDE" : "/ok");
        }

        if (tarjetas == 0) sb.Append("|error=sin-tarjetas");
        sb.Append("|pico=").Append(pico).Append("|excede=").Append(excede);
        return sb.ToString();
    }
}

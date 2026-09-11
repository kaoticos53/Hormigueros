using System;
using System.Globalization;
using System.Text;
using AntSim.Core.Telemetry;

namespace AntSim.Core.Scenario;

/// <summary>
/// Escenario de presets (Fase 4): emite las tarjetas canónicas del selector de
/// pools en dos formatos — texto legible (la tarjeta tal cual debe mostrar el
/// HUD) y JSON por preset (los datos estructurados para la UI). Determinista
/// byte a byte: la salida es función pura de <see cref="PoolPresets"/>, así el
/// equipo Unity puede diffarla contra lo que su UI renderiza — cualquier
/// desviación es un bug de la UI, no de los datos.
/// </summary>
public static class PresetScenario
{
    /// <summary>
    /// Tarjetas canónicas. Con <paramref name="json"/> false: una línea por
    /// preset (texto de tarjeta); con true: un objeto JSON por preset con los
    /// campos estructurados.
    /// </summary>
    public static string RenderCards(bool json = false)
    {
        var sb = new StringBuilder();

        if (!json)
        {
            sb.Append("== presets del selector de pools (benchmark Fase 3ter; docs/fase4-diseno-ux.md) ==")
              .AppendLine();
        }
        else
        {
            sb.Append("{\"mode\":\"presets\",\"presets\":[").AppendLine();
        }

        for (int i = 0; i < PoolPresets.All.Count; i++)
        {
            var p = PoolPresets.All[i];
            if (json)
            {
                if (i > 0) sb.Append(',').AppendLine();
                var b = p.Benchmark;
                sb.Append("{\"id\":\"").Append(p.Id).Append('"')
                  .Append(",\"displayName\":\"").Append(JsonEscape(p.DisplayName)).Append('"')
                  .Append(",\"tagline\":\"").Append(JsonEscape(p.Tagline)).Append('"')
                  .Append(",\"genomeFile\":").Append(p.GenomeFile != null
                      ? "\"" + JsonEscape(p.GenomeFile) + "\"" : "null")
                  .Append(",\"band\":\"").Append(JsonEscape(p.Band)).Append('"')
                  .Append(",\"benchmark\":{\"pickups\":").Append(b.Pickups)
                  .Append(",\"unloads\":").Append(b.Unloads)
                  .Append(",\"seedsWithUnload\":").Append(b.SeedsWithUnload)
                  .Append(",\"seedsTotal\":").Append(b.SeedsTotal)
                  .Append(",\"dropAvg\":").Append(Num(b.DropAvg))
                  .Append(",\"carryLegMean\":").Append(Num(b.CarryLegMean))
                  .Append('}');
                if (p.GameMode is var g && g.HasValue)
                {
                    sb.Append(",\"gameMode\":{\"seedsWithUnload\":").Append(g.Value.SeedsWithUnload)
                      .Append(",\"seedsTotal\":").Append(g.Value.SeedsTotal)
                      .Append(",\"unloads\":").Append(g.Value.Unloads)
                      .Append('}');
                }
                else
                {
                    sb.Append(",\"gameMode\":null");
                }
                sb.Append(",\"sourceDoc\":\"").Append(JsonEscape(p.SourceDoc)).Append('"')
                  .Append(",\"reproCommand\":\"").Append(JsonEscape(p.ReproCommand)).Append('"')
                  .Append(",\"card\":\"").Append(JsonEscape(PoolPresets.FormatCard(p))).Append('"')
                  .Append('}');
            }
            else
            {
                sb.Append(PoolPresets.FormatCard(p)).AppendLine();
                sb.Append("   fuente: ").Append(p.SourceDoc).AppendLine();
                sb.Append("   repro:  ").Append(p.ReproCommand).AppendLine();
            }
        }

        if (json)
            sb.AppendLine().Append("]}").AppendLine();

        return sb.ToString();
    }

    /// <summary>Float o null en formato canónico (punto, cultura invariante).</summary>
    private static string Num(float? v)
        => v is float f ? f.ToString("0.0", CultureInfo.InvariantCulture) : "null";

    private static string JsonEscape(string s)
        => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}

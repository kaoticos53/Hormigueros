using System;
using System.Collections.Generic;
using System.Globalization;

namespace AntSim.Unity.Scripts.Streaming
{
    /// <summary>
    /// Diálogo de importación (F4.3, «trae cerebros a tu mundo» — diseño UX
    /// §2.1). Modelo PURO: parsea la tarjeta canónica que emite el oráculo
    /// (<c>antsim --mode genome-info --import f.antgenome</c>, JSON de
    /// <c>GenomeImportInfo</c> en Core) y gestiona el flujo del diálogo:
    /// inspección → confirmación → arranque de partida sembrada. La UI nunca
    /// calcula nada del genoma: sha, metadatos y reglas de cuarentena vienen
    /// del Core; aquí solo vive el estado del diálogo.
    /// </summary>
    public sealed class ImportDialogModel
    {
        /// <summary>Estado del flujo de importación.</summary>
        public enum Phase
        {
            /// <summary>Sin archivo en proceso.</summary>
            Closed = 0,
            /// <summary>Inspección completada: mostrando la tarjeta de cuarentena.</summary>
            Reviewing = 1,
            /// <summary>Confirmado: partida sembrada lista para arrancar.</summary>
            Confirmed = 2,
        }

        /// <summary>Tarjeta parseada del oráculo (null hasta Inspect ok).</summary>
        public sealed class ImportCard
        {
            public string FileName = "";
            public string Sha256 = "";
            public long Bytes;
            public string PoolName = "";
            public string SpeciesHint = "";
            public ulong OriginSeed;
            public int Generation;
            public double BestFitness;
            public int GenomeCount;
            public string CardText = "";
            public string QuarantineNote = "";
        }

        public Phase CurrentPhase { get; private set; } = Phase.Closed;
        public ImportCard? Card { get; private set; }
        public string? Error { get; private set; }
        public string? SourcePath { get; private set; }

        /// <summary>Abrir el diálogo: inspecciona la ruta vía el JSON canónico
        /// que produce el oráculo. Devuelve el texto de tarjeta a mostrar o
        /// null si la inspección falló (ver <see cref="Error"/>).</summary>
        public string? Inspect(string path, string oracleJson)
        {
            SourcePath = path;
            Error = null;
            var parsed = ParseOracleJson(oracleJson);
            if (parsed == null)
            {
                Error = "respuesta del oráculo ilegible";
                CurrentPhase = Phase.Closed;
                return null;
            }
            if (!parsed.Ok)
            {
                Error = parsed.ErrorMessage;
                CurrentPhase = Phase.Closed;
                return null;
            }
            Card = parsed.Import;
            CurrentPhase = Phase.Reviewing;
            return Card.CardText;
        }

        /// <summary>Confirmar la importación: pasa a <c>Confirmed</c> y devuelve
        /// la ruta lista para <c>--seed-pool</c> (null si no hay tarjeta).</summary>
        public string? Confirm()
        {
            if (CurrentPhase != Phase.Reviewing || Card == null) return null;
            CurrentPhase = Phase.Confirmed;
            return SourcePath;
        }

        /// <summary>Cancelar: no toca nada (contrato §2.1 — cancelar es 0 riesgo).</summary>
        public void Cancel()
        {
            CurrentPhase = Phase.Closed;
            Card = null;
            Error = null;
            SourcePath = null;
        }

        /// <summary>Texto del diálogo en fase Reviewing (tarjeta + reglas).</summary>
        public string? RenderDialog()
        {
            var c = Card;
            if (c == null) return null;
            return c.CardText + "\n" + c.QuarantineNote;
        }

        // — Parser JSON mínimo (mismo estilo que GameStreamParser: sin deps) —

        internal sealed class ParsedCard
        {
            public bool Ok;
            public string? ErrorMessage;
            public ImportCard? Import;
        }

        internal static ParsedCard? ParseOracleJson(string json)
        {
            // El oráculo emite un objeto plano; si trae "error" no null con
            // ok:false es un fallo de inspección; ok:true trae la tarjeta.
            if (!TryGetValue(json, "ok", out string okRaw))
                return null;
            var parsed = new ParsedCard { Ok = okRaw == "true" };

            if (!parsed.Ok)
            {
                parsed.ErrorMessage = TryGetString(json, "error", out var e) ? e : "desconocido";
                return parsed;
            }

            var import = new ImportCard
            {
                FileName = TryGetString(json, "fileName", out var v1) ? Unescape(v1) : "",
                Sha256 = TryGetString(json, "sha256", out var v2) ? v2 : "",
                Bytes = TryGetLong(json, "bytes", out var v3) ? v3 : 0,
                PoolName = TryGetString(json, "poolName", out var v4) ? Unescape(v4) : "",
                SpeciesHint = TryGetString(json, "speciesHint", out var v5) ? Unescape(v5) : "",
                OriginSeed = TryGetUlong(json, "originSeed", out var v6) ? v6 : 0,
                Generation = (int)(TryGetLong(json, "generation", out var v7) ? v7 : 0),
                BestFitness = TryGetDouble(json, "bestFitness", out var v8) ? v8 : 0,
                GenomeCount = (int)(TryGetLong(json, "genomeCount", out var v9) ? v9 : 0),
                CardText = TryGetString(json, "card", out var v10) ? Unescape(v10) : "",
                QuarantineNote = TryGetString(json, "quarantine", out var v11) ? Unescape(v11) : "",
            };
            parsed.Import = import;
            return parsed;
        }

        private static bool TryGetValue(string json, string key, out string value)
        {
            value = "";
            string needle = "\"" + key + "\":";
            int i = json.IndexOf(needle, StringComparison.Ordinal);
            if (i < 0) return false;
            i += needle.Length;
            while (i < json.Length && json[i] == ' ') i++;
            int end = i;
            while (end < json.Length && json[end] != ',' && json[end] != '}') end++;
            value = json.Substring(i, end - i).Trim();
            return value.Length > 0;
        }

        private static bool TryGetString(string json, string key, out string value)
        {
            value = "";
            if (!TryGetValue(json, key, out var raw)) return false;
            if (raw == "null") return false;
            if (raw.Length >= 2 && raw[0] == '"') value = raw.Substring(1, raw.Length - 2);
            else value = raw;
            return true;
        }

        private static bool TryGetLong(string json, string key, out long v)
        {
            v = 0;
            if (!TryGetValue(json, key, out var raw)) return false;
            return long.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out v);
        }

        private static bool TryGetUlong(string json, string key, out ulong v)
        {
            v = 0;
            if (!TryGetValue(json, key, out var raw)) return false;
            return ulong.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out v);
        }

        private static bool TryGetDouble(string json, string key, out double v)
        {
            v = 0;
            if (!TryGetValue(json, key, out var raw)) return false;
            return double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out v);
        }

        private static string Unescape(string s) => s
            .Replace("\\\"", "\"").Replace("\\\\", "\\")
            .Replace("\\n", "\n").Replace("\\r", "\r").Replace("\\t", "\t");
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;

namespace AntSim.Unity.Scripts.Streaming
{
    /// <summary>
    /// Datos del selector de pools (F4.5) parseados desde <c>--mode presets --json</c>
    /// y render de los dos niveles del picker: recomendados (los 4 del diseño de UX)
    /// y especialistas (la cadena completa, con su contrapartida ⚠ visible).
    /// C# puro, sin UnityEngine — verificado contra la salida real del CLI en la suite.
    /// </summary>
    public sealed class PoolPickerModel
    {
        public sealed class Preset
        {
            public string Id = "";
            public string DisplayName = "";
            public string Tagline = "";
            public string? GenomeFile;
            public string Band = "";
            public int Pickups, Unloads, SeedsWithUnload, SeedsTotal;
            public float? DropAvg, CarryLegMean;
            public int? GameSeedsWithUnload, GameSeedsTotal;
            public string SourceDoc = "";
            public string ReproCommand = "";
            public string Card = "";

            /// <summary>F4.3: ruta del .antgenome lista para `--seed-pool` — extraída
            /// del propio repro canónico (la UI no hardcodea rutas de pools).</summary>
            public string? SeedPoolPath { get; private set; }

            /// <summary>Extrae SeedPoolPath del ReproCommand canónico (una vez).</summary>
            public string? ResolveSeedPoolPath()
            {
                if (SeedPoolPath != null) return SeedPoolPath;
                const string flag = "--seed-pool ";
                int i = ReproCommand.IndexOf(flag, StringComparison.Ordinal);
                if (i < 0) return null;
                int start = i + flag.Length;
                int end = ReproCommand.IndexOf(' ', start);
                SeedPoolPath = end < 0
                    ? ReproCommand[start..].Trim()
                    : ReproCommand[start..end].Trim();
                return SeedPoolPath.Length > 0 ? SeedPoolPath : null;
            }

            public bool IsRecommended => GameSeedsTotal != null || GenomeFile == null;
        }

        public readonly List<Preset> Presets = new();

        public static PoolPickerModel ParseJson(string json)
        {
            var model = new PoolPickerModel();
            int i = json.IndexOf("\"presets\":[", StringComparison.Ordinal);
            if (i < 0) return model;
            i += "\"presets\":[".Length;

            // Objetos top-level del array (con llaves anidadas de benchmark/gameMode).
            int depth = 0, start = i;
            for (; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '{') { if (depth == 0) start = i + 1; depth++; }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0) model.Presets.Add(ParsePreset(json.Substring(start, i - start)));
                    if (depth == 0 && json[i + 1] == ']') break;
                }
            }
            return model;
        }

        private static Preset ParsePreset(string obj)
        {
            var p = new Preset();
            p.Id = Str(obj, "id");
            p.DisplayName = Str(obj, "displayName");
            p.Tagline = Str(obj, "tagline");
            p.GenomeFile = NullStr(obj, "genomeFile");
            p.Band = Str(obj, "band");
            p.SourceDoc = Str(obj, "sourceDoc");
            p.ReproCommand = Str(obj, "reproCommand");
            p.Card = Str(obj, "card");

            int bi = obj.IndexOf("\"benchmark\":{", StringComparison.Ordinal);
            if (bi >= 0)
            {
                int end = obj.IndexOf('}', bi);
                string b = obj.Substring(bi, end - bi);
                p.Pickups = (int)Num(Str(b, "pickups"));
                p.Unloads = (int)Num(Str(b, "unloads"));
                p.SeedsWithUnload = (int)Num(Str(b, "seedsWithUnload"));
                p.SeedsTotal = (int)Num(Str(b, "seedsTotal"));
                p.DropAvg = NullNum(b, "dropAvg");
                p.CarryLegMean = NullNum(b, "carryLegMean");
            }

            int gi = obj.IndexOf("\"gameMode\":{", StringComparison.Ordinal);
            if (gi >= 0)
            {
                int end = obj.IndexOf('}', gi);
                string g = obj.Substring(gi, end - gi);
                p.GameSeedsWithUnload = (int)Num(Str(g, "seedsWithUnload"));
                p.GameSeedsTotal = (int)Num(Str(g, "seedsTotal"));
            }

            return p;
        }

        /// <summary>Nivel 1 del picker: los recomendados (los 4 del diseño de UX).</summary>
        public IEnumerable<Preset> Recommended()
        {
            // Los recomendados van primero en el stream canónico; el naturalista
            // (sin genomeFile) y los sembrados con gameMode 5/5 evaluable son la
            // fila de entrada. La regla canónica: los 4 primeros del JSON.
            for (int i = 0; i < Presets.Count && i < 4; i++)
                yield return Presets[i];
        }

        /// <summary>Nivel 2 del picker: los especialistas (resto de la cadena).</summary>
        public IEnumerable<Preset> Specialists()
        {
            for (int i = 4; i < Presets.Count; i++)
                yield return Presets[i];
        }

        // ————— extracción mínima —————

        private static string Str(string s, string key)
        {
            int i = s.IndexOf("\"" + key + "\":", StringComparison.Ordinal);
            if (i < 0) return "";
            i += key.Length + 3; // salta "key": (comilla, clave, comilla, dos puntos)
            if (i < s.Length && s[i] == '"')
            {
                i++;
                int j = s.IndexOf('"', i);
                return j > i ? s.Substring(i, j - i) : "";
            }
            int k = i;
            while (k < s.Length && s[k] != ',' && s[k] != '}') k++;
            return s.Substring(i, k - i).Trim();
        }

        private static string? NullStr(string s, string key)
        {
            int i = s.IndexOf("\"" + key + "\":", StringComparison.Ordinal);
            if (i < 0) return null;
            i += key.Length + 3;
            if (i + 4 <= s.Length && s.Substring(i, 4) == "null") return null;
            return Str(s, key);
        }

        private static float? NullNum(string s, string key)
        {
            string v = Str(s, key);
            if (v.Length == 0 || v == "null") return null;
            return float.Parse(v, CultureInfo.InvariantCulture);
        }

        private static double Num(string s)
            => double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0;
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace AntSim.Unity.Scripts.Streaming
{
    /// <summary>
    /// Modelo PURO de la configuración de lanzamiento (F5.3): lo que
    /// <c>scripts/play-game.ps1</c> / <c>play-game.bat</c> escriben y Unity
    /// aplica a la escena ANTES de entrar en Play. Sin esta pieza, elegir
    /// pool/grid/especie en el lanzador era decorativo — el juego siempre
    /// arrancaba con los defaults del bootstrapper y había que tocar el
    /// inspector a mano.
    ///
    /// Formato: <c>clave=valor</c>, una por línea; <c>#</c> comenta; las claves
    /// desconocidas se ignoran (compatibilidad hacia delante). Sin dependencias
    /// de Unity para poder verificarlo en la suite headless.
    /// </summary>
    public sealed class LaunchSettings
    {
        // F7: el juego arranca con el pool entrenado CON OBSTÁCULOS y con la
        // línea de visión del canal de comida. Es el que forrajea en el laberinto
        // que el juego monta por defecto (medido: 102 descargas frente a 39 del
        // de referencia en la partida canónica con `--maze`, 4 semillas). Los
        // pools anteriores siguen eligiéndose con `--pool` o importándose.
        public string Pool = "pretrain-maze-los3"; // nombre del pool (se resuelve a artifacts/) o ruta
        public string PoolPath = "";             // ruta RESUELTA por el lanzador (tiene prioridad)
        public int Grid = 96;          // celdas; el mundo mide Grid × 8 u
        public int Colonies = 2;
        public int Ticks = 216000; // 2 h de simulación
        public string Species = "lasius";
        public float Speed = 120f; // 40 × los 3× del lanzador anterior
        public int FrameEvery = 1;
        /// <summary>F6: terreno con laberinto (barreras visibles generadas de la semilla).</summary>
        public bool Maze = true;

        /// <summary>F8: reclutamiento por rastro — cúmulos de comida y reflejos innatos
        /// (la cargada deja rastro de comida y de casa; la que busca lo sigue). Es
        /// lo que hace visible y útil el canal E, así que va encendido en la partida.</summary>
        public bool Recruitment = true;

        // — F8: las PERILLAS del reclutamiento, para comparar configuraciones —
        // desde el lanzador (`--clusters`, `--cluster-size`, `--trail-…`) sin tocar
        // código. Los defaults son los que el mundo trae de fábrica: sin banderas,
        // la partida es la de siempre.

        /// <summary>Montones de comida que se siembran además de los ítems sueltos.</summary>
        public int ClusterCount = 8;

        /// <summary>Ítems por montón: el tamaño se sortea en [ClusterMin, ClusterMax].</summary>
        public int ClusterMin = 6;
        public int ClusterMax = 10;

        /// <summary>Radio del montón en unidades de mundo.</summary>
        public float ClusterRadius = 18f;

        /// <summary>Rastro de comida que deja la cargada (fracción de QMaxFood por segundo).</summary>
        public float TrailDeposit = 1f;

        /// <summary>Vida media (s) del rastro de comida.</summary>
        public float TrailHalfLife = 2100f;

        /// <summary>Ganancia del reflejo que sigue el rastro (rad/s a plena asimetría).</summary>
        public float TrailFollow = 8f;

        // Rangos: los mismos que acepta el mundo/CLI (WorldUnits, SpeedControlModel).
        public const int MinGrid = 16;
        public const int MaxGrid = 1024;
        public const int MinTicks = 30;
        public const int MaxTicks = 4_000_000;
        public const float MinSpeed = 10f;
        public const float MaxSpeed = 160f;
        public const int MinFrameEvery = 1;
        public const int MaxFrameEvery = 60;
        /// <summary>La escena monta marcadores y tarjetas para 2 colonias como máximo.</summary>
        public const int MaxColonies = 2;

        public static LaunchSettings Defaults() => new LaunchSettings();

        public string Serialize()
        {
            var sb = new StringBuilder();
            sb.Append("pool=").Append(Pool).Append('\n');
            sb.Append("poolPath=").Append(PoolPath).Append('\n');
            sb.Append("grid=").Append(Grid.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("colonies=").Append(Colonies.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("ticks=").Append(Ticks.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("species=").Append(Species).Append('\n');
            sb.Append("speed=").Append(Speed.ToString("0.###", CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("frameEvery=").Append(FrameEvery.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("maze=").Append(Maze ? '1' : '0').Append('\n');
            sb.Append("recruitment=").Append(Recruitment ? '1' : '0').Append('\n');
            sb.Append("clusters=").Append(ClusterCount.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("clusterSize=").Append(ClusterMin.ToString(CultureInfo.InvariantCulture))
              .Append(':').Append(ClusterMax.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("clusterRadius=").Append(ClusterRadius.ToString("0.###", CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("trailDeposit=").Append(TrailDeposit.ToString("0.###", CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("trailHalfLife=").Append(TrailHalfLife.ToString("0.###", CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("trailFollow=").Append(TrailFollow.ToString("0.###", CultureInfo.InvariantCulture)).Append('\n');
            return sb.ToString();
        }

        /// <summary>
        /// Parsea el texto del lanzador. NUNCA lanza: devuelve false con el
        /// motivo en <paramref name="error"/> (el llamador decide si cae a los
        /// defaults o aborta). Los valores por defecto se conservan para las
        /// claves ausentes.
        /// </summary>
        public static bool TryParse(string text, out LaunchSettings settings, out string error)
        {
            settings = Defaults();
            error = "";
            if (text == null) { error = "texto vacío"; return false; }

            foreach (string rawLine in text.Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) { error = $"línea sin clave=valor: «{line}»"; return false; }
                string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                string value = line.Substring(eq + 1).Trim();

                switch (key)
                {
                    case "pool": settings.Pool = value; break;
                    case "poolpath": settings.PoolPath = value; break;
                    case "species": settings.Species = value; break;
                    case "grid":
                        if (!TryInt(value, out settings.Grid, out error)) return false;
                        break;
                    case "colonies":
                        if (!TryInt(value, out settings.Colonies, out error)) return false;
                        break;
                    case "ticks":
                        if (!TryInt(value, out settings.Ticks, out error)) return false;
                        break;
                    case "frameevery":
                        if (!TryInt(value, out settings.FrameEvery, out error)) return false;
                        break;
                    case "speed":
                        if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out settings.Speed))
                        { error = $"speed no es un número: «{value}»"; return false; }
                        break;
                    case "maze":
                        settings.Maze = value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase);
                        break;
                    case "recruitment":
                        settings.Recruitment = value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase);
                        break;
                    case "clusters":
                        if (!TryInt(value, out settings.ClusterCount, out error)) return false;
                        break;
                    case "clustersize":
                    {
                        string[] mm = value.Split(':');
                        if (mm.Length != 2
                            || !TryInt(mm[0], out settings.ClusterMin, out error)
                            || !TryInt(mm[1], out settings.ClusterMax, out error))
                        {
                            error = $"clusterSize no es MIN:MAX: «{value}»";
                            return false;
                        }
                        break;
                    }
                    case "clusterradius":
                        if (!TryFloat(value, out settings.ClusterRadius, out error)) return false;
                        break;
                    case "traildeposit":
                        if (!TryFloat(value, out settings.TrailDeposit, out error)) return false;
                        break;
                    case "trailhalflife":
                        if (!TryFloat(value, out settings.TrailHalfLife, out error)) return false;
                        break;
                    case "trailfollow":
                        if (!TryFloat(value, out settings.TrailFollow, out error)) return false;
                        break;
                    default:
                        break; // clave desconocida: se ignora (compatibilidad)
                }
            }
            return settings.TryValidate(out error);
        }

        private static bool TryInt(string value, out int parsed, out string error)
        {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
            {
                error = $"se esperaba un entero y llegó «{value}»";
                return false;
            }
            error = "";
            return true;
        }

        private static bool TryFloat(string value, out float parsed, out string error)
        {
            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
            {
                error = $"se esperaba un número y llegó «{value}»";
                return false;
            }
            error = "";
            return true;
        }

        /// <summary>Reglas del mundo/CLI. Devuelve false con el motivo en <paramref name="error"/>.</summary>
        public bool TryValidate(out string error)
        {
            error = "";
            if (Grid < MinGrid || Grid > MaxGrid)
            { error = $"grid fuera de rango [{MinGrid}, {MaxGrid}]: {Grid}"; return false; }
            if (Colonies < 1 || Colonies > MaxColonies)
            { error = $"colonies fuera de rango [1, {MaxColonies}]: {Colonies}"; return false; }
            if (Ticks < MinTicks || Ticks > MaxTicks)
            { error = $"ticks fuera de rango [{MinTicks}, {MaxTicks}]: {Ticks}"; return false; }
            if (FrameEvery < MinFrameEvery || FrameEvery > MaxFrameEvery)
            { error = $"frameEvery fuera de rango [{MinFrameEvery}, {MaxFrameEvery}]: {FrameEvery}"; return false; }
            if (Speed < MinSpeed || Speed > MaxSpeed)
            { error = $"velocidad fuera de rango [{MinSpeed}, {MaxSpeed}]: {Speed}"; return false; }
            if (!IsValidSpeciesList(Species))
            { error = $"especie inválida: «{Species}» (esperado p. ej. lasius o lasius,eciton)"; return false; }
            // F8: montones y rastro. Misma regla que el CLI: los rangos viven en
            // RecruitmentKnobs, que la suite contrasta contra los del Core.
            if (!ToRecruitmentKnobs().TryValidate(out error)) return false;
            return true;
        }

        /// <summary>Las perillas de F8 como objeto de la vista (lo que el presenter
        /// manda al CLI; el mundo las aplica antes de sembrar).</summary>
        public RecruitmentKnobs ToRecruitmentKnobs() => new RecruitmentKnobs
        {
            Clusters = ClusterCount,
            ClusterMin = ClusterMin,
            ClusterMax = ClusterMax,
            ClusterRadius = ClusterRadius,
            TrailDeposit = TrailDeposit,
            TrailHalfLife = TrailHalfLife,
            TrailFollow = TrailFollow,
        };

        /// <summary>Lista de tokens alfabéticos separados por comas (la del CLI: <c>--species</c>).</summary>
        public static bool IsValidSpeciesList(string species)
        {
            if (string.IsNullOrEmpty(species)) return false;
            foreach (string token in species.Split(','))
            {
                string t = token.Trim();
                if (t.Length == 0) return false;
                for (int i = 0; i < t.Length; i++)
                    if (!char.IsLetter(t[i])) return false;
            }
            return true;
        }

        /// <summary>Línea de consola con lo que se va a aplicar (la imprime el editor al entrar en Play).</summary>
        public string Describe()
        {
            string pool = !string.IsNullOrEmpty(PoolPath) ? PoolPath
                        : (!string.IsNullOrEmpty(Pool) ? Pool : "(sin sembrar)");
            return $"pool={pool} · grid={Grid} ({Grid * 8} u) · colonias={Colonies} · " +
                   $"ticks={Ticks} · especie={Species} · velocidad={Speed.ToString("0.#", CultureInfo.InvariantCulture)}x · " +
                   $"frame-every={FrameEvery} · laberinto={(Maze ? "sí" : "no")} · " +
                   $"reclutamiento={(Recruitment ? "sí" : "no")}" +
                   (Recruitment ? " · " + ToRecruitmentKnobs().Describe() : "");
        }
    }
}

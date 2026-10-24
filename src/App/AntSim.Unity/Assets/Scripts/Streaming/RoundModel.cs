using System.Collections.Generic;

namespace AntSim.Unity.Scripts.Streaming
{
    /// <summary>
    /// F8 — REGLAS DE LA RONDA ENCADENADA, sin UnityEngine para poder verificarlas
    /// en la suite headless.
    ///
    /// QUÉ PROBLEMA RESUELVE. La partida clásica es UNA corrida: cuando la colonia
    /// se extingue (o se agota el horizonte) el juego se queda mostrando el final
    /// y no hay nada más que ver. Aquí se decide CUÁNDO cerrar la ronda y CON QUÉ
    /// se abre la siguiente:
    ///
    ///   · la ronda acaba cuando NO queda ni una adulta ni cría en ninguna colonia
    ///     (el CLI la corta en ese tick con `--until-extinct` y exporta su élite);
    ///   · la ronda siguiente usa una semilla DERIVADA de la anterior — el mundo y
    ///     su laberinto se generan de la semilla, así que es otro mapa sin depender
    ///     de un dado externo ni de la hora (la misma partida se reproduce igual);
    ///   · los cerebros son la élite exportada por la ronda que acaba de morir; si
    ///     esa exportación no existe (ExportElite apagado, repo sin artifacts/), se
    ///     conserva el pool con el que se estaba jugando — nunca se arranca sin
    ///     cerebros por un fallo de ruta.
    ///
    /// Una lista de colonias VACÍA no es extinción: sin datos no hay ronda que
    /// cerrar, y tratarla como tal encadenaría rondas vacías en bucle.
    /// </summary>
    public static class RoundModel
    {
        /// <summary>¿No queda ninguna hormiga ni cría? `colonies` es el canal A del
        /// tick (las mismas cifras que pinta la tarjeta: adultas, huevos, larvas y
        /// pupas). Lista vacía o nula ⇒ false (aún no hay datos de la ronda).</summary>
        public static bool AllColoniesDead(IReadOnlyList<GameStreamParser.ColonyView>? colonies)
        {
            if (colonies == null || colonies.Count == 0) return false;
            for (int i = 0; i < colonies.Count; i++)
            {
                var c = colonies[i];
                if (c.Adults > 0 || c.Eggs > 0 || c.Larvae > 0 || c.Pupae > 0)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Semilla de la ronda <paramref name="round"/> a partir de la anterior
        /// (splitmix64: determinista, sin estado y con buena dispersión). Nunca
        /// devuelve 0 — es el valor centinela de «no hay semilla armada».
        /// </summary>
        public static ulong NextSeed(ulong seed, int round)
        {
            ulong z = seed + 0x9E3779B97F4A7C15UL * (ulong)(round < 1 ? 1 : round);
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            z ^= z >> 31;
            return z == 0UL ? 1UL : z;
        }

        /// <summary>Pool con el que sembrar la ronda nueva: la élite exportada por la
        /// anterior si la hay; el pool actual si no. La EXISTENCIA del archivo la
        /// comprueba quien llama (este modelo no toca el disco).</summary>
        public static string NextSeedPool(string? exportedElite, string currentPool)
            => string.IsNullOrWhiteSpace(exportedElite) ? currentPool : exportedElite!;
    }
}

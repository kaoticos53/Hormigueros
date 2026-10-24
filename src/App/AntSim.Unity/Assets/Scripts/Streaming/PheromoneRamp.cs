using System;

namespace AntSim.Unity.Scripts.Streaming
{
    /// <summary>
    /// F8 — CÓMO SE CONVIERTE EL BYTE DEL CANAL E EN OPACIDAD DE PANTALLA. Modelo
    /// PURO (sin UnityEngine) para poder fijar la curva en la suite headless.
    ///
    /// POR QUÉ EXISTE. Antes la textura se componía con el byte del canal E tal
    /// cual como alfa: el camino se quedaba en el valor del byte (87 % de
    /// opacidad en la capa medida, máximo 222/255) y la cola que deja la difusión
    /// (las celdas de 8–40/255 alrededor del depósito) quedaba
    /// repartida por una superficie varias veces mayor que el camino, con un alfa
    /// bajo pero continuo. En el tablero eso se lee como una mancha: no hay un
    /// borde donde acabe el rastro ni un centro que destaque. Aquí el corte es
    /// RELATIVO a la capa: se descarta la cola por debajo del 18 % del máximo del
    /// propio mapa (con un suelo absoluto de 6 cuantizado para no amplificar el
    /// ruido de una capa vacía) y lo que queda se reescala al rango visible, con
    /// el núcleo opaco y el borde apagándose.
    ///
    /// MEDIDO sobre la capa real de la partida canónica (semilla 42, grid 96,
    /// laberinto, pool `pretrain-maze-los3`, `--recruitment --phero-every 600`):
    /// la capa de comida del tick 10 800 (máx. 222) pasa de 292 celdas pintadas a
    /// 75 y el corte que cruza su celda más fuerte, de 104×40 u a 56×40 u; la de
    /// casa del tick 10 200 (máx. 222) pasa de 695 a 258 celdas y de 216×208 u a
    /// 72×128 u. El núcleo, que antes se quedaba en alfa 0,87, llega a 1.
    ///
    /// NO toca el dato: `PheromoneTileModel` sigue con los bytes tal cual los
    /// manda el mundo, y las hormigas siguen leyendo el dato (no la pantalla).
    /// Es solo la lectura en el tablero.
    /// </summary>
    public static class PheromoneRamp
    {
        /// <summary>Suelo absoluto (cuantizado): por debajo es residuo de difusión.</summary>
        public const byte MinVisible = 6;

        /// <summary>Fracción del máximo de la capa por debajo de la cual no se pinta.</summary>
        public const float TailFraction = 0.18f;

        /// <summary>Opacidad del primer valor visible (el borde del camino).</summary>
        public const float EdgeAlpha = 0.30f;

        /// <summary>Gamma del tramo visible: núcleo sólido, borde que se apaga.</summary>
        public const float Gamma = 0.75f;

        /// <summary>Máximo de la capa (0 si está vacía).</summary>
        public static byte LayerMax(byte[] cells)
        {
            byte max = 0;
            for (int i = 0; i < cells.Length; i++)
                if (cells[i] > max) max = cells[i];
            return max;
        }

        /// <summary>Opacidad 0..1 de una celda para el máximo de su capa.</summary>
        public static float Alpha(byte value, byte layerMax)
        {
            if (value == 0) return 0f;
            float floor = Math.Max((float)MinVisible, layerMax * TailFraction);
            if (value <= floor) return 0f;
            float span = Math.Max(1f, layerMax - floor);
            float t = Math.Min(1f, (value - floor) / span);
            return EdgeAlpha + (1f - EdgeAlpha) * (float)Math.Pow(t, Gamma);
        }

        /// <summary>El byte de alfa que se escribe en la textura (0 si no se pinta).</summary>
        public static byte AlphaByte(byte value, byte layerMax)
            => (byte)Math.Clamp((int)MathF.Round(Alpha(value, layerMax) * 255f), 0, 255);
    }
}

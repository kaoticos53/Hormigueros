using System;
using System.Globalization;

namespace AntSim.Unity.Scripts.Streaming
{
    /// <summary>
    /// Modelo PURO para el control de velocidad de simulación/reproducción.
    /// Define el rango permitido (10× a 160×, con 120× como base normal = 100 %),
    /// presets rápidos, pasos de aceleración/desaceleración y conversiones/formateo de texto sin
    /// dependencias de UnityEngine (testeable headless con xUnit).
    ///
    /// La base es 120× porque es 40 × los 3× que el lanzador usaba como
    /// «normal» y la partida anterior se percibía lenta. El techo (160×) sale de
    /// la medición del CLI: ~6.000 ticks/s por núcleo en 96² (0,165 ms/tick), y
    /// 160× pide 4.800 ticks/s — por debajo, con margen para el laberinto.
    /// </summary>
    public static class SpeedControlModel
    {
        /// <summary>Velocidad mínima: 10× (8 % de la base).</summary>
        public const float MinSpeed = 10.00f;

        /// <summary>Velocidad máxima: 160× (133 % de la base).</summary>
        public const float MaxSpeed = 160.00f;

        /// <summary>Velocidad normal/base de simulación: 120× (100 %).</summary>
        public const float NormalSpeed = 120.00f;

        /// <summary>Velocidad por defecto en el arranque del visualizador: 120×.</summary>
        public const float DefaultSpeed = 120.00f;

        /// <summary>
        /// Presets de velocidad estándar en escala progresiva:
        /// 10× (8 %), 30× (25 %), 60× (50 %), 90× (75 %), 120× (100 % Base), 160× (133 %).
        /// </summary>
        public static readonly float[] Presets = { 10.0f, 30.0f, 60.0f, 90.0f, 120.0f, 160.0f };

        /// <summary>
        /// Limita una velocidad al rango [MinSpeed, MaxSpeed], salvo que sea 0 (pausa).
        /// </summary>
        public static float ClampSpeed(float speed, bool allowPause = true)
        {
            if (allowPause && speed <= 0f) return 0f;
            if (speed < MinSpeed) return MinSpeed;
            if (speed > MaxSpeed) return MaxSpeed;
            return speed;
        }

        /// <summary>Convierte un multiplicador (ej. 1.0 a 100.0) a porcentaje relativo a base (10 a 1000).</summary>
        public static float SpeedToPercent(float speed)
        {
            return (float)Math.Round((speed / NormalSpeed) * 100f, 1);
        }

        /// <summary>Convierte un porcentaje relativo a base (ej. 10 a 1000) a multiplicador (1.0 a 100.0).</summary>
        public static float PercentToSpeed(float percent)
        {
            return ClampSpeed((percent / 100f) * NormalSpeed, allowPause: false);
        }

        /// <summary>
        /// Incrementa la velocidad hacia el siguiente nivel lógico o preset.
        /// Si está pausado, reanuda en MinSpeed o velocidad previa.
        /// </summary>
        public static float StepUp(float currentSpeed)
        {
            if (currentSpeed < MinSpeed) return MinSpeed;

            // Si está muy cerca de un preset, pasar al siguiente preset
            for (int i = 0; i < Presets.Length; i++)
            {
                if (Presets[i] > currentSpeed + 0.01f)
                    return Presets[i];
            }

            return MaxSpeed;
        }

        /// <summary>
        /// Reduce la velocidad hacia el nivel lógico o preset inmediatamente inferior.
        /// </summary>
        public static float StepDown(float currentSpeed)
        {
            if (currentSpeed <= MinSpeed) return MinSpeed;

            for (int i = Presets.Length - 1; i >= 0; i--)
            {
                if (Presets[i] < currentSpeed - 0.01f)
                    return Presets[i];
            }

            return MinSpeed;
        }

        /// <summary>
        /// Formatea la velocidad con indicador de multiplicador y porcentaje respecto a la base.
        /// Ejemplos: "8% (10×)", "100% (120× Base)", "133% (160×)", "EN PAUSA".
        /// </summary>
        public static string FormatSpeed(float speed)
        {
            if (speed <= 0f) return "EN PAUSA";
            int percent = (int)Math.Round((speed / NormalSpeed) * 100f);
            string mult = speed.ToString("0.#", CultureInfo.InvariantCulture);
            if (Math.Abs(speed - NormalSpeed) < 0.01f)
                return $"{percent}% ({mult}× Base)";
            return $"{percent}% ({mult}×)";
        }

        /// <summary>
        /// Formatea solo la etiqueta corta de porcentaje (ej: "10%", "100%", "1000%").
        /// </summary>
        public static string FormatPercent(float speed)
        {
            if (speed <= 0f) return "0%";
            int percent = (int)Math.Round((speed / NormalSpeed) * 100f);
            return $"{percent}%";
        }
    }
}

using System;
using System.Globalization;

namespace AntSim.Unity.Scripts.Streaming
{
    /// <summary>
    /// Modelo PURO para el control de velocidad de simulación/reproducción.
    /// Define el rango permitido (10% a 1000% sobre base normal de 10.0× / 1.0× a 100.0×),
    /// presets rápidos, pasos de aceleración/desaceleración y conversiones/formateo de texto sin
    /// dependencias de UnityEngine (testeable headless con xUnit).
    /// </summary>
    public static class SpeedControlModel
    {
        /// <summary>Velocidad mínima: 10% de base (1.0×).</summary>
        public const float MinSpeed = 1.00f;

        /// <summary>Velocidad máxima: 1000% de base (100.0×).</summary>
        public const float MaxSpeed = 100.00f;

        /// <summary>Velocidad normal/base de simulación: 100% (10.0×).</summary>
        public const float NormalSpeed = 10.00f;

        /// <summary>Velocidad por defecto en el arranque del visualizador: 100% base (10.0×).</summary>
        public const float DefaultSpeed = 10.00f;

        /// <summary>
        /// Presets de velocidad estándar en escala progresiva:
        /// 10% (1.0×), 25% (2.5×), 50% (5.0×), 100% (10.0× Base), 250% (25.0×), 500% (50.0×), 1000% (100.0×).
        /// </summary>
        public static readonly float[] Presets = { 1.0f, 2.5f, 5.0f, 10.0f, 25.0f, 50.0f, 100.0f };

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
        /// Ejemplos: "10% (1×)", "100% (10× Base)", "500% (50×)", "1000% (100×)", "EN PAUSA".
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

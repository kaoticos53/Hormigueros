using System;
using System.Globalization;

namespace AntSim.Unity.Scripts.Streaming
{
    /// <summary>
    /// Modelo PURO para el control de velocidad de simulación/reproducción.
    /// Define el rango permitido (30% a 1000%), presets rápidos, pasos de
    /// aceleración/desaceleración y conversiones/formateo de texto sin
    /// dependencias de UnityEngine (testeable headless con xUnit).
    /// </summary>
    public static class SpeedControlModel
    {
        /// <summary>Velocidad mínima: 30% (0.3×).</summary>
        public const float MinSpeed = 0.30f;

        /// <summary>Velocidad máxima: 1000% (10.0×).</summary>
        public const float MaxSpeed = 10.00f;

        /// <summary>Velocidad normal: 100% (1.0×).</summary>
        public const float NormalSpeed = 1.00f;

        /// <summary>Velocidad por defecto en el arranque del visualizador (300% / 3.0×).</summary>
        public const float DefaultSpeed = 3.00f;

        /// <summary>
        /// Presets de velocidad estándar en escala progresiva:
        /// 30% (0.3×), 50% (0.5×), 100% (1.0×), 200% (2.0×), 300% (3.0×), 500% (5.0×), 1000% (10.0×).
        /// </summary>
        public static readonly float[] Presets = { 0.3f, 0.5f, 1.0f, 2.0f, 3.0f, 5.0f, 10.0f };

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

        /// <summary>Convierte un multiplicador (ej. 0.3 a 10.0) a porcentaje (30 a 1000).</summary>
        public static float SpeedToPercent(float speed)
        {
            return (float)Math.Round(speed * 100f, 1);
        }

        /// <summary>Convierte un porcentaje (ej. 30 a 1000) a multiplicador (0.3 a 10.0).</summary>
        public static float PercentToSpeed(float percent)
        {
            return ClampSpeed(percent / 100f, allowPause: false);
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
        /// Formatea la velocidad con indicador de multiplicador y porcentaje.
        /// Ejemplos: "30% (0.3×)", "100% (1.0×)", "300% (3.0×)", "1000% (10.0×)", "EN PAUSA".
        /// </summary>
        public static string FormatSpeed(float speed)
        {
            if (speed <= 0f) return "EN PAUSA";
            int percent = (int)Math.Round(speed * 100f);
            return $"{percent}% ({speed.ToString("0.#", CultureInfo.InvariantCulture)}×)";
        }

        /// <summary>
        /// Formatea solo la etiqueta corta de porcentaje (ej: "30%", "100%", "1000%").
        /// </summary>
        public static string FormatPercent(float speed)
        {
            if (speed <= 0f) return "0%";
            int percent = (int)Math.Round(speed * 100f);
            return $"{percent}%";
        }
    }
}

#if UNITY_EDITOR
using System;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;

namespace AntSim.Unity.Scripts.EditorTools
{
    /// <summary>
    /// Configura y activa Universal Render Pipeline (URP) en el proyecto,
    /// reemplazando el Built-in Render Pipeline deprecado en Unity 6.
    /// Crea el asset de URP y UniversalRendererData y los asigna a GraphicsSettings
    /// y QualitySettings para un renderizado moderno y optimizado.
    /// </summary>
    public static class UrpSetup
    {
        private const string SettingsFolder = "Assets/Settings";
        private const string UrpAssetPath = "Assets/Settings/UniversalRenderPipelineAsset.asset";
        private const string RendererDataPath = "Assets/Settings/UniversalRendererData.asset";

        [MenuItem("AntSim/Configurar URP (Universal Render Pipeline)", priority = 2)]
        public static void SetupUrp()
        {
            var asset = EnsureUrpPipelineAsset();
            if (asset != null)
            {
                Debug.Log("[AntSim] Universal Render Pipeline (URP) configurado y activado con éxito.");
            }
            else
            {
                Debug.LogWarning("[AntSim] No se pudo crear el asset URP automáticamente. Asegúrate de que el paquete com.unity.render-pipelines.universal esté resuelto en Unity.");
            }
        }

        /// <summary>
        /// Asegura que el pipeline URP esté creado y asignado como el Render Pipeline activo del proyecto.
        /// </summary>
        public static RenderPipelineAsset? EnsureUrpPipelineAsset()
        {
            if (GraphicsSettings.defaultRenderPipeline != null)
                return GraphicsSettings.defaultRenderPipeline;

            if (!AssetDatabase.IsValidFolder(SettingsFolder))
            {
                AssetDatabase.CreateFolder("Assets", "Settings");
            }

            var existingAsset = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(UrpAssetPath);
            if (existingAsset != null)
            {
                ApplyPipelineAsset(existingAsset);
                return existingAsset;
            }

            // Crear via reflexión para compatibilidad inmediata con cualquier versión de Unity 6 / URP
            var urpAssetType = Type.GetType("UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset, Unity.RenderPipelines.Universal.Runtime")
                            ?? Type.GetType("UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset, UnityEngine.Rendering.Universal");
            var rendererDataType = Type.GetType("UnityEngine.Rendering.Universal.UniversalRendererData, Unity.RenderPipelines.Universal.Runtime")
                                ?? Type.GetType("UnityEngine.Rendering.Universal.UniversalRendererData, UnityEngine.Rendering.Universal");

            if (urpAssetType != null && rendererDataType != null)
            {
                var rendererData = ScriptableObject.CreateInstance(rendererDataType);
                AssetDatabase.CreateAsset(rendererData, RendererDataPath);

                var createMethod = urpAssetType.GetMethod("Create", new[] { rendererDataType });
                RenderPipelineAsset? urpAsset = null;
                if (createMethod != null)
                {
                    urpAsset = createMethod.Invoke(null, new object[] { rendererData }) as RenderPipelineAsset;
                }
                else
                {
                    urpAsset = ScriptableObject.CreateInstance(urpAssetType) as RenderPipelineAsset;
                }

                if (urpAsset != null)
                {
                    AssetDatabase.CreateAsset(urpAsset, UrpAssetPath);
                    AssetDatabase.SaveAssets();
                    ApplyPipelineAsset(urpAsset);
                    return urpAsset;
                }
            }

            return null;
        }

        private static void ApplyPipelineAsset(RenderPipelineAsset asset)
        {
            GraphicsSettings.defaultRenderPipeline = asset;
            QualitySettings.renderPipeline = asset;
        }
    }
}
#endif

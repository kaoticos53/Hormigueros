#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using UnityEditor.SceneManagement;

namespace AntSim.Unity.Scripts.EditorTools
{
    /// <summary>
    /// Bootstrapper de la escena de juego (F4.2): construye programáticamente TODO
    /// el montaje — cámara, plano, marcadores de nido, meshes de hormiga/ítem,
    /// Canvas del HUD (tarjetas por colonia, toasts, historial, inspección) —
    /// y deja las referencias asignadas. Un comando de menú: Unity → AntSim →
    /// «Crear escena de juego», guardar, Play. Así la integración visual no
    /// depende de pasos manuales que se pudren.
    /// </summary>
    public static class SceneBootstrapper
    {
        [MenuItem("AntSim/Crear escena de juego", priority = 0)]
        public static void CreateGameScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // — Cámara: vista cenital del mundo (grid 256 por defecto) —
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            camGo.transform.position = new Vector3(128f, 160f, 128f);
            camGo.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            cam.orthographic = true;
            cam.orthographicSize = 150f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 500f;
            cam.clearFlags = CameraClearFlags.SolidCamera;
            cam.backgroundColor = new Color(0.13f, 0.12f, 0.10f); // tierra

            // — Luz (los DrawMesh necesitan iluminación) —
            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            // — Suelo —
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.position = new Vector3(128f, 0f, 128f);
            floor.transform.localScale = new Vector3(26f, 1f, 26f); // plane = 10 u ⇒ 256
            var floorRend = floor.GetComponent<Renderer>();
            floorRend.sharedMaterial = NewMat(new Color(0.35f, 0.30f, 0.22f), "FloorMat");

            // — Presenter (dibuja hormigas/ítems con DrawMesh) —
            var presenterGo = new GameObject("SimPresenter");
            var presenter = presenterGo.AddComponent<Presenter.SimPresenterBehaviour>();
            presenter.CliPath = "build/antsim";
            presenter.Grid = 256;
            presenter.Colonies = 2;
            presenter.Ticks = 48000;
            presenter.FrameEvery = 1;
            presenter.AntMesh = PrimitiveMesh(PrimitiveType.Capsule, 0.25f, "AntMesh");
            presenter.ItemMesh = PrimitiveMesh(PrimitiveType.Sphere, 0.6f, "ItemMesh");
            presenter.AntMaterial = NewMat(new Color(0.55f, 0.35f, 0.15f), "AntMat");
            presenter.CarrierMaterial = NewMat(new Color(0.95f, 0.75f, 0.2f), "CarrierMat");
            presenter.ItemMaterial = NewMat(new Color(0.3f, 0.75f, 0.35f), "ItemMat");

            // — Marcadores de nido (posiciones del mundo; el HUD ancla a las tarjetas) —
            for (int c = 0; c < 2; c++)
            {
                var nest = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                nest.name = $"Nest_{c}";
                float nx = c == 0 ? 64f : 192f;
                nest.transform.position = new Vector3(nx, 0.5f, 128f);
                nest.transform.localScale = new Vector3(3f, 0.5f, 3f);
                nest.GetComponent<Renderer>().sharedMaterial =
                    NewMat(c == 0 ? new Color(0.7f, 0.3f, 0.2f) : new Color(0.2f, 0.4f, 0.75f), $"NestMat{c}");
            }

            // — Canvas del HUD —
            var canvasGo = new GameObject("HUD Canvas");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGo.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasGo.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            if (Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<UnityEngine.EventSystems.EventSystem>();
                es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            }

            // — Tarjetas de colonia (izquierda) —
            var cardTexts = new Text[2];
            for (int c = 0; c < 2; c++)
            {
                var t = NewText(canvasGo.transform, $"ColonyCard_{c}",
                    new Vector2(14, -14 - c * 118), new Vector2(330, 110),
                    TextAnchor.UpperLeft, 14);
                cardTexts[c] = t;
            }

            // — Toasts (arriba a la derecha) —
            var toasts = NewText(canvasGo.transform, "Toasts",
                new Vector2(-14, -14), new Vector2(560, 200),
                TextAnchor.UpperRight, 15);

            // — Historial de comandos (abajo a la izquierda) —
            var history = NewText(canvasGo.transform, "CommandHistory",
                new Vector2(14, 14), new Vector2(420, 130),
                TextAnchor.LowerLeft, 13);

            // — Tarjeta de inspección (derecha) —
            var inspect = NewText(canvasGo.transform, "AntInspector",
                new Vector2(-14, 160), new Vector2(360, 200),
                TextAnchor.UpperRight, 14);
            var inspector = inspect.gameObject.AddComponent<Presenter.AntInspectorBehaviour>();
            inspector.Presenter = presenter;
            inspector.CardText = inspect;

            // — HUD layout: reparte el stream a todo —
            var hudGo = new GameObject("HudLayout");
            var hud = hudGo.AddComponent<Presenter.HudLayoutBehaviour>();
            hud.Presenter = presenter;
            hud.ColonyCardTexts = cardTexts;
            hud.ToastsText = toasts;
            hud.Inspector = inspector;
            hud.HistoryText = history;

            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("[SceneBootstrapper] Escena creada: guarda (Ctrl+S → Assets/Scenes/Game.unity) y pulsa Play. " +
                      "Publica antes el CLI: dotnet publish src/Tools/AntSim.Cli -c Release -o build/antsim");
        }

        /// <summary>Texto uGUI con las convenciones v1 (sin fuente custom).</summary>
        private static Text NewText(Transform parent, string name, Vector2 anchor,
            Vector2 size, TextAnchor align, int fontSize)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(
                anchor.x < 0 ? 1f : 0f, anchor.y < 0 ? 1f : 0f);
            rt.pivot = rt.anchorMin;
            rt.anchoredPosition = new Vector2(Mathf.Abs(anchor.x), Mathf.Abs(anchor.y));
            rt.sizeDelta = size;
            var t = go.AddComponent<Text>();
            t.text = "";
            t.alignment = align;
            t.fontSize = fontSize;
            t.color = new Color(0.92f, 0.90f, 0.85f);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.supportRichText = false;
            // Fuente por defecto (Liberation Sans en el editor); sin dependencias.
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (t.font == null) t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return t;
        }

        private static Material NewMat(Color c, string name)
        {
            var m = new Material(Shader.Find("Standard")) { name = name, color = c };
            AssetDatabase.CreateAsset(m, $"Assets/Materials/{name}.mat");
            return m;
        }

        private static Mesh PrimitiveMesh(PrimitiveType type, float scale, string name)
        {
            var go = GameObject.CreatePrimitive(type);
            var mesh = Object.Instantiate(go.GetComponent<MeshFilter>().sharedMesh);
            mesh.name = name;
            // Los meshes de primitivas vienen a escala 1: el presenter ya escala.
            if (scale != 1f)
            {
                var verts = mesh.vertices;
                for (int i = 0; i < verts.Length; i++) verts[i] *= scale;
                mesh.vertices = verts;
                mesh.RecalculateNormals();
            }
            Object.DestroyImmediate(go);
            AssetDatabase.CreateAsset(mesh, $"Assets/Materials/{name}.asset");
            return mesh;
        }
    }
}
#endif

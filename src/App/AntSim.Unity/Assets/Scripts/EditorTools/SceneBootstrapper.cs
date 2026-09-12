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
            cam.clearFlags = CameraClearFlags.SolidColor; // Unity 6: SolidCamera no existe
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
            // Canal E activo por defecto en la escena: el quad de feromonas ya
            // existe — sin este campo el canal quedaría apagado y el quad vacío.
            presenter.PheroEvery = 30;
            // Canal F (F5.0) apagado por defecto: requiere elegir hormiga;
            // se activa desde el inspector (InspectId + ActivEvery) o por click.

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

            // — Feromonas (F4.5): quad bajo las hormigas + RenderTexture del canal E —
            var pheroQuad = GameObject.CreatePrimitive(PrimitiveType.Plane);
            pheroQuad.name = "PheromoneTiles";
            pheroQuad.transform.position = new Vector3(128f, 0.02f, 128f);
            pheroQuad.transform.localScale = new Vector3(26f, 1f, 26f); // plane = 10 u ⇒ 256
            var pheroMat = NewMat(new Color(0.4f, 0.9f, 0.5f, 0f), "PheromoneMat");
            pheroMat.SetFloat("_Mode", 2f); // fade (transparente)
            var phero = pheroQuad.AddComponent<Presenter.PheromoneTileBehaviour>();
            phero.Presenter = presenter;
            phero.TargetMaterial = pheroMat;

            // — Canvas del HUD —
            var canvasGo = new GameObject("HUD Canvas");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGo.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasGo.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            // Unity 6: FindFirstObjectByType depende del orden de instancias —
            // deprecado; FindAnyObjectByType es el sustituto canónico.
            if (Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
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

            // — Click de selección (§5): raycast pantalla→mundo → PickNearest —
            var pickGo = new GameObject("AntPickClickHandler");
            var pick = pickGo.AddComponent<EditorTools.AntPickClickHandler>();
            pick.Presenter = presenter;
            pick.Inspector = inspector;

            // — Plan de intervención (F4.4): click-to-place de DropFood + resumen —
            var dropPlanText = NewText(canvasGo.transform, "DropPlan",
                new Vector2(14, 158), new Vector2(420, 46),
                TextAnchor.LowerLeft, 13);
            var dropGo = new GameObject("DropFoodClickHandler");
            var drop = dropGo.AddComponent<Presenter.DropFoodClickHandler>();
            drop.Presenter = presenter;
            hud.DropPlan = drop;
            hud.DropPlanText = dropPlanText;

            // — Salto de cámara por toast (contrato §1): ancla (x,y) del canal D —
            var jumpGo = new GameObject("ToastClickCameraJump");
            var jump = jumpGo.AddComponent<Presenter.ToastClickCameraJump>();
            jump.Hud = hud;

            // — F5.1: per-elemento — contenedor de toasts clicables + barras + botones —
            RectTransform toastContainer = CreateToastContainer(canvasGo.transform, hud);
            var stockImages = CreateStockBars(canvasGo.transform, hud, cardTexts.Length);
            var buttons = CreateNativeButtons(canvasGo.transform, hud);
            hud.BindNativeButtons(buttons[0], buttons[1], buttons[2]);

            // — Diálogo de importación (F4.3): tarjeta de cuarentena del oráculo —
            var importText = NewText(canvasGo.transform, "ImportDialog",
                new Vector2(-14, 380), new Vector2(560, 96),
                TextAnchor.UpperRight, 13);
            var importGo = new GameObject("ImportDialog");
            var import = importGo.AddComponent<Presenter.ImportDialogBehaviour>();
            import.Presenter = presenter;
            import.CliPath = presenter.CliPath;
            hud.ImportDialog = import;
            hud.ImportDialogText = importText;

            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("[SceneBootstrapper] Escena creada: guarda (Ctrl+S → Assets/Scenes/Game.unity) y pulsa Play. " +
                      "Publica antes el CLI: dotnet publish src/Tools/AntSim.Cli -c Release -o build/antsim");
        }

        // — F5.1: contenedor + plantilla de toast per-elemento (cada toast es un
        //   rect clicable con su ancla; el hit-test vive en el modelo puro) —
        private static RectTransform CreateToastContainer(Transform canvas, Presenter.HudLayoutBehaviour hud)
        {
            var containerGo = new GameObject("ToastContainer", typeof(RectTransform));
            containerGo.transform.SetParent(canvas, false);
            var c = (RectTransform)containerGo.transform;
            c.anchorMin = c.anchorMax = new Vector2(1f, 1f);
            c.pivot = new Vector2(1f, 1f);
            c.anchoredPosition = new Vector2(-14f, -14f);
            c.sizeDelta = new Vector2(560f, 200f);

            // Plantilla: Image de fondo (clicable) + Text — se instancia por toast.
            var tmplGo = new GameObject("ToastTemplate", typeof(RectTransform));
            tmplGo.transform.SetParent(c, false);
            var t = (RectTransform)tmplGo.transform;
            t.sizeDelta = new Vector2(560f, HudLayoutModel.ToastHeight);
            var bg = tmplGo.AddComponent<UnityEngine.UI.Image>();
            bg.color = new Color(0f, 0f, 0f, 0.55f);
            var txtGo = new GameObject("Text", typeof(RectTransform));
            txtGo.transform.SetParent(t, false);
            var trt = (RectTransform)txtGo.transform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(8f, 2f); trt.offsetMax = new Vector2(-8f, -2f);
            var text = txtGo.AddComponent<UnityEngine.UI.Text>();
            text.alignment = TextAnchor.MiddleLeft;
            text.fontSize = 14;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (text.font == null) text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            tmplGo.SetActive(false);

            hud.ToastContainer = c;
            hud.ToastTemplate = t;
            return c;
        }

        // — F5.1: barras de stock — un fondo + una Image con fillAmount por colonia —
        private static UnityEngine.UI.Image?[] CreateStockBars(Transform canvas,
            Presenter.HudLayoutBehaviour hud, int colonies)
        {
            var imgs = new UnityEngine.UI.Image?[colonies];
            for (int i = 0; i < colonies; i++)
            {
                var barGo = new GameObject($"StockBar_{i}", typeof(RectTransform));
                barGo.transform.SetParent(canvas, false);
                var rt = (RectTransform)barGo.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot = new Vector2(0f, 1f);
                rt.anchoredPosition = new Vector2(14f, -140f - i * 118f);
                rt.sizeDelta = new Vector2(300f, 10f);

                var back = barGo.AddComponent<UnityEngine.UI.Image>();
                back.color = new Color(0f, 0f, 0f, 0.4f);

                var fillGo = new GameObject("Fill", typeof(RectTransform));
                fillGo.transform.SetParent(barGo.transform, false);
                var frt = (RectTransform)fillGo.transform;
                frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one;
                frt.offsetMin = new Vector2(1f, 1f); frt.offsetMax = new Vector2(-1f, -1f);
                var fill = fillGo.AddComponent<UnityEngine.UI.Image>();
                fill.fillAmount = 0f;
                // El color lo pinta RenderStockBars según el modelo puro.
                imgs[i] = fill;
            }
            hud.StockBars = imgs;
            return imgs;
        }

        // — F5.1: botones nativos (Confirmar/Cancelar del import + Reiniciar con plan) —
        private static UnityEngine.UI.Button?[] CreateNativeButtons(Transform canvas, Presenter.HudLayoutBehaviour hud)
        {
            UnityEngine.UI.Button Make(string name, string label, Vector2 anchor, Vector2 pos, Vector2 size)
            {
                var go = new GameObject(name, typeof(RectTransform));
                go.transform.SetParent(canvas, false);
                var rt = (RectTransform)go.transform;
                rt.anchorMin = rt.anchorMax = anchor;
                rt.pivot = anchor;
                rt.anchoredPosition = pos;
                rt.sizeDelta = size;
                var img = go.AddComponent<UnityEngine.UI.Image>();
                img.color = new Color(0.18f, 0.18f, 0.16f, 0.95f);
                var btn = go.AddComponent<UnityEngine.UI.Button>();
                var txtGo = new GameObject("Label", typeof(RectTransform));
                txtGo.transform.SetParent(rt, false);
                var trt = (RectTransform)txtGo.transform;
                trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
                trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
                var text = txtGo.AddComponent<UnityEngine.UI.Text>();
                text.text = label;
                text.alignment = TextAnchor.MiddleCenter;
                text.fontSize = 14;
                text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (text.font == null) text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                return btn;
            }

            // Confirmar/Cancelar junto al diálogo de importación; Reiniciar junto al plan.
            var confirm = Make("ImportConfirmBtn", "Confirmar",
                new Vector2(1f, 1f), new Vector2(-594f, -380f), new Vector2(120f, 30f));
            var cancel = Make("ImportCancelBtn", "Cancelar",
                new Vector2(1f, 1f), new Vector2(-468f, -380f), new Vector2(120f, 30f));
            var restart = Make("RestartWithPlanBtn", "Reiniciar con plan",
                new Vector2(0f, 0f), new Vector2(448f, 14f), new Vector2(170f, 30f));
            return new UnityEngine.UI.Button?[] { confirm, cancel, restart };
        }

        private static class HudLayoutModel
        {
            public const float ToastHeight = Streaming.HudElementLayoutModel.ToastHeight;
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
            // La carpeta destino debe existir: en un proyecto recién abierto
            // Assets/Materials no existe y CreateAsset falla (visto en el Play pass).
            if (!AssetDatabase.IsValidFolder("Assets/Materials"))
                AssetDatabase.CreateFolder("Assets", "Materials");
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

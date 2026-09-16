#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using UnityEditor.SceneManagement;
using AntSim.Unity.Scripts.Streaming;

namespace AntSim.Unity.Scripts.EditorTools
{
    /// <summary>
    /// Multi-visor (F5.1bis, fase B del flujo del jugador): N simulaciones EN
    /// VIVO en una escena, cada una con su propio stream (proceso del CLI,
    /// semilla y pool independientes de los .antgenome de la fase A). Un comando
    /// de menú: Unity → AntSim → «Crear escena multi-visor».
    ///
    /// Cómo se separan las vistas (lo que hace posible ver 4 mundos a la vez):
    ///   · Cada vista es un rect de pantalla (Camera.rect) — la geometría la
    ///     decide MultiViewportModel (puro, testeado headless).
    ///   · Cada mundo vive en SU capa (Sim0..3): el suelo, los nidos y los
    ///     DrawMesh del presenter se crean en esa capa, y la cámara de la
    ///     vista solo ve su capa + UI. Sin capas, la cámara de la vista 0
    ///     dibujaría también los suelos de las otras (misma posición del
    ///     mundo: cuatro suelos superpuestos).
    ///   · Cada vista tiene su SimPresenterBehaviour con Seed/SeedPoolPath
    ///     propios: N procesos del CLI, un stream por vista.
    ///
    /// Los pools no se hardcodean: si existen en artifacts/, se reparten por
    /// vista en orden (los mismos que lista --mode presets); si no, las vistas
    /// salen sin sembrar (seed pools distintas por Seed) y el jugador asigna
    /// el pool desde el inspector antes de dar a Play.
    /// </summary>
    public static class MultiSimBootstrapper
    {
        private const int GridCells = 96;          // celdas del mundo (×8 u = 768)
        private const float PheroHeight = 0.25f;   // la misma altura que la escena simple

        /// <summary>Pools candidatos, en orden de preferencia (la cadena Fase 3ter
        /// con la que se validó la transferencia). Solo se usan los que EXISTEN.</summary>
        private static readonly string[] CandidatePools =
        {
            "artifacts/pretrain-warm-v2.antgenome",
            "artifacts/pretrain-warm-4.antgenome",
            "artifacts/pretrain-warm3.antgenome",
            "artifacts/pretrain-warm2.antgenome",
        };

        [MenuItem("AntSim/Crear escena multi-visor", priority = 1)]
        public static void CreateMultiSimScene()
        {
            CreateMultiSimScene(views: 4, baseSeed: 42);
        }

        /// <summary>Smoke del multi-visor (F5.1bis): 2 vistas que REPRODUCEN
        /// streams volcados a archivo (sin procesos del CLI en vivo). Ejecutable
        /// en batch: <c>-executeMethod …CreateMultiSimSmokeScene</c>. Cada vista
        /// usa <c>ReplayFile</c> — el mismo camino de reproducción del jugador
        /// sin red ni procesos, y determinista (los archivos son fijos).</summary>
        public static void CreateMultiSimSmokeScene()
        {
            CreateMultiSimScene(views: 2, baseSeed: 42, pools: new string?[] { null, null },
                replayFiles: new[]
                {
                    "artifacts/multiview-v0.jsonl",
                    "artifacts/multiview-v1.jsonl",
                });
        }

        /// <summary>Criterio de cierre de §2bis (F5.1bis): las CUATRO vistas con
        /// la cadena completa de pools (warm-v2 · hybrid · warm-4 · warm3 —
        /// una semilla distinta por vista) reproduciendo streams volcados.
        /// Ejecutable en batch: <c>-executeMethod …CreateMultiSimSmokeScene4</c>.
        /// Es el montaje del criterio: 4 mundos paralelos, 4 semáforos
        /// independientes, velocidad ≥2× verificable por la sonda.</summary>
        public static void CreateMultiSimSmokeScene4()
        {
            CreateMultiSimScene(views: 4, baseSeed: 42, pools: new string?[] { null, null, null, null },
                replayFiles: new[]
                {
                    "artifacts/multiview-v0.jsonl",
                    "artifacts/multiview-v1.jsonl",
                    "artifacts/multiview-v2.jsonl",
                    "artifacts/multiview-v3.jsonl",
                });
        }

        /// <summary>Criterio de cierre de F5.2a (5.2a.5): UNA vista Atta — el
        /// stream de la partida canónica de la cortadora (especies atta,lasius,
        /// hojas 100 %, warm-v2 en colonia 0, el mismo comando del pin CI
        /// check-atta-command.sh) volcado a artifacts/multiview-atta.jsonl y
        /// reproducido. La tarjeta de la vista debe mostrar el hongo de la
        /// colonia 0 (barra ocre) y la línea de cortes acumulados; la colonia 1
        /// Lasius SIN fungus y SIN línea de cortadora. Verificable en batch con
        /// <c>-executeMethod …MultiViewSmokeProbe.RunSmokeAtta</c>.</summary>
        public static void CreateMultiSimSmokeSceneAtta()
        {
            CreateMultiSimScene(views: 1, baseSeed: 42, pools: new string?[] { null },
                replayFiles: new[] { "artifacts/multiview-atta.jsonl" });
        }

        /// <summary>Monta la escena de N vistas. `pools` null = autodetección
        /// desde artifacts/ (una por vista, en orden; las vistas sobrantes sin
        /// pool). Lanza excepción con mensaje claro si N no está en 1..4.</summary>
        public static void CreateMultiSimScene(int views, ulong baseSeed,
            IReadOnlyList<string?>? pools = null, IReadOnlyList<string?>? replayFiles = null)
        {
            var layout = MultiViewportModel.Layout(views);   // valida 1..4
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            float world = WorldUnits.WorldSize(GridCells);
            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.15f;
            light.shadows = LightShadows.None;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            // — Pools: autodetección o los dados —
            var viewPools = new string?[views];
            if (pools != null)
            {
                for (int i = 0; i < views && i < pools.Count; i++) viewPools[i] = pools[i];
            }
            else
            {
                string? repo = RepoRoot();
                int k = 0;
                for (int i = 0; i < views; i++)
                {
                    while (k < CandidatePools.Length)
                    {
                        string cand = CandidatePools[k++];
                        if (repo != null && File.Exists(Path.Combine(repo, cand)))
                        { viewPools[i] = cand; break; }
                    }
                }
            }

            // — Una vista por elemento del layout (el presenter de cada una se
            //   guarda: la tarjeta compacta del HUD se alimenta de ÉL) —
            var presenters = new Presenter.SimPresenterBehaviour[layout.Count];
            for (int i = 0; i < layout.Count; i++)
            {
                var v = layout[i];
                string? replay = replayFiles != null && i < replayFiles.Count
                    ? replayFiles[i] : null;
                presenters[i] = BuildView(v, world, baseSeed + (ulong)i, viewPools[i], replay);
            }

            // — HUD común: rótulo + tarjeta compacta por vista —
            var canvasGo = new GameObject("HUD Canvas");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();
            if (Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<UnityEngine.EventSystems.EventSystem>();
                es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            }
            for (int i = 0; i < layout.Count; i++)
            {
                var v = layout[i];
                string replayName = replayFiles != null && i < replayFiles.Count && replayFiles[i] != null
                    ? Path.GetFileNameWithoutExtension(replayFiles[i]!) : null;
                string caption = MultiViewportModel.ViewCaption(
                    v.Label, baseSeed + (ulong)i,
                    replayName ?? (viewPools[i] != null ? Path.GetFileNameWithoutExtension(viewPools[i]!) : null));
                CreateViewLabel(canvasGo.transform, v, caption);
                CreateViewCard(canvasGo.transform, v, presenters[i]);
            }

            EnsureMaterialFolder();
            // Se guarda SOLA en SU archivo (nunca sobre Game.unity de la escena
            // simple) y se registra en build settings: la escena sobrevive a
            // cerrar el editor sin guardar y es comprobable como texto.
            EditorSceneManager.MarkSceneDirty(scene);
            const string ScenePath = "Assets/Scenes/MultiSim.unity";
            if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
                AssetDatabase.CreateFolder("Assets", "Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            RegisterInBuildSettings(ScenePath);
            Debug.Log($"[MultiSim] escena de {views} vistas creada y guardada en {ScenePath}. " +
                      "Pools: " + string.Join(", ", System.Array.ConvertAll(viewPools, p => p ?? "(sin)")));
        }

        /// <summary>Una vista: cámara con rect, mundo en su capa, presenter.
        /// Devuelve el presenter para que el HUD de la vista se le conecte.</summary>
        private static Presenter.SimPresenterBehaviour BuildView(MultiViewportModel.View v,
            float world, ulong seed, string? pool, string? replayFile = null)
        {
            int layer = MultiViewportModel.RenderLayer(v.Index);

            // — Cámara de la vista —
            var camGo = new GameObject($"Camera_V{v.Index}");
            camGo.layer = layer; // la cámara en sí no se renderiza; da igual, por claridad
            var cam = camGo.AddComponent<Camera>();
            camGo.transform.position = new Vector3(world * 0.5f, world * 0.1f, world * 0.5f);
            camGo.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            cam.orthographic = true;
            // Vista de UN tablero (cada vista encuadra su mundo entero): el
            // margen del 4% de la escena simple, más holgado porque el rect
            // puede no ser cuadrado (2 vistas: 1:2 de aspecto).
            cam.orthographicSize = world * 0.52f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = world * 2f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.22f, 0.175f, 0.13f);
            cam.rect = new Rect(v.X, v.Y, v.W, v.H);
            cam.cullingMask = MultiViewportModel.CameraMask(v.Index);
            cam.depth = v.Index; // orden de render estable entre vistas

            // — El mundo de la vista, TODO en su capa —
            var earth = new Color(0.42f, 0.33f, 0.23f);
            var gridLine = new Color(0.34f, 0.26f, 0.18f);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = $"Floor_V{v.Index}";
            SetLayerRecursive(floor, layer);
            floor.transform.position = new Vector3(world * 0.5f, 0f, world * 0.5f);
            floor.transform.localScale = new Vector3(world / 10f, 1f, world / 10f);
            DestroyCollider(floor);
            floor.GetComponent<Renderer>().sharedMaterial =
                NewGridMat($"FloorMat_V{v.Index}", earth, gridLine);

            var table = GameObject.CreatePrimitive(PrimitiveType.Plane);
            table.name = $"Table_V{v.Index}";
            SetLayerRecursive(table, layer);
            table.transform.position = new Vector3(world * 0.5f, -0.6f, world * 0.5f);
            table.transform.localScale = new Vector3(world * 2.6f / 10f, 1f, world * 2.6f / 10f);
            DestroyCollider(table);
            table.GetComponent<Renderer>().sharedMaterial =
                NewFlatMat(new Color(0.22f, 0.175f, 0.13f), $"TableMat_V{v.Index}");

            float nestR = world * 0.022f;
            var moundMat = NewFlatMat(new Color(0.30f, 0.25f, 0.20f), $"NestMoundMat_V{v.Index}");
            var accent0 = new Color(0.93f, 0.45f, 0.35f);
            var accent1 = new Color(0.42f, 0.63f, 0.95f);
            for (int c = 0; c < 2; c++)
            {
                float nx = world * (c + 1) / 3f;
                var mound = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                mound.name = $"NestMound_V{v.Index}_{c}";
                SetLayerRecursive(mound, layer);
                mound.transform.position = new Vector3(nx, 0.15f, world * 0.5f);
                mound.transform.localScale = new Vector3(nestR * 1.45f, 0.15f, nestR * 1.45f);
                DestroyCollider(mound);
                mound.GetComponent<Renderer>().sharedMaterial = moundMat;

                var nest = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                nest.name = $"Nest_V{v.Index}_{c}";
                SetLayerRecursive(nest, layer);
                nest.transform.position = new Vector3(nx, 0.45f, world * 0.5f);
                nest.transform.localScale = new Vector3(nestR, 0.3f, nestR);
                DestroyCollider(nest);
                nest.GetComponent<Renderer>().sharedMaterial =
                    NewFlatMat(c == 0 ? accent0 : accent1, $"NestMat_V{v.Index}_{c}");
            }

            // — Feromonas de la vista (misma receta que la escena simple) —
            var pheroQuad = GameObject.CreatePrimitive(PrimitiveType.Plane);
            pheroQuad.name = $"PheromoneTiles_V{v.Index}";
            SetLayerRecursive(pheroQuad, layer);
            pheroQuad.transform.position = new Vector3(world * 0.5f, PheroHeight, world * 0.5f);
            pheroQuad.transform.localScale = new Vector3(world / 10f, 1f, world / 10f);
            DestroyCollider(pheroQuad);
            var pheroMat = NewTransparentMat($"PheromoneMat_V{v.Index}");
            pheroQuad.GetComponent<Renderer>().sharedMaterial = pheroMat;

            // — Presenter de la vista: su stream, su capa de render —
            var presenterGo = new GameObject($"SimPresenter_V{v.Index}");
            var presenter = presenterGo.AddComponent<Presenter.SimPresenterBehaviour>();
            presenter.CliPath = "build/antsim";
            presenter.Grid = GridCells;
            presenter.Colonies = 2;
            // Las N partidas comparten horizonte (7200 ticks); velocidad por
            // vista con +/= (SpeedBoost) o el Speed del inspector.
            presenter.Ticks = 7200;
            presenter.FrameEvery = 2;   // 15 Hz de canal A por vista: N streams van finos
            presenter.Seed = seed;
            presenter.SeedPoolPath = pool;
            presenter.ReplayFile = replayFile; // smoke: reproducir archivo, no CLI
            presenter.PheroEvery = 30;
            presenter.RenderLayer = layer;
            presenter.AntMesh = PrimitiveMeshAsset(PrimitiveType.Capsule, $"AntMesh_V{v.Index}");
            presenter.ItemMesh = PrimitiveMeshAsset(PrimitiveType.Sphere, $"ItemMesh_V{v.Index}");
            presenter.AntMaterial = NewFlatMat(new Color(0.35f, 0.16f, 0.10f), $"AntMat_V{v.Index}");
            presenter.CarrierMaterial = NewFlatMat(new Color(0.98f, 0.72f, 0.16f), $"CarrierMat_V{v.Index}");
            presenter.ItemMaterial = NewFlatMat(new Color(0.36f, 0.78f, 0.36f), $"ItemMat_V{v.Index}");
            presenter.LeafMaterial = NewFlatMat(new Color(0.15f, 0.50f, 0.12f), $"LeafMat_V{v.Index}");
            presenter.BiteMaterial = NewFlatMat(new Color(0.30f, 0.15f, 0.05f), $"BiteMat_V{v.Index}");
            presenter.AntScale = 0f;
            presenter.ActorLift = world * 0.0008f;

            var phero = pheroQuad.AddComponent<Presenter.PheromoneTileBehaviour>();
            phero.Presenter = presenter;
            phero.TargetMaterial = pheroMat;
            return presenter;
        }

        /// <summary>Rótulo de la vista, anclado a SU rect del canvas (el HUD
        /// común mapea pantalla→canvas con los mismos anchos normalizados).
        /// Un GameObject solo admite UN Graphic: el fondo (Image) va en el padre
        /// y el texto en un hijo — dos Graphics en el mismo GO no se puede
        /// (destapado en batch: «a 'Image' is already added to the game object»)."</summary>
        private static void CreateViewLabel(Transform canvas, MultiViewportModel.View v,
            string caption)
        {
            var go = new GameObject($"ViewLabel_{v.Index}", typeof(RectTransform));
            go.transform.SetParent(canvas, false);
            var rt = (RectTransform)go.transform;
            // Mismos gaps que la cámara: el rótulo queda DENTRO del rect de su vista.
            rt.anchorMin = new Vector2(v.X, 1f - (v.Y + v.H));
            rt.anchorMax = new Vector2(v.X + v.W, 1f - v.Y);
            rt.offsetMin = new Vector2(12f, -34f);
            rt.offsetMax = new Vector2(-12f, -6f);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.07f, 0.075f, 0.09f, 0.72f);

            // Texto en un HIJO (un Graphic por GameObject).
            var tgo = new GameObject("Caption", typeof(RectTransform));
            tgo.transform.SetParent(go.transform, false);
            var trt = (RectTransform)tgo.transform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(10f, 2f);
            trt.offsetMax = new Vector2(-10f, -2f);
            var t = tgo.AddComponent<Text>();
            t.font = DefaultFont();
            t.fontSize = 17;
            t.fontStyle = FontStyle.Bold;
            t.color = new Color(0.93f, 0.91f, 0.86f);
            t.alignment = TextAnchor.MiddleLeft;
            t.horizontalOverflow = HorizontalWrapMode.Wrap; // Wrap trunca al ancho del rect
            t.text = caption;
        }
        /// <summary>Tarjeta compacta de la vista: fondo (Image) + texto por
        /// colonia + barra de reserva por colonia, anclada a SU rect del canvas,
        /// alimentada por SU presenter. Un Graphic por GameObject (lección del
        /// rótulo): panel → hijo Caption → hijos Bar_c.</summary>
        private static void CreateViewCard(Transform canvas, MultiViewportModel.View v,
            Presenter.SimPresenterBehaviour presenter)
        {
            var go = new GameObject($"ViewCard_{v.Index}", typeof(RectTransform));
            go.transform.SetParent(canvas, false);
            var rt = (RectTransform)go.transform;
            // Bajo el rótulo, dentro del rect de la vista: el panel ocupa el
            // ancho del viewport menos márgenes, altura fija de 3 líneas.
            rt.anchorMin = new Vector2(v.X, 1f - (v.Y + v.H));
            rt.anchorMax = new Vector2(v.X + v.W, 1f - v.Y);
            rt.pivot = new Vector2(0f, 1f);
            // Anclado arriba-izquierda del rect de la vista (bajo el rótulo):
            // la altura la manda sizeDelta porque anchorMin.y == anchorMax.y no
            // va aquí — usamos anclas sueltas con offsets explícitos.
            rt.anchorMin = rt.anchorMax; // punto único arriba-izq de la vista
            rt.offsetMin = new Vector2(12f, -106f);  // 66 de alto + 40 del rótulo
            rt.offsetMax = new Vector2(-12f, -40f);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.07f, 0.075f, 0.09f, 0.66f);

            // Texto (hijo — un Graphic por GO).
            var tgo = new GameObject("Body", typeof(RectTransform));
            tgo.transform.SetParent(go.transform, false);
            var trt = (RectTransform)tgo.transform;
            // Reserva 18 px a la izquierda para las barras verticales por colonia.
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(22f, 4f);
            trt.offsetMax = new Vector2(-8f, -4f);
            var body = tgo.AddComponent<Text>();
            body.font = DefaultFont();
            body.fontSize = 15;
            body.color = new Color(0.93f, 0.91f, 0.86f);
            body.alignment = TextAnchor.UpperLeft;
            body.horizontalOverflow = HorizontalWrapMode.Wrap;
            body.verticalOverflow = VerticalWrapMode.Overflow;
            body.supportRichText = true; // <b> del compacto

            // Barras de reserva (hijos): una franja vertical POR colonia a la
            // izquierda del texto — a media pantalla, una barra horizontal roba
            // ancho que la línea necesita.
            var bars = new Image?[4];
            for (int c = 0; c < bars.Length; c++)
            {
                var bgo = new GameObject($"Bar_{c}", typeof(RectTransform));
                bgo.transform.SetParent(go.transform, false);
                var brt = (RectTransform)bgo.transform;
                brt.anchorMin = new Vector2(0f, 0f);
                brt.anchorMax = new Vector2(0f, 1f);
                brt.pivot = new Vector2(0f, 0.5f);
                brt.anchoredPosition = new Vector2(8f + c * 4f, 0f);
                brt.sizeDelta = new Vector2(3f, -12f);
                var fill = bgo.AddComponent<Image>();
                fill.color = new Color(0.45f, 0.8f, 0.4f);
                bars[c] = fill;
            }

            var card = go.AddComponent<Presenter.ViewCardBehaviour>();
            card.Presenter = presenter;
            card.Body = body;
            card.StockBars = bars;

            // F5.2a.5: barras del HONGO — una segunda franja por colonia, a la
            // derecha de las de reserva (8 px de separación). Mismo patrón: se
            // crean SIEMPRE (una por id posible) y ViewCardBehaviour las deja al
            // 0 para colonias sin hongo. Solo en la escena Atta hoy, pero el
            // componente las pinta en cualquier montaje que las conecte.
            var fungusBars = new Image?[4];
            for (int c = 0; c < fungusBars.Length; c++)
            {
                var bgo = new GameObject($"FungusBar_{c}", typeof(RectTransform));
                bgo.transform.SetParent(go.transform, false);
                var brt = (RectTransform)bgo.transform;
                brt.anchorMin = new Vector2(0f, 0f);
                brt.anchorMax = new Vector2(0f, 1f);
                brt.pivot = new Vector2(0f, 0.5f);
                brt.anchoredPosition = new Vector2(20f + c * 4f, 0f);
                brt.sizeDelta = new Vector2(3f, -12f);
                var fill = bgo.AddComponent<Image>();
                fill.color = new Color(0.82f, 0.62f, 0.30f); // ocre
                fungusBars[c] = fill;
            }
            card.FungusBars = fungusBars;
        }

        // ── Primitivas propias (replican la receta de SceneBootstrapper con
        //    nombres por vista para que dos vistas no pisen el mismo asset) ──

        private static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform)
                SetLayerRecursive(child.gameObject, layer);
        }

        private static void DestroyCollider(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
        }

        private static void EnsureMaterialFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Materials"))
                AssetDatabase.CreateFolder("Assets", "Materials");
        }

        private static void CreateOrReplaceAsset(UnityEngine.Object asset, string path)
        {
            EnsureMaterialFolder();
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path) != null)
                AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(asset, path);
        }

        private static void RegisterInBuildSettings(string scenePath)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (scenes.Exists(s => s.path == scenePath)) return;
            scenes.Insert(0, new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static Texture2D NewGridTexture(Color fill, Color line, string name)
        {
            const int N = 64;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { name = name };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                    px[y * N + x] = (x == 0 || y == 0) ? (Color32)line : (Color32)fill;
            tex.SetPixels32(px);
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Bilinear;
            tex.Apply();
            CreateOrReplaceAsset(tex, $"Assets/Materials/{name}.asset");
            return tex;
        }

        private static Material NewGridMat(string name, Color fill, Color line)
        {
            var tex = NewGridTexture(fill, line, name + "_Tex");
            var sh = Shader.Find("Unlit/Texture");
            if (sh == null) sh = Shader.Find("Standard");
            var m = new Material(sh!) { name = name, mainTexture = tex };
            m.mainTextureScale = new Vector2(24f, 24f); // GridTiles de la escena simple
            if (m.HasProperty("_Color")) m.color = Color.white;
            EnsureMaterialFolder();
            CreateOrReplaceAsset(m, $"Assets/Materials/{name}.mat");
            return m;
        }

        private static Material NewFlatMat(Color c, string name)
        {
            var sh = Shader.Find("Unlit/Color");
            if (sh == null) sh = Shader.Find("Unlit/Texture");
            if (sh == null) sh = Shader.Find("Standard");
            var m = new Material(sh!) { name = name };
            m.color = c;
            if (sh != null && sh.name == "Unlit/Color") m.SetColor("_Color", c);
            if (sh != null && sh.name == "Standard")
            {
                m.SetColor("_Color", Color.black);
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", c);
            }
            EnsureMaterialFolder();
            CreateOrReplaceAsset(m, $"Assets/Materials/{name}.mat");
            return m;
        }

        private static Material NewTransparentMat(string name)
        {
            Shader? sh = Shader.Find("Unlit/Transparent");
            if (sh == null) sh = Shader.Find("Legacy Shaders/Transparent/Diffuse");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            if (sh == null) sh = Shader.Find("Standard");
            var m = new Material(sh!) { name = name };
            if (sh != null && sh.name == "Standard")
            {
                m.SetFloat("_Mode", 2f);
                m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetInt("_ZWrite", 0);
                m.DisableKeyword("_ALPHATEST_ON");
                m.EnableKeyword("_ALPHABLEND_ON");
                m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
            // Textura 1×1 transparente: nace invisible (lección del Play pass:
            // unlit-transparente sin textura es blanco OPACO).
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false) { name = name + "_Empty" };
            tex.SetPixels32(new[] { new Color32(255, 255, 255, 0) });
            tex.Apply();
            m.mainTexture = tex;
            if (m.HasProperty("_Color")) m.color = Color.white;
            EnsureMaterialFolder();
            CreateOrReplaceAsset(tex, $"Assets/Materials/{name}_Empty.asset");
            CreateOrReplaceAsset(m, $"Assets/Materials/{name}.mat");
            return m;
        }

        private static Mesh PrimitiveMeshAsset(PrimitiveType type, string name)
        {
            var go = GameObject.CreatePrimitive(type);
            var mesh = Object.Instantiate(go.GetComponent<MeshFilter>().sharedMesh);
            mesh.name = name;
            Object.DestroyImmediate(go);
            EnsureMaterialFolder();
            CreateOrReplaceAsset(mesh, $"Assets/Materials/{name}.asset");
            return mesh;
        }

        private static Font DefaultFont()
        {
            // La misma vía que la escena simple, con su mismo respaldo: Unity 6
            // usa LegacyRuntime.ttf, versiones previas Arial.ttf (GetBuiltinResource
            // lanza en vez de devolver null si el nombre no existe — probado en
            // el Play pass; por eso el try y no el null-check).
            try
            {
                var f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (f != null) return f;
            }
            catch { /* Unity < 6: cae a Arial */ }
            return Resources.GetBuiltinResource<Font>("Arial.ttf");
        }

        /// <summary>Repo root desde el proyecto (la misma regla que
        /// RepoPathResolver usa para el CLI): sube hasta encontrar AntSim.slnx.</summary>
        private static string? RepoRoot()
        {
            var dir = Path.GetDirectoryName(Application.dataPath);
            for (int i = 0; i < 6 && dir != null; i++)
            {
                if (File.Exists(Path.Combine(dir, "AntSim.slnx"))) return dir;
                dir = Path.GetDirectoryName(dir);
            }
            return null;
        }
    }
}
#endif

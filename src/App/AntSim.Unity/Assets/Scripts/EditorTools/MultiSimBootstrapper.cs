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
        /// <summary>Escena del MEDIDOR DE RENDIMIENTO (F5.3 rodaja 3): las CUATRO
        /// vistas con los pools reales de la cadena Fase 3ter, en el grid del modo
        /// juego (256) y sin reproducción de archivo — cada vista lanza su CLI, que
        /// es como juega el jugador. Son **8 colonias en pantalla** (4 × 2) y la
        /// carga de trabajo del mundo real, que es lo que hay que medir para el
        /// criterio de salida de la sub-fase. Ejecutable en batch:
        /// <c>-executeMethod …MultiSimBootstrapper.CreateMultiSimPerfScene</c>.</summary>
        public static void CreateMultiSimPerfScene() => CreateMultiSimPerfScene(PerfGridCells);

        /// <summary>Grid del medidor de rendimiento (el del modo juego).</summary>
        public const int PerfGridCells = 256;

        /// <summary>Horizonte del medidor: 12 000 ticks bastan para un mundo
        /// forrajeado (~200 hormigas por colonia) y mantienen el búfer de las
        /// cuatro vistas dentro de un orden de magnitud razonable.</summary>
        public const int PerfTicks = 12000;

        /// <summary>Canal A cada 5 ticks en el medidor: el COSTE de pintar depende de
        /// las hormigas visibles, no de la frecuencia del muestreo, y con 4 streams a
        /// 256² el canal cada tick multiplicaría por 5 la memoria del búfer.</summary>
        public const int PerfFrameEvery = 5;

        /// <summary>
        /// Velocidad de reproducción del medidor (F6). Antes salía del DEFAULT del
        /// presenter (10×); con la partida ya a 120× hay que ponerla EXPLÍCITA,
        /// porque el medidor mide el coste del sistema con 4 streams y a 120× cada
        /// vista consumiría 12 veces más ticks por segundo — otra carga, otro
        /// número. La escena versionada ya declara 10×: este campo es lo que hace
        /// que regenerarla no cambie la medida.
        /// </summary>
        public const float PerfSpeed = 10f;

        public static void CreateMultiSimPerfScene(int gridCells)
            => CreateMultiSimPerfScene(gridCells, PerfTicks);

        /// <summary>El medidor con un horizonte explícito. Existe por la medida del
        /// SISTEMA COMPLETO (F5.3, 6ª pieza): para que el CLI siga simulando durante
        /// TODA la ventana hay que darle mundo de sobra — con el horizonte del
        /// criterio el mundo se acaba en el calentamiento y lo que se mediría sería un
        /// player solo (la sonda lo detecta y suspende, pero mejor no llegar ahí).</summary>
        public static void CreateMultiSimPerfScene(int gridCells, int ticks)
            => CreateMultiSimPerfScene(gridCells, ticks, PerfColoniesPerView);

        /// <summary>Colonias por vista del medidor: el caso de carga del criterio.
        /// El barrido del precio del tick (F5.3, §4.10) lo mueve para saber cuántas
        /// colonias caben en los núcleos que el sistema consume.</summary>
        public const int PerfColoniesPerView = 2;

        /// <summary>El medidor con las tres magnitudes explícitas: grid, horizonte y
        /// colonias por vista. Las tres cambian el COSTE del mundo (que es lo que se
        /// mide), así que las tres tienen que poder declararse desde fuera.</summary>
        public static void CreateMultiSimPerfScene(int gridCells, int ticks, int coloniesPerView)
        {
            CreateMultiSimScene(views: 4, baseSeed: 42, pools: null, replayFiles: null,
                gridCells: gridCells, frameEvery: PerfFrameEvery, ticks: ticks,
                coloniesPerView: coloniesPerView);
        }

        public static void CreateMultiSimScene(int views, ulong baseSeed,
            IReadOnlyList<string?>? pools = null, IReadOnlyList<string?>? replayFiles = null,
            int gridCells = GridCells, int frameEvery = 2, int ticks = 36000,
            int coloniesPerView = 2)
        {
            var layout = MultiViewportModel.Layout(views);   // valida 1..4
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            float world = WorldUnits.WorldSize(gridCells);
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
                presenters[i] = BuildView(v, world, baseSeed + (ulong)i, viewPools[i], replay,
                    gridCells, frameEvery, ticks, coloniesPerView);
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
                string? replayName = replayFiles != null && i < replayFiles.Count && replayFiles[i] != null
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
            // CIERRE CANÓNICO: TODAS las variantes de este montador (menú, smoke,
            // medidor, atta) escriben la MISMA escena, así que el cierre va aquí,
            // en el único camino que las guarda — si no, el medidor dejaría
            // `MultiSim.unity` con anclas de sesión en cada pasada (ver
            // GeneratedAssets).
            GeneratedAssets.CloseWithCanonicalForm(ScenePath);
            Debug.Log($"[MultiSim] escena de {views} vistas creada y guardada en {ScenePath}. " +
                      "Pools: " + string.Join(", ", System.Array.ConvertAll(viewPools, p => p ?? "(sin)")));
        }

        /// <summary>Una vista: cámara con rect, mundo en su capa, presenter.
        /// Devuelve el presenter para que el HUD de la vista se le conecte.</summary>
        private static Presenter.SimPresenterBehaviour BuildView(MultiViewportModel.View v,
            float world, ulong seed, string? pool, string? replayFile = null,
            int gridCells = GridCells, int frameEvery = 2, int ticks = 36000,
            int coloniesPerView = 2)
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
            // Los materiales no se eligen aquí: se piden al MISMO modelo que la
            // escena simple, con el nombre de la vista (GameMaterialModel.View).
            // Así las dos escenas no pueden pintar colores distintos del mismo
            // material —antes estaban escritos dos veces— y el contrato de assets
            // comprueba las dos familias de `.mat`.
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = $"Floor_V{v.Index}";
            SetLayerRecursive(floor, layer);
            floor.transform.position = new Vector3(world * 0.5f, 0f, world * 0.5f);
            floor.transform.localScale = new Vector3(world / 10f, 1f, world / 10f);
            DestroyCollider(floor);
            floor.GetComponent<Renderer>().sharedMaterial =
                NewGridMat(UI.GameMaterialModel.ByName("FloorMat").View(v.Index),
                    UI.GameMaterialModel.GridTextureName(v.Index));

            var table = GameObject.CreatePrimitive(PrimitiveType.Plane);
            table.name = $"Table_V{v.Index}";
            SetLayerRecursive(table, layer);
            table.transform.position = new Vector3(world * 0.5f, -0.6f, world * 0.5f);
            table.transform.localScale = new Vector3(world * 2.6f / 10f, 1f, world * 2.6f / 10f);
            DestroyCollider(table);
            table.GetComponent<Renderer>().sharedMaterial =
                NewFlatMat(UI.GameMaterialModel.ByName("TableMat").View(v.Index));

            float nestR = world * 0.022f;
            var moundMat = NewFlatMat(UI.GameMaterialModel.ByName("NestMoundMat").View(v.Index));
            // Los nidos van donde los pone el MUNDO (el barrido de colonias de §4.10
            // mueve cuántas hay): `WorldSim.CreateColony` usa
            // WorldWidth × (id+1)/(colonyCount+1) sobre la línea media, y con 2
            // colonias eso es exactamente 1/3 y 2/3 — el mismo sitio de siempre.
            for (int c = 0; c < coloniesPerView; c++)
            {
                float nx = world * (c + 1) / (coloniesPerView + 1);
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
                // Dos acentos alternos: con más colonias no hay un color por colonia
                // (y el presenter solo reparte dos materiales por colonia), así que la
                // decoración alterna en vez de inventar una paleta nueva.
                nest.GetComponent<Renderer>().sharedMaterial =
                    NewFlatMat(UI.GameMaterialModel.Nest(c).View(v.Index));
            }

            // — Feromonas de la vista (misma receta que la escena simple) —
            var pheroQuad = GameObject.CreatePrimitive(PrimitiveType.Plane);
            pheroQuad.name = $"PheromoneTiles_V{v.Index}";
            SetLayerRecursive(pheroQuad, layer);
            pheroQuad.transform.position = new Vector3(world * 0.5f, PheroHeight, world * 0.5f);
            pheroQuad.transform.localScale = new Vector3(world / 10f, 1f, world / 10f);
            DestroyCollider(pheroQuad);
            var pheroMat = NewTransparentMat(UI.GameMaterialModel.ByName("PheromoneMat").View(v.Index),
                UI.GameMaterialModel.ReposeTextureName(v.Index));
            pheroQuad.GetComponent<Renderer>().sharedMaterial = pheroMat;

            // — Presenter de la vista: su stream, su capa de render —
            var presenterGo = new GameObject($"SimPresenter_V{v.Index}");
            var presenter = presenterGo.AddComponent<Presenter.SimPresenterBehaviour>();
            presenter.CliPath = "build/antsim";
            presenter.Grid = gridCells;
            presenter.Colonies = coloniesPerView;
            // Las N partidas comparten horizonte (7200 ticks); velocidad por
            // vista con +/= (SpeedBoost) o el Speed del inspector.
            presenter.Ticks = ticks;
            presenter.Speed = PerfSpeed;         // el medidor no hereda la base de 120×
            presenter.FrameEvery = frameEvery;   // 15 Hz de canal A por vista: N streams van finos
            presenter.Seed = seed;
            presenter.SeedPoolPath = pool;
            presenter.ReplayFile = replayFile; // smoke: reproducir archivo, no CLI
            // F8: el medidor mide el mundo CLÁSICO — el reclutamiento (cúmulos +
            // rastro de comida) es conducta de PARTIDA y cambiaría lo que se mide.
            presenter.Recruitment = false;
            presenter.PheroEvery = 30;
            presenter.RenderLayer = layer;
            // Mismas siluetas PROPIAS que la escena simple, con nombre por vista
            // (el generador es idempotente: si la malla ya está y coincide, no la
            // toca). La caja es la de las primitivas, así que la escala de abajo
            // sigue valiendo.
            presenter.AntMesh = GameAssetBuilder.EnsureActorMesh(
                UI.GameSpriteModel.Sprite.Ant, $"AntMesh_V{v.Index}");
            presenter.ItemMesh = GameAssetBuilder.EnsureActorMesh(
                UI.GameSpriteModel.Sprite.Load, $"ItemMesh_V{v.Index}");
            presenter.LeafMesh = GameAssetBuilder.EnsureActorMesh(
                UI.GameSpriteModel.Sprite.Leaf, $"LeafMesh_V{v.Index}");
            presenter.AntMaterial = NewFlatMat(UI.GameMaterialModel.ByName("AntMat").View(v.Index));
            presenter.CarrierMaterial = NewFlatMat(UI.GameMaterialModel.ByName("CarrierMat").View(v.Index));
            var antMat0 = NewFlatMat(UI.GameMaterialModel.ByName("AntMatColony0").View(v.Index));
            var carrierMat0 = NewFlatMat(UI.GameMaterialModel.ByName("CarrierMatColony0").View(v.Index));
            var antMat1 = NewFlatMat(UI.GameMaterialModel.ByName("AntMatColony1").View(v.Index));
            var carrierMat1 = NewFlatMat(UI.GameMaterialModel.ByName("CarrierMatColony1").View(v.Index));
            presenter.ColonyAntMaterials = new Material[] { antMat0, antMat1 };
            presenter.ColonyCarrierMaterials = new Material[] { carrierMat0, carrierMat1 };
            presenter.ItemMaterial = NewFlatMat(UI.GameMaterialModel.ByName("ItemMat").View(v.Index));
            presenter.LeafMaterial = NewFlatMat(UI.GameMaterialModel.ByName("LeafMat").View(v.Index));
            presenter.BiteMaterial = NewFlatMat(UI.GameMaterialModel.ByName("BiteMat").View(v.Index));
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
            //
            // Las anclas de la UI van de ABAJO arriba (0 = fondo del canvas) igual que
            // el rect de la cámara: el borde SUPERIOR de la vista es v.Y + v.H, y las
            // dos anclas lo comparten porque el rótulo es una banda colgada de ese
            // borde (la altura la dan los offsets, no el ancla). Aquí había un
            // `1 − (…)` que espejaba el HUD en vertical: el rótulo y la tarjeta de
            // cada vista caían sobre el viewport VECINO y, con anclas estiradas y
            // offsets de banda, el panel se extendía cientos de unidades (medido con la
            // sonda del player, 2026-09-20: rótulo y tarjeta de 606 unidades sobre un
            // tablero de 540, y la tarjeta de la vista 0 en la mitad de ABAJO).
            rt.anchorMin = new Vector2(v.X, v.Y + v.H);
            rt.anchorMax = new Vector2(v.X + v.W, v.Y + v.H);
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
            //
            // El panel se ESTIRA al rect de su vista (mismas anclas que el rótulo) y
            // los offsets recortan los márgenes y dejan la banda del rótulo fuera. Aquí
            // había un `rt.anchorMin = rt.anchorMax` que colapsaba las anclas a un
            // punto: con anclas de punto el rect lo manda el `sizeDelta` de los offsets
            // (−12−12 = −24) y el panel quedaba de ANCHO NEGATIVO — el texto, estirado a
            // ese rect, salía de −54 y uGUI no generaba ni un glifo: la tarjeta de
            // colonia del multi-visor no pintaba NADA (medido con la sonda del player,
            // 2026-09-20: «0 bandas de tinta», rect −54×58).
            rt.anchorMin = new Vector2(v.X, v.Y + v.H);
            rt.anchorMax = new Vector2(v.X + v.W, v.Y + v.H);
            rt.pivot = new Vector2(0f, 1f);
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

        /// <summary>
        /// Registra la escena del multi-visor en build settings SIN ponerse la
        /// primera: la primera escena de la lista es la que arranca un player, y
        /// esa es la del JUEGO (la registra <c>SceneBootstrapper</c>). Con
        /// <c>Insert(0)</c> —como estaba— regenerar el multi-visor cambiaba en
        /// silencio la escena de arranque: lo destapó el medidor de rendimiento, que
        /// regenera esta escena y dejó `EditorBuildSettings` con MultiSim delante de
        /// Game. Se AÑADE al final, que es el sitio de una escena auxiliar.
        /// </summary>
        private static void RegisterInBuildSettings(string scenePath)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (scenes.Exists(s => s.path == scenePath)) return;
            scenes.Add(new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        /// <summary>Misma rejilla que la escena simple, del MISMO modelo
        /// (`GameMaterialModel`): una vista no inventa su suelo.</summary>
        private static Texture2D NewGridTexture(string name)
        {
            const int N = UI.GameMaterialModel.GridSize;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { name = name };
            var linea = UI.GameMaterialModel.GridLine;
            var relleno = UI.GameMaterialModel.GridFill;
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                    px[y * N + x] = UI.GameMaterialModel.IsGridLine(x, y)
                        ? new Color32(linea.R, linea.G, linea.B, linea.A)
                        : new Color32(relleno.R, relleno.G, relleno.B, relleno.A);
            tex.SetPixels32(px);
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Bilinear;
            tex.Apply();
            CreateOrReplaceAsset(tex, $"Assets/Materials/{name}.asset");
            return tex;
        }

        private static Material NewGridMat(UI.GameMaterialModel.Spec spec, string textureName)
        {
            var tex = NewGridTexture(textureName);
            Shader? sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Unlit/Texture");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");

            float tiles = UI.GameMaterialModel.GridTiles;   // las mismas que la escena simple
            var m = new Material(sh!) { name = spec.Name, mainTexture = tex };
            m.mainTextureScale = new Vector2(tiles, tiles);
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            if (m.HasProperty("_BaseMap_ST")) m.SetVector("_BaseMap_ST", new Vector4(tiles, tiles, 0, 0));
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", ToColor(spec.Color));
            if (m.HasProperty("_Color")) m.color = ToColor(spec.Color);
            EnsureMaterialFolder();
            CreateOrReplaceAsset(m, $"Assets/Materials/{spec.Name}.mat");
            return m;
        }

        /// <summary>El color del MODELO, en el tipo del motor.</summary>
        private static Color ToColor(UI.GameMaterialModel.Rgba c) => new Color(c.R, c.G, c.B, c.A);

        private static Material NewFlatMat(UI.GameMaterialModel.Spec spec)
        {
            Shader? sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Unlit/Color");
            if (sh == null) sh = Shader.Find("Unlit/Texture");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");

            var c = ToColor(spec.Color);
            var m = new Material(sh!) { name = spec.Name };
            // Instancing por construcción (F5.3 rodaja 3): el presenter dibuja los
            // actores con Graphics.DrawMeshInstanced y ese API LANZA si el material
            // no lo tiene activado. Dejarlo al presenter era frágil: la escena
            // montada por el bootstrapper se quedaba sin hormigas con una excepción
            // por frame (defecto destapado por el medidor de rendimiento). El flag
            // sale del modelo (Flat ⇒ instancing).
            m.enableInstancing = spec.Instancing;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            m.color = c;
            if (sh != null && sh.name == "Standard")
            {
                m.SetColor("_Color", Color.black);
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", c);
            }
            EnsureMaterialFolder();
            CreateOrReplaceAsset(m, $"Assets/Materials/{spec.Name}.mat");
            return m;
        }

        private static Material NewTransparentMat(UI.GameMaterialModel.Spec spec, string textureName)
        {
            Shader? sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Unlit/Transparent");
            if (sh == null) sh = Shader.Find("Legacy Shaders/Transparent/Diffuse");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");

            var m = new Material(sh!) { name = spec.Name };
            if (sh != null && sh.name.StartsWith("Universal Render Pipeline"))
            {
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", 0f);
                m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetInt("_ZWrite", 0);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
            }
            else if (sh != null && sh.name == "Standard")
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
            //
            // IDEMPOTENTE: si el asset ya existe se reutiliza SU instancia. Antes
            // se pasaba por CreateOrReplaceAsset (borrar + crear) en cada pasada y
            // eso deja referencias muertas: un material que ya apuntaba a la
            // instancia anterior serializa «m_Texture: {fileID: 0}» en la siguiente
            // salvada. El caso observado fue el quad de feromonas de la escena
            // simple; la corrección hermana lleva la misma razón.
            var emptyPath = $"Assets/Materials/{textureName}.asset";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(emptyPath);
            if (tex == null)
            {
                var px = UI.GameMaterialModel.ReposePixel;   // blanco, alpha 0: invisible
                tex = new Texture2D(1, 1, TextureFormat.RGBA32, false) { name = textureName };
                tex.SetPixels32(new[] { new Color32(px.R, px.G, px.B, px.A) });
                tex.Apply();
                EnsureMaterialFolder();
                AssetDatabase.CreateAsset(tex, emptyPath);
            }
            m.mainTexture = tex;
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            if (m.HasProperty("_Color")) m.color = ToColor(spec.Color);
            CreateOrReplaceAsset(m, $"Assets/Materials/{spec.Name}.mat");
            return m;
        }

        /// <summary>La fuente del JUEGO, la misma que la escena simple (una sola
        /// tipografía para las dos escenas: es la identidad del proyecto, no una
        /// elección por montaje).</summary>
        private static Font DefaultFont() => GameAssetBuilder.EnsureFont();

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

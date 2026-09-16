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

            // Grid del mundo en CELDAS. Ojo: el Core simula en UNIDADES y su lado
            // es grid × WorldUnits.PerCell (SimConstants.CellSizeUnits = 8), no
            // `grid` — dimensionar la escena con `grid` dejaba el mundo 8× fuera de
            // cámara (bug detectado en el Play pass con el presente vivo).
            const int GridCells = 96;
            float world = Streaming.WorldUnits.WorldSize(GridCells); // 768 u para grid 96

            // — Cámara: vista cenital que cubre el mundo ENTERO —
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            camGo.transform.position = new Vector3(world * 0.5f, world * 0.1f, world * 0.5f);
            camGo.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            cam.orthographic = true;
            // El tablero es CUADRADO y la vista es ancha: ajustarlo por el lado
            // largo deja franjas laterales, así que se encuadra por la altura (4%
            // de margen) y esas franjas las cubre la MESA (ver TableMat) — antes
            // eran vacío oscuro: 38% de la pantalla sin nada.
            cam.orthographicSize = world * 0.52f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = world * 2f;
            cam.clearFlags = CameraClearFlags.SolidColor; // Unity 6: SolidCamera no existe
            cam.backgroundColor = new Color(0.22f, 0.175f, 0.13f); // mesa (respaldo)

            // — Luz + ambiente —
            // El mundo se pinta con materiales UNLIT (ver NewMat): así el color que
            // se ve es EXACTAMENTE el que se elige y no depende del ambiente del
            // editor, y la sonda visual (que lee píxeles) puede medirlo. La luz se
            // deja puesta —una escena sin ella desconcierta— pero el look del
            // tablero no pende de ella. El ambiente cálido sigue definido por si
            // alguien añade objetos con material Standard.
            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.97f, 0.90f);
            light.intensity = 1.15f;
            light.shadows = LightShadows.None;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.45f, 0.43f, 0.38f);
            RenderSettings.ambientEquatorColor = new Color(0.36f, 0.34f, 0.29f);
            RenderSettings.ambientGroundColor = new Color(0.22f, 0.20f, 0.16f);

            // — Suelo: tierra con rejilla de escala y marco —
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.position = new Vector3(world * 0.5f, 0f, world * 0.5f);
            floor.transform.localScale = new Vector3(world / 10f, 1f, world / 10f); // plane = 10 u
            RemoveCollider(floor);
            var earth = new Color(0.42f, 0.33f, 0.23f);       // tierra clara, legible
            var gridLine = new Color(0.34f, 0.26f, 0.18f);    // surco de labor
            // Rejilla cada N unidades: referencia de ESCALA para el jugador (sin
            // ella un mundo de 768 u no tiene ninguna pista de tamaño).
            var floorMat = NewUnlitTextureMat("FloorMat",
                NewGridTexture(earth, gridLine), GridTiles);
            floor.GetComponent<Renderer>().sharedMaterial = floorMat;

            // — Mesa: el tablero no flota en el vacío —
            // Un plano mayor y más oscuro bajo el suelo. Rellena las franjas que la
            // cámara ve fuera del tablero (una vista ancha sobre un mundo cuadrado)
            // con algo intencionado, en vez de con el color de fondo a secas.
            var table = GameObject.CreatePrimitive(PrimitiveType.Plane);
            table.name = "Table";
            table.transform.position = new Vector3(world * 0.5f, -0.6f, world * 0.5f);
            table.transform.localScale = new Vector3(world * 2.6f / 10f, 1f, world * 2.6f / 10f);
            RemoveCollider(table);
            table.GetComponent<Renderer>().sharedMaterial =
                NewMat(new Color(0.22f, 0.175f, 0.13f), "TableMat");

            // — Marco del mundo: cuatro cajas que cierran el tablero —
            // Claras a propósito: sobre tierra, un borde oscuro se lee como sombra
            // y no como límite; el canto de madera clara sí delimita el tablero.
            float rim = Mathf.Max(1.5f, world * 0.012f);
            var rimMat = NewMat(new Color(0.56f, 0.45f, 0.31f), "RimMat");
            Frame(rimMat, "Frame_N", world * 0.5f, world - rim * 0.5f, world, rim, rim);
            Frame(rimMat, "Frame_S", world * 0.5f, rim * 0.5f, world, rim, rim);
            Frame(rimMat, "Frame_W", rim * 0.5f, world * 0.5f, rim, world, rim);
            Frame(rimMat, "Frame_E", world - rim * 0.5f, world * 0.5f, rim, world, rim);

            // — Presenter (dibuja hormigas/ítems con DrawMesh) —
            var presenterGo = new GameObject("SimPresenter");
            var presenter = presenterGo.AddComponent<Presenter.SimPresenterBehaviour>();
            presenter.CliPath = "build/antsim";
            // Defaults alineados con el checklist del Play pass (96², 4 min de
            // sim, canal A a 30 Hz): primera descarga ≈t3950 y el semáforo pasa
            // a ámbar/verde dentro de la sesión. Para el fixture grande (256²,
            // relevo real) ajustar Grid/Ticks a mano o usar ReplayFile.
            presenter.Grid = GridCells;
            presenter.Colonies = 2;
            presenter.Ticks = 7200;
            presenter.FrameEvery = 1;
            // Mallas a ESCALA NATURAL (cápsula 2 u × 1 u, esfera 1 u de diámetro):
            // el presenter escala en unidades de mundo, así que pre-escalar aquí
            // solo escondía el número real (una cápsula de 0.25 u que el presenter
            // multiplicaba por una «escala» sin relación con el tamaño en pantalla).
            presenter.AntMesh = PrimitiveMesh(PrimitiveType.Capsule, 1f, "AntMesh");
            presenter.ItemMesh = PrimitiveMesh(PrimitiveType.Sphere, 1f, "ItemMesh");
            // Hormiga: marrón rojizo oscuro. Ni tan oscura como la mesa (se
            // perdería) ni tan clara como el suelo o el surco de la rejilla (se
            // confundiría con el terreno): es el tono que hace que la colonia se
            // VEA moverse por el tablero.
            presenter.AntMaterial = NewMat(new Color(0.35f, 0.16f, 0.10f), "AntMat");
            presenter.CarrierMaterial = NewMat(new Color(0.98f, 0.72f, 0.16f), "CarrierMat");
            presenter.ItemMaterial = NewMat(new Color(0.36f, 0.78f, 0.36f), "ItemMat");
            // F5.2a: hojas con mordiscos — verde oscuro para la hoja, marrón
            // oscuro para las muescas de corte (CutsLeft/CutsInitial del canal A).
            presenter.LeafMaterial = NewMat(new Color(0.15f, 0.50f, 0.12f), "LeafMat");
            presenter.BiteMaterial = NewMat(new Color(0.30f, 0.15f, 0.05f), "BiteMat");
            // Canal E activo por defecto en la escena: el quad de feromonas ya
            // existe — sin este campo el canal quedaría apagado y el quad vacío.
            presenter.PheroEvery = 30;
            // Escala visual DERIVADA del mundo (AntScale 0 = automática): en un
            // mundo de 768 u el valor histórico (0.6) dejaba hormigas sub-píxel.
            presenter.AntScale = 0f;
            presenter.ActorLift = world * 0.0008f;
            // Canal F (F5.0) apagado por defecto: requiere elegir hormiga;
            // se activa desde el inspector (InspectId + ActivEvery) o por click.

            // — Marcadores de nido (posiciones del mundo; el HUD ancla a las tarjetas) —
            // Misma fórmula que WorldSim: NestX = world·(id+1)/(colonies+1),
            // NestY = world·0.5 — en UNIDADES de mundo, así que los marcadores
            // coinciden con los nidos reales del stream (256/384 y 512/384 en grid 96).
            // El marcador va en DOS piezas para leerse: montículo oscuro + disco
            // del color de la colonia (el mismo acento que usa la tarjeta del HUD).
            float nestR = world * 0.022f; // marcador proporcional al mundo
            var moundMat = NewMat(new Color(0.30f, 0.25f, 0.20f), "NestMoundMat");
            for (int c = 0; c < 2; c++)
            {
                float nx = world * (c + 1) / 3f;
                var mound = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                mound.name = $"NestMound_{c}";
                mound.transform.position = new Vector3(nx, 0.15f, world * 0.5f);
                mound.transform.localScale = new Vector3(nestR * 1.45f, 0.15f, nestR * 1.45f);
                RemoveCollider(mound);
                mound.GetComponent<Renderer>().sharedMaterial = moundMat;

                var nest = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                nest.name = $"Nest_{c}";
                nest.transform.position = new Vector3(nx, 0.45f, world * 0.5f);
                nest.transform.localScale = new Vector3(nestR, 0.3f, nestR);
                RemoveCollider(nest);
                nest.GetComponent<Renderer>().sharedMaterial = NewMat(
                    c == 0 ? AccentColony0 : AccentColony1, $"NestMat{c}");
            }

            // — Feromonas (F4.5): capa TRANSPARENTE sobre el suelo —
            // El defecto que destapó la sonda visual: el quad conservaba el
            // material por DEFECTO del primitivo (blanco y opaco), cubría el suelo
            // entero y tapaba las hormigas. Ahora tiene material transparente
            // propio —el canal E solo le cambia la RenderTexture— y va por debajo
            // de la altura a la que se dibujan hormigas e ítems.
            var pheroQuad = GameObject.CreatePrimitive(PrimitiveType.Plane);
            pheroQuad.name = "PheromoneTiles";
            pheroQuad.transform.position = new Vector3(world * 0.5f, PheroHeight, world * 0.5f);
            pheroQuad.transform.localScale = new Vector3(world / 10f, 1f, world / 10f); // plane = 10 u
            RemoveCollider(pheroQuad);
            var pheroMat = NewTransparentMat("PheromoneMat");
            EnsureMaterialFolder();
            AssetDatabase.CreateAsset(pheroMat, "Assets/Materials/PheromoneMat.mat");
            pheroQuad.GetComponent<Renderer>().sharedMaterial = pheroMat;
            var phero = pheroQuad.AddComponent<Presenter.PheromoneTileBehaviour>();
            phero.Presenter = presenter;
            phero.TargetMaterial = pheroMat;
            // F5.1: la capa por defecto es la que el canal E clásico ya emitía
            // (home de la colonia 0), así que el aspecto no cambia al abrir la
            // escena; F y G recorren las demás. Con el canal múltiple
            // (--phero-layers) el HUD puede enseñar cualquier colonia y tipo.

            // — Canvas del HUD —
            var canvasGo = new GameObject("HUD Canvas");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            // 1920×1080 de referencia y match 0.5. El default (800×600) dibujaba el
            // HUD a 2× en una pantalla normal: todo tosco y desproporcionado.
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            // Unity 6: FindFirstObjectByType depende del orden de instancias —
            // deprecado; FindAnyObjectByType es el sustituto canónico.
            if (Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<UnityEngine.EventSystems.EventSystem>();
                es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            }

            // ── F5.1: HUD en PANELES ────────────────────────────────────────
            // Antes era un muro de texto suelto: sin fondo, el mundo se comía el
            // texto; sin jerarquía, todo pesaba igual. Ahora cada bloque vive en
            // un panel translúcido con barra de acento y tipografía consistente.
            // Las tarjetas son el bloque MÁS ancho del HUD: llevan la ventana de
            // 1 s (≈55 caracteres) en una línea sin partir, así que su ancho sale
            // de esa línea (14 px × 0,5 em ≈ 7 px/carácter ⇒ ~390 px + márgenes),
            // no de un número a ojo. La reserva vive en la barra de la base, que
            // por eso reserva 34 px de la caja de texto.
            var cardTexts = new Text[2];
            var cardPanels = new RectTransform[2];
            for (int c = 0; c < 2; c++)
            {
                var accent = c == 0 ? AccentColony0 : AccentColony1;
                // 204 de alto (antes 176) y 216 de paso: los 28 px nuevos son la
                // banda de la GRÁFICA de reserva (F5.1), que no cabía al lado de
                // la barra. Sin crecer la tarjeta, el gráfico habría obligado a
                // encoger el texto y el pass de aspecto cuenta el desborde.
                var panel = Panel(canvasGo.transform, $"ColonyCard_{c}",
                    new Vector2(16, -72 - c * 216), new Vector2(440, 204), accent);
                cardPanels[c] = panel;
                cardTexts[c] = PanelText(panel, "Body", 14, Ink);
                var cardBodyRt = cardTexts[c].rectTransform;
                // 62 px de inset inferior: barra de reserva (14–26) y gráfica (32–58).
                cardBodyRt.offsetMin = new Vector2(cardBodyRt.offsetMin.x, 62f);
            }

            // — Barra de estado (arriba, ancho completo) —
            var statusPanel = Panel(canvasGo.transform, "StatusBar", new Vector2(16, -16),
                new Vector2(0, 40), AccentNeutral);
            statusPanel.anchorMin = new Vector2(0f, 1f);
            statusPanel.anchorMax = new Vector2(1f, 1f);
            statusPanel.pivot = new Vector2(0f, 1f);
            // Con anchorMin.y == anchorMax.y la ALTURA la manda el rect: el
            // `sizeDelta` de Panel() se pierde al fijar los offsets, así que hay
            // que dar las dos esquinas. Con (16,0)/(-16,0) la altura quedaba en 0
            // y la barra de estado era invisible (lo destapó la sonda: el rect del
            // texto salía con altura NEGATIVA).
            statusPanel.offsetMin = new Vector2(16f, -40f);
            statusPanel.offsetMax = new Vector2(-16f, 0f);
            var status = PanelText(statusPanel, "Status", 16, Ink, TextAnchor.MiddleLeft, 10f);

            // — Toasts (arriba a la derecha; el contenedor per-elemento los pinta) —
            var toasts = NewText(canvasGo.transform, "Toasts",
                new Vector2(-16, -72), new Vector2(620, 200),
                TextAnchor.UpperRight, 15);
            toasts.color = Ink;

            // — Historial de comandos (abajo a la izquierda) —
            var dropPanel = Panel(canvasGo.transform, "DropPlanPanel",
                new Vector2(16, 16), new Vector2(600, 70), AccentNeutral);
            var dropPlanText = PanelText(dropPanel, "DropPlan", 14, InkDim,
                TextAnchor.UpperLeft, 12f);

            var historyPanel = Panel(canvasGo.transform, "CommandHistoryPanel",
                new Vector2(16, 94), new Vector2(600, 92), AccentNeutral);
            var history = PanelText(historyPanel, "CommandHistory", 14, InkDim);

            // — Tarjeta de inspección (abajo a la derecha) —
            var inspectPanel = Panel(canvasGo.transform, "AntInspectorPanel",
                new Vector2(-16, 16), new Vector2(460, 250), AccentNeutral);
            var inspect = PanelText(inspectPanel, "AntInspector", 14, Ink);
            var inspector = inspect.gameObject.AddComponent<Presenter.AntInspectorBehaviour>();
            inspector.Presenter = presenter;
            inspector.CardText = inspect;

            // — Pistas de teclado (abajo al centro): el juego no se explica solo —
            var hints = NewText(canvasGo.transform, "Hints",
                new Vector2(0, 18), new Vector2(1100, 26),
                TextAnchor.LowerCenter, 14);
            hints.color = InkDim;
            hints.rectTransform.anchorMin = new Vector2(0.5f, 0f);
            hints.rectTransform.anchorMax = new Vector2(0.5f, 0f);
            hints.rectTransform.pivot = new Vector2(0.5f, 0f);
            hints.rectTransform.anchoredPosition = new Vector2(0f, 18f);
            // Sin emoji (la fuente LegacyRuntime de uGUI no los tiene: salían
            // cajas vacías, uno de los motivos del aspecto pobre del HUD).
            hints.text = "click: inspeccionar hormiga   ·   D: marcar drops (Z deshace)   ·   " +
                         "F: capa de feromonas (home/food/alarm)   ·   G: colonia   ·   " +
                         "I: importar pool   ·   J: ir a la alerta   ·   Espacio: pausa   ·   " +
                         "\u2b07: soltar .antgenome";

            // — HUD layout: reparte el stream a todo —
            var hudGo = new GameObject("HudLayout");
            var hud = hudGo.AddComponent<Presenter.HudLayoutBehaviour>();
            hud.Presenter = presenter;
            hud.ColonyCardTexts = cardTexts;
            hud.ToastsText = toasts;
            hud.Inspector = inspector;
            hud.HistoryText = history;
            hud.StatusText = status;
            hud.HintsText = hints;

            // — Click de selección (§5): raycast pantalla→mundo → PickNearest —
            var pickGo = new GameObject("AntPickClickHandler");
            var pick = pickGo.AddComponent<EditorTools.AntPickClickHandler>();
            pick.Presenter = presenter;
            pick.Inspector = inspector;

            // — Plan de intervención (F4.4): click-to-place de DropFood + resumen —
            // (el panel se crea arriba, junto al historial, para que la columna
            //  izquierda quede apilada sin solapes)
            var dropGo = new GameObject("DropFoodClickHandler");
            var drop = dropGo.AddComponent<Presenter.DropFoodClickHandler>();
            drop.Presenter = presenter;
            hud.DropPlan = drop;
            hud.DropPlanText = dropPlanText;

            // — Salto de cámara por toast (contrato §1): ancla (x,y) del canal D —
            var jumpGo = new GameObject("ToastClickCameraJump");
            var jump = jumpGo.AddComponent<Presenter.ToastClickCameraJump>();
            jump.Hud = hud;

            // — Diálogo de importación (F4.3): modal CENTRADO con fondo atenuado —
            // Antes era otro texto suelto más: ahora es un modal de verdad (panel
            // centrado + velo oscuro detrás + botones DENTRO del panel).
            var backdropGo = new GameObject("DialogBackdrop", typeof(RectTransform));
            backdropGo.transform.SetParent(canvasGo.transform, false);
            var backdropRt = (RectTransform)backdropGo.transform;
            backdropRt.anchorMin = Vector2.zero; backdropRt.anchorMax = Vector2.one;
            backdropRt.offsetMin = Vector2.zero; backdropRt.offsetMax = Vector2.zero;
            var backdropImg = backdropGo.AddComponent<UnityEngine.UI.Image>();
            backdropImg.color = new Color(0f, 0f, 0f, 0.45f);
            backdropImg.raycastTarget = false;
            backdropGo.SetActive(false);

            var modalGo = new GameObject("ImportModal", typeof(RectTransform));
            modalGo.transform.SetParent(canvasGo.transform, false);
            var modalRt = (RectTransform)modalGo.transform;
            modalRt.anchorMin = modalRt.anchorMax = new Vector2(0.5f, 0.5f);
            modalRt.pivot = new Vector2(0.5f, 0.5f);
            modalRt.anchoredPosition = Vector2.zero;
            modalRt.sizeDelta = new Vector2(860f, 360f);
            var modalImg = modalGo.AddComponent<UnityEngine.UI.Image>();
            modalImg.color = PanelFill;
            var modalEdge = new GameObject("Edge", typeof(RectTransform));
            modalEdge.transform.SetParent(modalRt, false);
            var edgeRt = (RectTransform)modalEdge.transform;
            edgeRt.anchorMin = new Vector2(0f, 1f); edgeRt.anchorMax = new Vector2(1f, 1f);
            edgeRt.pivot = new Vector2(0.5f, 1f);
            edgeRt.anchoredPosition = Vector2.zero;
            edgeRt.sizeDelta = new Vector2(0f, 3f);
            modalEdge.AddComponent<UnityEngine.UI.Image>().color = AccentNeutral;
            var modalTitle = PanelText(modalRt, "Title", 19, AccentNeutral, TextAnchor.UpperLeft);
            var titleRt = modalTitle.rectTransform;
            titleRt.anchorMin = new Vector2(0f, 1f);
            titleRt.anchorMax = new Vector2(1f, 1f);
            titleRt.pivot = new Vector2(0f, 1f);
            titleRt.offsetMin = new Vector2(26f, -54f);   // borde inferior del título
            titleRt.offsetMax = new Vector2(-20f, -18f);  // borde superior
            modalTitle.text = "IMPORTAR POOL PRE-ENTRENADO";

            // — Campo de ruta: sin él el diálogo era inalcanzable (nadie podía dar
            //   la ruta del .antgenome) y los botones del modal no servían de nada —
            var pathCaption = PanelText(modalRt, "PathCaption", 13, InkDim, TextAnchor.UpperLeft);
            var capRt = pathCaption.rectTransform;
            capRt.anchorMin = new Vector2(0f, 1f); capRt.anchorMax = new Vector2(1f, 1f);
            capRt.pivot = new Vector2(0f, 1f);
            capRt.offsetMin = new Vector2(26f, -86f);
            capRt.offsetMax = new Vector2(-20f, -62f);
            pathCaption.text = "ruta del archivo .antgenome (relativa al repo o absoluta)";

            var pathField = CreateInputField(modalRt, "PathField",
                new Vector2(26f, -128f), new Vector2(-20f, -90f),
                "artifacts/pretrain-warm-v2.antgenome");

            var importText = PanelText(modalRt, "ImportDialog", 14, Ink, TextAnchor.UpperLeft);
            var bodyRt = importText.rectTransform;
            bodyRt.offsetMin = new Vector2(26f, 82f);     // deja sitio a los botones
            bodyRt.offsetMax = new Vector2(-20f, -140f);
            modalGo.SetActive(false);

            var importGo = new GameObject("ImportDialog");
            var import = importGo.AddComponent<Presenter.ImportDialogBehaviour>();
            import.Presenter = presenter;
            import.CliPath = presenter.CliPath;
            hud.ImportDialog = import;
            hud.ImportDialogText = importText;
            hud.DialogBackdrop = backdropGo;
            hud.ImportModal = modalGo;

            // — F5.1 deuda: overlay de drag & drop —— texto centrado que
            //   se enciende cuando el jugador arrastra un .antgenome ——
            var dragOverlayGo = new GameObject("DragOverlay", typeof(RectTransform));
            dragOverlayGo.transform.SetParent(canvasGo.transform, false);
            var dragRt = (RectTransform)dragOverlayGo.transform;
            dragRt.anchorMin = Vector2.zero; dragRt.anchorMax = Vector2.one;
            dragRt.offsetMin = Vector2.zero; dragRt.offsetMax = Vector2.zero;
            var dragImg = dragOverlayGo.AddComponent<UnityEngine.UI.Image>();
            dragImg.color = new Color(0.1f, 0.4f, 0.2f, 0.6f); // verde oscuro semitransparente
            dragImg.raycastTarget = false;
            var dragTextGo = new GameObject("DragText", typeof(RectTransform));
            dragTextGo.transform.SetParent(dragRt, false);
            var dragTextRt = (RectTransform)dragTextGo.transform;
            dragTextRt.anchorMin = dragTextRt.anchorMax = new Vector2(0.5f, 0.5f);
            dragTextRt.sizeDelta = new Vector2(600f, 80f);
            var dragText = dragTextGo.AddComponent<UnityEngine.UI.Text>();
            dragText.text = "\u2b07  Soltar para importar pool .antgenome";
            dragText.fontSize = 24;
            dragText.alignment = TextAnchor.MiddleCenter;
            dragText.color = Color.white;
            dragText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            hud.DragOverlayText = dragText;
            dragOverlayGo.SetActive(false);

            // — F5.1: per-elemento — contenedor de toasts clicables + barras + botones —
            RectTransform toastContainer = CreateToastContainer(canvasGo.transform, hud);
            var stockImages = CreateStockBars(cardPanels, hud, cardTexts.Length);
            CreateSparklines(cardPanels, presenter, cardTexts.Length);
            var buttons = CreateNativeButtons(modalRt, dropPanel, hud);
            hud.BindNativeButtons(buttons[0], buttons[1], buttons[2], buttons[3]);
            hud.BindImportPathField(pathField);

            // — Guardar + registrar en build settings —
            // No basta con crear la escena: el «reiniciar con plan» (F4.4) y el
            // diálogo de importación (F4.3) relanzan la partida con
            // `SceneManager.LoadScene`, que exige que la escena esté en build
            // settings. El Play pass ejecutado por el agente detectó que con
            // `m_Scenes: []` —el estado de un checkout limpio— el botón de
            // reinicio no podía funcionar, y que un player sin escenas arranca
            // vacío. Se guarda y se registra aquí, donde la escena se crea.
            const string ScenePath = "Assets/Scenes/Game.unity";
            System.IO.Directory.CreateDirectory(
                System.IO.Path.Combine(Application.dataPath, "Scenes"));
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            RegisterInBuildSettings(ScenePath);
            Debug.Log($"[SceneBootstrapper] Escena creada y guardada en {ScenePath}, registrada en " +
                      "build settings. Pulsa Play. Publica antes el CLI: " +
                      "dotnet publish src/Tools/AntSim.Cli -c Release -o build/antsim");
        }

        /// <summary>
        /// Registra la escena de juego en build settings sin pisar lo que ya
        /// hubiera: es lo que hace posible recargarla en Play («reiniciar con
        /// plan», importar un pool) y lo que hace que un player tenga algo que
        /// arrancar.
        /// </summary>
        private static void RegisterInBuildSettings(string scenePath)
        {
            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(
                EditorBuildSettings.scenes);
            if (scenes.Exists(s => s.path == scenePath)) return;
            scenes.Insert(0, new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
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
            bg.color = new Color(0.05f, 0.06f, 0.08f, 0.78f);
            // Barra de acento a la izquierda del toast: da jerarquía sin cambiar
            // el modelo (el color del texto ya lleva el NIVEL).
            var toastBarGo = new GameObject("Accent", typeof(RectTransform));
            toastBarGo.transform.SetParent(t, false);
            var toastBarRt = (RectTransform)toastBarGo.transform;
            toastBarRt.anchorMin = new Vector2(0f, 0f);
            toastBarRt.anchorMax = new Vector2(0f, 1f);
            toastBarRt.pivot = new Vector2(0f, 0.5f);
            toastBarRt.anchoredPosition = new Vector2(1f, 0f);
            toastBarRt.sizeDelta = new Vector2(4f, -6f);
            toastBarGo.AddComponent<UnityEngine.UI.Image>().color = AccentNeutral;
            var txtGo = new GameObject("Text", typeof(RectTransform));
            txtGo.transform.SetParent(t, false);
            var trt = (RectTransform)txtGo.transform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(12f, 2f); trt.offsetMax = new Vector2(-10f, -2f);
            var text = txtGo.AddComponent<UnityEngine.UI.Text>();
            text.alignment = TextAnchor.MiddleLeft;
            text.fontSize = 15;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.font = DefaultFont();
            tmplGo.SetActive(false);

            hud.ToastContainer = c;
            hud.ToastTemplate = t;
            return c;
        }

        // — F5.1: barras de stock — un fondo + una Image con fillAmount por colonia.
        //   F5.1: van DENTRO de la tarjeta de su colonia, como calibre en la base
        //   del panel (antes flotaban sueltas sobre el mundo, alineadas «a ojo»).
        /// <summary>
        /// Gráfica de reserva por tarjeta (F5.1). Es un RawImage con una textura
        /// GENERADA por el modelo puro (`ColonySparklineModel`), no un Sprite: la
        /// serie se reescribe a 1 Hz y generar el sprite cada segundo sería tirar
        /// memoria. El componente se refresca solo desde el presenter, así que el
        /// HUD no tiene que conocer la gráfica.
        /// </summary>
        private static void CreateSparklines(RectTransform[] cardPanels,
            Presenter.SimPresenterBehaviour presenter, int colonies)
        {
            for (int i = 0; i < colonies; i++)
            {
                var go = new GameObject($"Sparkline_{i}", typeof(RectTransform));
                go.transform.SetParent(cardPanels[i], false);
                var rt = (RectTransform)go.transform;
                rt.anchorMin = new Vector2(0f, 0f);
                rt.anchorMax = new Vector2(1f, 0f);
                rt.pivot = new Vector2(0f, 0f);
                rt.offsetMin = new Vector2(20f, 32f);
                rt.offsetMax = new Vector2(-14f, 58f);

                var img = go.AddComponent<UnityEngine.UI.RawImage>();
                img.raycastTarget = false;
                img.color = Color.white;

                var spark = go.AddComponent<Presenter.ColonySparklineBehaviour>();
                spark.Presenter = presenter;
                spark.ColonyId = i;
                spark.Target = img;
                // 1 px por unidad de UI (406 de ancho en la tarjeta) y 90 muestras
                // ⇒ el modelo reparte la ventana por todo el ancho.
                spark.Width = 406;
                spark.Height = 26;
            }
        }

        private static UnityEngine.UI.Image?[] CreateStockBars(RectTransform[] cardPanels,
            Presenter.HudLayoutBehaviour hud, int colonies)
        {
            var imgs = new UnityEngine.UI.Image?[colonies];
            for (int i = 0; i < colonies; i++)
            {
                var barGo = new GameObject($"StockBar_{i}", typeof(RectTransform));
                barGo.transform.SetParent(cardPanels[i], false);
                var rt = (RectTransform)barGo.transform;
                rt.anchorMin = new Vector2(0f, 0f);
                rt.anchorMax = new Vector2(1f, 0f);
                rt.pivot = new Vector2(0f, 0f);
                rt.offsetMin = new Vector2(20f, 14f);
                rt.offsetMax = new Vector2(-14f, 26f);

                var back = barGo.AddComponent<UnityEngine.UI.Image>();
                back.color = new Color(0f, 0f, 0f, 0.45f);
                back.raycastTarget = false;

                var fillGo = new GameObject("Fill", typeof(RectTransform));
                fillGo.transform.SetParent(barGo.transform, false);
                var frt = (RectTransform)fillGo.transform;
                frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one;
                frt.offsetMin = new Vector2(1f, 1f); frt.offsetMax = new Vector2(-1f, -1f);
                var fill = fillGo.AddComponent<UnityEngine.UI.Image>();
                fill.fillAmount = 0f;
                fill.raycastTarget = false;
                // El color lo pinta RenderStockBars según el modelo puro.
                imgs[i] = fill;
            }
            hud.StockBars = imgs;
            return imgs;
        }

        // — F5.1: botones nativos (Confirmar/Cancelar del import + Reiniciar con plan) —
        //   F5.1: cuelgan de SU panel (el modal y el panel del plan), con estados
        //   de color legibles en vez de un gris plano igual para todo.
        private static UnityEngine.UI.Button?[] CreateNativeButtons(Transform dialogParent,
            RectTransform dropPanel, Presenter.HudLayoutBehaviour hud)
        {
            UnityEngine.UI.Button Make(Transform canvas, string name, string label,
                Vector2 anchor, Vector2 pos, Vector2 size, Color? tint = null)
            {
                var go = new GameObject(name, typeof(RectTransform));
                go.transform.SetParent(canvas, false);
                var rt = (RectTransform)go.transform;
                rt.anchorMin = rt.anchorMax = anchor;
                rt.pivot = anchor;
                rt.anchoredPosition = pos;
                rt.sizeDelta = size;
                var img = go.AddComponent<UnityEngine.UI.Image>();
                img.color = tint ?? new Color(0.16f, 0.17f, 0.20f, 0.95f);
                var btn = go.AddComponent<UnityEngine.UI.Button>();
                // Feedback de estado: el color de fondo comunica si el botón se
                // puede pulsar (el gris plano anterior no distinguía nada).
                var colors = btn.colors;
                colors.normalColor = Color.white;
                colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
                colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
                colors.disabledColor = new Color(0.55f, 0.55f, 0.55f, 0.6f);
                colors.fadeDuration = 0.08f;
                btn.colors = colors;
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

            // Inspeccionar (acento, el primer paso) + Confirmar + Cancelar en la
            // base del modal; Reiniciar dentro del panel del plan. Las posiciones
            // son un CONJUNTO: cada botón 20 px a la izquierda del siguiente, todos
            // dentro del panel y con 22 px de margen inferior.
            var inspect = Make(dialogParent, "ImportInspectBtn", "Inspeccionar",
                new Vector2(1f, 0f), new Vector2(-20f, 20f), new Vector2(170f, 40f),
                new Color(0.16f, 0.42f, 0.34f, 0.95f));
            var confirm = Make(dialogParent, "ImportConfirmBtn", "Confirmar",
                new Vector2(1f, 0f), new Vector2(-202f, 20f), new Vector2(170f, 40f),
                new Color(0.20f, 0.34f, 0.52f, 0.95f));
            var cancel = Make(dialogParent, "ImportCancelBtn", "Cancelar",
                new Vector2(1f, 0f), new Vector2(-384f, 20f), new Vector2(170f, 40f));
            var restart = Make(dropPanel, "RestartWithPlanBtn", "Reiniciar con plan",
                new Vector2(1f, 0f), new Vector2(-12f, 14f), new Vector2(200f, 40f),
                new Color(0.16f, 0.42f, 0.34f, 0.95f));
            return new UnityEngine.UI.Button?[] { inspect, confirm, cancel, restart };
        }

        /// <summary>
        /// Campo de texto del modal (F5.1). uGUI no trae un «InputField» de una
        /// línea por sí solo: hay que montar la Image de fondo, el Text de edición
        /// y el placeholder, y asignarlos. Sin esto no había forma de dar la ruta
        /// del .antgenome y el pilar de importación era inalcanzable.
        /// `offsetMin`/`offsetMax` se interpretan como anclas dentro del panel que
        /// lo contiene (márgenes izquierdo/inferior y derecho/superior).
        /// </summary>
        private static UnityEngine.UI.InputField CreateInputField(RectTransform parent,
            string name, Vector2 offsetMin, Vector2 offsetMax, string placeholder)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;

            var bg = go.AddComponent<UnityEngine.UI.Image>();
            bg.color = new Color(0.03f, 0.035f, 0.05f, 0.9f);

            var text = MakeFieldText(rt, "Text", Ink);
            var ph = MakeFieldText(rt, "Placeholder", InkDim);
            ph.text = placeholder;

            var field = go.AddComponent<UnityEngine.UI.InputField>();
            field.targetGraphic = bg;
            field.textComponent = text;
            field.placeholder = ph;
            field.lineType = UnityEngine.UI.InputField.LineType.SingleLine;
            field.characterLimit = 260;
            field.caretColor = Ink;
            field.customCaretColor = true;
            field.selectionColor = new Color(AccentNeutral.r, AccentNeutral.g, AccentNeutral.b, 0.35f);
            return field;
        }

        /// <summary>Text interno de un InputField (edición o placeholder).</summary>
        private static Text MakeFieldText(RectTransform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(10f, 2f);
            rt.offsetMax = new Vector2(-10f, -2f);
            var t = go.AddComponent<Text>();
            t.font = DefaultFont();
            t.fontSize = 15;
            t.color = color;
            t.alignment = TextAnchor.MiddleLeft;
            t.supportRichText = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            return t;
        }

        private static class HudLayoutModel
        {
            public const float ToastHeight = Streaming.HudElementLayoutModel.ToastHeight;
        }

        // ── F5.1: paleta y medidas (una sola fuente de verdad) ───────────────
        private const int GridTiles = 24;        // rejilla del suelo cada world/24 u
        private const float PheroHeight = 0.25f; // altura de la capa de feromonas
        private static readonly Color PanelFill = new Color(0.07f, 0.075f, 0.09f, 0.82f);
        private static readonly Color Ink = new Color(0.93f, 0.91f, 0.86f);
        private static readonly Color InkDim = new Color(0.72f, 0.71f, 0.66f);
        private static readonly Color AccentColony0 = new Color(0.93f, 0.45f, 0.35f);
        private static readonly Color AccentColony1 = new Color(0.42f, 0.63f, 0.95f);
        private static readonly Color AccentNeutral = new Color(0.52f, 0.84f, 0.78f);

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
            t.color = Ink;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.supportRichText = false;
            t.font = DefaultFont(); // Liberation Sans: sin dependencias
            return t;
        }

        /// <summary>Fuente por defecto del HUD. LegacyRuntime es la de Unity 6;
        /// Arial queda como respaldo para proyectos antiguos.</summary>
        private static Font DefaultFont()
        {
            var f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (f == null) f = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return f;
        }

        /// <summary>
        /// Panel del HUD (F5.1): fondo translúcido + barra de acento a la
        /// izquierda. Todo con Image PLANO, sin sprites: un sprite creado en
        /// runtime no sobrevive a guardar/recargar la escena y el panel se
        /// quedaría sin fondo — el mismo tipo de fallo que dejó el mundo blanco.
        /// La convención de `anchor` es la de NewText: el signo elige la esquina.
        /// </summary>
        private static RectTransform Panel(Transform parent, string name, Vector2 anchor,
            Vector2 size, Color? accent = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(anchor.x < 0 ? 1f : 0f, anchor.y < 0 ? 1f : 0f);
            rt.pivot = rt.anchorMin;
            rt.anchoredPosition = new Vector2(Mathf.Abs(anchor.x), Mathf.Abs(anchor.y));
            rt.sizeDelta = size;
            var bg = go.AddComponent<UnityEngine.UI.Image>();
            bg.color = PanelFill;
            bg.raycastTarget = false;
            if (accent.HasValue)
            {
                var barGo = new GameObject("Accent", typeof(RectTransform));
                barGo.transform.SetParent(rt, false);
                var brt = (RectTransform)barGo.transform;
                brt.anchorMin = new Vector2(0f, 0f);
                brt.anchorMax = new Vector2(0f, 1f);
                brt.pivot = new Vector2(0f, 0.5f);
                brt.anchoredPosition = new Vector2(1f, 0f);
                brt.sizeDelta = new Vector2(4f, -12f);
                var bar = barGo.AddComponent<UnityEngine.UI.Image>();
                bar.color = accent.Value;
                bar.raycastTarget = false;
            }
            return rt;
        }

        /// <summary>Texto hijo de un panel, con margen interior y rich text activo.</summary>
        private static Text PanelText(RectTransform panel, string name, int fontSize,
            Color color, TextAnchor align = TextAnchor.UpperLeft, float pad = 14f)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(panel, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(pad + 6f, pad * 0.5f);
            rt.offsetMax = new Vector2(-pad, -pad * 0.5f);
            var t = go.AddComponent<Text>();
            t.font = DefaultFont();
            t.fontSize = fontSize;
            t.color = color;
            t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.supportRichText = true;
            t.raycastTarget = false;
            return t;
        }

        /// <summary>Los primitivos decorativos no necesitan collider: nada raycastea
        /// contra el suelo (el click resuelve el plano con matemática).</summary>
        private static void RemoveCollider(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
        }

        /// <summary>Una caja del marco del mundo.</summary>
        private static void Frame(Material mat, string name, float cx, float cz,
            float sx, float sz, float rim)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = new Vector3(cx, rim * 0.5f, cz);
            go.transform.localScale = new Vector3(sx, rim, sz);
            RemoveCollider(go);
            go.GetComponent<Renderer>().sharedMaterial = mat;
        }

        /// <summary>Textura de rejilla (fondo + línea) GUARDADA como asset: sirve de
        /// referencia de escala sobre el suelo y sobrevive a recargar la escena.</summary>
        private static Texture2D NewGridTexture(Color fill, Color line)
        {
            const int N = 64;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { name = "GridTex" };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                    px[y * N + x] = (x == 0 || y == 0) ? (Color32)line : (Color32)fill;
            tex.SetPixels32(px);
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Bilinear;
            tex.Apply();
            EnsureMaterialFolder();
            AssetDatabase.CreateAsset(tex, "Assets/Materials/GridTex.asset");
            return tex;
        }

        /// <summary>
        /// Material TRANSPARENTE para capas superpuestas (feromonas). Busca un
        /// shader que ya mezcle alpha y, si solo hay Standard, lo configura a mano
        /// (solo poner `_Mode` no basta: hacen falta blends, ZWrite y keyword).
        /// </summary>
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
            // Arranca SIN pintar (textura transparente) y con el tinte en blanco: el
            // canal E solo cambia la textura, así que el quad nace invisible.
            m.mainTexture = NewEmptyTexture();
            if (m.HasProperty("_Color")) m.color = Color.white;
            return m;
        }

        private static void EnsureMaterialFolder()
        {
            // La carpeta debe existir: en un proyecto recién abierto
            // Assets/Materials no existe y CreateAsset falla (visto en el Play pass).
            if (!AssetDatabase.IsValidFolder("Assets/Materials"))
                AssetDatabase.CreateFolder("Assets", "Materials");
        }

        /// <summary>
        /// Material de color PLANO del mundo (suelo, marco, nidos, hormigas,
        /// ítems). Unlit a propósito:
        ///
        ///   · El color que se ve es el que se elige — sin depender del ambiente
        ///     del editor ni de la intensidad de una luz. El defecto histórico
        ///     (Standard con ambiente por defecto) pintaba el tablero apagado y
        ///     hacía que cualquier ajuste de look fuese una lotería.
        ///   · La sonda visual MIDE píxeles: con iluminación, el tono renderizado
        ///     no es el del material y el invariante «hay hormigas visibles» no se
        ///     puede comprobar con tolerancias razonables.
        ///
        /// Unlit/Color es del pipeline integrado; si algún día el proyecto pasa a
        /// URP, el respaldo deja el color en emisión (también independiente de la luz).
        /// </summary>
        private static Material NewMat(Color c, string name)
        {
            var sh = Shader.Find("Unlit/Color");
            if (sh == null) sh = Shader.Find("Unlit/Texture");
            if (sh == null) sh = Shader.Find("Standard");
            var m = new Material(sh!) { name = name };
            m.color = c;
            if (sh != null && sh.name == "Unlit/Color") m.SetColor("_Color", c);
            if (sh != null && sh.name == "Standard")
            {
                // Respaldo: albedo negro + emisión pura ⇒ color independiente de la luz.
                m.SetColor("_Color", Color.black);
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", c);
            }
            EnsureMaterialFolder();
            AssetDatabase.CreateAsset(m, $"Assets/Materials/{name}.mat");
            return m;
        }

        /// <summary>Material unlit TEXTURIZADO (el suelo con su rejilla). La textura
        /// es un asset guardado: una textura creada en runtime no sobrevive a
        /// guardar/recargar la escena y el suelo se quedaría liso (o blanco).</summary>
        private static Material NewUnlitTextureMat(string name, Texture2D tex, int tiles)
        {
            var sh = Shader.Find("Unlit/Texture");
            if (sh == null) sh = Shader.Find("Standard");
            var m = new Material(sh!) { name = name, mainTexture = tex };
            m.mainTextureScale = new Vector2(tiles, tiles);
            // Unlit/Texture NO tiene `_Color` en Unity 6: asignarlo escupe
            // «doesn't have a color property '_Color'» en la consola. Se comprueba.
            if (m.HasProperty("_Color")) m.color = Color.white;
            EnsureMaterialFolder();
            AssetDatabase.CreateAsset(m, $"Assets/Materials/{name}.mat");
            return m;
        }

        /// <summary>
        /// Textura 1×1 TRANSPARENTE: el estado de reposo de la capa de feromonas.
        ///
        /// Unlit/Transparent SIN textura pinta BLANCO Y OPACO, así que el quad de
        /// feromonas tapaba el tablero entero hasta que llegaba el primer paquete
        /// del canal E — y en modo edición no llega nunca: al abrir la escena se
        /// veía un mundo blanco con dos puntos de color (los nidos). Es la causa
        /// del «el terreno es blanco» del Play pass, y por eso la capa arranca
        /// con una textura que no pinta nada en vez de sin textura.
        /// </summary>
        private static Texture2D NewEmptyTexture()
        {
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false) { name = "PheromoneEmpty" };
            tex.SetPixels32(new[] { new Color32(255, 255, 255, 0) });
            tex.Apply();
            EnsureMaterialFolder();
            AssetDatabase.CreateAsset(tex, "Assets/Materials/PheromoneEmpty.asset");
            return tex;
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

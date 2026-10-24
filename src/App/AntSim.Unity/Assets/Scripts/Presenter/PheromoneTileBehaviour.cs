using UnityEngine;

namespace AntSim.Unity.Scripts.Presenter
{
    /// <summary>
    /// Render de feromonas (F4.5) + selector de capa (F5.1) — wire-up FINO sobre
    /// <see cref="Streaming.PheromoneSelectorModel"/> (decide QUÉ capa y de qué
    /// color) y <see cref="Streaming.PheromoneTileModel"/> (decodifica el RLE).
    ///
    /// POR QUÉ HAY SELECTOR: las capas de feromona son POR COLONIA y hay tres
    /// tipos activos. El canal E clásico enseñaba una sola (home de la colonia 0),
    /// así que con dos colonias compitiendo no se podía ver el rastro de la otra
    /// ni distinguir «casa» de «comida» o «peligro». Ahora se elige con F (capa) y
    /// G (colonia), y cada capa tiene su color.
    ///
    /// Las acciones tienen punto de entrada SIN DISPOSITIVO (F5.2): el Play pass
    /// cicla capas sin teclado, igual que selecciona hormigas o marca drops.
    ///
    /// Si la capa pedida no viaja en el stream se pinta VACÍO — nunca se deja el
    /// frame anterior: un rastro viejo con la etiqueta equivocada es peor que no
    /// pintar nada (se leería como «esta colonia no tiene rastro»). Pero eso se
    /// decide al CAMBIAR de capa o colonia; un tick que aún no trae paquete (el
    /// canal E llega cada <c>PheroEvery</c> ticks) conserva el último mapa: si no,
    /// el tablero parpadeaba vacío entre paquete y paquete.
    /// </summary>
    public sealed class PheromoneTileBehaviour : MonoBehaviour
    {
        [Tooltip("Presenter cuyo stream lleva el canal E (--phero-every > 0).")]
        public SimPresenterBehaviour? Presenter;

        [Tooltip("Material con la RenderTexture destino (shader Unlit/Texture).")]
        public Material? TargetMaterial;

        [Tooltip("Pinta TODAS las capas de la colonia elegida a la vez, cada tipo con su color (food ámbar, home verde, alarm rojo, huella violeta). Con F se pasa a una sola capa y se cicla.")]
        public bool ShowAllLayers = true;

        [Tooltip("Color por capa (paleta del selector). Apágalo para pintar todo con TrailColor.")]
        public bool PaletteByLayer = true;

        [ColorUsage(false)]
        [Tooltip("Color fijo cuando PaletteByLayer está apagado.")]
        public Color TrailColor = new Color(0.35f, 0.85f, 0.45f);

        [Tooltip("Atajo para ciclar la capa (home → food → alarm).")]
        public KeyCode CycleLayerKey = KeyCode.F;

        [Tooltip("Atajo para ciclar la colonia.")]
        public KeyCode CycleColonyKey = KeyCode.G;

        /// <summary>Selector puro (tests e inspección): decide capa, colonia y color.</summary>
        public readonly Streaming.PheromoneSelectorModel Selector = new();

        /// <summary>Modelo puro de la capa PRINCIPAL (la del selector) — tests e
        /// inspección, y la que mide el HUD para la línea del rastro.</summary>
        public readonly Streaming.PheromoneTileModel Model = new();

        private readonly System.Collections.Generic.Dictionary<
            Streaming.PheromoneSelectorModel.Layer, Streaming.PheromoneTileModel> _layerModels = new();
        private readonly System.Collections.Generic.List<
            Streaming.PheromoneCompositeModel.Layer> _compose = new();
        private byte[] _rgb = System.Array.Empty<byte>();
        private byte[] _alpha = System.Array.Empty<byte>();

        private Texture2D? _cpu;
        private RenderTexture? _rt;
        private int _paintedTick = -1;
        private int _paintedColony = int.MinValue;
        private byte _paintedKind = 255;
        private bool _layerChosen;

        /// <summary>Etiqueta de lo pintado: todas las capas a la vez o la capa elegida.</summary>
        public string LayerLabel => ShowAllLayers
            ? $"feromonas · colonia {Selector.Colony} · todas (food+home+alarm+huella)"
            : Selector.Label;

        // ————— acciones sin dispositivo (F5.2) —————

        public void SelectLayer(Streaming.PheromoneSelectorModel.Layer kind)
        {
            Selector.Select(kind);
            _paintedTick = -1; // fuerza repintado aunque el tick no cambie
        }

        public void SelectColony(int colony)
        {
            Selector.SelectColony(colony);
            _paintedTick = -1;
        }

        public void CycleLayer()
        {
            // Con TODAS las capas a la vez (lo normal), F baja a la capa
            // principal y la cicla; al cerrar el ciclo (vuelve a home) sube otra
            // vez a «todas». El mismo atajo sirve para inspeccionar un tipo suelto
            // sin perder la vista completa.
            if (ShowAllLayers)
            {
                ShowAllLayers = false;
                Selector.Select(Streaming.PheromoneSelectorModel.Layer.Food);
            }
            else if (Selector.CycleLayer() == Streaming.PheromoneSelectorModel.Layer.Home)
            {
                ShowAllLayers = true;
            }
            _paintedTick = -1;
            ClearTiles();
        }

        public void CycleColony()
        {
            Selector.CycleColony();
            _paintedTick = -1;
        }

        private void Update()
        {
            var presenter = Presenter;
            if (presenter == null || TargetMaterial == null) return;

            var header = presenter.Presenter.Header;
            if (header != null) Selector.SetColonyCount(header.Colonies);

            // F8: con reclutamiento la capa que INTERESA es la de COMIDA —el camino
            // que deja la hormiga cargada hacia el montón—, así que el render
            // arranca en ella (F sigue ciclando). Sin reclutamiento se conserva el
            // default histórico (home, la que emitía el canal E clásico).
            if (!_layerChosen)
            {
                _layerChosen = true;
                if (presenter.Recruitment) SelectLayer(Streaming.PheromoneSelectorModel.Layer.Food);
            }

            if (Input.GetKeyDown(CycleLayerKey)) CycleLayer();
            if (Input.GetKeyDown(CycleColonyKey)) CycleColony();

            var view = presenter.Presenter.CurrentTick;
            if (view == null) return;

            // Cambiar de capa o de colonia sí debe limpiar: si esa combinación no
            // viaja en el stream, pintar la anterior sería mentir con la etiqueta
            // del HUD. Un tick SIN paquete, en cambio, no: el canal E llega cada
            // PheroEvery ticks y los demás frames tienen que conservar el último
            // mapa (limpiarlo era el parpadeo de 29 de cada 30 ticks).
            if (Selector.Colony != _paintedColony || (byte)Selector.Kind != _paintedKind)
            {
                _paintedColony = Selector.Colony;
                _paintedKind = (byte)Selector.Kind;
                _paintedTick = -1;
                ClearTiles();
            }

            if ((int)view.Tick == _paintedTick) return;

            // F8: el tablero enseña TODOS los tipos de feromona de la colonia
            // elegida, cada uno con su color (ver `PheromoneCompositeModel`).
            if (ShowAllLayers && PaletteByLayer)
            {
                PaintAllLayers(view);
                return;
            }

            string? payload = Selector.Payload(view);
            if (payload == null) return;

            if (!Model.Decode(payload))
            {
                ClearTiles();
                return;
            }
            _paintedTick = (int)view.Tick;

            int w = Model.Width, h = Model.Height;
            if (w == 0 || h == 0)
            {
                ClearTiles();
                return;
            }
            EnsureTextures(w, h);

            var rgb = PaletteByLayer
                ? Selector.Palette
                : new Streaming.PheromoneSelectorModel.Rgb(TrailColor.r, TrailColor.g, TrailColor.b);
            byte r = (byte)(rgb.R * 255f), g = (byte)(rgb.G * 255f), b = (byte)(rgb.B * 255f);
            var cells = Model.Cells;
            var pixels = new Color32[cells.Length];
            // Curva de PANTALLA (no toca el dato, ver `PheromoneRamp`): se descarta
            // la cola difusa por debajo del 18 % del máximo de la capa y lo que
            // queda va del borde al núcleo opaco. Antes el alfa era el byte tal
            // cual, así que el camino se quedaba en el valor del byte (sin llegar
            // a sólido) y el halo de difusión quedaba esparcido por el tablero: se
            // leía como una mancha. El corte es RELATIVO a la capa, así que un
            // rastro incipiente (máximo 8/255) se sigue viendo entero.
            byte layerMax = Streaming.PheromoneRamp.LayerMax(cells);
            // Y SE PINTAN EN ESPEJO: la celda (0,0) del canal E es el mundo (0,0)
            // —el Core deposita en `Cell(ant.X), Cell(ant.Y)`—, pero el quad es un
            // `PrimitiveType.Plane` de Unity y sus UV no empiezan ahí: el vértice
            // local (5,5) —mundo (+x,+z), el rincón opuesto— lleva uv (0,0), así
            // que el mapa salía ROTADO 180° y el rastro se veía en la otra mitad
            // del tablero. Medido en el frame vivo con `ProbeRecruitmentShot`
            // (ver docs §8bis): con el giro, casi todas las celdas fuertes caen
            // sobre su píxel; sin él, casi ninguna. Se corrige AQUÍ (y no girando
            // el quad) para que el modelo siga diciendo «celda (x,y) = mundo
            // (x·8, y·8)», que es lo que leen la suite y el HUD.
            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                int dst = (h - 1 - y) * w;
                for (int x = 0; x < w; x++)
                {
                    byte a = Streaming.PheromoneRamp.AlphaByte(cells[row + x], layerMax);
                    pixels[dst + (w - 1 - x)] = new Color32(r, g, b, a);
                }
            }
            _cpu!.SetPixels32(pixels);
            _cpu.Apply(false);
            Graphics.Blit(_cpu, _rt);
        }

        /// <summary>
        /// Pinta de una vez TODAS las capas que el stream trae para la colonia
        /// elegida: food, home, alarm y huella, cada tipo con su color. El modelo
        /// puro compone la mezcla y aquí solo se voltea a espejo (misma corrección
        /// del quad que en la capa suelta) y se sube a la RenderTexture.
        ///
        /// La capa PRINCIPAL del selector se decodifica además en <see cref="Model"/>:
        /// es la que miden el HUD y las sondas, así que sus cifras siguen siendo
        /// las del mismo rastro de siempre.
        /// </summary>
        private void PaintAllLayers(Streaming.GameStreamParser.TickView view)
        {
            _compose.Clear();
            _layerModels.Clear();

            int w = 0, h = 0;
            for (int k = 0; k < AllKinds.Length; k++)
            {
                var kind = AllKinds[k];
                string? data = FindPayload(view, Selector.Colony, kind);
                if (data == null) continue;

                var target = kind == Selector.Kind ? Model : TakeLayerModel(kind);
                if (!target.Decode(data)) continue;
                if (w == 0) { w = target.Width; h = target.Height; }
                if (target.Width != w || target.Height != h) continue;
                _compose.Add(new Streaming.PheromoneCompositeModel.Layer(kind, target.Cells));
            }

            // Canal E clásico (una sola capa, home de la colonia 0): se pinta igual,
            // en verde, para que una escena sin `--phero-layers` no se quede a oscuras.
            if (_compose.Count == 0 && !string.IsNullOrEmpty(view.Phero)
                && Selector.Colony == 0)
            {
                if (!Model.Decode(view.Phero)) { ClearTiles(); return; }
                w = Model.Width;
                h = Model.Height;
                _compose.Add(new Streaming.PheromoneCompositeModel.Layer(
                    Streaming.PheromoneSelectorModel.Layer.Home, Model.Cells));
            }

            if (_compose.Count == 0 || w == 0 || h == 0)
            {
                // Un tick SIN paquete del canal E (llega 1 de cada PheroEvery)
                // NO borra el tablero: se conserva el último mapa pintado, igual
                // que en la capa suelta. Borrarlo apagaba el rastro 29 de cada
                // 30 frames — se veía un fogonazo y desaparecía. Solo se limpia
                // cuando el tick SÍ trae feromonas pero ninguna de la colonia
                // elegida: ahí dejar la anterior sería mentir con la etiqueta.
                if (view.PheroSet.Count > 0 || !string.IsNullOrEmpty(view.Phero))
                    ClearTiles();
                return;
            }

            _paintedTick = (int)view.Tick;
            EnsureTextures(w, h);

            int cells = w * h;
            if (_rgb.Length < cells * 3) _rgb = new byte[cells * 3];
            if (_alpha.Length < cells) _alpha = new byte[cells];
            Streaming.PheromoneCompositeModel.Compose(_compose, w, h, _rgb, _alpha);

            var pixels = new Color32[cells];
            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                int dst = (h - 1 - y) * w;
                for (int x = 0; x < w; x++)
                {
                    int i = row + x;
                    pixels[dst + (w - 1 - x)] = new Color32(
                        _rgb[i * 3], _rgb[i * 3 + 1], _rgb[i * 3 + 2], _alpha[i]);
                }
            }
            _cpu!.SetPixels32(pixels);
            _cpu.Apply(false);
            Graphics.Blit(_cpu, _rt);
        }

        /// <summary>Las cuatro capas que el canal E puede traer, en orden de pintado.</summary>
        private static readonly Streaming.PheromoneSelectorModel.Layer[] AllKinds =
        {
            Streaming.PheromoneSelectorModel.Layer.Food,
            Streaming.PheromoneSelectorModel.Layer.Home,
            Streaming.PheromoneSelectorModel.Layer.Alarm,
            Streaming.PheromoneSelectorModel.Layer.Footprint,
        };

        private Streaming.PheromoneTileModel TakeLayerModel(Streaming.PheromoneSelectorModel.Layer kind)
        {
            if (!_layerModels.TryGetValue(kind, out var model))
            {
                model = new Streaming.PheromoneTileModel();
                _layerModels[kind] = model;
            }
            return model;
        }

        /// <summary>Paquete de (colonia, tipo) en el tick, o null si no viaja.</summary>
        private static string? FindPayload(Streaming.GameStreamParser.TickView view, int colony,
            Streaming.PheromoneSelectorModel.Layer kind)
        {
            for (int i = 0; i < view.PheroSet.Count; i++)
            {
                var e = view.PheroSet[i];
                if (e.Colony == colony && e.Kind == (byte)kind) return e.Data;
            }
            return null;
        }

        /// <summary>Deja la capa en blanco (canal ausente o capa sin datos).</summary>
        private void ClearTiles()
        {
            if (_cpu == null || _rt == null) return;
            var pixels = new Color32[_cpu.width * _cpu.height]; // (0,0,0,0)
            _cpu.SetPixels32(pixels);
            _cpu.Apply(false);
            Graphics.Blit(_cpu, _rt);
        }

        private void EnsureTextures(int w, int h)
        {
            if (_cpu != null && (_cpu.width != w || _cpu.height != h))
            {
                Destroy(_cpu);
                _cpu = null;
            }
            if (_cpu == null) _cpu = new Texture2D(w, h, TextureFormat.RGBA32, false);

            if (_rt != null && (_rt.width != w || _rt.height != h))
            {
                _rt.Release();
                _rt = null;
            }
            if (_rt == null)
            {
                _rt = new RenderTexture(w, h, 0);
                TargetMaterial!.mainTexture = _rt;
            }
        }

        private void OnDestroy()
        {
            // Fuera de Play `Destroy` no se puede llamar (Unity lo reporta como
            // ERROR en la consola, y la consola es parte del veredicto del Play
            // pass): al recrear la escena desde el editor este componente se
            // destruye en modo edición. En Play, el camino normal es `Destroy`.
            if (!Application.isPlaying)
            {
                if (_cpu != null) DestroyImmediate(_cpu);
                if (_rt != null) { _rt.Release(); DestroyImmediate(_rt); }
                return;
            }
            if (_cpu != null) Destroy(_cpu);
            if (_rt != null) _rt.Release();
        }
    }
}

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
    /// Si la capa pedida no viene en el stream se pinta VACÍO — nunca se deja el
    /// frame anterior: un rastro viejo con la etiqueta equivocada es peor que no
    /// pintar nada (se leería como «esta colonia no tiene rastro»).
    /// </summary>
    public sealed class PheromoneTileBehaviour : MonoBehaviour
    {
        [Tooltip("Presenter cuyo stream lleva el canal E (--phero-every > 0).")]
        public SimPresenterBehaviour? Presenter;

        [Tooltip("Material con la RenderTexture destino (shader Unlit/Texture).")]
        public Material? TargetMaterial;

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

        /// <summary>Modelo puro decodificado (tests e inspección).</summary>
        public readonly Streaming.PheromoneTileModel Model = new();

        private Texture2D? _cpu;
        private RenderTexture? _rt;
        private int _paintedTick = -1;
        private int _paintedColony = int.MinValue;
        private byte _paintedKind = 255;

        /// <summary>Etiqueta de la capa pintada («feromonas · colonia 1 · alarm»).</summary>
        public string LayerLabel => Selector.Label;

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
            Selector.CycleLayer();
            _paintedTick = -1;
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

            if (Input.GetKeyDown(CycleLayerKey)) CycleLayer();
            if (Input.GetKeyDown(CycleColonyKey)) CycleColony();

            var view = presenter.Presenter.CurrentTick;
            if (view == null) return;

            string? payload = Selector.Payload(view);
            bool yaPintado = (int)view.Tick == _paintedTick
                             && Selector.Colony == _paintedColony
                             && (byte)Selector.Kind == _paintedKind;
            if (yaPintado) return;

            _paintedTick = (int)view.Tick;
            _paintedColony = Selector.Colony;
            _paintedKind = (byte)Selector.Kind;

            if (payload == null || !Model.Decode(payload))
            {
                ClearTiles();
                return;
            }

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
            for (int i = 0; i < cells.Length; i++)
                pixels[i] = new Color32(r, g, b, cells[i]);
            _cpu!.SetPixels32(pixels);
            _cpu.Apply(false);
            Graphics.Blit(_cpu, _rt);
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

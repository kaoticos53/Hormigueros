using UnityEngine;

namespace AntSim.Unity.Scripts.Presenter
{
    /// <summary>
    /// Render de feromonas (F4.5) — wire-up FINO sobre <see cref="Streaming.PheromoneTileModel"/>.
    /// Un quad bajo las hormigas muestra la capa FoodTrail de la colonia 0: el
    /// decodificador puro llena la textura CPU-side y este componente la sube a
    /// la RenderTexture del material (filtro bilinear para que las celdas de 8 u
    /// se vean como rastros continuos). Pausa/velocidad del presenter aplican
    /// solas: la textura solo cambia cuando llega un paquete nuevo.
    /// </summary>
    public sealed class PheromoneTileBehaviour : MonoBehaviour
    {
        [Tooltip("Presenter cuyo stream lleva el canal E (--phero-every > 0).")]
        public SimPresenterBehaviour? Presenter;

        [Tooltip("Material con la RenderTexture destino (shader Unlit/Texture).")]
        public Material? TargetMaterial;

        [ColorUsage(false)]
        [Tooltip("Color del rastro a intensidad máxima.")]
        public Color TrailColor = new Color(0.35f, 0.85f, 0.45f);

        /// <summary>Modelo puro decodificado (tests e inspección).</summary>
        public readonly Streaming.PheromoneTileModel Model = new();

        private Texture2D? _cpu;
        private RenderTexture? _rt;
        private int _seenTick = -1;

        private void Update()
        {
            var presenter = Presenter;
            if (presenter == null || TargetMaterial == null) return;

            var view = presenter.Presenter.CurrentTick;
            if (view == null || view.Tick == (ulong)_seenTick) return;
            if (string.IsNullOrEmpty(view.Phero)) return;
            _seenTick = (int)view.Tick;

            if (!Model.Decode(view.Phero)) return;

            int w = Model.Width, h = Model.Height;
            if (w == 0 || h == 0) return;

            EnsureTextures(w, h);

            var pixels = new Color32[w * h];
            var cells = Model.Cells;
            byte r = (byte)(TrailColor.r * 255f), g = (byte)(TrailColor.g * 255f),
                 b = (byte)(TrailColor.b * 255f);
            for (int i = 0; i < cells.Length; i++)
            {
                byte a = cells[i];
                pixels[i] = new Color32(r, g, b, a);
            }
            _cpu!.SetPixels32(pixels);
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
            if (_cpu != null) Destroy(_cpu);
            if (_rt != null) _rt.Release();
        }
    }
}

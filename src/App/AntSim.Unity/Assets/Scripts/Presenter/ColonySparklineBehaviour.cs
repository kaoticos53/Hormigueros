using UnityEngine;

namespace AntSim.Unity.Scripts.Presenter
{
    /// <summary>
    /// F5.1 — gráfica de reserva de UNA colonia en su tarjeta. Wire-up FINO sobre
    /// <see cref="Streaming.ColonySparklineModel"/> (puro): el modelo guarda la
    /// serie 1 Hz y produce la textura; este componente la sube a un RawImage y la
    /// tiñe según la reserva (rojo por debajo del 20%, el mismo umbral del
    /// contrato). Sin lógica propia que pueda discrepar del modelo: si la gráfica
    /// se viera mal, se arregla en el modelo (y hay tests headless).
    /// </summary>
    public sealed class ColonySparklineBehaviour : MonoBehaviour
    {
        [Tooltip("Presenter del que sale el canal A (reserva por colonia).")]
        public SimPresenterBehaviour? Presenter;

        [Tooltip("Colonia cuya serie se dibuja.")]
        public int ColonyId;

        [Tooltip("RawImage destino (acepta una textura generada; un Image pediría Sprite).")]
        public UnityEngine.UI.RawImage? Target;

        [ColorUsage(false)] public Color NormalColor = new Color(0.35f, 0.75f, 0.30f);
        [ColorUsage(false)] public Color LowColor = new Color(0.90f, 0.20f, 0.15f);

        [Tooltip("Resolución interna de la textura (px). 1 px por muestra como máximo.")]
        public int Width = 212;
        public int Height = 24;

        /// <summary>Modelo puro (tests e inspección).</summary>
        public readonly Streaming.ColonySparklineModel Model = new();

        private Texture2D? _tex;
        private Color32[] _pixels = new Color32[0];
        private bool _painted;
        private bool _paintedLow;

        /// <summary>Última fracción de reserva dibujada (0..1); útil para la sonda.</summary>
        public float LatestFraction => Model.For(ColonyId).Latest;

        private void Update()
        {
            var presenter = Presenter;
            if (presenter == null || Target == null) return;

            var view = presenter.Presenter.CurrentTick;
            if (view == null) return;
            if (!Model.Observe(view)) return;   // muestrea a 1 Hz: el resto de ticks no cambia nada

            Redraw();
        }

        /// <summary>Fuerza el repintado con la serie actual (punto de entrada sin
        /// dispositivo: la sonda del Play pass puede pedir un frame sin esperar al
        /// siguiente segundo de simulación).</summary>
        public void Redraw()
        {
            if (Target == null) return;
            int w = Width < 1 ? 1 : Width;
            int h = Height < 1 ? 1 : Height;

            if (_tex == null || _tex.width != w || _tex.height != h)
            {
                if (_tex != null) DestroyTex(_tex);
                _tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };
                _pixels = new Color32[w * h];
                Target.texture = _tex;
            }

            bool low = Model.For(ColonyId).Low;
            var tint = low ? LowColor : NormalColor;
            byte r = (byte)(tint.r * 255f), g = (byte)(tint.g * 255f), b = (byte)(tint.b * 255f);

            var rgba = Model.Render(ColonyId, w, h);
            for (int i = 0; i < _pixels.Length; i++)
            {
                int p = i * 4;
                byte a = p + 3 < rgba.Length ? rgba[p + 3] : (byte)0;
                _pixels[i] = a == 0 ? new Color32(0, 0, 0, 0) : new Color32(r, g, b, a);
            }
            _tex!.SetPixels32(_pixels);
            _tex.Apply(false);
            _painted = true;
            _paintedLow = low;
        }

        /// <summary>La sonda puede preguntar si la gráfica se llegó a pintar y si
        /// estaba en rojo, sin leer píxeles de la pantalla.</summary>
        public bool HasPainted => _painted;
        public bool PaintedLow => _paintedLow;

        private void DestroyTex(Texture2D tex)
        {
            // Fuera de Play `Destroy` no se puede llamar (Unity lo reporta como
            // ERROR y la consola es parte del veredicto del Play pass).
            if (Application.isPlaying) Destroy(tex);
            else DestroyImmediate(tex);
        }

        private void OnDestroy()
        {
            if (_tex != null) DestroyTex(_tex);
        }
    }
}

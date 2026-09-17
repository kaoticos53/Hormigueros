using UnityEngine;
using AntSim.Unity.Scripts.Streaming;

namespace AntSim.Unity.Scripts.Presenter
{
    /// <summary>
    /// F5.3ter — panel de APRENDIZAJE de UNA colonia en su tarjeta: la curva de
    /// fitness por generación (textura generada por el modelo puro) y dos líneas
    /// de datos (cohorte/élite y cobertura del mundo). Wire-up fino sobre
    /// <see cref="LearningPanelModel"/>: aquí no hay ni una regla propia — si algo
    /// se ve mal se arregla en el modelo, que tiene tests headless.
    ///
    /// El color del trazo dice la COBERTURA (lo que el jugador quiere saber de un
    /// vistazo): azul = la colonia conoce una parte sustancial de su mundo, ámbar =
    /// apenas ha salido del nido. La forma de la curva dice el APRENDIZAJE.
    /// </summary>
    public sealed class LearningPanelBehaviour : MonoBehaviour
    {
        [Tooltip("Presenter del que sale el canal C (bloque de aprendizaje).")]
        public SimPresenterBehaviour? Presenter;

        [Tooltip("Colonia cuyo aprendizaje se dibuja.")]
        public int ColonyId;

        [Tooltip("RawImage de la curva (textura generada; un Image pediría Sprite).")]
        public UnityEngine.UI.RawImage? Target;

        [Tooltip("Texto de las dos líneas: fitness/generación y mundo/cobertura.")]
        public UnityEngine.UI.Text? Info;

        [ColorUsage(false)] public Color CurveColor = new Color(0.45f, 0.65f, 1.00f);
        [ColorUsage(false)] public Color LowCoverageColor = new Color(0.95f, 0.70f, 0.25f);
        [ColorUsage(false)] public Color IdleColor = new Color(0.55f, 0.58f, 0.62f);

        public int Width = 406;
        public int Height = 26;

        /// <summary>Modelo puro (tests e inspección desde la sonda del Play pass).</summary>
        public readonly LearningPanelModel Model = new();

        private Texture2D? _tex;
        private Color32[] _pixels = new Color32[0];
        private bool _painted;

        public bool HasPainted => _painted;

        /// <summary>Última cobertura dibujada (0..1); la sonda la lee sin tocar píxeles.</summary>
        public float Coverage => Model.For(ColonyId).Coverage;

        private void Update()
        {
            var presenter = Presenter;
            if (presenter == null) return;
            var view = presenter.Presenter.CurrentTick;
            if (view == null) return;
            if (!Model.Observe(view)) return;   // el bloque llega a 1 Hz: el resto de ticks no cambia nada
            Redraw();
        }

        /// <summary>Repinta con el modelo actual (punto de entrada para la sonda, sin
        /// esperar al siguiente bloque de canal C).</summary>
        public void Redraw()
        {
            var panel = Model.For(ColonyId);

            if (Info != null)
            {
                string text = panel.FitnessLine() + "\n" + panel.WorldLine();
                if (Info.text != text) Info.text = text;
            }

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

            var tint = panel.CoverageLevel switch
            {
                2 => CurveColor,
                1 => LowCoverageColor,
                _ => IdleColor,
            };
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
        }

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

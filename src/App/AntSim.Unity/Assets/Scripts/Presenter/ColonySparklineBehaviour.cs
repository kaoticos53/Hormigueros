using UnityEngine;

namespace AntSim.Unity.Scripts.Presenter
{
    /// <summary>
    /// F5.1 — gráfica de UNA colonia en su tarjeta: RESERVA (área) y DESCARGAS por
    /// ventana (barras). Wire-up FINO sobre
    /// <see cref="Streaming.ColonySparklineModel"/> (puro): el modelo guarda las
    /// series 1 Hz y produce las dos máscaras; este componente las sube a un
    /// RawImage, tiñe la reserva según su estado (rojo por debajo del 20%, el mismo
    /// umbral del contrato) y pinta las barras con su color. Sin lógica propia que
    /// pueda discrepar del modelo: si la gráfica se viera mal, se arregla en el
    /// modelo (y hay tests headless).
    ///
    /// QUIÉN LO ALIMENTA, y por qué no se sondea a sí mismo. El HUD drena TODOS los
    /// ticks presentados de cada frame (<see cref="HudLayoutBehaviour"/>) y se los
    /// pasa aquí, uno por tick, con <see cref="Observe"/>. Antes este componente
    /// leía `CurrentTick` en su Update y eso bastaba para la reserva —muestrea a
    /// 1 Hz y repetir un tick no cambia nada—, pero NO para contar descargas: a
    /// velocidad alta un frame presenta varios ticks y los del medio se perdían.
    /// </summary>
    public sealed class ColonySparklineBehaviour : MonoBehaviour
    {
        [Tooltip("Colonia cuya serie se dibuja.")]
        public int ColonyId;

        [Tooltip("RawImage destino (acepta una textura generada; un Image pediría Sprite).")]
        public UnityEngine.UI.RawImage? Target;

        [ColorUsage(false)] public Color NormalColor = new Color(0.35f, 0.75f, 0.30f);
        [ColorUsage(false)] public Color LowColor = new Color(0.90f, 0.20f, 0.15f);

        /// <summary>Color de las barras de descargas: ámbar, el mismo tono de
        /// «recurso entrando» que usan los avisos, para no confundirlo con el
        /// estado de la reserva (que es el que cambia a rojo).</summary>
        [ColorUsage(false)] public Color DropColor = new Color(0.95f, 0.72f, 0.25f);

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

        /// <summary>Descargas de la última ventana dibujada; útil para la sonda.</summary>
        public int LatestDrops => Model.For(ColonyId).DropsLatest;

        /// <summary>
        /// Un tick del stream, en orden. El HUD llama esto por CADA tick presentado
        /// (no solo por el último del frame), que es lo que hace que la cuenta de
        /// descargas no dependa de los fps: el modelo muestrea a 1 Hz por su cuenta
        /// y aquí solo se le pasa el testigo.
        /// </summary>
        public void Observe(Streaming.GameStreamParser.TickView view)
        {
            if (view == null) return;
            if (!Model.Observe(view)) return;   // muestrea a 1 Hz: el resto de ticks no cambia la imagen
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

            // Las descargas van ENCIMA y con su color: sobre el área se leen como
            // barras ámbar y por encima de ella (cuando la reserva estaba baja) se
            // recortan contra el fondo, que es justo cuando importan.
            var drops = Model.RenderDrops(ColonyId, w, h);
            byte dr = (byte)(DropColor.r * 255f), dg = (byte)(DropColor.g * 255f), db = (byte)(DropColor.b * 255f);
            for (int i = 0; i < _pixels.Length; i++)
            {
                int p = i * 4;
                if (p + 3 >= drops.Length || drops[p + 3] == 0) continue;
                _pixels[i] = new Color32(dr, dg, db, 255);
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

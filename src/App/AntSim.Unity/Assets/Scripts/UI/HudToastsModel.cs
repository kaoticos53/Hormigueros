using System.Collections.Generic;

namespace AntSim.Unity.Scripts.Streaming
{
    /// <summary>
    /// Cola de toasts del HUD (F4.2, contrato HUD §1) — modelo PURO. Consume las
    /// alertas del canal D (derivadas POR EL CORE con AlertDeriver: la UI nunca
    /// inventa umbrales) y gestiona el comportamiento de pila: sin duplicados
    /// (misma clave estable), expiración, tope de pila y tasa por colonia.
    /// Sin UnityEngine: verificado headless en la suite.
    /// </summary>
    public sealed class HudToastsModel
    {
        public const int DefaultMaxStack = 4;        // tope del pila (contrato §1)
        public const float DefaultLifetime = 6f;     // segundos de sim visibles

        /// <summary>Toast activo: el texto y el ancla vienen del canal D.</summary>
        public sealed class Toast
        {
            public readonly string Key;
            public readonly byte Level;
            public readonly int ColonyId;
            public readonly float X, Y;   // ancla de cámara (-1 = no aplica)
            public readonly ulong Tick;   // tick de emisión
            public readonly string Text;
            public float Age;             // segundos de sim desde emisión

            public Toast(in GameStreamParser.AlertView a)
            { Key = a.Key; Level = a.Level; ColonyId = a.ColonyId;
              X = a.X; Y = a.Y; Tick = a.Tick; Text = a.Text; Age = 0f; }
        }

        private readonly List<Toast> _active = new();
        private readonly HashSet<string> _keys = new();
        private readonly float _lifetime;
        private readonly int _maxStack;

        public HudToastsModel(float lifetime = DefaultLifetime, int maxStack = DefaultMaxStack)
        { _lifetime = lifetime; _maxStack = maxStack; }

        public IReadOnlyList<Toast> Active => _active;

        /// <summary>Ingiere las alertas de un tick (canal D, ya deduplicadas por
        /// el Core por cadencia — el model solo evita duplicados visuales).</summary>
        public void Observe(GameStreamParser.TickView v)
        {
            foreach (var a in v.Alerts) Push(new Toast(a));
        }

        /// <summary>Avanza el envejecimiento (segundos de SIM, no de reloj: la
        /// pausa congela los toasts, como el mundo).</summary>
        public void Tick(float dtSim)
        {
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                _active[i].Age += dtSim;
                if (_active[i].Age >= _lifetime)
                {
                    _keys.Remove(_active[i].Key);
                    _active.RemoveAt(i);
                }
            }
        }

        /// <summary>Empuja un toast: reemplaza al de su misma clave (sin dobles)
        /// y hace saltar al más viejo si se supera el tope del pila.</summary>
        public void Push(Toast t)
        {
            // Reemplazo por clave: la re-emisión de una alerta cadenciada refresca
            // su toast en vez de apilar un duplicado.
            for (int i = 0; i < _active.Count; i++)
            {
                if (_active[i].Key == t.Key)
                {
                    _keys.Remove(_active[i].Key);
                    _active.RemoveAt(i);
                    break;
                }
            }
            _keys.Add(t.Key);
            _active.Insert(0, t); // el más nuevo arriba
            while (_active.Count > _maxStack)
            {
                Toast oldest = _active[_active.Count - 1];
                _keys.Remove(oldest.Key);
                _active.RemoveAt(_active.Count - 1);
            }
        }

        /// <summary>Render de texto v1 de la pila (la más nueva arriba) — para la
        /// prueba visual headless y para HUDs sin uGUI; el Behaviour pinta cada
        /// toast con su color de nivel.</summary>
        public IEnumerable<string> RenderLines()
        {
            foreach (var t in _active)
            {
                string tag = t.ColonyId >= 0 ? $"[c{t.ColonyId}] " : "";
                yield return $"{Glyph(t.Level)} {tag}{t.Text}";
            }
        }

        /// <summary>Glifo de NIVEL por forma (F5.1): sin emoji — la fuente por
        /// defecto de uGUI no los tiene y salían cajas. El color lo pone el HUD
        /// (<c>ToastLevelColors</c>), el glifo solo refuerza la jerarquía.</summary>
        internal static string Glyph(byte lvl) => lvl switch
        {
            1 => "◐", // ámbar
            2 => "●", // verde
            3 => "■", // rojo
            _ => "·",  // info
        };
    }
}

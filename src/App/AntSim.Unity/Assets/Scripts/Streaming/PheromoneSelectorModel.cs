using System;

namespace AntSim.Unity.Scripts.Streaming
{
    /// <summary>
    /// F5.1 — selector de la capa de feromonas que se pinta, con su paleta.
    ///
    /// POR QUÉ. Las capas son POR COLONIA (una hormiga solo lee las suyas) y hay
    /// tres tipos activos. El canal E clásico enseñaba una sola —home de la
    /// colonia 0—, así que con dos colonias compitiendo el espectador veía media
    /// partida y no podía separar «rastro» de «peligro». Este modelo es PURO (sin
    /// UnityEngine): decide qué paquete se pinta y de qué color, y el MonoBehaviour
    /// solo lo obedece. Así el ciclo de capas y la paleta se verifican headless.
    ///
    /// Compatibilidad: si el stream trae el canal clásico (`phero`) y se pide
    /// exactamente esa capa (colonia 0, Home), se usa; con cualquier otra
    /// combinación no hay dato que pintar y se dice explícitamente (null) en vez
    /// de mentir pintando otra cosa.
    /// </summary>
    public sealed class PheromoneSelectorModel
    {
        /// <summary>Color en float 0..1 — neutro, para no depender de UnityEngine.</summary>
        public readonly struct Rgb
        {
            public readonly float R, G, B;
            public Rgb(float r, float g, float b) { R = r; G = g; B = b; }
        }

        /// <summary>
        /// Capa de feromona tal como viaja en el canal E. Los valores SON el
        /// contrato: coinciden con el ordinal de <c>AntSim.Core.Pheromone.PheromoneKind</c>
        /// (0 food, 1 home, 2 alarm). Se declara aquí y no se importa del Core a
        /// propósito: la app Unity NO referencia el Core — consume el stream, y esa
        /// frontera es lo que mantiene los modelos puros compilables headless sin
        /// arrastrar la simulación. El test que compara ambos ordinales es el que
        /// impide que los dos lados se separen.
        /// </summary>
        public enum Layer : byte
        {
            Food = 0,
            Home = 1,
            Alarm = 2
        }

        /// <summary>Orden de ciclo canónico: la capa que el canal clásico ya enseñaba
        /// (home) primero, para que el comportamiento por defecto no cambie.</summary>
        private static readonly Layer[] Cycle = { Layer.Home, Layer.Food, Layer.Alarm };

        public int Colony { get; private set; }
        public Layer Kind { get; private set; } = Layer.Home;
        public int ColonyCount { get; private set; } = 1;

        public PheromoneSelectorModel(int colonyCount = 1)
        {
            ColonyCount = colonyCount < 1 ? 1 : colonyCount;
        }

        /// <summary>Nombre corto y estable de la capa (el que usa el HUD).</summary>
        public static string NameOf(Layer kind) => kind switch
        {
            Layer.Food => "food",
            Layer.Home => "home",
            Layer.Alarm => "alarm",
            _ => "?"
        };

        /// <summary>Paleta por capa: cada tipo tiene su color, y el home conserva el
        /// verde que el render usaba antes de existir el selector.</summary>
        public static Rgb PaletteOf(Layer kind) => kind switch
        {
            Layer.Food => new Rgb(0.95f, 0.72f, 0.30f),  // ámbar: comida
            Layer.Home => new Rgb(0.35f, 0.85f, 0.45f),  // verde: casa
            Layer.Alarm => new Rgb(0.90f, 0.35f, 0.32f), // rojo: peligro
            _ => new Rgb(0.70f, 0.70f, 0.70f)
        };

        public Rgb Palette => PaletteOf(Kind);

        /// <summary>Etiqueta para el HUD: «feromonas · colonia 1 · home».</summary>
        public string Label => $"feromonas · colonia {Colony} · {NameOf(Kind)}";

        public void SetColonyCount(int count) => ColonyCount = count < 1 ? 1 : count;

        public void Select(Layer kind) => Kind = kind;

        public void SelectColony(int colony) => Colony = colony < 0 ? 0 : colony;

        /// <summary>Pasa a la siguiente capa del ciclo (home → food → alarm → home).</summary>
        public Layer CycleLayer()
        {
            int i = Array.IndexOf(Cycle, Kind);
            Kind = Cycle[(i < 0 ? 0 : i + 1) % Cycle.Length];
            return Kind;
        }

        /// <summary>Pasa a la siguiente colonia, envolviendo (0 → 1 → … → 0).</summary>
        public int CycleColony()
        {
            Colony = (Colony + 1) % ColonyCount;
            return Colony;
        }

        /// <summary>
        /// Rejilla base64 que hay que pintar en este tick, o null si esta capa no
        /// viene en el stream. Prioriza el canal E múltiple; el clásico solo vale
        /// para (colonia 0, Home), que es literalmente lo que emite.
        /// </summary>
        public string? Payload(GameStreamParser.TickView? view)
        {
            if (view == null) return null;

            for (int i = 0; i < view.PheroSet.Count; i++)
            {
                var e = view.PheroSet[i];
                if (e.Colony == Colony && e.Kind == (byte)Kind) return e.Data;
            }

            if (Colony == 0 && Kind == Layer.Home && !string.IsNullOrEmpty(view.Phero))
                return view.Phero;

            return null;
        }

        /// <summary>Capas distintas presentes en el tick (para saber qué hay que ofrecer).</summary>
        public static int LayerCountIn(GameStreamParser.TickView? view)
            => view == null ? 0 : view.PheroSet.Count;
    }
}

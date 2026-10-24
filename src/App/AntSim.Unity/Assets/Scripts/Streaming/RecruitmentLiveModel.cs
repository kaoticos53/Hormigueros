using System;

namespace AntSim.Unity.Scripts.Streaming
{
    /// <summary>
    /// F8 — LAS CIFRAS DE RECLUTAMIENTO DEL TABLERO, no las de la configuración.
    ///
    /// POR QUÉ EXISTE. La línea del HUD describía las PERILLAS con las que se
    /// sembró el mundo (`montones=8 · rastro=1× / 2100 s`), que es lo que se pide
    /// al CLI, no lo que se está viendo: si los montones ya se habían comido o el
    /// rastro no tenía ni una celda pintada, el HUD seguía prometiendo ocho
    /// montones y un rastro. Aquí se cuentan las cosas del tick PRESENTADO —los
    /// grupos de comida que quedan, los ítems y las celdas de feromona que el
    /// render pinta de verdad— para que la cifra del HUD y los píxeles del
    /// tablero digan lo mismo.
    ///
    /// El rastro se cuenta con la MISMA curva del render (<see cref="PheromoneRamp"/>):
    /// «celdas» es lo que se ve, no lo que viaja en el canal E. El dato sigue
    /// intacto — este modelo solo lee.
    ///
    /// Modelo PURO (sin UnityEngine): se compila en la suite headless.
    /// </summary>
    public sealed class RecruitmentLiveModel
    {
        /// <summary>Ítems que hacen MONTÓN. El mismo mínimo que usa la sonda del
        /// tablero: un ítem suelto no es un montón sembrado.</summary>
        public const int PileMinItems = 3;

        /// <summary>Unidades de mundo por celda del canal E (contrato del Core).</summary>
        public const int CellSizeUnits = 8;

        /// <summary>Comida en el mundo (montones y sueltas).</summary>
        public int Items { get; private set; }

        /// <summary>Grupos de comida con al menos <see cref="PileMinItems"/> ítems
        /// dentro del radio de montón.</summary>
        public int Piles { get; private set; }

        /// <summary>Celdas de la capa pintada que se VEN (alfa &gt; 0).</summary>
        public int TrailCells { get; private set; }

        /// <summary>Máximo del canal E de la capa pintada (0 si vacía).</summary>
        public byte TrailMax { get; private set; }

        /// <summary>Ancho/alto (u) del corte que cruza la celda más fuerte.</summary>
        public int BandWidthUnits { get; private set; }
        public int BandHeightUnits { get; private set; }

        /// <summary>Nombre corto de la capa pintada («food», «home», …).</summary>
        public string Layer { get; private set; } = "";

        /// <summary>¿Hay alguna cifra que publicar? (false antes del primer tick).</summary>
        public bool HasWorld { get; private set; }

        /// <summary>Cuenta comida y montones del tick presentado. `radius` es el
        /// radio de montón de las perillas, el mismo con el que se sembró.</summary>
        public void Observe(GameStreamParser.TickView? view, float radius)
        {
            if (view == null)
            {
                Items = 0;
                Piles = 0;
                HasWorld = false;
                return;
            }
            HasWorld = true;
            int n = view.Items.Count;
            Items = n;
            Piles = n == 0 ? 0 : CountPiles(view, Math.Max(1f, radius));
        }

        /// <summary>
        /// Cuenta montones con union-find sobre la distancia entre ítems: dos ítems
        /// a menos de `radius` están en el mismo montón, y el montón exige
        /// <see cref="PileMinItems"/> ítems. Un montón sembrado (6–10 ítems en 18 u)
        /// cuenta uno; una migaja suelta, ninguno. O(n²) con n de decenas: se llama
        /// una vez por frame, no por tick.
        /// </summary>
        private static int CountPiles(GameStreamParser.TickView view, float radius)
        {
            int n = view.Items.Count;
            var parent = new int[n];
            for (int i = 0; i < n; i++) parent[i] = i;

            int Find(int i)
            {
                while (parent[i] != i)
                {
                    parent[i] = parent[parent[i]];
                    i = parent[i];
                }
                return i;
            }

            void Union(int a, int b)
            {
                int ra = Find(a), rb = Find(b);
                if (ra != rb) parent[ra] = rb;
            }

            float r2 = radius * radius;
            for (int i = 0; i < n; i++)
            {
                var a = view.Items[i];
                for (int j = i + 1; j < n; j++)
                {
                    var b = view.Items[j];
                    float dx = a.X - b.X, dy = a.Y - b.Y;
                    if (dx * dx + dy * dy <= r2) Union(i, j);
                }
            }

            var count = new int[n];
            for (int i = 0; i < n; i++) count[Find(i)]++;
            int piles = 0;
            for (int i = 0; i < n; i++)
                if (count[i] >= PileMinItems) piles++;
            return piles;
        }

        /// <summary>
        /// Cifras de la capa que el render está pintando. `cells` es la rejilla
        /// row-major del canal E (w×h) y `layer` su nombre corto. Sin paquete (o
        /// con la capa vacía) publica cero celdas: el HUD no puede enseñar el
        /// rastro de la capa anterior con la etiqueta nueva.
        /// </summary>
        public void ObserveTrail(string layer, int width, int height, byte[]? cells)
        {
            Layer = layer ?? "";
            TrailCells = 0;
            TrailMax = 0;
            BandWidthUnits = 0;
            BandHeightUnits = 0;
            if (cells == null || width <= 0 || height <= 0 || cells.Length < width * height)
                return;

            byte max = PheromoneRamp.LayerMax(cells);
            TrailMax = max;
            int painted = 0;
            for (int i = 0; i < cells.Length; i++)
                if (PheromoneRamp.AlphaByte(cells[i], max) > 0) painted++;
            TrailCells = painted;
            if (painted == 0) return;

            // La celda MÁS FUERTE de la capa (la primera si hay empate) y el corte
            // que la cruza sobre las celdas PINTADAS: es la cifra que dice si el
            // camino se lee como camino o como mancha.
            int best = 0;
            for (int i = 1; i < cells.Length; i++)
                if (cells[i] > cells[best]) best = i;
            int bx = best % width, by = best / width;

            int wx = 1, wy = 1;
            for (int x = bx - 1; x >= 0 && Painted(cells, width, x, by, max); x--) wx++;
            for (int x = bx + 1; x < width && Painted(cells, width, x, by, max); x++) wx++;
            for (int y = by - 1; y >= 0 && Painted(cells, width, bx, y, max); y--) wy++;
            for (int y = by + 1; y < height && Painted(cells, width, bx, y, max); y++) wy++;
            BandWidthUnits = wx * CellSizeUnits;
            BandHeightUnits = wy * CellSizeUnits;
        }

        private static bool Painted(byte[] cells, int width, int x, int y, byte max)
            => PheromoneRamp.AlphaByte(cells[y * width + x], max) > 0;
    }
}

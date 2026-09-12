using System;

namespace AntSim.Unity.Scripts.Streaming
{
    /// <summary>
    /// Decodificador del canal E (F4.5): el paquete <c>"phero"</c> del stream es
    /// la capa FoodTrail de la colonia 0 codificada como RLE por filas +
    /// base64. Modelo PURO (sin UnityEngine): decodifica a una rejilla de bytes
    /// cuantizados [0,255] que el Behaviour sube a la RenderTexture. La UI no
    /// interpreta la feromona: pinta lo que el Core emite.
    /// </summary>
    public sealed class PheromoneTileModel
    {
        private byte[] _cells = Array.Empty<byte>();
        private int _width, _height;

        /// <summary>Ancho/alto de la rejilla en celdas (0 hasta el primer paquete).</summary>
        public int Width => _width;
        public int Height => _height;

        /// <summary>Valor cuantizado [0,255] de una celda (0 si fuera de rango).</summary>
        public byte Cell(int x, int y)
            => (uint)x < (uint)_width && (uint)y < (uint)_height
                ? _cells[y * _width + x] : (byte)0;

        /// <summary>Rejilla completa (row-major, w×h). Longitud w*h tras el primer paquete.</summary>
        public byte[] Cells => _cells;

        /// <summary>
        /// Decodifica el payload base64. Formato: cabecera u16 LE (w, h) seguida
        /// de filas no vacías: u16 LE (y) + pares (valor byte, run byte). Las
        /// celdas no mencionadas quedan a 0 (empezamos con la rejilla limpia:
        /// cada paquete es un fotograma completo, no un delta).
        /// </summary>
        public bool Decode(string base64)
        {
            byte[] data;
            try { data = Convert.FromBase64String(base64); }
            catch (FormatException) { return false; }
            if (data.Length < 4) return false;

            int w = data[0] | (data[1] << 8);
            int h = data[2] | (data[3] << 8);
            if (w <= 0 || h <= 0) return false;

            if (_width != w || _height != h)
            {
                _width = w; _height = h;
                _cells = new byte[w * h];
            }
            else
            {
                Array.Clear(_cells, 0, _cells.Length); // fotograma completo: limpia primero
            }

            int i = 4;
            while (i + 2 <= data.Length)
            {
                int y = data[i] | (data[i + 1] << 8);
                i += 2;
                // La fila consume exactamente w celdas de runs (sin marcador de
                // fin: el límite de la fila es llegar a w).
                while (i + 1 < data.Length && _rowPos < w)
                {
                    byte v = data[i]; byte run = data[i + 1];
                    i += 2;
                    if (run == 0) return false;     // run nulo: payload roto
                    int xEnd = _rowPos + run;
                    if (xEnd > w) return false;     // run fuera de la fila
                    if (v != 0 && y < h)
                    {
                        int rowBase = y * w;
                        for (int k = _rowPos; k < xEnd; k++)
                            _cells[rowBase + k] = v;
                    }
                    _rowPos = xEnd;
                }
                _rowPos = 0;
            }
            return true;
        }

        private int _rowPos;

        /// <summary>Mapea coordenadas de mundo (unidades del Core) a índice de
        /// celda de la rejilla (CellSizeUnits = 8 u por celda).</summary>
        public int CellX(float worldX) => Math.Clamp((int)(worldX / 8f), 0, _width - 1);
        public int CellY(float worldY) => Math.Clamp((int)(worldY / 8f), 0, _height - 1);
    }
}

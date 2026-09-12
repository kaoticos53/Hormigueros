namespace AntSim.Unity.Scripts.Streaming
{
    /// <summary>
    /// F5.2 — Mapeo «rayo → plano del mundo» compartido por los dos handlers de
    /// click (<see cref="Presenter.AntPickClickHandler"/> para seleccionar,
    /// <see cref="Presenter.DropFoodClickHandler"/> para marcar un drop).
    ///
    /// Los handlers aportan el rayo de la cámara y el plano; la geometría —y sus
    /// dos casos degenerados— vive aquí. Es PURO y no usa UnityEngine (trabaja con
    /// floats), así que se compila y se verifica en la suite headless: el mismo
    /// código que decide dónde cae un click está cubierto por tests sin abrir el
    /// editor. Antes estaba duplicado, literal, en los dos handlers.
    /// </summary>
    public static class WorldPlaneRay
    {
        /// <summary>
        /// Punto en el que un rayo corta el plano horizontal <c>y = planeY</c>.
        /// Devuelve <c>false</c> —sin punto de impacto— en los dos casos en los que
        /// NO debe haber ni selección ni drop:
        ///
        /// · el rayo es PARALELO al plano (<paramref name="dirY"/> == 0), y
        /// · el plano queda DETRÁS del origen (<c>t &lt; 0</c>): un rayo que apunta
        ///   hacia ARRIBA desde por encima del suelo, es decir que se aleja del
        ///   plano en vez de cortarlo.
        ///
        /// El paralelismo y el signo de <c>t</c> se comparan de forma exacta: es la
        /// convención que ya tenían los handlers y la que hace predecible un caso
        /// límite que, de otro modo, colaría un click a kilómetros de distancia.
        /// </summary>
        public static bool TryHit(
            float originX, float originY, float originZ,
            float dirX, float dirY, float dirZ,
            float planeY,
            out float hitX, out float hitZ)
        {
            hitX = 0f;
            hitZ = 0f;
            if (dirY == 0f) return false;

            float t = (planeY - originY) / dirY;
            if (t < 0f) return false;

            hitX = originX + dirX * t;
            hitZ = originZ + dirZ * t;
            return true;
        }
    }
}

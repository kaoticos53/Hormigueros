namespace AntSim.Unity.Scripts.Streaming
{
    /// <summary>
    /// F5.1/Play-pass — escala del mundo. El Core simula en UNIDADES, no en
    /// celdas: <c>WorldSim.WorldWidth = gridCells * SimConstants.CellSizeUnits</c>
    /// (CellSizeUnits = 8 u). La vista asumía 1 celda = 1 u y dimensionaba la
    /// escena y los clicks para <c>grid</c> u, dejando el mundo real (grid×8 u)
    /// fuera de cámara y rechazando drops por «fuera del mundo».
    ///
    /// Esta constante es el ESPEJO de <c>SimConstants.CellSizeUnits</c>: la capa
    /// Unity no referencia el Core por diseño, así que la escala se declara aquí
    /// una sola vez y un test headless verifica que sigue coincidiendo con el Core.
    /// </summary>
    public static class WorldUnits
    {
        /// <summary>Unidades de mundo por celda — espejo de SimConstants.CellSizeUnits.</summary>
        public const float PerCell = 8f;

        /// <summary>Lado del mundo (cuadrado) en unidades para un grid de celdas.</summary>
        public static float WorldSize(int gridCells) => gridCells * PerCell;
    }
}

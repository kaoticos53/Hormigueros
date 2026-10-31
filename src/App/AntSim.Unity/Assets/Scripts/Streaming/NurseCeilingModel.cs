using System;

namespace AntSim.Unity.Scripts.Streaming
{
    /// <summary>
    /// EL TECHO DE NODRIZAS, tal como lo ve la VISTA (F12).
    ///
    /// ESPEJO DELIBERADO de <c>ColonyController.NurseShare</c> y
    /// <c>ColonyController.MaxAdultsPerColony</c>. Unity no referencia el Core
    /// (no hay asmdef ni plugin: la vista solo conoce sus modelos puros), así que
    /// la cuenta que acota la cría en vuelo existe en los dos lados. Lo que
    /// impide que deriven no es la disciplina sino una prueba
    /// (<c>NurseCeilingTests.Espejo_ElTechoDeLaVistaEsElDelCore</c>): si alguien
    /// mueve la cuota de nodrizas o el tope de adultas en el Core, la suite falla
    /// aquí y no en una partida donde el guard del HUD vigila un techo que ya no
    /// es el del mundo.
    ///
    /// POR QUÉ HACE FALTA. La puesta de la ronda se para cuando la cola de cría
    /// cubre lo que sostienen las nodrizas (`adultas · NurseShare`), así que el
    /// número de pupas tiene un techo duro: con la tropa llena son 6. El HUD
    /// pinta `pupae` del stream tal cual —no lo recorta— y por eso el techo se
    /// puede VIGILAR desde fuera: si algún día la tarjeta enseña 7, el mundo se
    /// salió de su economía o el stream dejó de ser el que se midió.
    ///
    /// Modelo PURO (sin UnityEngine): se compila también en la suite headless.
    /// </summary>
    public static class NurseCeilingModel
    {
        /// <summary>Fracción de adultas nodrizas — la misma que paga la
        /// alimentación larval en el Core.</summary>
        public const float NurseShare = 0.15f;

        /// <summary>Tope duro de adultas del diseño (la tarjeta lo pinta como "/40").</summary>
        public const int MaxAdults = 40;

        /// <summary>
        /// Cría en vuelo que sostienen esas adultas: `max(1, (int)(adultas ·
        /// NurseShare))`, la MISMA expresión que usa la puesta. El 1 evita que
        /// una colonia recién nacida (0 adultas) quede sin relevo.
        /// </summary>
        public static int For(int adults) => Math.Max(1, (int)(adults * NurseShare));

        /// <summary>El techo con la tropa llena: el número que el HUD no debe
        /// pasar nunca (6 con las constantes actuales).</summary>
        public static int Hard => For(MaxAdults);
    }
}

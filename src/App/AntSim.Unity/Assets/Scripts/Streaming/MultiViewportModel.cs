using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace AntSim.Unity.Scripts.Streaming
{
    /// <summary>
    /// F5.1bis — modelo PURO del multi-visor (fase B: varias simulaciones en
    /// vivo). Decide GEOMETRÍA y etiquetas de las N vistas; el bootstrapper de
    /// Unity solo la aplica (Camera.rect, RenderLayer y texto). Compilado en la
    /// suite headless (como el resto de modelos puros) para verificarlo sin
    /// arrancar el editor.
    ///
    /// Reglas de la división (todas con test):
    ///  - 1 vista: pantalla completa. 2: media pantalla cada una (columnas).
    ///    3–4: cuadrícula 2×2. >4: se RECHAZA — 4 procesos del CLI ya son el
    ///    coste asumido (criterio de cierre del plan §2bis) y un mosaico 3×3
    ///    de vistas de 640 px deja tableros ilegibles.
    ///  - Los rects dejan un borde de 0.005 entre vistas para que la
    ///    separación se lea (dos tableros de tierra pegados parecen uno roto).
    /// </summary>
    public sealed class MultiViewportModel
    {
        /// <summary>Máximo de vistas simultáneas: 4 procesos CLI + 4 streams y
        /// el mosaico 2×2 sigue legible. Más vistas: rechazo explícito.</summary>
        public const int MaxViews = 4;

        /// <summary>Borde normalizado entre vistas (en fracción de pantalla).</summary>
        public const float Gap = 0.005f;

        public sealed class View
        {
            /// <summary>Rect normalizado de la cámara (x, y, w, h — 0..1).</summary>
            public float X, Y, W, H;
            /// <summary>Índice de vista (0..N-1): nombra objetos y elige capa.</summary>
            public int Index;
            /// <summary>Etiqueta del HUD de la vista (nombre corto + origen).</summary>
            public string Label = "";
        }

        /// <summary>Número de columnas/filas de la cuadrícula para N vistas.</summary>
        public static void GridFor(int views, out int cols, out int rows)
        {
            switch (views)
            {
                case 1: cols = 1; rows = 1; break;
                case 2: cols = 2; rows = 1; break;   // dos tableros lado a lado
                case 3:
                case 4: cols = 2; rows = 2; break;   // cuadrícula completa
                default:
                    throw new ArgumentOutOfRangeException(nameof(views),
                        $"el multi-visor soporta 1..{MaxViews} vistas (pedidas: {views})");
            }
        }

        /// <summary>Layout completo de N vistas, con etiqueta. N fuera de rango
        /// ⇒ excepción (el bootstrapper nunca crea un mosaico ambiguo).</summary>
        public static IReadOnlyList<View> Layout(int views, IReadOnlyList<string>? labels = null)
        {
            if (views < 1) throw new ArgumentOutOfRangeException(nameof(views));
            GridFor(views, out int cols, out int rows);

            var list = new List<View>(views);
            float cw = 1f / cols, rh = 1f / rows;
            for (int i = 0; i < views; i++)
            {
                int col = i % cols, row = i / cols;
                // Unity: Y=0 abajo. La vista 0 arriba-izquierda (orden de lectura).
                float x = col * cw, y = 1f - (row + 1) * rh;
                // El borde: cada vista se encoge GAP/2 por lado (solo lados
                // INTERIORES con más de una vista; con 1 vista, pantalla completa).
                float x0 = cols > 1 ? x + (col > 0 ? Gap / 2f : 0f) : 0f;
                float x1 = cols > 1 ? x + cw - (col < cols - 1 ? Gap / 2f : 0f) : 1f;
                float y0 = rows > 1 ? y + (row < rows - 1 ? Gap / 2f : 0f) : 0f;
                float y1 = rows > 1 ? y + rh - (row > 0 ? Gap / 2f : 0f) : 1f;
                list.Add(new View
                {
                    X = x0, Y = y0,
                    W = x1 - x0, H = y1 - y0,
                    Index = i,
                    Label = LabelFor(i, labels)
                });
            }
            return list;
        }

        /// <summary>Etiqueta por defecto: «Vista N» + semilla/pool si se dan.</summary>
        public static string LabelFor(int index, IReadOnlyList<string>? labels)
        {
            if (labels != null && index < labels.Count && !string.IsNullOrEmpty(labels[index]))
                return labels[index];
            return "Vista " + (index + 1);
        }

        /// <summary>Texto del rótulo de una vista: label + detalle (semilla y
        /// pool), recortado a lo que cabe en una línea de HUD. Puro: testeable.</summary>
        public static string ViewCaption(string label, ulong seed, string? poolName)
        {
            var sb = new StringBuilder(label);
            sb.Append(" · seed ").Append(seed.ToString(CultureInfo.InvariantCulture));
            if (!string.IsNullOrEmpty(poolName))
            {
                sb.Append(" · ").Append(poolName);
            }
            return sb.ToString();
        }

        /// <summary>Capa de render de la vista i: las cámaras se reparten
        /// capas DISJUNTAS (Sim0..3, 8..11 en el proyecto) para que cada una
        /// dibuje SOLO su mundo. Puro: el ordinal ES el contrato con
        /// TagManager.asset (test en la suite).</summary>
        public static int RenderLayer(int viewIndex)
        {
            if (viewIndex < 0 || viewIndex >= MaxViews)
                throw new ArgumentOutOfRangeException(nameof(viewIndex));
            return 8 + viewIndex;
        }

        /// <summary>Máscara de capas que la cámara de la vista i debe ver: SU
        /// capa Sim + las globales (Default 1, UI 5 — el HUD se ve en todas).</summary>
        public static int CameraMask(int viewIndex)
        {
            return (1 << 0) | (1 << 5) | (1 << RenderLayer(viewIndex));
        }
    }
}

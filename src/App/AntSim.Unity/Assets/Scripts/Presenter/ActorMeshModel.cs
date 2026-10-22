using System;
using System.Collections.Generic;
using AntSim.Unity.Scripts.UI;

namespace AntSim.Unity.Scripts.Presenter
{
    /// <summary>
    /// MALLAS DE LOS ACTORES — modelo PURO: convierte las siluetas del juego
    /// (<see cref="GameSpriteModel"/>, polígonos convexos) en vértices e índices,
    /// sin tocar UnityEngine. El <c>Mesh</c> de Unity lo monta el editor con
    /// estos arrays; aquí está la parte que se puede verificar headless.
    ///
    /// Cómo: cada stamp se extruye en Z (la malla es un sólido plano) con caras
    /// delantera y trasera y las bandas laterales. Al ser CONVEXO cada stamp, el
    /// abanico de triángulos es siempre válido — no hace falta triangulación de
    /// polígonos generales ni detectar casos degenerados. Los stamps se solapan
    /// (la hormiga es tres lóbulos que se pisan): al ser un material OPACO y sin
    /// transparencia, el solape no se ve, y a cambio no hay que calcular ninguna
    /// unión booleana.
    ///
    /// La caja es la de <see cref="GameSpriteModel"/>: x ∈ [−0,5, 0,5], y ∈ [−1, 1],
    /// z ∈ [−0,5, 0,5]. Es EXACTAMENTE la caja de las primitivas que sustituyen
    /// (cápsula 2 u × 1 u y esfera 1 u), así que la rotación y la escala que ya
    /// aplica el presenter siguen valiendo sin tocarlas.
    /// </summary>
    public static class ActorMeshModel
    {
        public sealed class Data
        {
            public readonly float[] Vertices;   // x,y,z por vértice
            public readonly int[] Triangles;    // índices, 3 por triángulo
            public readonly int StampCount;

            public Data(float[] vertices, int[] triangles, int stampCount)
            {
                Vertices = vertices;
                Triangles = triangles;
                StampCount = stampCount;
            }

            public int VertexCount => Vertices.Length / 3;
            public int TriangleCount => Triangles.Length / 3;
        }

        /// <summary>Extruye las siluetas. <paramref name="halfThickness"/> es la
        /// mitad del grosor en Z (0,5 = la caja del presenter).</summary>
        public static Data Build(IReadOnlyList<Stamp> stamps, float halfThickness = GameSpriteModel.HalfThickness)
        {
            var verts = new List<float>();
            var tris = new List<int>();
            foreach (var stamp in stamps) Append(verts, tris, stamp, halfThickness);
            return new Data(verts.ToArray(), tris.ToArray(), stamps.Count);
        }

        private static void Append(List<float> verts, List<int> tris, Stamp stamp, float halfThickness)
        {
            int n = stamp.P.Length;
            int baseIndex = verts.Count / 3;

            // — Vértices: primero la cara delantera (+Z), luego la trasera (−Z) —
            for (int layer = 0; layer < 2; layer++)
            {
                float z = layer == 0 ? halfThickness : -halfThickness;
                for (int i = 0; i < n; i++)
                {
                    verts.Add(stamp.P[i].X);
                    verts.Add(stamp.P[i].Y);
                    verts.Add(z);
                }
            }

            // — Caras: abanico (convexo ⇒ válido). La trasera va al revés para
            //   que su normal mire hacia −Z, como el formato espera. —
            for (int i = 1; i + 1 < n; i++)
            {
                tris.Add(baseIndex);
                tris.Add(baseIndex + i);
                tris.Add(baseIndex + i + 1);

                tris.Add(baseIndex + n);
                tris.Add(baseIndex + n + i + 1);
                tris.Add(baseIndex + n + i);
            }

            // — Bandas laterales: un cuadrilátero por arista del contorno —
            for (int i = 0; i < n; i++)
            {
                int a = baseIndex + i;
                int b = baseIndex + (i + 1) % n;
                int aBack = a + n;
                int bBack = b + n;
                tris.Add(a); tris.Add(aBack); tris.Add(bBack);
                tris.Add(a); tris.Add(bBack); tris.Add(b);
            }
        }
    }
}

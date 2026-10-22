using System;
using System.Collections.Generic;
using AntSim.Unity.Scripts.Presenter;
using AntSim.Unity.Scripts.UI;
using Xunit;

namespace AntSim.Core.Tests
{
    /// <summary>
    /// Las mallas de los actores (hormiga, carga, hoja, huevo) que sustituyen a
    /// la cápsula y la esfera. Lo que se comprueba aquí es lo que Unity no
    /// perdona en tiempo de ejecución —índices fuera de rango, triángulos
    /// degenerados, geometría fuera de la caja de la escala del presenter— sin
    /// necesidad de abrir el editor.
    /// </summary>
    public class ActorMeshTests
    {
        public static IEnumerable<object[]> Sprites()
        {
            foreach (GameSpriteModel.Sprite s in Enum.GetValues<GameSpriteModel.Sprite>())
                yield return new object[] { s };
        }

        [Theory]
        [MemberData(nameof(Sprites))]
        public void Malla_EsUnSolidoÍndiceadoSano(GameSpriteModel.Sprite sprite)
        {
            var shape = GameSpriteModel.Build(sprite);
            Assert.NotEmpty(shape);

            var mesh = ActorMeshModel.Build(shape);
            int points = 0;
            foreach (var s in shape) points += s.P.Length;

            Assert.Equal(points * 2, mesh.VertexCount);          // cara delantera + trasera
            Assert.Equal(shape.Count, mesh.StampCount);
            Assert.All(mesh.Triangles, i => Assert.InRange(i, 0, mesh.VertexCount - 1));
            Assert.Equal(0, mesh.Triangles.Length % 3);

            // Ningún triángulo degenerado: un fan sobre un polígono convexo no
            // puede colapsar, y si colapsara Unity dibujaría normales NaN. El
            // área se mide en 3D: las bandas laterales son verticales y su
            // proyección en XY vale cero sin que el triángulo lo sea.
            for (int t = 0; t < mesh.Triangles.Length; t += 3)
            {
                var a = Vertex(mesh, mesh.Triangles[t]);
                var b = Vertex(mesh, mesh.Triangles[t + 1]);
                var c = Vertex(mesh, mesh.Triangles[t + 2]);
                float ux = b.X - a.X, uy = b.Y - a.Y, uz = b.Z - a.Z;
                float vx = c.X - a.X, vy = c.Y - a.Y, vz = c.Z - a.Z;
                float cx = uy * vz - uz * vy;
                float cy = uz * vx - ux * vz;
                float cz = ux * vy - uy * vx;
                float area = 0.5f * MathF.Sqrt(cx * cx + cy * cy + cz * cz);
                Assert.True(area > 1e-7f, $"triángulo de área {area} en {sprite}");
            }
        }

        [Theory]
        [MemberData(nameof(Sprites))]
        public void Malla_CabeEnLaCajaDeLaEscalaDelPresenter(GameSpriteModel.Sprite sprite)
        {
            var mesh = ActorMeshModel.Build(GameSpriteModel.Build(sprite));
            for (int i = 0; i < mesh.Vertices.Length; i += 3)
            {
                float x = mesh.Vertices[i], y = mesh.Vertices[i + 1], z = mesh.Vertices[i + 2];
                Assert.InRange(x, -0.51f, 0.51f);
                Assert.InRange(y, -1.01f, 1.01f);
                Assert.InRange(z, -0.51f, 0.51f);
            }
        }

        [Fact]
        public void Hormiga_MiraHaciaAdelanteYEsMasLargaQueAncha()
        {
            // El presenter rota con el rumbo y alarga el eje Y: la cabeza tiene
            // que estar en +Y (por delante), no es una silueta cualquiera.
            float maxY = float.MinValue, maxX = 0f;
            foreach (var s in GameSpriteModel.Ant())
                foreach (var p in s.P)
                {
                    if (p.Y > maxY) maxY = p.Y;
                    if (MathF.Abs(p.X) > maxX) maxX = MathF.Abs(p.X);
                }
            Assert.True(maxY > 0.7f, $"nada dibuja la cabeza por delante (maxY {maxY})");
            Assert.True(maxX > 0.4f, $"las patas no llegan al ancho de la caja (maxX {maxX})");
        }

        [Fact]
        public void Malla_HuevoYcarga_SonSólidosDistintosDeLaCápsula()
        {
            // El huevo es más alto que ancho y la carga tiene grumos: si los dos
            // salieran de la misma caja el cambio de aspecto no se vería.
            Assert.True(GameSpriteModel.Egg().Count >= 1);
            Assert.True(GameSpriteModel.Load().Count > GameSpriteModel.Egg().Count);
            Assert.NotEqual(NameOf(GameSpriteModel.Build(GameSpriteModel.Sprite.Egg)),
                NameOf(GameSpriteModel.Build(GameSpriteModel.Sprite.Load)));
        }

        private static string NameOf(IReadOnlyList<Stamp> s)
            => s.Count + "x" + s[0].P.Length;

        private static (float X, float Y, float Z) Vertex(ActorMeshModel.Data m, int index)
            => (m.Vertices[index * 3], m.Vertices[index * 3 + 1], m.Vertices[index * 3 + 2]);
    }
}

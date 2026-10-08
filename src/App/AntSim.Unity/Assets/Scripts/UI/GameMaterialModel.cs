using System;
using System.Collections.Generic;

namespace AntSim.Unity.Scripts.UI
{
    /// <summary>
    /// EL MODELO DE LOS MATERIALES Y LAS TEXTURAS DEL JUEGO: la paleta del mundo,
    /// la rejilla del suelo y la textura de reposo de las feromonas, en datos
    /// PUROS (sin UnityEngine) que la suite headless puede comprobar.
    ///
    /// POR QUÉ EXISTE. Los materiales y las texturas son ARTEFACTOS del
    /// bootstrapper: los `.mat` y los `.asset` que el repo versiona y con los que
    /// se PINTA el juego. El color, en cambio, vivía como literal dentro de los
    /// montadores —<c>SceneBootstrapper</c> y <c>MultiSimBootstrapper</c>—, que son
    /// código de EDITOR: no compilan en la suite, así que nada comprobaba que el
    /// color elegido fuese el color pintado (ni que el `.mat` en disco fuese el
    /// que el montador escribe hoy). Es el mismo agujero que cerraron
    /// <see cref="GameFontModel"/> para el `.ttf` y <see cref="GameSpriteModel"/>
    /// para las mallas, y se cierra igual: el dato vive aquí, los montadores lo
    /// APLICAN y `GeneratedMaterialContractTests` compara el artefacto (el `.mat`
    /// de disco y el `.asset` de la textura) contra el modelo.
    ///
    /// UNA SOLA FUENTE DE VERDAD, no dos copias. El mismo color lo usan los DOS
    /// montajes —escena simple y multi-visor—: antes estaba escrito dos veces
    /// (0.22/0.175/0.13 en el suelo de uno y en la mesa del otro, los dos acentos
    /// en ambos) con el riesgo evidente de que uno se moviera sin el otro. El
    /// multi-visor no tiene paleta propia: construye los MISMOS materiales con el
    /// sufijo de la vista (<see cref="Spec.ViewName"/>).
    ///
    /// LO QUE NO ESTÁ AQUÍ, a propósito: las medidas del mundo que no son color
    /// (radio del nido, altura de la capa de feromonas) y los colores de la UI
    /// que no son un material (el fondo de panel y la tinta del HUD, constantes
    /// del montador). El conjunto se eligió por lo que el CONTRATO puede
    /// comprobar: un `.mat` y una textura están en disco; un color de `Graphic`
    /// vive solo dentro de la escena guardada.
    /// </summary>
    public static class GameMaterialModel
    {
        /// <summary>Guía del shader de TODOS los materiales generados, medido en
        /// los `.mat` del repo: <c>Universal Render Pipeline/Unlit</c> (el que
        /// <c>Shader.Find</c> devuelve en este proyecto; la guía la fija la
        /// versión del paquete URP, por eso está clavada aquí y no resuelta por
        /// nombre — la suite corre sin editor y no puede preguntarle a Unity).
        ///
        /// Unlit es una DECISIÓN, no una comodidad: el color que se ve es el
        /// elegido —sin depender del ambiente ni de una luz— y la sonda visual
        /// puede medir píxeles contra él. Si algún día se sube URP y la guía
        /// cambia, el contrato lo dice con el número delante y el arreglo es
        /// regenerar los materiales y actualizar esta constante.</summary>
        public const string ShaderGuid = "650dd9526735d5b46b79224bc6e94025";

        // ── Valores de enum de Unity que el montador APLICA (y el contrato
        //    compara sin editor): aquí están escritos como números porque este
        //    fichero es puro; el montador sigue usando los nombres legibles
        //    (TextureFormat.RGBA32, TextureWrapMode.Repeat, FilterMode.Bilinear).
        /// <summary>TextureFormat.RGBA32</summary>
        public const int Rgba32Format = 4;
        /// <summary>TextureWrapMode.Repeat</summary>
        public const int WrapRepeat = 0;
        /// <summary>FilterMode.Bilinear</summary>
        public const int FilterBilinear = 1;

        /// <summary>Un color de material: los MISMOS cuatro flotantes que el
        /// montador le pasa a <c>new Color(...)</c>, tal cual (medido: Unity
        /// serializa `_BaseColor` con el valor autorado, sin convertirlo a
        /// espacio lineal).</summary>
        public readonly struct Rgba
        {
            public readonly float R, G, B, A;

            public Rgba(float r, float g, float b, float a = 1f)
            {
                R = r; G = g; B = b; A = a;
            }
        }

        /// <summary>El color de UN PÍXEL de una textura. Va en bytes y no en
        /// flotantes a propósito: `SetPixels32` recibe bytes, así que el píxel
        /// del artefacto es el byte escrito y no depende del redondeo de la
        /// conversión float→byte (0,23·255 = 58,65 ⇒ 59; si esa conversión
        /// cambiara, el suelo cambiaría de color sin que nadie lo tocara).</summary>
        public readonly struct Rgba32
        {
            public readonly byte R, G, B, A;

            public Rgba32(byte r, byte g, byte b, byte a = 255)
            {
                R = r; G = g; B = b; A = a;
            }
        }

        /// <summary>Qué clase de material genera el montador. No es una etiqueta
        /// decorativa: cada tipo tiene su receta (textura, transparencia,
        /// instancing) y el contrato la comprueba.</summary>
        public enum Kind
        {
            /// <summary>Color plano unlit. Con INSTANCING: el presenter dibuja los
            /// actores con <c>Graphics.DrawMeshInstanced</c>, que lanza si el
            /// material no lo tiene activado (una excepción por frame dejaba la
            /// escena sin hormigas, defecto destapado por el medidor de
            /// rendimiento).</summary>
            Flat,

            /// <summary>Suelo: unlit con la textura de rejilla del mundo y sus
            /// repeticiones.</summary>
            Grid,

            /// <summary>Capa de feromonas: TRANSPARENTE y con la textura de reposo
            /// (unlit-transparente SIN textura pinta blanco OPACO y taparía el
            /// tablero: el defecto del Play pass de F5.1).</summary>
            Transparent,
        }

        /// <summary>Un material del juego: nombre del `.mat`, clase, color y —para
        /// los nidos— su colonia.</summary>
        public sealed class Spec
        {
            /// <summary>Nombre del `.mat` en la escena simple (sin extensión).</summary>
            public readonly string Name;
            public readonly Kind MaterialKind;
            public readonly Rgba Color;

            /// <summary>Índice de colonia si el material es el acento de un nido
            /// (−1 = no lo es). Cambia el nombre de la variante de vista:
            /// `NestMat_V&lt;vista&gt;_&lt;colonia&gt;`.</summary>
            public readonly int Colony;

            /// <summary>¿El multi-visor construye también su variante por vista?
            /// (El marco del mundo es lo único que no: las vistas pequeñas no
            /// llevan canto.)</summary>
            public readonly bool InViews;

            public Spec(string name, Kind kind, Rgba color, bool inViews = true, int colony = -1)
            {
                Name = name;
                MaterialKind = kind;
                Color = color;
                InViews = inViews;
                Colony = colony;
            }

            /// <summary>El material se dibuja con INSTANCING. Es una consecuencia
            /// del tipo y no un ajuste suelto: el presenter dibuja los actores con
            /// <c>Graphics.DrawMeshInstanced</c>, que lanza si el material no lo
            /// tiene activado.</summary>
            public bool Instancing => MaterialKind == Kind.Flat;

            /// <summary>Nombre del `.mat` en el montaje de la vista <paramref name="view"/>.</summary>
            public string ViewName(int view)
                => Colony < 0 ? Name + "_V" + view : "NestMat_V" + view + "_" + Colony;

            /// <summary>El mismo material (color y clase) con el nombre de la
            /// vista: es lo que el multi-visor pide para no tener paleta propia.</summary>
            public Spec View(int view) => new Spec(ViewName(view), MaterialKind, Color, InViews, Colony);
        }

        /// <summary>Acento de la colonia 0 (barras del HUD y disco del nido: el
        /// mismo color en los dos sitios, que es justo lo que el código prometía
        /// en un comentario y ahora cumple por construcción).</summary>
        public static readonly Rgba Accent0 = new Rgba(0.93f, 0.45f, 0.35f);

        /// <summary>Acento de la colonia 1.</summary>
        public static readonly Rgba Accent1 = new Rgba(0.42f, 0.63f, 0.95f);

        /// <summary>Acento de una colonia: cicla los dos del contrato (con más
        /// colonias no hay un color por colonia, y el HUD solo tiene dos).</summary>
        public static Rgba Accent(int colony) => colony % 2 == 0 ? Accent0 : Accent1;

        /// <summary>El color plano de los materiales TEXTURIZADOS: el color lo
        /// pone la textura, y el tinte se queda en blanco (neutro: multiplicar
        /// por blanco no altera el píxel).</summary>
        private static readonly Rgba White = new Rgba(1f, 1f, 1f);

        /// <summary>Los materiales de la escena simple, que es el catálogo
        /// completo: el multi-visor deriva de aquí. El orden es el del montaje
        /// (suelo, mesa, marco, actores, ítems, nidos, feromonas).</summary>
        public static IReadOnlyList<Spec> Specs { get; } = new[]
        {
            // — Suelo y mesa —
            new Spec("FloorMat", Kind.Grid, White),
            new Spec("TableMat", Kind.Flat, new Rgba(0.22f, 0.175f, 0.13f)),
            // El marco del tablero: claro a propósito, para que el canto se lea
            // como límite y no como sombra. Es el único material sin variantes
            // por vista (las vistas del multi-visor no llevan marco).
            new Spec("RimMat", Kind.Flat, new Rgba(0.56f, 0.45f, 0.31f), inViews: false),

            // — Actores (hormiga, hormiga con carga) —
            new Spec("AntMat", Kind.Flat, new Rgba(0.35f, 0.16f, 0.10f)),
            new Spec("CarrierMat", Kind.Flat, new Rgba(0.98f, 0.72f, 0.16f)),
            // Diferenciados por colonia, armonizados con los acentos de nido:
            // colonia 0 terracota/ámbar, colonia 1 azul acero/celeste.
            new Spec("AntMatColony0", Kind.Flat, new Rgba(0.85f, 0.35f, 0.20f)),
            new Spec("CarrierMatColony0", Kind.Flat, new Rgba(0.98f, 0.70f, 0.20f)),
            new Spec("AntMatColony1", Kind.Flat, new Rgba(0.25f, 0.55f, 0.90f)),
            new Spec("CarrierMatColony1", Kind.Flat, new Rgba(0.40f, 0.85f, 0.95f)),

            // — Ítems: carga verde, hoja verde oscuro y sus muescas marrones —
            new Spec("ItemMat", Kind.Flat, new Rgba(0.36f, 0.78f, 0.36f)),
            new Spec("LeafMat", Kind.Flat, new Rgba(0.15f, 0.50f, 0.12f)),
            new Spec("BiteMat", Kind.Flat, new Rgba(0.30f, 0.15f, 0.05f)),

            // — Nidos: montículo + disco del color de la colonia —
            new Spec("NestMoundMat", Kind.Flat, new Rgba(0.30f, 0.25f, 0.20f)),
            new Spec("NestMat0", Kind.Flat, Accent0, colony: 0),
            new Spec("NestMat1", Kind.Flat, Accent1, colony: 1),

            // — Capa de feromonas (transparente) —
            new Spec("PheromoneMat", Kind.Transparent, White),
        };

        /// <summary>El material con ese nombre. LANZA si no existe: un nombre
        /// mal escrito en un montador tiene que doler al montar la escena, no
        /// pintar un material de otro color.</summary>
        public static Spec ByName(string name)
        {
            foreach (var s in Specs) if (s.Name == name) return s;
            var nombres = new List<string>(Specs.Count);
            foreach (var s in Specs) nombres.Add(s.Name);
            throw new ArgumentException(
                $"no hay ningún material llamado «{name}» en el modelo. Los que hay: {string.Join(", ", nombres)}",
                nameof(name));
        }

        /// <summary>El disco del nido de una colonia (mismo acento que su barra
        /// en el HUD).</summary>
        public static Spec Nest(int colony) => ByName("NestMat" + colony);

        // ── Texturas ───────────────────────────────────────────────────────

        /// <summary>Lado de la rejilla del suelo, en píxeles. Potencia de dos
        /// porque se repite: el filtrado no la necesita cuadrada, pero la
        /// repetición sí la quiere sin costuras.</summary>
        public const int GridSize = 64;

        /// <summary>Repeticiones de la rejilla sobre el suelo (una celda cada
        /// world/24 unidades: la referencia de ESCALA del jugador).</summary>
        public const float GridTiles = 24f;

        /// <summary>Relleno de la rejilla: «tierra clara, legible» — 0,42/0,33/0,23.</summary>
        public static readonly Rgba32 GridFill = new Rgba32(107, 84, 59);

        /// <summary>Línea de la rejilla: «surco de labor» — 0,34/0,26/0,18.</summary>
        public static readonly Rgba32 GridLine = new Rgba32(87, 66, 46);

        /// <summary>El único píxel de la textura de reposo: BLANCO y alpha 0 =
        /// invisible. Alpha 255 convertiría el reposo en un quad blanco opaco.</summary>
        public static readonly Rgba32 ReposePixel = new Rgba32(255, 255, 255, 0);

        /// <summary>¿El píxel (x, y) de la rejilla es línea? Las dos filas del
        /// borde superior e izquierdo: la retícula se repite por el mundo y las
        /// líneas de las celdas contiguas coinciden en ese borde.</summary>
        public static bool IsGridLine(int x, int y) => x == 0 || y == 0;

        /// <summary>Nombre del `.asset` de la rejilla: uno para la escena simple
        /// y uno por vista en el multi-visor (cada vista pinta su suelo y no
        /// comparte textura).</summary>
        public static string GridTextureName(int view) => view < 0 ? "GridTex" : "FloorMat_V" + view + "_Tex";

        /// <summary>Nombre del `.asset` de la textura de reposo de las feromonas.</summary>
        public static string ReposeTextureName(int view) => view < 0 ? "PheromoneEmpty" : "PheromoneMat_V" + view + "_Empty";
    }
}

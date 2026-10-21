#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using AntSim.Unity.Scripts.Presenter;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Probe: ¿el EJE de la silueta de la hormiga cae sobre su RUMBO, medido en PÍXELES?
///
/// POR QUÉ. `ProbeActorOrientation` cierra media pregunta: mide con las matrices del
/// propio presenter que el morro de la MALLA apunte al rumbo. Queda fuera todo lo que
/// pasa DESPUÉS de la matriz —el encuadre de la cámara (¿mira al nadir? ¿su arriba es
/// el norte de pantalla?), la proyección ortográfica, el rasterizado y el aliasing—,
/// que es justo donde se pierde un giro: la hormiga que se dibujaba «de lado» tenía
/// el morro a 90° del rumbo y ninguna medida de estado lo veía.
///
/// CÓMO. Renderiza la MALLA REAL y el MATERIAL REAL del juego (los del presenter de la
/// escena abierta) con un rumbo FIJO, en una cámara de usar y tirar que copia la del
/// juego (misma posición, misma rotación, mismo `orthographicSize`) y con la MISMA
/// matriz que usa el dibujo (`ActorRotation` + `ActorBoxScale`), y mide el eje de la
/// silueta en los píxeles:
///
///   · la silueta es la DIFERENCIA entre el frame con la hormiga y el mismo frame sin
///     ella: lo que la hormiga AÑADE a la imagen, sin clasificar colores a mano (el
///     material es UNLIT —medido: el píxel sale del color declarado—, pero apoyarse en
///     eso haría que un material con luz midiera otra cosa);
///   · la cámara sólo ve la capa de la hormiga, así que el resto de la escena no entra
///     en el render y el fondo es plano (lo que cambia entre los dos frames es ella);
///   · el eje sale del segundo momento (PCA) de esos píxeles, y el MORRO del reparto
///     de área a lo largo del eje: el gáster pesa más que la cabeza, así que el
///     extremo LIGERO es el morro. Ese reparto se COMPRUEBA a zoom, que es donde
///     tiene margen: medido a la escala de la partida deja **1 px** de diferencia en
///     los rumbos diagonales (16 px contra 15 px), y un dato así no decide — ahí se
///     publica y no se usa;
///   · se mide a DOS distancias: a la escala de la partida (unos 19 px de hormiga, el
///     tamaño al que la ve el jugador, donde manda el aliasing) y 6× más cerca, donde
///     el ángulo se mide fino. El eje NO depende del zoom —la cámara es ortográfica—
///     pero el ruido de píxel sí, y las dos filas lo enseñan.
///
/// La referencia es el rumbo del Core tal y como tiene que verse: el mundo mapea `X→X`
/// e `Y→Z` y la cámara mira al nadir con su arriba en el +Z del mundo, así que el rumbo
/// θ tiene que salir en la imagen como `(cos θ, sen θ)` con la Y hacia ARRIBA (la fila
/// 0 de `ReadPixels` es la de abajo).
///
/// Y comprueba que la comprobación TIENE DIENTES: el mismo render con el giro que se
/// coló (`-rumbo` en vez de `90 − rumbo`, el defecto real medido el 2026-10-10) tiene
/// que FALLAR en el eje y en el morro a las dos escalas; si pasara, las aserciones no
/// estarían mirando el render.
///
/// Devuelve `ok|resumen | filas` o `error|…`, y deja en `Temp/` la evidencia que se
/// MIRA (`antsim-heading-pixels.png`): ocho hormigas, una por rumbo (arriba a la escala
/// de la partida, abajo a zoom), con el rumbo ESPERADO pintado encima en magenta y el
/// morro MEDIDO marcado en cian. NO vive bajo Assets/: es herramienta del repo.
/// </summary>
public static class ProbeAntHeadingPixels
{
    // ── Parámetros de la medida ──────────────────────────────────────────────
    /// <summary>Lado del recorte cuadrado que se renderiza. Con el `orthographicSize`
    /// de la escena (399,36 u para un mundo de 768), este lado da la MISMA escala de
    /// píxeles que un viewport de 1080 de alto: la hormiga sale a los ~19 px a los que
    /// la ve el jugador. El recorte no cambia la escala, sólo el encuadre.</summary>
    private const int Canvas = 1080;

    /// <summary>Ventana que se lee alrededor del centro de la imagen (la hormiga va
    /// centrada en la cámara). Se lee sólo la ventana, no el frame entero: el resto
    /// es fondo plano y leerlo costaría 250× más.</summary>
    private const int Window = 192;

    /// <summary>Cuántas veces más cerca se repite la medida. La cámara es ortográfica,
    /// así que el eje de la imagen no cambia; lo que cambia es el aliasing.</summary>
    private const int Zoom = 6;

    /// <summary>Capa propia para la hormiga del render: la cámara sólo la ve a ella, de
    /// modo que el fondo es el despeje y nada de la escena puede taparla ni colarse en
    /// la diferencia (31 es una capa sin nombre en el proyecto: no hay que tocar el
    /// TagManager para medir).</summary>
    private const int Layer = 31;

    /// <summary>Diferencia mínima entre los dos frames para contar un píxel como de la
    /// hormiga (de 255). El canto tiene antialias y aporta diferencias pequeñas, que
    /// son justo las que codifican la forma: el umbral es bajo a propósito.</summary>
    private const int DifMin = 8;

    /// <summary>Tolerancia del eje a zoom, en grados: aquí la medida es fina y el
    /// error es de redondeo de píxel.</summary>
    private const float EjeTolZoom = 5f;

    /// <summary>Tolerancia del eje a la escala de la partida: la hormiga mide ~19 px y
    /// el aliasing mete ruido de varios grados, así que la tolerancia es la que separa
    /// «ruido» de «defecto» (el que se coló era de 90°).</summary>
    private const float EjeTolPartida = 20f;

    /// <summary>Tolerancia del morro: la pregunta es el SIGNO (el reparto de área de
    /// los dos extremos decide cuál es la cabeza), no los grados. Se comprueba sólo a
    /// zoom: a la escala de la partida el reparto deja **1 px** de margen en los rumbos
    /// diagonales (medido: 16 px contra 15 px con la hormiga a 19 px), y un dato así no
    /// decide nada — ahí el resultado se publica como informativo.</summary>
    private const float MorroTol = 30f;

    /// <summary>Rumbos de prueba, en grados: los ocho (<see cref="ProbeActorOrientation"/>
    /// usa los mismos) para que un cruce de ejes —que acertaría en los cardinales— no
    /// pase por bueno.</summary>
    private static readonly float[] Rumbos = { 0f, 45f, 90f, 135f, 180f, 225f, 270f, 315f };

    /// <summary>Rumbos del CONTRASTE (el giro que se coló): cuatro basta para exigir
    /// que el render lo vea.</summary>
    private static readonly float[] RumbosContraste = { 0f, 90f, 180f, 270f };

    private const int Lado = 160;   // lado de cada tesela de la evidencia en PNG

    public static string Run()
    {
        // ── El terreno: la cámara del juego y las piezas con las que dibuja ──
        var juego = Camera.main;
        if (juego == null) return "error|no hay Camera.main: la escena de juego tiene que estar abierta en el editor";
        if (!juego.orthographic)
            return "error|la cámara del juego no es ortográfica: la medida de píxeles supone la vista cenital de la escena";
        float nadir = Vector3.Angle(juego.transform.forward, Vector3.down);
        float norte = Vector3.Angle(juego.transform.up, Vector3.forward);
        if (nadir > 1f || norte > 1f)
            return "error|la cámara del juego no es la cenital de la escena (mira a " + F(nadir)
                 + "° del nadir y su arriba queda a " + F(norte)
                 + "° del norte de pantalla): así el eje de la silueta no se puede atribuir al rumbo";

        var presenter = UnityEngine.Object.FindAnyObjectByType<SimPresenterBehaviour>();
        if (presenter == null) return "error|no hay SimPresenterBehaviour en la escena abierta";
        var mesh = presenter.AntMesh;
        if (mesh == null) return "error|el presenter no tiene AntMesh: no hay malla que renderizar";
        var material = presenter.AntMaterial;
        if (presenter.ColonyAntMaterials != null && presenter.ColonyAntMaterials.Length > 0
            && presenter.ColonyAntMaterials[0] != null) material = presenter.ColonyAntMaterials[0];
        if (material == null) return "error|el presenter no tiene material de hormiga (ni de la colonia 0)";
        float largo = presenter.EffectiveAntScale;
        float mundo = presenter.WorldSide;
        if (largo <= 0f || mundo <= 0f)
            return "error|la escala del mundo o la de la hormiga están a cero: no hay píxeles que medir";

        float pxPorU = Canvas / (2f * juego.orthographicSize);
        float grosor = SimPresenterBehaviour.ActorBoxScale(largo).x;   // ancho de la caja, para el informe
        float elevar = presenter.ActorLift > 0f ? presenter.ActorLift : mundo * 0.0008f;
        var centro = new Vector3(juego.transform.position.x, elevar, juego.transform.position.z);

        // La escena del juego no es parte de la medida: la cámara y la hormiga del
        // render son objetos de usar y tirar (`HideAndDontSave`), y esto lo COMPRUEBA
        // en vez de prometerlo — un probe que ensucia la escena que el pase mide
        // después sería un falso verde con consecuencias.
        var escena = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        bool suciaAntes = escena.isDirty;

        var rig = Crear(mesh, material, juego, centro, elevar);
        var fallos = new List<string>();
        try
        {
            // La evidencia se pinta mientras se mide: una fila por escala, una tesela
            // por rumbo. La fila de arriba es la escala de la PARTIDA (donde la hormiga
            // mide ~19 px: se agranda ×3 para que el ojo la lea) y la de abajo el ZOOM.
            var mont = new Color32[Lado * (Rumbos.Length * 2) * Lado];   // ancho × alto
            int montW = Lado * Rumbos.Length;
            for (int i = 0; i < mont.Length; i++) mont[i] = new Color32(24, 24, 28, 255);

            float peorEjePartida = 0f, peorEjeZoom = 0f, peorMorroPartida = 0f, peorMorroZoom = 0f;
            float menorContraste = float.MaxValue;
            var filas = new List<string>();

            for (int escala = 0; escala < 2; escala++)
            {
                bool partida = escala == 0;
                float ortho = partida ? juego.orthographicSize : juego.orthographicSize / Zoom;
                string nombre = partida ? "PARTIDA" : "ZOOM" + Zoom;
                int agrandar = partida ? 3 : 1;   // sólo para la evidencia en PNG

                for (int i = 0; i < Rumbos.Length; i++)
                {
                    float h = Rumbos[i];
                    Color32[] on; byte[] mask;
                    var s = Medida(rig, SimPresenterBehaviour.ActorRotation(h * Mathf.Deg2Rad),
                                   largo, centro, ortho, out on, out mask);

                    if (s.Pixeles == 0)
                    {
                        fallos.Add(nombre + " h=" + F(h) + "°: la hormiga no aparece en el render (0 px)");
                        continue;
                    }

                    float errEje = ErrorEje(s.EjeDeg, h);
                    float errMorro = ErrorMod360(s.MorroDeg, h);
                    if (partida)
                    {
                        if (errEje > peorEjePartida) peorEjePartida = errEje;
                        if (errMorro > peorMorroPartida) peorMorroPartida = errMorro;
                    }
                    else
                    {
                        if (errEje > peorEjeZoom) peorEjeZoom = errEje;
                        if (errMorro > peorMorroZoom) peorMorroZoom = errMorro;
                    }

                    // El reparto de área de los dos extremos va en la fila porque es
                    // LO QUE DECIDE el morro: el extremo ligero es la cabeza.
                    string fila = nombre + " h=" + F(h) + "° eje=" + F(s.EjeDeg) + "° (desvía " + F(errEje)
                        + "°) morro=" + F(s.MorroDeg) + "° (desvía " + F(errMorro) + "°"
                        + (partida ? "; a esta escala no decide" : "") + ") px=" + s.Pixeles
                        + " l×a=" + F(s.Largo) + "×" + F(s.Ancho) + " px extremos="
                        + F(s.AreaFuerte) + "/" + F(s.AreaFloja) + " px";
                    filas.Add(fila);

                    // El EJE se comprueba a las dos escalas; el MORRO sólo a zoom, donde
                    // el reparto de área de los extremos tiene margen de sobra.
                    float tope = partida ? EjeTolPartida : EjeTolZoom;
                    bool morroDecide = !partida;
                    if (errEje > tope || (morroDecide && errMorro > MorroTol)) fallos.Add(fila);

                    // La fila de la PARTIDA va ARRIBA en la imagen: en la textura la
                    // fila 0 es la de ABAJO, así que la de partida ocupa la mitad alta.
                    Tesela(mont, montW, Lado * i, partida ? Lado : 0, on, s, h, agrandar);

                    // ── El CONTRASTE: el mismo render con el giro que se coló ──
                    if (i < RumbosContraste.Length && RumbosContraste[i] == h)
                    {
                        Color32[] onMal; byte[] maskMal;
                        var mal = Medida(rig, Quaternion.Euler(90f, -h, 0f), largo, centro, ortho, out onMal, out maskMal);
                        float errMalEje = ErrorEje(mal.EjeDeg, h);
                        float errMalMorro = ErrorMod360(mal.MorroDeg, h);
                        float menor = Mathf.Min(errMalEje, errMalMorro);
                        if (menor < menorContraste) menorContraste = menor;
                        filas.Add(nombre + " h=" + F(h) + "° con el giro viejo (-rumbo): eje=" + F(mal.EjeDeg)
                            + "° (desvía " + F(errMalEje) + "°) morro=" + F(mal.MorroDeg)
                            + "° (desvía " + F(errMalMorro) + "°) px=" + mal.Pixeles);
                        // Si el giro viejo PASARA lo que se comprueba a esta escala, este
                        // bloque sería decorativo: tiene que fallar como falla la puerta.
                        if (errMalEje <= tope && (!morroDecide || errMalMorro <= MorroTol))
                            fallos.Add("la comprobación NO tiene dientes: el giro viejo (-rumbo) pasa en " + nombre + " h=" + F(h) + "°");
                    }
                }
            }

            GuardarPng(mont, montW, Lado * 2);
            string escenaTxt = suciaAntes == escena.isDirty
                ? "escena \"" + escena.name + "\": " + (suciaAntes ? "ya sucia antes" : "sin tocar")
                : "ESCENA DEL JUEGO MARCADA COMO SUCIA";
            if (suciaAntes != escena.isDirty) fallos.Add("el render dejó la escena del juego sucia (" + escena.name + ")");

            string resumen = "el eje de la silueta cae sobre el rumbo en los 8 rumbos a las dos escalas"
                + " (desvío máximo " + F(peorEjePartida) + "° a escala de partida, tolerancia "
                + F(EjeTolPartida) + "°, y " + F(peorEjeZoom) + "° a zoom " + Zoom + "×, tolerancia "
                + F(EjeTolZoom) + "°) con el morro ≤ " + F(peorMorroZoom) + "° a zoom (tolerancia " + F(MorroTol)
                + "°; a 19 px el reparto de área de los extremos deja 1 px de margen y no decide, así que ahí se informa: ≤ "
                + F(peorMorroPartida) + "°)"
                + " y el giro viejo (-rumbo)"
                + " desvía ≥ " + F(menorContraste) + "° (la comprobación tiene dientes)"
                + " | evidencia: Temp/antsim-heading-pixels.png (magenta = rumbo esperado, cian = morro medido)"
                + " · hormiga de " + F(pxPorU * largo) + " px de largo en un lienzo de " + Canvas + " px"
                + " (el material " + material.name + ", la malla " + mesh.name + ", " + F(grosor) + " u de caja)"
                + " · " + escenaTxt
                + " | " + string.Join(" | ", filas.ToArray());

            // La evidencia se CONSERVA además en Temp/ (el pase la copia a artifacts/):
            // sin ella, «medido en píxeles» es una frase sin nada que mirar.
            if (fallos.Count > 0)
                return "error|la silueta NO sigue el rumbo en el render: " + string.Join(" · ", fallos.ToArray())
                     + " | " + string.Join(" | ", filas.ToArray());
            return "ok|" + resumen;
        }
        finally
        {
            rig.Dispose();
        }
    }

    // ── Lo medido sobre una silueta ─────────────────────────────────────────
    private struct Silueta
    {
        public int Pixeles;
        public float Cx, Cy;          // centroide, en píxeles de la ventana
        public float EjeDeg;          // ángulo del eje principal (imagen, Y hacia arriba)
        public float MorroDeg;        // ángulo del morro (el eje orientado por la asimetría)
        public float Largo, Ancho;    // extensión a lo largo y a lo ancho, en px
        public float TipX, TipY;      // punta del morro (px de la ventana)
        public float AreaFuerte, AreaFloja;   // reparto de área de los dos extremos
    }

    /// <summary>El render de usar y tirar: cámara y hormiga en una capa propia, más la
    /// RenderTexture y la textura de lectura. Todo se destruye en <see cref="Dispose"/>
    /// —nada de esto puede quedar en la escena del juego.</summary>
    private sealed class Rig : IDisposable
    {
        public Camera Cam = null!;
        public MeshRenderer Hormiga = null!;
        public Transform HormigaT = null!;
        public RenderTexture Rt = null!;
        public Texture2D Tex = null!;
        private GameObject _camGo = null!, _hormigaGo = null!;

        public Rig(GameObject camGo, Camera cam, GameObject hormigaGo, MeshRenderer hormiga, RenderTexture rt, Texture2D tex)
        {
            _camGo = camGo; Cam = cam; _hormigaGo = hormigaGo; Hormiga = hormiga;
            HormigaT = hormiga.transform; Rt = rt; Tex = tex;
        }

        public void Dispose()
        {
            if (Hormiga != null) Hormiga.enabled = true;
            if (_hormigaGo != null) UnityEngine.Object.DestroyImmediate(_hormigaGo);
            if (_camGo != null) UnityEngine.Object.DestroyImmediate(_camGo);
            if (Tex != null) UnityEngine.Object.DestroyImmediate(Tex);
            if (Rt != null) RenderTexture.ReleaseTemporary(Rt);
        }
    }

    private static Rig Crear(Mesh mesh, Material material, Camera juego, Vector3 centro, float elevar)
    {
        var rt = RenderTexture.GetTemporary(Canvas, Canvas, 24, RenderTextureFormat.ARGB32);
        rt.antiAliasing = 1;
        rt.filterMode = FilterMode.Point;

        var camGo = EditorUtility.CreateGameObjectWithHideFlags("SimHeadingPixelsCam",
            HideFlags.HideAndDontSave, typeof(Camera));
        var cam = camGo.GetComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = juego.orthographicSize;
        cam.nearClipPlane = juego.nearClipPlane;
        cam.farClipPlane = juego.farClipPlane;
        cam.clearFlags = CameraClearFlags.SolidColor;
        // El fondo se elige INVIRTIENDO el color del material: así el contraste entre la
        // hormiga y el fondo está garantizado —el enmascarado no depende de con qué
        // color se pintó la hormiga— y la evidencia se lee.
        var color = material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") : material.color;
        cam.backgroundColor = new Color(1f - color.r, 1f - color.g, 1f - color.b, 1f);
        cam.cullingMask = 1 << Layer;
        cam.allowHDR = false;
        cam.allowMSAA = false;
        cam.transform.position = juego.transform.position;
        cam.transform.rotation = juego.transform.rotation;
        cam.targetTexture = rt;

        var hormigaGo = EditorUtility.CreateGameObjectWithHideFlags("SimHeadingPixelsAnt",
            HideFlags.HideAndDontSave, typeof(MeshFilter), typeof(MeshRenderer));
        hormigaGo.layer = Layer;
        hormigaGo.GetComponent<MeshFilter>().sharedMesh = mesh;
        var mr = hormigaGo.GetComponent<MeshRenderer>();
        mr.sharedMaterial = material;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        hormigaGo.transform.position = centro;

        var tex = new Texture2D(Window, Window, TextureFormat.RGBA32, false);
        return new Rig(camGo, cam, hormigaGo, mr, rt, tex);
    }

    /// <summary>Pinta la hormiga con un giro y mide su silueta. Devuelve también el
    /// frame CON ella (para la evidencia) y la máscara.</summary>
    private static Silueta Medida(Rig rig, Quaternion rot, float largo, Vector3 centro, float orthoSize,
                                  out Color32[] on, out byte[] mask)
    {
        rig.Cam.orthographicSize = orthoSize;
        rig.HormigaT.position = centro;
        rig.HormigaT.rotation = rot;
        rig.HormigaT.localScale = SimPresenterBehaviour.ActorBoxScale(largo);

        rig.Hormiga.enabled = true;
        on = Leer(rig);
        rig.Hormiga.enabled = false;
        var off = Leer(rig);
        rig.Hormiga.enabled = true;

        return Medir(on, off, out mask);
    }

    /// <summary>Renderiza la cámara a su RenderTexture y devuelve la ventana central.</summary>
    private static Color32[] Leer(Rig rig)
    {
        rig.Cam.Render();
        var previo = RenderTexture.active;
        RenderTexture.active = rig.Rt;
        int o = (Canvas - Window) / 2;
        rig.Tex.ReadPixels(new Rect(o, o, Window, Window), 0, 0, false);
        rig.Tex.Apply(false);
        var px = rig.Tex.GetPixels32();
        RenderTexture.active = previo;
        return px;
    }

    /// <summary>El eje de la silueta (segundo momento) y el morro (el extremo LIGERO:
    /// el gáster pesa más que la cabeza). La fila 0 de la lectura es la de ABAJO, así
    /// que la Y de este análisis es la Y de la imagen y el ángulo se compara con el
    /// rumbo sin cambiar de marco.</summary>
    private static Silueta Medir(Color32[] on, Color32[] off, out byte[] mask)
    {
        mask = new byte[Window * Window];
        var s = new Silueta();
        double sx = 0, sy = 0;
        for (int y = 0; y < Window; y++)
            for (int x = 0; x < Window; x++)
            {
                int i = y * Window + x;
                int d = Mathf.Max(Mathf.Abs(on[i].r - off[i].r),
                        Mathf.Max(Mathf.Abs(on[i].g - off[i].g), Mathf.Abs(on[i].b - off[i].b)));
                if (d < DifMin) continue;
                mask[i] = 1;
                s.Pixeles++;
                sx += x; sy += y;
            }
        if (s.Pixeles == 0) return s;

        s.Cx = (float)(sx / s.Pixeles);
        s.Cy = (float)(sy / s.Pixeles);

        double xx = 0, yy = 0, xy = 0;
        for (int y = 0; y < Window; y++)
            for (int x = 0; x < Window; x++)
            {
                if (mask[y * Window + x] == 0) continue;
                double dx = x - s.Cx, dy = y - s.Cy;
                xx += dx * dx; yy += dy * dy; xy += dx * dy;
            }
        s.EjeDeg = Normaliza180((float)(0.5 * Math.Atan2(2 * xy, xx - yy) * Mathf.Rad2Deg));

        float ax = Mathf.Cos(s.EjeDeg * Mathf.Deg2Rad), ay = Mathf.Sin(s.EjeDeg * Mathf.Deg2Rad);
        float tMin = float.MaxValue, tMax = float.MinValue, uMax = 0f;
        for (int y = 0; y < Window; y++)
            for (int x = 0; x < Window; x++)
            {
                if (mask[y * Window + x] == 0) continue;
                float dx = x - s.Cx, dy = y - s.Cy;
                float t = dx * ax + dy * ay;
                float u = -dx * ay + dy * ax;
                if (t < tMin) tMin = t;
                if (t > tMax) tMax = t;
                if (Mathf.Abs(u) > uMax) uMax = Mathf.Abs(u);
            }
        s.Largo = tMax - tMin;
        s.Ancho = 2f * uMax;

        // El reparto de área por tercios: el gáster es el extremo PESADO y el morro el
        // ligero (la cabeza más las antenas ocupan bastante menos que el abdomen).
        float b0 = tMin + (tMax - tMin) / 3f, b2 = tMax - (tMax - tMin) / 3f;
        int n0 = 0, n2 = 0;
        for (int y = 0; y < Window; y++)
            for (int x = 0; x < Window; x++)
            {
                if (mask[y * Window + x] == 0) continue;
                float dx = x - s.Cx, dy = y - s.Cy;
                float t = dx * ax + dy * ay;
                if (t < b0) n0++;
                else if (t > b2) n2++;
            }
        s.AreaFuerte = n0; s.AreaFloja = n2;
        bool morroEsPositivo = n2 < n0;
        s.MorroDeg = Normaliza360(morroEsPositivo ? s.EjeDeg : s.EjeDeg + 180f);

        // La punta del morro: el píxel más lejano en la dirección del morro.
        float mx = Mathf.Cos(s.MorroDeg * Mathf.Deg2Rad), my = Mathf.Sin(s.MorroDeg * Mathf.Deg2Rad);
        float best = float.MinValue;
        for (int y = 0; y < Window; y++)
            for (int x = 0; x < Window; x++)
            {
                if (mask[y * Window + x] == 0) continue;
                float proj = (x - s.Cx) * mx + (y - s.Cy) * my;
                if (proj > best) { best = proj; s.TipX = x; s.TipY = y; }
            }
        return s;
    }

    // ── La evidencia que se mira ────────────────────────────────────────────
    /// <summary>Una tesela: la hormiga ampliada (vecino más próximo) con el rumbo
    /// ESPERADO pintado encima (magenta) y el morro MEDIDO (punto cian). Si las dos
    /// cosas coinciden, la verificación se ve; si no, se ve el fallo.</summary>
    private static void Tesela(Color32[] mont, int montW, int x0, int y0,
                               Color32[] on, Silueta s, float rumboDeg, int agrandar)
    {
        int src = Lado / agrandar;
        int sx0 = Mathf.RoundToInt(s.Cx) - src / 2;
        int sy0 = Mathf.RoundToInt(s.Cy) - src / 2;
        for (int ty = 0; ty < Lado; ty++)
            for (int tx = 0; tx < Lado; tx++)
            {
                int px = sx0 + tx / agrandar, py = sy0 + ty / agrandar;
                if (px < 0 || py < 0 || px >= Window || py >= Window) continue;
                Pinta(mont, montW, x0 + tx, y0 + ty, on[py * Window + px]);
            }

        float cx = Lado / 2f, cy = Lado / 2f;
        var magenta = new Color32(255, 0, 255, 255);
        var cian = new Color32(0, 255, 255, 255);
        float dx = Mathf.Cos(rumboDeg * Mathf.Deg2Rad), dy = Mathf.Sin(rumboDeg * Mathf.Deg2Rad);
        for (int i = -Lado; i <= Lado; i++)
            Pinta(mont, montW, x0 + Mathf.RoundToInt(cx + dx * i), y0 + Mathf.RoundToInt(cy + dy * i), magenta);

        int tipX = x0 + Mathf.RoundToInt(cx + (s.TipX - s.Cx) * agrandar);
        int tipY = y0 + Mathf.RoundToInt(cy + (s.TipY - s.Cy) * agrandar);
        for (int dy2 = -2; dy2 <= 2; dy2++)
            for (int dx2 = -2; dx2 <= 2; dx2++)
                if (dx2 * dx2 + dy2 * dy2 <= 5)
                    Pinta(mont, montW, tipX + dx2, tipY + dy2, cian);
    }

    private static void Pinta(Color32[] buf, int w, int x, int y, Color32 c)
    {
        if (x < 0 || y < 0 || x >= w || y >= buf.Length / w) return;
        buf[y * w + x] = c;
    }

    private static void GuardarPng(Color32[] px, int w, int h)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.SetPixels32(px);
        tex.Apply();
        try { File.WriteAllBytes(Ruta("antsim-heading-pixels.png"), tex.EncodeToPNG()); }
        catch (Exception) { /* la evidencia de texto ya va por stdout */ }
        UnityEngine.Object.DestroyImmediate(tex);
    }

    /// <summary>En `Temp/` del proyecto, como las demás evidencias de sonda (no es un
    /// asset: no se importa ni entra en el control de versiones).</summary>
    private static string Ruta(string file)
        => Path.Combine(Path.Combine(Application.dataPath, ".."), Path.Combine("Temp", file));

    // ── Cuentas de ángulos ─────────────────────────────────────────────────
    private static float Normaliza180(float deg)
    {
        deg %= 180f;
        if (deg < 0f) deg += 180f;
        return deg;
    }

    private static float Normaliza360(float deg)
    {
        deg %= 360f;
        if (deg < 0f) deg += 360f;
        return deg;
    }

    /// <summary>Desvío del eje: el eje es una RECTA, así que 179° y 1° distan 2°.</summary>
    private static float ErrorEje(float medido, float esperado)
    {
        float d = Mathf.Abs(Normaliza180(medido) - Normaliza180(esperado)) % 180f;
        return d > 90f ? 180f - d : d;
    }

    /// <summary>Desvío del morro: aquí SÍ hay sentido (el morro es una punta, no una
    /// recta), así que un giro de 180° se ve.</summary>
    private static float ErrorMod360(float medido, float esperado)
    {
        float d = Mathf.Abs(Normaliza360(medido) - Normaliza360(esperado)) % 360f;
        return d > 180f ? 360f - d : d;
    }

    private static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);
}
#endif

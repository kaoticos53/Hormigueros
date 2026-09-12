using System.Globalization;
using System.Text;
using AntSim.Unity.Scripts.Presenter;
using AntSim.Unity.Scripts.Streaming;
using UnityEngine;

/// <summary>
/// Sonda VISUAL del Play pass (F5.1) — mide PÍXELES de un frame real, no el
/// modelo. Existe porque el pass verificado hasta ahora comprobaba que el mundo
/// tenía hormigas en el ESTADO, no que se vieran: el quad de feromonas con
/// material por defecto (blanco opaco) tapaba el suelo y las hormigas, y todos
/// los invariantes seguían en verde. Aquí se cuenta lo que el jugador ve.
///
/// Tres fuentes:
///   · CAMERA — render de la cámara a una RenderTexture pequeña: el mundo sin
///     HUD (suelo, hormigas, ítems, feromonas).
///   · SCREEN — `ScreenCapture` del backbuffer: incluye el HUD.
///   · MOSAIC — el frame de pantalla reducido a una rejilla de CARACTERES: un
///     agente no puede mirar un PNG, así que el look se lee en texto (suelo
///     tierra, paneles, puntos de color del HUD). 48×16 celdas.
///
/// Salida (una línea, `clave=valor` separados por `|`, sin `|` en los valores):
///   world=WxH · floor=R:G:B · brown=0/1 · antPx=N · carrierPx=N
///   itemPx=N · darkFrac=F · ui=SEC · uiDark=R:G:B · textPx=N · mosaic=...
///
/// Lo consume scripts/playpass-live.sh. NO vive bajo Assets/: es herramienta del repo.
public static class ProbeVisualSample
{
    // 640×480 (no 320×240): a esa resolución una hormiga de 14 u ocupa 3-4 px y
    // el conteo se volvía insensible — «se ven hormigas» tiene que poder fallar.
    private const int RtWidth = 640;
    private const int RtHeight = 480;
    private const int MosaicW = 48;
    private const int MosaicH = 16;

    private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    public static string Run()
    {
        var sb = new StringBuilder("VIS");
        var parser = new StringBuilder();

        float floorR, floorG, floorB;
        int antPx, carrierPx, itemPx, totalPx;
        CameraRender(out floorR, out floorG, out floorB, out antPx, out carrierPx, out itemPx,
            out totalPx, parser);

        sb.Append("|world=").Append(RtWidth).Append('x').Append(RtHeight);
        sb.Append("|floor=").Append(F(floorR)).Append(':').Append(F(floorG)).Append(':').Append(F(floorB));
        // Terreno «tierra»: el rojo manda y el azul es bajo. Si el quad de
        // feromonas vuelve a tapar el suelo, esto se pone gris/blanco (r≈g≈b).
        bool brown = floorR > floorG + 0.02f && floorG > floorB + 0.02f && floorB < 0.45f;
        sb.Append("|brown=").Append(brown ? 1 : 0);
        sb.Append("|antPx=").Append(antPx);
        sb.Append("|carrierPx=").Append(carrierPx);
        sb.Append("|itemPx=").Append(itemPx);
        sb.Append("|totalPx=").Append(totalPx);

        ScreenStats(sb);
        sb.Append("|mosaic=").Append(ScreenMosaic());
        sb.Append('|').Append(parser.Length > 0 ? parser.ToString() : "detail=-");
        return sb.ToString();
    }

    /// <summary>Render de la cámara a una RT pequeña y estadísticas de color.</summary>
    private static void CameraRender(out float floorR, out float floorG, out float floorB,
        out int antPx, out int carrierPx, out int itemPx, out int totalPx, StringBuilder detail)
    {
        floorR = floorG = floorB = -1f;
        antPx = carrierPx = itemPx = totalPx = 0;

        var cam = Camera.main;
        if (cam == null) { detail.Append("cam=null"); return; }
        Color32 floorMed, gridMed, bgMed;

        var presenter = Object.FindAnyObjectByType<SimPresenterBehaviour>();
        var rt = RenderTexture.GetTemporary(RtWidth, RtHeight, 24);
        var prevTarget = cam.targetTexture;
        var prevActive = RenderTexture.active;
        try
        {
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(RtWidth, RtHeight, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, RtWidth, RtHeight), 0, 0);
            tex.Apply();
            var px = tex.GetPixels32();
            Object.Destroy(tex);

            // Color de las hormigas/portadores/ítems tal como los pinta el
            // presenter: se cuentan píxeles parecidos, con tolerancia.
            Color32 ant = MaterialColor(presenter != null ? presenter.AntMaterial : null);
            Color32 car = MaterialColor(presenter != null ? presenter.CarrierMaterial : null);
            Color32 item = MaterialColor(presenter != null ? presenter.ItemMaterial : null);
            detail.Append("antRGB=").Append(C(ant)).Append(";carRGB=").Append(C(car))
                  .Append(";itemRGB=").Append(C(item));

            // Paleta de referencia de la escena (medida, no supuesta): el suelo
            // sale de la propia mediana de abajo; el fondo, de la esquina.
            floorMed = default; gridMed = default; bgMed = default;
            bgMed = px[2 * RtWidth + 2];

            // Suelo = MEDIANA de 49 puntos repartidos DENTRO del mundo (0.2–0.8
            // del lado), no de una esquina: la cámara cubre más que el tablero
            // (el orthoSize lleva margen), así que la esquina mide el FONDO, no el
            // suelo — la primera versión de esta sonda daba brown=0 por eso, no
            // porque el suelo estuviera mal. La mediana aguanta que una muestra
            // caiga sobre una hormiga, un ítem o un nido.
            var samples = new System.Collections.Generic.List<Color32>(49);
            for (int gy = 1; gy <= 7; gy++)
                for (int gx = 1; gx <= 7; gx++)
                {
                    int sx = (int)(RtWidth * (0.15f + 0.7f * gx / 8f));
                    int sy = (int)(RtHeight * (0.15f + 0.7f * gy / 8f));
                    samples.Add(px[sy * RtWidth + sx]);
                }
            samples.Sort((a, b2) => (a.r + a.g + a.b).CompareTo(b2.r + b2.g + b2.b));
            var med = samples[samples.Count / 2];
            floorMed = med;
            // El surco de la rejilla es la muestra MÁS OSCURA del terreno (sin
            // salir del 10% inferior, que ya podría ser una hormiga o el fondo).
            gridMed = samples[samples.Count / 10];
            floorR = med.r / 255f; floorG = med.g / 255f; floorB = med.b / 255f;

            // Clasificación por COLOR MÁS CERCANO, no por tolerancia suelta: con el
            // suelo tierra (107,84,59) y la hormiga (76,46,26) distan ~40, así que
            // un `tol=40` contaba el tablero ENTERO como hormigas (antPx=47265 de
            // 76800 en la primera medida). El más cercano separa las dos familias
            // —que es justo lo que distingue «se ve el suelo» de «se ven hormigas»—
            // y solo se acepta si además está razonablemente cerca (no cualquier
            // pixel oscuro o brillante).
            float dark = 0f;
            for (int i = 0; i < px.Length; i++)
            {
                totalPx++;
                var best = Nearest(px[i], out int bestDist, ant, car, item, floorMed, gridMed, bgMed);
                // Umbral ESTRECHO (12): el suelo y el surco de la rejilla están a
                // ~20-35 del tono de hormiga, así que un umbral ancho volvía a
                // contar tablero como si fueran hormigas.
                if (bestDist <= 12)
                {
                    if (best == 0) antPx++;
                    else if (best == 1) carrierPx++;
                    else if (best == 2) itemPx++;
                }
                if (px[i].r < 40 && px[i].g < 40 && px[i].b < 40) dark++;
            }
            detail.Append(";darkFrac=").Append(F(dark / Mathf.Max(1, px.Length)));
        }
        finally
        {
            cam.targetTexture = prevTarget;
            RenderTexture.active = prevActive;
            RenderTexture.ReleaseTemporary(rt);
        }
    }

    /// <summary>Estadísticas del backbuffer (incluye HUD) si el editor lo permite.</summary>
    private static void ScreenStats(StringBuilder sb)
    {
        Texture2D? shot = CaptureScreen();
        if (shot == null) { sb.Append("|ui=none"); return; }
        var px = shot.GetPixels32();
        int w = shot.width, h = shot.height;
        // Banda superior izquierda y banda inferior derecha: donde vive el HUD.
        float r = 0, g = 0, b = 0; int n = 0; int text = 0;
        for (int y = h - 1; y >= h - 1 - (int)(h * 0.42f) && y >= 0; y -= 3)
            for (int x = 0; x < (int)(w * 0.34f); x += 3)
            {
                var c = px[y * w + x];
                r += c.r; g += c.g; b += c.b; n++;
                if (c.r > 150 && c.g > 150 && c.b > 150) text++;
            }
        sb.Append("|ui=SCREEN|uiWxH=").Append(w).Append('x').Append(h);
        sb.Append("|uiDark=").Append(F(r / Mathf.Max(1, n) / 255f)).Append(':')
          .Append(F(g / Mathf.Max(1, n) / 255f)).Append(':')
          .Append(F(b / Mathf.Max(1, n) / 255f));
        sb.Append("|textPx=").Append(text);
        Object.Destroy(shot);
    }

    /// <summary>
    /// El frame de pantalla como REJILLA DE CARACTERES (48×16). Un agente que
    /// lee stdout no puede mirar un PNG: esto convierte el look en algo
    /// inspeccionable. Cada celda es la MEDIA de su bloque, y el símbolo dice
    /// qué domina:
    ///   '#' panel oscuro · '.' blanco/brillante · ' ' medio · 'e' tierra ·
    ///   'a' hormiga · 'c' portadora · 'i' ítem · 'g' verde · 'r'/'b' acentos.
    /// </summary>
    private static string ScreenMosaic()
    {
        Texture2D? shot = CaptureScreen();
        if (shot == null) return "none";
        var px = shot.GetPixels32();
        int w = shot.width, h = shot.height;
        var sb = new StringBuilder();
        for (int gy = 0; gy < MosaicH; gy++)
        {
            if (gy > 0) sb.Append('/');
            for (int gx = 0; gx < MosaicW; gx++)
            {
                int x0 = gx * w / MosaicW, x1 = System.Math.Max(x0 + 1, (gx + 1) * w / MosaicW);
                int top = gy * h / MosaicH, bot = System.Math.Max(top + 1, (gy + 1) * h / MosaicH);
                // ReadPixels devuelve la fila 0 ABAJO: se invierte para que la
                // rejilla se lea como la pantalla (fila 0 = arriba).
                int y1 = h - top, y0 = System.Math.Max(0, h - bot);
                long r = 0, g = 0, b = 0; int n = 0; int bright = 0;
                for (int y = y0; y < y1; y += 2)
                    for (int x = x0; x < x1; x += 2)
                    {
                        var c = px[y * w + x];
                        r += c.r; g += c.g; b += c.b; n++;
                        if (c.r > 200 && c.g > 200 && c.b > 200) bright++;
                    }
                if (n == 0) { sb.Append('?'); continue; }
                float fr = r / n / 255f, fg = g / n / 255f, fb = b / n / 255f;
                float lum = 0.3f * fr + 0.6f * fg + 0.1f * fb;
                float brightFrac = (float)bright / n;
                char ch;
                if (lum < 0.16f) ch = '#';                                  // panel/hueco oscuro
                else if (brightFrac > 0.05f || (fr > 0.78f && fg > 0.78f && fb > 0.78f)) ch = '.'; // texto/blanco
                else if (fr > 0.45f && fr > fg * 1.25f && fg > fb) ch = 'e'; // tierra cálida
                else if (fr > 0.55f && fg > 0.45f && fb < 0.35f) ch = 'c';   // ámbar portadora
                else if (fg > fr + 0.05f && fg > fb + 0.05f) ch = 'g';       // verde (ítem)
                else if (fb > fr + 0.08f) ch = 'b';                          // azul
                else if (fr > fg + 0.08f) ch = 'r';                          // rojo/óxido
                else if (lum > 0.55f) ch = '-';
                else ch = ' ';
                sb.Append(ch);
            }
        }
        Object.Destroy(shot);
        return sb.ToString();
    }

    private static Texture2D? CaptureScreen()
    {
        try { return ScreenCapture.CaptureScreenshotAsTexture(); }
        catch (System.Exception) { return null; }
    }

    /// <summary>Índice del color de referencia MÁS cercano y su distancia.</summary>
    private static int Nearest(Color32 p, out int dist, params Color32[] palette)
    {
        int best = -1; dist = int.MaxValue;
        for (int i = 0; i < palette.Length; i++)
        {
            int dr = p.r - palette[i].r, dg = p.g - palette[i].g, db = p.b - palette[i].b;
            int d = dr * dr + dg * dg + db * db;
            if (d < dist) { dist = d; best = i; }
        }
        dist = (int)System.Math.Sqrt(dist / 3.0);
        return best;
    }

    private static string C(Color32 c) => c.r + "," + c.g + "," + c.b;

    private static Color32 MaterialColor(Material m)
        => m == null ? new Color32(0, 0, 0, 255) : (Color32)m.color;

    private static bool Near(Color32 a, Color32 b, int tol)
    {
        int dr = a.r - b.r, dg = a.g - b.g, db = a.b - b.b;
        if (dr < 0) dr = -dr;
        if (dg < 0) dg = -dg;
        if (db < 0) db = -db;
        return dr <= tol && dg <= tol && db <= tol;
    }
}

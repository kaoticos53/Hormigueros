using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using AntSim.Unity.Scripts.UI;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Sonda de LOS ICONOS EN EL HUD VIVO (F5.1). Existe porque la comparación de
/// los iconos a 14 px se hizo sobre una muestra del `.ttf` renderizada aparte:
/// eso prueba que el GLIFO es legible, no que la TARJETA lo pinte así. Aquí se
/// recorta del frame REAL de Play el rectángulo de cada tarjeta y se anota dónde
/// cae cada icono según el `TextGenerator` de uGUI:
///
///   · el recorte NATIVO de la tarjeta (PNG), que es la evidencia que se mira;
///   · la posición de cada icono dentro del recorte y el AVANCE que le reserva
///     el HUD (medido en píxeles de pantalla, con el `scaleFactor` del canvas
///     incluido: el canvas escala con la resolución, así que los 14 px del
///     diseño no son 14 px de pantalla en todas las ventanas);
///   · el tamaño de em EFECTIVO (`fontSize × scaleFactor`), que es contra el que
///     hay que comparar la referencia del `.ttf` — comparar contra 14 px con el
///     canvas al 98 % mediría una diferencia de escala, no de forma;
///   · y el tamaño del RUN de cada icono (`&lt;size=N&gt;`), porque los iconos de la
///     tarjeta NO van al tamaño del cuerpo: van al doble. Comparar el icono
///     contra el cuerpo daría una diferencia de escala haciéndose pasar por
///     diferencia de forma — exactamente el error que esta sonda existe para
///     evitar.
///
/// Los quads de `TextGenerator.verts` NO se usan: no hay 4 vértices por carácter
/// (medido: el primer icono de una tarjeta salía como un quad de 0 px de alto).
/// La posición horizontal sí es fiable — `UICharInfo.cursorPos + charWidth` es
/// la caja de avance del carácter — y es lo que se publica.
///
/// NO vive bajo Assets/: es herramienta del repo. Lo consume
/// scripts/playpass-live.sh (captura) y scripts/playpass-icons.py (comparación
/// contra la referencia a tamaño efectivo).
/// </summary>
public static class ProbeHudIconShot
{
    /// <summary>Cuántos caracteres del texto viven dentro de etiquetas `<…>`.
    /// Hace falta para decidir el mapeo a la lista del generador (ver `map=`).</summary>
    private static int TagChars(string s)
    {
        int n = 0;
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] != '<') continue;
            int close = s.IndexOf('>', i + 1);
            if (close <= i) continue;
            n += close - i + 1;
            i = close;
        }
        return n;
    }

    /// <summary>El tamaño del run al que ENTRA un carácter: `&lt;size=N&gt;` lo abre y
    /// `&lt;/size&gt;` lo devuelve al cuerpo del Text. Los tres iconos de la tarjeta van
    /// en un run al doble, así que sin esto la comparación se haría contra el
    /// tamaño equivocado.</summary>
    private static int RunSize(string tag, int current, int body)
    {
        if (tag.StartsWith("size=", StringComparison.Ordinal))
            return int.TryParse(tag.Substring(5), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int n) ? n : current;
        if (tag.StartsWith("/size", StringComparison.Ordinal)) return body;
        return current;
    }

    private static bool IsIcon(int cp) => cp == GameFontModel.AntGlyph
        || cp == GameFontModel.LoadGlyph || cp == GameFontModel.EggGlyph;

    private static string GlyphName(int cp) => cp == GameFontModel.AntGlyph ? "Ant"
        : cp == GameFontModel.LoadGlyph ? "Load" : cp == GameFontModel.EggGlyph ? "Egg" : "?";

    public static string Run()
    {
        var sb = new StringBuilder("HUDICONS");
        var frame = Capture();
        if (frame == null) return sb.Append("|error=sin-captura").ToString();
        int W = frame.width, H = frame.height;
        sb.Append("|frame=").Append(W).Append('x').Append(H);

        var px = frame.GetPixels32();

        int textsWithIcons = 0, icons = 0, card = 0;
        var rows = new List<string>();
        var texts = UnityEngine.Object.FindObjectsByType<Text>(FindObjectsInactive.Exclude);
        foreach (var t in texts)
        {
            if (t == null || string.IsNullOrEmpty(t.text)) continue;
            if (!HasIcon(t.text)) continue;
            textsWithIcons++;

            // El generador se REPOBLA a mano: el de la última reconstrucción del
            // canvas puede ser de un frame anterior, y aquí importa que la caja
            // de avance corresponda al texto que se acaba de leer.
            var settings = t.GetGenerationSettings(t.rectTransform.rect.size);
            t.cachedTextGenerator.Populate(t.text, settings);
            var chars = t.cachedTextGenerator.characters;

            float scale = t.canvas != null ? t.canvas.scaleFactor : 1f;
            // Tamaño del rectángulo en unidades del CANVAS (las que usa el
            // generador): el recorte da píxeles de pantalla, y sin las dos
            // medidas no se puede pasar de una a otra — el objeto puede estar
            // escalado por su padre y el rect NO mide lo que ocupa en pantalla.
            var rsize = t.rectTransform.rect.size;
            var corners = new Vector3[4];
            t.rectTransform.GetWorldCorners(corners);
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            var cam = t.canvas != null ? t.canvas.worldCamera : null;
            foreach (var c in corners)
            {
                // `GetWorldCorners` YA devuelve el espacio de mundo: aquí se
                // proyecta a pantalla, sin volver a aplicar el transform (el
                // doble transform dejaba el rect de la tarjeta degenerado y la
                // sonda decía «0 iconos» con el HUD lleno de ellos).
                var p = RectTransformUtility.WorldToScreenPoint(cam, c);
                if (p.x < minX) minX = p.x;
                if (p.y < minY) minY = p.y;
                if (p.x > maxX) maxX = p.x;
                if (p.y > maxY) maxY = p.y;
            }
            int cxs = Mathf.Clamp(Mathf.FloorToInt(minX), 0, W - 1);
            int cxe = Mathf.Clamp(Mathf.CeilToInt(maxX), 0, W - 1);
            int cys = Mathf.Clamp(Mathf.FloorToInt(minY), 0, H - 1);
            int cye = Mathf.Clamp(Mathf.CeilToInt(maxY), 0, H - 1);
            string name = "antsim-hud-card" + card++;
            bool hasCrop = cxe > cxs && cye > cys;
            // (el recorte se guarda aunque solo haya una tarjeta con rect: la
            // segunda puede tenerlo degenerado si su panel aún no se ha
            // dispuesto — pasa con el editor recién entrado en Play)
            if (hasCrop) SaveCrop(px, W, cxs, cys, cxe, cye, name + ".png");
            sb.Append("|scale=").Append(F(scale))
              .Append("|rsize=").Append(F(rsize.x)).Append('x').Append(F(rsize.y))
              .Append("|ref=1920x1080|font=").Append(t.fontSize)
              .Append("|em=").Append(F(t.fontSize * scale))
              .Append("|t=").Append(t.gameObject.name)
              .Append("|path=").Append(t.transform.parent != null ? t.transform.parent.name : "-")
              .Append("|rect=").Append(cxs).Append(',').Append(cys).Append(',').Append(cxe).Append(',').Append(cye)
              .Append("|crop=").Append(hasCrop ? (cxe - cxs + 1) + "x" + (cye - cys + 1) : "0x0")
              .Append("|file=").Append(hasCrop ? name + ".png" : "-")
              .Append("|chars=").Append(chars.Count)
              .Append("|len=").Append(t.text.Length)
              // ¿La fuente del HUD TIENE los iconos? Un glifo que falta se pinta
              // con una caja o con la fuente de reserva, y el avance lo delata:
              // 720 unidades para el icono frente a 500 de una letra. Sin este
              // dato, una tarjeta con «cajas» pasaría la mirada por buena.
              .Append("|font=").Append(t.font != null ? t.font.name : "-")
              .Append("|hasAnt=").Append(t.font != null && t.font.HasCharacter((char)GameFontModel.AntGlyph) ? 1 : 0)
              .Append("|hasLoad=").Append(t.font != null && t.font.HasCharacter((char)GameFontModel.LoadGlyph) ? 1 : 0)
              .Append("|hasEgg=").Append(t.font != null && t.font.HasCharacter((char)GameFontModel.EggGlyph) ? 1 : 0);
            if (!hasCrop) { sb.Append("|deg=1"); continue; }

            // Cada `UICharInfo` da el AVANCE que uGUI reserva al carácter, en
            // píxeles de pantalla. Se pasa a unidades de la em y se compara con lo
            // DECLARADO por la fuente, con dos avisos que costaron confusión:
            //
            //   · la normalización va contra el tamaño EFECTIVO DEL RUN
            //     (`runSize × scaleFactor`), no contra el cuerpo del `Text`: el
            //     icono de la tarjeta vive en un `<size=28>` y convertirlo con el
            //     cuerpo de 14 daba 1357 unidades —el doble— haciéndolo parecer un
            //     desajuste de la fuente;
            //   · el avance reservado NO es el declarado: Unity carga el glifo con
            //     el hinting de FreeType, que cae al píxel entero. Medido con la
            //     fuente cargada por el motor: el icono declara 0,72 em (720
            //     unidades, 20,16 px a 28 px) y el generador reserva 20 px; en el
            //     HUD en vivo con el canvas al 98,3 % —cara de 27 px— reserva
            //     19 px. Por eso se publica `ofDecl` (qué fracción del avance
            //     declarado reserva) en vez de dar por hecho que son iguales; la
            //     cuenta del ancho de las tarjetas vive en la suite headless
            //     (`HudLayoutTests.HintedAdvance`).
            string s = t.text;

            // ¿La lista de caracteres del generador INCLUYE las etiquetas de rich
            // text? No hay API que lo diga y las dos convenciones se han visto:
            // se deduce del recuento y se publica (`map=`), porque con el mapeo
            // equivocado se lee el avance de OTRO carácter y el resultado parece
            // plausible (500 unidades, el de una letra) sin ser el del icono.
            int tagChars = TagChars(s);
            int visibleCount = s.Length - tagChars;
            bool skipTags = chars.Count <= visibleCount + 1;
            string map = chars.Count >= s.Length ? "raw"
                : skipTags ? "tags-fuera" : "desconocido";

            int visible = 0;
            float advLetter = -1f, advIcon = -1f;
            int runSize = t.fontSize;   // el cuerpo: los `<size=N>` lo suben por tramos
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
                if (ch == '<')
                {
                    int close = s.IndexOf('>', i + 1);
                    if (close > i)
                    {
                        runSize = RunSize(s.Substring(i + 1, close - i - 1), runSize, t.fontSize);
                        i = close;
                        if (skipTags) continue;
                    }
                }
                int idx = skipTags ? visible : i;
                if (idx < chars.Count)
                {
                    float eff = runSize * scale;   // tamaño del run en pantalla
                    float units = eff > 0f
                        ? chars[idx].charWidth * GameFontModel.UnitsPerEm / eff : 0f;
                    if (IsIcon(ch))
                    {
                        icons++;
                        if (advIcon < 0f) advIcon = units;
                        // Posición CRUDA del cursor: el generador trabaja en el
                        // rectángulo del texto con el origen en su esquina
                        // superior izquierda y la Y hacia abajo, así que esta es
                        // la posición del carácter DENTRO del recorte.
                        var ci = chars[idx];
                        // `size` es el tamaño del run en px de diseño y `px` el
                        // efectivo en pantalla: es contra ESTE contra el que se
                        // renderiza la referencia del `.ttf`.
                        float declared = GameFontModel.AdvanceOf(ch) * eff / GameFontModel.UnitsPerEm;
                        rows.Add("icon=" + GlyphName(ch) + " size=" + runSize
                            + " px=" + F(eff)
                            + " units=" + F(units)
                            + " decl=" + F(declared)
                            + " ofDecl=" + F(declared > 0f ? ci.charWidth / declared : 0f)
                            + " raw=" + F(ci.cursorPos.x) + "," + F(ci.cursorPos.y)
                            + " w=" + F(ci.charWidth));
                    }
                    else if (advLetter < 0f && ch > ' ' && ch != '\n')
                    {
                        advLetter = units;
                    }
                }
                visible++;
            }
            sb.Append("|map=").Append(map)
              .Append("|advLetter=").Append(F(advLetter))
              .Append("|advIcon=").Append(F(advIcon))
              // El TEXTO de la tarjeta, con los saltos escapados: es lo que
              // permite comparar el recorte contra la referencia SIN deducir
              // coordenadas de uGUI — se renderiza esta misma cadena con la
              // fuente y se alinea con los píxeles reales, y así los iconos se
              // localizan solos.
              .Append("|txt=").Append(Escape(t.text))
              .Append('|').Append(string.Join(";", rows.ToArray()));
            rows.Clear();
        }

        sb.Append("|texts=").Append(textsWithIcons).Append("|icons=").Append(icons);
        if (textsWithIcons == 0)
            sb.Append("|detail=ninguna Text del HUD lleva iconos (¿escena sin HUD? ¿Play?)");
        // La pantalla ENTERA, con el mismo criterio de orientación que los
        // recortes: es la evidencia que se mira en el panel de preview.
        SaveCrop(px, W, 0, 0, W - 1, H - 1, "antsim-hud-frame.png");
        UnityEngine.Object.Destroy(frame);
        return sb.ToString();
    }

    private static bool HasIcon(string s)
    {
        foreach (var c in GameFontModel.Codepoints)
            if (IsIcon(c) && s.IndexOf(char.ConvertFromUtf32(c), StringComparison.Ordinal) >= 0) return true;
        return false;
    }

    /// <summary>Un punto del espacio del texto a píxeles de pantalla (origen
    /// abajo-izquierda, como `ScreenCapture`). Para el canvas Overlay la cámara
    /// es nula y el resultado ya sale en píxeles; el mismo camino vale si algún
    /// día el HUD se pinta con cámara.</summary>
    private static Vector2 ToScreen(Text t, Vector3 local)
    {
        var canvas = t.canvas;
        var world = t.rectTransform.TransformPoint(local);
        return RectTransformUtility.WorldToScreenPoint(canvas != null ? canvas.worldCamera : null, world);
    }

    private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>El texto en una línea: `|` separa campos y `\n` separa líneas,
    /// así que ambos se escapan.</summary>
    private static string Escape(string s) => s
        .Replace("\\", "\\\\").Replace("|", "\\p").Replace("\r", "").Replace("\n", "\\n");

    private static Texture2D Capture()
    {
        try { return ScreenCapture.CaptureScreenshotAsTexture(); }
        catch (Exception) { return null; }
    }

    private static string TempPath(string file)
        => Path.Combine(Path.Combine(Application.dataPath, ".."), Path.Combine("Temp", file));

    /// <summary>El recorte NATIVO de la tarjeta (y del frame completo, para
    /// poder mirar el HUD entero en el panel de preview).</summary>
    private static void SaveCrop(Color32[] px, int W, int x0, int y0, int x1, int y1, string file)
    {
        int w = x1 - x0 + 1, h = y1 - y0 + 1;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        var sub = new Color32[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                sub[y * w + x] = px[(y0 + y) * W + (x0 + x)];
        tex.SetPixels32(sub);
        tex.Apply();
        try { File.WriteAllBytes(TempPath(file), tex.EncodeToPNG()); }
        catch (Exception) { /* la evidencia de texto ya va por stdout */ }
        UnityEngine.Object.Destroy(tex);
    }
}

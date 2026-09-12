using System.Text;
using AntSim.Unity.Scripts.Presenter;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Diagnóstico VISUAL de la escena (F5.1): qué materiales/mallas tienen las
/// cosas del mundo y cómo está montado el HUD. Devuelve una línea por elemento,
/// `clave=valor`, para poder comparar antes/después de un cambio de aspecto sin
/// depender de una captura.
/// NO vive bajo Assets/: es herramienta del repo.
/// </summary>
public static class ProbeSceneInspect
{
    public static string Run()
    {
        var sb = new StringBuilder();
        var p = Object.FindAnyObjectByType<SimPresenterBehaviour>();
        if (p != null)
        {
            sb.AppendLine("presenter.antMesh=" + Name(p.AntMesh) + " antMat=" + Name(p.AntMaterial)
                + " carrierMat=" + Name(p.CarrierMaterial) + " itemMesh=" + Name(p.ItemMesh)
                + " itemMat=" + Name(p.ItemMaterial));
            sb.AppendLine("presenter.grid=" + p.Grid + " speed=" + p.Speed
                + " pheroEvery=" + p.PheroEvery + " antScale=" + p.AntScale);
            // Estado VIVO del presenter: distingue «la sim no corre» de «la sim
            // corre pero no se ve» — la pregunta que decide dónde mirar.
            var live = p.CurrentState;
            sb.AppendLine("presenter.live tick=" + (p.Presenter.CurrentTick != null
                ? p.Presenter.CurrentTick.Tick : 0)
                + " ants=" + (live != null ? live.Ants.Count : -1)
                + " items=" + (live != null ? live.Items.Count : -1));
        }
        else sb.AppendLine("presenter=AUSENTE");

        foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
        {
            var r = go.GetComponent<Renderer>();
            if (r == null) continue;
            if (!go.name.StartsWith("Floor") && !go.name.StartsWith("Nest")
                && !go.name.StartsWith("Pheromone") && !go.name.StartsWith("Wall")) continue;
            // `material.color` en un shader sin `_Color` (Unlit/Texture,
            // Unlit/Transparent) escribe un ERROR en la consola de Unity: se
            // consulta antes. El ruido en la consola importa — la consola es
            // parte del veredicto del Play pass.
            string col = "sin-_Color";
            if (r.sharedMaterial != null && r.sharedMaterial.HasProperty("_Color"))
                col = r.sharedMaterial.color.ToString();
            var mtex = r.sharedMaterial != null ? r.sharedMaterial.mainTexture : null;
            sb.AppendLine("world." + go.name + " mat=" + Name(r.sharedMaterial)
                + " shader=" + (r.sharedMaterial != null && r.sharedMaterial.shader != null
                    ? r.sharedMaterial.shader.name : "-")
                + " tex=" + (mtex == null ? "NULL" : mtex.name + "(" + mtex.width + "x" + mtex.height + ")")
                + " color=" + col
                + " active=" + (go.activeInHierarchy ? 1 : 0)
                + " pos=" + go.transform.position.ToString("0.#")
                + " scale=" + go.transform.localScale.ToString("0.#"));
        }

        var canvas = Object.FindAnyObjectByType<Canvas>();
        if (canvas == null) { sb.AppendLine("canvas=AUSENTE"); return sb.ToString(); }
        var scaler = canvas.GetComponent<CanvasScaler>();
        sb.AppendLine("canvas.mode=" + canvas.renderMode + " scaler=" + (scaler != null ? scaler.uiScaleMode.ToString() : "-")
            + " refRes=" + (scaler != null ? scaler.referenceResolution.ToString() : "-")
            + " match=" + (scaler != null ? scaler.matchWidthOrHeight.ToString("0.##") : "-"));

        Dump(canvas.transform, sb, 0);

        // Overflow de texto (F5.1): un HUD «feo» suele ser, literalmente, texto
        // que no cabe. Se mide con la métrica REAL de la fuente (preferredHeight)
        // contra el rect, en vez de a ojo. Los textos anclados a ambos lados
        // (offsetMin/offsetMax, tamaño derivado) se miden contra su rect efectivo.
        int over = 0;
        foreach (var t in Object.FindObjectsByType<Text>(FindObjectsSortMode.None))
        {
            if (!t.isActiveAndEnabled || string.IsNullOrEmpty(t.text)) continue;
            var r = t.rectTransform.rect;
            float prefW = t.preferredWidth, prefH = t.preferredHeight;
            bool bad = prefW > r.width + 1f || prefH > r.height + 1f;
            if (!bad) continue;
            over++;
            sb.AppendLine("hud.OVERFLOW " + t.name + " rect=" + r.width.ToString("0") + "x"
                + r.height.ToString("0") + " pref=" + prefW.ToString("0") + "x"
                + prefH.ToString("0") + " wrap=" + t.horizontalOverflow +
                " text=\"" + Tail(t.text) + "\"");
        }
        sb.AppendLine("hud.overflow=" + over);
        return sb.ToString();
    }

    private static void Dump(Transform t, StringBuilder sb, int depth)
    {
        if (depth > 4) return;
        foreach (Transform c in t)
        {
            string pad = new string(' ', depth * 2);
            var rt = c as RectTransform;
            var txt = c.GetComponent<Text>();
            var img = c.GetComponent<Image>();
            var btn = c.GetComponent<Button>();
            var sb2 = new StringBuilder("hud.").Append(pad).Append(c.name);
            if (rt != null)
                sb2.Append(" anchor=").Append(rt.anchorMin.ToString("0.##"))
                   .Append('>').Append(rt.anchorMax.ToString("0.##"))
                   .Append(" pos=").Append(rt.anchoredPosition.ToString("0.#"))
                   .Append(" size=").Append(rt.sizeDelta.ToString("0.#"));
            if (img != null) sb2.Append(" img=").Append(ColorStr(img.color))
                .Append(img.sprite != null ? "+sprite" : "+noSprite")
                .Append(img.raycastTarget ? " ray" : "");
            if (btn != null) sb2.Append(" BTN=").Append(btn.interactable ? "on" : "off");
            if (txt != null) sb2.Append(" text=").Append(ColorStr(txt.color))
                .Append(" fs=").Append(txt.fontSize)
                .Append(" align=").Append(txt.alignment)
                .Append(" font=").Append(txt.font != null ? txt.font.name : "-")
                .Append(" body=\"").Append(Tail(txt.text)).Append('"');
            sb.AppendLine(sb2.ToString());
            Dump(c, sb, depth + 1);
        }
    }

    private static string Tail(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = s.Replace('\n', '/').Replace('|', '/');
        return s.Length <= 60 ? s : s.Substring(0, 60) + "…";
    }

    private static string ColorStr(Color c)
        => c.r.ToString("0.##") + "," + c.g.ToString("0.##") + "," + c.b.ToString("0.##")
         + "," + c.a.ToString("0.##");

    private static string Name(Object o)
        => o == null ? "NULL" : o.name;
}

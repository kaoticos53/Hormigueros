using System.Globalization;
using System.Text;
using UnityEngine;

/// <summary>
/// Diagnóstico del «mundo blanco» (F5.1): desactiva UNO a uno los objetos del
/// mundo y mide el color del centro del tablero tras cada cambio. Identifica qué
/// superficie pinta de blanco sin depender de leer un PNG.
/// Salida: `objeto=color` por línea, más la textura de cada material del mundo.
/// NO vive bajo Assets/: es herramienta del repo.
/// </summary>
public static class ProbeWhiteDiag
{
    private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    public static string Run()
    {
        var sb = new StringBuilder();
        var cam = Camera.main;
        if (cam == null) return "cam=null";

        // Materiales y texturas del mundo: el principal sospechoso es un material
        // sin textura (unlit sin _MainTex = blanco opaco).
        foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
        {
            if (!go.name.StartsWith("Floor") && !go.name.StartsWith("Table")
                && !go.name.StartsWith("Pheromone") && !go.name.StartsWith("Nest")
                && !go.name.StartsWith("Frame")) continue;
            var r = go.GetComponent<Renderer>();
            if (r == null || r.sharedMaterial == null) continue;
            var m = r.sharedMaterial;
            var t = m.mainTexture;
            sb.Append("mat.").Append(go.name).Append(" shader=").Append(m.shader != null ? m.shader.name : "-")
              .Append(" tex=").Append(t == null ? "NULL" : t.name + "(" + t.width + "x" + t.height + ")")
              .Append(" color=").Append(m.HasProperty("_Color") ? ColorStr(m.color) : "sin-_Color")
              .AppendLine();
        }

        sb.Append("frame.centro=").AppendLine(F(Center(cam)));
        sb.Append("frame.esquina=").AppendLine(F(Corner(cam)));

        // Uno a uno: el objeto que deja de ser sospechoso al apagarse es EL culpable.
        string[] names = { "PheromoneTiles", "Table", "Floor", "Frame_N", "Frame_S", "Frame_E", "Frame_W" };
        foreach (var n in names)
        {
            var go = GameObject.Find(n);
            if (go == null) { sb.Append("apaga.").Append(n).AppendLine("=ausente"); continue; }
            bool was = go.activeSelf;
            go.SetActive(false);
            sb.Append("apaga.").Append(n).Append(" centro=").Append(F(Center(cam)))
              .Append(" esquina=").AppendLine(F(Corner(cam)));
            go.SetActive(was);
        }
        return sb.ToString();
    }

    /// <summary>Media del centro del tablero en el render de la cámara.</summary>
    private static float Center(Camera cam) => Patch(cam, 0.5f, 0.5f);

    /// <summary>Media de la esquina superior izquierda (fuera del tablero).</summary>
    private static float Corner(Camera cam) => Patch(cam, 0.1f, 0.9f);

    private static float Patch(Camera cam, float u, float v)
    {
        const int W = 64, H = 48;
        var rt = RenderTexture.GetTemporary(W, H, 16);
        var prevT = cam.targetTexture;
        var prevA = RenderTexture.active;
        try
        {
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            int x = (int)(u * (W - 1)), y = (int)(v * (H - 1));
            var c = tex.GetPixels32()[y * W + x];
            Object.Destroy(tex);
            return (0.3f * c.r + 0.6f * c.g + 0.1f * c.b) / 255f;
        }
        finally
        {
            cam.targetTexture = prevT;
            RenderTexture.active = prevA;
            RenderTexture.ReleaseTemporary(rt);
        }
    }

    private static string ColorStr(Color c)
        => F(c.r) + "," + F(c.g) + "," + F(c.b) + "," + F(c.a);
}

using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// 0.1.5: F-14 version of the AB-4 HUD extras (wing-sweep gauge with an F-14 planform). Dev-only, never shipped.
public static partial class F14Build
{
    const string HudSrc = "Assets/Blueprinter/_donotship/GameObject/FastBomber1_HUDExtras_PLACEHOLDER.prefab";
    const string HudDir = Mod + "/Hud";

    static Texture2D Readable(Sprite sp)
    {
        var src = sp.texture; var r = sp.textureRect;
        var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32);
        Graphics.Blit(src, rt); var prev = RenderTexture.active; RenderTexture.active = rt;
        var t = new Texture2D((int)r.width, (int)r.height, TextureFormat.RGBA32, false);
        t.ReadPixels(new Rect(r.x, src.height - r.y - r.height, r.width, r.height), 0, 0); t.Apply();
        RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt); return t;
    }

    // probe: export the gauge sprites + rect info to F14Bridge/hud/
    public static void DumpGauge()
    {
        string od = F14Bridge.Dir + "/hud/"; Directory.CreateDirectory(od);
        var root = PrefabUtility.LoadPrefabContents(HudSrc);
        var sb = new StringBuilder();
        try
        {
            var g = root.transform.Find("swingWingGauge");
            foreach (var img in g.GetComponentsInChildren<Image>(true))
            {
                var rt = img.rectTransform; var sp = img.sprite;
                sb.AppendLine(HPath(img.transform, g) + " sprite=" + (sp ? sp.name : "null") + " size=" + rt.sizeDelta + " pivot=" + rt.pivot + " anchors=" + rt.anchorMin + "/" + rt.anchorMax +
                    " lp=" + rt.localPosition + " rot=" + rt.localEulerAngles + " scale=" + rt.localScale + " type=" + img.type + " preserve=" + img.preserveAspect + " fill=" + img.fillMethod + "/" + img.fillAmount + " color=" + img.color +
                    (sp ? " rect=" + sp.rect + " spPivot=" + sp.pivot + " ppu=" + sp.pixelsPerUnit + " border=" + sp.border : ""));
                if (sp)
                {
                    try { var t = Readable(sp); File.WriteAllBytes(od + img.name + ".png", t.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(t); }
                    catch (Exception e) { sb.AppendLine("  png failed " + e.Message); }
                }
            }
            var wag = g.GetComponent("WingAngleGauge");
            var so = new SerializedObject(wag); var it = so.GetIterator();
            while (it.NextVisible(true)) if (it.depth <= 1) sb.AppendLine("  field " + it.propertyPath + " = " + (it.propertyType == SerializedPropertyType.Float ? it.floatValue.ToString() : it.propertyType == SerializedPropertyType.Boolean ? it.boolValue.ToString() : it.propertyType.ToString()));
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        File.WriteAllText(od + "gauge.txt", sb.ToString());
        L("gauge dumped");
    }

    static string HPath(Transform t, Transform root)
    {
        var s = t.name; while (t.parent != null && t != root) { t = t.parent; if (t == root) break; s = t.name + "/" + s; }
        return s;
    }

    // ---------------------------------------------------------------- F-14 sweep gauge
    // Gauge frame (scaler space of swingWingGauge): wing pivot stays where the AB-4 had it; F-14 planform drawn to scale around it.
    // The game sets wingPivot.localEulerAngles.z = displayed sweep (20..68), so the wing sprite is the left wing unswept to 0 deg.
    static readonly Vector2 GPiv = new Vector2(-22.3f, 4.7f);
    const float GS = 6.8f;      // gauge units per metre
    const int GPPU = 6;         // texture pixels per gauge unit
    static Vector2 G(Vector3 w) => GPiv + new Vector2((w.x - F14Data.WingPivot_L.x) * GS, (w.z - F14Data.WingPivot_L.z) * GS);

    static void RasterG(bool[] mask, int W, int H, Vector2 a, Vector2 b, Vector2 c)
    {
        int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x)))), x1 = Mathf.Min(W - 1, Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))));
        int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y)))), y1 = Mathf.Min(H - 1, Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))));
        float d = (b.y - c.y) * (a.x - c.x) + (c.x - b.x) * (a.y - c.y);
        if (Mathf.Abs(d) < 1e-6f) return;
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float px = x + 0.5f, py = y + 0.5f;
                float l1 = ((b.y - c.y) * (px - c.x) + (c.x - b.x) * (py - c.y)) / d, l2 = ((c.y - a.y) * (px - c.x) + (a.x - c.x) * (py - c.y)) / d, l3 = 1 - l1 - l2;
                if (l1 >= -0.02f && l2 >= -0.02f && l3 >= -0.02f) mask[y * W + x] = true;
            }
    }

    // pts: triangles already in gauge units; returns sprite + its rect (min corner, size) in gauge units
    static Sprite GaugeSprite(string name, System.Collections.Generic.List<Vector2[]> tris, float clipMaxX, out Rect rect)
    {
        float mnx = 1e9f, mny = 1e9f, mxx = -1e9f, mxy = -1e9f;
        foreach (var t in tris) foreach (var p in t) { mnx = Mathf.Min(mnx, p.x); mny = Mathf.Min(mny, p.y); mxx = Mathf.Max(mxx, Mathf.Min(p.x, clipMaxX)); mxy = Mathf.Max(mxy, p.y); }
        mnx -= 1f; mny -= 1f; mxx += 1f; mxy += 1f;
        int W = Mathf.CeilToInt((mxx - mnx) * GPPU), H = Mathf.CeilToInt((mxy - mny) * GPPU);
        rect = new Rect(mnx, mny, W / (float)GPPU, H / (float)GPPU);
        var mask = new bool[W * H];
        foreach (var t in tris) RasterG(mask, W, H, (t[0] - new Vector2(mnx, mny)) * GPPU, (t[1] - new Vector2(mnx, mny)) * GPPU, (t[2] - new Vector2(mnx, mny)) * GPPU);
        int cx = Mathf.FloorToInt((clipMaxX - mnx) * GPPU);
        for (int y = 0; y < H; y++) for (int x = Mathf.Max(0, cx); x < W; x++) mask[y * W + x] = false;
        var px = new Color32[W * H]; int n = 0;
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int i = y * W + x; if (!mask[i]) continue; n++;
                bool edge = false;
                for (int dy = -2; dy <= 2 && !edge; dy++)
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        if (dx * dx + dy * dy > 5) continue;
                        int xx = x + dx, yy = y + dy;
                        if (xx < 0 || yy < 0 || xx >= W || yy >= H || !mask[yy * W + xx]) { edge = true; break; }
                    }
                // the centreline cut is not an outline edge on the fuselage half
                if (edge && x >= cx - 3 && cx < W) { bool inner = y > 2 && y < H - 3 && mask[(y - 2) * W + x] && mask[(y + 2) * W + x]; if (inner) edge = false; }
                px[i] = edge ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 40);
            }
        if (!AssetDatabase.IsValidFolder(HudDir)) AssetDatabase.CreateFolder(Mod, "Hud");
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false); tex.SetPixels32(px); tex.Apply();
        string path = HudDir + "/F14B_gauge_" + name + ".png";
        File.WriteAllBytes(path, tex.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.textureType = TextureImporterType.Sprite; ti.spriteImportMode = SpriteImportMode.Single; ti.alphaIsTransparency = true;
        ti.mipmapEnabled = false; ti.textureCompression = TextureImporterCompression.Uncompressed; ti.npotScale = TextureImporterNPOTScale.None; ti.SaveAndReimport();
        L("gauge sprite " + name + " " + W + "x" + H + " px, " + n + " filled, rect " + rect);
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    static void AddTris(System.Collections.Generic.List<Vector2[]> list, string child, Func<Vector3, bool> keep, Func<Vector2, Vector2> map)
    {
        var (v, t) = WorldTris(child);
        if (v.Length == 0) { L("gauge: no mesh " + child); return; }
        for (int i = 0; i + 2 < t.Length; i += 3)
        {
            var a = v[t[i]]; var b = v[t[i + 1]]; var c = v[t[i + 2]];
            if (!keep((a + b + c) / 3f)) continue;
            list.Add(new[] { map(G(a)), map(G(b)), map(G(c)) });
        }
    }

    public static GameObject BuildHudExtras()
    {
        model = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
        float centre = G(Vector3.zero).x;
        var fus = new System.Collections.Generic.List<Vector2[]>();
        Func<Vector2, Vector2> id = p => p;
        foreach (var n in new[] { "F14_Body", "F14_Canopy", "F14_Stab_L" }) AddTris(fus, n, w => w.x <= 0.05f, id);
        var fs = GaugeSprite("fuselage", fus, centre, out var fr);
        // left wing (+flap, spoiler) unswept from the model's 20 deg to 0 deg about the pivot (gauge rotation is CCW-positive = tip aft)
        var wing = new System.Collections.Generic.List<Vector2[]>();
        var rot = Quaternion.Euler(0, 0, -20f);
        Func<Vector2, Vector2> unsweep = p => GPiv + (Vector2)(rot * (Vector3)(p - GPiv));
        foreach (var n in new[] { "F14_Wing_L", "F14_Slat_L", "F14_Flap_L", "F14_Spoiler_L" }) AddTris(wing, n, w => true, unsweep);
        var ws = GaugeSprite("wing", wing, 1e6f, out var wr);

        string dst = Mod + "/F14B_HUDExtras.prefab";
        var root = PrefabUtility.LoadPrefabContents(HudSrc);
        try
        {
            var g = root.transform.Find("swingWingGauge");
            var sc = g.Find("scaler");
            var fu = sc.Find("fuselage").GetComponent<Image>();
            var frt = fu.rectTransform; frt.pivot = new Vector2(0.5f, 0.5f); frt.localScale = Vector3.one; frt.localRotation = Quaternion.identity;
            frt.sizeDelta = fr.size; frt.localPosition = new Vector3(fr.center.x, fr.center.y, 0);
            fu.sprite = fs; fu.preserveAspect = false; fu.type = Image.Type.Simple;
            var po = sc.Find("pivotOffset"); po.localPosition = new Vector3(GPiv.x, GPiv.y, 0); po.localRotation = Quaternion.identity;
            var wp = po.Find("wingPivot");
            var wi = wp.Find("wingIcon").GetComponent<Image>();
            var wrt = wi.rectTransform; wrt.localScale = Vector3.one / wp.localScale.x; wrt.localRotation = Quaternion.identity;
            wrt.sizeDelta = wr.size; wrt.pivot = new Vector2((GPiv.x - wr.xMin) / wr.width, (GPiv.y - wr.yMin) / wr.height); wrt.localPosition = Vector3.zero;
            wi.sprite = ws; wi.preserveAspect = false; wi.type = Image.Type.Simple;
            // range arc: 20..68 deg
            var arc = sc.Find("tiltArc").GetComponent<Image>(); arc.fillAmount = 48f / 360f;
            arc.rectTransform.localPosition = new Vector3(GPiv.x, GPiv.y, 0); arc.rectTransform.localEulerAngles = new Vector3(0, 0, 20f);
            PrefabUtility.SaveAsPrefabAsset(root, dst);
            L("HUD extras: F-14 sweep gauge saved " + dst + " fuselage rect " + fr + " wing rect " + wr);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        return AssetDatabase.LoadAssetAtPath<GameObject>(dst);
    }

    // check: render the gauge at 20 / 45 / 68 deg into F14Bridge/hud/gauge_*.png
    public static void ShotGauge()
    {
        var pf = AssetDatabase.LoadAssetAtPath<GameObject>(Mod + "/F14B_HUDExtras.prefab");
        string od = F14Bridge.Dir + "/hud/";
        var cgo = new GameObject("gcam"); var cam = cgo.AddComponent<Camera>(); cam.orthographic = true; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.1f, 0.1f, 0.1f, 1);
        var cv = new GameObject("gcanvas", typeof(Canvas)); var canvas = cv.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = cam;
        var inst = (GameObject)UnityEngine.Object.Instantiate(pf, cv.transform, false);
        try
        {
            var g = inst.transform.Find("swingWingGauge");
            foreach (var go in inst.GetComponentsInChildren<Transform>(true)) if (go != inst.transform && !go.IsChildOf(g) && go.parent == inst.transform && go != g) go.gameObject.SetActive(false);
            var rtx = new RenderTexture(512, 512, 24); cam.targetTexture = rtx;
            var rtr = (RectTransform)cv.transform;
            foreach (var a in new[] { 20f, 45f, 68f })
            {
                g.Find("scaler/pivotOffset/wingPivot").localEulerAngles = new Vector3(0, 0, a);
                // centre the camera on the gauge
                Canvas.ForceUpdateCanvases();
                cam.orthographicSize = 1; 
                canvas.renderMode = RenderMode.WorldSpace; cv.transform.position = Vector3.zero; cv.transform.localScale = Vector3.one * 0.01f;
                inst.transform.localPosition = -g.localPosition;
                cam.transform.position = new Vector3(0, 0, -10); cam.orthographicSize = 1.0f;
                foreach (var img in inst.GetComponentsInChildren<Image>(true)) img.color = new Color(0, 1, 0, 1);
                cam.Render();
                RenderTexture.active = rtx; var t = new Texture2D(512, 512, TextureFormat.RGB24, false); t.ReadPixels(new Rect(0, 0, 512, 512), 0, 0); t.Apply(); RenderTexture.active = null;
                File.WriteAllBytes(od + "gauge_" + a + ".png", t.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(t);
            }
            cam.targetTexture = null; rtx.Release();
        }
        finally { UnityEngine.Object.DestroyImmediate(cv); UnityEngine.Object.DestroyImmediate(cgo); }
        L("gauge shots done");
    }

    public static void DumpStatusRoot()
    {
        var sb = new StringBuilder();
        foreach (var path in new[] { "Assets/Blueprinter/_donotship/GameObject/StatusDisplay_FastBomber1_PLACEHOLDER.prefab", Mod + "/F14B_StatusDisplay.prefab" })
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var rt = (RectTransform)root.transform; var img = root.GetComponent<Image>();
                sb.AppendLine(path + ": anchors " + rt.anchorMin + "/" + rt.anchorMax + " pivot " + rt.pivot + " size " + rt.sizeDelta + " anchoredPos " + rt.anchoredPosition + " rect " + rt.rect + " scale " + rt.localScale +
                    " img " + (img ? (img.sprite ? img.sprite.name + " " + img.sprite.rect : "null") + " type " + img.type + " preserve " + img.preserveAspect + " color " + img.color : "none"));
                foreach (Transform c in root.transform) { var r2 = c as RectTransform; var i2 = c.GetComponent<Image>(); sb.AppendLine("  " + c.name + " lp " + c.localPosition + (r2 ? " size " + r2.sizeDelta + " pivot " + r2.pivot + " anchors " + r2.anchorMin + "/" + r2.anchorMax : "") + (i2 && i2.sprite ? " sprite " + i2.sprite.name : "")); }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        File.WriteAllText(F14Bridge.Dir + "/hud/status_root.txt", sb.ToString()); L("status root dumped");
    }
}

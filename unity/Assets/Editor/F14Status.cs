using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// 0.1.4: F-14 damage status display. The AB-4 display maps parts by list order, so its layout is copied and
// every part image gets an F-14 top-view silhouette drawn from the model (all images share one frame).
public static partial class F14Build
{
    const string StatusDir = Mod + "/Status";
    const int SPX = 256;                       // texture size
    const float SPAN = 21f;                    // metres covered by the texture (x and z)
    static readonly Vector2 SCtr = new Vector2(0f, 0.6f);   // world (x, z) at the texture centre
    static readonly Vector2 DispCtr = new Vector2(-145f, 132f);  // canvas position of the texture centre (AB-4 layout)
    const float DispSize = 270f;

    static Vector2 Px(Vector3 w) => new Vector2((w.x - SCtr.x) / SPAN * SPX + SPX * 0.5f, (w.z - SCtr.y) / SPAN * SPX + SPX * 0.5f);
    public static Vector2 StatusCanvas(Vector3 w) => DispCtr + new Vector2((w.x - SCtr.x) / SPAN * DispSize, (w.z - SCtr.y) / SPAN * DispSize);

    static void Raster(bool[] mask, Vector2 a, Vector2 b, Vector2 c)
    {
        int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x)))), x1 = Mathf.Min(SPX - 1, Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))));
        int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y)))), y1 = Mathf.Min(SPX - 1, Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))));
        float d = (b.y - c.y) * (a.x - c.x) + (c.x - b.x) * (a.y - c.y);
        if (Mathf.Abs(d) < 1e-6f) { var m = (a + b + c) / 3f; int mx = Mathf.Clamp((int)m.x, 0, SPX - 1), my = Mathf.Clamp((int)m.y, 0, SPX - 1); mask[my * SPX + mx] = true; return; }
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float px = x + 0.5f, py = y + 0.5f;
                float l1 = ((b.y - c.y) * (px - c.x) + (c.x - b.x) * (py - c.y)) / d;
                float l2 = ((c.y - a.y) * (px - c.x) + (a.x - c.x) * (py - c.y)) / d;
                float l3 = 1f - l1 - l2;
                if (l1 >= -0.02f && l2 >= -0.02f && l3 >= -0.02f) mask[y * SPX + x] = true;
            }
    }

    static (Vector3[] v, int[] t) WorldTris(string child)
    {
        var tr = model.transform.Find(child); if (!tr) return (new Vector3[0], new int[0]);
        var m = tr.GetComponent<MeshFilter>().sharedMesh; var v = m.vertices;
        for (int i = 0; i < v.Length; i++) v[i] = tr.TransformPoint(v[i]);
        return (v, m.triangles);
    }

    static Sprite StatusSprite(string name, IEnumerable<(Vector3[] v, int[] t, Func<Vector3, bool> f)> srcs)
    {
        var mask = new bool[SPX * SPX];
        foreach (var (v, t, f) in srcs)
            for (int i = 0; i + 2 < t.Length; i += 3)
            {
                var a = v[t[i]]; var b = v[t[i + 1]]; var c = v[t[i + 2]];
                if (!f((a + b + c) / 3f)) continue;
                Raster(mask, Px(a), Px(b), Px(c));
            }
        var px = new Color32[SPX * SPX]; int n = 0;
        for (int y = 0; y < SPX; y++)
            for (int x = 0; x < SPX; x++)
            {
                int i = y * SPX + x; if (!mask[i]) continue; n++;
                bool edge = x == 0 || y == 0 || x == SPX - 1 || y == SPX - 1 || !mask[i - 1] || !mask[i + 1] || !mask[i - SPX] || !mask[i + SPX];
                px[i] = edge ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 70);
            }
        var tex = new Texture2D(SPX, SPX, TextureFormat.RGBA32, false); tex.SetPixels32(px); tex.Apply();
        string path = StatusDir + "/F14B_status_" + name + ".png";
        File.WriteAllBytes(path, tex.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.textureType = TextureImporterType.Sprite; ti.spriteImportMode = SpriteImportMode.Single; ti.alphaIsTransparency = true;
        ti.mipmapEnabled = false; ti.textureCompression = TextureImporterCompression.CompressedHQ; ti.maxTextureSize = SPX; ti.SaveAndReimport();
        if (n == 0) L("STATUS EMPTY " + name);
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    // root-rect background (AB-4 StatusDisplay root: anchors/pivot fixed, rect x -290..0, y -11.6..278.4)
    static readonly Vector2 RootMin = new Vector2(-290f, -11.6f); const float RootSize = 290f;
    static Vector2 Pr(Vector3 w) => (StatusCanvas(w) - RootMin) * (SPX / RootSize);
    static Sprite BgSprite(IEnumerable<(Vector3[] v, int[] t, Func<Vector3, bool> f)> srcs)
    {
        var mask = new bool[SPX * SPX];
        foreach (var (v, t, f) in srcs)
            for (int i = 0; i + 2 < t.Length; i += 3)
            {
                var a = v[t[i]]; var b = v[t[i + 1]]; var c = v[t[i + 2]];
                if (!f((a + b + c) / 3f)) continue;
                Raster(mask, Pr(a), Pr(b), Pr(c));
            }
        var px = new Color32[SPX * SPX];
        for (int y = 0; y < SPX; y++)
            for (int x = 0; x < SPX; x++)
            {
                int i = y * SPX + x; if (!mask[i]) continue;
                bool edge = x == 0 || y == 0 || x == SPX - 1 || y == SPX - 1 || !mask[i - 1] || !mask[i + 1] || !mask[i - SPX] || !mask[i + SPX];
                px[i] = edge ? new Color32(255, 255, 255, 64) : new Color32(255, 255, 255, 18);   // = the 0.1.5 outline at 25 % opacity
            }
        var tex = new Texture2D(SPX, SPX, TextureFormat.RGBA32, false); tex.SetPixels32(px); tex.Apply();
        string path = StatusDir + "/F14B_status_bg.png";
        File.WriteAllBytes(path, tex.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.textureType = TextureImporterType.Sprite; ti.spriteImportMode = SpriteImportMode.Single; ti.alphaIsTransparency = true;
        ti.mipmapEnabled = false; ti.textureCompression = TextureImporterCompression.CompressedHQ; ti.maxTextureSize = SPX; ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    static (Vector3[] v, int[] t) MergeTris((Vector3[] v, int[] t) a, (Vector3[] v, int[] t) b)
    {
        var v = new Vector3[a.v.Length + b.v.Length]; a.v.CopyTo(v, 0); b.v.CopyTo(v, a.v.Length);
        var t = new int[a.t.Length + b.t.Length]; a.t.CopyTo(t, 0); for (int i = 0; i < b.t.Length; i++) t[a.t.Length + i] = b.t[i] + a.v.Length;
        return (v, t);
    }

    public static GameObject BuildStatusDisplay()
    {
        if (!AssetDatabase.IsValidFolder(StatusDir)) AssetDatabase.CreateFolder(Mod, "Status");
        model = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
        var body = WorldTris("F14_Body"); var canopy = WorldTris("F14_Canopy");
        var wl = MergeTris(WorldTris("F14_Wing_L"), WorldTris("F14_Slat_L")); var wr = MergeTris(WorldTris("F14_Wing_R"), WorldTris("F14_Slat_R"));   // 0.1.8: slats are separate meshes
        Func<Vector3, float> ax = v => Mathf.Abs(v.x);
        var B = new Dictionary<string, List<(Vector3[] v, int[] t, Func<Vector3, bool> f)>>();
        void A(string key, (Vector3[] v, int[] t) m, Func<Vector3, bool> f) { if (!B.ContainsKey(key)) B[key] = new List<(Vector3[] v, int[] t, Func<Vector3, bool> f)>(); B[key].Add((m.v, m.t, f)); }
        Func<Vector3, bool> all = v => true;
        // same regions as the colliders (F14Assets.Colliders), keyed by the AB-4 status image names
        A("FastBomber1", body, v => v.z > -1.0f && v.z <= 0.45f && ax(v) <= 0.9f);
        A("fuselage_F", body, v => v.z > 0.5f && v.z <= 3.85f && ax(v) <= 0.9f);
        A("cockpit", body, v => v.z > 3.9f && v.z <= 7.9f && ax(v) < 0.85f && v.y > -0.03f); A("cockpit", canopy, all);
        A("nose", body, v => v.z > 7.9f);
        A("cheek_FL", body, v => v.z > 2.3f && v.z <= 3.85f && v.x < -0.9f); A("cheek_FR", body, v => v.z > 2.3f && v.z <= 3.85f && v.x > 0.9f);
        A("cheek_RL", body, v => v.z > 0.5f && v.z <= 2.3f && v.x < -2.44f); A("cheek_RR", body, v => v.z > 0.5f && v.z <= 2.3f && v.x > 2.44f);
        A("wingroot_L", body, v => v.z > -3.5f && v.z <= 0.5f && v.x < -2.44f && v.x > -3.9f); A("wingroot_R", body, v => v.z > -3.5f && v.z <= 0.5f && v.x > 2.44f && v.x < 3.9f);
        A("engines_L", body, v => v.z > -5.6f && v.z <= 0.5f && v.x < -0.9f && v.x >= -2.44f); A("engines_R", body, v => v.z > -5.6f && v.z <= 0.5f && v.x > 0.9f && v.x <= 2.44f);
        A("nozzles_L", body, v => v.z <= -5.6f && v.x < -0.9f && v.x > -2.3f && v.y < 0.95f); A("nozzles_R", body, v => v.z <= -5.6f && v.x > 0.9f && v.x < 2.3f && v.y < 0.95f);
        A("fuselage_R", body, v => v.z > -4.6f && v.z <= -1.0f && ax(v) <= 0.9f);
        A("tail", body, v => v.z <= -4.6f && ax(v) <= 0.9f);
        A("tail", WorldTris("F14_Fin_L"), all); A("tail", WorldTris("F14_Fin_R"), all); A("tail", WorldTris("F14_Rudder_L"), all); A("tail", WorldTris("F14_Rudder_R"), all);
        A("elevator_L", WorldTris("F14_Stab_L"), all); A("elevator_R", WorldTris("F14_Stab_R"), all);
        A("wing1_L", wl, v => v.x >= -6.5f); A("wing1_R", wr, v => v.x <= 6.5f);
        A("wing2_L", wl, v => v.x < -6.5f && v.x >= -8.9f); A("wing2_R", wr, v => v.x > 6.5f && v.x <= 8.9f);
        A("wingtip_L", wl, v => v.x < -8.9f); A("wingtip_R", wr, v => v.x > 8.9f);
        A("flap_L", WorldTris("F14_Flap_L"), all); A("flap_R", WorldTris("F14_Flap_R"), all);
        A("aileron_L", WorldTris("F14_Spoiler_L"), all); A("aileron_R", WorldTris("F14_Spoiler_R"), all);
        var sprites = new Dictionary<string, Sprite>();
        foreach (var kv in B) sprites[kv.Key] = StatusSprite(kv.Key, kv.Value);
        // whole aircraft, used as the background
        var bg = new List<(Vector3[] v, int[] t, Func<Vector3, bool> f)>();
        foreach (var n in new[] { "F14_Body", "F14_Canopy", "F14_Wing_L", "F14_Wing_R", "F14_Flap_L", "F14_Flap_R", "F14_Stab_L", "F14_Stab_R", "F14_Fin_L", "F14_Fin_R", "F14_Rudder_L", "F14_Rudder_R", "F14_Spoiler_L", "F14_Spoiler_R", "F14_Slat_L", "F14_Slat_R" }) { var m = WorldTris(n); bg.Add((m.v, m.t, all)); }
        var bgs = StatusSprite("all", bg);
        // 0.1.6: the root image is the game's aircraftBackground: it sets its colour/alpha and fades it out with the part images.
        // The faint whole-aircraft outline is baked into it, laid out in the root rect (290 x 290, x -290..0, y -11.6..278.4).
        var rootBg = BgSprite(bg);

        string dst = Mod + "/F14B_StatusDisplay.prefab";
        var root = PrefabUtility.LoadPrefabContents("Assets/Blueprinter/_donotship/GameObject/StatusDisplay_FastBomber1_PLACEHOLDER.prefab");
        try
        {
            int done = 0;
            foreach (var img in root.GetComponentsInChildren<Image>(true))
            {
                if (img.transform == root.transform)
                {
                    img.sprite = rootBg; img.type = Image.Type.Simple; img.preserveAspect = false; img.color = Color.white; img.raycastTarget = false; continue;
                }
                var nm = img.transform.name;
                if (nm.Contains("FIRE")) continue;
                if (!sprites.TryGetValue(nm, out var sp)) { L("status: no F-14 image for " + nm); continue; }
                var rt = img.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f);
                rt.localScale = Vector3.one; rt.localRotation = Quaternion.identity;
                rt.sizeDelta = new Vector2(DispSize, DispSize); rt.localPosition = new Vector3(DispCtr.x, DispCtr.y, 0);
                img.sprite = sp; img.preserveAspect = true; img.type = Image.Type.Simple; done++;
            }
            // engine fire lamps: 1-2 on the left nacelle, 3-4 on the right (two game engines per F110)
            var fire = new[] { ("ENGINE 1 FIRE", new Vector3(-1.45f, 0, -2.6f)), ("ENGINE 2 FIRE", new Vector3(-1.45f, 0, -3.6f)), ("ENGINE 3 FIRE", new Vector3(1.45f, 0, -3.6f)), ("ENGINE 4 FIRE", new Vector3(1.45f, 0, -2.6f)) };
            foreach (var (n, w) in fire) { var t = root.transform.Find(n); if (t) { var p = StatusCanvas(w); t.localPosition = new Vector3(p.x, p.y, 0); } }
            PrefabUtility.SaveAsPrefabAsset(root, dst);
            L("status display: " + done + " part images, saved " + dst);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        return AssetDatabase.LoadAssetAtPath<GameObject>(dst);
    }
}

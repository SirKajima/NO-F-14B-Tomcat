using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static partial class F14Build
{
    const string ColAsset = Mod + "/F14B_Colliders.asset";

    static Vector3[] WorldVerts(string child)
    {
        var t = model.transform.Find(child);
        var m = t.GetComponent<MeshFilter>().sharedMesh;
        var v = m.vertices; var o = t.position;
        for (int i = 0; i < v.Length; i++) v[i] += o;
        return v;
    }

    static Mesh BoxMesh(Vector3 a, Vector3 b, string name)
    {
        var m = new Mesh { name = name };
        m.vertices = new[] { new Vector3(a.x, a.y, a.z), new Vector3(b.x, a.y, a.z), new Vector3(b.x, b.y, a.z), new Vector3(a.x, b.y, a.z),
                             new Vector3(a.x, a.y, b.z), new Vector3(b.x, a.y, b.z), new Vector3(b.x, b.y, b.z), new Vector3(a.x, b.y, b.z) };
        m.triangles = new[] { 0,2,1, 0,3,2, 4,5,6, 4,6,7, 0,1,5, 0,5,4, 3,7,6, 3,6,2, 0,4,7, 0,7,3, 1,2,6, 1,6,5 };
        m.RecalculateNormals(); m.RecalculateBounds();
        return m;
    }

    static readonly Dictionary<string, Bounds> WorldBoxes = new Dictionary<string, Bounds>();
    static readonly Dictionary<string, Vector3[]> HullPts = new Dictionary<string, Vector3[]>();
    static Vector3[] Support(Vector3[] pts, Bounds box)
    {
        var c = Vector3.zero; foreach (var p in pts) c += p; c /= pts.Length;
        var dirs = new List<Vector3>();
        int n = 96; float ga = Mathf.PI * (3f - Mathf.Sqrt(5f));
        for (int i = 0; i < n; i++) { float y = 1f - 2f * (i + 0.5f) / n, r = Mathf.Sqrt(1f - y * y), th = ga * i; dirs.Add(new Vector3(Mathf.Cos(th) * r, y, Mathf.Sin(th) * r)); }
        for (int i = 0; i < 8; i++) dirs.Add(new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1).normalized);
        var outp = new HashSet<Vector3>();
        foreach (var d in dirs)
        {
            float best = -1e9f; Vector3 bp = c;
            foreach (var p in pts) { float s = Vector3.Dot(p, d); if (s > best) { best = s; bp = p; } }
            var q = bp + (c - bp).normalized * 0.03f;
            q = Vector3.Max(box.min, Vector3.Min(box.max, q));
            outp.Add(q);
        }
        return outp.ToArray();
    }
    // vertices only matter: Unity/PhysX cooks the convex hull of them (triangles are a fan so the mesh is valid)
    static Mesh HullMesh(Vector3[] v, string name)
    {
        var m = new Mesh { name = name }; m.vertices = v;
        var tri = new List<int>(); for (int i = 1; i + 1 < v.Length; i++) { tri.Add(0); tri.Add(i); tri.Add(i + 1); }
        m.triangles = tri.ToArray(); m.RecalculateNormals(); m.RecalculateBounds();
        return m;
    }

    static void Colliders()
    {
        var body = WorldVerts("F14_Body");
        var canopy = WorldVerts("F14_Canopy");
        var wl = WorldVerts("F14_Wing_L").Concat(WorldVerts("F14_Slat_L")).ToArray(); var wrr = WorldVerts("F14_Wing_R").Concat(WorldVerts("F14_Slat_R")).ToArray();   // 0.1.8: slats separate
        var regions = new List<(string path, IEnumerable<Vector3> src, Func<Vector3, bool> f, bool clampTop)>();
        Func<Vector3, float> ax = v => Mathf.Abs(v.x);
        regions.Add(("", body, v => v.z > -1.0f && v.z <= 0.45f && ax(v) <= 0.9f, false));
        regions.Add(("fuselage_F", body, v => v.z > 0.5f && v.z <= 3.85f && ax(v) <= 0.9f, false));
        regions.Add(("fuselage_F/fuselage_FF", body, v => v.z > 3.9f && v.z <= 7.9f && ax(v) < 0.85f && v.y <= -0.12f, false));
        regions.Add(("fuselage_F/fuselage_FF/cockpit", body.Concat(canopy), v => v.z > 3.9f && v.z <= 7.9f && ax(v) < 0.85f && v.y > -0.03f, false));
        regions.Add(("fuselage_F/fuselage_FF/nose", body, v => v.z > 7.96f && v.z <= 10.6f, false));
        regions.Add(("fuselage_F/fuselage_FF/cheek_FL", body, v => v.z > 2.3f && v.z <= 3.85f && v.x < -2.5f && v.x > -3.7f, true));
        regions.Add(("fuselage_F/fuselage_FF/cheek_FR", body, v => v.z > 2.3f && v.z <= 3.85f && v.x > 2.5f && v.x < 3.7f, true));
        regions.Add(("fuselage_F/cheek_RL", body, v => v.z > 0.5f && v.z <= 2.24f && v.x < -2.5f && v.x > -3.7f, true));
        regions.Add(("fuselage_F/cheek_RR", body, v => v.z > 0.5f && v.z <= 2.24f && v.x > 2.5f && v.x < 3.7f, true));
        regions.Add(("wingroot_L", body, v => v.z > -3.5f && v.z <= 0.44f && v.x < -2.5f && v.x > -3.7f, true));
        regions.Add(("wingroot_R", body, v => v.z > -3.5f && v.z <= 0.44f && v.x > 2.5f && v.x < 3.7f, true));
        regions.Add(("intake_L", body, v => v.z > 1.0f && v.z <= 3.85f && v.x < -0.96f && v.x > -2.44f, true));
        regions.Add(("intake_R", body, v => v.z > 1.0f && v.z <= 3.85f && v.x > 0.96f && v.x < 2.44f, true));
        regions.Add(("gearbay_L", body, v => v.z > -1.6f && v.z <= 0.94f && v.x < -0.96f && v.x > -2.44f && v.y > -1.0f, true));
        regions.Add(("gearbay_R", body, v => v.z > -1.6f && v.z <= 0.94f && v.x > 0.96f && v.x < 2.44f && v.y > -1.0f, true));
        regions.Add(("wingroot_L/engines_L", body, v => v.z > -5.6f && v.z <= -1.66f && v.x < -0.96f && v.x > -2.44f && v.y < 1.0f, true));
        regions.Add(("wingroot_R/engines_R", body, v => v.z > -5.6f && v.z <= -1.66f && v.x > 0.96f && v.x < 2.44f && v.y < 1.0f, true));
        regions.Add(("wingroot_L/engines_L/nozzles_L", body, v => v.z <= -5.66f && v.x < -0.96f && v.x > -2.3f && v.y < 0.95f, false));
        regions.Add(("wingroot_R/engines_R/nozzles_R", body, v => v.z <= -5.66f && v.x > 0.96f && v.x < 2.3f && v.y < 0.95f, false));
        regions.Add(("fuselage_R", body, v => v.z > -4.6f && v.z <= -1.06f && ax(v) <= 0.9f, false));
        regions.Add(("fuselage_R/tail", body, v => v.z <= -4.66f && ax(v) <= 0.9f, false));
        regions.Add(("wingroot_L/engines_L/rudder_L", body.Concat(WorldVerts("F14_Rudder_L")), v => v.y > 1.06f && v.x < -1.1f && v.x > -2.1f && v.z < -3.5f, false));
        regions.Add(("wingroot_R/engines_R/rudder_R", body.Concat(WorldVerts("F14_Rudder_R")), v => v.y > 1.06f && v.x > 1.1f && v.x < 2.1f && v.z < -3.5f, false));
        regions.Add(("wingroot_L/engines_L/nozzles_L/elevator_L", WorldVerts("F14_Stab_L"), v => v.x < -2.45f, false));
        regions.Add(("wingroot_R/engines_R/nozzles_R/elevator_R", WorldVerts("F14_Stab_R"), v => v.x > 2.45f, false));
        regions.Add(("wingroot_L/wing1_L", wl, v => v.x < -2.95f && v.x >= -6.5f && v.z > -1.75f, false));
        regions.Add(("wingroot_R/wing1_R", wrr, v => v.x > 2.95f && v.x <= 6.5f && v.z > -1.75f, false));
        regions.Add(("wingroot_L/wing1_L/wing2_L", wl, v => v.x < -6.56f && v.x >= -8.85f && v.z > -1.6f, false));
        regions.Add(("wingroot_R/wing1_R/wing2_R", wrr, v => v.x > 6.56f && v.x <= 8.85f && v.z > -1.6f, false));
        regions.Add(("wingroot_L/wing1_L/wing2_L/wingtip_L", wl, v => v.x < -8.91f, false));
        regions.Add(("wingroot_R/wing1_R/wing2_R/wingtip_R", wrr, v => v.x > 8.91f, false));
        regions.Add(("wingroot_L/wing1_L/flap_L", WorldVerts("F14_Flap_L"), v => v.x < -3.66f && v.x > -8.8f, false));
        regions.Add(("wingroot_R/wing1_R/flap_R", WorldVerts("F14_Flap_R"), v => v.x > 3.66f && v.x < 8.8f, false));

        if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(ColAsset) != null) AssetDatabase.DeleteAsset(ColAsset);
        bool first = true;
        WorldBoxes.Clear(); HullPts.Clear();
        void Put(string path, Bounds wb)
        {
            var t = PT(path);
            var mc = t.GetComponent<MeshCollider>();
            if (!mc) { L("no MeshCollider on " + path); return; }
            var a = t.InverseTransformPoint(wb.min); var b = t.InverseTransformPoint(wb.max);
            Mesh mesh;
            if (HullPts.TryGetValue(path, out var hp) && hp.Length >= 8) mesh = HullMesh(hp.Select(p => t.InverseTransformPoint(p)).ToArray(), "F14B_col_" + (path == "" ? "root" : t.name));
            else mesh = BoxMesh(Vector3.Min(a, b), Vector3.Max(a, b), "F14B_col_" + (path == "" ? "root" : t.name));
            if (first) { AssetDatabase.CreateAsset(mesh, ColAsset); first = false; } else AssetDatabase.AddObjectToAsset(mesh, ColAsset);
            mc.sharedMesh = mesh; mc.convex = true;
            WorldBoxes[path] = wb;
        }
        foreach (var r in regions)
        {
            var pts = r.src.Where(r.f).ToArray();
            if (pts.Length < 8) { L("REGION EMPTY " + r.path); continue; }
            var b = new Bounds(pts[0], Vector3.zero); foreach (var p in pts) b.Encapsulate(p);
            var mn = b.min + Vector3.one * 0.03f; var mx = b.max - Vector3.one * 0.03f;
            if (r.clampTop) mx.y = Mathf.Min(mx.y, 0.40f);
            if (mx.y - mn.y < 0.06f) mx.y = mn.y + 0.06f;
            b.SetMinMax(mn, mx);
            // 0.1.3: convex hull of the region (support points), shrunk 3 cm and clipped to the old box -> follows the real shape
            // (the 0.1.2 boxes carried the ventral-fin depth back to the nozzles: tail strike at 3.2 deg instead of ~11)
            HullPts[r.path] = Support(pts, b);
            Put(r.path, b);
        }
        Put("wingroot_L/wing1_L/wing2_L/aileron_L", new Bounds(new Vector3(-7.4f, 0.75f, -1.75f), new Vector3(1.6f, 0.06f, 0.5f)));
        Put("wingroot_R/wing1_R/wing2_R/aileron_R", new Bounds(new Vector3(7.4f, 0.75f, -1.75f), new Vector3(1.6f, 0.06f, 0.5f)));
        AssetDatabase.SaveAssets();
        L("colliders: " + WorldBoxes.Count);
    }

    static IEnumerable<Component> Parts_() => R.GetComponentsInChildren<Component>(true).Where(c => c != null && c.GetType().Name == "AeroPart");

    static void Report()
    {
        var sb = new StringBuilder();
        var joined = new HashSet<string>();
        foreach (var ap in Parts_())
        {
            var so = new SerializedObject(ap); var j = so.FindProperty("joints");
            for (int i = 0; i < j.arraySize; i++)
            {
                var cp = j.GetArrayElementAtIndex(i).FindPropertyRelative("connectedPart").objectReferenceValue as Component;
                if (cp) { joined.Add(F14Tools.Path(ap.transform, R) + "|" + F14Tools.Path(cp.transform, R)); joined.Add(F14Tools.Path(cp.transform, R) + "|" + F14Tools.Path(ap.transform, R)); }
            }
        }
        foreach (var s in new[] { "L", "R" }) { joined.Add("wingroot_" + s + "/wing1_" + s + "|wingroot_" + s); joined.Add("wingroot_" + s + "|wingroot_" + s + "/wing1_" + s); }
        var keys = WorldBoxes.Keys.ToList();
        for (int i = 0; i < keys.Count; i++)
            for (int k = i + 1; k < keys.Count; k++)
            {
                var a = WorldBoxes[keys[i]]; var b = WorldBoxes[keys[k]];
                var ea = a; ea.Expand(0.12f);
                if (ea.Intersects(b) && !joined.Contains(keys[i] + "|" + keys[k]))
                    sb.AppendLine("OVERLAP(unjointed) " + (keys[i] == "" ? "root" : keys[i]) + " <-> " + keys[k]);
            }
        float m = 0, mf = 0; Vector3 cg = Vector3.zero, cgf = Vector3.zero;
        foreach (var ap in Parts_())
        {
            var p = F14Tools.Path(ap.transform, R);
            float pm = GetF(ap, "mass");
            Vector3 c = WorldBoxes.ContainsKey(p) ? WorldBoxes[p].center : ap.transform.position;
            m += pm; cg += pm * c;
            var ft = C(ap.transform, "FuelTank"); float fuel = ft ? GetF(ft, "fuelCapacity") : 0;
            mf += pm + fuel; cgf += (pm + fuel) * c;
            sb.AppendLine(string.Format("part {0,-48} m={1,6} A={2,4} box={3}", p == "" ? "root" : p, pm, GetF(ap, "wingArea"), WorldBoxes.ContainsKey(p) ? WorldBoxes[p].center.ToString("F2") + " " + WorldBoxes[p].size.ToString("F2") : "none"));
        }
        foreach (var lg in R.GetComponentsInChildren<Component>(true).Where(c => c != null && c.GetType().Name == "LandingGear"))
        {
            float gm = GetF(lg, "mass"); m += gm; cg += gm * lg.transform.position; mf += gm; cgf += gm * lg.transform.position;
            var so = new SerializedObject(lg);
            string Pos(UnityEngine.Object o) { if (o is Transform tt) return tt.position.ToString("F2"); if (o is GameObject gg) return gg.transform.position.ToString("F2"); if (o is Component cc) return cc.transform.position.ToString("F2"); return "null"; }
            var gh = so.FindProperty("gearHinge").objectReferenceValue;
            sb.AppendLine("gear " + lg.transform.name + " pos=" + lg.transform.position.ToString("F2") + " cast=" + Pos(so.FindProperty("castPoint").objectReferenceValue) +
                " uns=" + Pos(so.FindProperty("unsprung").objectReferenceValue) + " bump=" + Pos(so.FindProperty("bumpStop").objectReferenceValue) + " fold=" + GetF(lg, "foldDegrees") + " hingeRight=" + (gh is Transform ght ? ght.right.ToString("F2") : "null"));
        }
        {
            var mains = R.GetComponentsInChildren<Component>(true).Where(c => c != null && c.GetType().Name == "LandingGear" && c.transform.name != "gear_F").ToArray();
            float gy = 0f, gz = 0f;
            foreach (var lg in mains) { var cp = new SerializedObject(lg).FindProperty("castPoint").objectReferenceValue as Transform; gy += cp.position.y - GetF(lg, "suspensionTravel"); gz += cp.position.z; }
            gy /= mains.Length; gz /= mains.Length;
            float minA = 90f; string who = "";
            foreach (var mc in R.GetComponentsInChildren<MeshCollider>(true))
            {
                if (!mc.sharedMesh || mc.transform.name.StartsWith("gear")) continue;
                foreach (var v in mc.sharedMesh.vertices) { var w = mc.transform.TransformPoint(v); float dz = gz - w.z; if (dz < 0.5f) continue; float a = Mathf.Atan2(w.y - gy, dz) * Mathf.Rad2Deg; if (a < minA) { minA = a; who = mc.transform.name; } }
            }
            sb.AppendLine("TAIL tail-strike angle (extended gear, colliders) " + minA.ToString("F1") + " deg at " + who + "; main tyre ground y " + gy.ToString("F3"));
        }
        sb.AppendLine("EMPTY mass=" + m + " CG=" + (cg / m).ToString("F2") + "   FULL mass=" + mf + " CG=" + (cgf / mf).ToString("F2"));
        File.WriteAllText(F14Tools.Out + "prefab_report.txt", sb.ToString());
        L("report written");
    }

    public static void Step3_Assets()
    {
        string fa = "Assets/Blueprinter/Mods/FA18C/";
        string par = Mod + "/F14B_Parameters.asset", def = Mod + "/F14B_AircraftDefinition.asset", liv = Mod + "/F14B_Livery_NavyGray.asset";
        string icon = Mod + "/F14B_Icon.png";
        foreach (var p in new[] { par, def, liv }) if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(p) != null) AssetDatabase.DeleteAsset(p);
        AssetDatabase.CopyAsset(fa + "FA18C_Parameters.asset", par);
        AssetDatabase.CopyAsset(fa + "FA18C_AircraftDefinition.asset", def);
        AssetDatabase.CopyAsset(fa + "FA18C_Livery_NavyGray.asset", liv);
        if (!File.Exists(icon)) AssetDatabase.CopyAsset(fa + "FA18C_Icon.png", icon);
        AssetDatabase.Refresh();
        var ti = AssetImporter.GetAtPath(icon) as TextureImporter; if (ti) { ti.textureType = TextureImporterType.Sprite; ti.SaveAndReimport(); }

        var lv = AssetDatabase.LoadAssetAtPath<ScriptableObject>(liv);
        var lso = new SerializedObject(lv);
        lso.FindProperty("Texture").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "/F14B_8bc3cf9c_base.png");
        lso.FindProperty("Glossiness").floatValue = 0.45f;
        lso.ApplyModifiedPropertiesWithoutUndo();
        string livGuid = AssetDatabase.AssetPathToGUID(liv);

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var pa = AssetDatabase.LoadAssetAtPath<ScriptableObject>(par);
        var pso = new SerializedObject(pa);
        pso.FindProperty("aircraftName").stringValue = "F-14B Tomcat";
        pso.FindProperty("rankRequired").intValue = 3;
        var lvs = pso.FindProperty("liveries"); lvs.arraySize = 2;
        lvs.GetArrayElementAtIndex(0).FindPropertyRelative("name").stringValue = "BDF Navy Gray";
        lvs.GetArrayElementAtIndex(1).FindPropertyRelative("name").stringValue = "PALA Navy Gray";
        for (int i = 0; i < 2; i++) lvs.GetArrayElementAtIndex(i).FindPropertyRelative("assetReference.m_AssetGUID").stringValue = livGuid;
        var sd = BuildStatusDisplay();
        pso.FindProperty("StatusDisplay").objectReferenceValue = sd;
        // 0.1.4: AB-4 HUD set = the Hornet's plus the wing-sweep gauge
        pso.FindProperty("HUDExtras").objectReferenceValue = BuildHudExtras();   // 0.1.5: F-14 copy with an F-14 planform in the sweep gauge
        pso.FindProperty("aircraftGLimit").floatValue = 8.5f;
        pso.FindProperty("maxSpeed").floatValue = 650f;
        pso.FindProperty("takeoffSpeed").floatValue = 72f;
        pso.FindProperty("takeoffDistance").floatValue = 1000f;
        pso.FindProperty("turningRadius").floatValue = 1600f;
        pso.FindProperty("cornerSpeed").floatValue = 160f;
        pso.FindProperty("approachSpeed").floatValue = 68f;
        pso.FindProperty("landingSpeed").floatValue = 70f;
        pso.FindProperty("shortLandingSpeed").floatValue = 68f;
        var wm = prefab.GetComponentsInChildren<Component>(true).First(c => c != null && c.GetType().Name == "WeaponManager");
        var wso = new SerializedObject(wm); var sets = wso.FindProperty("hardpointSets");
        UnityEngine.Object Opt(int set, string name)
        {
            var o = sets.GetArrayElementAtIndex(set).FindPropertyRelative("weaponOptions");
            for (int k = 0; k < o.arraySize; k++) { var x = o.GetArrayElementAtIndex(k).objectReferenceValue; if (x && x.name == name) return x; }
            L("no option " + name + " in set " + set); return null;
        }
        var lo = pso.FindProperty("loadouts"); lo.arraySize = 2;
        var w0 = lo.GetArrayElementAtIndex(0).FindPropertyRelative("weapons"); w0.arraySize = 5;
        for (int k = 0; k < 5; k++) w0.GetArrayElementAtIndex(k).objectReferenceValue = null;
        var w1 = lo.GetArrayElementAtIndex(1).FindPropertyRelative("weapons"); w1.arraySize = 5;
        w1.GetArrayElementAtIndex(0).objectReferenceValue = Opt(0, "gun_20mm_internal");
        w1.GetArrayElementAtIndex(1).objectReferenceValue = Opt(1, "AAM1_single");
        w1.GetArrayElementAtIndex(2).objectReferenceValue = Opt(2, "AAM2_single");
        w1.GetArrayElementAtIndex(3).objectReferenceValue = Opt(3, "AAM2_single");
        w1.GetArrayElementAtIndex(4).objectReferenceValue = Opt(4, "AAM2_single");
        pso.ApplyModifiedPropertiesWithoutUndo();

        var da = AssetDatabase.LoadAssetAtPath<ScriptableObject>(def);
        var dso = new SerializedObject(da);
        dso.FindProperty("jsonKey").stringValue = "F14B";
        dso.FindProperty("WorkshopTag").stringValue = "F-14B Tomcat";
        dso.FindProperty("unitName").stringValue = "F-14B Tomcat";
        dso.FindProperty("code").stringValue = "F-14B";
        dso.FindProperty("description").stringValue = "A twin-engine, two-seat, carrier-based fleet-defence fighter with variable-sweep wings that move from 20 to 68 degrees with speed. The F-14B pairs the Tomcat airframe with General Electric F110 engines, a 20mm rotary cannon, air-to-air shoulder rails and pylons on the wing gloves, and three fuselage tunnel stations for missiles or bombs.";
        dso.FindProperty("mapIcon").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Sprite>(icon);
        dso.FindProperty("unitPrefab").objectReferenceValue = prefab;
        dso.FindProperty("aircraftParameters").objectReferenceValue = pa;
        dso.FindProperty("length").floatValue = 19.1f;
        dso.FindProperty("width").floatValue = 19.55f;
        dso.FindProperty("height").floatValue = 4.88f;
        dso.FindProperty("mass").floatValue = 19000f;
        dso.FindProperty("value").floatValue = 75f;
        dso.FindProperty("spawnOffset").vector3Value = new Vector3(0, 2.15f, -1.0f);
        dso.FindProperty("aircraftInfo.emptyWeight").floatValue = 19000f;
        dso.FindProperty("aircraftInfo.maxSpeed").floatValue = 2485f;
        dso.FindProperty("aircraftInfo.stallSpeed").floatValue = 210f;
        dso.FindProperty("aircraftInfo.maneuverability").floatValue = 7f;
        dso.FindProperty("aircraftInfo.maxWeight").floatValue = 33720f;
        dso.ApplyModifiedPropertiesWithoutUndo();

        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var ac = root.GetComponents<Component>().First(c => c != null && c.GetType().Name == "Aircraft");
            var aso = new SerializedObject(ac); aso.FindProperty("definition").objectReferenceValue = da;
            var rcs = aso.FindProperty("RCS"); if (rcs != null) { L("RCS " + rcs.floatValue + " -> 1.0"); rcs.floatValue = 1.0f; }
            aso.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }

        string op = Mod + "/OpAddAircraftToHangars.asset";
        File.WriteAllText(op, File.ReadAllText(fa + "OpAddAircraftToHangars.asset").Replace("aircraftJsonKey: FA18C", "aircraftJsonKey: F14B"));
        File.WriteAllText(Mod + "/modinfo.json", "{\n    \"displayName\": \"F-14B Tomcat\",\n    \"version\": \"" + Version + "\"\n}");
        AssetDatabase.Refresh();
        AssetDatabase.SaveAssets();
        L("Step3 done; livery guid " + livGuid);
    }

    public static void Step4_Build()
    {
        var t = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Blueprinter.ModBuilder")).FirstOrDefault(x => x != null);
        var mi = t.GetMethod("Build", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        L("building with " + mi);
        mi.Invoke(null, new object[] { "F14B", "F-14B Tomcat", Version, "E:/Project/NOModding/Builds" });
        L("Step4 build returned");
    }

    public static void All() { Step2_Prefab(); Step3_Assets(); }
}

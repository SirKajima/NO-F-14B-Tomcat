using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static partial class F14Build
{
    public const string PrefabPath = Mod + "/F14B_FlyingPrefab.prefab";
    const string AB4 = "Assets/Blueprinter/_donotship/GameObject/FastBomber1_PLACEHOLDER.prefab";
    const string HornetPrefab = "Assets/Blueprinter/Mods/FA18C/FA18C_FlyingPrefab.prefab";

    // ------------------------------------------------------------------ helpers
    static Transform R;
    static Transform F(string path) { var t = R.Find(path); if (t == null) L("MISSING " + path); return t; }
    static Component C(Transform t, string type) => t == null ? null : t.GetComponents<Component>().FirstOrDefault(c => c != null && c.GetType().Name == type);
    static Component C(string path, string type) => C(F(path), type);
    static void P(string path, Vector3 w) { var t = F(path); if (t) t.position = w; }
    static void PR(string path, Vector3 w, Quaternion q) { var t = F(path); if (t) { t.position = w; t.rotation = q; } }

    static SerializedProperty Prop(SerializedObject so, string p) { var x = so.FindProperty(p); if (x == null) L("NO PROP " + so.targetObject.GetType().Name + "." + p); return x; }
    static void SetF(Component c, string p, float v) { if (!c) { L("null comp for " + p); return; } var so = new SerializedObject(c); var x = Prop(so, p); if (x != null) { x.floatValue = v; so.ApplyModifiedPropertiesWithoutUndo(); } }
    static void SetI(Component c, string p, int v) { if (!c) return; var so = new SerializedObject(c); var x = Prop(so, p); if (x != null) { x.intValue = v; so.ApplyModifiedPropertiesWithoutUndo(); } }
    static void SetB(Component c, string p, bool v) { if (!c) return; var so = new SerializedObject(c); var x = Prop(so, p); if (x != null) { x.boolValue = v; so.ApplyModifiedPropertiesWithoutUndo(); } }
    static void SetV(Component c, string p, Vector3 v) { if (!c) return; var so = new SerializedObject(c); var x = Prop(so, p); if (x != null) { x.vector3Value = v; so.ApplyModifiedPropertiesWithoutUndo(); } }
    static void SetO(Component c, string p, UnityEngine.Object v) { if (!c) return; var so = new SerializedObject(c); var x = Prop(so, p); if (x != null) { x.objectReferenceValue = v; so.ApplyModifiedPropertiesWithoutUndo(); } }
    static float GetF(Component c, string p) { var so = new SerializedObject(c); var x = so.FindProperty(p); return x == null ? float.NaN : x.floatValue; }

    // rotate t so its local X lies along dir (sense kept equal to the old X), staying as close as possible to the old up
    static void AlignX(Transform t, Vector3 dir)
    {
        if (!t) return;
        dir.Normalize();
        if (Vector3.Dot(t.right, dir) < 0) dir = -dir;
        var up0 = t.up;
        var fwd = Vector3.Cross(dir, up0);
        if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.Cross(dir, t.forward);
        fwd.Normalize();
        var up = Vector3.Cross(fwd, dir).normalized;
        t.rotation = Quaternion.LookRotation(fwd, up);
    }

    static GameObject model;
    static Transform Attach(string meshName, Transform parent, string newName = null)
    {
        var src = model.transform.Find(meshName);
        if (!src || !parent) { L("ATTACH FAIL " + meshName + " -> " + (parent ? parent.name : "null")); return null; }
        var go = new GameObject(newName ?? meshName);
        go.transform.position = src.position; go.transform.rotation = src.rotation;
        go.AddComponent<MeshFilter>().sharedMesh = src.GetComponent<MeshFilter>().sharedMesh;
        var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterials = src.GetComponent<MeshRenderer>().sharedMaterials;
        go.transform.SetParent(parent, true);
        go.layer = parent.gameObject.layer;
        AddDamageRenderer(mr);
        return go.transform;
    }

    static void AddDamageRenderer(Renderer r)
    {
        var t = r.transform;
        Component part = null;
        while (t != null && part == null) { part = C(t, "AeroPart"); t = t.parent; }
        if (part == null) return;
        var so = new SerializedObject(part);
        var arr = so.FindProperty("damageMaterial.renderers");
        if (arr == null) return;
        arr.arraySize++;
        arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = r;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // ------------------------------------------------------------------ main
    public static void Step2_Prefab()
    {
        model = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
        var src = AssetDatabase.LoadAssetAtPath<GameObject>(AB4);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
        PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        go.name = "F14B_FlyingPrefab";
        R = go.transform;
        try
        {
            Strip();
            Layout();
            Meshes();
            Spoilers();
            Slats();
            Gear();
            Controls();
            RudderJoints();
            Engines();
            NozzleFX();
            Parts();
            Cockpit();
            Ejection();
            NavLights();
            Ladder();
            TailHookSetup();
            Weapons();
            Colliders();
            Report();
            PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
            L("Step2 saved " + PrefabPath);
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }

    static readonly string[] Keep = { "afterburner", "flame", "glow", "EjectionSeat", "Parachute", "DrogueChute", "chocks", "ladder", "navlight" };
    static void Strip()
    {
        int n = 0;
        foreach (var mf in R.GetComponentsInChildren<MeshFilter>(true))
        {
            var p = F14Tools.Path(mf.transform, R);
            if (Keep.Any(k => p.Contains(k)) && !p.EndsWith("LoD")) continue;
            mf.sharedMesh = null;
            var r = mf.GetComponent<MeshRenderer>(); if (r) r.enabled = false;
            n++;
        }
        // damage renderer lists start empty (F-14 renderers are added as they are attached)
        foreach (var part in R.GetComponentsInChildren<Component>(true).Where(c => c != null && c.GetType().Name == "AeroPart"))
        {
            var so = new SerializedObject(part); var a = so.FindProperty("damageMaterial.renderers");
            if (a != null) { a.arraySize = 0; so.ApplyModifiedPropertiesWithoutUndo(); }
        }
        var lod = R.GetComponent<LODGroup>(); if (lod) UnityEngine.Object.DestroyImmediate(lod);
        L("stripped " + n + " AB-4 meshes");
    }

    static void Layout()
    {
        P("fuselage_F", new Vector3(0, 0.15f, 2.2f));
        P("fuselage_F/fuselage_FF", new Vector3(0, -0.35f, 5.9f));
        P("fuselage_F/fuselage_FF/cockpit", new Vector3(0, 0.8f, 5.9f));
        P("fuselage_F/fuselage_FF/nose", new Vector3(0, 0.05f, 9.2f));
        P("fuselage_F/fuselage_FF/cheek_FL", new Vector3(-2.9f, 0.3f, 2.9f));
        P("fuselage_F/fuselage_FF/cheek_FR", new Vector3(2.9f, 0.3f, 2.9f));
        P("fuselage_F/cheek_RL", new Vector3(-3.05f, 0.3f, 1.2f));
        P("fuselage_F/cheek_RR", new Vector3(3.05f, 0.3f, 1.2f));
        P("fuselage_F/gearDoorHinge_FL1", F14Data.NoseDoorP_L); F("fuselage_F/gearDoorHinge_FL1").rotation = Quaternion.identity;
        P("fuselage_F/gearDoorHinge_FR1", F14Data.NoseDoorP_R); F("fuselage_F/gearDoorHinge_FR1").rotation = Quaternion.identity;
        P("fuselage_R", new Vector3(0, 0.1f, -2.8f));
        P("fuselage_R/tail", new Vector3(0, 0.05f, -6.1f));
        P("intake_L", new Vector3(-1.7f, 0f, 2.5f)); P("intake_R", new Vector3(1.7f, 0f, 2.5f));
        P("gearbay_L", new Vector3(-1.75f, -0.25f, -0.4f)); P("gearbay_R", new Vector3(1.75f, -0.25f, -0.4f));
        P("vaporCone", new Vector3(0, 0.3f, 1.0f));
        P("ladder", new Vector3(-0.95f, -0.8f, 5.2f));
        P("gear_F_armtarget", new Vector3(0, -0.4f, 5.9f));
        foreach (var s in new[] { "L", "R" })
        {
            float sx = s == "L" ? -1f : 1f;
            string wr = "wingroot_" + s;
            P(wr, new Vector3(sx * 2.95f, 0.55f, -1.2f));
            P(wr + "/wingHinge_" + s + "a", s == "L" ? F14Data.WingPivot_L : F14Data.WingPivot_R);
            string w1 = wr + "/wing1_" + s;
            P(w1, new Vector3(sx * 4.8f, 0.6f, -0.3f));
            P(w1 + "/wing1_" + s + "_anchor", s == "L" ? F14Data.WingPivot_L : F14Data.WingPivot_R);
            P(w1 + "/pylon_" + s + "_hinge", new Vector3(sx * 4.0f, 0.5f, -0.3f));
            string w2 = w1 + "/wing2_" + s;
            P(w2, new Vector3(sx * 7.6f, 0.6f, -1.0f));
            P(w2 + "/wing2_" + s + "_anchor", new Vector3(sx * 6.6f, 0.6f, -0.7f));
            P(w2 + "/aileron_" + s, new Vector3(sx * 7.3f, 0.62f, -1.75f));
            string wt = w2 + "/wingtip_" + s;
            P(wt, new Vector3(sx * 9.3f, 0.6f, -1.4f));
            P(wt + "/navlight_" + s, new Vector3(sx * 9.22f, 0.53f, -1.40f));
            P(wt + "/navlight_" + s + "/navlight_" + s + "_effects", new Vector3(sx * 9.25f, 0.53f, -1.40f));
            P(wt + "/wingtipvortex_" + s, new Vector3(sx * 9.6f, 0.6f, -2.3f));
            P(w1 + "/flap_" + s, new Vector3(sx * 5.6f, 0.57f, -2.0f));
            string en = wr + "/engines_" + s;
            P(en, new Vector3(sx * 1.5f, 0f, -3.4f));
            string nz = en + "/nozzles_" + s;
            P(nz, new Vector3(sx * 1.42f, 0f, -6.4f));
            P(nz + "/elevator_" + s, new Vector3(sx * 3.3f, 0.06f, -6.0f));
            P(en + "/rudderFoldHinge_" + s + "a", new Vector3(sx * 1.52f, 1.0f, -4.6f));
            P(en + "/rudder_" + s, new Vector3(sx * 1.55f, 1.9f, -5.9f));
            P("gearbay_" + s + "/flareEjector_" + s, new Vector3(sx * 1.2f, -0.6f, -3.5f));
        }
        P("wingroot_L/engines_L/engine1", new Vector3(-1.45f, 0, -3.0f)); P("wingroot_L/engines_L/engine2", new Vector3(-1.45f, 0, -3.8f));
        P("wingroot_R/engines_R/engine3", new Vector3(1.45f, 0, -3.8f)); P("wingroot_R/engines_R/engine4", new Vector3(1.45f, 0, -3.0f));
        foreach (var n in new[] { "wingroot_L/engines_L/nozzles_L/nozzle1", "wingroot_L/engines_L/nozzles_L/nozzle2" }) P(n, new Vector3(-1.42f, 0f, -7.1f));
        foreach (var n in new[] { "wingroot_R/engines_R/nozzles_R/nozzle3", "wingroot_R/engines_R/nozzles_R/nozzle4" }) P(n, new Vector3(1.42f, 0f, -7.1f));
        F("wingroot_L/engines_L/nozzles_L/nozzle2/afterburner2_nozzle_glow").localScale = Vector3.one * 0.001f;
        F("wingroot_R/engines_R/nozzles_R/nozzle4/afterburner4_nozzle_glow").localScale = Vector3.one * 0.001f;
        L("layout done");
    }

    static void Meshes()
    {
        Attach("F14_Body", R);
        Attach("F14_Canopy", F("fuselage_F/fuselage_FF/cockpit/doorHinge_L1/doorHinge_L2/door_L"));
        Attach("F14_NoseDoor_L", F("fuselage_F/gearDoorHinge_FL1/gearDoorHinge_FL2"));
        Attach("F14_NoseDoor_R", F("fuselage_F/gearDoorHinge_FR1/gearDoorHinge_FR2"));
        foreach (var s in new[] { "L", "R" })
        {
            string wr = "wingroot_" + s, w1 = wr + "/wing1_" + s, en = wr + "/engines_" + s, nz = en + "/nozzles_" + s;
            Attach("F14_Wing_" + s, F(w1));
            var fh1 = F(w1 + "/flap_" + s + "/flapHinge_" + s + "1");
            fh1.position = s == "L" ? F14Data.FlapP_L : F14Data.FlapP_R;
            AlignX(fh1, s == "L" ? F14Data.FlapD_L : F14Data.FlapD_R);
            Attach("F14_Flap_" + s, F(w1 + "/flap_" + s + "/flapHinge_" + s + "1/flapHinge_" + s + "2"));
            var eh1 = F(nz + "/elevator_" + s + "/elevatorHinge_" + s + "1");
            eh1.position = s == "L" ? F14Data.StabP_L : F14Data.StabP_R;
            AlignX(eh1, new Vector3(1, 0, 0));
            Attach("F14_Stab_" + s, F(nz + "/elevator_" + s + "/elevatorHinge_" + s + "1/elevatorHinge_" + s + "2"));
            Attach("F14_Fin_" + s, F(en + "/rudder_" + s));
            var rh1 = F(en + "/rudder_" + s + "/rudder_" + s + "_hinge1");
            var ln = F(en + "/rudder_" + s + "/rudder_" + s + "_hinge1/rudder_" + s + "_hinge2/rudder_" + s + "_visible/rudder_" + s + "_liftNormal");
            var lnUp0 = ln ? ln.up : Vector3.up;
            rh1.position = s == "L" ? F14Data.RudderP_L : F14Data.RudderP_R;
            AlignX(rh1, s == "L" ? F14Data.RudderD_L : F14Data.RudderD_R);
            if (ln)
            {
                var want = new Vector3(Mathf.Sign(lnUp0.x), 0, 0);
                ln.rotation = Quaternion.FromToRotation(ln.up, want) * ln.rotation;
                ln.position = new Vector3((s == "L" ? -1 : 1) * 1.55f, 1.9f, -6.0f);
            }
            Attach("F14_Rudder_" + s, F(en + "/rudder_" + s + "/rudder_" + s + "_hinge1/rudder_" + s + "_hinge2"));
        }
        var fr = F("fuselage_R");
        var hu = F("wingroot_L/wing1_L/wing2_L/wingtip_L/airbrake_L_mount/airbrake_L_hinge_U");
        var hl = F("wingroot_L/wing1_L/wing2_L/wingtip_L/airbrake_L_mount/airbrake_L_hinge_L");
        var hl2 = F("wingroot_R/wing1_R/wing2_R/wingtip_R/airbrake_R_mount/airbrake_R_hinge_L");
        var hu2 = F("wingroot_R/wing1_R/wing2_R/wingtip_R/airbrake_R_mount/airbrake_R_hinge_U");
        foreach (var h in new[] { hu, hl, hl2, hu2 }) h.SetParent(fr, true);
        hu.position = F14Data.SBUpP; AlignX(hu, F14Data.SBUpD);
        hu2.position = F14Data.SBUpP; AlignX(hu2, F14Data.SBUpD);
        hl.position = F14Data.SBLowP_L; AlignX(hl, new Vector3(1, 0, 0));
        hl2.position = F14Data.SBLowP_R; AlignX(hl2, new Vector3(1, 0, 0));
        Attach("F14_SB_Upper", hu);
        Attach("F14_SB_Lower_L", hl);
        Attach("F14_SB_Lower_R", hl2);
        L("meshes attached; SB hinge right vectors U=" + hu.right.ToString("F2") + " L=" + hl.right.ToString("F2") + " rot U=" + hu.eulerAngles.ToString("F1"));
    }

    static float FoldAngle(Vector3 trunnion, Vector3 axle, Vector3 stowed, ref Vector3 axis, float deg)
    {
        float best = 1e9f, ang = deg; Vector3 bestAx = axis;
        foreach (var ax in new[] { axis, -axis })
            foreach (var a in new[] { deg, -deg })
            {
                var p = trunnion + Quaternion.AngleAxis(a, ax) * (axle - trunnion);
                float d = (p - stowed).magnitude;
                if (d < best) { best = d; ang = a; bestAx = ax; }
            }
        axis = bestAx;
        L("fold check err=" + best.ToString("F3") + " ang=" + ang + " axis=" + axis.ToString("F2"));
        return ang;
    }

    // a door: pivot (world-identity) under a part, door transform under it; mesh attached closed; open angle about Unity Z (hinge along the fuselage)
    static Transform MakeDoor(string name, string parentPart, Vector3 hinge, string mesh)
    {
        var p = new GameObject(name + "_hinge").transform; p.SetParent(F(parentPart), false); p.position = hinge; p.rotation = Quaternion.identity; p.gameObject.layer = p.parent.gameObject.layer;
        var d = new GameObject(name).transform; d.SetParent(p, false); d.localPosition = Vector3.zero; d.localRotation = Quaternion.identity; d.gameObject.layer = p.gameObject.layer;
        Attach(mesh, d);
        return d;
    }
    // pick the sign of the open angle that swings the door's free edge downward
    static Vector3 DoorOpen(Transform d, float deg)
    {
        var mf = d.GetComponentInChildren<MeshFilter>(); var v = mf.sharedMesh.vertices; var mt = mf.transform;
        float best = 1e9f; float pick = deg;
        foreach (var a in new[] { deg, -deg })
        {
            var q = Quaternion.Euler(0, 0, a); float low = 0f;
            foreach (var x in v) { var w = d.position + q * (mt.TransformPoint(x) - d.position); low = Mathf.Min(low, w.y - d.position.y); }
            // free edge should end up below the hinge and as close to straight down as possible
            if (-low < best) { } 
            float sc = low; if (sc < best) { best = sc; pick = a; }
        }
        return new Vector3(0, 0, pick);
    }
    static void AddDoor(SerializedProperty doors, Transform d, Vector3 open)
    {
        doors.arraySize++;
        var e = doors.GetArrayElementAtIndex(doors.arraySize - 1);
        e.FindPropertyRelative("transform").objectReferenceValue = d;
        e.FindPropertyRelative("closedAngle").vector3Value = Vector3.zero;
        e.FindPropertyRelative("openAngle").vector3Value = open;
    }

    static void GearLeg(string s, Vector3 tr, Vector3 axle, float strutRot)
    {
        string gb = "gearbay_" + s, mount = gb + "/gearMount_" + s, hinge = mount + "/gearHinge_" + s, gear = hinge + "/gear_" + s;
        var m = F(mount); m.position = tr; m.rotation = Quaternion.identity;
        var h = F(hinge); h.localPosition = Vector3.zero; h.localRotation = Quaternion.identity;
        var g = F(gear);
        var bump = F(gear + "/bumpstop");
        var uns = F(gear + "/gear_unsprung_" + s);
        var bog = F(gear + "/gear_unsprung_" + s + "/bogey");
        var wF = F(gear + "/gear_unsprung_" + s + "/bogey/wheel_" + s + "F");
        var wR = F(gear + "/gear_unsprung_" + s + "/bogey/wheel_" + s + "R");
        // strut-rotation transform between the fold hinge and the sprung strut (wheel turns flat while folding)
        var sr = new GameObject("F14_StrutRot_" + s).transform;
        sr.SetParent(h, false); sr.position = tr; sr.rotation = Quaternion.identity; sr.gameObject.layer = h.gameObject.layer;
        g.SetParent(sr, true); g.position = tr; g.rotation = Quaternion.identity;
        bump.position = axle + new Vector3(0, 0.5f - 0.47f, 0); bump.rotation = Quaternion.identity;   // travel - wheel radius
        uns.position = axle; uns.rotation = Quaternion.identity;
        bog.position = axle; bog.rotation = Quaternion.identity;
        foreach (var w in new[] { wF, wR }) { w.localPosition = Vector3.zero; w.localRotation = Quaternion.identity; }
        Attach("F14_MainStrut_" + s, sr);
        Attach("F14_MainFork_" + s, uns);
        Attach("F14_MainWheel_" + s, wF);
        var lg = C(g, "LandingGear");
        SetF(lg, "foldDegrees", -90f);
        SetV(lg, "hingeFoldMotion", Vector3.zero);
        SetO(lg, "strutRotationTransform", sr);
        SetF(lg, "strutRotation", strutRot);
        SetO(lg, "castPoint", bump);
        SetF(lg, "wheelRadius", 0.47f);
        SetF(lg, "suspensionTravel", 0.5f);
        SetF(lg, "springRate", 1.2e6f);
        SetF(lg, "dampingRate", 1.0e5f);
        SetF(lg, "maxCompression", 0.45f);
        SetF(lg, "mass", 300f);
        var fd = MakeDoor("F14_MainFwdDoor_" + s, "gearbay_" + s, s == "L" ? F14Data.MainFwdDoorP_L : F14Data.MainFwdDoorP_R, "F14_MainFwdDoor_" + s);
        var so = new SerializedObject(lg); var gd = so.FindProperty("gearDoors"); gd.arraySize = 0;
        var op = DoorOpen(fd, 67f); AddDoor(gd, fd, op); so.ApplyModifiedPropertiesWithoutUndo();
        L("main " + s + " forward door open " + op);
        var bc = g.GetComponent<BoxCollider>();
        if (bc) { bc.center = g.InverseTransformPoint(new Vector3(tr.x, (tr.y + axle.y) * 0.5f + 0.2f, tr.z)); bc.size = new Vector3(0.3f, Mathf.Abs(tr.y - axle.y) * 0.6f, 0.3f); }
        L("gear " + s + " fold -90, strutRotation " + strutRot);
    }

    // nose cast point above the axle: travel - wheel radius (0.17) puts the extended tyre at its design height;
    // the extra 0.037 m trims the static attitude to ~0.5 deg nose-up with the stiffer strut
    const float NoseCastOffset = 0.45f - 0.28f + 0.037f;
    static void Gear()
    {
        GearLeg014("L", F14Data2.MainScrewP_L, F14Data2.MainAxle_L, F14Data2.MainRetractQ_L, F14Data2.MainSlide_L);
        GearLeg014("R", F14Data2.MainScrewP_R, F14Data2.MainAxle_R, F14Data2.MainRetractQ_R, F14Data2.MainSlide_R);
        var gh = F("gearhinge_F"); gh.position = F14Data.NoseTrunnion; gh.rotation = Quaternion.identity;
        var g = F("gearhinge_F/gear_F"); g.position = F14Data.NoseTrunnion; g.rotation = Quaternion.identity;
        var bump = F("gearhinge_F/gear_F/bumpstop");
        bump.position = F14Data2.NoseAxle + new Vector3(0, NoseCastOffset, 0); bump.rotation = Quaternion.identity;
        var uns = F("gearhinge_F/gear_F/gear_unsprung_F"); uns.position = F14Data2.NoseAxle; uns.rotation = Quaternion.identity;
        var w = F("gearhinge_F/gear_F/gear_unsprung_F/wheel_F"); w.position = F14Data2.NoseAxle; w.rotation = Quaternion.identity;
        P("gearhinge_F/gear_F/gearlight_F", F14Data2.NoseAxle + new Vector3(0, 0.8f, 0.3f));
        Attach("F14_NoseStrut", g);
        Attach("F14_NoseFork", uns);
        Attach("F14_NoseWheel", w);
        var lg = C(g, "LandingGear");
        SetF(lg, "foldDegrees", -90f);
        SetV(lg, "hingeFoldMotion", Vector3.zero);
        SetF(lg, "wheelRadius", 0.28f);
        SetF(lg, "suspensionTravel", 0.45f);
        SetF(lg, "springRate", 4.5e5f);      // 0.1.3: carrier-rated (broke at 215 kN in the 0.1.2 test)
        SetF(lg, "dampingRate", 9.0e4f);      // 0.1.5: was 5e4, nose porpoised on braking
        SetF(lg, "maxCompression", 0.42f);
        SetF(lg, "mass", 150f);
        SetF(lg, "contactArea", 0.04f);
        SetO(lg, "castPoint", bump);
        var er = C("wingroot_R/engines_R/nozzles_R/elevator_R", "AeroPart");
        var eso = new SerializedObject(er); var ej = eso.FindProperty("joints");
        for (int i = 0; i < ej.arraySize; i++) { var cp = ej.GetArrayElementAtIndex(i).FindPropertyRelative("connectedPart"); if (cp.objectReferenceValue && cp.objectReferenceValue.name == "engines_L") cp.objectReferenceValue = C("wingroot_R/engines_R", "AeroPart"); }
        eso.ApplyModifiedPropertiesWithoutUndo();
        // nose door leaves (two halves, outer-edge hinges) and the actuator slot strip
        var ff = "fuselage_F/fuselage_FF";
        var lL = MakeDoor("F14_NoseLeaf_L", ff, F14Data.NoseLeafP_L, "F14_NoseLeaf_L");
        var lR = MakeDoor("F14_NoseLeaf_R", ff, F14Data.NoseLeafP_R, "F14_NoseLeaf_R");
        var sL = MakeDoor("F14_NoseStrip_L", ff, F14Data.NoseStripP_L, "F14_NoseStrip_L");
        var sR = MakeDoor("F14_NoseStrip_R", ff, F14Data.NoseStripP_R, "F14_NoseStrip_R");
        // actuator piston: cylinder on the airframe, rod on the strut, each kept pointing at the other (game GearPart LookAt)
        var cyl = new GameObject("F14_NosePistonCyl_pivot").transform; cyl.SetParent(F(ff), false); cyl.position = F14Data.NosePistonCylP;
        var rod = new GameObject("F14_NosePistonRod_pivot").transform; rod.SetParent(g, false); rod.position = F14Data.NosePistonRodP;
        cyl.gameObject.layer = cyl.parent.gameObject.layer; rod.gameObject.layer = g.gameObject.layer;
        cyl.rotation = Quaternion.LookRotation(rod.position - cyl.position, Vector3.up);
        rod.rotation = Quaternion.LookRotation(cyl.position - rod.position, Vector3.up);
        Attach("F14_NosePistonCyl", cyl); Attach("F14_NosePistonRod", rod);
        var so = new SerializedObject(lg);
        var mp = so.FindProperty("movingParts"); mp.arraySize = 2;
        mp.GetArrayElementAtIndex(0).FindPropertyRelative("transform").objectReferenceValue = cyl;
        mp.GetArrayElementAtIndex(0).FindPropertyRelative("target").objectReferenceValue = rod;
        mp.GetArrayElementAtIndex(0).FindPropertyRelative("foldAngles").vector3Value = Vector3.zero;
        mp.GetArrayElementAtIndex(1).FindPropertyRelative("transform").objectReferenceValue = rod;
        mp.GetArrayElementAtIndex(1).FindPropertyRelative("target").objectReferenceValue = cyl;
        mp.GetArrayElementAtIndex(1).FindPropertyRelative("foldAngles").vector3Value = Vector3.zero;
        var doors = so.FindProperty("gearDoors");
        for (int i = 0; i < doors.arraySize; i++)
        {
            var d = doors.GetArrayElementAtIndex(i);
            var tname = (d.FindPropertyRelative("transform").objectReferenceValue as Transform).name;
            bool left = tname.Contains("FL");
            d.FindPropertyRelative("closedAngle").vector3Value = new Vector3(0, 0, left ? 90f : -90f);
            d.FindPropertyRelative("openAngle").vector3Value = Vector3.zero;
        }
        foreach (var d in new[] { lL, lR, sL, sR }) { var op = DoorOpen(d, 88f); AddDoor(doors, d, op); L("nose door " + d.name + " open " + op); }
        so.ApplyModifiedPropertiesWithoutUndo();
        var bc = g.GetComponent<BoxCollider>();
        if (bc) { bc.center = g.InverseTransformPoint((F14Data.NoseTrunnion + F14Data.NoseAxle) * 0.5f + new Vector3(0, 0.2f, 0)); bc.size = new Vector3(0.3f, 1.0f, 0.3f); }
        L("gear done");
    }

    static void Controls()
    {
        var sw = C(R, "SwingWingController");
        var so = new SerializedObject(sw);
        var rot = so.FindProperty("rotators");
        for (int i = 0; i < rot.arraySize; i++)
        {
            var r = rot.GetArrayElementAtIndex(i);
            r.FindPropertyRelative("maxAngle").floatValue = 48f;
            r.FindPropertyRelative("rotationSpeed").floatValue = 18f;   // 0.1.5: keeps up with hard decelerations
            var sa = r.FindPropertyRelative("sweepAeroProperties");
            float[] area = { -3.2f, -3.2f, -0.8f };
            for (int k = 0; k < sa.arraySize && k < 3; k++)
            {
                sa.GetArrayElementAtIndex(k).FindPropertyRelative("swingAreaChange").floatValue = area[k];
                sa.GetArrayElementAtIndex(k).FindPropertyRelative("swingDragChange").floatValue = -0.15f;
            }
        }
        // 0.1.4: F-14 schedule (wings forward to M0.7, fully aft at ~M1.0; the real CADC is 50/60/68 deg at M0.8/0.9/1.0)
        so.FindProperty("forwardMach").floatValue = 0.70f;
        so.FindProperty("sweptMach").floatValue = 0.98f;
        so.FindProperty("showHUDStatus").boolValue = true;
        so.FindProperty("displayAngleMin").floatValue = 20f;
        so.FindProperty("displayAngleMax").floatValue = 68f;
        // roll spoilers stay active up to 57 deg sweep (F14B.Systems locks them above); the game would lock them as soon as the wings move
        so.FindProperty("lockingControlSurfaces").arraySize = 0;
        so.ApplyModifiedPropertiesWithoutUndo();
        var fold = C("fuselage_R", "SwingWingController");
        var fso = new SerializedObject(fold); var fr = fso.FindProperty("rotators");
        for (int i = 0; i < fr.arraySize; i++) fr.GetArrayElementAtIndex(i).FindPropertyRelative("maxAngle").floatValue = 0f;
        fso.ApplyModifiedPropertiesWithoutUndo();

        foreach (var s in new[] { "L", "R" })
        {
            float sg = s == "L" ? -1 : 1;
            var ail = C("wingroot_" + s + "/wing1_" + s + "/wing2_" + s + "/aileron_" + s, "ControlSurface");
            SetF(ail, "rollRange", sg * 25f);
            var el = C("wingroot_" + s + "/engines_" + s + "/nozzles_" + s + "/elevator_" + s, "ControlSurface");
            SetF(el, "pitchRange", 24f); SetF(el, "rollRange", -sg * 12f); SetF(el, "servoSpeed", 90f);
            var rd = C("wingroot_" + s + "/engines_" + s + "/rudder_" + s, "ControlSurface");
            float yr = GetF(rd, "yawRange"); SetF(rd, "yawRange", Mathf.Sign(yr) * 30f);
            var wt = C("wingroot_" + s + "/wing1_" + s + "/wing2_" + s + "/wingtip_" + s, "ControlSurface");
            SetF(wt, "yawSplitFactor", 0f); SetF(wt, "maxSplit", 60f); SetF(wt, "splitDrag", 0.016f);   // 0.1.5: was 0.08 (~4.8 m2 per side, far too strong)
            var hld = C("wingroot_" + s + "/wing1_" + s + "/flap_" + s, "HighLiftDevice");
            SetF(hld, "speedDeployed", 90f); SetF(hld, "speedRetracted", 130f);   // 0.1.5: full flaps through rotation SetF(hld, "deployedArea", 6f);
            var hso = new SerializedObject(hld); var mp = hso.FindProperty("movingParts");
            mp.GetArrayElementAtIndex(1).FindPropertyRelative("anglesDeployed").vector3Value = new Vector3(-35f, 0, 0);
            hso.ApplyModifiedPropertiesWithoutUndo();
        }
        var cf = C("fuselage_F/fuselage_FF/cockpit", "ControlsFilter");
        SetF(cf, "minSpeed", 5f);
        SetF(cf, "flyByWire.gLimitPositive", 8.0f);
        SetF(cf, "flyByWire.cornerSpeed", 160f);
        SetF(cf, "flyByWire.postStallManeuverSpeed", 200f);
        SetF(cf, "flyByWire.maxRollSpeed", 250f);
        SetF(cf, "flyByWire.takeoffSpeed", 55f);
        SetF(cf, "flyByWire.pidTransitionSpeed", 160f);
        SetF(cf, "flyByWire.maxPitchAngularVel", 0.56f);
        SetF(cf, "flyByWire.maxRollAngularVel", 4.5f);
        SetF(cf, "flyByWire.alphaLimiter", 27f);
        SetF(cf, "flyByWire.alphaLimiterStrength", 0.1f);
        SetF(cf, "flyByWire.pFactorFast", 5.5f);   // 0.1.5: lower loop gains, less energy into the airframe
        SetF(cf, "flyByWire.iFactor", 0f);
        SetF(cf, "flyByWire.dFactorFast", 1.6f);
        SetF(cf, "flyByWire.yawTightness", 2.5f);
        SetF(cf, "flyByWire.yawWeathervaning", 0f);
        SetF(cf, "flyByWire.rollTightness", 0.6f);
        var so2 = new SerializedObject(cf); so2.FindProperty("flightAssistName").stringValue = "Stability Assist"; so2.ApplyModifiedPropertiesWithoutUndo();
        L("controls done");
    }

    static void Engines()
    {
        foreach (var e in new[] { "wingroot_L/engines_L/engine1", "wingroot_L/engines_L/engine2", "wingroot_R/engines_R/engine3", "wingroot_R/engines_R/engine4" })
            SetF(C(e, "Turbofan"), "staticThrust", 39000f);     // 0.1.4 game feel: +5 % dry
        foreach (var n in new[] { "wingroot_L/engines_L/nozzles_L/nozzle1", "wingroot_L/engines_L/nozzles_L/nozzle2", "wingroot_R/engines_R/nozzles_R/nozzle3", "wingroot_R/engines_R/nozzles_R/nozzle4" })
        {
            var jn = C(n, "JetNozzle"); var so = new SerializedObject(jn);
            var ab = so.FindProperty("afterburners");
            for (int i = 0; i < ab.arraySize; i++) ab.GetArrayElementAtIndex(i).FindPropertyRelative("thrust").floatValue = 25000f;   // 256 kN total in AB
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        L("engines done");
    }

    static readonly (string path, float mass, float area, float drag, float fuel)[] PartTable =
    {
        ("", 840, 8, 0.15f, 1500), ("fuselage_F", 1360, 2.5f, 0.1f, 2000), ("fuselage_F/fuselage_FF", 1100, 0.5f, 0.03f, 0),
        ("fuselage_F/fuselage_FF/cockpit", 1100, 0, 0.12f, 0), ("fuselage_F/fuselage_FF/nose", 650, 0, 0.03f, 0),
        ("fuselage_F/fuselage_FF/cheek_FL", 110, 0.5f, 0.02f, 0), ("fuselage_F/fuselage_FF/cheek_FR", 110, 0.5f, 0.02f, 0),
        ("fuselage_F/cheek_RL", 230, 2, 0.02f, 0), ("fuselage_F/cheek_RR", 230, 2, 0.02f, 0),
        ("fuselage_R", 1000, 3f, 0.1f, 1300), ("fuselage_R/tail", 400, 0.5f, 0.05f, 0),
        ("intake_L", 700, 0, 0.12f, 0), ("intake_R", 700, 0, 0.12f, 0),
        ("gearbay_L", 550, 0, 0.1f, 400), ("gearbay_R", 550, 0, 0.1f, 400),
        ("wingroot_L", 1600, 3.5f, 0.05f, 500), ("wingroot_R", 1600, 3.5f, 0.05f, 500),
        ("wingroot_L/wing1_L", 900, 9, 0.3f, 400), ("wingroot_R/wing1_R", 900, 9, 0.3f, 400),
        ("wingroot_L/wing1_L/wing2_L", 520, 6.5f, 0.25f, 125), ("wingroot_R/wing1_R/wing2_R", 520, 6.5f, 0.25f, 125),
        ("wingroot_L/wing1_L/wing2_L/aileron_L", 70, 2, 0, 0), ("wingroot_R/wing1_R/wing2_R/aileron_R", 70, 2, 0, 0),
        ("wingroot_L/wing1_L/wing2_L/wingtip_L", 160, 1, 0.1f, 0), ("wingroot_R/wing1_R/wing2_R/wingtip_R", 160, 1, 0.1f, 0),
        ("wingroot_L/wing1_L/flap_L", 110, 2f, 0, 0), ("wingroot_R/wing1_R/flap_R", 110, 2f, 0, 0),
        ("wingroot_L/engines_L", 800, 0, 0.08f, 0), ("wingroot_R/engines_R", 800, 0, 0.08f, 0),
        ("wingroot_L/engines_L/nozzles_L", 400, 0, 0.08f, 0), ("wingroot_R/engines_R/nozzles_R", 400, 0, 0.08f, 0),
        ("wingroot_L/engines_L/nozzles_L/elevator_L", 270, 4.5f, 0, 0), ("wingroot_R/engines_R/nozzles_R/elevator_R", 270, 4.5f, 0, 0),
        ("wingroot_L/engines_L/rudder_L", 230, 5, 0, 0), ("wingroot_R/engines_R/rudder_R", 230, 5, 0, 0),
    };

    static Transform PT(string p) => p == "" ? R : F(p);

    static void Parts()
    {
        foreach (var e in PartTable)
        {
            var t = PT(e.path); var ap = C(t, "AeroPart");
            if (!ap) { L("no AeroPart at " + e.path); continue; }
            SetF(ap, "mass", e.mass); SetF(ap, "wingArea", e.area); SetF(ap, "dragArea", e.drag);
            var ft = C(t, "FuelTank");
            if (ft) SetF(ft, "fuelCapacity", e.fuel);
            else if (e.fuel > 0) L("no FuelTank at " + e.path);
        }
        foreach (var ap in R.GetComponentsInChildren<Component>(true).Where(c => c != null && c.GetType().Name == "AeroPart"))
        {
            var p = F14Tools.Path(ap.transform, R);
            int af = p.Contains("elevator") ? 0 : 1;
            var so = new SerializedObject(ap); var x = so.FindProperty("airfoil");
            if (x.intValue >= 0) { x.intValue = af; so.ApplyModifiedPropertiesWithoutUndo(); }
        }
        L("parts done");
    }

    static void Cockpit()
    {
        string ck = "fuselage_F/fuselage_FF/cockpit";
        var h1 = F(ck + "/doorHinge_L1"); h1.position = F14Data.CanopyP; h1.rotation = Quaternion.identity;
        var h1r = F(ck + "/doorHinge_R1"); h1r.position = F14Data.CanopyP; h1r.rotation = Quaternion.identity;
        var door = F(ck + "/doorHinge_L1/doorHinge_L2/door_L");
        var cm = door.Find("F14_Canopy"); if (cm) { var src = model.transform.Find("F14_Canopy"); cm.position = src.position; cm.rotation = src.rotation; }
        var can = C(door, "Canopy"); var so = new SerializedObject(can);
        var hs = so.FindProperty("canopyHinges"); hs.arraySize = 1;
        hs.GetArrayElementAtIndex(0).FindPropertyRelative("hingeAngle").floatValue = -38f;
        so.FindProperty("glassRenderers").arraySize = 0;
        so.ApplyModifiedPropertiesWithoutUndo();
        Seat(ck + "/Pilot", ck + "/Pilot/pilot/pilot_armature/pelvis/chest/neck/head/helmetCamPoint", new Vector3(0, 1.35f, 7.10f));   // 0.1.5: eyes just ahead of the model seat headrests (were behind them)
        Seat(ck + "/WSO", ck + "/WSO/pilot/pilot_armature/pelvis/chest/neck/head/helmetCamPoint2", new Vector3(0, 1.39f, 5.45f));   // 0.1.6: RIO 11 cm lower, helmet was touching the canopy
        L("cockpit done");
    }
    static void Seat(string seat, string eye, Vector3 eyeTarget)
    {
        var s = F(seat); var e = F(eye); if (!s || !e) return;
        s.position += eyeTarget - e.position;
    }

    // AAM = guided missile mainly for air targets; BOMB = bomb / glide bomb; OTHER = tanks, pods, guns, AG missiles
    static string WeaponKind(UnityEngine.Object mount)
    {
        if (mount == null) return "OTHER";
        var so = new SerializedObject(mount); var ip = so.FindProperty("info");
        var info = ip != null ? ip.objectReferenceValue : null; if (info == null) return "OTHER";
        var io = new SerializedObject(info);
        bool missile = io.FindProperty("missile").boolValue, bomb = io.FindProperty("bomb").boolValue, glide = io.FindProperty("glideBomb").boolValue, gun = io.FindProperty("gun").boolValue;
        float aa = io.FindProperty("effectiveness.antiAir").floatValue, ag = io.FindProperty("effectiveness.antiSurface").floatValue;
        if (missile && !gun && aa > ag) return "AAM";
        if (bomb || glide) return "BOMB";
        return "OTHER";
    }

    static void Weapons()
    {
        var wm = C("fuselage_F/fuselage_FF/cockpit", "WeaponManager");
        var hornet = PrefabUtility.LoadPrefabContents(HornetPrefab);
        try
        {
            var hwm = hornet.GetComponentsInChildren<Component>(true).First(c => c != null && c.GetType().Name == "WeaponManager");
            var hso = new SerializedObject(hwm); var hsets = hso.FindProperty("hardpointSets");
            Func<int, UnityEngine.Object[]> opts = i => { var a = hsets.GetArrayElementAtIndex(i).FindPropertyRelative("weaponOptions"); return Enumerable.Range(0, a.arraySize).Select(k => a.GetArrayElementAtIndex(k).objectReferenceValue).ToArray(); };
            var sets = new List<(string name, UnityEngine.Object[] o, (Transform t, Component part, int idx)[] hps)>();
            Transform HP(string name, string parent, Vector3 pos) { var g = new GameObject(name); g.transform.position = pos; g.transform.SetParent(F(parent), true); g.layer = 14; return g.transform; }
            // 0.1.3 stations from the report (missile positions in the user's model): wing gloves = AAM only, fuselage = AAM + bombs
            var cannon = HP("F14_cannon", "fuselage_F/fuselage_FF", new Vector3(-0.72f, -0.15f, 6.3f));
            var railL = HP("F14_hp_shoulder_L", "wingroot_L", new Vector3(-3.69f, 0.355f, 0.39f));
            var railR = HP("F14_hp_shoulder_R", "wingroot_R", new Vector3(3.69f, 0.355f, 0.39f));
            var gloveL = HP("F14_hp_glove_L", "wingroot_L", new Vector3(-3.31f, -0.11f, 0.80f));
            var gloveR = HP("F14_hp_glove_R", "wingroot_R", new Vector3(3.31f, -0.11f, 0.80f));
            var tf = HP("F14_hp_tunnelF", "fuselage_F", new Vector3(0f, -0.52f, 2.78f));
            var trL = HP("F14_hp_tunnelR_L", "fuselage_R", new Vector3(-0.37f, -0.33f, -1.61f));
            var trR = HP("F14_hp_tunnelR_R", "fuselage_R", new Vector3(0.37f, -0.33f, -1.61f));
            Component AP(string p) => C(PT(p), "AeroPart");
            UnityEngine.Object[] U(params int[] ix) => ix.SelectMany(i => opts(i)).Where(o => o != null).Distinct().ToArray();
            var aam = new Func<UnityEngine.Object, bool>(o => WeaponKind(o) == "AAM");
            var aamBomb = new Func<UnityEngine.Object, bool>(o => { var k = WeaponKind(o); return k == "AAM" || k == "BOMB"; });
            var shoulderOpts = U(1).Where(aam).ToArray();
            var gloveOpts = U(1, 2, 3, 4).Where(aam).ToArray();
            var fusOpts = U(3, 4, 5).Where(aamBomb).ToArray();
            L("options: shoulder " + string.Join(",", shoulderOpts.Select(o => o.name)) + " | glove " + string.Join(",", gloveOpts.Select(o => o.name)) + " | fuselage " + string.Join(",", fusOpts.Select(o => o.name)));
            WeaponFixups(railL, railR, tf, shoulderOpts.FirstOrDefault());
            sets.Add(("Internal Cannon", opts(0), new[] { (cannon, AP("fuselage_F/fuselage_FF"), 3) }));
            sets.Add(("Glove Shoulder Rails", shoulderOpts, new[] { (railL, AP("wingroot_L"), 5), (railR, AP("wingroot_R"), 0) }));
            sets.Add(("Glove Pylons", gloveOpts, new[] { (gloveL, AP("wingroot_L"), 4), (gloveR, AP("wingroot_R"), 1) }));
            sets.Add(("Tunnel Front", fusOpts, new[] { (tf, AP("fuselage_F"), 2) }));
            sets.Add(("Tunnel Rear", fusOpts, new[] { (trL, AP("fuselage_R"), 4), (trR, AP("fuselage_R"), 1) }));
            var so = new SerializedObject(wm); var arr = so.FindProperty("hardpointSets");
            arr.arraySize = sets.Count;
            for (int i = 0; i < sets.Count; i++)
            {
                var e = arr.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("name").stringValue = sets[i].name;
                e.FindPropertyRelative("precludingHardpointSets").arraySize = 0;
                e.FindPropertyRelative("SymmetryWithPrev").boolValue = false;
                e.FindPropertyRelative("SymmetryName").stringValue = "";
                e.FindPropertyRelative("weaponMount").objectReferenceValue = null;
                var wo = e.FindPropertyRelative("weaponOptions"); wo.arraySize = sets[i].o.Length;
                for (int k = 0; k < sets[i].o.Length; k++) wo.GetArrayElementAtIndex(k).objectReferenceValue = sets[i].o[k];
                var hp = e.FindPropertyRelative("hardpoints"); hp.arraySize = sets[i].hps.Length;
                for (int k = 0; k < sets[i].hps.Length; k++)
                {
                    var h = hp.GetArrayElementAtIndex(k);
                    h.FindPropertyRelative("transform").objectReferenceValue = sets[i].hps[k].t;
                    h.FindPropertyRelative("part").objectReferenceValue = sets[i].hps[k].part;
                    h.FindPropertyRelative("bayDoors").arraySize = 0;
                    h.FindPropertyRelative("doorOpenDuration").floatValue = 0;
                    h.FindPropertyRelative("pylonOptions").arraySize = 0;
                    h.FindPropertyRelative("Pylon").objectReferenceValue = null;
                    h.FindPropertyRelative("Plug").objectReferenceValue = null;
                    h.FindPropertyRelative("BuiltInWeapons").arraySize = 0;
                    h.FindPropertyRelative("BuiltInTurrets").arraySize = 0;
                    h.FindPropertyRelative("HardpointIndex").intValue = sets[i].hps[k].idx;
                }
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            var names = new StringBuilder();
            for (int i = 0; i < hsets.arraySize; i++) names.Append(i + ":" + hsets.GetArrayElementAtIndex(i).FindPropertyRelative("name").stringValue + " ");
            L("weapons done; hornet sets " + names);
        }
        finally { PrefabUtility.UnloadPrefabContents(hornet); }
    }
}

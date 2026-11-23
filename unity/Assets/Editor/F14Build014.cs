using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// 0.1.4 additions to the F-14B build (dev-only, never shipped).
public static partial class F14Build
{
    public const string Version = "0.1.8";

    // ground line at rest (aircraft frame): main and nose contact points with the static compression from the 0.1.3 recorder
    static float GroundY(float z)
    {
        float zm = F14Data2.MainAxle_L.z, ym = F14Data2.MainAxle_L.y + (0.5f - 0.47f) - 0.5f + 0.104f;
        float zn = F14Data2.NoseAxle.z, yn = F14Data2.NoseAxle.y + NoseCastOffset - 0.45f + 0.071f;
        return ym + (yn - ym) * (z - zm) / (zn - zm);
    }

    // ---------------------------------------------------------------- main gear: exact screw retraction
    static void GearLeg014(string s, Vector3 screwP, Vector3 axle, Quaternion q, Vector3 slide)
    {
        string gb = "gearbay_" + s, mount = gb + "/gearMount_" + s, hinge = mount + "/gearHinge_" + s, gear = hinge + "/gear_" + s;
        q.ToAngleAxis(out float ang, out Vector3 ax);
        if (ang > 180f) ang -= 360f;
        var m = F(mount); m.position = screwP; m.rotation = Quaternion.FromToRotation(Vector3.right, ax.normalized);
        var h = F(hinge); h.localPosition = Vector3.zero; h.localRotation = Quaternion.identity;
        var g = F(gear);
        var bump = F(gear + "/bumpstop");
        var uns = F(gear + "/gear_unsprung_" + s);
        var bog = F(gear + "/gear_unsprung_" + s + "/bogey");
        var wF = F(gear + "/gear_unsprung_" + s + "/bogey/wheel_" + s + "F");
        var wR = F(gear + "/gear_unsprung_" + s + "/bogey/wheel_" + s + "R");
        // the game writes strutRotationTransform.localEulerAngles = (0, a, 0), so it needs a world-aligned parent under the (axis-aligned) hinge
        var al = new GameObject("F14_GearAlign_" + s).transform;
        al.SetParent(h, false); al.position = screwP; al.rotation = Quaternion.identity; al.gameObject.layer = h.gameObject.layer;
        var sr = new GameObject("F14_StrutRot_" + s).transform;
        sr.SetParent(al, false); sr.localPosition = Vector3.zero; sr.localRotation = Quaternion.identity; sr.gameObject.layer = h.gameObject.layer;
        g.SetParent(sr, true); g.position = screwP; g.rotation = Quaternion.identity;
        bump.position = axle + new Vector3(0, 0.5f - 0.47f, 0); bump.rotation = Quaternion.identity;   // travel - wheel radius
        uns.position = axle; uns.rotation = Quaternion.identity;
        bog.position = axle; bog.rotation = Quaternion.identity;
        foreach (var w in new[] { wF, wR }) { w.localPosition = Vector3.zero; w.localRotation = Quaternion.identity; }
        Attach("F14_MainStrut_" + s, sr);
        Attach("F14_MainFork_" + s, uns);
        Attach("F14_MainWheel_" + s, wF);
        var lg = C(g, "LandingGear");
        SetF(lg, "foldDegrees", ang);
        SetV(lg, "hingeFoldMotion", m.InverseTransformVector(slide));
        SetO(lg, "strutRotationTransform", sr);
        SetF(lg, "strutRotation", 0f);
        SetO(lg, "castPoint", bump);
        SetF(lg, "wheelRadius", 0.47f);
        SetF(lg, "suspensionTravel", 0.5f);
        SetF(lg, "springRate", 1.2e6f);
        SetF(lg, "dampingRate", 1.0e5f);
        SetF(lg, "maxCompression", 0.45f);
        SetF(lg, "contactArea", 0.09f);      // 0.1.4: was 0.035 (AB-4); soft-ground drag scales with pressure^2 and broke the left wheel on grass
        SetF(lg, "mass", 300f);
        var so = new SerializedObject(lg); var gd = so.FindProperty("gearDoors"); gd.arraySize = 0;
        var fd = MakeDoor("F14_MainFwdDoor_" + s, gb, s == "L" ? F14Data.MainFwdDoorP_L : F14Data.MainFwdDoorP_R, "F14_MainFwdDoor_" + s);
        var op = DoorOpen(fd, 67f); AddDoor(gd, fd, op);
        var ad = MakeDoor("F14_MainAftDoor_" + s, gb, s == "L" ? F14Data2.MainAftDoorP_L : F14Data2.MainAftDoorP_R, "F14_MainAftDoor_" + s);
        var opa = DoorOpen(ad, 59f); AddDoor(gd, ad, opa);
        so.ApplyModifiedPropertiesWithoutUndo();
        var bc = g.GetComponent<BoxCollider>();
        if (bc) { bc.center = g.InverseTransformPoint(new Vector3((screwP.x + axle.x) * 0.5f, (screwP.y + axle.y) * 0.5f, (screwP.z + axle.z) * 0.5f)); bc.size = new Vector3(0.3f, Mathf.Abs(screwP.y - axle.y) * 0.6f, 0.3f); }
        // check: where the screw puts the wheel when stowed
        var stowed = q * (axle - screwP) + screwP + slide;
        L("main " + s + " screw fold " + ang.ToString("F1") + " about " + ax.ToString("F3") + " slide " + slide.ToString("F3") + " -> stowed wheel " + stowed.ToString("F3") + " doors fwd " + op + " aft " + opa);
    }

    // ---------------------------------------------------------------- spoilers (visual; driven by F14B.Systems)
    static void Spoilers()
    {
        foreach (var s in new[] { "L", "R" })
        {
            var w1 = F("wingroot_" + s + "/wing1_" + s);
            var src = model.transform.Find("F14_Spoiler_" + s);
            if (!src) { L("no spoiler mesh " + s); continue; }
            var c = src.GetComponent<MeshRenderer>().bounds.center;
            var h = new GameObject("F14_SpoilerHinge_" + s).transform; h.SetParent(w1, false); h.gameObject.layer = w1.gameObject.layer;
            h.position = s == "L" ? F14Data2.SpoilerP_L : F14Data2.SpoilerP_R; h.rotation = Quaternion.identity;
            AlignX(h, s == "L" ? F14Data2.SpoilerD_L : F14Data2.SpoilerD_R);
            var t = h.position + Quaternion.AngleAxis(10f, h.right) * (c - h.position);
            if (t.y < c.y) h.rotation = Quaternion.AngleAxis(180f, h.up) * h.rotation;   // +X rotation must lift the trailing edge
            Attach("F14_Spoiler_" + s, h);
            L("spoiler " + s + " hinge " + h.position.ToString("F2") + " right " + h.right.ToString("F2"));
        }
    }

    // ---------------------------------------------------------------- 0.1.8 slats (visual; driven by F14B.Systems)
    // Hinge line 0.35 m below the slat trailing edge (F14Data3); +X rotation = leading edge down, slat forward (slot opens).
    static void Slats()
    {
        foreach (var s in new[] { "L", "R" })
        {
            var w1 = F("wingroot_" + s + "/wing1_" + s);
            var src = model.transform.Find("F14_Slat_" + s);
            if (!src) { L("no slat mesh " + s); continue; }
            var h = new GameObject("F14_SlatHinge_" + s).transform; h.SetParent(w1, false); h.gameObject.layer = w1.gameObject.layer;
            h.position = s == "L" ? F14Data3.SlatP_L : F14Data3.SlatP_R; h.rotation = Quaternion.identity;
            AlignX(h, s == "L" ? F14Data3.SlatD_L : F14Data3.SlatD_R);
            var le = s == "L" ? F14Data3.SlatLE_L : F14Data3.SlatLE_R;
            var t = h.position + Quaternion.AngleAxis(10f, h.right) * (le - h.position);
            if (t.y > le.y) h.rotation = Quaternion.AngleAxis(180f, h.up) * h.rotation;   // +X rotation must lower the leading edge
            Attach("F14_Slat_" + s, h);
            var t2 = h.position + Quaternion.AngleAxis(17f, h.right) * (le - h.position);
            L("slat " + s + " hinge " + h.position.ToString("F3") + " right " + h.right.ToString("F3") + " LE at 17 deg moves " + (t2 - le).ToString("F3"));
        }
    }

    // ---------------------------------------------------------------- rudders: rigid joints instead of the AB-4 tail-fold rotators (they flexed = shaking)
    static void RudderJoints()
    {
        var fold = C("fuselage_R", "SwingWingController");
        if (fold)
        {
            var fso = new SerializedObject(fold);
            fso.FindProperty("rotators").arraySize = 0;
            fso.FindProperty("lockingControlSurfaces").arraySize = 0;
            fso.FindProperty("showHUDStatus").boolValue = false;
            fso.ApplyModifiedPropertiesWithoutUndo();
        }
        UnityEngine.Object snd = null;
        var en0 = C("wingroot_L/engines_L", "AeroPart");
        var ej = new SerializedObject(en0).FindProperty("joints");
        if (ej.arraySize > 0) snd = ej.GetArrayElementAtIndex(0).FindPropertyRelative("breakSound").objectReferenceValue;
        foreach (var s in new[] { "L", "R" })
        {
            var rd = C("wingroot_" + s + "/engines_" + s + "/rudder_" + s, "AeroPart");
            var en = C("wingroot_" + s + "/engines_" + s, "AeroPart");
            var so = new SerializedObject(rd); var j = so.FindProperty("joints"); j.arraySize = 1;
            var e = j.GetArrayElementAtIndex(0);
            e.FindPropertyRelative("connectedPart").objectReferenceValue = en;
            e.FindPropertyRelative("tensor").objectReferenceValue = null;
            e.FindPropertyRelative("solverIterations").intValue = 12;
            e.FindPropertyRelative("breakForce").floatValue = 4e5f;
            e.FindPropertyRelative("breakTorque").floatValue = 4e5f;
            e.FindPropertyRelative("anchor").objectReferenceValue = null;
            e.FindPropertyRelative("breakSound").objectReferenceValue = snd;
            e.FindPropertyRelative("joint").objectReferenceValue = null;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        L("rudders: fixed joints to the nacelles, tail-fold rotators removed");
    }

    // ---------------------------------------------------------------- nav lights: AB-4 light housings removed, glow on the F-14's own light
    static void NavLights()
    {
        foreach (var s in new[] { "L", "R" })
        {
            string wt = "wingroot_" + s + "/wing1_" + s + "/wing2_" + s + "/wingtip_" + s;
            var nl = F(wt + "/navlight_" + s); if (!nl) continue;
            var p = s == "L" ? F14Data2.NavLight_L : F14Data2.NavLight_R;
            var fx = F(wt + "/navlight_" + s + "/navlight_" + s + "_effects");
            var fxw = fx ? fx.position : Vector3.zero;
            nl.position = p;
            if (fx) fx.position = p + new Vector3(s == "L" ? -0.03f : 0.03f, 0, 0);
            var mf = nl.GetComponent<MeshFilter>(); if (mf) mf.sharedMesh = null;
            var mr = nl.GetComponent<MeshRenderer>(); if (mr) mr.enabled = false;
            L("navlight " + s + " at " + p.ToString("F3") + " (effects was " + fxw.ToString("F3") + ")");
        }
    }

    // ---------------------------------------------------------------- boarding ladder on the ground, against the left cockpit sill
    static void Ladder()
    {
        var l = F("ladder"); if (!l) return;
        var mf = l.GetComponent<MeshFilter>(); if (!mf || !mf.sharedMesh) return;
        var b = mf.sharedMesh.bounds;
        Bounds W() { var wb = new Bounds(l.TransformPoint(b.center), Vector3.zero); for (int i = 0; i < 8; i++) wb.Encapsulate(l.TransformPoint(b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)))); return wb; }
        var wb0 = W();
        float z = 5.2f, gy = GroundY(z);
        l.position += new Vector3(0, gy - wb0.min.y, 0);
        var wb1 = W(); float top = wb1.max.y;
        var body = WorldVerts("F14_Body");
        float side = 0f; foreach (var v in body) if (v.x < 0 && Mathf.Abs(v.z - z) < 0.3f && Mathf.Abs(v.y - top) < 0.25f) side = Mathf.Max(side, -v.x);
        if (side < 0.3f) side = 0.9f;
        l.position += new Vector3(-side - 0.04f - wb1.max.x, 0, 0);
        var wb2 = W();
        L("ladder: ground " + gy.ToString("F3") + " height " + wb2.size.y.ToString("F2") + " fuselage side " + side.ToString("F2") + " bounds " + wb2.min.ToString("F2") + ".." + wb2.max.ToString("F2"));
    }

    // ---------------------------------------------------------------- afterburner: one plume per nacelle at the F-14 nozzle exit, sized to it
    static void NozzleFX()
    {
        var body = WorldVerts("F14_Body");
        foreach (var (s, sx, n1, n2) in new[] { ("L", -1f, "1", "2"), ("R", 1f, "3", "4") })
        {
            var pts = body.Where(v => v.x * sx > 0.8f && v.x * sx < 2.3f && v.z < -6.0f && v.y > -1.0f && v.y < 1.0f).ToArray();
            if (pts.Length < 20) { L("nozzle " + s + " no verts"); continue; }
            float zmin = pts.Min(v => v.z);
            var ring = pts.Where(v => v.z < zmin + 0.12f).ToArray();
            var c = Vector3.zero; foreach (var v in ring) c += v; c /= ring.Length;
            float r = ring.Select(v => new Vector2(v.x - c.x, v.y - c.y).magnitude).OrderBy(x => x).ElementAt((int)(ring.Length * 0.8f));
            string nz = "wingroot_" + s + "/engines_" + s + "/nozzles_" + s;
            var noz = F(nz + "/nozzle" + n1);
            var glow = F(nz + "/nozzle" + n1 + "/afterburner" + n1 + "_nozzle_glow");
            var flame = F(nz + "/nozzle" + n1 + "/afterburner" + n1 + "_nozzle_glow/afterburner" + n1 + "_flame");
            noz.position = new Vector3(c.x, c.y, zmin + 0.02f);
            float ex = 0.35f, ey = 0.35f;
            var gmf = glow ? glow.GetComponent<MeshFilter>() : null;
            if (gmf && gmf.sharedMesh) { var e = gmf.sharedMesh.bounds.extents; ex = Mathf.Max(0.05f, e.x); ey = Mathf.Max(0.05f, e.y); }
            float kx = r * 0.85f / ex, ky = r * 0.85f / ey, k = (kx + ky) * 0.5f; float rab = Mathf.Max(ex, ey);
            if (glow) { glow.localPosition = new Vector3(0, 0, 0.30f); glow.localRotation = Quaternion.identity; glow.localScale = new Vector3(kx, ky, k); }
            if (flame) flame.localScale = new Vector3(1f, 1f, 1.7f);
            if (flame) { flame.localPosition = Vector3.zero; flame.localScale = new Vector3(1f, 1f, 1.7f); }
            var jn = C(noz, "JetNozzle"); var so = new SerializedObject(jn);
            so.FindProperty("heatHaze.sizeMin").floatValue *= k; so.FindProperty("heatHaze.sizeMax").floatValue *= k;
            so.ApplyModifiedPropertiesWithoutUndo();
            // the second AB-4 nozzle of the nacelle stays at the same point with its visuals hidden
            var noz2 = F(nz + "/nozzle" + n2); if (noz2) noz2.position = noz.position;
            var g2 = F(nz + "/nozzle" + n2 + "/afterburner" + n2 + "_nozzle_glow"); if (g2) g2.localScale = Vector3.one * 0.001f;
            var hz2 = F(nz + "/nozzle" + n2 + "/heat_haze_" + n2); if (hz2) hz2.gameObject.SetActive(false);
            L("nozzle " + s + " exit " + noz.position.ToString("F3") + " radius " + r.ToString("F3") + " AB-4 glow extents " + ex.ToString("F3") + "/" + ey.ToString("F3") + " scale " + kx.ToString("F2") + "/" + ky.ToString("F2"));
        }
    }

    // ---------------------------------------------------------------- ejection: canopy jettison then individual seats (no AB-4 escape capsule)
    static void Ejection()
    {
        string ck = "fuselage_F/fuselage_FF/cockpit";
        var cap = C(ck, "EscapeCapsule"); if (cap) UnityEngine.Object.DestroyImmediate(cap);
        foreach (var n in new[] { "EjectFlame", "EjectFlame", "EjectSmoke", "Parachute", "DrogueChute" }) { var t = F(ck + "/" + n); if (t) UnityEngine.Object.DestroyImmediate(t.gameObject); }
        foreach (var p in new[] { ck + "/Pilot", ck + "/WSO" })
        {
            var pc = C(p, "Pilot"); var so = new SerializedObject(pc);
            so.FindProperty("ejectionSeat").boolValue = true;
            so.FindProperty("exitDirection").enumValueIndex = 0;
            var ec = so.FindProperty("escapeCapsule"); if (ec != null) ec.boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();
            var seat = F(p + "/EjectionSeat"); if (seat) { var smf = seat.GetComponent<MeshFilter>(); if (smf) smf.sharedMesh = null; var smr = seat.GetComponent<MeshRenderer>(); if (smr) smr.enabled = false; }
        }
        var door = F(ck + "/doorHinge_L1/doorHinge_L2/door_L");
        var can = C(door, "Canopy"); var cso = new SerializedObject(can);
        var cm = door.Find("F14_Canopy");
        var col = door.GetComponent<BoxCollider>(); if (col == null) col = door.gameObject.AddComponent<BoxCollider>();
        if (cm) { var mb = cm.GetComponent<MeshFilter>().sharedMesh.bounds; var wc = cm.TransformPoint(mb.center); col.center = door.InverseTransformPoint(wc); col.size = Vector3.Scale(mb.size, new Vector3(0.9f, 0.6f, 0.9f)); }
        cso.FindProperty("ejectionTransform").objectReferenceValue = door;
        cso.FindProperty("mass").floatValue = 80f;
        cso.FindProperty("ejectionCollider").objectReferenceValue = col;
        cso.FindProperty("ejectionForce").vector3Value = new Vector3(0, 6000f, 0);
        cso.FindProperty("forcePosition").vector3Value = new Vector3(0, 0, 0.3f);
        cso.FindProperty("fireTime").floatValue = 0.25f;
        var snd = AssetDatabase.FindAssets("canopyJettison_PLACEHOLDER").Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<AudioClip>).FirstOrDefault(x => x != null);
        if (snd) cso.FindProperty("ejectSound").objectReferenceValue = snd;
        cso.ApplyModifiedPropertiesWithoutUndo();
        L("ejection: capsule removed, seats on, canopy jettison (sound " + (snd ? snd.name : "none") + ")");
    }

    // ---------------------------------------------------------------- tail hook (game TailHook component, like the Hornet)
    static void TailHookSetup()
    {
        var src = model.transform.Find("F14_Hook"); if (!src) { L("no hook mesh"); return; }
        var hk = new GameObject("F14B_TailHook").transform; hk.SetParent(R, false); hk.position = F14Data.HookP; hk.rotation = Quaternion.identity; hk.gameObject.layer = 0;
        var arm = Attach("F14_Hook", hk, "F14B_TailHook_Arm"); arm.gameObject.layer = 0;
        var v = arm.GetComponent<MeshFilter>().sharedMesh.vertices; Vector3 tip = hk.position; float best = 0;
        foreach (var x in v) { var w = arm.TransformPoint(x); float d = (w - hk.position).sqrMagnitude; if (d > best) { best = d; tip = w; } }
        var end = new GameObject("F14B_TailHook_End").transform; end.SetParent(arm, false); end.position = tip; end.gameObject.layer = 0;
        var cast = new GameObject("F14B_TailHook_Cast").transform; cast.SetParent(arm, false); cast.position = hk.position; cast.rotation = Quaternion.LookRotation(tip - hk.position, Vector3.up); cast.gameObject.layer = 0;
        var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("TailHook")).FirstOrDefault(t => t != null);
        var th = hk.gameObject.AddComponent(type);
        // deployed angle: tip ~5 cm above the ground line in level attitude (it touches at landing attitude)
        var rel = tip - hk.position; float target = GroundY(tip.z) + 0.05f; float pick = -50f; float err = 1e9f;
        for (float a = -89f; a <= 89f; a += 0.5f)
        {
            var p = hk.position + Quaternion.Euler(a, 0, 0) * rel;
            if (p.y > hk.position.y - 0.3f) continue;
            float e = Mathf.Abs(p.y - GroundY(p.z) - 0.05f);
            if (e < err) { err = e; pick = a; }
        }
        var so = new SerializedObject(th);
        so.FindProperty("hinge").objectReferenceValue = hk; so.FindProperty("hook").objectReferenceValue = arm;
        so.FindProperty("castPoint").objectReferenceValue = cast; so.FindProperty("hookEnd").objectReferenceValue = end;
        so.FindProperty("mass").floatValue = 40f; so.FindProperty("stowedAngle").floatValue = 0f; so.FindProperty("deployedAngle").floatValue = pick;
        so.ApplyModifiedPropertiesWithoutUndo();
        L("tail hook: hinge " + hk.position.ToString("F3") + " tip " + tip.ToString("F3") + " length " + rel.magnitude.ToString("F2") + " deployed " + pick + " (err " + err.ToString("F3") + ", target y " + target.ToString("F2") + ")");
    }

    // ---------------------------------------------------------------- weapons: shoulder rails turned 90 deg onto the glove pylon side, front tunnel rack up to the skin
    static float MountDepth(UnityEngine.Object mount)
    {
        try
        {
            var so = new SerializedObject(mount); var pf = so.FindProperty("prefab")?.objectReferenceValue as GameObject;
            if (!pf) return 0.2f;
            var rs = pf.GetComponentsInChildren<MeshFilter>(true).Where(m => m.sharedMesh).ToArray(); if (rs.Length == 0) return 0.2f;
            var b = new Bounds(pf.transform.InverseTransformPoint(rs[0].transform.TransformPoint(rs[0].sharedMesh.bounds.center)), Vector3.zero);
            foreach (var m in rs) { var mb = m.sharedMesh.bounds; for (int i = 0; i < 8; i++) b.Encapsulate(pf.transform.InverseTransformPoint(m.transform.TransformPoint(mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))))); }
            return Mathf.Clamp(-b.center.y, 0.08f, 0.45f);
        }
        catch { return 0.2f; }
    }
    static void WeaponFixups(Transform railL, Transform railR, Transform tunnelF, UnityEngine.Object railOpt)
    {
        float d = MountDepth(railOpt);
        // keep the missile where the model has it: the rack turns 90 deg about the fore-aft axis, its top faces the pylon (inboard)
        railL.rotation = Quaternion.Euler(0, 0, -90f); railR.rotation = Quaternion.Euler(0, 0, 90f);
        railL.position += new Vector3(d, -d, 0); railR.position += new Vector3(-d, -d, 0);
        var body = WorldVerts("F14_Body");
        var pz = tunnelF.position.z;
        var col = body.Where(v => Mathf.Abs(v.x) < 0.18f && Mathf.Abs(v.z - pz) < 0.35f && v.y < 0.2f).ToArray();
        float yb = col.Length > 0 ? col.Min(v => v.y) : tunnelF.position.y;
        float y0 = tunnelF.position.y;
        tunnelF.position = new Vector3(tunnelF.position.x, yb, pz);
        L("weapons: rail depth " + d.ToString("F2") + " rails at " + railL.position.ToString("F2") + " / " + railR.position.ToString("F2") + "; tunnel front y " + y0.ToString("F2") + " -> " + yb.ToString("F2"));
    }
}

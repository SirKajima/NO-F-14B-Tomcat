using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

// F-14B Tomcat systems for Nuclear Option (goes with the F-14B .nobp):
//  - spoilers: roll spoilers (up-only on the down-going wing, active below 57 deg sweep), ground spoilers, direct lift control
//  - roll-spoiler lock above 57 deg sweep (the invisible roll surfaces in the prefab)
//  - tail hook recognised by the game's AI/airbase logic, arrest pitch relief
//  - 0.1.5: structure assist (stiffer wing/tail joints: no flex, no 6 Hz flutter), transonic wave drag,
//           Axis Modifier + Throttle no longer latches manual wing sweep
//  - 0.1.6: carrier gear (stronger struts, nose strut stiffens while on the wire), wheel ground-ray probe so the
//           nose wheel can't sink into the deck (same fix as the Hornet; chains with the Hornet plugin's patch)
//  - 0.1.9: weapon mounts ignore collisions with the F-14's own parts (Multi-Missile spawn damage fix)
//  - 0.1.8: leading-edge slats (17 deg with the flaps, 7 deg maneuver slats by AoA, stall protection) + slat lift
namespace F14BSystems
{
    [BepInPlugin("f14b.systems", "F-14B Tomcat Systems", Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Version = "0.1.9";
        internal static BepInEx.Logging.ManualLogSource Log;
        internal static ConfigEntry<bool> RollSpoilers, GroundSpoilers, DLC, TailHookOn;
        internal static ConfigEntry<float> SpoilerMaxSweep, ArrestRelief, GroundSpoilerDrag, DlcStrength;
        internal static ConfigEntry<bool> StructureAssist, BlockModifierSweep, CarrierGear, ArrestNoseGear, GearProbe, GearDiag, SlatsOn, ManeuverSlats;
        internal static ConfigEntry<float> SlatLift;
        internal static ConfigEntry<bool> MountCollisionFix, MountCollisionLog;
        internal static ConfigEntry<int> SolverIterations;
        internal static ConfigEntry<float> JointDamping, JointStiffness, WaveDragArea;
        void Awake()
        {
            Log = Logger;
            RollSpoilers = Config.Bind("Spoilers", "RollSpoilers", true, "Spoilers rise on the down-going wing for roll while the wings are swept less than SpoilerMaxSweep (like the real F-14). Above it, roll comes from the stabilators only.");
            SpoilerMaxSweep = Config.Bind("Spoilers", "SpoilerMaxSweep", 57f, new ConfigDescription("Wing sweep (deg) above which the roll spoilers lock down.", new AcceptableValueRange<float>(20f, 68f)));
            GroundSpoilers = Config.Bind("Spoilers", "GroundSpoilers", true, "All spoilers pop up on the ground with the throttle at idle (lift dump + drag after touchdown). Pushing the throttle up retracts them, so a bolter or touch-and-go is unaffected.");
            GroundSpoilerDrag = Config.Bind("Spoilers", "GroundSpoilerDragArea", 2.0f, new ConfigDescription("Extra drag area (m^2) with ground spoilers up. Lift dump is 3x this.", new AcceptableValueRange<float>(0f, 6f)));
            DLC = Config.Bind("Spoilers", "DirectLiftControl", true, "Approach mode (gear down, flaps out, airborne): spoilers sit a little up and move with the stick, giving quick small lift changes for glideslope corrections.");
            DlcStrength = Config.Bind("Spoilers", "DLCStrength", 1.0f, new ConfigDescription("Scale of the DLC lift change (1 = about +/-0.05 g at approach speed).", new AcceptableValueRange<float>(0f, 3f)));
            TailHookOn = Config.Bind("Carrier", "TailHook", true, "Tell the game the F-14 has a tail hook (AI landing logic and carrier spawns).");
            ArrestRelief = Config.Bind("Carrier", "ArrestPitchRelief", 0.15f, new ConfigDescription("How much of the nose-down pitch from the arresting wire is removed. 0 = the full real nose-down slam onto the deck, 1 = the nose stays level. The nose strut stiffens while on the wire (ArrestNoseGear) so it survives the slam.", new AcceptableValueRange<float>(0f, 1f)));
            CarrierGear = Config.Bind("Carrier", "CarrierGear", true, "Carrier-rated struts: main gear 1.5 MN/m (was 1.2), nose 600 kN/m (was 450), more damping. For no-flare deck landings.");
            ArrestNoseGear = Config.Bind("Carrier", "ArrestNoseGear", true, "While the hook is on a wire the nose strut stiffens (1.1 MN/m) and may compress to full travel, so the nose can slam onto the deck without the gear breaking.");
            GearProbe = Config.Bind("Carrier", "GearContactProbe", true, "If a wheel's ground ray misses (deck seams, the ray starting just under a deck edge), probe again from higher up and across the twin nose wheels, so the nose wheel can't sink into the deck and lose steering.");
            GearDiag = Config.Bind("Carrier", "GearDiagnostics", true, "Write a line to the BepInEx log when the probe saves a wheel or the nose wheel loses the deck.");
            MountCollisionFix = Config.Bind("Weapons", "MountSelfCollisionFix", true, "Weapon mounts (including other mods' weapons such as Multi-Missile) never collide with this jet's own parts. Stops parts getting damaged at spawn when a mod's missile or bomb collider reaches into a neighbouring part.");
            MountCollisionLog = Config.Bind("Weapons", "MountCollisionLog", false, "Log a line for every mount the fix is applied to.");
            SlatsOn = Config.Bind("Slats", "Slats", true, "Leading-edge slats: out 17 deg with the flaps (takeoff, landing), forward and down to open a slot over the wing.");
            ManeuverSlats = Config.Bind("Slats", "ManeuverSlats", true, "In the air below Mach 0.85 with the wings at 50 deg sweep or less, the slats come out 7 deg by themselves as angle of attack rises (7.5-10.5 deg AoA), and further at very high AoA below Mach 0.5 (stall protection).");
            SlatLift = Config.Bind("Slats", "SlatLiftArea", 4f, new ConfigDescription("Extra lift area (m^2, both wings) with the slats fully out, growing with angle of attack from 8 to 16 deg (the slats keep the airflow attached). A little drag comes with it.", new AcceptableValueRange<float>(0f, 10f)));
            StructureAssist = Config.Bind("Structure", "StructureAssist", true, "Holds the wing, tail and engine sections rigid against each other (the game joins them with physics joints that bend under g and flutter at high speed). Uses equal and opposite forces, so it adds no lift, thrust or drag.");
            SolverIterations = Config.Bind("Structure", "SolverIterations", 30, new ConfigDescription("Physics solver passes for the F-14's parts (the game uses 6-12).", new AcceptableValueRange<int>(6, 60)));
            JointDamping = Config.Bind("Structure", "JointDamping", 0.25f, new ConfigDescription("Share of the relative motion between joined parts removed each physics step.", new AcceptableValueRange<float>(0f, 0.5f)));
            JointStiffness = Config.Bind("Structure", "JointStiffness", 0.08f, new ConfigDescription("Share of the bend between joined parts pulled back each physics step.", new AcceptableValueRange<float>(0f, 0.2f)));
            WaveDragArea = Config.Bind("Flight", "WaveDragArea", 0.6f, new ConfigDescription("Extra drag area (m^2) through the transonic rise (peaks M1.15-1.3, eases off at M2). Sets the top speed: about M1.2 at low level.", new AcceptableValueRange<float>(0f, 2f)));
            BlockModifierSweep = Config.Bind("Wing Sweep", "BlockModifierThrottleSweep", true, "Holding Axis Modifier while moving the throttle no longer switches the wings to manual sweep (that latched the wings swept back after using the airbrake). Custom Axis 1 still gives manual sweep.");
            var h = new Harmony("f14b.systems"); int ok = 0;
            foreach (var t in typeof(Plugin).Assembly.GetTypes())
            {
                if (t.GetCustomAttributes(typeof(HarmonyPatch), false).Length == 0) continue;
                try { h.CreateClassProcessor(t).Patch(); ok++; } catch (Exception e) { Log.LogWarning($"patch {t.Name} failed: {e.Message}"); }
            }
            Log.LogInfo($"F-14B Tomcat Systems {Version} loaded ({ok} patch groups)");
        }
        internal static bool IsF14(Aircraft a)
        {
            if (a == null) return false; var d = a.definition; return d != null && d.jsonKey == "F14B";
        }
    }

    [HarmonyPatch(typeof(Aircraft), "FixedUpdate")]
    static class Systems
    {
        class St
        {
            public bool init, f14; public float initTries;
            public Rigidbody rb; public SwingWingController sweep; public LandingGear[] gears; public HighLiftDevice flap;
            public ControlSurface[] ail = new ControlSurface[0]; public bool locked;
            public Transform[] hinge = new Transform[2]; public Quaternion[] rest = new Quaternion[2]; public float[] ang = new float[2];
            public float ground, dlc; public bool logGs;
            public List<Link> links = new List<Link>(); public float nextScan; public bool loggedLinks;
            public LandingGear nose; public TailHook hook; public bool arrest; public float arrestEnd, noseK, noseC, noseMax;
            public Transform[] slatH = new Transform[2]; public Quaternion[] slatRest = new Quaternion[2]; public Rigidbody[] slatRb = new Rigidbody[2]; public float slat;
        }
        class Link { public Joint j; public Rigidbody c, p; public bool hinge; public Quaternion rest; }
        static readonly Dictionary<Aircraft, St> state = new Dictionary<Aircraft, St>();
        static readonly AccessTools.FieldRef<HighLiftDevice, float> fHld = AccessTools.FieldRefAccess<HighLiftDevice, float>("position");
        static readonly MethodInfo miLock = AccessTools.Method(typeof(ControlSurface), "SetLocked");
        const float MaxDeflect = 55f, DlcBias = 0.14f;

        // At runtime the game unparents every UnitPart, so search the root and every part.
        static T[] All<T>(Aircraft a) where T : Component
        {
            var set = new HashSet<T>();
            foreach (var c in a.GetComponentsInChildren<T>(true)) set.Add(c);
            var parts = a.GetAllParts();
            if (parts != null) foreach (var p in parts) if (p != null) foreach (var c in p.GetComponentsInChildren<T>(true)) set.Add(c);
            var arr = new T[set.Count]; set.CopyTo(arr); return arr;
        }
        static void Init(Aircraft a, St s)
        {
            s.init = true; s.f14 = Plugin.IsF14(a); if (!s.f14) return;
            s.rb = a.rb;
            foreach (var w in All<SwingWingController>(a)) { try { if (s.sweep == null || w.GetUpperAngleLimit() - w.GetLowerAngleLimit() > s.sweep.GetUpperAngleLimit() - s.sweep.GetLowerAngleLimit()) s.sweep = w; } catch (Exception) { } }
            s.gears = All<LandingGear>(a);
            foreach (var g in s.gears) if (g != null && g.gameObject.name == "gear_F") s.nose = g;
            if (Plugin.CarrierGear.Value)
                foreach (var g in s.gears)
                {
                    if (g == null) continue; bool n = g == s.nose;
                    float k0 = GearPatch.Spring(g), c0 = GearPatch.Damp(g);
                    GearPatch.Spring(g) = Mathf.Max(k0, n ? 6.0e5f : 1.5e6f); GearPatch.Damp(g) = Mathf.Max(c0, n ? 1.0e5f : 1.5e5f);
                    Plugin.Log.LogInfo($"F-14B carrier gear {g.gameObject.name}: spring {k0:F0} -> {GearPatch.Spring(g):F0} N/m, damping {c0:F0} -> {GearPatch.Damp(g):F0}");
                }
            var hks = All<TailHook>(a); if (hks.Length > 0) s.hook = hks[0];
            foreach (var h in All<HighLiftDevice>(a)) { if (s.flap == null) s.flap = h; }
            var ails = new List<ControlSurface>();
            foreach (var c in All<ControlSurface>(a)) if (c != null && c.name.StartsWith("aileron_")) ails.Add(c);
            s.ail = ails.ToArray();
            foreach (var t in All<Transform>(a))
            {
                if (t.name == "F14_SpoilerHinge_L") { s.hinge[0] = t; s.rest[0] = t.localRotation; }
                else if (t.name == "F14_SpoilerHinge_R") { s.hinge[1] = t; s.rest[1] = t.localRotation; }
                else if (t.name == "F14_SlatHinge_L") { s.slatH[0] = t; s.slatRest[0] = t.localRotation; s.slatRb[0] = t.GetComponentInParent<Rigidbody>(); }
                else if (t.name == "F14_SlatHinge_R") { s.slatH[1] = t; s.slatRest[1] = t.localRotation; s.slatRb[1] = t.GetComponentInParent<Rigidbody>(); }
            }
            Plugin.Log.LogInfo($"F-14B systems on '{a.unitName}': sweep {(s.sweep != null)}, roll surfaces {s.ail.Length}, spoiler hinges {(s.hinge[0] != null)}/{(s.hinge[1] != null)}, slat hinges {(s.slatH[0] != null)}/{(s.slatH[1] != null)}, gears {s.gears.Length}");
        }
        // ---------------------------------------------------------------- structure assist
        static void Scan(Aircraft a, St s)
        {
            var old = new Dictionary<Joint, Link>(); foreach (var l in s.links) if (l.j != null) old[l.j] = l;
            s.links.Clear();
            int it = Plugin.SolverIterations.Value;
            var parts = a.GetAllParts(); if (parts == null) return;
            foreach (var part in parts)
            {
                if (part == null) continue;
                foreach (var j in part.GetComponents<Joint>())
                {
                    if (j == null || j.connectedBody == null) continue;
                    var c = j.GetComponent<Rigidbody>(); var p = j.connectedBody;
                    if (c == null || c == p) continue;
                    bool hinge = j is HingeJoint; if (!hinge && !(j is FixedJoint)) continue;
                    foreach (var rb in new[] { c, p }) { if (rb.solverIterations < it) rb.solverIterations = it; if (rb.solverVelocityIterations < it / 2) rb.solverVelocityIterations = it / 2; }
                    // the bend reference is taken once per joint (at spawn, before any load) and kept
                    if (old.TryGetValue(j, out var ol) && ol.p == p) { s.links.Add(ol); continue; }
                    s.links.Add(new Link { j = j, c = c, p = p, hinge = hinge, rest = Quaternion.Inverse(p.rotation) * c.rotation });
                }
            }
            if (!s.loggedLinks && s.links.Count > 0) { s.loggedLinks = true; Plugin.Log.LogInfo($"F-14B structure assist: {s.links.Count} joints ({s.links.FindAll(l => l.hinge).Count} hinges), solver {it}"); }
        }
        static float Iaxis(Rigidbody rb, Vector3 n)
        {
            var l = Quaternion.Inverse(rb.rotation * rb.inertiaTensorRotation) * n; var I = rb.inertiaTensor;
            return Mathf.Max(1e-3f, l.x * l.x * I.x + l.y * l.y * I.y + l.z * l.z * I.z);
        }
        static float InvMassAt(Rigidbody rb, Vector3 pt, Vector3 n)
        {
            var rxn = Vector3.Cross(pt - rb.worldCenterOfMass, n); float r2 = rxn.sqrMagnitude;
            return 1f / rb.mass + (r2 > 1e-8f ? r2 / Iaxis(rb, rxn / Mathf.Sqrt(r2)) : 0f);
        }
        static void Structure(Aircraft a, St s, float dt)
        {
            if (Time.time >= s.nextScan) { s.nextScan = Time.time + 1f; Scan(a, s); }
            float f = Plugin.JointDamping.Value, b = Plugin.JointStiffness.Value;
            for (int i = s.links.Count - 1; i >= 0; i--)
            {
                var L = s.links[i];
                if (L.j == null || L.c == null || L.p == null || L.j.connectedBody != L.p) { s.links.RemoveAt(i); continue; }
                if (L.c.isKinematic || L.p.isKinematic) continue;
                // rotation: damp relative spin, pull back the bend (for the sweep hinge only off-axis, so sweeping is untouched)
                Vector3 dw = L.c.angularVelocity - L.p.angularVelocity;
                var qe = L.c.rotation * Quaternion.Inverse(L.p.rotation * L.rest);
                if (L.hinge)
                {
                    var ax = L.j.transform.TransformDirection(L.j.axis).normalized;
                    dw -= Vector3.Dot(dw, ax) * ax;
                    var tw = new Quaternion(0, 0, 0, qe.w); var pr = Vector3.Dot(new Vector3(qe.x, qe.y, qe.z), ax) * ax;
                    tw.x = pr.x; tw.y = pr.y; tw.z = pr.z; float tn = Mathf.Sqrt(tw.x * tw.x + tw.y * tw.y + tw.z * tw.z + tw.w * tw.w);
                    if (tn > 1e-6f) { tw.x /= tn; tw.y /= tn; tw.z /= tn; tw.w /= tn; qe = qe * Quaternion.Inverse(tw); }
                }
                qe.ToAngleAxis(out float ang, out Vector3 eax); if (ang > 180f) ang -= 360f;
                Vector3 th = (float.IsNaN(eax.x) || Mathf.Abs(ang) < 1e-3f) ? Vector3.zero : eax.normalized * (ang * Mathf.Deg2Rad);
                Vector3 u = dw * (f / dt) + th * (b / (dt * dt));
                float um = u.magnitude;
                if (um > 1e-6f)
                {
                    var n = u / um; float Ir = 1f / (1f / Iaxis(L.c, n) + 1f / Iaxis(L.p, n));
                    var tq = -u * Ir; L.c.AddTorque(tq, ForceMode.Force); L.p.AddTorque(-tq, ForceMode.Force);
                }
                // translation at the anchor (fixed joints): damp relative velocity, pull back the drift
                if (!L.hinge)
                {
                    Vector3 A = L.c.transform.TransformPoint(L.j.anchor), B = L.p.transform.TransformPoint(L.j.connectedAnchor);
                    Vector3 dv = L.c.GetPointVelocity(A) - L.p.GetPointVelocity(A);
                    Vector3 ua = dv * (f / dt) + (A - B) * (b / (dt * dt));
                    float am = ua.magnitude;
                    if (am > 1e-6f)
                    {
                        var n = ua / am; float me = 1f / (InvMassAt(L.c, A, n) + InvMassAt(L.p, A, n));
                        var F = -ua * me; L.c.AddForceAtPosition(F, A, ForceMode.Force); L.p.AddForceAtPosition(-F, A, ForceMode.Force);
                    }
                }
            }
        }

        // ---------------------------------------------------------------- nose strut on the wire
        static readonly AccessTools.FieldRef<TailHook, bool> fHooked = AccessTools.FieldRefAccess<TailHook, bool>("hooked");
        static void ArrestNose(St s)
        {
            if (s.nose == null || s.hook == null) return;
            bool hk = false; try { hk = fHooked(s.hook); } catch (Exception) { return; }
            if (hk) s.arrestEnd = Time.time + 2f;
            bool want = hk || Time.time < s.arrestEnd;
            if (want == s.arrest) return;
            s.arrest = want; var g = s.nose;
            if (want)
            {
                s.noseK = GearPatch.Spring(g); s.noseC = GearPatch.Damp(g); s.noseMax = GearPatch.MaxComp(g);
                GearPatch.Spring(g) = Mathf.Max(s.noseK, 1.1e6f); GearPatch.Damp(g) = Mathf.Max(s.noseC, 1.4e5f);
                GearPatch.MaxComp(g) = Mathf.Max(s.noseMax, GearPatch.Travel(g) - 0.01f);
                Plugin.Log.LogInfo($"F-14B on the wire: nose strut {s.noseK:F0} -> {GearPatch.Spring(g):F0} N/m, break limit {s.noseMax:F2} -> {GearPatch.MaxComp(g):F2} m");
            }
            else
            {
                GearPatch.Spring(g) = s.noseK; GearPatch.Damp(g) = s.noseC; GearPatch.MaxComp(g) = s.noseMax;
                Plugin.Log.LogInfo("F-14B off the wire: nose strut restored");
            }
        }

        static void Postfix(Aircraft __instance)
        {
            var a = __instance; if (a == null) return;
            try
            {
                if (!state.TryGetValue(a, out var s)) { s = new St(); state[a] = s; }
                if (!s.init) { if (a.definition == null) return; Init(a, s); }
                if (!s.f14) return;
                if (s.rb == null) { s.rb = a.rb; if (s.rb == null) return; }
                float dt = Time.fixedDeltaTime;
                if (Plugin.StructureAssist.Value) Structure(a, s, dt);
                if (Plugin.ArrestNoseGear.Value) ArrestNose(s);
                var inp = a.GetInputs();
                float roll = inp != null ? inp.roll : 0f, pitchIn = inp != null ? inp.pitch : 0f, thr = inp != null ? inp.throttle : 0f;
                float sweepDeg = 20f; try { if (s.sweep != null) sweepDeg = s.sweep.GetWingAngle(); } catch (Exception) { }
                bool rollOk = Plugin.RollSpoilers.Value && sweepDeg <= Plugin.SpoilerMaxSweep.Value;
                // lock the roll surfaces when swept (game would lock them as soon as the wing moves; the prefab no longer does)
                bool wantLock = !rollOk;
                if (wantLock != s.locked && miLock != null)
                {
                    s.locked = wantLock;
                    foreach (var c in s.ail) if (c != null) try { miLock.Invoke(c, new object[] { wantLock }); } catch (Exception) { }
                }
                bool wow = false; if (s.gears != null) foreach (var g in s.gears) if (g != null && g.enabled && g.WeightOnWheel(0.02f)) { wow = true; break; }
                Vector3 v = s.rb.velocity; float V = v.magnitude; float q = 0.5f * a.airDensity * V * V;
                float gsp = new Vector3(v.x, 0, v.z).magnitude;
                float flap = s.flap != null ? fHld(s.flap) : 0f;
                // ground spoilers: weight on wheels, gear down, throttle idle, rolling
                bool gsOn = Plugin.GroundSpoilers.Value && wow && a.gearDeployed && thr < 0.08f && gsp > 8f;
                s.ground = Mathf.MoveTowards(s.ground, gsOn ? 1f : 0f, dt / 0.6f);
                if (gsOn != s.logGs) { s.logGs = gsOn; if (gsOn) Plugin.Log.LogInfo($"F-14B ground spoilers up at {gsp * 1.944f:F0} kt"); }
                // DLC: approach config, airborne
                bool dlcOn = Plugin.DLC.Value && !wow && a.gearDeployed && flap > 0.5f && V > 40f;
                float dlcT = dlcOn ? Mathf.Clamp(DlcBias + DlcBias * Mathf.Clamp(pitchIn, -1f, 1f), 0f, 2f * DlcBias) : 0f;   // game pitch input: pull = negative
                s.dlc = Mathf.MoveTowards(s.dlc, dlcT, dt / 0.25f);
                // per side: roll (up-only on the down-going wing), ground, DLC
                for (int i = 0; i < 2; i++)
                {
                    float r = rollOk ? Mathf.Clamp01(i == 0 ? -roll : roll) : 0f;
                    float tgt = Mathf.Max(r, Mathf.Max(s.ground, s.dlc)) * MaxDeflect;
                    s.ang[i] = Mathf.MoveTowards(s.ang[i], tgt, dt * 160f);
                    var h = s.hinge[i]; if (h != null) h.localRotation = s.rest[i] * Quaternion.Euler(s.ang[i], 0f, 0f);
                }
                // slats: with the flaps, maneuver schedule by AoA, stall protection
                float sos = 340f; try { sos = LevelInfo.GetSpeedOfSound(a.transform.GlobalPosition().y); } catch (Exception) { }
                float mach = V / Mathf.Max(sos, 250f);
                Vector3 lv = a.transform.InverseTransformDirection(v);
                float aoa = V > 5f ? Mathf.Atan2(-lv.y, lv.z) * Mathf.Rad2Deg : 0f;
                float slatT = 17f * Mathf.Clamp01(flap);
                if (Plugin.ManeuverSlats.Value && !wow && sweepDeg <= 50f && mach < 0.85f && V > 30f)
                {
                    float man = 7f * Mathf.InverseLerp(7.5f, 10.5f, aoa);
                    float prot = mach < 0.5f ? 17f * Mathf.InverseLerp(15f, 20f, aoa) : 0f;
                    slatT = Mathf.Max(slatT, Mathf.Max(man, prot));
                }
                if (!Plugin.SlatsOn.Value) slatT = 0f;
                s.slat = Mathf.MoveTowards(s.slat, slatT, dt * 25f);
                for (int i = 0; i < 2; i++) { var h = s.slatH[i]; if (h != null) h.localRotation = s.slatRest[i] * Quaternion.Euler(s.slat, 0f, 0f); }
                if (s.rb.isKinematic || V < 1f) return;
                Vector3 dir = v / V;
                if (Plugin.WaveDragArea.Value > 0f)
                {
                    float m = mach;
                    float k = m < 0.95f ? 0f : m < 1.15f ? Mathf.SmoothStep(0f, 1f, (m - 0.95f) / 0.2f) : m < 1.3f ? 1f : Mathf.Lerp(1f, 0.4f, Mathf.Clamp01((m - 1.3f) / 0.7f));
                    if (k > 0f) s.rb.AddForce(-dir * (q * Plugin.WaveDragArea.Value * k), ForceMode.Force);
                }
                Vector3 up = Vector3.Cross(v, a.transform.right).normalized;   // lift direction
                if (s.slat > 0.1f && Plugin.SlatLift.Value > 0f)
                {
                    // the slots keep the flow attached at high AoA: lift that grows from 8 to 16 deg AoA, applied on each wing
                    float L = q * Plugin.SlatLift.Value * 0.5f * (s.slat / 17f) * Mathf.Clamp01((aoa - 8f) / 8f);
                    if (L > 0f)
                        for (int i = 0; i < 2; i++)
                        {
                            var rb = s.slatRb[i]; var h = s.slatH[i];
                            if (rb == null || h == null || rb.isKinematic) continue;
                            rb.AddForceAtPosition(up * L - dir * (L * 0.12f), h.position, ForceMode.Force);
                        }
                }
                if (s.ground > 0.01f)
                {
                    float cd = Plugin.GroundSpoilerDrag.Value * s.ground;
                    s.rb.AddForce(-dir * (q * cd) - up * (q * cd * 3f), ForceMode.Force);
                }
                if (s.dlc > 0.001f || dlcOn)
                {
                    // around the bias the spoilers trade lift: below bias = more lift, above = less (about 4 m^2 of lift area at full travel)
                    float dL = -q * 4f * Plugin.DlcStrength.Value * (s.dlc - DlcBias) / DlcBias;
                    s.rb.AddForce(up * dL - dir * (Mathf.Abs(dL) * 0.08f), ForceMode.Force);
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("F-14B systems: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(WeaponManager), "HasTailHook")]
    static class HookPatch
    {
        static readonly Dictionary<Aircraft, bool> Has = new Dictionary<Aircraft, bool>();
        static readonly AccessTools.FieldRef<WeaponManager, Aircraft> acOf = AccessTools.FieldRefAccess<WeaponManager, Aircraft>("aircraft");
        static void Postfix(WeaponManager __instance, ref bool __result)
        {
            if (__result) return;
            var a = acOf(__instance);
            if (a == null || !Plugin.IsF14(a)) return;
            if (!Has.TryGetValue(a, out var h)) { h = Plugin.TailHookOn.Value && a.GetComponentsInChildren<TailHook>(true).Length > 0; Has[a] = h; }
            __result = h;
        }
    }

    [HarmonyPatch(typeof(TailHook), "ApplyForce")]
    static class ArrestPatch
    {
        static void Prefix(TailHook __instance, out Vector3 __state)
        {
            __state = new Vector3(float.NaN, 0f, 0f);
            float k = Plugin.ArrestRelief.Value; if (k <= 0f) return;
            var up = __instance.unitPart; var e = __instance.hookEnd;
            if (up == null || e == null || up.rb == null) return;
            if (!Plugin.IsF14(up.parentUnit as Aircraft)) return;
            var rb = up.rb; Vector3 p = e.position, ax = rb.transform.up;
            float dh = Vector3.Dot(rb.worldCenterOfMass - p, ax);
            __state = e.localPosition; e.position = p + ax * (dh * k);
        }
        static void Postfix(TailHook __instance, Vector3 __state)
        {
            if (float.IsNaN(__state.x)) return;
            var e = __instance.hookEnd; if (e != null) e.localPosition = __state;
        }
    }

    [HarmonyPatch(typeof(SwingWingController), "CheckForManualInput")]
    static class ManualSweepPatch
    {
        static readonly FieldInfo fAc = AccessTools.Field(typeof(SwingWingController), "aircraft");
        static readonly FieldInfo fPlayer = AccessTools.Field(typeof(SwingWingController), "player");
        static readonly FieldInfo fMode = AccessTools.Field(typeof(SwingWingController), "swingWingMode");
        static readonly FieldInfo fPrev = AccessTools.Field(typeof(SwingWingController), "customAxis1Prev");
        static readonly FieldInfo fInputs = AccessTools.Field(typeof(SwingWingController), "inputs");
        static MethodInfo mButton, mAxis, mAxisPrev; static bool warned;
        static bool Prefix(SwingWingController __instance)
        {
            try
            {
                if (!Plugin.BlockModifierSweep.Value) return true;
                var a = fAc?.GetValue(__instance) as Aircraft; if (!Plugin.IsF14(a)) return true;
                if (Convert.ToInt32(fMode.GetValue(__instance)) != 0) return true;      // already manual: let the game handle the way back to auto
                var pl = fPlayer?.GetValue(__instance); if (pl == null) return true;
                var t = pl.GetType();
                if (mButton == null) { mButton = t.GetMethod("GetButton", new[] { typeof(string) }); mAxis = t.GetMethod("GetAxisRaw", new[] { typeof(string) }); mAxisPrev = t.GetMethod("GetAxisRawPrev", new[] { typeof(string) }); }
                if (!(bool)mButton.Invoke(pl, new object[] { "Axis Modifier" })) return true;
                float ax = (float)mAxis.Invoke(pl, new object[] { "Custom Axis 1" }), axp = (float)mAxisPrev.Invoke(pl, new object[] { "Custom Axis 1" });
                if (Mathf.Abs(ax - axp) > 0f) return true;                              // the real sweep axis moved: normal behaviour
                // modifier + throttle only: stay in auto; keep the game's change tracker in step
                var inp = fInputs?.GetValue(__instance) as ControlInputs; if (inp != null) fPrev.SetValue(__instance, inp.customAxis1);
                return false;
            }
            catch (Exception e) { if (!warned) { warned = true; Plugin.Log.LogWarning("F-14B manual sweep patch: " + e.Message); } return true; }
        }
    }

    // ---------------- 0.1.6: wheel ground ray fallback + diagnostics (port of the Hornet 0.6.2 fix) ----------------
    // The game casts one Physics.Linecast per wheel in LandingGear.FixedUpdate. If the Hornet plugin is installed it has
    // already swapped that call for its own GearCast; this transpiler runs after it and wraps whichever call it finds.
    [HarmonyPatch(typeof(LandingGear), "FixedUpdate")]
    [HarmonyAfter("fa18c.hornet.systems")]
    static class GearPatch
    {
        internal static readonly AccessTools.FieldRef<LandingGear, float> Spring = AccessTools.FieldRefAccess<LandingGear, float>("springRate");
        internal static readonly AccessTools.FieldRef<LandingGear, float> Damp = AccessTools.FieldRefAccess<LandingGear, float>("dampingRate");
        internal static readonly AccessTools.FieldRef<LandingGear, float> Travel = AccessTools.FieldRefAccess<LandingGear, float>("suspensionTravel");
        internal static readonly AccessTools.FieldRef<LandingGear, float> MaxComp = AccessTools.FieldRefAccess<LandingGear, float>("maxCompression");
        static readonly AccessTools.FieldRef<LandingGear, Aircraft> AcOf = AccessTools.FieldRefAccess<LandingGear, Aircraft>("aircraft");
        delegate bool CastFn(Vector3 s, Vector3 e, out RaycastHit hit, int mask, LandingGear g);
        static CastFn inner;
        class G { public bool f14, nose; public Aircraft ac; public float lastHit = -99f, lastLog = -99f, missSince = -1f; }
        static readonly Dictionary<LandingGear, G> info = new Dictionary<LandingGear, G>();
        static readonly Dictionary<Aircraft, List<G>> mains = new Dictionary<Aircraft, List<G>>();
        static readonly RaycastHit[] buf = new RaycastHit[16];
        const float Raise = 0.35f, TwinOff = 0.2f;

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
        {
            var lc = AccessTools.Method(typeof(Physics), "Linecast", new[] { typeof(Vector3), typeof(Vector3), typeof(RaycastHit).MakeByRefType(), typeof(int) });
            var mine = AccessTools.Method(typeof(GearPatch), nameof(F14Cast));
            var wrap = AccessTools.Method(typeof(GearPatch), nameof(F14Wrap));
            int n = 0;
            foreach (var ci in code)
            {
                if (ci.opcode == OpCodes.Call && ci.operand is MethodInfo mi)
                {
                    if (mi == lc) { n++; yield return new CodeInstruction(OpCodes.Ldarg_0); yield return new CodeInstruction(OpCodes.Call, mine); continue; }
                    var ps = mi.GetParameters();
                    if (mi.IsStatic && mi.Name == "GearCast" && mi.DeclaringType != typeof(GearPatch) && ps.Length == 5 && ps[4].ParameterType == typeof(LandingGear) && ps[2].ParameterType == typeof(RaycastHit).MakeByRefType())
                    {
                        CastFn fn = null;
                        try { fn = (CastFn)Delegate.CreateDelegate(typeof(CastFn), mi); }
                        catch (Exception e) { Plugin.Log.LogWarning("F-14B gear probe: could not chain " + mi.DeclaringType.FullName + ": " + e.Message); }
                        if (fn != null) { inner = fn; n++; Plugin.Log.LogInfo("F-14B gear probe chained after " + mi.DeclaringType.FullName); yield return new CodeInstruction(OpCodes.Call, wrap); continue; }
                    }
                }
                yield return ci;
            }
            if (n != 1) Plugin.Log.LogWarning($"F-14B GearPatch: expected 1 ground ray in LandingGear.FixedUpdate, patched {n}");
        }

        static bool F14Wrap(Vector3 s, Vector3 e, out RaycastHit hit, int mask, LandingGear g)
        {
            var x = Get(g);
            if (x.f14 || inner == null) return F14Cast(s, e, out hit, mask, g);
            return inner(s, e, out hit, mask, g);
        }

        static G Get(LandingGear g)
        {
            if (info.TryGetValue(g, out var x) && x.ac != null) return x;
            x = new G { ac = AcOf(g) };
            x.f14 = Plugin.IsF14(x.ac);
            x.nose = g.gameObject.name == "gear_F";
            info[g] = x;
            if (x.f14 && !x.nose && x.ac != null)
            {
                if (!mains.TryGetValue(x.ac, out var l)) mains[x.ac] = l = new List<G>();
                l.Add(x);
            }
            return x;
        }
        static bool Own(Collider c, Aircraft ac)
        {
            var rb = c.attachedRigidbody; if (rb == null) return false;
            var up = rb.GetComponent<UnitPart>();
            return up != null && up.parentUnit == ac;
        }

        static bool F14Cast(Vector3 s, Vector3 e, out RaycastHit hit, int mask, LandingGear g)
        {
            bool ok = Physics.Linecast(s, e, out hit, mask);
            if (!Plugin.GearProbe.Value && !Plugin.GearDiag.Value) return ok;
            var x = Get(g);
            if (!x.f14) return ok;
            float now = Time.time;
            if (ok) { x.lastHit = now; x.missSince = -1f; return true; }
            if (x.missSince < 0f) x.missSince = now;
            bool othersDown = false;
            if (x.nose && mains.TryGetValue(x.ac, out var ml)) foreach (var m in ml) if (now - m.lastHit < 0.1f) othersDown = true;
            Vector3 d = e - s; float len = d.magnitude; if (len < 1e-4f) return false; d /= len;
            Vector3 right = x.ac != null ? x.ac.transform.right : g.transform.right;
            bool found = false; float best = float.MaxValue; RaycastHit bh = default; int which = -1;
            if (Plugin.GearProbe.Value)
            {
                int probes = x.nose ? 3 : 1;
                for (int i = 0; i < probes; i++)
                {
                    Vector3 off = i == 0 ? Vector3.zero : right * (i == 1 ? TwinOff : -TwinOff);
                    int c = Physics.RaycastNonAlloc(s + off - d * Raise, d, buf, len + Raise, mask);
                    for (int j = 0; j < c; j++)
                    {
                        var h = buf[j];
                        if (h.collider == null || Own(h.collider, x.ac)) continue;
                        if (h.distance < best) { best = h.distance; bh = h; found = true; which = i; }
                    }
                }
            }
            if (found)
            {
                float dist = best - Raise;
                float minD = Travel(g) - MaxComp(g) + 0.02f;            // never more compression than just under the break limit
                bool clamped = dist < minD;
                bh.distance = Mathf.Max(dist, minD);
                hit = bh; x.lastHit = now;
                if (Plugin.GearDiag.Value && now - x.lastLog > 1f)
                {
                    x.lastLog = now;
                    Plugin.Log.LogInfo($"F-14B gear probe saved {g.gameObject.name}: {(which == 0 ? "centre (raised start)" : "twin wheel " + (which == 1 ? "right" : "left"))}, ground {dist:F2} m below cast point{(clamped ? " (at/above the ray start - clamped)" : "")}, collider '{bh.collider.name}' layer {bh.collider.gameObject.layer}");
                }
                return true;
            }
            if (Plugin.GearDiag.Value && x.nose && othersDown && now - x.missSince > 0.25f && now - x.lastLog > 2f)
            {
                x.lastLog = now;
                var sb = new System.Text.StringBuilder();
                int c = Physics.RaycastNonAlloc(s - d * 1.5f, d, buf, 4f);
                for (int j = 0; j < c; j++)
                {
                    var h = buf[j]; if (h.collider == null) continue;
                    sb.Append($" ['{h.collider.name}' L{h.collider.gameObject.layer}{(h.collider.isTrigger ? " trig" : "")}{(Own(h.collider, x.ac) ? " OWN" : "")} {h.distance - 1.5f:F2}m]");
                }
                Plugin.Log.LogWarning($"F-14B nose wheel lost the deck while the mains are down (mask {mask}). Hits below the nose cast point:{(c == 0 ? " none" : sb.ToString())}");
            }
            return false;
        }
    }

    // Weapon mounts from other mods (e.g. Multi-Missile) can carry solid colliders that reach into a neighbouring part
    // of this jet. At spawn the physics engine pushes the overlapping parts apart, which damages them. Make every
    // spawned mount ignore collisions with this aircraft's own colliders (it still collides with everything else).
    [HarmonyPatch(typeof(Hardpoint), "SpawnMount")]
    static class MountSelfCollisionPatch
    {
        static void Postfix(Hardpoint __instance, Aircraft aircraft)
        {
            try
            {
                if (!Plugin.MountCollisionFix.Value || !Plugin.IsF14(aircraft)) return;
                var v = Traverse.Create(__instance).Field("spawnedPrefab").GetValue();
                GameObject go = v as GameObject; if (go == null && v is Component comp) go = comp.gameObject;
                if (go == null) return;
                var own = go.GetComponentsInChildren<Collider>(true);
                if (own.Length == 0) return;
                var mine = new HashSet<Collider>(own);
                var all = new HashSet<Collider>(aircraft.GetComponentsInChildren<Collider>(true));
                var parts = aircraft.GetAllParts();
                if (parts != null) foreach (var p in parts) if (p != null) foreach (var c in p.GetComponentsInChildren<Collider>(true)) all.Add(c);
                int n = 0;
                foreach (var c in all)
                {
                    if (c == null || c.isTrigger || mine.Contains(c)) continue;
                    foreach (var m in own) { if (m == null || m.isTrigger) continue; Physics.IgnoreCollision(m, c, true); n++; }
                }
                if (Plugin.MountCollisionLog.Value) Plugin.Log.LogInfo($"mount {go.name}: {own.Length} collider(s) set to ignore {n} pairs with own airframe");
            }
            catch (Exception e) { Plugin.Log.LogWarning("mount collision fix failed: " + e.Message); }
        }
    }
}

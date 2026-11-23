using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
namespace F14BDebug
{
[BepInPlugin("f14b.flight.debug", "F-14B Flight Debug Recorder", Plugin.Version)]
public class Plugin : BaseUnityPlugin
{
public const string Version = "0.1.3";
internal static BepInEx.Logging.ManualLogSource Log;
internal static ConfigEntry<bool> Enabled, PlayerOnly, ShowOverlay, EchoEvents;
internal static ConfigEntry<string> AircraftKeys, MarkKey, OverlayKey;
internal static ConfigEntry<float> TelemetryHz, StatusInterval, OverG, UnderG, HighAoA;
internal static string Root;
static bool legacyInputBroken;
void Awake()
{
Log = Logger;
Enabled = Config.Bind("General", "Enabled", true, "Record flight debug data.");
AircraftKeys = Config.Bind("General", "AircraftKeys", "F14B", "Aircraft definition keys to record, comma separated (F14B, FA18C, ...). * = every aircraft you fly (handy to compare with the FS-41 or vanilla jets).");
PlayerOnly = Config.Bind("General", "PlayerOnly", true, "Only record the jet you are flying (not AI or other players).");
TelemetryHz = Config.Bind("General", "TelemetryHz", 10f, new ConfigDescription("Rows per second written to telemetry.csv.", new AcceptableValueRange<float>(1f, 50f)));
StatusInterval = Config.Bind("General", "StatusLineSeconds", 5f, "A one-line status summary is written to the BepInEx log this often (0 = off).");
EchoEvents = Config.Bind("General", "EchoEventsToBepInExLog", true, "Also write every event to BepInEx\\LogOutput.log.");
ShowOverlay = Config.Bind("Overlay", "ShowOverlay", true, "Small live readout in the top-left corner (toggle in game with the overlay key). Useful in video recordings.");
MarkKey = Config.Bind("Keys", "MarkKey", "F8", "Press to drop a MARK in the log with a full snapshot (use it when something feels wrong).");
OverlayKey = Config.Bind("Keys", "OverlayKey", "F9", "Show/hide the overlay.");
OverG = Config.Bind("Thresholds", "OverG", 7.5f, "Log g episodes above this.");
UnderG = Config.Bind("Thresholds", "NegativeG", -2.5f, "Log g episodes below this.");
HighAoA = Config.Bind("Thresholds", "HighAoA", 20f, "Log angle-of-attack episodes above this (deg).");
Root = Path.Combine(Paths.BepInExRootPath, "F14B_Debug");
Directory.CreateDirectory(Root);
var h = new Harmony("f14b.flight.debug");
int ok = 0;
foreach (var t in typeof(Plugin).Assembly.GetTypes())
{
if (t.GetCustomAttributes(typeof(HarmonyPatch), false).Length == 0) continue;
try { h.CreateClassProcessor(t).Patch(); ok++; }
catch (Exception e) { Log.LogWarning($"patch {t.Name} failed: {e.Message}"); }
}
Log.LogInfo($"F-14B Flight Debug Recorder {Version} loaded: {ok} patch groups, recording '{AircraftKeys.Value}' to {Root}");
}
static bool KeyDown(string name)
{
if (string.IsNullOrEmpty(name)) return false;
if (!legacyInputBroken)
{
try { if (Enum.TryParse(name, true, out KeyCode kc)) return Input.GetKeyDown(kc); }
catch (Exception) { legacyInputBroken = true; }
}
try
{
var kb = UnityEngine.InputSystem.Keyboard.current;
if (kb != null && Enum.TryParse(name, true, out UnityEngine.InputSystem.Key k)) return kb[k].wasPressedThisFrame;
}
catch (Exception) { }
return false;
}
void Update()
{
if (!Enabled.Value) return;
if (KeyDown(MarkKey.Value)) Recorder.MarkAll();
if (KeyDown(OverlayKey.Value)) ShowOverlay.Value = !ShowOverlay.Value;
Recorder.Housekeeping();
}
void OnGUI() { if (Enabled.Value && ShowOverlay.Value) Recorder.DrawOverlay(); }
void OnApplicationQuit() { Recorder.CloseAll("game closed"); }
void OnDestroy() { Recorder.CloseAll("plugin unloaded"); }
internal static bool KeyMatch(Aircraft a)
{
var d = a.definition; if (d == null) return false;
string keys = AircraftKeys.Value.Trim();
if (keys == "*") return true;
foreach (var k in keys.Split(',')) if (k.Trim() == d.jsonKey) return true;
return false;
}
internal static bool LocalOk(Aircraft a)
{
if (!PlayerOnly.Value) return true;
try { return GameManager.IsLocalAircraft(a); } catch (Exception) { return false; }
}
}
[HarmonyPatch(typeof(Aircraft), "FixedUpdate")]
static class P_AircraftFixed
{
static void Postfix(Aircraft __instance)
{
if (!Plugin.Enabled.Value) return;
try { Recorder.Tick(__instance); }
catch (Exception e) { Recorder.Fault("Tick", e); }
}
}
[HarmonyPatch(typeof(AeroPart), "OnCollisionEnter")]
static class P_Collision
{
static void Postfix(AeroPart __instance, Collision collision)
{
try { var r = Recorder.For(__instance); if (r != null) r.OnCollision(__instance, collision); }
catch (Exception e) { Recorder.Fault("Collision", e); }
}
}
[HarmonyPatch(typeof(UnitPart), "OnJointBreak")]
static class P_JointBreak
{
static void Postfix(UnitPart __instance, float breakForce)
{
try { var r = Recorder.For(__instance); if (r != null) r.Event("JOINT_BREAK", $"part {__instance.name} joint broke (force {breakForce:F0}) | {r.Snap()}"); }
catch (Exception e) { Recorder.Fault("JointBreak", e); }
}
}
[HarmonyPatch(typeof(UnitPart), "Detach")]
static class P_Detach
{
static void Prefix(UnitPart __instance) { Recorder.PartDetached(__instance); }
}
[HarmonyPatch(typeof(AeroPart), "Detach")]
static class P_DetachAero
{
static void Prefix(AeroPart __instance) { Recorder.PartDetached(__instance); }
}
[HarmonyPatch(typeof(AeroPart), "ApplyDamage")]
static class P_Damage
{
static void Postfix(AeroPart __instance, float netPierceDamage, float netBlastDamage, float netFireDamage, float netImpactDamage)
{
try { var r = Recorder.For(__instance); if (r != null) r.OnDamage(__instance, netPierceDamage, netBlastDamage, netFireDamage, netImpactDamage); }
catch (Exception e) { Recorder.Fault("Damage", e); }
}
}
[HarmonyPatch(typeof(LandingGear), "BreakWheel")]
static class P_GearBreak
{
static void Prefix(LandingGear __instance)
{
try { var r = Recorder.ForGear(__instance); if (r != null) r.OnGearBreak(__instance); }
catch (Exception e) { Recorder.Fault("GearBreak", e); }
}
}
enum Phase { Init, Parked, Taxi, TakeoffRoll, Airborne, Rollout }
class GearInfo
{
public LandingGear g; public string name; public bool main, nose;
public bool wow; public float comp, ratio, force, fold, foldPrev; public float peakRatio, peakForce;
public bool bottomed; public float wowT;
public bool lost; public float lastComp, lastDepth = -1f;
}
partial class Recorder
{
static readonly CultureInfo IC = CultureInfo.InvariantCulture;
static readonly Dictionary<Aircraft, Recorder> recs = new Dictionary<Aircraft, Recorder>();
static readonly HashSet<int> ignored = new HashSet<int>();
static readonly Dictionary<int, float> nextCheck = new Dictionary<int, float>();
static readonly Dictionary<LandingGear, Recorder> gearOwner = new Dictionary<LandingGear, Recorder>();
static Recorder lastActive;
static readonly HashSet<string> faults = new HashSet<string>();
internal static void Fault(string where, Exception e)
{
string k = where + e.GetType().Name + e.Message;
if (faults.Add(k)) Plugin.Log.LogWarning($"[F14B-DBG] {where} error (logged once): {e}");
}
static readonly AccessTools.FieldRef<LandingGear, float> fComp = AccessTools.FieldRefAccess<LandingGear, float>("compressionDistance");
static readonly AccessTools.FieldRef<LandingGear, float> fForce = AccessTools.FieldRefAccess<LandingGear, float>("compressionForce");
static readonly AccessTools.FieldRef<LandingGear, float> fDampF = AccessTools.FieldRefAccess<LandingGear, float>("dampingForce");
static readonly AccessTools.FieldRef<LandingGear, float> fMaxComp = AccessTools.FieldRefAccess<LandingGear, float>("maxCompression");
static readonly AccessTools.FieldRef<LandingGear, float> fTravel = AccessTools.FieldRefAccess<LandingGear, float>("suspensionTravel");
static readonly AccessTools.FieldRef<LandingGear, float> fSpring = AccessTools.FieldRefAccess<LandingGear, float>("springRate");
static readonly AccessTools.FieldRef<LandingGear, float> fDamp = AccessTools.FieldRefAccess<LandingGear, float>("dampingRate");
static readonly AccessTools.FieldRef<LandingGear, float> fFold = AccessTools.FieldRefAccess<LandingGear, float>("foldAmount");
static readonly AccessTools.FieldRef<LandingGear, float> fWheelR = AccessTools.FieldRefAccess<LandingGear, float>("wheelRadius");
static readonly AccessTools.FieldRef<LandingGear, float> fBrake = AccessTools.FieldRefAccess<LandingGear, float>("brakeStrength");
static readonly AccessTools.FieldRef<LandingGear, float> fSteer = AccessTools.FieldRefAccess<LandingGear, float>("steeringAngle");
static readonly AccessTools.FieldRef<LandingGear, GameObject> fUnsprung = AccessTools.FieldRefAccess<LandingGear, GameObject>("unsprung");
static readonly AccessTools.FieldRef<LandingGear, Collider> fGearCol = AccessTools.FieldRefAccess<LandingGear, Collider>("gearCollider");
static readonly AccessTools.FieldRef<LandingGear, Transform> fCast = AccessTools.FieldRefAccess<LandingGear, Transform>("castPoint");
static readonly AccessTools.FieldRef<HighLiftDevice, float> fHld = AccessTools.FieldRefAccess<HighLiftDevice, float>("position");
static readonly AccessTools.FieldRef<ControlSurface, float> fSplit = AccessTools.FieldRefAccess<ControlSurface, float>("splitAmount");
static readonly AccessTools.FieldRef<ControlSurface, float> fMaxSplit = AccessTools.FieldRefAccess<ControlSurface, float>("maxSplit");
static readonly AccessTools.FieldRef<TailHook, float> fHookAmt = AccessTools.FieldRefAccess<TailHook, float>("deployedAmount");
static readonly AccessTools.FieldRef<TailHook, bool> fHooked = AccessTools.FieldRefAccess<TailHook, bool>("hooked");
static readonly AccessTools.FieldRef<Turbofan, bool> fFanOk = AccessTools.FieldRefAccess<Turbofan, bool>("operable");
static readonly AccessTools.FieldRef<Turbojet, bool> fJetOk = AccessTools.FieldRefAccess<Turbojet, bool>("operable");
internal Aircraft a; Rigidbody rb; string key, dir; StreamWriter csv, ev;
readonly List<GearInfo> gears = new List<GearInfo>();
Turbofan[] fans = new Turbofan[0]; Turbojet[] jets = new Turbojet[0];
static readonly System.Reflection.FieldInfo fiDepth = AccessTools.Field(typeof(LandingGear), "groundDepth");
static float GDepth(LandingGear g) { try { return fiDepth != null ? (float)fiDepth.GetValue(g) : -1f; } catch (Exception) { return -1f; } }
bool ejected; Transform spL, spR; Quaternion spL0, spR0; float spLdeg, spRdeg;
SwingWingController sweep; HighLiftDevice flap; readonly List<ControlSurface> brakes = new List<ControlSurface>(); TailHook hook;
readonly HashSet<Collider> gearCols = new HashSet<Collider>();
float t0, lastRow, lastStatus = -99f, lastCg = -99f; int rows;
bool initDone; float firstSeen; int initTries;
Phase phase = Phase.Init; float phaseT;
Vector3 mainContact, noseContact; float tailLimit = 90f, visTailLimit = 90f; string tailWho = "?", visTailWho = "?";
readonly List<Vector3> tailPts = new List<Vector3>(); float levelPitch;
float tas, eas, mach, gs, altFt, raltFt, vsFpm, pitch, roll, hdg, aoa, beta, gN, pRate, qRate, rRate, turnRate, accel, massKg, fuelKg, fuelFlow, thrustKN, rpm, sweepDeg, sweepPos, flapPos, sbSplit, hookAmt, tailClr = -1f;
bool hooked, anyWow, mainWow, noseWow, gearCmd; Vector3 cgLocal; Vector3 vPrev; bool hasPrev; float tasPrev, fuelPrev = -1f, fuelPrevT, hdgPrev; float vsPrevFpm;
ControlInputs inp;
float airT, groundT, stillT; bool staticLogged; float noWowT;
float rollStartT = -1f, rollDist, rollMaxPitch, rollMinTail = 99f, t100 = -1f, rotateT = -1f, rotateKeas, rotatePitch, liftT = -1f, liftKeas, liftKtas, liftPitch, liftAoa, liftDist; bool rolledStart, got50;
float tdT = -1f, tdKeas, tdSink, tdPitch, tdAoa, tdRoll, tdPeakG, tdBeta; string tdFirst = ""; bool tdOpen; float noseTdT = -1f, rollDistLand, landMaxDecel, brakeMax; int bounces; float bounceMaxAlt;
float gEpPeak, gEpT = -1f; bool gEp; float aoaEpPeak, aoaEpT = -1f, aoaEpMinKeas; bool aoaEp; float depLogT = -99f;
float sweepLogged = -999f; bool flapOut, sbOut, hookDown, wasHooked; float gearCmdT = -1f; bool gearMoving; float gearStillT;
float maxMach, maxKeas, maxAlt, maxG = -99f, minG = 99f, maxAoa; int tailStrikes, scrapes, gearBreaks, partsLost;
readonly Dictionary<string, float> partLogT = new Dictionary<string, float>();
readonly HashSet<string> engineOut = new HashSet<string>();
bool fuel20, fuel10;
readonly RaycastHit[] hits = new RaycastHit[16];
internal static Recorder For(UnitPart p)
{
if (p == null) return null;
var ac = p.parentUnit as Aircraft; if (ac == null) return null;
return recs.TryGetValue(ac, out var r) && r.initDone ? r : null;
}
internal static Recorder ForGear(LandingGear g) => g != null && gearOwner.TryGetValue(g, out var r) ? r : null;
internal static void Tick(Aircraft ac)
{
if (ac == null) return;
int id = ac.GetInstanceID();
if (ignored.Contains(id)) return;
if (!recs.TryGetValue(ac, out var r))
{
if (ac.definition == null) return;
if (!Plugin.KeyMatch(ac)) { ignored.Add(id); return; }
if (ac.remoteSim) return;
if (nextCheck.TryGetValue(id, out var nc) && Time.time < nc) return;
nextCheck[id] = Time.time + 1f;
if (!Plugin.LocalOk(ac)) return;
r = new Recorder { a = ac, firstSeen = Time.time };
recs[ac] = r;
}
r.Step();
}
internal static void Housekeeping()
{
List<Aircraft> dead = null;
foreach (var kv in recs)
{
var r = kv.Value;
if (kv.Key == null) { r.Close("aircraft destroyed / despawned"); (dead ??= new List<Aircraft>()).Add(kv.Key); continue; }
if (r.initDone && kv.Key.disabled && !r.disabledLogged) { r.disabledLogged = true; r.Event("DISABLED", "aircraft disabled (destroyed, crashed or pilot ejected) | " + r.Snap()); }
if (r.initDone && r.csv != null && Time.time - r.lastFlush > 1f) { r.lastFlush = Time.time; try { r.csv.Flush(); r.ev.Flush(); } catch (Exception) { } }
}
if (dead != null) foreach (var d in dead) recs.Remove(d);
}
bool disabledLogged; float lastFlush;
internal static void CloseAll(string why)
{
foreach (var r in recs.Values) r.Close(why);
recs.Clear();
}
internal static void MarkAll()
{
foreach (var r in recs.Values) if (r.initDone && r.a != null) r.Event("MARK", "user mark (" + Plugin.MarkKey.Value + ") | " + r.Snap() + " | " + r.GearStr(true));
}
internal static void PartDetached(UnitPart p)
{
try
{
var r = For(p); if (r == null) return;
if (!r.partLogT.ContainsKey("detach:" + p.name)) { r.partLogT["detach:" + p.name] = Time.time; r.partsLost++; r.Event("PART_LOST", $"part {p.name} detached | {r.Snap()}"); }
}
catch (Exception e) { Fault("Detach", e); }
}
static T[] All<T>(Aircraft ac) where T : Component
{
var set = new HashSet<T>();
foreach (var c in ac.GetComponentsInChildren<T>(true)) set.Add(c);
var parts = ac.GetAllParts();
if (parts != null) foreach (var p in parts) if (p != null) foreach (var c in p.GetComponentsInChildren<T>(true)) set.Add(c);
var arr = new T[set.Count]; set.CopyTo(arr); return arr;
}
static string F(float v, int d = 1) => float.IsNaN(v) || float.IsInfinity(v) ? "nan" : v.ToString("F" + d, IC);
static string V(Vector3 v, int d = 2) => "(" + F(v.x, d) + ", " + F(v.y, d) + ", " + F(v.z, d) + ")";
const float MS2KT = 1.943844f, M2FT = 3.28084f;
internal void Event(string tag, string msg)
{
string line = $"{F(Time.time - t0, 2),8} s  {tag,-14} {msg}";
try { ev?.WriteLine(line); } catch (Exception) { }
if (Plugin.EchoEvents.Value) Plugin.Log.LogInfo("[F14B-DBG] " + tag + ": " + msg);
}
internal string Snap()
{
return $"{phase} KEAS {F(eas * MS2KT, 0)} KTAS {F(tas * MS2KT, 0)} M{F(mach, 2)} GS {F(gs * MS2KT, 0)} kt, alt {F(altFt, 0)} ft (radar {F(raltFt, 0)}), VS {F(vsFpm, 0)} fpm, " +
$"pitch {F(pitch)} roll {F(roll)} hdg {F(hdg, 0)}, AoA {F(aoa)} beta {F(beta)}, g {F(gN, 2)}, rates p/q/r {F(pRate, 0)}/{F(qRate, 0)}/{F(rRate, 0)} deg/s, " +
$"in p/r/y/thr/brk {F(inp != null ? inp.pitch : 0, 2)}/{F(inp != null ? inp.roll : 0, 2)}/{F(inp != null ? inp.yaw : 0, 2)}/{F(inp != null ? inp.throttle : 0, 2)}/{F(inp != null ? inp.brake : 0, 2)}, " +
$"sweep {F(sweepDeg, 0)} deg, flap {F(flapPos, 2)}, SB {F(sbSplit, 2)}, gear {(gearCmd ? "DN" : "UP")}, thrust {F(thrustKN, 0)} kN, fuel {F(fuelKg, 0)} kg, mass {F(massKg, 0)} kg, tail clr {(tailClr >= 0 ? F(tailClr, 2) + " m" : "-")}";
}
internal string GearStr(bool full)
{
var sb = new StringBuilder("gear ");
foreach (var gi in gears)
{
sb.Append(gi.name).Append(gi.wow ? "[WOW " : "[air ").Append(F(gi.comp, 3)).Append(" m ").Append(F(gi.ratio * 100f, 0)).Append("% ").Append(F(gi.force / 1000f, 1)).Append(" kN");
if (full) sb.Append(" fold ").Append(F(gi.fold, 2));
sb.Append("] ");
}
return sb.ToString();
}
void Init()
{
initTries++;
rb = a.rb; if (rb == null) rb = a.CockpitRB();
if (rb == null) return;
key = a.definition != null ? a.definition.jsonKey : "unknown";
gears.Clear(); gearCols.Clear();
foreach (var g in All<LandingGear>(a))
{
if (g == null) continue;
var gi = new GearInfo { g = g, name = g.gameObject.name };
Vector3 lp = a.transform.InverseTransformPoint(g.transform.position);
gi.nose = lp.z > 2f; gi.main = !gi.nose;
gears.Add(gi); gearOwner[g] = this;
var c = fGearCol(g); if (c != null) gearCols.Add(c);
}
if (gears.Count == 0 && initTries < 300) return;
fans = All<Turbofan>(a); jets = All<Turbojet>(a);
foreach (var s in All<SwingWingController>(a)) { try { if (sweep == null || s.GetUpperAngleLimit() - s.GetLowerAngleLimit() > sweep.GetUpperAngleLimit() - sweep.GetLowerAngleLimit()) sweep = s; } catch (Exception) { } }
foreach (var h in All<HighLiftDevice>(a)) { if (flap == null) flap = h; }
foreach (var cs in All<ControlSurface>(a)) { try { if (fMaxSplit(cs) > 0f) brakes.Add(cs); } catch (Exception) { } }
var hooks = All<TailHook>(a); if (hooks.Length > 0) hook = hooks[0];
foreach (var t in All<Transform>(a)) { if (t.name == "F14_SpoilerHinge_L") { spL = t; spL0 = t.localRotation; } else if (t.name == "F14_SpoilerHinge_R") { spR = t; spR0 = t.localRotation; } }
t0 = Time.time;
string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", IC);
dir = Path.Combine(Plugin.Root, stamp + "_" + key);
Directory.CreateDirectory(dir);
csv = new StreamWriter(Path.Combine(dir, "telemetry.csv"), false, new UTF8Encoding(false), 1 << 16);
ev = new StreamWriter(Path.Combine(dir, "events.log"), false, new UTF8Encoding(false), 1 << 14);
initDone = true; lastActive = this;
WriteHeader();
Geometry();
Event("SESSION", $"recorder {Plugin.Version}, aircraft '{a.unitName}' key {key}, files in {dir}");
Describe();
}
void WriteHeader()
{
var sb = new StringBuilder("t_s,phase,ktas,keas,mach,gs_kt,alt_ft,ralt_ft,vs_fpm,pitch_deg,roll_deg,hdg_deg,aoa_deg,beta_deg,g,p_dps,q_dps,r_dps,turn_dps,accel_kts,in_pitch,in_roll,in_yaw,in_throttle,in_brake,flight_assist,gear_cmd,gear_state");
foreach (var gi in gears) sb.Append($",{gi.name}_wow,{gi.name}_comp_m,{gi.name}_comp_pct,{gi.name}_force_kN,{gi.name}_fold");
sb.Append(",sweep_deg,sweep_pos,flap_pos,speedbrake,hook_pos,hooked,thrust_kN,rpm_ratio,fuel_kg,fuel_flow_kgph,mass_kg,cg_y,cg_z,tail_clear_m,roll_dist_m,spoiler_L_deg,spoiler_R_deg,ejected");
csv.WriteLine(sb.ToString());
}
void Geometry()
{
float my = 0f, mz = 0f, ny = 0f, nz = 0f; int nm = 0, nn = 0;
foreach (var gi in gears)
{
// game: suspension ray from castPoint, length suspensionTravel; tyre bottom = castPoint - (travel - compression)
var cp = fCast(gi.g); Transform wt = cp != null ? cp : gi.g.transform;
Vector3 lp = a.transform.InverseTransformPoint(wt.position); float gyl = lp.y - (fTravel(gi.g) - fComp(gi.g));
if (gi.main) { my += gyl; mz += lp.z; nm++; } else { ny += gyl; nz += lp.z; nn++; }
}
if (nm > 0) mainContact = new Vector3(0f, my / nm, mz / nm);
if (nn > 0) noseContact = new Vector3(0f, ny / nn, nz / nn);
if (nm > 0 && nn > 0) levelPitch = Mathf.Atan2(mainContact.y - noseContact.y, noseContact.z - mainContact.z) * Mathf.Rad2Deg;
var cand = new List<KeyValuePair<float, Vector3>>();
foreach (var c in All<Collider>(a))
{
if (c == null || c.isTrigger || gearCols.Contains(c)) continue;
foreach (var w in Corners(c))
{
Vector3 lp = a.transform.InverseTransformPoint(w);
float dz = lp.z - mainContact.z, dy = lp.y - mainContact.y;
if (dz > -0.5f) continue;
float ang = Mathf.Atan2(dy, -dz) * Mathf.Rad2Deg;
cand.Add(new KeyValuePair<float, Vector3>(ang, lp));
if (ang < tailLimit) { tailLimit = ang; tailWho = c.name + " on " + (c.attachedRigidbody != null ? c.attachedRigidbody.name : c.transform.root.name); }
}
}
cand.Sort((x, y) => x.Key.CompareTo(y.Key));
for (int i = 0; i < cand.Count && tailPts.Count < 4; i++) tailPts.Add(cand[i].Value);
foreach (var mf in All<MeshFilter>(a))
{
if (mf == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
var mr = mf.GetComponent<MeshRenderer>(); if (mr == null || !mr.enabled) continue;
string n = mf.name;
if (n.Contains("Wheel") || n.Contains("Strut") || n.Contains("Fork") || n.Contains("Door") || n.Contains("Leaf") || n.Contains("Strip") || n.Contains("Piston") || n.Contains("Hook")) continue;
var vs = mf.sharedMesh.vertices;
for (int i = 0; i < vs.Length; i += 3)
{
Vector3 w = mf.transform.TransformPoint(vs[i]);
Vector3 lp = a.transform.InverseTransformPoint(w);
float dz = lp.z - mainContact.z, dy = lp.y - mainContact.y;
if (dz > -0.5f) continue;
float ang = Mathf.Atan2(dy, -dz) * Mathf.Rad2Deg;
if (ang < visTailLimit) { visTailLimit = ang; visTailWho = n; }
}
}
}
static IEnumerable<Vector3> Corners(Collider c)
{
if (c is BoxCollider bc)
{
Vector3 h = bc.size * 0.5f;
for (int i = 0; i < 8; i++)
yield return bc.transform.TransformPoint(bc.center + new Vector3((i & 1) == 0 ? -h.x : h.x, (i & 2) == 0 ? -h.y : h.y, (i & 4) == 0 ? -h.z : h.z));
yield break;
}
if (c is MeshCollider mc && mc.sharedMesh != null && mc.sharedMesh.isReadable)
{
var vs = mc.sharedMesh.vertices;
for (int i = 0; i < vs.Length; i++) yield return mc.transform.TransformPoint(vs[i]);
yield break;
}
var b = c.bounds;
for (int i = 0; i < 8; i++) yield return new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
}
void Describe()
{
massKg = a.GetMass();
float fuelQ = 0f; try { fuelQ = a.GetFuelQuantity(); } catch (Exception) { }
Vector3 cg = a.transform.InverseTransformPoint(a.GetCenterOfMass());
float maxT = 0f; foreach (var f in fans) if (f != null) maxT += f.GetMaxThrust(); foreach (var j in jets) if (j != null) maxT += j.GetMaxThrust();
float area = 0f; int nParts = 0; foreach (var p in All<AeroPart>(a)) { if (p == null) continue; nParts++; try { area += p.GetWingArea(); } catch (Exception) { } }
Event("AIRFRAME", $"mass {F(massKg, 0)} kg, fuel {F(fuelQ, 0)} kg ({F(a.GetFuelLevel() * 100f, 0)}%), CG local {V(cg)}, {nParts} aero parts, total lift area {F(area, 1)} m2, " +
$"{fans.Length} turbofans + {jets.Length} turbojets, max thrust {F(maxT / 1000f, 0)} kN (T/W {F(maxT / (massKg * 9.81f), 2)}), " +
$"sweep controller {(sweep != null ? sweep.name + " " + F(sweep.GetLowerAngleLimit(), 0) + "-" + F(sweep.GetUpperAngleLimit(), 0) + " deg" : "none")}, flap HLD {(flap != null ? flap.name : "none")}, speedbrake surfaces {brakes.Count}, hook {(hook != null ? "yes" : "no")}");
float wn = 0f;
if (Mathf.Abs(noseContact.z - mainContact.z) > 0.1f) wn = Mathf.Clamp01((cg.z - mainContact.z) / (noseContact.z - mainContact.z));
foreach (var gi in gears)
{
var g = gi.g; Vector3 lp = a.transform.InverseTransformPoint(g.transform.position);
var u = fUnsprung(g); Vector3 up = u != null ? a.transform.InverseTransformPoint(u.transform.position) : lp;
float share = gi.nose ? wn : (1f - wn) / Mathf.Max(1, CountMain());
float load = share * massKg * 9.81f, k = fSpring(g);
Event("GEAR_SETUP", $"{gi.name} ({(gi.nose ? "nose" : "main")}) hinge {V(lp)} axle {V(up)} wheel r {F(fWheelR(g), 2)} m, spring {F(k, 0)} N/m, damping {F(fDamp(g), 0)}, travel {F(fTravel(g), 2)} m, max comp {F(fMaxComp(g), 2)} m | " +
$"predicted static load {F(share * 100f, 1)}% = {F(load / 1000f, 1)} kN -> compression {F(k > 0 ? load / k : 0f, 3)} m");
}
Event("GEOMETRY", $"main contact {V(mainContact)} nose contact {V(noseContact)} (aircraft frame, m) -> pitch on equal-compression gear {F(levelPitch, 2)} deg (+ = nose up), wheelbase {F(noseContact.z - mainContact.z, 2)} m");
Event("TAIL_LIMIT", $"tail-strike pitch limit (colliders, rotating on the main wheels) {F(tailLimit, 1)} deg, first contact: {tailWho}; visual mesh limit {(visTailLimit < 89f ? F(visTailLimit, 1) + " deg (" + visTailWho + ")" : "n/a (meshes not readable)")}");
}
int CountMain() { int n = 0; foreach (var gi in gears) if (gi.main) n++; return n; }
void Step()
{
if (!initDone)
{
if (Time.time - firstSeen < 0.5f) return;
Init();
if (!initDone) return;
}
if (a.remoteSim || rb == null) return;
lastActive = this;
float dt = Time.fixedDeltaTime, now = Time.time;
var tr = a.transform;
Vector3 v = rb.velocity;
tas = a.speed; float rho = a.airDensity;
eas = tas * Mathf.Sqrt(Mathf.Max(rho, 0f) / 1.225f);
float altM = tr.GlobalPosition().y;
try { mach = tas / LevelInfo.GetSpeedOfSound(altM); } catch (Exception) { mach = tas / 340f; }
gs = new Vector3(v.x, 0f, v.z).magnitude;
altFt = altM * M2FT; raltFt = a.radarAlt * M2FT; vsFpm = v.y * M2FT * 60f;
Vector3 fwd = tr.forward, up = tr.up, right = tr.right;
pitch = Mathf.Asin(Mathf.Clamp(fwd.y, -1f, 1f)) * Mathf.Rad2Deg;
roll = Mathf.Atan2(-right.y, up.y) * Mathf.Rad2Deg;
hdg = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg; if (hdg < 0f) hdg += 360f;
Vector3 air = v - a.GetWindVelocity();
Vector3 lv = tr.InverseTransformDirection(air);
if (lv.magnitude > 5f) { aoa = Mathf.Atan2(-lv.y, lv.z) * Mathf.Rad2Deg; beta = Mathf.Atan2(lv.x, lv.z) * Mathf.Rad2Deg; } else { aoa = 0f; beta = 0f; }
gN = a.gForce;
Vector3 w = tr.InverseTransformDirection(rb.angularVelocity) * Mathf.Rad2Deg;
pRate = -w.z; qRate = -w.x; rRate = w.y;
if (hasPrev && dt > 0f)
{
accel = (tas - tasPrev) / dt * MS2KT;
Vector3 h0 = new Vector3(vPrev.x, 0, vPrev.z), h1 = new Vector3(v.x, 0, v.z);
turnRate = h0.sqrMagnitude > 1f && h1.sqrMagnitude > 1f ? Vector3.SignedAngle(h0, h1, Vector3.up) / dt : 0f;
}
vsPrevFpm = hasPrev ? vPrev.y * M2FT * 60f : vsFpm;
vPrev = v; tasPrev = tas; hasPrev = true;
inp = a.GetInputs();
gearCmd = a.gearDeployed;
anyWow = mainWow = noseWow = false;
foreach (var gi in gears)
{
if (gi.g == null) { gi.wow = false; continue; }
gi.wow = gi.g.WeightOnWheel(0.02f);
gi.comp = fComp(gi.g); float mc = fMaxComp(gi.g); gi.ratio = mc > 0f ? gi.comp / mc : 0f;
gi.force = fForce(gi.g) + fDampF(gi.g);
gi.foldPrev = gi.fold; gi.fold = fFold(gi.g);
if (!gi.g.enabled) { if (!gi.lost && gearCmd && gi.foldPrev < 0.05f) { gi.lost = true; gearBreaks++; Event("GEAR_LOST", $"{gi.name} wheel switched off by the game (BreakWheel: over-compression, soft-ground drag or hinge angle); last compression {F(gi.lastComp, 3)} m, ground depth {F(gi.lastDepth, 3)} m, KEAS {F(eas * MS2KT, 0)}, ground speed {F(gs * MS2KT, 0)} kt, brake {F(inp != null ? inp.brake : 0, 2)}, yaw {F(inp != null ? inp.yaw : 0, 2)} | {Snap()}"); } }
else { gi.lastComp = gi.comp; gi.lastDepth = GDepth(gi.g); }
if (gi.wow) { anyWow = true; if (gi.main) mainWow = true; else noseWow = true; }
if (gi.ratio > 0.95f && !gi.bottomed) { gi.bottomed = true; Event("GEAR_BOTTOMED", $"{gi.name} strut fully compressed ({F(gi.comp, 3)} m, {F(gi.force / 1000f, 0)} kN) | {Snap()}"); }
if (gi.ratio < 0.8f) gi.bottomed = false;
}
massKg = a.GetMass();
if (now - lastCg > 1f)
{
lastCg = now; cgLocal = tr.InverseTransformPoint(a.GetCenterOfMass());
float fq = 0f; try { fq = a.GetFuelQuantity(); } catch (Exception) { }
if (fuelPrev >= 0f && now > fuelPrevT) fuelFlow = (fuelPrev - fq) / (now - fuelPrevT) * 3600f;
fuelPrev = fq; fuelPrevT = now; fuelKg = fq;
}
thrustKN = 0f; rpm = 0f; int ne = 0;
foreach (var f in fans) if (f != null) { thrustKN += f.GetThrust() / 1000f; rpm += f.GetRPMRatio(); ne++; if (!fFanOk(f) && engineOut.Add(f.name)) Event("ENGINE_OUT", $"{f.name} not operable | {Snap()}"); }
foreach (var j in jets) if (j != null) { thrustKN += j.GetThrust() / 1000f; rpm += j.GetRPMRatio(); ne++; if (!fJetOk(j) && engineOut.Add(j.name)) Event("ENGINE_OUT", $"{j.name} not operable | {Snap()}"); }
if (ne > 0) rpm /= ne;
if (sweep != null) { try { sweepDeg = sweep.GetWingAngle(); sweepPos = sweep.GetSwingPosition(); } catch (Exception) { } }
if (flap != null) flapPos = fHld(flap);
sbSplit = 0f; foreach (var b in brakes) if (b != null) { float ms = fMaxSplit(b); if (ms > 0f) sbSplit = Mathf.Max(sbSplit, fSplit(b) / ms); }
if (hook != null) { hookAmt = fHookAmt(hook); hooked = fHooked(hook); }
spLdeg = spL != null ? Quaternion.Angle(spL0, spL.localRotation) : 0f; spRdeg = spR != null ? Quaternion.Angle(spR0, spR.localRotation) : 0f;
if (!ejected) { bool ej = false; try { ej = a.HasEjected(); } catch (Exception) { } if (ej) { ejected = true; Event("EJECTION", $"crew ejected at KEAS {F(eas * MS2KT, 0)} M{F(mach, 2)}, alt {F(altFt, 0)} ft, radar alt {F(raltFt, 0)} ft, pitch {F(pitch)}, roll {F(roll)}, g {F(gN, 2)} (later g/AoA/departure events are suppressed: the empty jet is tumbling) | {Snap()}"); } }
tailClr = -1f;
if (raltFt < 60f && tailPts.Count > 0) tailClr = TailClearance();
Track(dt, now);
Episodes(now);
if (now - lastRow >= 1f / Mathf.Max(1f, Plugin.TelemetryHz.Value)) { lastRow = now; Row(); }
if (Plugin.StatusInterval.Value > 0f && now - lastStatus >= Plugin.StatusInterval.Value)
{
lastStatus = now;
Plugin.Log.LogInfo($"[F14B-DBG] status: {phase} KEAS {F(eas * MS2KT, 0)} M{F(mach, 2)} alt {F(altFt, 0)} ft VS {F(vsFpm, 0)} AoA {F(aoa)} pitch {F(pitch)} g {F(gN, 2)} sweep {F(sweepDeg, 0)} thrust {F(thrustKN, 0)} kN fuel {F(fuelKg, 0)} kg {(anyWow ? GearStr(false) : "")}");
}
}
float TailClearance()
{
float best = 999f;
foreach (var p in tailPts)
{
Vector3 w = a.transform.TransformPoint(p);
int n = Physics.RaycastNonAlloc(w + Vector3.up * 3f, Vector3.down, hits, 80f, ~0, QueryTriggerInteraction.Ignore);
float d = 999f;
for (int i = 0; i < n; i++)
{
var c = hits[i].collider; if (c == null) continue;
var rbh = c.attachedRigidbody;
if (rbh != null) { var up = rbh.GetComponent<UnitPart>(); if (up != null && up.parentUnit == a) continue; }
if (hits[i].distance < d) d = hits[i].distance;
}
if (d < 999f) best = Mathf.Min(best, d - 3f);
}
return best < 999f ? best : -1f;
}
void SetPhase(Phase p, string why)
{
if (p == phase) return;
Event("PHASE", $"{phase} -> {p} ({why})");
phase = p; phaseT = Time.time;
}
void Track(float dt, float now)
{
bool onGround = anyWow;
if (onGround) { groundT += dt; airT = 0f; noWowT = 0f; } else { airT += dt; groundT = 0f; noWowT += dt; }
stillT = onGround && gs < 0.5f ? stillT + dt : 0f;
foreach (var gi in gears) gi.wowT = gi.wow ? gi.wowT + dt : 0f;
noseOffT = noseWow ? 0f : noseOffT + dt;
if (phase == Phase.Init) SetPhase(onGround ? Phase.Parked : Phase.Airborne, onGround ? "spawned on the ground" : "spawned in the air");
if (stillT > 3f && !staticLogged)
{
staticLogged = true;
float tot = 0f, fn = 0f; foreach (var gi in gears) { tot += Mathf.Max(0f, gi.force); if (gi.nose) fn += Mathf.Max(0f, gi.force); }
float clr = tailPts.Count > 0 ? TailClearance() : -1f;
Event("STATIC", $"stopped: pitch {F(pitch, 2)} deg roll {F(roll, 2)} deg, nose-wheel load {F(tot > 0 ? fn / tot * 100f : 0f, 1)}% of {F(tot / 1000f, 0)} kN (weight {F(massKg * 9.81f / 1000f, 0)} kN), CG {V(cgLocal)}, tail clearance {F(clr, 2)} m | {GearStr(false)}");
}
if (stillT == 0f && gs > 2f) staticLogged = false;
switch (phase)
{
case Phase.Parked:
case Phase.Taxi:
if (!onGround && noWowT > 0.5f) { SetPhase(Phase.Airborne, "left the ground"); break; }
if (phase == Phase.Parked && gs > 2f) SetPhase(Phase.Taxi, "moving");
if (phase == Phase.Taxi && stillT > 2f) SetPhase(Phase.Parked, "stopped");
float thr = inp != null ? inp.throttle : 0f;
if (!rolledStart && thr >= 0.6f && gs * MS2KT < 40f) { rolledStart = true; ResetRoll(now); }
if (rolledStart && thr < 0.4f && gs * MS2KT < 40f) rolledStart = false;
if (gs * MS2KT >= 40f && thr >= 0.6f)
{
if (!rolledStart) { ResetRoll(now); Event("TAKEOFF", "roll started already moving (timing from 40 kt)"); }
rolledStart = true;
SetPhase(Phase.TakeoffRoll, $"throttle {F(thr, 2)} through 40 kt");
}
if (rolledStart) rollDist += gs * dt;
break;
case Phase.TakeoffRoll:
rollDist += gs * dt;
rollMaxPitch = Mathf.Max(rollMaxPitch, pitch);
if (tailClr >= 0f) rollMinTail = Mathf.Min(rollMinTail, tailClr);
if (t100 < 0f && eas * MS2KT >= 100f) t100 = now - rollStartT;
if (rotateT < 0f && mainWow && noseOffT > 0.25f && GearT(true) > 0.2f)
{
rotateT = now - rollStartT; rotateKeas = eas * MS2KT; rotatePitch = pitch;
Event("ROTATE", $"nose wheel off at KEAS {F(rotateKeas, 0)} after {F(rotateT, 1)} s / {F(rollDist, 0)} m, stick {F(inp != null ? inp.pitch : 0, 2)} | {Snap()}");
}
if (!onGround && noWowT > 0.3f)
{
liftT = now - rollStartT - noWowT; liftKeas = eas * MS2KT; liftKtas = tas * MS2KT; liftPitch = pitch; liftAoa = aoa; liftDist = rollDist;
Event("LIFTOFF", $"KEAS {F(liftKeas, 0)} KTAS {F(liftKtas, 0)} pitch {F(liftPitch)} AoA {F(liftAoa)} after {F(liftT, 1)} s / {F(liftDist, 0)} m ({F(liftDist * M2FT, 0)} ft), max pitch on the ground {F(rollMaxPitch)} deg (tail limit {F(tailLimit)}), min tail clearance {(rollMinTail < 99f ? F(rollMinTail, 2) + " m" : "-")} | {Snap()}");
got50 = false;
SetPhase(Phase.Airborne, "liftoff");
break;
}
if (gs * MS2KT < 30f) { Event("TAKEOFF_ABORT", $"slowed below 30 kt after {F(now - rollStartT, 1)} s / {F(rollDist, 0)} m | {Snap()}"); rolledStart = false; SetPhase(Phase.Taxi, "takeoff aborted"); }
break;
case Phase.Airborne:
if (liftT >= 0f && !got50 && raltFt >= 50f)
{
got50 = true;
Event("TAKEOFF_SUMMARY", $"100 kt at {F(t100, 1)} s, rotate KEAS {F(rotateKeas, 0)} ({F(rotateT, 1)} s), liftoff KEAS {F(liftKeas, 0)} ({F(liftT, 1)} s, {F(liftDist, 0)} m), 50 ft at {F(now - rollStartT, 1)} s / {F(rollDist, 0)} m, mass {F(massKg, 0)} kg, sweep {F(sweepDeg, 0)}, flap {F(flapPos, 2)}");
}
if (liftT >= 0f && !got50) rollDist += gs * dt;
if (onGround) Touchdown(now);
break;
case Phase.Rollout:
rollDistLand += gs * dt;
if (inp != null) brakeMax = Mathf.Max(brakeMax, inp.brake);
landMaxDecel = Mathf.Max(landMaxDecel, -accel);
if (noseTdT < 0f && noseWow) { noseTdT = now - tdT; Event("NOSE_DOWN", $"nose wheel on {F(noseTdT, 2)} s after touchdown, KEAS {F(eas * MS2KT, 0)}, pitch rate {F(qRate, 1)} deg/s | {GearStr(false)}"); }
if (tdOpen && now - tdT > 1.5f) CloseTouchdown();
if (tdOpen) { tdPeakG = Mathf.Max(tdPeakG, gN); foreach (var gi in gears) { gi.peakRatio = Mathf.Max(gi.peakRatio, gi.ratio); gi.peakForce = Mathf.Max(gi.peakForce, gi.force); } }
if (!onGround && noWowT > 0.3f)
{
if (now - tdT < 6f) { bounces++; Event("BOUNCE", $"airborne again {F(now - tdT, 1)} s after touchdown (#{bounces}) | {Snap()}"); }
else Event("TOUCH_AND_GO", $"airborne again after {F(rollDistLand, 0)} m on the ground | {Snap()}");
if (tdOpen) CloseTouchdown();
SetPhase(Phase.Airborne, "bounce / go-around");
break;
}
if (gs * MS2KT < 5f)
{
Event("ROLLOUT", $"stopped after {F(now - tdT, 1)} s / {F(rollDistLand, 0)} m ({F(rollDistLand * M2FT, 0)} ft), max decel {F(landMaxDecel, 1)} kt/s, max brake {F(brakeMax, 2)}, hook {(wasHooked ? "caught a wire" : "no wire")}, bounces {bounces}");
SetPhase(Phase.Parked, "stopped");
rolledStart = false;
}
break;
}
}
float lastAirT, noseOffT;
float GearT(bool main) { float t = 99f; foreach (var gi in gears) if (gi.main == main) t = Mathf.Min(t, gi.wowT); return t; }
void ResetRoll(float now)
{
rollStartT = now; rollDist = 0f; rollMaxPitch = -90f; rollMinTail = 99f; t100 = -1f; rotateT = -1f; liftT = -1f; rotateKeas = 0f;
}
void Touchdown(float now)
{
if (phase == Phase.Airborne && now - phaseT > 8f) bounces = 0;
tdT = now; tdKeas = eas * MS2KT; tdSink = vsPrevFpm; tdPitch = pitch; tdAoa = aoa; tdRoll = roll; tdBeta = beta; tdPeakG = gN; tdOpen = true;
noseTdT = -1f; rollDistLand = 0f; landMaxDecel = 0f; brakeMax = 0f; wasHooked = hooked;
var first = new StringBuilder(); foreach (var gi in gears) { gi.peakRatio = gi.ratio; gi.peakForce = gi.force; if (gi.wow) first.Append(gi.name).Append(' '); }
tdFirst = first.ToString().Trim();
if (noseWow) noseTdT = 0f;
Event("TOUCHDOWN", $"first contact {tdFirst}{(noseWow && !mainWow ? " (NOSE FIRST!)" : "")}, KEAS {F(tdKeas, 0)}, sink {F(-tdSink, 0)} fpm, pitch {F(tdPitch)} (tail limit {F(tailLimit)}), AoA {F(tdAoa)}, roll {F(tdRoll)}, beta {F(tdBeta)}, hook {(hooked ? "hooked" : hookAmt > 0.5f ? "down" : "up")} | {Snap()}");
SetPhase(Phase.Rollout, "touchdown");
}
void CloseTouchdown()
{
tdOpen = false;
var sb = new StringBuilder();
foreach (var gi in gears) sb.Append(gi.name).Append(' ').Append(F(gi.peakRatio * 100f, 0)).Append("% ").Append(F(gi.peakForce / 1000f, 0)).Append(" kN; ");
string rating = -tdSink < 360f ? "smooth" : -tdSink < 700f ? "firm" : -tdSink < 1000f ? "hard (carrier-like)" : "very hard";
Event("TOUCHDOWN_SUMMARY", $"{rating}: sink {F(-tdSink, 0)} fpm, peak g {F(tdPeakG, 2)} in the first 1.5 s, peak strut use {sb}");
}
void Episodes(float now)
{
if (anyWow) lastAirT = 0f; else lastAirT += Time.fixedDeltaTime;
if (!anyWow)
{
maxMach = Mathf.Max(maxMach, mach); maxKeas = Mathf.Max(maxKeas, eas * MS2KT); maxAlt = Mathf.Max(maxAlt, altFt);
maxG = Mathf.Max(maxG, gN); minG = Mathf.Min(minG, gN); maxAoa = Mathf.Max(maxAoa, aoa);
}
bool gHi = !ejected && !anyWow && (gN > Plugin.OverG.Value || gN < Plugin.UnderG.Value);
if (gHi && !gEp) { gEp = true; gEpT = now; gEpPeak = gN; }
if (gEp) { if (Mathf.Abs(gN) > Mathf.Abs(gEpPeak)) gEpPeak = gN; if (!gHi) { gEp = false; Event(gEpPeak > 0 ? "HIGH_G" : "NEGATIVE_G", $"peak {F(gEpPeak, 2)} g for {F(now - gEpT, 1)} s at KEAS {F(eas * MS2KT, 0)} M{F(mach, 2)}, mass {F(massKg, 0)} kg, sweep {F(sweepDeg, 0)}, stick {F(inp != null ? inp.pitch : 0, 2)}, assist {(a.flightAssist ? "on" : "off")}"); } }
bool aHi = !ejected && !anyWow && tas > 20f && aoa > Plugin.HighAoA.Value;
if (aHi && !aoaEp) { aoaEp = true; aoaEpT = now; aoaEpPeak = aoa; aoaEpMinKeas = eas * MS2KT; }
if (aoaEp) { aoaEpPeak = Mathf.Max(aoaEpPeak, aoa); aoaEpMinKeas = Mathf.Min(aoaEpMinKeas, eas * MS2KT); if (!aHi) { aoaEp = false; Event("HIGH_AOA", $"peak AoA {F(aoaEpPeak)} deg for {F(now - aoaEpT, 1)} s, min KEAS {F(aoaEpMinKeas, 0)}, g {F(gN, 2)}, sweep {F(sweepDeg, 0)}, flap {F(flapPos, 2)}"); } }
if (!ejected && !anyWow && tas > 30f && (Mathf.Abs(beta) > 12f || Mathf.Abs(rRate) > 40f) && now - depLogT > 2f) { depLogT = now; Event("DEPARTURE?", $"sideslip {F(beta)} deg, yaw rate {F(rRate, 0)} deg/s, roll rate {F(pRate, 0)} | {Snap()}"); }
if (sweep != null && Mathf.Abs(sweepDeg - sweepLogged) >= 5f) { sweepLogged = sweepDeg; Event("SWEEP", $"{F(sweepDeg, 0)} deg (pos {F(sweepPos, 2)}) at M{F(mach, 2)} KEAS {F(eas * MS2KT, 0)}"); }
if (flap != null) { if (!flapOut && flapPos > 0.5f) { flapOut = true; Event("FLAPS", $"out ({F(flapPos, 2)}) at KEAS {F(eas * MS2KT, 0)}"); } else if (flapOut && flapPos < 0.1f) { flapOut = false; Event("FLAPS", $"in at KEAS {F(eas * MS2KT, 0)}"); } }
if (!sbOut && sbSplit > 0.2f) { sbOut = true; Event("SPEEDBRAKE", $"open ({F(sbSplit, 2)}) at KEAS {F(eas * MS2KT, 0)}"); } else if (sbOut && sbSplit < 0.05f) { sbOut = false; Event("SPEEDBRAKE", "closed"); }
if (hook != null)
{
if (!hookDown && hookAmt > 0.5f) { hookDown = true; Event("HOOK", $"down at KEAS {F(eas * MS2KT, 0)} radar {F(raltFt, 0)} ft"); } else if (hookDown && hookAmt < 0.1f) { hookDown = false; Event("HOOK", "up"); }
if (hooked && !wasHooked) { wasHooked = true; Event("ARREST", $"wire caught at GS {F(gs * MS2KT, 0)} kt | {Snap()}"); }
if (!hooked && wasHooked && phase != Phase.Rollout) wasHooked = false;
}
if (gearCmdT < 0f || gearCmd != lastGearCmd)
{
if (gearCmdT >= 0f) { Event("GEAR_CMD", $"gear {(gearCmd ? "DOWN" : "UP")} commanded at KEAS {F(eas * MS2KT, 0)} radar {F(raltFt, 0)} ft"); gearMoving = true; gearStillT = 0f; }
gearCmdT = now; lastGearCmd = gearCmd;
}
if (gearMoving)
{
bool moving = false; foreach (var gi in gears) if (Mathf.Abs(gi.fold - gi.foldPrev) > 1e-4f) moving = true;
gearStillT = moving ? 0f : gearStillT + Time.fixedDeltaTime;
string gst = a.gearState.ToString();
bool locked = gst.StartsWith("Locked");
if ((locked && now - gearCmdT > 0.5f) || now - gearCmdT > 20f) { gearMoving = false; Event("GEAR_DONE", $"gear {(gearCmd ? "down" : "up")} after {F(now - gearCmdT, 1)} s, state {gst} | {GearStr(true)}"); }
}
float fl = 1f; try { fl = a.GetFuelLevel(); } catch (Exception) { }
if (!fuel20 && fl < 0.2f) { fuel20 = true; Event("FUEL", $"below 20% ({F(fuelKg, 0)} kg), flow {F(fuelFlow, 0)} kg/h"); }
if (!fuel10 && fl < 0.1f) { fuel10 = true; Event("FUEL", $"below 10% ({F(fuelKg, 0)} kg), flow {F(fuelFlow, 0)} kg/h"); }
}
bool lastGearCmd;
void Row()
{
var sb = new StringBuilder(512);
void C(float x, int d) { sb.Append(',').Append(F(x, d)); }
sb.Append(F(Time.time - t0, 2)).Append(',').Append(phase);
C(tas * MS2KT, 1); C(eas * MS2KT, 1); C(mach, 3); C(gs * MS2KT, 1); C(altFt, 0); C(raltFt, 1); C(vsFpm, 0);
C(pitch, 2); C(roll, 2); C(hdg, 1); C(aoa, 2); C(beta, 2); C(gN, 2); C(pRate, 1); C(qRate, 1); C(rRate, 1); C(turnRate, 2); C(accel, 2);
C(inp != null ? inp.pitch : 0, 3); C(inp != null ? inp.roll : 0, 3); C(inp != null ? inp.yaw : 0, 3); C(inp != null ? inp.throttle : 0, 3); C(inp != null ? inp.brake : 0, 2);
sb.Append(',').Append(a.flightAssist ? 1 : 0).Append(',').Append(gearCmd ? 1 : 0).Append(',').Append(a.gearState);
foreach (var gi in gears) { sb.Append(',').Append(gi.wow ? 1 : 0); C(gi.comp, 3); C(gi.ratio * 100f, 1); C(gi.force / 1000f, 2); C(gi.fold, 3); }
C(sweepDeg, 1); C(sweepPos, 3); C(flapPos, 3); C(sbSplit, 3); C(hookAmt, 2); sb.Append(',').Append(hooked ? 1 : 0);
C(thrustKN, 1); C(rpm, 3); C(fuelKg, 0); C(fuelFlow, 0); C(massKg, 0); C(cgLocal.y, 3); C(cgLocal.z, 3); C(tailClr, 2);
C(phase == Phase.Rollout ? rollDistLand : rollDist, 0);
C(spLdeg, 1); C(spRdeg, 1); sb.Append(',').Append(ejected ? 1 : 0);
try { csv.WriteLine(sb.ToString()); rows++; } catch (Exception) { }
}
internal void OnCollision(AeroPart part, Collision col)
{
if (a == null || col == null) return;
int n = col.contactCount; if (n == 0) return;
var c = col.GetContact(0);
var other = c.otherCollider;
if (other != null && other.attachedRigidbody != null)
{
var op = other.attachedRigidbody.GetComponent<UnitPart>();
if (op != null && op.parentUnit == a) return;
}
Vector3 lp = a.transform.InverseTransformPoint(c.point);
bool gearHit = c.thisCollider != null && gearCols.Contains(c.thisCollider);
string kind;
if (gearHit) kind = "GEAR_COLLIDER";
else if (lp.z < mainContact.z - 0.5f && Mathf.Abs(lp.x) < 3.5f) kind = "TAIL_STRIKE";
else if (Mathf.Abs(lp.x) > 3.5f) kind = "WING_STRIKE";
else if (lp.z > mainContact.z + 3f) kind = "NOSE_STRIKE";
else kind = "BELLY_STRIKE";
string k = kind + part.name;
if (partLogT.TryGetValue(k, out var lt) && Time.time - lt < 0.5f) return;
partLogT[k] = Time.time;
if (kind == "TAIL_STRIKE") tailStrikes++; else if (!gearHit) scrapes++;
Event(kind, $"part {part.name} collider '{(c.thisCollider != null ? c.thisCollider.name : "?")}' hit '{(other != null ? other.name : "?")}' (layer {(other != null ? other.gameObject.layer : -1)}) at aircraft-frame {V(lp)}, rel speed {F(col.relativeVelocity.magnitude, 1)} m/s, impulse {F(col.impulse.magnitude, 0)} N*s, pitch {F(pitch)} (limit {F(tailLimit)}) | {Snap()} | {GearStr(false)}");
}
internal void OnDamage(AeroPart part, float pierce, float blast, float fire, float impact)
{
if (pierce + blast + fire + impact <= 0f) return;
string k = "dmg" + part.name;
if (partLogT.TryGetValue(k, out var lt) && Time.time - lt < 0.5f) return;
partLogT[k] = Time.time;
Event("DAMAGE", $"part {part.name}: pierce {F(pierce, 1)} blast {F(blast, 1)} fire {F(fire, 1)} impact {F(impact, 1)}, hp left {F(part.hitPoints, 1)} | {Snap()}");
}
internal void OnGearBreak(LandingGear g)
{
gearBreaks++;
Event("GEAR_BROKEN", $"{g.gameObject.name} broke: compression {F(fComp(g), 3)} / max {F(fMaxComp(g), 3)} m, force {F(fForce(g) / 1000f, 0)} kN, sink {F(-vsFpm, 0)} fpm (prev {F(-vsPrevFpm, 0)}) | {Snap()}");
}
void Close(string why)
{
if (!initDone || csv == null) return;
try
{
Event("SESSION_END", $"{why}. Max M{F(maxMach, 2)}, max KEAS {F(maxKeas, 0)}, max alt {F(maxAlt, 0)} ft, g {F(minG, 2)} to {F(maxG, 2)}, max AoA {F(maxAoa)}, tail strikes {tailStrikes}, other strikes {scrapes}, gear broken {gearBreaks}, parts lost {partsLost}, {rows} telemetry rows");
csv.Flush(); csv.Close(); ev.Flush(); ev.Close();
}
catch (Exception) { }
csv = null; ev = null;
}
static GUIStyle style; static Texture2D bg;
internal static void DrawOverlay()
{
var r = lastActive;
if (r == null || r.a == null || !r.initDone) return;
if (style == null)
{
style = new GUIStyle(GUI.skin.label) { fontSize = 13, richText = true };
style.normal.textColor = Color.white;
bg = new Texture2D(1, 1); bg.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.55f)); bg.Apply();
}
string txt =
$"<b>{r.key} DEBUG</b>  REC  [{Plugin.MarkKey.Value} mark  {Plugin.OverlayKey.Value} hide]  {r.phase}\n" +
$"KEAS {F(r.eas * MS2KT, 0)}  KTAS {F(r.tas * MS2KT, 0)}  M{F(r.mach, 2)}  ALT {F(r.altFt, 0)} ft  RA {F(r.raltFt, 0)}  VS {F(r.vsFpm, 0)}\n" +
$"AoA {F(r.aoa)}  beta {F(r.beta)}  pitch {F(r.pitch)}  roll {F(r.roll)}  g {F(r.gN, 2)}  p/q/r {F(r.pRate, 0)}/{F(r.qRate, 0)}/{F(r.rRate, 0)}\n" +
$"sweep {F(r.sweepDeg, 0)}  flap {F(r.flapPos, 2)}  SB {F(r.sbSplit, 2)}  thrust {F(r.thrustKN, 0)} kN  fuel {F(r.fuelKg, 0)} kg  {F(r.fuelFlow, 0)} kg/h\n" +
$"{r.GearStr(false)}\n" +
$"tail clr {(r.tailClr >= 0 ? F(r.tailClr, 2) + " m" : "-")} (limit {F(r.tailLimit)} deg)  strikes {r.tailStrikes}/{r.scrapes}  roll dist {F(r.phase == Phase.Rollout ? r.rollDistLand : r.rollDist, 0)} m";
var rect = new Rect(8, 8, 660, 112);
GUI.DrawTexture(rect, bg);
GUI.Label(new Rect(14, 10, 650, 110), txt, style);
}
}
}

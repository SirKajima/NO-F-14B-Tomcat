using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Dev-only helpers for the F-14B build (never shipped).
public static class F14Tools
{
    public const string Out = "E:/Project/NOModding/F14Bridge/";

    public static string Path(Transform t, Transform root)
    {
        var s = new List<string>();
        while (t != null && t != root) { s.Add(t.name); t = t.parent; }
        s.Reverse();
        return string.Join("/", s);
    }

    public static string DumpSO(UnityEngine.Object o, Transform root, int maxArray = 40)
    {
        var sb = new StringBuilder();
        var so = new SerializedObject(o);
        var it = so.GetIterator();
        bool enter = true;
        while (it.Next(enter))
        {
            enter = true;
            if (it.propertyPath.StartsWith("m_") && it.depth == 0 && it.propertyPath != "m_Mesh" && it.propertyPath != "m_Center" && it.propertyPath != "m_Size" && it.propertyPath != "m_Materials") { enter = false; continue; }
            if (it.isArray && it.propertyType != SerializedPropertyType.String && it.arraySize > maxArray) { sb.AppendLine(new string(' ', it.depth * 2) + it.name + " [" + it.arraySize + "] (skipped)"); enter = false; continue; }
            string v = "";
            switch (it.propertyType)
            {
                case SerializedPropertyType.Integer: v = it.intValue.ToString(); break;
                case SerializedPropertyType.Boolean: v = it.boolValue.ToString(); break;
                case SerializedPropertyType.Float: v = it.floatValue.ToString("G5"); break;
                case SerializedPropertyType.String: v = it.stringValue; break;
                case SerializedPropertyType.Enum: v = it.enumValueIndex.ToString(); break;
                case SerializedPropertyType.Vector3: v = it.vector3Value.ToString("F3"); enter = false; break;
                case SerializedPropertyType.Vector2: v = it.vector2Value.ToString("F3"); enter = false; break;
                case SerializedPropertyType.Quaternion: v = it.quaternionValue.eulerAngles.ToString("F2"); enter = false; break;
                case SerializedPropertyType.AnimationCurve:
                    var c = it.animationCurveValue; v = "curve " + string.Join(" ", c.keys.Select(k => "(" + k.time.ToString("G4") + "," + k.value.ToString("G4") + ")")); break;
                case SerializedPropertyType.ObjectReference:
                    var r = it.objectReferenceValue;
                    if (r == null) v = "null";
                    else if (r is Component cc) v = cc.GetType().Name + "@" + Path(cc.transform, root);
                    else if (r is GameObject g) v = "GO@" + Path(g.transform, root);
                    else v = r.GetType().Name + ":" + r.name;
                    break;
                case SerializedPropertyType.Generic: v = it.isArray ? "[" + it.arraySize + "]" : ""; break;
                default: v = it.propertyType.ToString(); break;
            }
            if (it.name == "Array" || it.name == "size") continue;
            sb.AppendLine(new string(' ', it.depth * 2) + it.name + " = " + v);
        }
        return sb.ToString();
    }

    public static void DumpPrefab(string assetPath, string outName)
    {
        var root = PrefabUtility.LoadPrefabContents(assetPath);
        try
        {
            var sb = new StringBuilder();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                sb.AppendLine("### " + Path(t, root.transform) + "  lp=" + t.localPosition.ToString("F3") + " le=" + t.localEulerAngles.ToString("F1") + " ls=" + t.localScale.ToString("F3") + " wp=" + t.position.ToString("F3") + (t.gameObject.activeSelf ? "" : " INACTIVE") + " layer=" + t.gameObject.layer);
                foreach (var c in t.GetComponents<Component>())
                {
                    if (c == null) { sb.AppendLine("  <missing script>"); continue; }
                    if (c is Transform) continue;
                    sb.AppendLine("  [" + c.GetType().Name + "]");
                    if (c is MeshFilter mf) { sb.AppendLine("    mesh = " + (mf.sharedMesh ? mf.sharedMesh.name + " v" + mf.sharedMesh.vertexCount : "null")); continue; }
                    if (c is MeshRenderer mr) { sb.AppendLine("    enabled=" + mr.enabled + " mats=" + string.Join(",", mr.sharedMaterials.Select(m => m ? m.name : "null"))); continue; }
                    if (c is ParticleSystem || c is ParticleSystemRenderer || c is AudioSource || c is Light) continue;
                    foreach (var line in DumpSO(c, root.transform).Split('\n'))
                        if (line.Trim().Length > 0) sb.AppendLine("    " + line.TrimEnd());
                }
            }
            File.WriteAllText(Out + outName, sb.ToString());
            Debug.Log("[F14] dumped " + assetPath + " -> " + outName);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    public static void DumpAsset(string assetPath, string outName)
    {
        var sb = new StringBuilder();
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(assetPath))
        {
            if (o == null) continue;
            sb.AppendLine("### " + o.GetType().Name + " " + o.name);
            sb.AppendLine(DumpSO(o, null, 200));
        }
        File.WriteAllText(Out + outName, sb.ToString());
    }

    public static void DumpAB4()
    {
        DumpPrefab("Assets/Blueprinter/_donotship/GameObject/FastBomber1_PLACEHOLDER.prefab", "dump_ab4.txt");
    }
    public static void DumpHornet()
    {
        DumpPrefab("Assets/Blueprinter/Mods/FA18C/FA18C_FlyingPrefab.prefab", "dump_fa18c.txt");
        DumpAsset("Assets/Blueprinter/Mods/FA18C/FA18C_Parameters.asset", "dump_fa18c_params.txt");
        DumpAsset("Assets/Blueprinter/Mods/FA18C/FA18C_AircraftDefinition.asset", "dump_fa18c_def.txt");
    }
    public static void DumpAB4Assets()
    {
        var guids = AssetDatabase.FindAssets("FastBomber1", new[] { "Assets/Blueprinter/_donotship" });
        var sb = new StringBuilder();
        foreach (var g in guids) sb.AppendLine(AssetDatabase.GUIDToAssetPath(g));
        File.WriteAllText(Out + "ab4_assets.txt", sb.ToString());
    }
}

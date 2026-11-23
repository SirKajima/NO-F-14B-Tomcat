using System; using System.IO; using System.Linq; using System.Reflection; using System.Text; using UnityEditor; using UnityEngine;
// Dev-only read-only probes for the F-14B build (never shipped).
public static class F14Probe
{
    const string D = "E:/Project/NOModding/F14Bridge/";
    // type names in Assembly-CSharp containing any keyword from probe_kw.txt
    public static void TypeSearch()
    {
        var asm = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "Assembly-CSharp");
        var kws = File.ReadAllLines(D + "probe_kw.txt").Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
        var sb = new StringBuilder();
        foreach (var t in asm.GetTypes().OrderBy(t => t.FullName))
            if (kws.Any(k => t.FullName.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0))
                sb.AppendLine(t.FullName + " : " + (t.BaseType != null ? t.BaseType.Name : ""));
        File.WriteAllText(D + "probe_types.txt", sb.ToString());
        Debug.Log("[F14] type search done");
    }
    // full prefab dumps of vanilla aircraft whose names are listed in probe_prefabs.txt (asset paths)
    public static void DumpPrefabs()
    {
        foreach (var l in File.ReadAllLines(D + "probe_prefabs.txt"))
        {
            var p = l.Trim(); if (p.Length == 0) continue;
            F14Tools.DumpPrefab(p, "dump_" + Path.GetFileNameWithoutExtension(p) + ".txt");
        }
        Debug.Log("[F14] prefab dumps done");
    }
    // list prefabs/assets under _donotship whose name contains a keyword from probe_kw.txt
    public static void AssetSearch()
    {
        var kws = File.ReadAllLines(D + "probe_kw.txt").Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
        var sb = new StringBuilder();
        foreach (var g in AssetDatabase.FindAssets("", new[] { "Assets/Blueprinter/_donotship" }))
        {
            var p = AssetDatabase.GUIDToAssetPath(g);
            if (kws.Any(k => Path.GetFileName(p).IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0)) sb.AppendLine(p);
        }
        File.WriteAllText(D + "probe_assets.txt", sb.ToString());
        Debug.Log("[F14] asset search done");
    }
    public static void DumpF14() { F14Tools.DumpPrefab(F14Build.PrefabPath, "dump_f14b.txt"); }
}
// rev3

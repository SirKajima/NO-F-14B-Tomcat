using System.IO; using System.Linq; using UnityEditor; using UnityEngine;
// 0.1.4 verification renders (dev-only).
public static class F14Shot3
{
    static void Shot(Vector3 dir, Vector3 up, float size, string file, Vector3 center, int w = 900, int h = 700)
    {
        var camGo = new GameObject("F14ShotCam"); var cam = camGo.AddComponent<Camera>();
        cam.orthographic = true; cam.orthographicSize = size; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.25f, 0.27f, 0.3f);
        cam.transform.position = center - dir * 50f; cam.transform.rotation = Quaternion.LookRotation(dir, up);
        cam.nearClipPlane = 0.1f; cam.farClipPlane = 200f;
        var rt = new RenderTexture(w, h, 24); cam.targetTexture = rt; cam.aspect = (float)w / h;
        var lightGo = new GameObject("F14ShotLight"); var li = lightGo.AddComponent<Light>(); li.type = LightType.Directional; li.intensity = 1.2f; lightGo.transform.rotation = Quaternion.LookRotation(dir + Vector3.down * 0.6f + Vector3.right * 0.3f);
        cam.Render();
        RenderTexture.active = rt; var tex = new Texture2D(w, h, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
        Directory.CreateDirectory(F14Tools.Out + "s3"); File.WriteAllBytes(F14Tools.Out + "s3/" + file, tex.EncodeToJPG(85));
        RenderTexture.active = null; Object.DestroyImmediate(camGo); Object.DestroyImmediate(lightGo); Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
    }
    static float GF(Component c, string p) => new SerializedObject(c).FindProperty(p).floatValue;
    static void Gear(GameObject go, float fold)
    {
        foreach (var lg in go.GetComponentsInChildren<Component>(true).Where(c => c != null && c.GetType().Name == "LandingGear"))
        {
            var so = new SerializedObject(lg);
            var hinge = so.FindProperty("gearHinge").objectReferenceValue as Transform;
            float fd = so.FindProperty("foldDegrees").floatValue; var fm = so.FindProperty("hingeFoldMotion").vector3Value;
            if (hinge) { hinge.localEulerAngles = new Vector3(fd * fold, 0, 0); hinge.localPosition = fm * fold; }
            var sr = so.FindProperty("strutRotationTransform").objectReferenceValue as Transform;
            if (sr) sr.localEulerAngles = new Vector3(0, so.FindProperty("strutRotation").floatValue * fold, 0);
            var d = so.FindProperty("gearDoors");
            for (int i = 0; i < d.arraySize; i++) { var e = d.GetArrayElementAtIndex(i); var t = e.FindPropertyRelative("transform").objectReferenceValue as Transform; if (t) t.localEulerAngles = Vector3.Lerp(e.FindPropertyRelative("openAngle").vector3Value, e.FindPropertyRelative("closedAngle").vector3Value, fold); }
            var mp = so.FindProperty("movingParts");
            for (int i = 0; i < mp.arraySize; i++) { var e = mp.GetArrayElementAtIndex(i); var t = e.FindPropertyRelative("transform").objectReferenceValue as Transform; var tg = e.FindPropertyRelative("target").objectReferenceValue as Transform; if (t && tg) t.LookAt(tg); }
        }
    }
    static void Weapons(GameObject go)
    {
        var wm = go.GetComponentsInChildren<Component>(true).First(c => c != null && c.GetType().Name == "WeaponManager");
        var sets = new SerializedObject(wm).FindProperty("hardpointSets");
        for (int i = 1; i < sets.arraySize; i++)
        {
            var s = sets.GetArrayElementAtIndex(i); var o = s.FindPropertyRelative("weaponOptions");
            UnityEngine.Object opt = null; for (int k = 0; k < o.arraySize; k++) { var x = o.GetArrayElementAtIndex(k).objectReferenceValue; if (x && (x.name.Contains("AAM2") || x.name.Contains("AAM1"))) { opt = x; if (x.name.Contains("single")) break; } }
            if (!opt) continue;
            var pf = new SerializedObject(opt).FindProperty("prefab").objectReferenceValue as GameObject; if (!pf) continue;
            var hp = s.FindPropertyRelative("hardpoints");
            for (int k = 0; k < hp.arraySize; k++) { var t = hp.GetArrayElementAtIndex(k).FindPropertyRelative("transform").objectReferenceValue as Transform; if (!t) continue; var inst = (GameObject)PrefabUtility.InstantiatePrefab(pf); inst.transform.SetParent(t, false); inst.transform.localPosition = Vector3.zero; inst.transform.localRotation = Quaternion.identity; }
        }
    }
    public static void Run()
    {
        var p = AssetDatabase.LoadAssetAtPath<GameObject>(F14Build.PrefabPath);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(p);
        PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        go.transform.position = new Vector3(0, 1000, 0);
        var c = go.transform.position;
        try
        {
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) if (t.name == "ladder") t.gameObject.SetActive(true);
            try { Weapons(go); } catch (System.Exception e) { Debug.Log("[F14] shot3 weapons: " + e.Message); }
            Gear(go, 0f);
            Shot(new Vector3(0.75f, 0.45f, -0.5f).normalized, Vector3.up, 2.2f, "main_iso_down.jpg", c + new Vector3(-2.2f, -0.4f, 0.0f));
            Shot(Vector3.forward, Vector3.up, 2.0f, "main_front_down.jpg", c + new Vector3(-2.2f, -0.5f, 0));
            Shot(Vector3.left, Vector3.up, 2.2f, "main_side_down.jpg", c + new Vector3(-2.2f, -0.5f, 0.4f));
            Shot(Vector3.right, Vector3.up, 1.8f, "nose_side_down.jpg", c + new Vector3(0, -0.7f, 6.3f));
            Shot(Vector3.left, Vector3.up, 5.5f, "full_side_down.jpg", c + new Vector3(0, -0.2f, 0.5f));
            Shot(Vector3.right, Vector3.up, 2.4f, "cockpit_ladder.jpg", c + new Vector3(0, -0.2f, 5.4f));
            Shot(new Vector3(-0.6f, -0.55f, -0.6f).normalized, Vector3.up, 1.4f, "cockpit_iso.jpg", c + new Vector3(0, 0.9f, 5.6f));
            Shot(Vector3.forward, Vector3.up, 2.5f, "rear_nozzles.jpg", c + new Vector3(0, 0.2f, -6f));
            Shot(new Vector3(0.4f, 0.2f, 1f).normalized, Vector3.up, 2.2f, "pylons_front.jpg", c + new Vector3(-2.6f, -0.2f, 1.0f));
            Shot(Vector3.forward, Vector3.up, 2.6f, "pylons_front2.jpg", c + new Vector3(0f, -0.3f, 2.5f));
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) if (t.name.StartsWith("F14_SpoilerHinge_") || t.name.StartsWith("F14_SlatHinge_")) t.localRotation = t.localRotation * Quaternion.Euler(t.name.StartsWith("F14_Slat") ? 17f : 45f, 0, 0);
            foreach (var th in go.GetComponentsInChildren<Component>(true).Where(x => x != null && x.GetType().Name == "TailHook")) { var so = new SerializedObject(th); var hk = so.FindProperty("hook").objectReferenceValue as Transform; if (hk) hk.localEulerAngles = new Vector3(so.FindProperty("deployedAngle").floatValue, 0, 0); }
            Shot(new Vector3(0.5f, -0.8f, -0.35f).normalized, Vector3.forward, 6f, "spoilers_up.jpg", c + new Vector3(-4.5f, 0.5f, -1.5f));
            Shot(Vector3.left, Vector3.up, 3.2f, "hook_down.jpg", c + new Vector3(0, -0.6f, -5.6f));
            Shot(new Vector3(-0.35f, 0.1f, 0.93f).normalized, Vector3.up, 1.6f, "slat_out_le.jpg", c + new Vector3(-6.2f, 0.45f, 0.4f));
            Shot(new Vector3(-0.95f, 0.0f, -0.3f).normalized, Vector3.up, 1.2f, "slat_out_section.jpg", c + new Vector3(-4.2f, 0.45f, 0.2f));
            Gear(go, 0.5f);
            Shot(new Vector3(0.75f, 0.45f, -0.5f).normalized, Vector3.up, 2.4f, "main_iso_half.jpg", c + new Vector3(-2.2f, -0.2f, 0.4f));
            Gear(go, 1f);
            Shot(Vector3.up, Vector3.forward, 5.0f, "bottom_up.jpg", c + new Vector3(0, 0, 2.0f));
            Shot(new Vector3(0.75f, 0.6f, -0.3f).normalized, Vector3.up, 2.2f, "main_iso_up.jpg", c + new Vector3(-2.2f, 0.1f, 0.6f));
            Shot(Vector3.left, Vector3.up, 2.2f, "main_side_up.jpg", c + new Vector3(-2.2f, 0.0f, 0.6f));
            Shot(Vector3.right, Vector3.up, 1.8f, "nose_side_up.jpg", c + new Vector3(0, -0.4f, 6.5f));
            Shot(new Vector3(0.6f, 0.75f, 0.3f).normalized, Vector3.up, 2.4f, "main_iso_up_below.jpg", c + new Vector3(-2.0f, 0.2f, 1.0f));
        }
        finally { Object.DestroyImmediate(go); }
        Debug.Log("[F14] shot3 done");
    }
}

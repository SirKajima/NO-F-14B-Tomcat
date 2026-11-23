using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Dev-only build script for the F-14B Tomcat mod (lives in Assets/Editor, never shipped).
public static partial class F14Build
{
    public const string Mod = "Assets/Blueprinter/Mods/F14B";
    public const string Fbx = Mod + "/Model/F14B.fbx";
    public const string MatDir = Mod + "/Model/Materials";
    public const string TexDir = Mod + "/Model/Textures";
    static void L(string s) { Debug.Log("[F14] " + s); }

    public static Material LoadMat(string blenderName) => AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/F14B_" + blenderName.Substring(0, 8) + ".mat");

    public static void Step1_Import()
    {
        AssetDatabase.Refresh();
        // ---- textures
        foreach (var p in Directory.GetFiles(TexDir, "*.png"))
        {
            var ap = p.Replace('\\', '/');
            var ti = AssetImporter.GetAtPath(ap) as TextureImporter;
            if (ti == null) { L("no importer " + ap); continue; }
            bool n = ap.EndsWith("_n.png"), ms = ap.EndsWith("_ms.png");
            ti.textureType = n ? TextureImporterType.NormalMap : TextureImporterType.Default;
            ti.sRGBTexture = !(n || ms);
            ti.maxTextureSize = 4096;
            ti.textureCompression = TextureImporterCompression.Compressed;
            ti.mipmapEnabled = true;
            ti.SaveAndReimport();
        }
        // ---- livery shader (copy of the Hornet's URP Lit + _Livery albedo)
        string shSrc = "Assets/Blueprinter/Mods/FA18C/Model040/Shaders/FA18C_LiveryLit.shader";
        string shDst = Mod + "/Model/Shaders/F14B_LiveryLit.shader";
        if (!AssetDatabase.IsValidFolder(Mod + "/Model/Shaders")) AssetDatabase.CreateFolder(Mod + "/Model", "Shaders");
        var txt = File.ReadAllText(shSrc);
        int q0 = txt.IndexOf('"'), q1 = txt.IndexOf('"', q0 + 1);
        string oldName = txt.Substring(q0 + 1, q1 - q0 - 1);
        txt = txt.Substring(0, q0 + 1) + "F14B/LiveryLit" + txt.Substring(q1);
        File.WriteAllText(shDst, txt);
        AssetDatabase.ImportAsset(shDst);
        L("livery shader copied (was '" + oldName + "')");
        var livery = AssetDatabase.LoadAssetAtPath<Shader>(shDst);
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        // ---- materials
        if (!AssetDatabase.IsValidFolder(MatDir)) AssetDatabase.CreateFolder(Mod + "/Model", "Materials");
        foreach (var md in F14Data.Mats)
        {
            string path = MatDir + "/F14B_" + md.name.Substring(0, 8) + ".mat";
            bool atlas = md.name.StartsWith("8bc3cf9c");
            var m = new Material(atlas && livery != null ? livery : lit);
            m.name = "F14B_" + md.name.Substring(0, 8);
            m.SetFloat("_Cull", 0f);
            if (md.baseTex != null)
            {
                var bt = AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "/" + md.baseTex);
                var nt = AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "/" + md.normalTex);
                m.SetTexture("_BaseMap", bt);
                if (atlas) m.SetTexture("_Livery", bt);
                m.SetColor("_BaseColor", Color.white);
                if (nt != null) { m.SetTexture("_BumpMap", nt); m.EnableKeyword("_NORMALMAP"); }
                m.SetFloat("_Metallic", 0.1f);
                m.SetFloat("_Smoothness", 0.45f);
                if (!atlas)
                {
                    m.SetFloat("_AlphaClip", 1f); m.SetFloat("_Cutoff", 0.5f); m.EnableKeyword("_ALPHATEST_ON");
                    m.renderQueue = (int)RenderQueue.AlphaTest;
                }
            }
            else
            {
                m.SetColor("_BaseColor", md.color);
                m.SetFloat("_Smoothness", 0.9f);
                if (md.transparent)
                {
                    m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f);
                    m.SetOverrideTag("RenderType", "Transparent");
                    m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                    m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                    m.SetInt("_ZWrite", 0);
                    m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    m.renderQueue = (int)RenderQueue.Transparent;
                }
            }
            var old = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (old != null) { EditorUtility.CopySerialized(m, old); }
            else AssetDatabase.CreateAsset(m, path);
        }
        AssetDatabase.SaveAssets();
        // ---- FBX importer
        var mi = AssetImporter.GetAtPath(Fbx) as ModelImporter;
        mi.globalScale = 1f; mi.useFileScale = true; mi.bakeAxisConversion = false;
        mi.isReadable = true;
        mi.importNormals = ModelImporterNormals.Import;
        mi.importTangents = ModelImporterTangents.CalculateMikk;
        mi.meshCompression = ModelImporterMeshCompression.Off;
        mi.indexFormat = ModelImporterIndexFormat.Auto;
        mi.importAnimation = false; mi.importCameras = false; mi.importLights = false;
        mi.animationType = ModelImporterAnimationType.None;
        mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        mi.materialLocation = ModelImporterMaterialLocation.InPrefab;
        foreach (var md in F14Data.Mats)
            mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), md.name), LoadMat(md.name));
        mi.SaveAndReimport();
        // ---- report
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
        var sb = new StringBuilder();
        foreach (Transform c in model.transform)
        {
            var mf = c.GetComponent<MeshFilter>(); var mr = c.GetComponent<MeshRenderer>();
            var b = mr ? mr.bounds : new Bounds();
            sb.AppendLine(c.name + " pos=" + c.position.ToString("F3") + " rot=" + c.eulerAngles.ToString("F1") + " scale=" + c.lossyScale.ToString("F3") +
                " v=" + (mf ? mf.sharedMesh.vertexCount : 0) + " sub=" + (mf ? mf.sharedMesh.subMeshCount : 0) + " bmin=" + b.min.ToString("F2") + " bmax=" + b.max.ToString("F2") +
                " mats=" + (mr ? string.Join(",", mr.sharedMaterials.Select(x => x ? x.name : "null")) : ""));
        }
        File.WriteAllText(F14Tools.Out + "fbx_report.txt", sb.ToString());
        L("Step1 done");
    }
}

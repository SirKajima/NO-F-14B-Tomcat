using UnityEngine;
// Generated from f14_export_info.json (Blender export). Unity coords.
public static class F14Data {
    public static readonly Vector3 WingPivot_L = new Vector3(-2.7430f, 0.6100f, 0.3120f);
    public static readonly Vector3 FlapP_L = new Vector3(-5.7275f, 0.5119f, -1.7875f);
    public static readonly Vector3 FlapD_L = new Vector3(-0.9936f, 0.0027f, -0.1126f);
    public static readonly Vector3 RudderP_L = new Vector3(-1.5134f, 1.5874f, -6.3414f);
    public static readonly Vector3 RudderD_L = new Vector3(-0.0801f, 0.8502f, -0.5203f);
    public static readonly Vector3 StabP_L = new Vector3(-2.0069f, 0.0609f, -5.8207f);
    public static readonly Vector3 SBLowP_L = new Vector3(-0.4007f, 0.0310f, -5.3586f);
    public static readonly Vector3 SBLowD_L = new Vector3(0.0539f, -0.0346f, 0.9979f);
    public static readonly Vector3 NoseDoorP_L = new Vector3(-0.2939f, -0.4516f, 6.5336f);
    public static readonly Vector3 WingPivot_R = new Vector3(2.7430f, 0.6100f, 0.3120f);
    public static readonly Vector3 FlapP_R = new Vector3(5.7272f, 0.5119f, -1.7875f);
    public static readonly Vector3 FlapD_R = new Vector3(-0.9936f, -0.0027f, 0.1127f);
    public static readonly Vector3 RudderP_R = new Vector3(1.5142f, 1.5875f, -6.3442f);
    public static readonly Vector3 RudderD_R = new Vector3(0.0793f, 0.8515f, -0.5183f);
    public static readonly Vector3 StabP_R = new Vector3(2.0070f, 0.0610f, -5.8207f);
    public static readonly Vector3 SBLowP_R = new Vector3(0.4007f, 0.0310f, -5.3586f);
    public static readonly Vector3 SBLowD_R = new Vector3(-0.0539f, -0.0346f, 0.9979f);
    public static readonly Vector3 NoseDoorP_R = new Vector3(0.2940f, -0.4516f, 6.5336f);
    public static readonly Vector3 SBUpP = new Vector3(-0.0000f, 0.2296f, -5.6407f);
    public static readonly Vector3 SBUpD = new Vector3(-1.0000f, 0.0000f, -0.0000f);
    public static readonly Vector3 CanopyP = new Vector3(-0.0000f, 0.6487f, 3.9467f);
    public static readonly Vector3 HookP = new Vector3(-0.0000f, -0.0303f, -4.9972f);
    public static readonly Vector3 NoseTrunnion = new Vector3(0.0000f, -0.0143f, 6.3956f);
    public static readonly Vector3 NoseAxle = new Vector3(0.0000f, -1.6687f, 6.2099f);
    public static readonly Vector3 MainTrunnion_L = new Vector3(-2.2264f, 0.3000f, -0.6597f);
    public static readonly Vector3 MainAxle_L = new Vector3(-2.5000f, -1.4125f, -0.8208f);
    public static readonly Vector3 MainTrunnion_R = new Vector3(2.2264f, 0.3000f, -0.6597f);
    public static readonly Vector3 MainAxle_R = new Vector3(2.5000f, -1.4125f, -0.8208f);
    public static readonly Vector3 MainDeployAxis_L = new Vector3(-0.9255f, -0.1371f, 0.3531f);
    public static readonly Vector3 MainDeployAxis_R = new Vector3(-0.9255f, 0.1371f, -0.3531f);
    public const float MainDeployDeg = 105.939f;
    public static readonly Vector3 MainAxleDir_L = new Vector3(-0.9901f, 0.1401f, 0.0123f);
    public static readonly Vector3 MainAxleDir_R = new Vector3(0.9901f, 0.1401f, 0.0123f);
    public static readonly Vector3 MainStowedAxle_L = new Vector3(-1.9000f, 0.5950f, 1.5950f);
    public static readonly Vector3 MainStowedAxle_R = new Vector3(1.9000f, 0.5950f, 1.5950f);
    public const float NoseFoldDeg = 75f;
    public static readonly Vector3 NoseStowedAxle = new Vector3(0.0000f, -0.2000f, 8.0500f);
    public const float GroundY = -2.0725f;
    // 0.1.3 doors / piston (hinge points, Unity coords)
    public static readonly Vector3 NoseLeafP_L = new Vector3(-0.3766f, -0.3883f, 7.4339f);
    public static readonly Vector3 NoseLeafP_R = new Vector3(0.3765f, -0.3883f, 7.4849f);
    public static readonly Vector3 NoseStripP_L = new Vector3(-0.0568f, -0.4312f, 5.6146f);
    public static readonly Vector3 NoseStripP_R = new Vector3(0.0567f, -0.4312f, 5.6146f);
    public static readonly Vector3 MainFwdDoorP_L = new Vector3(-2.1627f, 0.4149f, 1.5433f);
    public static readonly Vector3 MainFwdDoorP_R = new Vector3(2.1627f, 0.4149f, 1.5433f);
    public static readonly Vector3 NosePistonCylP = new Vector3(0.0000f, -0.3779f, 4.9486f);
    public static readonly Vector3 NosePistonRodP = new Vector3(0.0000f, -0.7603f, 5.9937f);
    public struct Mat { public string name, baseTex, normalTex; public float alpha; public bool transparent; public Color color; }
    public static readonly Mat[] Mats = {
        new Mat{ name="5a42bd56-19ab-4f02-9f4d-78bb034c77bb", baseTex="F14B_5a42bd56_base.png", normalTex="F14B_5a42bd56_n.png", alpha=1.000f, transparent=false, color=new Color(1.0000f,1.0000f,1.0000f,1.000f) },
        new Mat{ name="66e7d946-6ae5-439a-9274-8395c5192eec", baseTex=null, normalTex=null, alpha=0.600f, transparent=true, color=new Color(0.0007f,0.0022f,0.0137f,0.600f) },
        new Mat{ name="733a841d-8ade-468b-b158-bacf5b684769", baseTex="F14B_733a841d_base.png", normalTex="F14B_733a841d_n.png", alpha=1.000f, transparent=false, color=new Color(1.0000f,1.0000f,1.0000f,1.000f) },
        new Mat{ name="7a37476d-13ba-4f03-9674-2e655a5eee4b", baseTex=null, normalTex=null, alpha=0.500f, transparent=true, color=new Color(0.0000f,0.0000f,0.0000f,0.500f) },
        new Mat{ name="8248b29f-68f8-45fc-917d-79cab598b06d", baseTex="F14B_8248b29f_base.png", normalTex="F14B_8248b29f_n.png", alpha=1.000f, transparent=false, color=new Color(1.0000f,1.0000f,1.0000f,1.000f) },
        new Mat{ name="8bc3cf9c-7af9-4f72-ac6c-26f821ceca9a", baseTex="F14B_8bc3cf9c_base.png", normalTex="F14B_8bc3cf9c_n.png", alpha=1.000f, transparent=false, color=new Color(1.0000f,1.0000f,1.0000f,1.000f) },
        new Mat{ name="9c2bd7b7-b36d-4a82-8563-35b7a4b64f67", baseTex=null, normalTex=null, alpha=0.600f, transparent=true, color=new Color(0.0836f,0.0026f,0.0026f,0.600f) },
        new Mat{ name="a79b8aca-6168-4335-a712-1aeb57158472", baseTex=null, normalTex=null, alpha=0.200f, transparent=true, color=new Color(0.0000f,0.0000f,0.0000f,0.200f) },
        new Mat{ name="e0ff0269-b554-4c41-8aa8-c06dab01fdfa", baseTex="F14B_e0ff0269_base.png", normalTex="F14B_e0ff0269_n.png", alpha=1.000f, transparent=false, color=new Color(1.0000f,1.0000f,1.0000f,1.000f) },
        new Mat{ name="e45737ef-055d-4769-a2c5-9a17c419379c", baseTex="F14B_e45737ef_base.png", normalTex="F14B_e45737ef_n.png", alpha=1.000f, transparent=false, color=new Color(1.0000f,1.0000f,1.0000f,1.000f) },
        new Mat{ name="f281d110-318e-4a46-95e5-839c6773dd69", baseTex=null, normalTex=null, alpha=0.900f, transparent=true, color=new Color(0.0000f,0.0000f,0.0000f,0.900f) },
    };
}

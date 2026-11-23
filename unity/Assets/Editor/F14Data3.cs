using UnityEngine;

// 0.1.8 slat hinge data (Unity aircraft frame), from f14_export_info.json (prep 0.1.8): U(x, y, z) = (-x, z, -y) of Blender.
public static class F14Data3
{
    public static readonly Vector3 SlatP_L = new Vector3(-6.1880f, 0.2677f, -0.7117f);    // hinge line point, 0.35 m below the slat trailing edge
    public static readonly Vector3 SlatP_R = new Vector3(6.1880f, 0.2677f, -0.7117f);
    public static readonly Vector3 SlatD_L = new Vector3(-0.95304f, -0.01909f, -0.30223f); // spanwise hinge direction
    public static readonly Vector3 SlatD_R = new Vector3(0.95304f, -0.01910f, -0.30223f);
    public static readonly Vector3 SlatLE_L = new Vector3(-6.2232f, 0.5607f, -0.3945f);    // mid-span leading edge
    public static readonly Vector3 SlatLE_R = new Vector3(6.2231f, 0.5607f, -0.3945f);
}

#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class FbxSubAssetInspector
{
    private const string OutputPath = "Assets/_TempInspect/fbx_subassets.txt";

    [MenuItem("RelicFairy/Dev/Dump Weapon FBX SubAssets")]
    public static void DumpWeaponFbxSubAssets()
    {
        string[] fbxPaths =
        {
            "Assets/_ThirdParty/WeaponObject/Art/Weapons/Stylized/Swords/Meshes_Swords/Sword4_1.fbx",
            "Assets/_ThirdParty/WeaponObject/Art/Weapons/Stylized/Swords/Meshes_Swords/Sword4_2.fbx",
            "Assets/_ThirdParty/WeaponObject/Art/Weapons/Stylized/Swords/Meshes_Swords/Sword4_3.fbx",
            "Assets/_ThirdParty/WeaponObject/Art/Weapons/Stylized/Swords/Meshes_Swords/Sword1_1.fbx",
            "Assets/_ThirdParty/WeaponObject/Art/Weapons/Stylized/Swords/Meshes_Swords/Sword1_2.fbx",
            "Assets/_ThirdParty/WeaponObject/Art/Weapons/Stylized/Swords/Meshes_Swords/Sword1_3.fbx",
            "Assets/_ThirdParty/WeaponObject/Art/Weapons/Stylized/Bows/Meshes_Bows/Bow1_1.fbx",
            "Assets/_ThirdParty/WeaponObject/Art/Weapons/Stylized/Bows/Meshes_Bows/Bow1_2.fbx",
            "Assets/_ThirdParty/WeaponObject/Art/Weapons/Stylized/Bows/Meshes_Bows/Bow1_3.fbx",
            "Assets/_ThirdParty/WeaponObject/Art/Weapons/Stylized/Crossbows/Meshes_Crossbows/Crossbow1_1.fbx",
            "Assets/_ThirdParty/WeaponObject/Art/Weapons/Stylized/Crossbows/Meshes_Crossbows/Crossbow1_2.fbx",
            "Assets/_ThirdParty/WeaponObject/Art/Weapons/Stylized/Crossbows/Meshes_Crossbows/Crossbow1_3.fbx",
        };

        StringBuilder sb = new StringBuilder();
        foreach (string fbx in fbxPaths)
        {
            sb.AppendLine("=== " + fbx);
            string guid = AssetDatabase.AssetPathToGUID(fbx);
            sb.AppendLine("GUID: " + guid);

            Object[] subs = AssetDatabase.LoadAllAssetsAtPath(fbx);
            sb.AppendLine("SubAsset count: " + subs.Length);
            foreach (Object o in subs)
            {
                if (o == null) continue;
                long localId;
                string localGuid;
                bool ok = AssetDatabase.TryGetGUIDAndLocalFileIdentifier(o, out localGuid, out localId);
                sb.AppendLine(string.Format("  - type={0,-30} name={1,-30} fileID={2} ok={3}",
                    o.GetType().Name, o.name, localId, ok));
            }

            GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            if (root != null)
            {
                MeshFilter[] mfs = root.GetComponentsInChildren<MeshFilter>(true);
                sb.AppendLine("MeshFilters in hierarchy: " + mfs.Length);
                foreach (var mf in mfs)
                {
                    string p = GetGameObjectPath(mf.transform);
                    Mesh m = mf.sharedMesh;
                    long mid = 0; string mg = "";
                    if (m != null) AssetDatabase.TryGetGUIDAndLocalFileIdentifier(m, out mg, out mid);
                    sb.AppendLine(string.Format("  GO[{0}] mesh={1} fileID={2}", p, m != null ? m.name : "null", mid));
                }
                MeshRenderer[] mrs = root.GetComponentsInChildren<MeshRenderer>(true);
                sb.AppendLine("MeshRenderers in hierarchy: " + mrs.Length);
                foreach (var mr in mrs)
                {
                    string p = GetGameObjectPath(mr.transform);
                    sb.AppendLine(string.Format("  GO[{0}] materials count={1}", p, mr.sharedMaterials.Length));
                    foreach (var mat in mr.sharedMaterials)
                    {
                        if (mat == null) { sb.AppendLine("    mat=null"); continue; }
                        long mid; string mg;
                        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(mat, out mg, out mid);
                        string mp = AssetDatabase.GetAssetPath(mat);
                        sb.AppendLine(string.Format("    mat name={0} guid={1} fileID={2} path={3}",
                            mat.name, mg, mid, mp));
                    }
                }
            }
            sb.AppendLine();
        }

        string dir = Path.GetDirectoryName(OutputPath);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(OutputPath, sb.ToString());
        AssetDatabase.Refresh();
        Debug.Log("FBX sub-asset dump written: " + OutputPath);
    }

    private static string GetGameObjectPath(Transform t)
    {
        if (t == null) return "";
        string p = t.name;
        Transform cur = t.parent;
        while (cur != null) { p = cur.name + "/" + p; cur = cur.parent; }
        return p;
    }
}
#endif

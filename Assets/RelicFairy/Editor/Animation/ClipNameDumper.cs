#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 폴더 안 FBX들의 <b>실제 AnimationClip 이름</b>을 찍는다.
/// 메뉴: RelicFairy/Animation/Dump Clip Names
///
/// AnimatorOverrideController는 <b>클립 이름</b>으로 덮는다. 그런데 클립 이름은 FBX 파일명과
/// 다를 수 있고(임포터에서 개명), .meta의 clipAnimations가 없으면 규칙을 추정하기도 어렵다.
/// 추정으로 오버라이드 키를 쓰면 조용히 실패하므로 — 실제로 두 번 그랬다 — 여기서 확인하고 쓴다.
/// </summary>
public static class ClipNameDumper
{
    private static readonly string[] Folders =
    {
        "Assets/RelicFairy/_Imported/GhostSamurai_Animset/Animation/katana/Common/Inplace",
        "Assets/RelicFairy/_Imported/GhostSamurai_Animset/Animation/katana/APose/Movement/Inplace",
        "Assets/RelicFairy/_Imported/GhostSamurai_Animset/Animation/katana/APose",
    };

    [MenuItem("RelicFairy/Animation/Dump Clip Names")]
    public static void Dump()
    {
        foreach (var folder in Folders)
        {
            if (!Directory.Exists(folder)) { Debug.LogWarning($"[클립명] 폴더 없음: {folder}"); continue; }

            foreach (var path in Directory.GetFiles(folder, "*.FBX", SearchOption.TopDirectoryOnly))
            {
                string p = path.Replace(Path.DirectorySeparatorChar, '/');
                foreach (var a in AssetDatabase.LoadAllAssetsAtPath(p))
                {
                    if (a is not AnimationClip c || c.name.StartsWith("__preview__")) continue;
                    Debug.Log($"[클립명] {Path.GetFileNameWithoutExtension(p)}  ==>  {c.name}");
                }
            }
        }
    }
}
#endif

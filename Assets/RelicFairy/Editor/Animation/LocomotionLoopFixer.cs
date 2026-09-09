#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 로코모션 순환 클립의 <b>Loop Time</b>을 켠다.
/// 메뉴: RelicFairy/Animation/Fix Locomotion Loop Time
///
/// 증상 — 이동을 유지하면 애니가 끝까지 갔다가 같은 구간만 되풀이한다.
/// 원인은 걷기·달리기 클립이 <c>loopTime = false</c>로 임포트돼 순환으로 취급되지 않는 것.
/// 블렌드 트리는 상태 시간이 계속 흐르므로, 루프가 아닌 클립은 끝에서 어정쩡하게 되감긴다.
///
/// ⚠ <c>.meta</c>를 직접 고치지 않는다(프로젝트 규약). <see cref="ModelImporter"/>로 바꾸고
/// 재임포트한다 — 이게 Unity가 인정하는 유일한 경로다.
///
/// Idle·Start·End·Turn은 대상이 아니다. 순환이 아니라 일회성이라 루프를 켜면 오히려 어색해진다.
/// </summary>
public static class LocomotionLoopFixer
{
    private static readonly string[] Folders =
    {
        "Assets/RelicFairy/_Imported/GhostSamurai_Animset/Animation/katana/Common/Inplace",
        "Assets/RelicFairy/_Imported/GhostSamurai_Animset/Animation/katana/APose/Movement/Inplace",
        "Assets/RelicFairy/_Imported/GhostSamurai_Animset/Animation/Bow/Movement/Inplace",
    };

    /// <summary>이 조각이 이름에 있으면 순환 클립으로 본다.</summary>
    private static readonly string[] LoopMarkers = { "StrafeWalk", "StrafeRun", "Strafe_Walk", "Strafe_Run", "_Walk_", "_Run_" };

    /// <summary>순환처럼 보여도 일회성인 것 — 루프를 켜면 안 된다.</summary>
    private static readonly string[] OneShotMarkers = { "_Start", "_End", "Turn", "Sprint" };

    [MenuItem("RelicFairy/Animation/Fix Locomotion Loop Time")]
    public static void Fix()
    {
        int fixedCount = 0, skipped = 0;

        foreach (var folder in Folders)
        {
            if (!Directory.Exists(folder)) continue;

            foreach (var raw in Directory.GetFiles(folder, "*.FBX", SearchOption.TopDirectoryOnly))
            {
                string path = raw.Replace(Path.DirectorySeparatorChar, '/');
                string name = Path.GetFileNameWithoutExtension(path);

                if (!IsLoopClip(name)) { skipped++; continue; }

                var imp = AssetImporter.GetAtPath(path) as ModelImporter;
                if (imp == null) continue;

                // clipAnimations가 비어 있으면 임포터가 기본값을 쓰는 상태다 — 기본을 가져와 수정한 뒤 되돌려 넣는다.
                var clips = imp.clipAnimations;
                if (clips == null || clips.Length == 0) clips = imp.defaultClipAnimations;
                if (clips == null || clips.Length == 0) continue;

                bool changed = false;
                for (int i = 0; i < clips.Length; i++)
                {
                    if (clips[i].loopTime) continue;
                    clips[i].loopTime = true;
                    changed = true;
                }

                if (!changed) { skipped++; continue; }

                imp.clipAnimations = clips;
                imp.SaveAndReimport();
                fixedCount++;
                Debug.Log($"[루프] loopTime 켬 — {name}");
            }
        }

        AssetDatabase.Refresh();
        Debug.Log($"[루프] 완료 — 수정 {fixedCount} · 건너뜀 {skipped}");
    }

    private static bool IsLoopClip(string name)
    {
        foreach (var s in OneShotMarkers) if (name.Contains(s)) return false;
        foreach (var s in LoopMarkers)    if (name.Contains(s)) return true;
        return false;
    }
}
#endif

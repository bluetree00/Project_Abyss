#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 로코모션 클립의 <b>고유 이동속도</b>를 잰다.
/// 메뉴: RelicFairy/Animation/Measure Locomotion Clip Speed
///
/// 왜 필요한가 — 달리기 클립은 제작 당시의 보폭 속도로 재생되는데 캐릭터는 코드가 정한 속도로 움직인다.
/// 둘이 다르면 그 차이만큼 발이 지면을 긁는다(foot sliding). 없애려면
/// <c>anim.speed = 실제속도 / 클립고유속도</c> 로 맞춰야 하고, 그 분모가 여기서 나온다.
///
/// 측정은 <see cref="AnimationClip.averageSpeed"/>(루트모션 평균 속도)를 쓴다. 수평 성분만 본다 —
/// 상하 진동은 보폭과 무관하다. 클립이 in-place(루트모션 없음)면 0이 나오며, 그때는
/// 이 방식으로 못 재고 아티스트가 의도한 속도를 문서에서 받아야 한다.
/// </summary>
public static class LocomotionClipSpeedProbe
{
    /// <summary>잴 대상. 로코모션에 실제로 쓰는 클립만 둔다(전수 조사는 노이즈가 크다).</summary>
    private static readonly string[] Targets =
    {
        "Assets/RelicFairy/Animations/Player/Test_01/Move/CLazy@Mvm_Jog.FBX",
        "Assets/RelicFairy/Animations/Player/Test_01/Move/CLazy@Mvm_Walk.FBX",
        // 회피(대시) — Dodge 상태의 원본과 무기 장착 시 덮이는 클립
        "Assets/RelicFairy/Animations/Player/Test_01/Move/GhostSamurai_APose_Avoid_F_Inplace.FBX",
        "Assets/CombatGirlsCharacterPack/CombatGirl_Shield/Animations/Special/SS_Quickshift_F.fbx",
    };

    // 비교 기준 — PlayerCharacterData 의 값. 여기와 어긋나면 보정 계수가 1에서 멀어진다.
    private const float WalkSpeed = 5f;
    private const float RunSpeed  = 8f;

    [MenuItem("RelicFairy/Animation/Measure Locomotion Clip Speed")]
    public static void Measure()
    {
        // 한 줄씩 따로 찍는다 — 콘솔(및 MCP read_console)이 멀티라인 메시지의 첫 줄만 돌려준다.
        Debug.Log($"[고유속도] 기준 — 걷기 {WalkSpeed} m/s · 달리기 {RunSpeed} m/s · 보정계수 = 목표 / 고유");

        foreach (var path in Targets)
        {
            foreach (var clip in LoadClips(path))
            {
                var v = clip.averageSpeed;
                float horizontal = new Vector2(v.x, v.z).magnitude;

                // 컨트롤러 YAML에 클립을 물릴 때 필요한 좌표. 커스텀 클립은 fileID가 7400000이 아니다.
                if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(clip, out string g, out long fid))
                    Debug.Log($"[클립참조] {clip.name} · fileID {fid} · guid {g}");

                if (horizontal < 0.01f)
                {
                    Debug.Log($"[고유속도] {clip.name} · 길이 {clip.length:F2}s · 고유 ~0 " +
                              "→ in-place(루트모션 없음). 이 방식으로 못 잼");
                    continue;
                }

                Debug.Log($"[고유속도] {clip.name} · 길이 {clip.length:F2}s · 고유 {horizontal:F2} m/s " +
                          $"· 보정 걷기 ×{WalkSpeed / horizontal:F2} · 달리기 ×{RunSpeed / horizontal:F2}");
            }
        }
    }

    /// <summary>FBX 안의 AnimationClip 서브에셋을 모은다(__preview__ 는 에디터 내부용이라 제외).</summary>
    private static IEnumerable<AnimationClip> LoadClips(string path)
    {
        var result = new List<AnimationClip>();
        var assets = AssetDatabase.LoadAllAssetsAtPath(path);
        if (assets == null || assets.Length == 0)
        {
            Debug.LogWarning($"[클립 고유속도] 경로 없음: {path}");
            return result;
        }

        foreach (var a in assets)
            if (a is AnimationClip c && !c.name.StartsWith("__preview__")) result.Add(c);

        if (result.Count == 0) Debug.LogWarning($"[클립 고유속도] AnimationClip 없음: {path}");
        return result;
    }
}
#endif

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

using AnimatorControllerAsset = UnityEditor.Animations.AnimatorController;

/// <summary>
/// 바닥 아래로 들어가는 Knight 팩 <c>.anim</c> 3개를 GhostSamurai 휴머노이드 클립 내용으로 <b>덮어쓴다</b>.
/// 메뉴: RelicFairy/Animation/Replace Sinking Knight Clips (멱등)
///
/// 실측(Probe Clip Foot Height, 2026-09-09): <c>QSkill_01.anim</c>·<c>ESkill_01.anim</c>·<c>HeavyChargeAccept.anim</c>과
/// 그걸 복제한 <c>RelicQ_Main.anim</c>은 CombatGirl 아바타에서 골반이 원점(−0.04m)에 떨어져 발이 <b>−0.73m</b>에 박힌다
/// (49/49 프레임). 다른 클립 80여 개는 전부 ±0.05m 안. 이 세 클립은 휴머노이드 루트 커브(RootT)가 없어
/// 리타게팅이 몸 위치를 못 잡는 것 — 값을 손보는 게 아니라 클립 자체를 바꿔야 한다.
///
/// 왜 새 에셋이 아니라 <b>덮어쓰기</b>인가 — 오버라이드 키는 클립 <b>이름</b>이고 컨트롤러·무기 세트가
/// 이 GUID/이름(QSkill_01, ESkill_01)을 이미 가리킨다. 파일을 갈아끼우면 참조가 끊기고 키가 바뀐다.
/// <see cref="EditorUtility.CopySerialized"/>로 내용만 바꾸고 이름·GUID는 유지한다(RelicQSlashClipGenerator와 같은 방식).
/// </summary>
public static class SinkingClipReplacer
{
    private const string KnightDir      = "Assets/RelicFairy/Characters/Player/Knight/Animations";
    private const string ControllerPath = "Assets/RelicFairy/Characters/Player/PlayerBaseController.controller";
    private const string GS             = "Assets/RelicFairy/_Imported/GhostSamurai_Animset/Animation/katana";

    /// <summary>(덮어쓸 .anim, 원본 FBX). 원본 FBX의 첫 클립을 쓴다.</summary>
    private static readonly (string anim, string fbx, string why)[] Targets =
    {
        // 무기 E 기본 상태 — 무형검 무형참이 여기서 검기를 날린다. 한 번 크게 베는 Attack02_2(2.25s).
        ($"{KnightDir}/ESkill_01.anim",          $"{GS}/APose/Attack/Inplace/GhostSamurai_APose_Attack02_2_Inplace.FBX",  "무기 E 기본"),
        // 무기 R 기본 상태 — 무기 세트가 대부분 덮지만 폴백이 바닥 아래여선 안 된다. SPAttack02(2.63s).
        ($"{KnightDir}/QSkill_01.anim",          $"{GS}/Nameless/GhostSamurai_APose_SPAttack02_Inplace.FBX",              "무기 R 기본"),
        // 유물 Q 단독 모션(랜슬롯 마무리·가웨인 캐스트) — 크게 내리찍는 SPAttack01(2.90s, 옛 강공격 클립).
        ($"{KnightDir}/RelicQ/RelicQ_Main.anim", $"{GS}/APose/Attack/Inplace/GhostSamurai_APose_SPAttack01_Inplace.FBX", "유물 Q 단독"),
        // 강공격 폐지로 소비처는 없지만 컨트롤러 상태가 남아 있어 같이 고친다.
        ($"{KnightDir}/HeavyChargeAccept.anim",  $"{GS}/Nameless/GhostSamurai_APose_SPAttack02_Inplace.FBX",              "잔재"),
    };

    /// <summary>RelicQ_Main 상태 속도 — SPAttack01 2.90s를 1.45s 마무리로.</summary>
    private const float RelicQMainSpeed = 2f;

    [MenuItem("RelicFairy/Animation/Replace Sinking Knight Clips")]
    public static void Replace()
    {
        int ok = 0;
        foreach (var (anim, fbx, why) in Targets)
        {
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(anim);
            if (existing == null) { Debug.LogWarning($"[침몰클립] 대상 없음: {anim}"); continue; }

            AnimationClip src = null;
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(fbx))
                if (o is AnimationClip c && !c.name.StartsWith("__preview__")) { src = c; break; }
            if (src == null) { Debug.LogError($"[침몰클립] 원본 클립 없음: {fbx}"); continue; }

            string keepName = existing.name;
            var copy = Object.Instantiate(src);
            AnimationUtility.SetAnimationEvents(copy, new AnimationEvent[0]);   // 원본 이벤트(트레일·공격종료)는 오발동 원인
            EditorUtility.CopySerialized(copy, existing);
            existing.name = keepName;          // 이름 = 오버라이드 키. 절대 바뀌면 안 된다.
            Object.DestroyImmediate(copy);
            EditorUtility.SetDirty(existing);
            ok++;
            Debug.Log($"[침몰클립] {keepName} ← {src.name} ({existing.length:F2}s) — {why}");
        }

        // RelicQ_Main 속도 — 긴 강공격 클립을 마무리 템포로.
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorControllerAsset>(ControllerPath);
        if (controller != null)
        {
            foreach (var layer in controller.layers)
                foreach (var cs in layer.stateMachine.states)
                    if (cs.state != null && cs.state.name == "RelicQ_Main")
                    {
                        cs.state.speed = RelicQMainSpeed;
                        cs.state.writeDefaultValues = false;
                        EditorUtility.SetDirty(controller);
                        Debug.Log($"[침몰클립] RelicQ_Main speed={RelicQMainSpeed}");
                    }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[침몰클립] 완료 — {ok}/{Targets.Length}");
    }
}
#endif

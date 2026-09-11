#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// _ThirdParty/Suriyun 이 전역 네임스페이스에 동명의 MonoBehaviour를 두고 있어 그쪽이 먼저 잡힌다.
using AnimatorControllerAsset = UnityEditor.Animations.AnimatorController;

/// <summary>
/// 유물 Q <b>단독 모션 전용 상태</b> <c>RelicQ_Main</c>을 컨트롤러에 만든다.
/// 메뉴: RelicFairy/Animation/Ensure Relic Q Main State (멱등)
///
/// 왜 필요한가 — 유물 Q(가웨인 캐스트·랜슬롯 마무리)는 지금까지 <c>QSkill_01</c> 상태를 썼는데,
/// 그 상태의 원본 클립 이름 <c>QSkill_01</c>은 무기 세트가 R 스킬 모션으로 덮어쓰는 키다
/// (카타나·무형검 → QSkill_Iasen, 대검 → GreatswordQSkill, 활 → Bow_Shoot_Aim_Idle 루프).
/// 그래서 <b>같은 유물 Q인데 든 무기에 따라 모션이 바뀌었다</b>(활을 들면 조준 대기 자세로 심판의 일격을 마무리).
///
/// 오버라이드 키는 상태 이름이 아니라 <b>원본 클립 이름</b>이므로, 상태만 새로 만들고 공용 클립을 물리면
/// 여전히 같이 덮인다. 그래서 클립도 <c>RelicQ_Main.anim</c>으로 <b>복제</b>해 키를 분리한다.
/// 원본은 Knight QSkill_01.anim이 아니라 GhostSamurai SPAttack01 — Knight 클립은 CombatGirl에서 발이 −0.73m에 박힌다(실측).
/// </summary>
public static class RelicQMainStateBuilder
{
    private const string ControllerPath = "Assets/RelicFairy/Characters/Player/PlayerBaseController.controller";
    private const string SourceClipPath = "Assets/RelicFairy/_Imported/GhostSamurai_Animset/Animation/katana/APose/Attack/Inplace/GhostSamurai_APose_SPAttack01_Inplace.FBX";
    private const string OutFolder      = "Assets/RelicFairy/Characters/Player/Knight/Animations/RelicQ";
    private const string StateName      = "RelicQ_Main";
    private const string AnchorState    = "QSkill_01";
    /// <summary>SPAttack01(2.90s)을 마무리 템포(1.45s)로.</summary>
    private const float  StateSpeed     = 2f;

    [MenuItem("RelicFairy/Animation/Ensure Relic Q Main State")]
    public static void Ensure()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorControllerAsset>(ControllerPath);
        if (controller == null) { Debug.LogError($"[RelicQMain] 컨트롤러 없음: {ControllerPath}"); return; }

        AnimationClip src = null;
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(SourceClipPath))
            if (o is AnimationClip c && !c.name.StartsWith("__preview__")) { src = c; break; }
        if (src == null) { Debug.LogError($"[RelicQMain] 원본 클립 없음: {SourceClipPath}"); return; }

        if (!AssetDatabase.IsValidFolder(OutFolder))
            AssetDatabase.CreateFolder(OutFolder.Substring(0, OutFolder.LastIndexOf('/')), OutFolder.Substring(OutFolder.LastIndexOf('/') + 1));

        // 1) 클립 복제 — 이벤트는 비운다(마무리 타격은 코드가 시각과 판정을 따로 잡는다).
        string clipPath = $"{OutFolder}/{StateName}.anim";
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
        if (clip == null)
        {
            var copy = Object.Instantiate(src);
            copy.name = StateName;
            AnimationUtility.SetAnimationEvents(copy, new AnimationEvent[0]);
            AssetDatabase.CreateAsset(copy, clipPath);
            clip = copy;
            Debug.Log($"[RelicQMain] 클립 생성: {clipPath} (len={clip.length:F2}s)");
        }

        // 2) 상태 업서트 — QSkill_01과 같은 스테이트머신에.
        var sm = FindStateMachineOf(controller, AnchorState);
        if (sm == null) { Debug.LogError($"[RelicQMain] '{AnchorState}' 상태를 찾지 못했다."); return; }

        AnimatorState st = null;
        foreach (var cs in sm.states)
            if (cs.state != null && cs.state.name == StateName) { st = cs.state; break; }
        if (st == null)
        {
            st = sm.AddState(StateName);
            Debug.Log($"[RelicQMain] 상태 신설: {StateName}");
        }
        st.motion             = clip;
        st.speed              = StateSpeed;
        st.writeDefaultValues = false;   // 프로젝트 규칙

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        Debug.Log($"[RelicQMain] 완료 — 상태 '{StateName}' speed={StateSpeed} 클립='{clip.name}'");
    }

    private static AnimatorStateMachine FindStateMachineOf(AnimatorControllerAsset controller, string stateName)
    {
        foreach (var layer in controller.layers)
        {
            var found = FindStateMachineOf(layer.stateMachine, stateName);
            if (found != null) return found;
        }
        return null;
    }

    private static AnimatorStateMachine FindStateMachineOf(AnimatorStateMachine sm, string stateName)
    {
        foreach (var cs in sm.states)
            if (cs.state != null && cs.state.name == stateName) return sm;
        foreach (var child in sm.stateMachines)
        {
            var found = FindStateMachineOf(child.stateMachine, stateName);
            if (found != null) return found;
        }
        return null;
    }
}
#endif

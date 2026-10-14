#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// 카타나 R 환영베기 전용 연타 모션 = 사무라이 팩 「SPAttack05」(사용자 10-08 — 추천안으로 먼저 보고 판단).
/// - 예전엔 평타 콤보(GroundLightAttack_01~03)를 0.12초마다 갈아 끼워 스킬로 안 보였다(기획 장비스킬_조작감개선 §5-C2).
/// - 무기끼리 겹치면 안 된다: 무형검 · 카타나 평타 = Attack01 계열 · 대검 = 02_3 · 02_4 · 03_5 · 기본 ESkill_01 = 02_2 ·
///   기본 QSkill_01 = SPAttack02 · 유물 Q = SPAttack01. 후보 16개 비교는 실측 메뉴 장비 형/16(시트 기획/환영베기_연타모션_후보_20261008.png).
/// - 붙이는 자리 = PlayerBaseController의 빈 상태 QSkill_03(쓰는 곳 0). QSkill_01은 유물 Q 폴백과 무기 R이 공유해 싸운다(메모 weapon_skill_tier_derivation).
///   다른 무기는 이 상태를 틀지 않으므로 무기별 덮어쓰기 없이 기본 모션으로 둔다. 상태 속도 2배(3.17초 클립 — 연타 3번이 0.55초 안에 몰린다).
/// - _ThirdParty(추적 제외)의 클립은 _Imported로 옮긴다(AssetDatabase.MoveAsset — GUID 보존).
/// 환영베기 쪽 값(전용 상태 · 시작 지점 · 타격 순간)은 PhantomDance.asset — 실측 장비 형/18로 휘두름 순간에 맞춘다.
/// </summary>
public static class KatanaPhantomFlurryClip
{
    private const string Clip     = "GhostSamurai_APose_SPAttack05_Inplace.FBX";
    private const string Imported = "Assets/RelicFairy/_Imported/GhostSamurai_Animset/Animation/katana/APose/Attack/Inplace/";
    private const string Third    = "Assets/_ThirdParty/GhostSamurai_Animset/Animation/katana/APose/Attack/Inplace/";
    private const string CtrlPath = "Assets/RelicFairy/Characters/Player/PlayerBaseController.controller";
    private const string State    = "QSkill_03";
    private const float  Speed    = 2f;
    private const string Phantom  = "Assets/RelicFairy/Weapon/Katana/Data/KatanaBase/PhantomDance.asset";
    // 클립 속 휘두름(속도 1 기준, 실측 16): 1.04 · 1.28 · 1.59초 연타 + 2.71초 큰 마무리. 0.8초부터 틀고(앞 예비 동작을 건너뜀) 속도 2 →
    // 스킬 시작부터 0.12 · 0.24 · 0.395 · 0.955초. 앞 4타는 연타 구간(0.12 ~ 0.395)에 고르게, 5타(마무리)는 큰 휘두름에.
    private const float StartSeconds = 0.8f;
    private static readonly float[] HitTimes = { 0.12f, 0.21f, 0.30f, 0.395f, 0.955f };

    [MenuItem("RelicFairy/Weapon/Katana Phantom Flurry = Samurai SPAttack05")]
    public static void Apply()
    {
        if (AssetDatabase.LoadMainAssetAtPath(Imported + Clip) == null)
        {
            if (AssetDatabase.LoadMainAssetAtPath(Third + Clip) == null) { Debug.LogWarning("[KatanaPhantomFlurry] 클립 없음: " + Clip); return; }
            string err = AssetDatabase.MoveAsset(Third + Clip, Imported + Clip);
            if (!string.IsNullOrEmpty(err)) { Debug.LogWarning("[KatanaPhantomFlurry] 옮기기 실패: " + err); return; }
        }

        AnimationClip clip = null;
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(Imported + Clip))
            if (o is AnimationClip c && !c.name.StartsWith("__preview__")) { clip = c; break; }
        if (clip == null) { Debug.LogWarning("[KatanaPhantomFlurry] 클립을 못 읽음"); return; }

        var ctrl = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(CtrlPath);
        if (ctrl == null) { Debug.LogWarning("[KatanaPhantomFlurry] 컨트롤러 없음"); return; }
        AnimatorState state = null;
        foreach (var layer in ctrl.layers)
            if ((state = Find(layer.stateMachine)) != null) break;
        if (state == null) { Debug.LogWarning("[KatanaPhantomFlurry] 상태 없음: " + State); return; }

        state.motion = clip;
        state.speed = Speed;
        state.writeDefaultValues = false;   // 새로 쓰는 상태 — 규칙(writeDefaultValues = false)
        EditorUtility.SetDirty(state);
        EditorUtility.SetDirty(ctrl);

        var pd = AssetDatabase.LoadAssetAtPath<PhantomDanceBehaviorSO>(Phantom);
        if (pd == null) { Debug.LogWarning("[KatanaPhantomFlurry] 환영베기 데이터 없음: " + Phantom); return; }
        var so = new SerializedObject(pd);
        so.FindProperty("flurryState").stringValue = State;
        so.FindProperty("flurryStartNormalized").floatValue = StartSeconds / clip.length;   // 클립 초 → 정규화
        var arr = so.FindProperty("flurryHitTimes");
        arr.arraySize = HitTimes.Length;
        for (int i = 0; i < HitTimes.Length; i++) arr.GetArrayElementAtIndex(i).floatValue = HitTimes[i];
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(pd);
        AssetDatabase.SaveAssets();
        Debug.Log($"[KatanaPhantomFlurry] {State} = {clip.name} · {clip.length:0.00}초 · 속도 {Speed}");
    }

    private static AnimatorState Find(AnimatorStateMachine sm)
    {
        foreach (var s in sm.states)
            if (s.state.name == State) return s.state;
        foreach (var child in sm.stateMachines)
        {
            var found = Find(child.stateMachine);
            if (found != null) return found;
        }
        return null;
    }
}
#endif

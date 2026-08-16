#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Animations;
using UnityEngine;
// _ThirdParty/Suriyun 이 전역 네임스페이스에 동명의 MonoBehaviour를 두고 있어 그쪽이 먼저 잡힌다.
using AnimatorControllerAsset = UnityEditor.Animations.AnimatorController;

/// <summary>
/// 유물 Q(다단) 전용 참격 클립 생성기.
///
/// 근접 평타 콤보 클립(NormalAttack_1~3.FBX 내장)을 <b>복제</b>해 애니메이션 이벤트를 전부 제거한 뒤
/// 독립 .anim 에셋으로 저장하고, Addressables 등록 + 플레이어 컨트롤러에 재생용 상태를 추가한다.
///
/// <b>원본을 그대로 물리면 안 된다.</b> 원본에는 AE_AttackEnd / AE_BeginTrail / AE_EndTrail /
/// SpawnSlashEffect0~3 이벤트가 박혀 있어 Q 도중 오발동한다(특히 AE_AttackEnd는 공격 상태를 끝내버린다).
/// 타격 판정·데미지·타이밍은 코드(LancelotMadnessRelic / JudgmentStrikeRuntime)가 이미 처리하므로
/// 복제본에 이벤트는 필요 없다 — 순수 비주얼로만 쓴다.
///
/// 상태 이름은 클립 이름과 같게 둔다. AnimatorOverrideController의 키는 상태 이름이 아니라
/// <b>그 상태가 물고 있는 원본 클립의 이름</b>이라, 둘이 같아야 유물 데이터가 한 이름으로 둘 다 가리킬 수 있다.
///
/// 메뉴: RelicFairy/Animation/Generate Relic Q Slash Clips (멱등 — 다시 눌러도 안전)
/// </summary>
public static class RelicQSlashClipGenerator
{
    private const string OutFolder      = "Assets/RelicFairy/Characters/Player/Knight/Animations/RelicQ";
    private const string ControllerPath = "Assets/RelicFairy/Characters/Player/PlayerBaseController.controller";
    private const string AnchorState    = "QSkill_01";   // 새 상태를 이 상태와 같은 스테이트머신에 나란히 붙인다
    private const string GroupName      = "Animations";
    private const float  StateSpeed     = 2f;            // QSkill_01과 동일 — 참격 템포를 맞춘다

    // 세 단계 모두 전용 상태를 쓴다 — QSkill_01은 무기 E/R 스킬(Katana HolySlash·PhantomDance, Bow FocusShot)이
    // animationOverride로 직접 지목하는 공용 상태라, 유물이 여기 끼면 무기 스킬 모션이 유물 참격으로 바뀐다.
    private static readonly (string fbx, string clip, string outName, bool addState)[] Sources =
    {
        ("Assets/RelicFairy/Animations/Player/Test_01/Attack/NormalAttack_1.FBX", "GroundLightAttack_01", "RelicQ_Slash1", true),
        ("Assets/RelicFairy/Animations/Player/Test_01/Attack/NormalAttack_2.FBX", "GroundLightAttack_02", "RelicQ_Slash2", true),
        ("Assets/RelicFairy/Animations/Player/Test_01/Attack/NormalAttack_3.FBX", "GroundLightAttack_03", "RelicQ_Slash3", true),
    };

    [MenuItem("RelicFairy/Animation/Generate Relic Q Slash Clips")]
    public static void Execute()
    {
        EnsureFolder();

        var settings   = AddressableAssetSettingsDefaultObject.Settings;
        var group      = settings != null ? (settings.FindGroup(GroupName) ?? settings.DefaultGroup) : null;
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorControllerAsset>(ControllerPath);
        var anchorSm   = controller != null ? FindStateMachineOf(controller, AnchorState) : null;

        if (controller == null) Debug.LogError($"[RelicQSlash] 컨트롤러 없음: {ControllerPath}");
        else if (anchorSm == null) Debug.LogError($"[RelicQSlash] '{AnchorState}' 상태를 찾지 못해 새 상태를 붙일 위치를 정할 수 없다.");

        foreach (var (fbx, clipName, outName, addState) in Sources)
        {
            var src = FindClip(fbx, clipName);
            if (src == null)
            {
                Debug.LogError($"[RelicQSlash] 원본 클립 없음: {fbx} → '{clipName}'");
                continue;
            }

            var clip = CreateStrippedCopy(src, outName);
            if (clip == null) continue;

            Debug.Log($"[RelicQSlash] 생성: {outName} | length={clip.length:F3}s | fps={clip.frameRate} | " +
                      $"제거한 이벤트 {AnimationUtility.GetAnimationEvents(src).Length}개");

            if (group != null) RegisterAddressable(settings, group, OutFolder + "/" + outName + ".anim", outName);
            if (addState && anchorSm != null) UpsertState(anchorSm, outName, clip);
        }

        if (settings != null) settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, null, true);
        if (controller != null) EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[RelicQSlash] 완료.");
    }

    // ── Private Methods ───────────────────────────────────────────
    private static void EnsureFolder()
    {
        if (AssetDatabase.IsValidFolder(OutFolder)) return;
        var parent = OutFolder.Substring(0, OutFolder.LastIndexOf('/'));
        AssetDatabase.CreateFolder(parent, OutFolder.Substring(OutFolder.LastIndexOf('/') + 1));
    }

    private static AnimationClip FindClip(string fbxPath, string clipName)
    {
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
            if (o is AnimationClip c && c.name == clipName) return c;
        return null;
    }

    /// <summary>원본을 복제하고 애니메이션 이벤트를 전부 비워 .anim 에셋으로 저장한다.</summary>
    private static AnimationClip CreateStrippedCopy(AnimationClip src, string outName)
    {
        string path = OutFolder + "/" + outName + ".anim";

        var copy = Object.Instantiate(src);
        copy.name = outName;
        AnimationUtility.SetAnimationEvents(copy, new AnimationEvent[0]);

        var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (existing != null)
        {
            // 덮어쓰기 — 에셋을 지우면 GUID가 바뀌어 컨트롤러/Addressables 참조가 끊긴다.
            EditorUtility.CopySerialized(copy, existing);
            existing.name = outName;
            Object.DestroyImmediate(copy);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        AssetDatabase.CreateAsset(copy, path);
        return copy;
    }

    private static void RegisterAddressable(AddressableAssetSettings settings, AddressableAssetGroup group,
                                            string assetPath, string address)
    {
        var guid = AssetDatabase.AssetPathToGUID(assetPath);
        if (string.IsNullOrEmpty(guid)) return;

        var entry = settings.FindAssetEntry(guid)
                    ?? settings.CreateOrMoveEntry(guid, group, readOnly: false, postEvent: false);
        entry.address = address;
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

    /// <summary>같은 이름 상태가 있으면 모션만 갱신, 없으면 새로 만든다(멱등).</summary>
    private static void UpsertState(AnimatorStateMachine sm, string stateName, AnimationClip clip)
    {
        foreach (var cs in sm.states)
        {
            if (cs.state == null || cs.state.name != stateName) continue;
            cs.state.motion             = clip;
            cs.state.speed              = StateSpeed;
            cs.state.writeDefaultValues = false;
            Debug.Log($"[RelicQSlash] 상태 갱신: {stateName}");
            return;
        }

        var st = sm.AddState(stateName);
        st.motion             = clip;
        st.speed              = StateSpeed;
        st.writeDefaultValues = false;   // 프로젝트 규칙 — 새 상태는 항상 false
        Debug.Log($"[RelicQSlash] 상태 신설: {stateName}");
    }
}
#endif

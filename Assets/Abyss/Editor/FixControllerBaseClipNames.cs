using UnityEditor;
using UnityEngine;

/// <summary>
/// Knight.controller의 HeavyCharge / GroundHeavyAttack 상태에 연결된
/// base clip 이름을 KatanaAnimation.asset의 baseClipName과 일치시킨다.
///
/// AnimatorOverrideService.Override(keyName, clip) 은 base clip 이름으로 매칭하므로,
/// base clip 이름 == KatanaAnimation.baseClipName == BowAnimation.baseClipName 이어야 한다.
/// </summary>
public static class FixControllerBaseClipNames
{
    // HeavyCharge 상태의 base clip: BowHeavyCharge.FBX → 클립 이름 "HeavyCharge"로 변경
    private const string HeavyChargeFbx =
        "Assets/_ThirdParty/GhostSamurai_Animset/Animation/Bow/Attack/Pose/BowHeavyCharge.FBX";

    // GroundHeavyAttack 상태의 base clip: SPAttack01.FBX → 클립 이름 "GroundHeavyAttack"로 변경
    private const string GroundHeavyFbx =
        "Assets/_ThirdParty/GhostSamurai_Animset/Animation/katana/APose/Attack/Inplace/GhostSamurai_APose_SPAttack01_Inplace.FBX";

    [MenuItem("Tools/Fix Controller Base Clip Names")]
    public static void Fix()
    {
        FixClipName(HeavyChargeFbx, "HeavyCharge");
        FixClipName(GroundHeavyFbx, "GroundHeavyAttack");
        Debug.Log("[FixBaseClips] 완료. Knight.controller Override 매칭 가능 상태.");
    }

    private static void FixClipName(string fbxPath, string targetName)
    {
        var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
        if (importer == null)
        {
            Debug.LogError($"[FixBaseClips] FBX 없음: {fbxPath}");
            return;
        }

        var clips = importer.clipAnimations;
        if (clips == null || clips.Length == 0)
            clips = importer.defaultClipAnimations;

        if (clips == null || clips.Length == 0)
        {
            Debug.LogError($"[FixBaseClips] 클립 없음: {fbxPath}");
            return;
        }

        if (clips[0].name == targetName)
        {
            Debug.Log($"[FixBaseClips] 이미 '{targetName}': {fbxPath}");
            return;
        }

        string old = clips[0].name;
        clips[0].name = targetName;
        importer.clipAnimations = clips;
        importer.SaveAndReimport();
        Debug.Log($"[FixBaseClips] '{old}' → '{targetName}': {fbxPath}");
    }
}

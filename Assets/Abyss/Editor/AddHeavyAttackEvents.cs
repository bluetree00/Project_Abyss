using UnityEditor;
using UnityEngine;

public static class AddHeavyAttackEvents
{
    private const string AttackFbxPath =
        "Assets/_ThirdParty/GhostSamurai_Animset/Animation/katana/APose/Attack/Inplace/GhostSamurai_APose_SPAttack02_Inplace.FBX";

    private const string ChargeFbxPath =
        "Assets/_ThirdParty/GhostSamurai_Animset/Animation/katana/APose/Defense/Inplace/GhostSamurai_DefenseR_Loop_Inplace.FBX";

    [MenuItem("Tools/Fix HeavyAttack Root Motion + Events")]
    public static void Fix()
    {
        FixCharge();
        FixAttack();
    }

    private static void FixCharge()
    {
        var importer = AssetImporter.GetAtPath(ChargeFbxPath) as ModelImporter;
        if (importer == null) { Debug.LogError($"[HeavyEvents] 없음: {ChargeFbxPath}"); return; }

        var clips = importer.clipAnimations;
        if (clips == null || clips.Length == 0)
            clips = importer.defaultClipAnimations;
        if (clips == null || clips.Length == 0) { Debug.LogError("[HeavyEvents] HeavyCharge 클립 없음"); return; }

        ApplyRootLock(clips[0]);
        clips[0].loop = true;
        clips[0].loopTime = true;
        clips[0].events = new AnimationEvent[0];

        importer.clipAnimations = clips;
        importer.SaveAndReimport();
        Debug.Log($"[HeavyEvents] HeavyCharge 루트모션 고정 완료: {clips[0].name}");
    }

    private static void FixAttack()
    {
        var importer = AssetImporter.GetAtPath(AttackFbxPath) as ModelImporter;
        if (importer == null) { Debug.LogError($"[HeavyEvents] 없음: {AttackFbxPath}"); return; }

        var clips = importer.clipAnimations;
        if (clips == null || clips.Length == 0)
            clips = importer.defaultClipAnimations;
        if (clips == null || clips.Length == 0) { Debug.LogError("[HeavyEvents] SPAttack02 클립 없음"); return; }

        ApplyRootLock(clips[0]);
        clips[0].loop = false;
        clips[0].loopTime = false;
        // time = normalized (0~1). 초(seconds) 아님.
        // SpawnSlashEffect0=0.417(41.7%), AE_AttackEnd=0.707(70.7%)
        clips[0].events = new AnimationEvent[]
        {
            new AnimationEvent { time = 0.417f,     functionName = "SpawnSlashEffect0" },
            new AnimationEvent { time = 0.7074689f, functionName = "AE_AttackEnd" },
        };

        importer.clipAnimations = clips;
        importer.SaveAndReimport();
        Debug.Log($"[HeavyEvents] GroundHeavyAttack 이벤트 등록 완료: {clips[0].name}");
    }

    private static void ApplyRootLock(ModelImporterClipAnimation clip)
    {
        clip.keepOriginalPositionXZ = true;
        clip.keepOriginalPositionY = true;
        clip.keepOriginalOrientation = true;
        clip.lockRootHeightY = true;
        clip.lockRootPositionXZ = true;
        clip.lockRootRotation = true;
    }
}

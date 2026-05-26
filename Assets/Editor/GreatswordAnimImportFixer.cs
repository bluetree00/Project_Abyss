using UnityEditor;
using UnityEngine;

public static class GreatswordAnimImportFixer
{
    [MenuItem("Tools/Fix Greatsword Anim Import")]
    public static void Fix()
    {
        string[] paths = new string[]
        {
            "Assets/RelicFairy/Animations/Player/Test_01/Attack/GreatswordAttack_01.FBX",
            "Assets/RelicFairy/Animations/Player/Test_01/Attack/GreatswordAttack_02.FBX",
            "Assets/RelicFairy/Animations/Player/Test_01/Attack/GreatswordAttack_03.FBX",
        };

        string[] clipNames = new string[]
        {
            "GreatswordAttack_01",
            "GreatswordAttack_02",
            "GreatswordAttack_03",
        };

        float[] offsets = new float[] { 70f, -110f, -65f };

        // 각 클립별 애니메이션 이벤트
        // 1타(stepIndex 0): 베기
        var events1 = new AnimationEvent[]
        {
            MakeEvent(0.25f, "SpawnSlashEffect0"),
            MakeEvent(0.25f, "AE_BeginTrail"),
            MakeEvent(0.75f, "AE_EndTrail"),
            MakeEvent(1.0f,  "AE_AttackEnd"),
        };

        // 2타(stepIndex 1): 베기
        var events2 = new AnimationEvent[]
        {
            MakeEvent(0.2f,  "SpawnSlashEffect1"),
            MakeEvent(0.2f,  "AE_BeginTrail"),
            MakeEvent(0.75f, "AE_EndTrail"),
            MakeEvent(1.0f,  "AE_AttackEnd"),
        };

        // 3타(stepIndex 2): 찌르기
        var events3 = new AnimationEvent[]
        {
            MakeEvent(0.15f, "AE_BeginTrail"),
            MakeEvent(0.2f,  "SpawnSlashEffect2"),
            MakeEvent(0.7f,  "AE_EndTrail"),
            MakeEvent(1.0f,  "AE_AttackEnd"),
        };

        AnimationEvent[][] allEvents = { events1, events2, events3 };

        for (int i = 0; i < paths.Length; i++)
        {
            var importer = AssetImporter.GetAtPath(paths[i]) as ModelImporter;
            if (importer == null) continue;

            importer.animationType = ModelImporterAnimationType.Human;
            importer.sourceAvatar = null;

            var defaultClips = importer.defaultClipAnimations;
            if (defaultClips == null || defaultClips.Length == 0) continue;

            var clip = defaultClips[0];
            clip.name = clipNames[i];

            clip.lockRootRotation = true;
            clip.lockRootHeightY = true;
            clip.lockRootPositionXZ = true;
            clip.keepOriginalPositionXZ = false;
            clip.keepOriginalPositionY = true;
            clip.keepOriginalOrientation = false;
            clip.rotationOffset = offsets[i];

            // 애니메이션 이벤트 등록
            clip.events = allEvents[i];

            importer.clipAnimations = new ModelImporterClipAnimation[] { clip };
            importer.SaveAndReimport();

            AnimationClip loadedClip = null;
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(paths[i]))
            {
                if (asset is AnimationClip ac && !ac.name.StartsWith("__"))
                { loadedClip = ac; break; }
            }

            int evtCount = loadedClip != null ? loadedClip.events.Length : 0;
            Debug.Log($"[GreatswordFix] {clipNames[i]} | offset={offsets[i]}° | events={evtCount} | length={loadedClip?.length:F2}s");
        }

        Debug.Log("[GreatswordFix] Done!");
    }

    private static AnimationEvent MakeEvent(float time, string functionName)
    {
        return new AnimationEvent
        {
            time = time,
            functionName = functionName,
        };
    }
}

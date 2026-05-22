using UnityEditor;
using UnityEngine;

public static class CheckClipDuration
{
    [MenuItem("Tools/Check Heavy Attack Clip Durations")]
    public static void Check()
    {
        string[] paths = new[]
        {
            "Assets/_ThirdParty/GhostSamurai_Animset/Animation/katana/APose/Attack/Inplace/GhostSamurai_APose_SPAttack02_Inplace.FBX",
            "Assets/_ThirdParty/GhostSamurai_Animset/Animation/katana/APose/Defense/Inplace/GhostSamurai_DefenseR_Loop_Inplace.FBX",
        };

        foreach (var path in paths)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null) { Debug.LogWarning($"클립 없음: {path}"); continue; }
            Debug.Log($"[ClipInfo] {clip.name} | length={clip.length:F4}s | frameRate={clip.frameRate} | isLooping={clip.isLooping}");
        }
    }
}

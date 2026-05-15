using UnityEditor;
using UnityEngine;

public static class SetControllerDummyMotions
{
    private const string ControllerPath =
        "Assets/Abyss/Characters/Player/PlayerBaseController.controller";
    private const string HeavyChargeFbx =
        "Assets/_ThirdParty/GhostSamurai_Animset/Animation/katana/APose/GhostSamurai_APose_Idle.FBX";
    private const string GroundHeavyAttackFbx =
        "Assets/_ThirdParty/GhostSamurai_Animset/Animation/katana/APose/Attack/Inplace/GhostSamurai_APose_SPAttack01_Inplace.FBX";

    [MenuItem("Tools/Set Controller Dummy Motions")]
    public static void Set()
    {
        var controller = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(ControllerPath);
        if (controller == null) { Debug.LogError("[SetDummyMotions] 컨트롤러 없음"); return; }

        var heavyClip  = AssetDatabase.LoadAssetAtPath<AnimationClip>(HeavyChargeFbx);
        var groundClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(GroundHeavyAttackFbx);

        if (heavyClip  == null) { Debug.LogError("[SetDummyMotions] HeavyCharge FBX 클립 없음");       return; }
        if (groundClip == null) { Debug.LogError("[SetDummyMotions] GroundHeavyAttack FBX 클립 없음"); return; }

        int count = 0;
        var layers = controller.layers;
        for (int i = 0; i < layers.Length; i++)
        {
            var states = layers[i].stateMachine.states;
            for (int j = 0; j < states.Length; j++)
            {
                var state = states[j].state;
                if (state.name == "HeavyCharge")
                {
                    state.motion = heavyClip;
                    EditorUtility.SetDirty(state);
                    Debug.Log($"[SetDummyMotions] HeavyCharge motion='{heavyClip.name}'");
                    count++;
                }
                else if (state.name == "GroundHeavyAttack")
                {
                    state.motion = groundClip;
                    EditorUtility.SetDirty(state);
                    Debug.Log($"[SetDummyMotions] GroundHeavyAttack motion='{groundClip.name}'");
                    count++;
                }
            }
        }

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        Debug.Log($"[SetDummyMotions] 완료 {count}개 상태 등록");
    }
}

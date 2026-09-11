#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// 로코모션 상태(<c>MoveBlend</c>)를 <b>2D Freeform Directional</b> 블렌드 트리로 재구성한다.
/// 메뉴: RelicFairy/Animation/Rebuild Locomotion Blend Tree
///
/// 왜 도구인가 — 자식 17개짜리 2D 트리를 컨트롤러 YAML로 손으로 짜면 fileID·좌표를 한 곳만
/// 틀려도 조용히 깨진다. 그리고 무기 세트(검·활)를 붙일 때마다 같은 작업을 반복해야 하므로
/// 재실행 가능한 형태로 둔다.
///
/// <b>베이스는 맨손(Common)</b>이다. 무기를 들면 <see cref="WeaponAnimationSetSO"/> 매핑이
/// 같은 자리에 검(APose)·활(Bow) 클립을 오버라이드로 꽂는다 — 트리는 하나면 된다.
/// </summary>
public static class LocomotionBlendTreeBuilder
{
    private const string ControllerPath = "Assets/RelicFairy/Characters/Player/PlayerBaseController.controller";
    private const string ClipFolder     = "Assets/RelicFairy/_Imported/GhostSamurai_Animset/Animation/katana/Common/Inplace";
    private const string StateName      = "MoveBlend";
    private const string ParamX         = "MoveX";
    private const string ParamY         = "MoveY";

    /// <summary>걷기 링 반경 — 정규화 속도 기준(걷기 5 / 달리기 8 → 0.625).</summary>
    private const float WalkRadius = 0.625f;
    private const float RunRadius  = 1.0f;
    /// <summary>달리기 링 클립 재생 배속. 인플레이스 클립이라 발속도가 8m/s를 못 따라가 발이 미끄러진다 — 시각 판단으로 조정(2026-09-08).</summary>
    private const float RunTimeScale = 1.3f;

    /// <summary>8방향 단위 벡터. X=우, Y=전방. 대각선은 정규화해 링 위에 올린다.</summary>
    private static readonly (string dir, float x, float y)[] Dirs =
    {
        ("F",   0f,      1f),
        ("FL", -0.7071f, 0.7071f),
        ("FR",  0.7071f, 0.7071f),
        ("L",  -1f,      0f),
        ("R",   1f,      0f),
        ("B",   0f,     -1f),
        ("BL", -0.7071f,-0.7071f),
        ("BR",  0.7071f,-0.7071f),
    };

    [MenuItem("RelicFairy/Animation/Rebuild Locomotion Blend Tree")]
    public static void Rebuild()
    {
        var ac = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(ControllerPath);
        if (ac == null) { Debug.LogError($"[로코모션 트리] 컨트롤러 없음: {ControllerPath}"); return; }

        EnsureParam(ac, ParamX);
        EnsureParam(ac, ParamY);

        var state = FindState(ac, StateName);
        if (state == null) { Debug.LogError($"[로코모션 트리] 상태 '{StateName}' 없음"); return; }

        var idle = LoadClip("Common_Idle");
        if (idle == null) { Debug.LogError("[로코모션 트리] Common_Idle 없음 — 이관이 안 됐다"); return; }

        var tree = new UnityEditor.Animations.BlendTree
        {
            name               = "Locomotion2D",
            blendType          = UnityEditor.Animations.BlendTreeType.FreeformDirectional2D,
            blendParameter     = ParamX,
            blendParameterY    = ParamY,
            useAutomaticThresholds = false,
        };
        AssetDatabase.AddObjectToAsset(tree, ac);

        var children = new List<UnityEditor.Animations.ChildMotion>
        {
            new UnityEditor.Animations.ChildMotion { motion = idle, position = Vector2.zero, timeScale = 1f, directBlendParameter = ParamX },
        };

        int missing = 0;
        // 걷기 링 — Common만 방향 앞 언더스코어가 없다(Common_StrafeWalkFL). 규약 차이에 주의.
        foreach (var (dir, x, y) in Dirs)
            missing += AddRing(children, $"Common_StrafeWalk{dir}", x, y, WalkRadius, 1f);
        // 달리기 링
        foreach (var (dir, x, y) in Dirs)
            missing += AddRing(children, $"Common_StrafeRun_{dir}", x, y, RunRadius, RunTimeScale);

        tree.children = children.ToArray();
        state.motion  = tree;

        EditorUtility.SetDirty(ac);
        AssetDatabase.SaveAssets();

        Debug.Log($"[로코모션 트리] 재구성 완료 — 자식 {children.Count}개 " +
                  $"(Idle 1 + 걷기 8 + 달리기 8), 누락 {missing}개");
    }

    /// <summary>링 위 한 자리를 채운다. 클립이 없으면 자리를 비우고 누락으로 센다(조용히 넘기지 않는다).</summary>
    private static int AddRing(List<UnityEditor.Animations.ChildMotion> list, string clipName, float x, float y, float radius, float timeScale)
    {
        var clip = LoadClip(clipName);
        if (clip == null)
        {
            Debug.LogWarning($"[로코모션 트리] 클립 없음 — 자리 비움: {clipName}");
            return 1;
        }

        list.Add(new UnityEditor.Animations.ChildMotion
        {
            motion               = clip,
            position             = new Vector2(x * radius, y * radius),
            timeScale            = timeScale,
            directBlendParameter = ParamX,
        });
        return 0;
    }

    /// <summary>FBX 안의 AnimationClip 서브에셋을 이름으로 찾는다.</summary>
    private static AnimationClip LoadClip(string clipName)
    {
        string path = $"{ClipFolder}/GhostSamurai_{clipName}_Inplace.FBX";
        foreach (var a in AssetDatabase.LoadAllAssetsAtPath(path))
            if (a is AnimationClip c && !c.name.StartsWith("__preview__")) return c;
        return null;
    }

    private static void EnsureParam(UnityEditor.Animations.AnimatorController ac, string name)
    {
        foreach (var p in ac.parameters)
            if (p.name == name) return;
        ac.AddParameter(name, AnimatorControllerParameterType.Float);
        Debug.Log($"[로코모션 트리] 파라미터 추가: {name}");
    }

    private static UnityEditor.Animations.AnimatorState FindState(UnityEditor.Animations.AnimatorController ac, string name)
    {
        foreach (var layer in ac.layers)
            foreach (var s in layer.stateMachine.states)
                if (s.state.name == name) return s.state;
        return null;
    }
}
#endif

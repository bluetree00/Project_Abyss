#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;

/// <summary>
/// "캐릭터가 바닥을 뚫는다"를 <b>발바닥 높이</b>로 실측한다 — RootT.y(골반)는 웅크림과 침몰을 구분 못 한다.
/// 메뉴: RelicFairy/Animation/Probe Clip Foot Height → Library/RelicFairy_ClipFootHeight.txt
///
/// 방법: 플레이어 프리팹을 임시로 띄워(HideAndDontSave, 씬 오염 없음) 실제 아바타 위에서 클립을 샘플링하고
/// <see cref="Animator.leftFeetBottomHeight"/>/<see cref="Animator.rightFeetBottomHeight"/>(루트 기준 발바닥 높이)를 읽는다.
/// 두 발 중 낮은 쪽의 최소값이 음수면 그 프레임에 발이 바닥 아래로 들어간다.
/// 임포터 Y 기준(Original/Feet)과 함께 찍어 어떤 클립을 Feet 기준으로 돌려야 하는지 바로 고를 수 있게 한다.
/// </summary>
public static class ClipFootProbe
{
    private const string PlayerPrefab   = "Assets/RelicFairy/Characters/Player/Gawain/Prefabs/PlayerCharacter.prefab";
    private const string ControllerPath = "Assets/RelicFairy/Characters/Player/PlayerBaseController.controller";
    private static readonly string[] SetPaths =
    {
        "Assets/RelicFairy/Weapon/Katana/Data/KatanaBase/KatanaAnimation.asset",
        "Assets/RelicFairy/Weapon/Greatsword/Data/GreatswordBase/GreatswordAnimation.asset",
        "Assets/RelicFairy/Weapon/Bow/Data/BowBase/BowAnimation.asset",
        "Assets/RelicFairy/Weapon/Nameless/Data/NamelessBase/NamelessAnimation.asset",
    };
    private const int Samples = 48;

    [MenuItem("RelicFairy/Animation/Probe Clip Foot Height")]
    public static void Probe()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefab);
        if (prefab == null) { Debug.LogError($"[발높이] 프리팹 없음: {PlayerPrefab}"); return; }

        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        go.hideFlags = HideFlags.HideAndDontSave;
        go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        var animator = go.GetComponentInChildren<Animator>();
        if (animator == null) { Object.DestroyImmediate(go); Debug.LogError("[발높이] Animator 없음"); return; }
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;   // 화면 밖이면 포즈를 안 쓴다

        var sb = new StringBuilder();
        var seen = new HashSet<AnimationClip>();

        // 애니메이션 창과 같은 경로(AnimationMode)로 샘플링 — 휴머노이드 리타게팅을 아바타 위에서 실제로 돌린다.
        AnimationMode.StartAnimationMode();
        try
        {
            sb.AppendLine("[발높이] 값 = 발/발끝 본 중 가장 낮은 것의 Y − 루트 Y (m). 음수면 바닥 아래. 기준선은 Common_Idle 행과 비교.");
            sb.AppendLine("[발높이] ── 컨트롤러 모션 ──");
            var ac = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(ControllerPath);
            if (ac != null)
                foreach (var layer in ac.layers)
                    foreach (var cs in layer.stateMachine.states)
                        Collect(cs.state.motion, cs.state.name, go, animator, sb, seen);

            sb.AppendLine("[발높이] ── 무기 세트 어드레서블 클립 ──");
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            foreach (var p in SetPaths)
            {
                var set = AssetDatabase.LoadAssetAtPath<WeaponAnimationSetSO>(p);
                if (set == null) continue;
                foreach (var m in set.GetAllMappings())
                {
                    if (m == null || string.IsNullOrEmpty(m.addressableKey)) continue;
                    var clip = FindAddressableClip(settings, m.addressableKey);
                    if (clip != null) Report(clip, $"{set.name}:{m.addressableKey}", go, animator, sb, seen);
                }
            }
        }
        finally
        {
            AnimationMode.StopAnimationMode();
            Object.DestroyImmediate(go);
        }

        string outPath = System.IO.Path.Combine(Application.dataPath, "..", "Library", "RelicFairy_ClipFootHeight.txt");
        System.IO.File.WriteAllText(outPath, sb.ToString());
        Debug.Log(sb.ToString());
    }

    private static void Collect(Motion motion, string label, GameObject go, Animator animator, StringBuilder sb, HashSet<AnimationClip> seen)
    {
        if (motion == null) return;
        if (motion is AnimationClip clip) { Report(clip, label, go, animator, sb, seen); return; }
        if (motion is UnityEditor.Animations.BlendTree tree)
            foreach (var child in tree.children)
                Collect(child.motion, $"{label}/{tree.name}", go, animator, sb, seen);
    }

    private static readonly HumanBodyBones[] FootBones =
        { HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot, HumanBodyBones.LeftToes, HumanBodyBones.RightToes };

    private static void Report(AnimationClip clip, string label, GameObject go, Animator animator, StringBuilder sb, HashSet<AnimationClip> seen)
    {
        if (clip == null || !seen.Add(clip)) return;

        float rootY = go.transform.position.y;
        float minFoot = float.MaxValue, first = 0f, minHips = float.MaxValue;
        int below = 0;
        var hips = animator.GetBoneTransform(HumanBodyBones.Hips);

        for (int i = 0; i <= Samples; i++)
        {
            float t = clip.length * i / Samples;
            AnimationMode.BeginSampling();
            AnimationMode.SampleAnimationClip(animator.gameObject, clip, t);
            AnimationMode.EndSampling();

            float foot = float.MaxValue;
            foreach (var b in FootBones)
            {
                var tr = animator.GetBoneTransform(b);
                if (tr != null) foot = Mathf.Min(foot, tr.position.y - rootY);
            }
            if (foot == float.MaxValue) foot = 0f;
            if (i == 0) first = foot;
            minFoot = Mathf.Min(minFoot, foot);
            if (hips != null) minHips = Mathf.Min(minHips, hips.position.y - rootY);
            if (foot < -0.03f) below++;
        }

        string basis = "(.anim)";
        string path  = AssetDatabase.GetAssetPath(clip);
        if (AssetImporter.GetAtPath(path) is ModelImporter mi)
        {
            var clips = mi.clipAnimations;
            if (clips == null || clips.Length == 0) clips = mi.defaultClipAnimations;
            if (clips != null)
                foreach (var c in clips)
                    if (c.name == clip.name)
                    { basis = c.keepOriginalPositionY ? "Original" : (c.heightFromFeet ? "Feet" : "CoM"); break; }
        }

        string flag = minFoot < -0.05f ? "  ◆ 바닥 아래" : "";
        sb.AppendLine($"  {label}: '{clip.name}' 발 min={minFoot:+0.000;-0.000} first={first:+0.000;-0.000} 골반 min={minHips:0.000} 아래프레임={below}/{Samples + 1} Y기준={basis}{flag}");
    }

    private static AnimationClip FindAddressableClip(UnityEditor.AddressableAssets.Settings.AddressableAssetSettings settings, string key)
    {
        if (settings == null) return null;
        foreach (var g in settings.groups)
        {
            if (g == null) continue;
            foreach (var e in g.entries)
            {
                if (e.address != key) continue;
                string p = AssetDatabase.GUIDToAssetPath(e.guid);
                foreach (var a in AssetDatabase.LoadAllAssetsAtPath(p))
                    if (a is AnimationClip c && !c.name.StartsWith("__preview__")) return c;
            }
        }
        return null;
    }
}
#endif

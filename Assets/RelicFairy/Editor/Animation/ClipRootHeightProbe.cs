#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;

/// <summary>
/// "캐릭터가 바닥을 뚫는다" 점검용 — 플레이어가 쓰는 모든 휴머노이드 클립의 <b>루트 높이(RootT.y)</b>를 실측한다.
/// 메뉴: RelicFairy/Animation/Probe Clip Root Height → 콘솔 + Library/RelicFairy_ClipRootHeight.txt
///
/// 휴머노이드 클립의 RootT.y는 아바타 human scale로 정규화된 골반 높이다(서 있으면 대략 0.9~1.0).
/// 이 값이 다른 클립보다 눈에 띄게 낮으면 그 클립을 재생할 때 몸이 바닥 아래로 내려간다.
/// 원인은 대개 ModelImporter의 Root Transform Position(Y) 기준이 <b>Original</b>(원본 리그의 루트 위치 그대로)이라
/// 리그 원점이 다른 팩의 클립이 CombatGirl 위에서 어긋나는 것 — 기준을 <b>Feet</b>로 바꾸면 발바닥이 y=0에 맞는다.
///
/// 대상: 베이스 컨트롤러가 참조하는 모든 모션(블렌드 트리 자식 포함) + 무기 세트/유물이 어드레서블로 덮는 클립.
/// FBX 클립은 임포터 설정(기준·베이크)도 함께 찍는다.
/// </summary>
public static class ClipRootHeightProbe
{
    private const string ControllerPath = "Assets/RelicFairy/Characters/Player/PlayerBaseController.controller";
    private static readonly string[] SetPaths =
    {
        "Assets/RelicFairy/Weapon/Katana/Data/KatanaBase/KatanaAnimation.asset",
        "Assets/RelicFairy/Weapon/Greatsword/Data/GreatswordBase/GreatswordAnimation.asset",
        "Assets/RelicFairy/Weapon/Bow/Data/BowBase/BowAnimation.asset",
        "Assets/RelicFairy/Weapon/Crossbow/Data/CrossbowBase/CrossbowAnimation.asset",
        "Assets/RelicFairy/Weapon/Nameless/Data/NamelessBase/NamelessAnimation.asset",
    };

    [MenuItem("RelicFairy/Animation/Probe Clip Root Height")]
    public static void Probe()
    {
        var sb = new StringBuilder();
        var seen = new HashSet<AnimationClip>();

        sb.AppendLine("[루트높이] ── 컨트롤러 모션 ──");
        var ac = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(ControllerPath);
        if (ac != null)
            foreach (var layer in ac.layers)
                foreach (var cs in layer.stateMachine.states)
                    Collect(cs.state.motion, cs.state.name, sb, seen);

        sb.AppendLine("[루트높이] ── 무기 세트 어드레서블 클립 ──");
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        foreach (var p in SetPaths)
        {
            var set = AssetDatabase.LoadAssetAtPath<WeaponAnimationSetSO>(p);
            if (set == null) continue;
            foreach (var m in set.GetAllMappings())
            {
                if (m == null || string.IsNullOrEmpty(m.addressableKey)) continue;
                var clip = FindAddressableClip(settings, m.addressableKey);
                if (clip == null) { sb.AppendLine($"  {set.name}:{m.addressableKey}: (클립 없음)"); continue; }
                Report(clip, $"{set.name}:{m.addressableKey}", sb, seen);
            }
        }

        string outPath = System.IO.Path.Combine(Application.dataPath, "..", "Library", "RelicFairy_ClipRootHeight.txt");
        System.IO.File.WriteAllText(outPath, sb.ToString());
        Debug.Log(sb.ToString());
    }

    private static void Collect(Motion motion, string label, StringBuilder sb, HashSet<AnimationClip> seen)
    {
        if (motion == null) return;
        if (motion is AnimationClip clip) { Report(clip, label, sb, seen); return; }
        if (motion is UnityEditor.Animations.BlendTree tree)
            foreach (var child in tree.children)
                Collect(child.motion, $"{label}/{tree.name}", sb, seen);
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
                string path = AssetDatabase.GUIDToAssetPath(e.guid);
                foreach (var a in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (a is AnimationClip c && !c.name.StartsWith("__preview__")) return c;
            }
        }
        return null;
    }

    private static void Report(AnimationClip clip, string label, StringBuilder sb, HashSet<AnimationClip> seen)
    {
        if (clip == null || !seen.Add(clip)) return;

        string path = AssetDatabase.GetAssetPath(clip);
        var curve = AnimationUtility.GetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), "RootT.y"));
        string height = "RootT.y 없음(제네릭?)";
        if (curve != null && curve.length > 0)
        {
            float min = float.MaxValue, max = float.MinValue, sum = 0f; int n = 0;
            for (float t = 0f; t <= clip.length; t += Mathf.Max(0.02f, clip.length / 40f))
            {
                float v = curve.Evaluate(t);
                min = Mathf.Min(min, v); max = Mathf.Max(max, v); sum += v; n++;
            }
            height = $"RootT.y min={min:F3} avg={sum / Mathf.Max(1, n):F3} max={max:F3} first={curve.Evaluate(0f):F3}";
        }

        string importer = "";
        if (AssetImporter.GetAtPath(path) is ModelImporter mi)
        {
            var clips = mi.clipAnimations;
            if (clips == null || clips.Length == 0) clips = mi.defaultClipAnimations;
            ModelImporterClipAnimation found = null;
            if (clips != null) foreach (var c in clips) if (c.name == clip.name) { found = c; break; }
            if (found != null)
                importer = $" | Y기준={(found.keepOriginalPositionY ? "Original" : (found.heightFromFeet ? "Feet" : "CenterOfMass"))} bakeY={found.lockRootHeightY} bakeRot={found.lockRootRotation} bakeXZ={found.lockRootPositionXZ} rootMotion={(mi.motionNodeName ?? "")}";
        }
        else importer = " | (.anim)";

        sb.AppendLine($"  {label}: '{clip.name}' len={clip.length:F2} {height}{importer}  ← {System.IO.Path.GetFileName(path)}");
    }
}
#endif

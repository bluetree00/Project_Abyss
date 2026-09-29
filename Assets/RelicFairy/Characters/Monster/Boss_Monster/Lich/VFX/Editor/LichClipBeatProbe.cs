using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace RelicFairy.Monster.EditorTools
{
/// <summary>
/// 리치 시전 클립의 박자 실측 — 손이 가장 높이 올라가는 순간(충전 자세)과 그 뒤 손이 가장 빠른 순간(방출)을 잰다.
/// 시전 박자(<see cref="LichSwingDriver.PlayBeat"/>)의 상수를 이 값으로 정한다. 결과: Logs/lich_clip_beats.txt.
/// 프리팹을 격리 장면에 열어(PrefabUtility.LoadPrefabContents) 샘플하므로 열린 씬은 건드리지 않는다.
/// </summary>
public static class LichClipBeatProbe
{
    private const string PrefabPath = "Assets/RelicFairy/Characters/Monster/Boss_Monster/Lich/Lich.prefab";
    private const string ControllerPath = "Assets/RelicFairy/Characters/Monster/Boss_Monster/Lich/LichAnimator.controller";
    private const float  Step       = 1f / 60f;

    private static readonly string[] States =
    {
        "MagicBolt", "ArcaneOrb", "ElementalBarrage", "DeathRayStart", "TeleportStrike", "Phase2Entry", "Appear", "ScytheThrow",
    };

    [MenuItem("RelicFairy/Boss/Lich/Measure Cast Clip Beats")]
    public static void Measure()
    {
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        var sb   = new StringBuilder();
        try
        {
            var anim = root.GetComponentInChildren<Animator>(true);
            // 프리팹의 애니메이터는 컨트롤러가 비어 있다(런타임에 넣는다) — 에셋을 직접 연다.
            var ctrl = anim != null ? anim.runtimeAnimatorController as UnityEditor.Animations.AnimatorController : null;
            if (ctrl == null) ctrl = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(ControllerPath);
            if (anim == null || ctrl == null) { Debug.LogWarning("[LichClipBeat] 애니메이터 컨트롤러를 못 찾았다"); return; }

            Transform right = null, left = null;
            if (anim.isHuman)
            {
                right = anim.GetBoneTransform(HumanBodyBones.RightHand);
                left  = anim.GetBoneTransform(HumanBodyBones.LeftHand);
            }
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name.ToLowerInvariant();
                if (!n.Contains("hand")) continue;
                if (right == null && (n.Contains("_r") || n.Contains("right") || n.Contains(" r "))) right = t;
                if (left  == null && (n.Contains("_l") || n.Contains("left")  || n.Contains(" l "))) left  = t;
            }
            sb.AppendLine($"prefab={PrefabPath} human={anim.isHuman} right={Path(right, root.transform)} left={Path(left, root.transform)}");

            var clips = new Dictionary<string, AnimationClip>();
            foreach (var cs in ctrl.layers[0].stateMachine.states)
                if (cs.state.motion is AnimationClip c) clips[cs.state.name] = c;

            foreach (var state in States)
            {
                if (!clips.TryGetValue(state, out var clip)) { sb.AppendLine($"{state}: (없음)"); continue; }
                sb.AppendLine(MeasureClip(state, clip, anim.gameObject, root.transform, right, left));
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/lich_clip_beats.txt", sb.ToString(), Encoding.UTF8);
        Debug.Log("[LichClipBeat] Logs/lich_clip_beats.txt\n" + sb);
    }

    private static string MeasureClip(string state, AnimationClip clip, GameObject target, Transform root,
                                      Transform right, Transform left)
    {
        int frames = Mathf.Max(2, Mathf.CeilToInt(clip.length / Step) + 1);
        var hr = new float[frames];
        var hl = new float[frames];
        var vr = new float[frames];
        var vl = new float[frames];
        Vector3 pr = Vector3.zero, pl = Vector3.zero;
        for (int i = 0; i < frames; i++)
        {
            float t = Mathf.Min(clip.length, i * Step);
            clip.SampleAnimation(target, t);
            Vector3 r = right != null ? root.InverseTransformPoint(right.position) : Vector3.zero;
            Vector3 l = left  != null ? root.InverseTransformPoint(left.position)  : Vector3.zero;
            hr[i] = r.y;
            hl[i] = l.y;
            if (i > 0)
            {
                vr[i] = (r - pr).magnitude / Step;
                vl[i] = (l - pl).magnitude / Step;
            }
            pr = r;
            pl = l;
        }

        // 두 손 중 더 높이 드는 손을 기준으로
        int peakR = ArgMax(hr, 0), peakL = ArgMax(hl, 0);
        bool useRight = hr[peakR] - Min(hr) >= hl[peakL] - Min(hl);
        var h = useRight ? hr : hl;
        var v = useRight ? vr : vl;
        int peak    = useRight ? peakR : peakL;
        int release = ArgMax(v, peak);
        int fastest = ArgMax(v, 0);

        var curve = new StringBuilder();
        for (int i = 0; i < frames; i += 6)
            curve.Append($" {i * Step:F1}:{h[i]:F2}/{v[i]:F1}");

        return $"{state}: len={clip.length:F2}s loop={clip.isLooping} hand={(useRight ? "R" : "L")} " +
               $"raise={Min(h):F2}→{h[peak]:F2}m @ {peak * Step:F2}s · releasePeak={v[release]:F1}m/s @ {release * Step:F2}s · " +
               $"fastest={v[fastest]:F1}m/s @ {fastest * Step:F2}s\n   곡선(초:높이/속도){curve}";
    }

    private static int ArgMax(float[] a, int from)
    {
        int best = Mathf.Clamp(from, 0, a.Length - 1);
        for (int i = best; i < a.Length; i++) if (a[i] > a[best]) best = i;
        return best;
    }

    private static float Min(float[] a)
    {
        float m = float.MaxValue;
        foreach (var x in a) if (x < m) m = x;
        return m;
    }

    private static string Path(Transform t, Transform root)
    {
        if (t == null) return "(없음)";
        var parts = new List<string>();
        for (var c = t; c != null && c != root; c = c.parent) parts.Insert(0, c.name);
        return string.Join("/", parts);
    }
}
}

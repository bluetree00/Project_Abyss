#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

using AnimatorControllerAsset = UnityEditor.Animations.AnimatorController;

/// <summary>
/// 숲의 수호자 돌진 클립을 <b>제자리판</b>(<c>&lt;파일명&gt;_InPlace.anim</c>)으로 굽고, 컨트롤러 상태가 그 클립을 쓰게 바꾼다(10-03).
/// 메뉴: RelicFairy/Animation/Bake Forest Guardian In-Place Charge Clips (멱등 — 다시 돌리면 같은 GUID에 덮어쓴다)
///
/// 원인 — 제네릭 리그에 루트 모션 노드가 비어 있어(motionNodeName 없음) 클립의 골반 전진이 몸에 그대로 실린다.
/// <c>Treant@Charge</c>는 1초 동안 골반이 앞으로 12.6 m 간다. 코드(FGChargePatternSO)가 NavMeshAgent.Move로 따로 옮기니
/// 몸은 「코드 이동 + 클립 전진」만큼 갔다가, 다음 상태로 넘어가는 순간 transform 자리로 튄다(4-1 「돌진 뒤 제자리 순간이동」).
///
/// 처리 — 원본 FBX 클립의 커브·설정·이벤트를 새 클립에 옮기고, 골반(및 있으면 루트) <c>m_LocalPosition.x/z</c>에서
/// 시작→끝 직선을 뺀다: v'(t) = v(t) − (v0 + (v1 − v0)·t/len). 시작·끝은 0, 걸음 흔들림은 남는다
/// (선례 <c>Treant@SlowWalk_InPlace.anim</c>: 골반 x/z 시작=끝=0, 범위 0.135/0.124 m 그대로).
/// 감속이 큰 클립은 직선을 빼도 앞뒤 흔들림이 크게 남는다 — 잔차 범위가 <see cref="ResidualWarn"/>를 넘으면 경고한다.
///
/// 컨트롤러는 YAML이 아니라 <see cref="AnimatorControllerAsset"/> API로 고친다(레이어 · 하위 상태 기계 · 블렌드 트리 전부).
/// </summary>
public static class InPlaceClipBaker
{
    private const string Dir            = "Assets/RelicFairy/Characters/Monster/Monster/ForestGuardian/Art/Animations/";
    private const string ControllerPath = "Assets/RelicFairy/Characters/Monster/Monster/ForestGuardian/ForestGuardianAnimatorController.controller";
    private const string Suffix         = "_InPlace";
    private const float  ResidualWarn   = 0.6f;   // m — 직선을 뺀 뒤 남는 앞뒤 흔들림이 이보다 크면 경고
    private const int    Samples        = 60;
    private const string Tag            = "[제자리 굽기]";

    private static readonly string[] SourceFbx =
    {
        "Treant@Charge.fbx",
        "Treant@ChargeTackle01.fbx",
        "Treant@ChargeTackle02.fbx",
        "Treant@ChargeTackle03.fbx",
    };

    /// <summary>수평 이동을 걷어낼 경로 — 골반, 그리고 클립에 있으면 루트.</summary>
    private static readonly HashSet<string> DriftPaths = new() { "", "Treant", "TreantPelvis" };

    [MenuItem("RelicFairy/Animation/Bake Forest Guardian In-Place Charge Clips")]
    public static void Bake()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning($"{Tag} 플레이 중에는 굽지 않는다 — 에셋 쓰기가 런을 깰 수 있다. 플레이를 멈추고 다시 실행.");
            return;
        }

        var map = new Dictionary<AnimationClip, AnimationClip>();
        foreach (var file in SourceFbx)
        {
            var src = LoadFbxClip(Dir + file);
            if (src == null) { Debug.LogError($"{Tag} 원본 클립 없음: {Dir + file}"); continue; }

            string outPath = Dir + Path.GetFileNameWithoutExtension(file) + Suffix + ".anim";
            var baked = BakeOne(src, outPath);
            if (baked != null) map[src] = baked;
        }

        var controller = AssetDatabase.LoadAssetAtPath<AnimatorControllerAsset>(ControllerPath);
        if (controller == null)
        {
            Debug.LogError($"{Tag} 컨트롤러 없음: {ControllerPath}");
        }
        else
        {
            int changed = 0;
            foreach (var layer in controller.layers)
                changed += ReplaceInMachine(layer.stateMachine, map, layer.name);
            if (changed > 0) EditorUtility.SetDirty(controller);
            Debug.Log($"{Tag} 컨트롤러 상태 교체 {changed}건 — {controller.name}");
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"{Tag} 완료 — 클립 {map.Count}/{SourceFbx.Length}");
    }

    // ── 클립 굽기 ─────────────────────────────────────────

    private static AnimationClip LoadFbxClip(string path)
    {
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
            if (o is AnimationClip c && !c.name.StartsWith("__preview__")) return c;
        return null;
    }

    private static AnimationClip BakeOne(AnimationClip src, string outPath)
    {
        string assetName = Path.GetFileNameWithoutExtension(outPath);
        var clip = new AnimationClip
        {
            name      = assetName,
            frameRate = src.frameRate,
            wrapMode  = src.wrapMode,
        };

        // 수치 커브 — 수평 이동 경로의 x/z만 직선을 뺀다
        var bindings = AnimationUtility.GetCurveBindings(src);
        var curves   = new AnimationCurve[bindings.Length];
        for (int i = 0; i < bindings.Length; i++)
        {
            var b = bindings[i];
            var c = AnimationUtility.GetEditorCurve(src, b);
            if (c != null && b.type == typeof(Transform) && DriftPaths.Contains(b.path) &&
                (b.propertyName == "m_LocalPosition.x" || b.propertyName == "m_LocalPosition.z"))
            {
                var flat = Detrend(c);
                LogCurve(src, b, c, flat);
                c = flat;
            }
            curves[i] = c;
        }
        AnimationUtility.SetEditorCurves(clip, bindings, curves);

        // 오브젝트 참조 커브(있으면)
        var refBindings = AnimationUtility.GetObjectReferenceCurveBindings(src);
        if (refBindings.Length > 0)
        {
            var refCurves = new ObjectReferenceKeyframe[refBindings.Length][];
            for (int i = 0; i < refBindings.Length; i++)
                refCurves[i] = AnimationUtility.GetObjectReferenceCurve(src, refBindings[i]);
            AnimationUtility.SetObjectReferenceCurves(clip, refBindings, refCurves);
        }

        var settings = AnimationUtility.GetAnimationClipSettings(src);
        // 돌진은 최대 약 2.7초(43 m ÷ 16 m/s)인데 원본은 1초 비반복이라 1초 뒤 자세가 굳은 채 미끄러졌다 — 걸음 2주기라 반복으로(10-03)
        if (src.name == "Charge") settings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        AnimationUtility.SetAnimationEvents(clip, AnimationUtility.GetAnimationEvents(src));

        // 저장 — 이미 있으면 내용만 바꿔 GUID(컨트롤러 참조)를 지킨다
        var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(outPath);
        if (existing != null)
        {
            EditorUtility.CopySerialized(clip, existing);
            existing.name = assetName;
            Object.DestroyImmediate(clip);
            EditorUtility.SetDirty(existing);
            Debug.Log($"{Tag} 덮어씀 {outPath} ← {src.name} ({existing.length:F2}s, 커브 {bindings.Length})");
            return existing;
        }

        AssetDatabase.CreateAsset(clip, outPath);
        Debug.Log($"{Tag} 생성 {outPath} ← {src.name} ({clip.length:F2}s, 커브 {bindings.Length})");
        return clip;
    }

    /// <summary>v'(t) = v(t) − (v0 + (v1 − v0)·(t − t0)/len). 접선에서도 같은 기울기를 빼 곡선 모양(흔들림)은 그대로 둔다.</summary>
    private static AnimationCurve Detrend(AnimationCurve src)
    {
        var keys = src.keys;
        if (keys.Length == 0) return new AnimationCurve(keys);

        float t0 = keys[0].time, v0 = keys[0].value;
        float t1 = keys[keys.Length - 1].time, v1 = keys[keys.Length - 1].value;
        float slope = t1 > t0 ? (v1 - v0) / (t1 - t0) : 0f;

        for (int i = 0; i < keys.Length; i++)
        {
            var k = keys[i];
            k.value -= v0 + slope * (k.time - t0);
            if (!float.IsInfinity(k.inTangent))  k.inTangent  -= slope;   // 계단(상수) 접선은 그대로
            if (!float.IsInfinity(k.outTangent)) k.outTangent -= slope;
            keys[i] = k;
        }
        return new AnimationCurve(keys) { preWrapMode = src.preWrapMode, postWrapMode = src.postWrapMode };
    }

    private static void LogCurve(AnimationClip src, EditorCurveBinding b, AnimationCurve before, AnimationCurve after)
    {
        float len = src.length;
        float min = float.MaxValue, max = float.MinValue;
        for (int i = 0; i <= Samples; i++)
        {
            float v = after.Evaluate(len * i / Samples);
            if (v < min) min = v;
            if (v > max) max = v;
        }
        float range = max - min;
        string line = $"{Tag} {src.name} {b.path} {b.propertyName} 원본 {before.Evaluate(0f):F3}→{before.Evaluate(len):F3} " +
                      $"⇒ {after.Evaluate(0f):F3}→{after.Evaluate(len):F3} 잔차범위 {range:F3}";
        if (range > ResidualWarn)
            Debug.LogWarning(line + $" ⚠ {ResidualWarn} m 초과 — 감속·가속이 커서 직선 제거로는 몸이 앞뒤로 흔들린다");
        else
            Debug.Log(line);
    }

    // ── 컨트롤러 교체 ─────────────────────────────────────

    private static int ReplaceInMachine(AnimatorStateMachine sm, Dictionary<AnimationClip, AnimationClip> map, string where)
    {
        if (sm == null) return 0;
        int n = 0;
        foreach (var cs in sm.states)
        {
            var st = cs.state;
            if (st == null) continue;
            if (st.motion is AnimationClip c && map.TryGetValue(c, out var to))
            {
                st.motion = to;
                EditorUtility.SetDirty(st);
                Debug.Log($"{Tag} 상태 {where}/{st.name}: {c.name} → {to.name}");
                n++;
            }
            else if (st.motion is BlendTree bt)
            {
                n += ReplaceInTree(bt, map, $"{where}/{st.name}");
            }
        }
        foreach (var child in sm.stateMachines)
            n += ReplaceInMachine(child.stateMachine, map, $"{where}/{child.stateMachine.name}");
        return n;
    }

    private static int ReplaceInTree(BlendTree bt, Dictionary<AnimationClip, AnimationClip> map, string where)
    {
        int n = 0;
        bool changed = false;
        var children = bt.children;   // 구조체 배열 사본 — 고친 뒤 다시 넣어야 반영된다
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i].motion is AnimationClip c && map.TryGetValue(c, out var to))
            {
                children[i].motion = to;
                changed = true;
                Debug.Log($"{Tag} 블렌드 트리 {where}/{bt.name}[{i}]: {c.name} → {to.name}");
                n++;
            }
            else if (children[i].motion is BlendTree sub)
            {
                n += ReplaceInTree(sub, map, $"{where}/{bt.name}");
            }
        }
        if (changed)
        {
            bt.children = children;
            EditorUtility.SetDirty(bt);
        }
        return n;
    }
}
#endif

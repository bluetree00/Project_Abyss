#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityAnimatorController = UnityEditor.Animations.AnimatorController;

public static class ForestGuardianSlowWalkFixer
{
    private const string SourceClipPath =
        "Assets/RelicFairy/Characters/Monster/Monster/ForestGuardian/Art/Animations/Treant@SlowWalk.fbx";
    private const string OutputClipPath =
        "Assets/RelicFairy/Characters/Monster/Monster/ForestGuardian/Art/Animations/Treant@SlowWalk_InPlace.anim";
    private const string ControllerPath =
        "Assets/RelicFairy/Characters/Monster/Monster/ForestGuardian/ForestGuardianAnimatorController.controller";

    private const string SourceClipName = "SlowWalk";
    private const string OutputClipName = "SlowWalk_InPlace";
    private const string WalkStateName = "Walk";

    [MenuItem("RelicFairy/Dev/ForestGuardian/Diagnose SlowWalk")]
    public static void DiagnoseSlowWalkRootCurves()
    {
        var sourceClip = LoadSourceClip();
        if (sourceClip == null)
            return;

        var positionBindings = AnimationUtility.GetCurveBindings(sourceClip)
            .Where(IsTransformPositionBinding)
            .OrderBy(binding => binding.path.Length)
            .ThenBy(binding => binding.path)
            .ThenBy(binding => binding.propertyName)
            .ToArray();

        if (positionBindings.Length == 0)
        {
            Debug.LogWarning("[FG SlowWalk Fixer] No transform position curves were found on SlowWalk.");
            return;
        }

        var chosenRootPath = ResolveRootPath(sourceClip, positionBindings);
        Debug.Log($"[FG SlowWalk Fixer] Chosen root path: {FormatPath(chosenRootPath)}");

        foreach (var binding in positionBindings)
        {
            var curve = AnimationUtility.GetEditorCurve(sourceClip, binding);
            if (curve == null || curve.length == 0)
                continue;

            float start = curve.Evaluate(0f);
            float end = curve.Evaluate(sourceClip.length);
            Debug.Log(
                $"[FG SlowWalk Fixer] path={FormatPath(binding.path)} property={binding.propertyName} " +
                $"start={start:F5} end={end:F5} delta={(end - start):F5}");
        }
    }

    [MenuItem("RelicFairy/Dev/ForestGuardian/Rebuild SlowWalk In-Place")]
    public static void RebuildSlowWalkInPlace()
    {
        var sourceClip = LoadSourceClip();
        if (sourceClip == null)
            return;

        var bakedClip = UnityEngine.Object.Instantiate(sourceClip);
        bakedClip.name = OutputClipName;

        CopyClipSettings(sourceClip, bakedClip);
        RemoveNetRootTranslation(sourceClip, bakedClip);

        EnsureParentFolder(OutputClipPath);
        AssetDatabase.DeleteAsset(OutputClipPath);
        AssetDatabase.CreateAsset(bakedClip, OutputClipPath);

        var savedClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(OutputClipPath);
        if (savedClip == null)
        {
            Debug.LogError("[FG SlowWalk Fixer] Failed to create the in-place SlowWalk clip.");
            return;
        }

        if (!RebindWalkState(savedClip))
            return;

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log(
            $"[FG SlowWalk Fixer] Created '{OutputClipPath}' and rebound controller state '{WalkStateName}' " +
            "to the in-place clip.");
    }

    private static AnimationClip LoadSourceClip()
    {
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(SourceClipPath))
        {
            if (asset is AnimationClip clip && !clip.name.StartsWith("__", StringComparison.Ordinal) &&
                clip.name == SourceClipName)
            {
                return clip;
            }
        }

        Debug.LogError($"[FG SlowWalk Fixer] Could not find clip '{SourceClipName}' at '{SourceClipPath}'.");
        return null;
    }

    private static void CopyClipSettings(AnimationClip sourceClip, AnimationClip targetClip)
    {
        AnimationUtility.SetAnimationEvents(targetClip, AnimationUtility.GetAnimationEvents(sourceClip));
        AnimationUtility.SetAnimationClipSettings(targetClip, AnimationUtility.GetAnimationClipSettings(sourceClip));

        foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(sourceClip))
        {
            var frames = AnimationUtility.GetObjectReferenceCurve(sourceClip, binding);
            AnimationUtility.SetObjectReferenceCurve(targetClip, binding, frames);
        }
    }

    private static void RemoveNetRootTranslation(AnimationClip sourceClip, AnimationClip targetClip)
    {
        var positionBindings = AnimationUtility.GetCurveBindings(sourceClip)
            .Where(IsTransformPositionBinding)
            .ToArray();

        if (positionBindings.Length == 0)
        {
            Debug.LogWarning("[FG SlowWalk Fixer] SlowWalk has no transform position curves to process.");
            return;
        }

        string rootPath = ResolveRootPath(sourceClip, positionBindings);
        if (rootPath == null)
        {
            Debug.LogError("[FG SlowWalk Fixer] Failed to resolve a root path for SlowWalk.");
            return;
        }

        foreach (var binding in positionBindings)
        {
            if (binding.path != rootPath)
                continue;

            if (binding.propertyName != "m_LocalPosition.x" &&
                binding.propertyName != "m_LocalPosition.z")
            {
                continue;
            }

            var curve = AnimationUtility.GetEditorCurve(targetClip, binding);
            if (curve == null || curve.length == 0)
                continue;

            var processed = RemoveLinearDisplacement(curve, targetClip.length);
            AnimationUtility.SetEditorCurve(targetClip, binding, processed);
        }

        Debug.Log($"[FG SlowWalk Fixer] Removed net XZ translation from root path {FormatPath(rootPath)}.");
    }

    private static AnimationCurve RemoveLinearDisplacement(AnimationCurve sourceCurve, float clipLength)
    {
        if (sourceCurve == null || sourceCurve.length == 0 || clipLength <= 0f)
            return sourceCurve;

        float start = sourceCurve.Evaluate(0f);
        float end = sourceCurve.Evaluate(clipLength);
        float slope = (end - start) / clipLength;

        var keys = sourceCurve.keys;
        for (int i = 0; i < keys.Length; i++)
        {
            var key = keys[i];
            key.value -= start + (slope * key.time);
            key.inTangent -= slope;
            key.outTangent -= slope;
            keys[i] = key;
        }

        var curve = new AnimationCurve(keys)
        {
            preWrapMode = sourceCurve.preWrapMode,
            postWrapMode = sourceCurve.postWrapMode
        };

        return curve;
    }

    private static bool RebindWalkState(AnimationClip newClip)
    {
        var controller = AssetDatabase.LoadAssetAtPath<UnityAnimatorController>(ControllerPath);
        if (controller == null)
        {
            Debug.LogError($"[FG SlowWalk Fixer] Could not load controller at '{ControllerPath}'.");
            return false;
        }

        foreach (var layer in controller.layers)
        {
            foreach (var childState in layer.stateMachine.states)
            {
                if (childState.state == null || childState.state.name != WalkStateName)
                    continue;

                childState.state.motion = newClip;
                EditorUtility.SetDirty(childState.state);
                EditorUtility.SetDirty(controller);
                return true;
            }
        }

        Debug.LogError($"[FG SlowWalk Fixer] Controller state '{WalkStateName}' was not found.");
        return false;
    }

    private static bool IsTransformPositionBinding(EditorCurveBinding binding)
    {
        return binding.type == typeof(Transform) &&
               (binding.propertyName == "m_LocalPosition.x" ||
                binding.propertyName == "m_LocalPosition.y" ||
                binding.propertyName == "m_LocalPosition.z");
    }

    private static string ResolveRootPath(AnimationClip clip, EditorCurveBinding[] positionBindings)
    {
        return positionBindings
            .Select(binding => binding.path)
            .Distinct()
            .Select(path => new
            {
                Path = path,
                Displacement = CalculateRootDisplacement(clip, path),
                Depth = string.IsNullOrEmpty(path) ? 0 : path.Count(ch => ch == '/') + 1
            })
            .OrderByDescending(entry => entry.Displacement)
            .ThenBy(entry => entry.Depth)
            .ThenBy(entry => entry.Path.Length)
            .Select(entry => entry.Path)
            .FirstOrDefault();
    }

    private static float CalculateRootDisplacement(AnimationClip clip, string path)
    {
        float dx = GetCurveDelta(clip, path, "m_LocalPosition.x");
        float dz = GetCurveDelta(clip, path, "m_LocalPosition.z");
        return Mathf.Sqrt((dx * dx) + (dz * dz));
    }

    private static float GetCurveDelta(AnimationClip clip, string path, string propertyName)
    {
        var binding = new EditorCurveBinding
        {
            path = path,
            type = typeof(Transform),
            propertyName = propertyName
        };

        var curve = AnimationUtility.GetEditorCurve(clip, binding);
        if (curve == null || curve.length == 0)
            return 0f;

        return curve.Evaluate(clip.length) - curve.Evaluate(0f);
    }

    private static string FormatPath(string path)
    {
        return string.IsNullOrEmpty(path) ? "<root>" : path;
    }

    private static void EnsureParentFolder(string assetPath)
    {
        string directory = Path.GetDirectoryName(assetPath);
        if (string.IsNullOrEmpty(directory))
            return;

        if (!Directory.Exists(directory))
            Directory.CreateDirectory(directory);
    }
}
#endif

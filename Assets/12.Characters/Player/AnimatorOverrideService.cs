// ─────────────────────────────────────────────
// AnimatorOverrideService.cs
// ─────────────────────────────────────────────
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// AnimatorOverrideController 편의 서비스
/// - 이름 기반으로 안전하게 오버라이드
/// - AnimationResourceManager와 연동 가능
/// </summary>
public sealed class AnimatorOverrideService
{
    public Animator Animator { get; }
    public AnimatorOverrideController AOC { get; private set; }

    private readonly Dictionary<string, AnimationClip> _originalByName;
    private readonly Dictionary<AnimationClip, AnimationClip> _currentMap;

    public AnimatorOverrideService(Animator anim)
    {
        if (anim == null)
        {
            Debug.LogError("[AnimatorOverrideService] Animator null");
            return;
        }

        Animator = anim;
        AOC = anim.runtimeAnimatorController as AnimatorOverrideController
            ?? new AnimatorOverrideController(anim.runtimeAnimatorController);
        Animator.runtimeAnimatorController = AOC;

        _originalByName = new Dictionary<string, AnimationClip>(128);
        _currentMap = new Dictionary<AnimationClip, AnimationClip>(128);

        BuildCache();
    }

    private void BuildCache()
    {
        var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>();
        AOC.GetOverrides(pairs);

        foreach (var kv in pairs)
        {
            var original = kv.Key;
            if (original == null) continue;
            _originalByName[original.name] = original;
            _currentMap[original] = kv.Value ?? original;
        }
    }

    public bool Override(string keyName, AnimationClip newClip)
    {
        if (!_originalByName.TryGetValue(keyName, out var original)) return false;
        AOC[original] = newClip;
        _currentMap[original] = newClip;
        return true;
    }

    public void OverrideSeries(string prefix, IReadOnlyList<AnimationClip> clips)
    {
        var keys = GetKeysByPrefix(prefix);
        if (keys.Count == 0) return;

        for (int i = 0; i < keys.Count; i++)
        {
            var clip = (clips != null && i < clips.Count) ? clips[i] : null;
            if (clip != null) Override(keys[i], clip);
        }
    }

    public IReadOnlyList<string> GetKeysByPrefix(string prefix)
    {
        var list = new List<string>();
        foreach (var name in _originalByName.Keys)
            if (name.StartsWith(prefix)) list.Add(name);
        return list;
    }

    // ─────────────────────────────────────────────
    // AnimationResourceManager 연동 메서드
    // ─────────────────────────────────────────────
    public bool OverrideFromResource(string keyName)
    {
        var clip = Managers.AnimationResources.GetClip(keyName);
        if (clip == null)
        {
            Debug.LogWarning($"[AnimatorOverrideService] 리소스 '{keyName}' 없음");
            return false;
        }
        return this.Override(keyName, clip);
    }

    public void OverrideSeriesFromResource(string prefix, List<string> suffixes)
    {
        var clips = new List<AnimationClip>();
        foreach (var s in suffixes)
        {
            var clip = Managers.AnimationResources.GetClip(prefix + s);
            if (clip != null) clips.Add(clip);
        }
        this.OverrideSeries(prefix, clips);
    }
}

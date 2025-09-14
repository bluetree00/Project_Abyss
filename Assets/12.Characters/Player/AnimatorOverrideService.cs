using UnityEngine;
using System.Collections.Generic;

public sealed class AnimatorOverrideService
{
    private readonly Animator _anim;
    private readonly AnimatorOverrideController _aoc;
    private readonly Dictionary<string, AnimationClip> _originalByName;

    public AnimatorOverrideService(Animator anim)
    {
        _anim = anim;

        _aoc = (anim.runtimeAnimatorController as AnimatorOverrideController)
             ?? new AnimatorOverrideController(anim.runtimeAnimatorController);
        _anim.runtimeAnimatorController = _aoc;

        // 원본 캐시 (이름 → 원본클립)
        _originalByName = new Dictionary<string, AnimationClip>(64);
        var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>();
        _aoc.GetOverrides(pairs);
        foreach (var kv in pairs)
            _originalByName[kv.Key.name] = kv.Key;
    }

    public void Apply(Dictionary<string, AnimationClip> map)
    {
        if (map == null) return;
        foreach (var kv in map)
        {
            if (kv.Value == null) continue;
            if (_originalByName.TryGetValue(kv.Key, out var original))
                _aoc[original] = kv.Value; // O(1)
            else
                Debug.LogWarning($"[AnimOverride] 기본 클립 '{kv.Key}' 미존재");
        }
    }

    public void RevertKeys(IEnumerable<string> keys)
    {
        if (keys == null) return;
        foreach (var key in keys)
            if (_originalByName.TryGetValue(key, out var original))
                _aoc[original] = original;
    }

    public void RevertAll()
    {
        foreach (var kv in _originalByName)
            _aoc[kv.Value] = kv.Value;
    }
}

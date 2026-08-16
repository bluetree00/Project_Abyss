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
    // 무기 미적용 시점의 오버라이드 스냅샷(=CombatGirl 기본 클립). 무기 해제/교체 시 원복 대상.
    private readonly Dictionary<string, AnimationClip> _baselineByName;
    // 현재 무기가 덮어쓴 키 — 다음 무기 적용 전 이 키들만 베이스라인으로 되돌린다.
    private readonly HashSet<string> _dirtyKeys;

    public AnimatorOverrideService(Animator anim)
    {
        if (anim == null)
        {
            Debug.LogError("[AnimatorOverrideService] Animator null");
            return;
        }

        Animator = anim;

        // 중요: 공유 AOC 에셋을 그대로 쓰면 런타임 Override()가 에셋 자체를 변질시켜
        // 다음 실행의 baseline까지 오염된다(무기 클립이 기본 로코로 굳는 버그).
        // 항상 베이스 컨트롤러 위에 "새 인스턴스"를 만들고 기존 오버라이드를 복사해 사용한다.
        if (anim.runtimeAnimatorController is AnimatorOverrideController existingAoc)
        {
            AOC = new AnimatorOverrideController(existingAoc.runtimeAnimatorController);
            var seed = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            existingAoc.GetOverrides(seed);
            AOC.ApplyOverrides(seed);
        }
        else
        {
            AOC = new AnimatorOverrideController(anim.runtimeAnimatorController);
        }
        Animator.runtimeAnimatorController = AOC;

        _originalByName = new Dictionary<string, AnimationClip>(128);
        _currentMap = new Dictionary<AnimationClip, AnimationClip>(128);
        _baselineByName = new Dictionary<string, AnimationClip>(128);
        _dirtyKeys = new HashSet<string>();

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
            var baseline = kv.Value ?? original;
            _originalByName[original.name] = original;
            _currentMap[original] = baseline;
            _baselineByName[original.name] = baseline;
        }
    }

    public bool Override(string keyName, AnimationClip newClip)
    {
        if (!_originalByName.TryGetValue(keyName, out var original)) return false;
        AOC[original] = newClip;
        _currentMap[original] = newClip;
        _dirtyKeys.Add(keyName);
        return true;
    }

    /// <summary>
    /// 유물 오버라이드 — <b>무기 교체(<see cref="ResetOverrides"/>)로 되돌아가지 않는다.</b>
    /// Q 슬롯은 유물 전용이라 원복 대상(_dirtyKeys)에 넣지 않는다.
    /// </summary>
    public bool OverrideRelic(string keyName, AnimationClip newClip)
    {
        if (!_originalByName.TryGetValue(keyName, out var original)) return false;
        AOC[original] = newClip;
        _currentMap[original] = newClip;
        _dirtyKeys.Remove(keyName);   // 다른 경로가 먼저 더럽혔더라도 원복 대상에서 뺀다
        return true;
    }

    /// <summary>현재 무기가 덮어쓴 오버라이드를 베이스라인(무기 미적용 기본 클립)으로 되돌린다.
    /// 무기 교체/해제 시 이전 무기의 클립(로코모션 포함)이 남는 것을 방지.
    /// 유물 Q 키는 _dirtyKeys에 들어가지 않으므로 여기서 건드려지지 않는다.</summary>
    public void ResetOverrides()
    {
        if (_dirtyKeys.Count == 0) return;
        foreach (var keyName in _dirtyKeys)
        {
            if (!_originalByName.TryGetValue(keyName, out var original)) continue;
            var baseline = _baselineByName.TryGetValue(keyName, out var b) ? b : original;
            AOC[original] = baseline;
            _currentMap[original] = baseline;
        }
        _dirtyKeys.Clear();
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

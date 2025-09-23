using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// AnimatorOverrideController(AOC) 편의 헬퍼.
/// - 기본 컨트롤러의 원본 AnimationClip을 캐시해두고, 이름 기준으로 안전하게 오버라이드/되돌리기를 제공.
/// - 무기 교체 등으로 베이스 컨트롤러가 바뀌어도 Rebind(ResetBaseController)로 재초기화 가능.
/// </summary>
public sealed class AnimatorOverrideService
{
    public Animator Animator        { get; }
    public AnimatorOverrideController AOC { get; private set; }

    // 기본 컨트롤러의 "키 이름 → 원본 클립" 캐시
    private readonly Dictionary<string, AnimationClip> _originalByName;

    // AOC 인덱서에 바로 쓸 수 있도록 "원본 클립 → 현재 클립" 매핑도 유지(필요 시)
    private readonly Dictionary<AnimationClip, AnimationClip> _currentMap;

    /// <summary>
    /// anim.runtimeAnimatorController 를 AOC로 교체(없으면 생성)하고
    /// 원본 클립 캐시를 만든다.
    /// </summary>
    public AnimatorOverrideService(Animator anim)
    {
        if (anim == null)
        {
            Debug.LogError("[AnimOverrideService] Animator가 null입니다.");
            return;
        }

        Animator = anim;
        AOC = (anim.runtimeAnimatorController as AnimatorOverrideController)
           ?? new AnimatorOverrideController(anim.runtimeAnimatorController);
        Animator.runtimeAnimatorController = AOC;

        _originalByName = new Dictionary<string, AnimationClip>(128);
        _currentMap     = new Dictionary<AnimationClip, AnimationClip>(128);

        BuildCache();
    }

    /// <summary>
    /// 베이스(RuntimeAnimatorController)가 바뀌었을 때 호출(무기 교체 등).
    /// 내부 캐시/매핑을 모두 재구성한다.
    /// </summary>
    public void ResetBaseController(RuntimeAnimatorController newBase)
    {
        if (Animator == null) return;
        if (newBase == null)
        {
            Debug.LogError("[AnimOverrideService] ResetBaseController: newBase가 null입니다.");
            return;
        }

        AOC = (newBase as AnimatorOverrideController) ?? new AnimatorOverrideController(newBase);
        Animator.runtimeAnimatorController = AOC;

        _originalByName.Clear();
        _currentMap.Clear();
        BuildCache();
    }

    /// <summary>
    /// 현재 AOC의 원본 클립 목록을 읽어 캐시를 구성한다.
    /// </summary>
    private void BuildCache()
    {
        var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>();
        AOC.GetOverrides(pairs);

        foreach (var kv in pairs)
        {
            var original = kv.Key;
            if (original == null) continue;

            _originalByName[original.name] = original;
            // 현재 매핑도 최신화(초기에는 원본=원본)
            _currentMap[original] = kv.Value ?? original;
        }
    }

    // ─────────────────────────────────────────────
    // 오버라이드 API
    // ─────────────────────────────────────────────

    /// <summary>
    /// 특정 키 이름(기본 클립명)에 대해 새 클립으로 오버라이드.
    /// </summary>
    public bool Override(string keyName, AnimationClip newClip)
    {
        if (AOC == null) return false;
        if (string.IsNullOrEmpty(keyName) || newClip == null) return false;

        if (!_originalByName.TryGetValue(keyName, out var original))
        {
            Debug.LogWarning($"[AnimOverride] 기본 클립 '{keyName}' 미존재");
            return false;
        }

        AOC[original] = newClip;
        _currentMap[original] = newClip;
        return true;
    }

    /// <summary>
    /// 여러 키를 한 번에 오버라이드(이름 → 새 클립).
    /// 존재하지 않는 키는 경고 후 건너뜀.
    /// </summary>
    public void OverrideMap(IReadOnlyDictionary<string, AnimationClip> map)
    {
        if (AOC == null || map == null || map.Count == 0) return;

        foreach (var (key, clip) in map)
        {
            if (clip == null) continue;
            Override(key, clip);
        }
    }

    /// <summary>
    /// 접두사(prefix)가 같은 기본 클립들을 순서대로 오버라이드.
    /// 예) "NormalAttack_" + 1..N
    /// </summary>
    /// <param name="prefix">기본 클립명 접두사</param>
    /// <param name="clips">새 클립 리스트(부족하면 남은 키는 원본 유지)</param>
    /// <param name="requireExactCount">true면 개수 불일치 시 경고</param>
    public void OverrideSeries(string prefix, IReadOnlyList<AnimationClip> clips, bool requireExactCount = false)
    {
        if (AOC == null || string.IsNullOrEmpty(prefix)) return;

        var keys = GetKeysByPrefix(prefix);
        if (keys.Count == 0) { Debug.LogWarning($"[AnimOverride] '{prefix}' 접두사 기본 클립이 없습니다."); return; }

        if (requireExactCount && (clips == null || clips.Count != keys.Count))
            Debug.LogWarning($"[AnimOverride] '{prefix}' 개수 불일치: base={keys.Count}, new={(clips?.Count ?? 0)}");

        for (int i = 0; i < keys.Count; i++)
        {
            var k = keys[i];
            var clip = (clips != null && i < clips.Count) ? clips[i] : null;
            if (clip != null) Override(k, clip);
        }
    }

    // ─────────────────────────────────────────────
    // 되돌리기 API
    // ─────────────────────────────────────────────

    public void RevertKey(string keyName)
    {
        if (AOC == null || string.IsNullOrEmpty(keyName)) return;

        if (_originalByName.TryGetValue(keyName, out var original))
        {
            AOC[original] = original;
            _currentMap[original] = original;
        }
    }

    public void RevertKeys(IEnumerable<string> keys)
    {
        if (keys == null) return;
        foreach (var k in keys) RevertKey(k);
    }

    public void RevertPrefix(string prefix)
    {
        var keys = GetKeysByPrefix(prefix);
        if (keys.Count == 0) return;
        foreach (var k in keys) RevertKey(k);
    }

    public void RevertAll()
    {
        if (AOC == null) return;

        foreach (var kv in _originalByName)
        {
            var original = kv.Value;
            AOC[original] = original;
            _currentMap[original] = original;
        }
    }

    // ─────────────────────────────────────────────
    // 조회/유틸
    // ─────────────────────────────────────────────

    public bool HasKey(string keyName) => _originalByName.ContainsKey(keyName);

    public IReadOnlyList<string> GetKeysByPrefix(string prefix)
    {
        if (string.IsNullOrEmpty(prefix)) return System.Array.Empty<string>();
        // 이름에 접두사만 일치하면 당겨옴 + 숫자 정렬 시도(뒤에 1,2,3…이 붙는 패턴을 대비)
        var list = new List<string>();
        foreach (var name in _originalByName.Keys)
            if (name.StartsWith(prefix)) list.Add(name);

        list.Sort((a, b) =>
        {
            // "Prefix_12" → 12 추출 시도
            int ParseTail(string s)
            {
                int i = s.Length - 1;
                while (i >= 0 && char.IsDigit(s[i])) i--;
                return int.TryParse(s.Substring(i + 1), out var num) ? num : int.MaxValue;
            }
            return ParseTail(a).CompareTo(ParseTail(b));
        });

        return list;
    }

    public AnimationClip GetOriginalClip(string keyName)
        => _originalByName.TryGetValue(keyName, out var original) ? original : null;

    public AnimationClip GetCurrentClip(string keyName)
    {
        if (!_originalByName.TryGetValue(keyName, out var original)) return null;
        return _currentMap.TryGetValue(original, out var current) ? current : original;
    }
}

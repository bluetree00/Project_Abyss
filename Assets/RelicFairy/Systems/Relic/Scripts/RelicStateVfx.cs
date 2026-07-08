using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 유물 상태 피드백 VFX 관리자(활성/비활성 토글 구조). 플레이어에 부착.
/// 상태별(정오/광란 등) 루프 VFX를 Addressable로 자식 스폰·캐시하고 SetActive로 켜고 끈다.
/// 일회성 히트/스킬 VFX는 PlayOneShot. 키 미설정·에셋 부재 시 무해 no-op(그레이스풀).
/// 프리팹 키는 유물이 상수로 등록(Register) — 에셋 배선 후 채움.
/// </summary>
public sealed class RelicStateVfx : MonoBehaviour
{
    private readonly Dictionary<string, string>     _keys      = new();  // stateKey → addressable key
    private readonly Dictionary<string, GameObject> _instances = new();  // stateKey → 스폰 인스턴스
    private readonly Dictionary<string, bool>       _desired   = new();  // 로드 레이스 대비 최종 원하는 상태
    private readonly HashSet<string>                _loading   = new();

    /// <summary>상태 키 ↔ Addressable 프리팹 키 등록(빈 값은 무시).</summary>
    public void Register(string stateKey, string addressableKey)
    {
        if (!string.IsNullOrEmpty(stateKey) && !string.IsNullOrEmpty(addressableKey))
            _keys[stateKey] = addressableKey;
    }

    /// <summary>상태 VFX 활성/비활성. 최초 활성 시 비동기 스폰, 이후는 SetActive 토글.</summary>
    public void SetActive(string stateKey, bool on)
    {
        _desired[stateKey] = on;
        if (_instances.TryGetValue(stateKey, out var go) && go != null)
        {
            if (go.activeSelf != on) go.SetActive(on);
            return;
        }
        if (on) EnsureAsync(stateKey).Forget();
    }

    /// <summary>일회성 VFX(히트/착탄) — 지정 위치에 스폰 후 자동 정리.</summary>
    public static void PlayOneShot(string addressableKey, Vector3 pos)
    {
        if (!string.IsNullOrEmpty(addressableKey)) PlayOneShotAsync(addressableKey, pos).Forget();
    }

    private async UniTaskVoid EnsureAsync(string stateKey)
    {
        if (_loading.Contains(stateKey) || !_keys.TryGetValue(stateKey, out var key)) return;
        _loading.Add(stateKey);
        GameObject go = null;
        try { go = await Managers.AddressableManager.InstantiateAsync(key, transform); }
        catch (Exception) { /* 키 부재 등 — 무해 */ }
        finally { _loading.Remove(stateKey); }

        if (this == null) { if (go != null) Destroy(go); return; }
        if (go == null) return;
        _instances[stateKey] = go;
        go.SetActive(_desired.TryGetValue(stateKey, out var d) && d); // 로드 중 토글 반영
    }

    private static async UniTaskVoid PlayOneShotAsync(string key, Vector3 pos)
    {
        GameObject go = null;
        try { go = await Managers.AddressableManager.InstantiateAsync(key, null, true); }
        catch (Exception) { return; }
        if (go == null) return;
        go.transform.position = pos;
        Destroy(go, 3f);
    }

    private void OnDestroy()
    {
        foreach (var kv in _instances) if (kv.Value != null) Destroy(kv.Value);
        _instances.Clear();
    }
}

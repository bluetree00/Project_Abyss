using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.VFX;
using INab.CommonVFX;

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
            ApplyPlayState(go, on);
            return;
        }
        if (on) EnsureAsync(stateKey).Forget();
    }

    /// <summary>
    /// 일회성 VFX(히트/착탄) — 지정 위치에 스폰 후 자동 정리.
    /// scale은 프리팹 원본 크기에 곱하는 배율(1이면 원본). 이펙트가 실제 판정 범위보다 커 보이면 여기서 줄인다.
    /// forward를 주면 그 방향을 바라보게 회전(전방 참격 등). Vector3.zero면 회전 없음.
    /// </summary>
    /// <param name="prewarm">파티클을 이 시간만큼 미리 진행시킨 상태로 켠다(초). 지연 버스트가 있는 이펙트의 클라이맥스를 앞당길 때.</param>
    public static void PlayOneShot(string addressableKey, Vector3 pos, float scale = 1f, Vector3 forward = default, float prewarm = 0f)
    {
        if (!string.IsNullOrEmpty(addressableKey))
            PlayOneShotAsync(addressableKey, pos, scale, forward, prewarm).Forget();
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

        BindToHost(go);
        _instances[stateKey] = go;

        bool on = _desired.TryGetValue(stateKey, out var d) && d;
        go.SetActive(on);          // 로드 중 토글 반영
        ApplyPlayState(go, on);
    }

    /// <summary>
    /// VFX Graph 오라를 실제로 재생/정지시킨다.
    ///
    /// INab Character Effects는 <b>초기 이벤트가 비어 있게(m_InitialEventName 공란) 저작</b>돼 있다
    /// — 즉 GameObject를 켜는 것만으로는 입자가 한 톨도 안 나오고, 스크립트가 Play()를 불러줘야 한다.
    /// (에셋은 멀쩡한데 "아무것도 안 보이는" 상태의 원인.)
    /// ParticleSystem 기반 이펙트에는 VisualEffect가 없으므로 이 호출은 그냥 지나간다.
    /// </summary>
    private static void ApplyPlayState(GameObject go, bool on)
    {
        var vfx = go.GetComponentsInChildren<VisualEffect>(true);
        for (int i = 0; i < vfx.Length; i++)
        {
            if (on) vfx[i].Play();
            else    vfx[i].Stop();
        }
    }

    /// <summary>
    /// VFX Graph 기반 오라(INab Character Effects 등)를 <b>몸에 감기게</b> 만든다.
    ///
    /// 이들 이펙트는 캐릭터를 감싸는 볼륨을 VFXLossyTransformBinder로 그래프에 먹인다.
    /// 그런데 프리팹의 Target은 비어 있고(런타임에만 알 수 있으므로 저작 불가),
    /// Target이 null이면 IsValid가 false라 바인딩이 아예 안 돌아 이펙트가 월드 원점에 뜬다
    /// — "붙긴 붙었는데 몸에 안 감기는" 상태의 원인. 여기서 호스트(플레이어)를 물려준다.
    /// </summary>
    private void BindToHost(GameObject go)
    {
        var binders = go.GetComponentsInChildren<VFXLossyTransformBinder>(true);
        for (int i = 0; i < binders.Length; i++)
            if (binders[i].Target == null) binders[i].Target = transform;
    }

    private static async UniTaskVoid PlayOneShotAsync(string key, Vector3 pos, float scale, Vector3 forward, float prewarm)
    {
        GameObject go = null;
        try { go = await Managers.AddressableManager.InstantiateAsync(key, null, true); }
        catch (Exception) { return; }
        if (go == null) return;

        var t = go.transform;
        t.position = pos;

        // 프리팹 원본 스케일에 배율을 곱한다(원본을 훼손하지 않고 크기만 조절).
        if (scale > 0f && !Mathf.Approximately(scale, 1f))
            t.localScale = Vector3.Scale(t.localScale, Vector3.one * scale);

        if (forward.sqrMagnitude > 0.0001f)
            t.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);

        if (prewarm > 0f)
        {
            // 시스템마다 따로 돌린다 — 자식까지 한 번에 돌리면 각자의 startDelay가 뭉개진다.
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                ps.Simulate(prewarm, false, true);
                ps.Play(false);
            }
        }

        Destroy(go, 3f);
    }

    private void OnDestroy()
    {
        foreach (var kv in _instances) if (kv.Value != null) Destroy(kv.Value);
        _instances.Clear();
    }
}

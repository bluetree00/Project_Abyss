using System;
using System.Collections.Generic;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 피라미드 슬래시 패턴에서 좌우에 소환되는 공중 부유 검.
/// IDamageable 구현으로 플레이어의 공격을 받으며, 파괴 시 콜백을 발동한다.
/// </summary>
public class DKFloatingSword : MonoBehaviour, IDamageable
{
    private const float DismissSeconds = 0.5f;   // 부서짐 · 패턴 끝 — 디졸브로 거두고 풀에 돌려준다

    // ── 상태 ─────────────────────────────────────────────────
    public bool IsSameColorAsDK { get; private set; }

    private float                 _hp;
    private float                 _bobAmplitude;
    private float                 _bobSpeed;
    private float                 _bobTimer;
    private Vector3               _basePosition;
    private Action<bool, Vector3> _onDestroyed;
    private bool                  _destroyed;
    private bool                  _dismissing;
    private readonly List<Collider> _offColliders = new();

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 초기화
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    /// <param name="isSameColorAsDK">DK 검 색상과 동일하면 true.</param>
    /// <param name="onDestroyed">파괴 시 콜백 (isSameColor, 기준 위치).</param>
    public void Initialize(bool isSameColorAsDK, float hp,
                           float bobAmplitude, float bobSpeed,
                           Action<bool, Vector3> onDestroyed)
    {
        IsSameColorAsDK = isSameColorAsDK;
        _hp             = hp;
        _bobAmplitude   = bobAmplitude;
        _bobSpeed       = bobSpeed;
        _onDestroyed    = onDestroyed;
        _basePosition   = transform.position;
        _bobTimer       = UnityEngine.Random.Range(0f, Mathf.PI * 2f); // 두 검 위상 차이
        _destroyed      = false;
        _dismissing     = false;
        foreach (var c in _offColliders) if (c != null) c.enabled = true;   // 풀 재사용 — 거둘 때 끈 판정을 되살린다
        _offColliders.Clear();
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // Lifecycle
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private void Update()
    {
        _bobTimer += Time.deltaTime * _bobSpeed;
        transform.position = _basePosition + Vector3.up * (Mathf.Sin(_bobTimer) * _bobAmplitude);
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // IDamageable
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    public void TakeDamage(float amount, UnityEngine.GameObject instigator,
                           float knockbackMultiplier = 1f,
                           bool isCrit = false)
    {
        if (_destroyed) return;
        _hp -= amount;
        if (_hp > 0f) return;

        _destroyed = true;
        _onDestroyed?.Invoke(IsSameColorAsDK, _basePosition);
        Dismiss();
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 거두기
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    /// <summary>디졸브로 거두고 풀에 돌려준다 — 부서질 때 · 패턴이 끝날 때(10-12 디졸브 원칙 — 예전엔 바로 사라졌다). 그동안 맞지 않는다.</summary>
    public void Dismiss()
    {
        if (_dismissing) return;
        _dismissing = true;
        _destroyed  = true;
        foreach (var c in GetComponents<Collider>())
            if (c.enabled) { c.enabled = false; _offColliders.Add(c); }
        var go = gameObject;
        DissolveEffect.PlayDisappear(go, DismissSeconds, () => { if (this != null && _dismissing) BossEffectPool.Release(go); });
    }
}
}

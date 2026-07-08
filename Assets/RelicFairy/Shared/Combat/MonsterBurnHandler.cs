using UnityEngine;

/// <summary>
/// 몬스터에 부착되는 화상 DoT 핸들러.
/// 정해진 간격마다 IDamageable.TakeDamage 를 호출해 지속 피해를 가한다.
/// 같은 대상에 다시 적용되면 더 강한 쪽 DPS 유지 + 지속시간 갱신.
/// </summary>
[DisallowMultipleComponent]
public sealed class MonsterBurnHandler : MonoBehaviour
{
    // ── Private ───────────────────────────────────────────────
    private IDamageable _target;
    private GameObject  _instigator;
    private float       _dps;
    private float       _tickInterval;
    private float       _remaining;
    private float       _tickAccum;

    // ── Public API ────────────────────────────────────────────
    /// <summary>대상 GameObject 에 화상을 적용한다. 컴포넌트가 없으면 추가, 있으면 갱신.</summary>
    public static void Apply(GameObject target, float dps, float duration, float tickInterval, GameObject instigator)
    {
        if (target == null || dps <= 0f || duration <= 0f || tickInterval <= 0f) return;
        if (!target.TryGetComponent<IDamageable>(out var dmg)) return;

        // [가이드라인 비주얼] 화상 마커(점화색 재사용) — 모든 화상 사용처 공통 단일 지점
        GuidelineVisual.StatusApplied(target.transform, "burn", duration);

        if (!target.TryGetComponent<MonsterBurnHandler>(out var h))
            h = target.AddComponent<MonsterBurnHandler>();

        h.Configure(dmg, instigator, dps, tickInterval, duration);
    }

    /// <summary>대상의 화상을 즉시 폭발(잔여 총량을 1회 피해로) — 가웨인 정오 즉발.</summary>
    public static void DetonateOn(GameObject target)
    {
        if (target != null && target.TryGetComponent<MonsterBurnHandler>(out var h)) h.Detonate();
    }

    /// <summary>남은 화상 총량(dps × 잔여시간)을 즉시 피해로 가하고 소멸.</summary>
    public void Detonate()
    {
        if (_target != null)
        {
            float burst = _dps * Mathf.Max(0f, _remaining);
            if (burst > 0f) _target.TakeDamage(burst, _instigator);
        }
        Destroy(this);
    }

    // ── Lifecycle ─────────────────────────────────────────────
    private void Update()
    {
        if (_target == null) { Destroy(this); return; }

        _remaining -= Time.deltaTime;
        _tickAccum += Time.deltaTime;

        if (_tickAccum >= _tickInterval)
        {
            float damage = _dps * _tickInterval;
            _target.TakeDamage(damage, _instigator);
            // 팝업은 대상측(MonsterBase.TakeDamage) 자체 처리
            _tickAccum -= _tickInterval;
        }

        if (_remaining <= 0f)
            Destroy(this);
    }

    // ── Private Methods ───────────────────────────────────────
    private void Configure(IDamageable target, GameObject instigator, float dps, float tickInterval, float duration)
    {
        _target       = target;
        _instigator   = instigator;
        _tickInterval = tickInterval;
        _dps          = Mathf.Max(_dps, dps);    // 더 강한 DPS 유지
        _remaining    = Mathf.Max(_remaining, duration); // 더 긴 지속시간 유지
    }
}

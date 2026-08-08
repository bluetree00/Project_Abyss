using UnityEngine;
using RelicFairy.Monster;

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
    private MonsterBase _monster;     // DoT 경로(TakeSynergyDamage)용 — 넉백·GetHit 없이 피해만
    private GameObject  _instigator;
    private float       _dps;
    private float       _tickInterval;
    private float       _remaining;
    private float       _total;        // 게이지 비율용(부여된 최대 지속)
    private float       _tickAccum;

    // ── Properties ────────────────────────────────────────────
    /// <summary>남은 화상 시간(초). 디버프 UI가 읽는다.</summary>
    public float Remaining => Mathf.Max(0f, _remaining);
    /// <summary>남은 비율(0~1). 게이지용.</summary>
    public float Remaining01 => _total > 0f ? Mathf.Clamp01(_remaining / _total) : 0f;

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
            if (burst > 0f) DealDot(burst);
        }
        Destroy(this);
    }

    /// <summary>대상 화상의 fraction(0~1)만큼을 즉시 피해로 터뜨리고, 그 비율만큼 잔여를 줄인다(나머지는 계속 탄다). — 가웨인 '불사르기'.</summary>
    public static void DetonateOn(GameObject target, float fraction)
    {
        if (target != null && target.TryGetComponent<MonsterBurnHandler>(out var h)) h.Detonate(fraction);
    }

    /// <summary>남은 화상의 fraction 비율을 즉시 피해로 가하고, 그만큼 잔여시간을 소진. 전부 소진되면 소멸.</summary>
    public void Detonate(float fraction)
    {
        fraction = Mathf.Clamp01(fraction);
        if (_target == null || fraction <= 0f || _remaining <= 0f) return;

        float burst = _dps * _remaining * fraction;
        if (burst > 0f) DealDot(burst);

        _remaining -= _remaining * fraction;   // 터뜨린 비율만큼 잔여 소진
        if (_remaining <= 0f) Destroy(this);
    }

    /// <summary>
    /// 풀 반환 시 화상을 즉시 무해화한다. Destroy 는 프레임 끝에야 실제로 제거되므로,
    /// 그 사이에 Update 가 한 번 더 돌거나 같은 프레임에 몬스터가 재사용되면 남은 화상이 그대로 이어져
    /// 스폰하자마자 피해가 들어간다. 상태를 먼저 지워 그 창을 없애고 나서 컴포넌트를 제거한다.
    /// (_target 을 비우면 Update 는 피해 없이 곧바로 빠져나간다.)
    /// </summary>
    public void CancelForPooling()
    {
        _target     = null;
        _monster    = null;
        _instigator = null;
        _dps        = 0f;
        _remaining  = 0f;
        _total      = 0f;
        _tickAccum  = 0f;
        Destroy(this);
    }

    /// <summary>from의 화상을 to에게 그대로 옮긴다(전염) — dps·잔여시간·간격 복사. from에 화상이 없으면 무시. 가웨인 '화상 전염/재앙'.</summary>
    public static void SpreadTo(GameObject from, GameObject to, GameObject instigator)
    {
        if (from == null || to == null) return;
        if (!from.TryGetComponent<MonsterBurnHandler>(out var src) || src._remaining <= 0f) return;
        Apply(to, src._dps, src._remaining, src._tickInterval, instigator);
    }

    /// <summary>대상 화상의 잔여시간을 addSeconds만큼 연장(중첩 근사). 화상이 없으면 무시. 가웨인 '심판의 낙인' 보스 분기.</summary>
    public static void ExtendOn(GameObject target, float addSeconds)
    {
        if (target == null || addSeconds <= 0f) return;
        if (!target.TryGetComponent<MonsterBurnHandler>(out var h) || h._remaining <= 0f) return;
        h._remaining += addSeconds;
        h._total = Mathf.Max(h._total, h._remaining);
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
            DealDot(damage);
            // 팝업은 대상측(TakeSynergyDamage) 자체 처리
            _tickAccum -= _tickInterval;
        }

        if (_remaining <= 0f)
            Destroy(this);
    }

    // ── Private Methods ───────────────────────────────────────
    /// <summary>화상은 DoT다 — 넉백·GetHit 없는 시너지 경로로 피해만 가한다(방어 그대로 적용).</summary>
    private void DealDot(float amount)
    {
        if (_monster != null) _monster.TakeSynergyDamage(amount, _instigator, 0f, false, DamageKind.Dot, RuneElement.Fire);
        else                  _target.TakeDamage(amount, _instigator, 0f);   // 몬스터가 아닌 대상 폴백
    }

    private void Configure(IDamageable target, GameObject instigator, float dps, float tickInterval, float duration)
    {
        _target       = target;
        _monster      = target as MonsterBase;
        _instigator   = instigator;
        _tickInterval = tickInterval;
        _dps          = Mathf.Max(_dps, dps);    // 더 강한 DPS 유지
        _remaining    = Mathf.Max(_remaining, duration); // 더 긴 지속시간 유지
        _total        = Mathf.Max(_total, _remaining);   // 게이지 기준(부여 직후 = 100%)
    }
}

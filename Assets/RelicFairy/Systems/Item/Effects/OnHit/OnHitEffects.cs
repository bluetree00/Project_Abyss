using UnityEngine;
using UnityEngine.AI;

// ═══════════════════════════════════════════════════════════
// 공격 적중 시 발동하는 효과
// ═══════════════════════════════════════════════════════════

/// <summary>
/// 적중 시 확률로 독(DoT) 부여 — 공격력 비율 기반(설계 ③ A안).
///  value=발동확률, value2=틱당 피해비율(EffAtk×value2), value3=틱간격, duration=총지속.
/// MonsterStatusReceiver.ApplyDot로 흡수(방어무시) — 룬 점화/독과 동일 경로. 가이드라인 마커 자동.
/// </summary>
public sealed class PoisonOnHitEffect : ItemEffectBase
{
    public PoisonOnHitEffect(ItemEffectSlot s) : base(s) { }

    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (Random.value >= _value) return;
        if (report.Target == null) return;

        var mb = report.Target.GetComponentInParent<RelicFairy.Monster.MonsterBase>();
        if (mb == null) return;

        int atk = ctx.Stats != null ? ctx.Stats.GetEffectiveAttack(ctx.WeaponType.GetAttackStatKind()) : 0;
        float dmgPerTick = Mathf.Max(1f, atk * Mathf.Max(0f, _value2));
        float interval   = _value3 > 0f ? _value3 : 1f;
        int   ticks      = Mathf.Max(1, Mathf.RoundToInt((_duration > 0f ? _duration : interval) / interval));

        // ApplyDot 내부에서 가이드라인 상태 마커(item_poison → 독) 자동 표시.
        mb.Status.ApplyDot("item_poison", dmgPerTick, interval, ticks,
                           ctx.Player != null ? ctx.Player.gameObject : null);

        // [정리] 레거시 원소 프리팹(VFX_Poison) 기본키 제거 — 상태 표시는 속성 VFX(StatusApplied→독 몸 이펙트)가
        // 단독 담당한다. 둘 다 두면 같은 타격에 이펙트가 겹쳤다. 데이터로 키를 명시한 경우에만 추가 연출.
        var vfx = ResolveVfxKey(null);
        if (!string.IsNullOrEmpty(vfx)) ItemEffectVfxHelper.SpawnOneShotAt(vfx, report.Target.transform.position);
    }
}

public sealed class FreezeEffect : ItemEffectBase
{
    public FreezeEffect(ItemEffectSlot s) : base(s) { }

    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (Random.value >= _value) return;
        if (report.Target == null) return;

        // [정리] 레거시 기본키(VFX_Wet=물 원소 재활용) 제거 — 빙결 표시는 속성 VFX(얼음 몸 이펙트)가 담당.
        var vfx = ResolveVfxKey(null);
        if (!string.IsNullOrEmpty(vfx)) ItemEffectVfxHelper.SpawnOneShotAt(vfx, report.Target.transform.position);

        // 상태이상 통합 수신기로 흡수 — 빙결 = CC(이동·FSM 정지).
        // 지속은 duration(초) 필드를 사용. 구버전 데이터(지속을 max_stack에 넣은 경우) 하위호환 폴백.
        var mb = report.Target.GetComponentInParent<RelicFairy.Monster.MonsterBase>();
        if (mb != null)
        {
            float dur = _duration > 0f ? _duration : (_maxStack > 0 ? _maxStack : 2f);
            mb.Status.ApplyCc("freeze", dur);
        }
    }
}

public sealed class ExtraAttackEffect : ItemEffectBase
{
    private const string DefaultVfxKey = "VFX_ExtraAttack";

    public ExtraAttackEffect(ItemEffectSlot s) : base(s) { }

    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (Random.value >= _value) return;
        if (report.Target == null || ctx.Player == null) return;

        if (report.Target.TryGetComponent<IDamageable>(out var damageable))
        {
            damageable.TakeDamage(report.DamageDealt, ctx.Player.gameObject, knockbackMultiplier: 0f);

            var hitPos = report.HitPosition != Vector3.zero
                ? report.HitPosition
                : report.Target.transform.position;
            ItemEffectVfxHelper.SpawnOneShotAt(ResolveVfxKey(DefaultVfxKey), hitPos);
            ItemEffectVfxHelper.ShowNotice($"<color=#FFDD55>추가 타격</color> {report.DamageDealt:F0} → {report.Target.name}");
            Debug.Log($"[ExtraAttack] 추가 타격 {report.DamageDealt:F0} → {report.Target.name}");
        }
    }
}

public sealed class TeleportSwapEffect : ItemEffectBase
{
    private const float SampleRadius = 1.5f;

    public TeleportSwapEffect(ItemEffectSlot s) : base(s) { }

    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (Random.value >= _value) return;
        if (ctx.Player == null || report.Target == null) return;

        var playerPos = ctx.Player.transform.position;
        var targetPos = report.Target.transform.position;

        // NavMesh 검증 — 두 착지점 모두 NavMesh 위로 스냅 가능할 때만 교체(벽끼임/낙사 방지).
        if (!NavMesh.SamplePosition(targetPos, out var playerHit, SampleRadius, NavMesh.AllAreas)) return;
        if (!NavMesh.SamplePosition(playerPos, out var targetHit, SampleRadius, NavMesh.AllAreas)) return;

        ctx.Player.transform.position = playerHit.position;
        report.Target.transform.position = targetHit.position;
        Debug.Log("[TeleportSwap] 위치 교체!");
    }
}

public sealed class HPRegenOnHitEffect : ItemEffectBase
{
    // 다단 히트로 매 타격마다 쌓이는 것 방지 — 내부 쿨다운(value2초, 기본 0.5s).
    private const float DefaultCooldown = 0.5f;
    private float _cooldownEnd;

    public HPRegenOnHitEffect(ItemEffectSlot s) : base(s) { }

    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (ctx.Player == null) return;
        if (Time.time < _cooldownEnd) return;

        _cooldownEnd = Time.time + (_value2 > 0f ? _value2 : DefaultCooldown);

        // 적중 시 <b>회복</b>이었다 — 적을 때려 HP를 얻는 전형적인 흡혈이라 정책상 둘 수 없다.
        // 흡수량은 그대로 두고 보호막으로 바꾼다(적에게서 가져오는 것 없음).
        float amount = Mathf.Max(1f, _value);
        ctx.Player.RuntimeStats.AddShield(amount);
        ItemGuide.Toast(ctx.Player.transform.position, $"보호막 +{(int)amount}");
    }
}

public sealed class PetrifyEffect : ItemEffectBase
{
    // ⚠️ 여기만 레거시 원소 프리팹을 그대로 둔다(독/빙결/기절과 달리 제거 금지).
    // 석화는 6속성(불·얼음·번개·독·빛·어둠) 어디에도 속하지 않아 속성 VFX가 대신 표시해 주지 못한다.
    // 이걸 지우면 석화가 완전 무표시가 된다. 속성 체계에 석화가 편입되면 그때 정리할 것.
    private const string DefaultVfxKey = "VFX_Petrify";

    public PetrifyEffect(ItemEffectSlot s) : base(s) { }

    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (Random.value >= _value) return;
        if (report.Target == null) return;

        ItemEffectVfxHelper.SpawnOneShotAt(ResolveVfxKey(DefaultVfxKey), report.Target.transform.position);

        // 상태이상 통합 수신기로 흡수 — 석화 = CC(완전 무력화). 지속은 _duration 재사용.
        var mb = report.Target.GetComponentInParent<RelicFairy.Monster.MonsterBase>();
        if (mb != null) mb.Status.ApplyCc("petrify", _duration > 0f ? _duration : 1.5f);
    }
}

public sealed class StunEffect : ItemEffectBase
{
    public StunEffect(ItemEffectSlot s) : base(s) { }

    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (Random.value >= _value) return;
        if (report.Target == null) return;

        // [정리] 레거시 기본키(VFX_Shock=번개 원소 재활용) 제거 — 기절 표시는 속성 VFX(전기)가 담당.
        var vfx = ResolveVfxKey(null);
        if (!string.IsNullOrEmpty(vfx)) ItemEffectVfxHelper.SpawnOneShotAt(vfx, report.Target.transform.position);

        // 상태이상 시스템(MonsterBase.ApplyStun)과 통합 — 룬 감전과 동일 경로.
        var mb = report.Target.GetComponentInParent<RelicFairy.Monster.MonsterBase>();
        if (mb != null) mb.ApplyStun(_duration > 0f ? _duration : 1f);
    }
}

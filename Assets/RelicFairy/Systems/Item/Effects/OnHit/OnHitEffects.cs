using UnityEngine;
using UnityEngine.AI;

// ═══════════════════════════════════════════════════════════
// 공격 적중 시 발동하는 효과
// 확률 · 상태 지속은 그 적중의 발동 계수(ProcCoefficient)를 곱한다 — 카타나 평타 0.7 · 대검 1.6 · 2차 피해 0.2(10-14 A1).
// ═══════════════════════════════════════════════════════════

/// <summary>적중 효과 공용 — 발동 계수 · 룬 CC 재적용 간격.</summary>
internal static class OnHitProc
{
    private const string CcKey = "item_cc", CcShownKey = "item_cc_shown";
    private const float  CcGap = 2f;

    /// <summary>이 적중의 발동 계수(옛 경로 · 계수 없는 보고는 1).</summary>
    public static float Coef(in DamageReport r) => r.ProcCoef > 0f ? r.ProcCoef : 1f;

    /// <summary>확률 × 계수로 굴린다(상한 100%).</summary>
    public static bool Roll(float chance, in DamageReport r) => Random.value < Mathf.Min(1f, chance * Coef(r));

    /// <summary>
    /// 룬 CC(빙결 · 기절 · 석화) 같은 대상 재적용 간격 — CC가 끝난 뒤 2초는 다시 안 걸린다(10-14 K7).
    /// 예전엔 간격이 없어 공속 + 각인이면 계속 묶어 뒀다. 막힌 동안 「면역」은 그 창에 한 번만 보인다.
    /// </summary>
    public static bool TryGateCc(RelicFairy.Monster.MonsterBase mb, float duration)
    {
        if (mb.Status.IsOnCooldown(CcKey))
        {
            if (!mb.Status.IsOnCooldown(CcShownKey))
            {
                mb.Status.SetCooldown(CcShownKey, CcGap);
                ItemGuide.Toast(mb.transform.position, "면역");
            }
            return false;
        }
        mb.Status.SetCooldown(CcKey, duration + CcGap);
        return true;
    }
}

/// <summary>
/// 적중 시 확률로 독 · 화상(DoT) 부여.
///  value=발동확률, value2=틱당 피해비율, value3=틱간격, duration=총지속, max_stack=독 최대 중첩(0이면 8).
///  • 독(불 아닌 룬) — 공격력 × value2를 중첩당으로 · 걸 때마다 +1중첩(최대 8) — 연타형(10-14 A3).
///  • 화상(불 룬) — 그 타격 피해 × value2 · 가장 센 1개만 남음 — 한 방형(10-14 A3).
/// MonsterStatusReceiver 중첩 · 가장 센 DoT로 흡수(방어무시). 가이드라인 마커 자동.
/// </summary>
public sealed class PoisonOnHitEffect : ItemEffectBase
{
    private string _statusId = "item_poison";

    public PoisonOnHitEffect(ItemEffectSlot s) : base(s) { }

    /// <summary>
    /// 이 룬의 속성 — 불 속성 룬(「스며드는 화염의 룬」, 설명 「화상」)은 같은 지속 피해를 화상(item_burn)으로 건다.
    /// 예전엔 늘 독(item_poison)이라 초록 독 몸 이펙트 · 「중독」 아이콘이 떴다(10-11 이펙트 톤 2차 D-4). 피해 · 틱은 같다.
    /// </summary>
    public void SetElement(string elementId) => _statusId = elementId == "FIRE" ? "item_burn" : "item_poison";

    /// <summary>
    /// 「OnCrit」 · 「AfterHit」은 공통 판정표에 없어 꺼져 있었다(10-15 점검 — 스며드는 · 되받아치는 화염의 룬 화상이 한 번도 안 걸렸다).
    /// 스며드는 화염은 설계 · 설명이 「적중 시 화상」이라 적중으로, 되받아치는 화염은 아래 「맞은 뒤 창」이 거른다.
    /// </summary>
    public override bool IsActive(ItemEffectContext ctx) => _trigger is "OnCrit" or "AfterHit" || base.IsActive(ctx);

    private const int DefaultPoisonStacks = 8;

    // 되받아치는 화염(10-14 R2) — 발동이 「AfterHit」이면 맞은 뒤 창(duration) 동안만 적중이 화상 · 창은 6초에 한 번
    private float _hurtWindowUntil = -999f, _hurtReadyAt;
    private const float HurtWindowGap = 6f;

    public override void OnPostTakeDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (_trigger != "AfterHit" || Time.time < _hurtReadyAt) return;
        _hurtReadyAt     = Time.time + HurtWindowGap;
        _hurtWindowUntil = Time.time + (_duration > 0f ? _duration : 4f);
    }

    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (_trigger == "AfterHit" && Time.time > _hurtWindowUntil) return;
        if (!OnHitProc.Roll(_value, report)) return;
        if (report.Target == null) return;

        var mb = report.Target.GetComponentInParent<RelicFairy.Monster.MonsterBase>();
        if (mb == null) return;

        float interval   = _value3 > 0f ? _value3 : 1f;
        int   ticks      = Mathf.Max(1, Mathf.RoundToInt((_duration > 0f ? _duration : interval) * OnHitProc.Coef(report) / interval));   // 지속 × 계수
        var   owner      = ctx.Player != null ? ctx.Player.gameObject : null;

        // 상태 몸 이펙트(item_poison → 독 · item_burn → 불)는 수신기가 자동 표시.
        if (_statusId == "item_burn")
        {
            float perTick = Mathf.Max(1f, report.DamageDealt * Mathf.Max(0f, _value2));   // 그 타격 피해 비율 — 큰 한 방이 화상을 키운다
            mb.Status.ApplyStrongestDot(_statusId, perTick, interval, ticks, owner);
        }
        else
        {
            int atk = ctx.Stats != null ? ctx.Stats.GetEffectiveAttack(ctx.WeaponType.GetAttackStatKind()) : 0;
            float perStack = Mathf.Max(1f, atk * Mathf.Max(0f, _value2));
            mb.Status.ApplyStackingDot(_statusId, perStack, interval, ticks, _maxStack > 1 ? _maxStack : DefaultPoisonStacks, owner);
        }

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
        if (!OnHitProc.Roll(_value, report)) return;
        if (report.Target == null) return;

        // 상태이상 통합 수신기로 흡수 — 빙결 = CC(이동·FSM 정지).
        // 지속은 duration(초) 필드 × 발동 계수. 구버전 데이터(지속을 max_stack에 넣은 경우) 하위호환 폴백.
        var mb = report.Target.GetComponentInParent<RelicFairy.Monster.MonsterBase>();
        if (mb == null) return;
        float dur = (_duration > 0f ? _duration : (_maxStack > 0 ? _maxStack : 2f)) * OnHitProc.Coef(report);
        if (!OnHitProc.TryGateCc(mb, dur)) return;

        // [정리] 레거시 기본키(VFX_Wet=물 원소 재활용) 제거 — 빙결 표시는 속성 VFX(얼음 몸 이펙트)가 담당.
        var vfx = ResolveVfxKey(null);
        if (!string.IsNullOrEmpty(vfx)) ItemEffectVfxHelper.SpawnOneShotAt(vfx, report.Target.transform.position);
        mb.Status.ApplyCc("freeze", dur);
    }
}

public sealed class ExtraAttackEffect : ItemEffectBase
{
    private const string DefaultVfxKey = "VFX_ExtraAttack";

    public ExtraAttackEffect(ItemEffectSlot s) : base(s) { }

    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (!OnHitProc.Roll(_value, report)) return;
        if (report.Target == null || ctx.Player == null) return;

        if (report.Target.TryGetComponent<IDamageable>(out var damageable))
        {
            using (DamageSourceScope.Enter(DamageSource.Rune))   // 결과창 · 실측 피해 비중 — 복사 피해는 룬 몫(스킬 적중에서 터져도 스킬로 세지 않는다 · 10-16)
                damageable.TakeDamage(report.DamageDealt, ctx.Player.gameObject, knockbackMultiplier: 0f);

            var hitPos = report.HitPosition != Vector3.zero
                ? report.HitPosition
                : report.Target.transform.position;
            ItemEffectVfxHelper.SpawnOneShotAt(ResolveVfxKey(DefaultVfxKey), hitPos);
            // HUD 왼쪽 알림은 뺐다 — 타격의 12%마다 떠 같은 순간 이펙트 · 피해 숫자와 같은 말을 했다(10-11 톤 2차 V-12)
            RFLog.D($"[ExtraAttack] 추가 타격 {report.DamageDealt:F0} → {report.Target.name}");
        }
    }
}

public sealed class TeleportSwapEffect : ItemEffectBase
{
    private const float SampleRadius = 1.5f;

    public TeleportSwapEffect(ItemEffectSlot s) : base(s) { }

    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (!OnHitProc.Roll(_value, report)) return;
        if (ctx.Player == null || report.Target == null) return;
        // 보스와는 바꾸지 않는다 — 제자리 보스(죽음의 기사)가 플레이어 구역으로 넘어오고 플레이어는 유리벽 너머에 남는다(09-30)
        if (report.Target.TryGetComponent<RelicFairy.Monster.IBoss>(out _)) return;

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
        if (!OnHitProc.Roll(_value, report)) return;
        if (report.Target == null) return;

        // 상태이상 통합 수신기로 흡수 — 석화 = CC(완전 무력화). 지속은 _duration × 발동 계수.
        var mb = report.Target.GetComponentInParent<RelicFairy.Monster.MonsterBase>();
        if (mb == null) return;
        float dur = (_duration > 0f ? _duration : 1.5f) * OnHitProc.Coef(report);
        if (!OnHitProc.TryGateCc(mb, dur)) return;

        ItemEffectVfxHelper.SpawnOneShotAt(ResolveVfxKey(DefaultVfxKey), report.Target.transform.position);
        mb.Status.ApplyCc("petrify", dur);
    }
}

public sealed class StunEffect : ItemEffectBase
{
    public StunEffect(ItemEffectSlot s) : base(s) { }

    public override void OnPostDealDamage(ItemEffectContext ctx, DamageReport report)
    {
        if (!OnHitProc.Roll(_value, report)) return;
        if (report.Target == null) return;

        // 상태이상 시스템(MonsterBase.ApplyStun)과 통합 — 룬 감전과 동일 경로. 지속 × 발동 계수.
        var mb = report.Target.GetComponentInParent<RelicFairy.Monster.MonsterBase>();
        if (mb == null) return;
        float dur = (_duration > 0f ? _duration : 1f) * OnHitProc.Coef(report);
        if (!OnHitProc.TryGateCc(mb, dur)) return;

        // [정리] 레거시 기본키(VFX_Shock=번개 원소 재활용) 제거 — 기절 표시는 속성 VFX(전기)가 담당.
        var vfx = ResolveVfxKey(null);
        if (!string.IsNullOrEmpty(vfx)) ItemEffectVfxHelper.SpawnOneShotAt(vfx, report.Target.transform.position);
        mb.ApplyStun(dur);
    }
}

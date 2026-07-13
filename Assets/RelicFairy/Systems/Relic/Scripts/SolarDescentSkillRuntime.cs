using System.Collections.Generic;
using UnityEngine;
using RelicFairy.Monster;

/// <summary>
/// 가웨인 고유 스킬 — 낙일(태양 강림 리워크). 정오 전용·구간당 1회(GawainZenithRelic 게이팅).
/// 전방 지정 지점에 태양이 낙하 → 착탄 반경 대폭발(ATK×skillMult) + 작열 지대(지속 화염 장판) 생성.
/// "태양이 정점에서 강림한다" 컨셉 직접 표현(부채꼴 → 낙하 폭발).
/// 수치는 RELIC_STAT_DATA(gawain) 슬롯 구동. 화상은 MonsterBurnHandler, 장판은 SolarZone.
/// </summary>
public sealed class SolarDescentSkillRuntime : ISkillRuntime
{
    private const string RelicKey = "gawain";
    private const int V_FIRST_HIT = 10, V_SKILL_MULT = 11, V_BURN_DURATION = 12, V_BURN_TICK_RATIO = 13;

    private const string AnimName        = "QSkill_01";
    private const float  AnimDuration    = 1.0f;
    private const float  HitTime         = 0.35f;
    private const float  ImpactDistance  = 3.0f;   // 전방 낙하 지점 거리
    private const float  ImpactRadius    = 3.5f;   // 착탄 폭발 반경
    private const float  ZoneRadius      = 3.5f;   // 작열 지대 반경
    private const float  ZoneDuration    = 4.0f;   // 작열 지대 지속
    private const float  ZoneTickInterval = 0.5f;
    private const string ImpactVfxKey     = "vfx_gawain_solar_impact"; // 낙일 착탄 VFX(에셋 배선 후 등록)

    private readonly GawainZenithRelic _relic;
    private float _elapsed;
    private bool  _hitDone;

    public SolarDescentSkillRuntime(GawainZenithRelic relic) { _relic = relic; }

    public void OnEnter(SkillExecutionContext ctx)
    {
        _elapsed = 0f; _hitDone = false;
        _relic?.MarkSkillUsed(); // 구간당 1회 소비

        if (ctx.PlayerTransform != null)
            GuidelineVisual.Toast(ctx.PlayerTransform.position + Vector3.up * 2.4f, "낙일", GuidelineVisual.ToastKind.Relic);

        ctx.RotateToMouse();
        ctx.SetMoveScale(0f);
        ctx.Animator?.CrossFade(AnimName, 0.1f);
    }

    public void OnUpdate(SkillExecutionContext ctx)
    {
        _elapsed += Time.deltaTime;
        if (!_hitDone && _elapsed >= HitTime) { _hitDone = true; Impact(ctx); }
        if (_elapsed >= AnimDuration) ctx.RequestEnd?.Invoke();
    }

    public void OnExit(SkillExecutionContext ctx) => ctx.SetMoveScale(1f);

    private void Impact(SkillExecutionContext ctx)
    {
        var pt = ctx.PlayerTransform;
        Vector3 fwd = pt.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.forward;
        fwd.Normalize();
        Vector3 impact = pt.position + fwd * ImpactDistance;

        float markMul = (_relic != null && _relic.ConsumeMarkFirstHit()) ? (1f + V(V_FIRST_HIT, 0.5f)) : 1f;
        float dmg     = ctx.CalculateDamage(V(V_SKILL_MULT, 3.5f)) * markMul;
        float effAtk  = ctx.RuntimeStats.GetEffectiveAttack(AttackStatKind.Melee);
        float burnDps = effAtk * V(V_BURN_TICK_RATIO, 0.15f);
        float burnDur = V(V_BURN_DURATION, 6f);

        GuidelineVisual.SynergyDamage(impact + Vector3.up * 0.2f, false); // 착탄 표시
        RelicStateVfx.PlayOneShot(ImpactVfxKey, impact);                  // 낙일 착탄 VFX

        var owner  = ctx.Controller.gameObject;
        var buffer = new List<MonsterBase>(32);
        int found = CombatQuery.GetNearbyEnemies(impact, ImpactRadius, owner, 32, buffer);

        Debug.Log($"[가웨인Q] 발동 | mult={V(V_SKILL_MULT, 3.5f):F2} dmg={dmg:F0} | 착탄({ImpactRadius}m) 적중 대상={found}마리");

        foreach (var mb in buffer)
        {
            if (mb == null) continue;

            // 주 피해 파이프라인 — 직접 TakeDamage를 부르면 크리티컬·아이템·서약·패시브·타격감이 전부 스킵된다.
            float applied = CombatDamage.Deal(new CombatDamage.Request
            {
                Target              = mb.gameObject,
                BaseDamage          = dmg,
                Owner               = owner,
                ActionType          = WeaponActionType.QSkill,
                KnockbackMultiplier = 0.3f,
                HitPoint            = mb.transform.position + Vector3.up * 1.2f,
                SourcePosition      = impact,
            });
            Debug.Log($"[가웨인Q] → {mb.name}: 요청 {dmg:F0} → 실제적용 {applied:F0}");

            // 화상은 2차 피해(DoT) — 파이프라인을 타지 않는다(틱마다 크릿/흡혈이 터지면 안 됨).
            if (burnDps > 0f)
                MonsterBurnHandler.Apply(mb.gameObject, burnDps, burnDur, ZoneTickInterval, owner);
        }

        // 작열 지대 — 착탄 지점에 지속 화염 장판(틱 피해 + 화상)
        SolarZone.Spawn(impact, ZoneRadius, ZoneDuration,
                        tickDamage: effAtk * 0.10f, burnDps: burnDps, tickInterval: ZoneTickInterval, instigator: owner);
    }

    private static float V(int slot, float fallback)
        => Managers.RelicStatData != null ? Managers.RelicStatData.Get(RelicKey, slot, fallback) : fallback;
}

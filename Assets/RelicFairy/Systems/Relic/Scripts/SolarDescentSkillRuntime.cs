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

    // ── 타이밍은 애니와 이펙트에 맞춘다(실측) ─────────────────────────
    //  • 애니 QSkill_01 : 클립 1.0초 × 상태 speed 2 = 실제 0.5초. 내리치는 접촉 프레임이 대략 0.4초.
    //  • 착탄 VFX(Effect_10_BigMeteorHitEffect) : 스폰 즉시 터진다(첫 파티클 delay 0) · 전체 3초.
    //    → 스윙이 꽂히는 순간에 폭발과 피해를 동시에 얹는다. 그전에 때리면 허공을 치는 것처럼 보인다.
    private const string AnimName        = "QSkill_01";
    // '해를 떨어뜨린다'는 무게 — 높은 하늘에서, 크게, 조금 길게 떨어진다.
    private const float  MeteorFallHeight = 22f;   // 불덩이가 시작하는 하늘 높이
    private const float  MeteorFallTime   = 0.6f;  // 낙하 시간(높은 만큼 길게)
    private const float  CastTime         = 0.30f; // 하늘로 손을 뻗어 태양을 부르는 순간(낙하 시작)
    private const float  ImpactDistance  = 3.0f;   // 전방 낙하 지점 거리
    private const float  ImpactRadius    = 6.5f;   // 착탄 폭발 반경 — 크게(태양이 내리꽂히는 광역)
    private const float  ImpactVfxScale  = 3.2f;   // 착탄 폭발 크기 — 반경에 맞춰 크게
    private const float  ZoneRadius      = 6.0f;   // 작열 지대 반경 — 착탄 반경에 맞춰 넓게
    private const float  ZoneDuration    = 4.0f;   // 작열 지대 지속
    private const float  ZoneTickInterval = 0.5f;

    private const float  FallDamageShare = 0.30f;  // 총 피해 중 낙하가 가져가는 몫(나머지는 착탄)
    private const float  FallHitRadius   = 3.0f;   // 낙하하는 태양 아래 피해 범위(착탄 반경보다 좁게)
    // 캐스트 + 낙하 후 짧은 후딜.
    private static float AnimDuration => CastTime + MeteorFallTime + 0.25f;

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
        // 캐스트 순간 태양을 부른다 → 불덩이가 하늘에서 떨어지기 시작 → MeteorFallTime 뒤 착탄.
        if (!_hitDone && _elapsed >= CastTime) { _hitDone = true; CallMeteor(ctx); }
        if (_elapsed >= AnimDuration) ctx.RequestEnd?.Invoke();
    }

    public void OnExit(SkillExecutionContext ctx) => ctx.SetMoveScale(1f);

    /// <summary>
    /// 태양을 부른다 — 하늘에서 불덩이가 떨어진다.
    /// 내려오는 태양이 지면 근처 적을 <b>훑으며 태우고</b>(낙하 피해), 착탄하면 대폭발로 마무리한다.
    /// 착탄에 몰지 않고 낙하로 나눠, 떨어지는 과정 자체가 게임플레이로 느껴지게 한다.
    /// </summary>
    private void CallMeteor(SkillExecutionContext ctx)
    {
        var pt = ctx.PlayerTransform;
        Vector3 fwd = pt.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.forward;
        fwd.Normalize();
        Vector3 impact = pt.position + fwd * ImpactDistance;

        // 낙하 중 공격력이 변할 일은 없으니 지금 확정해 콜백으로 넘긴다.
        float markMul = (_relic != null && _relic.ConsumeMarkFirstHit()) ? (1f + V(V_FIRST_HIT, 0.5f)) : 1f;
        int   effAtk  = ctx.RuntimeStats.GetEffectiveAttack(AttackStatKind.Melee);
        float total   = effAtk * V(V_SKILL_MULT, 3.5f) * markMul;

        // 낙하가 총 피해의 30%를 여러 틱으로, 착탄이 70%를 한 방에.
        float impactDmg = total * (1f - FallDamageShare);
        float fallDmg   = total * FallDamageShare;

        var owner = ctx.Controller.gameObject;

        SolarMeteor.Strike(impact, MeteorFallHeight, MeteorFallTime, ImpactVfxScale, ImpactRadius,
                           onGroundContact: sunPos => OnMeteorFallTick(sunPos, fallDmg, owner),
                           onImpact:        ()      => OnMeteorImpact(impact, effAtk, impactDmg, owner));
    }

    /// <summary>
    /// 낙하 중 태양이 지면 가까이 왔을 때 매 틱 호출 — 태양 아래 좁은 범위의 적을 태운다(약하게, 방어무시).
    /// SolarMeteor가 이미 '지면 근처'일 때만 부르므로 여기선 범위 질의만 한다.
    /// </summary>
    private void OnMeteorFallTick(Vector3 sunPos, float fallDmg, GameObject owner)
    {
        var buf = new List<GameObject>(16);
        int n = CombatQuery.GetNearbyDamageables(sunPos, FallHitRadius, owner, 16, buf);
        for (int i = 0; i < n; i++)
        {
            var t = buf[i];
            if (t == null) continue;
            // 낙하 피해는 2차(방어무시 즉발) — 여러 틱이라 파이프라인을 태우면 크리·흡혈이 매 틱 터진다.
            CombatQuery.DealSynergyDamage(t, fallDmg, owner, 1f);
        }
    }

    /// <summary>착탄 순간 — 폭발 여파(피해·화상·장판·타격감)를 낸다.</summary>
    private void OnMeteorImpact(Vector3 impact, int effAtk, float dmg, GameObject owner)
    {
        float burnDps = effAtk * V(V_BURN_TICK_RATIO, 0.15f);
        float burnDur = V(V_BURN_DURATION, 6f);

        GuidelineVisual.SynergyDamage(impact + Vector3.up * 0.2f, false);   // 착탄 표시
        HitFeelService.CameraShake(0.28f, 0.30f);                          // 태양이 하늘에서 꽂히는 무게
        HitFeelService.HitStop(0.05f, 0.06f);                              // 착탄 순간 짧은 정지 — 타격의 방점

        var buffer = new List<GameObject>(32);
        // 몬스터로 좁히지 않고 IDamageable 전체를 잡는다 — 그래야 훈련용 허수아비에도 들어간다.
        int found = CombatQuery.GetNearbyDamageables(impact, ImpactRadius, owner, 32, buffer);

        RFLog.D($"[가웨인Q] 착탄 | dmg={dmg:F0} | 반경({ImpactRadius}m) 적중 {found}");

        foreach (var target in buffer)
        {
            if (target == null) continue;

            // 주 피해 파이프라인 — 직접 TakeDamage를 부르면 크리티컬·아이템·서약·패시브·타격감이 전부 스킵된다.
            CombatDamage.Deal(new CombatDamage.Request
            {
                Target              = target,
                BaseDamage          = dmg,
                Owner               = owner,
                ActionType          = WeaponActionType.QSkill,
                KnockbackMultiplier = 0.3f,
                HitPoint            = target.transform.position + Vector3.up * 1.2f,
                SourcePosition      = impact,
            });

            // 화상은 2차 피해(DoT) — 파이프라인을 타지 않는다(틱마다 크릿/흡혈이 터지면 안 됨).
            // IDamageable이면 붙으므로 더미에도 그대로 걸린다.
            if (burnDps > 0f)
                MonsterBurnHandler.Apply(target, burnDps, burnDur, ZoneTickInterval, owner);
        }

        // 작열 지대 — 착탄 지점에 지속 화염 장판(틱 피해 + 화상)
        SolarZone.Spawn(impact, ZoneRadius, ZoneDuration,
                        tickDamage: effAtk * 0.10f, burnDps: burnDps, tickInterval: ZoneTickInterval, instigator: owner);
    }

    private static float V(int slot, float fallback)
        => Managers.RelicStatData != null ? Managers.RelicStatData.Get(RelicKey, slot, fallback) : fallback;
}

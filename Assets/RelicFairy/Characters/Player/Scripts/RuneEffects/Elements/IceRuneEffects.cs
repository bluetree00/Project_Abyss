using System;
using System.Collections.Generic;
using UnityEngine;
using RelicFairy.Monster;

// ═══════════════════════════════════════════════════════════════════════════
// ❄ 얼음 Ice — 제어 (4단계 누적, 적 상태 = 서리/빙결)
//
// ST(서리 Slow / 빙결 CC + 만료·해제 콜백)를 그대로 재사용. 신규 인프라 최소.
// 추가분: ST에 GetSlowStacks(서리 중첩 질의) + SetCooldown/IsOnCooldown(적별 재빙결 방지) 일반 프리미티브.
// "스킬 적중" 판정 = RuneEffectDispatcher.IsWithinSkillWindow(스킬 실행 경로 ActSkillStateBase→NotifySkillUsed 연동).
//
// 데이터 키(실데이터): IceFrost / IceFreeze / IceShatter / IceGlacier. threshold=점유수(6/10/13/19).
// 상태 id: 서리="frost"(Slow), 빙결="freeze"(CC), 재빙결 쿨="freeze_cd".
// ═══════════════════════════════════════════════════════════════════════════

public abstract class IceRuneEffectBase : RuneElementEffectBase
{
    protected const string ID_FROST  = "frost";
    protected const string ID_FREEZE = "freeze";
    protected const string CD_FREEZE = "freeze_cd";
}

/// <summary>
/// 1단계 서리 — 적중 시 이속 -12% 3초, 최대 2중첩(-24%). ST ApplySlow 재사용.
/// value=이속감소(0.12), max_stack=2, duration=3.
/// </summary>
public sealed class IceFrostEffect : IceRuneEffectBase
{
    public override void OnHit(in HitInfo hit, PlayerController player)
    {
        if (hit.Target == null) return;
        var mb = hit.Target.GetComponentInParent<MonsterBase>();
        if (mb == null) return;

        int maxStacks = Entry.max_stack > 0 ? Entry.max_stack : 2;
        float dur = Entry.duration > 0f ? Entry.duration : 3f;
        mb.Status.ApplySlow(ID_FROST, Entry.value, dur, maxStacks);
    }
}

/// <summary>
/// 2단계 빙결 — 서리 2중첩 적에 "스킬 적중" 시 빙결 1.5초(적별 내부쿨 10초). ST ApplyCc 재사용.
/// 해제 시(만료) 분쇄(방어-20%)·빙하(광역+서리) 효과를 onExpire로 예약(누적 상위단계 활성 스냅샷).
/// value=빙결지속(1.5), value2=요구 서리중첩(2), value3=내부쿨(10).
/// </summary>
public sealed class IceFreezeEffect : IceRuneEffectBase
{
    private const float SKILL_WINDOW   = 0.6f;
    private const float GLACIER_RADIUS = 4f;
    private static readonly List<MonsterBase> s_buf = new();

    public override void OnHit(in HitInfo hit, PlayerController player)
    {
        if (hit.Target == null) return;
        var disp = player.RuneEffects;
        if (disp == null || !disp.IsWithinSkillWindow(SKILL_WINDOW)) return;   // 스킬 적중만

        var mb = hit.Target.GetComponentInParent<MonsterBase>();
        if (mb == null) return;

        int required = Entry.value2 > 0f ? (int)Entry.value2 : 2;
        if (mb.Status.GetSlowStacks(ID_FROST) < required) return;
        if (mb.Status.IsOnCooldown(CD_FREEZE)) return;

        float freezeDur  = Entry.value  > 0f ? Entry.value  : 1.5f;
        float internalCd = Entry.value3 > 0f ? Entry.value3 : 10f;

        mb.Status.ApplyCc(ID_FREEZE, freezeDur, BuildOnFreezeExpire(player, mb, disp));
        mb.Status.SetCooldown(CD_FREEZE, internalCd);
    }

    /// <summary>빙결 해제 시 효과 — 분쇄(받피 증폭 근사)·빙하(광역 공150% + 서리 1중첩). 빙결 시점의 활성 단계 스냅샷.</summary>
    private Action BuildOnFreezeExpire(PlayerController player, MonsterBase mb, RuneEffectDispatcher disp)
    {
        // 3단계 분쇄: 방어 -20% ≈ 받는피해 증폭
        var shatterE = disp.GetActive("IceShatter")?.Entry;
        float defAmp = shatterE != null ? shatterE.value2 : 0f;
        float defDur = shatterE != null && shatterE.duration > 0f ? shatterE.duration : 5f;

        // 4단계 빙하: 광역 + 서리
        var glacierE = disp.GetActive("IceGlacier")?.Entry;
        bool  glacier    = glacierE != null;
        float glacierDmg = glacier ? glacierE.value * GetEffectiveAttack(player) : 0f;

        var frostE = disp.GetActive("IceFrost")?.Entry;
        float frostMag = frostE != null ? frostE.value : 0.12f;
        float frostDur = frostE != null && frostE.duration > 0f ? frostE.duration : 3f;
        int   frostMax = frostE != null && frostE.max_stack > 0 ? frostE.max_stack : 2;

        var instigator = player.gameObject;

        return () =>
        {
            if (mb == null) return;

            if (defAmp > 0f) mb.ApplyDamageTakenAmp(defAmp, defDur, "shatter");

            if (glacier && glacierDmg > 0f)
            {
                Vector3 origin = mb.transform.position;
                int n = CombatQuery.GetNearbyEnemies(origin, GLACIER_RADIUS, null, 16, s_buf);
                for (int i = 0; i < n; i++)
                {
                    CombatQuery.DealSynergyDamage(s_buf[i], glacierDmg, instigator);
                    s_buf[i].Status.ApplySlow(ID_FROST, frostMag, frostDur, frostMax);
                }
            }
        };
    }
}

/// <summary>
/// 3단계 분쇄 — 빙결(HasCc) 적 공격 시 피해 +30%(방어무시 추가). 해제 시 방어-20%는 빙결 onExpire가 처리.
/// value=증폭(0.3), value2=방어감소(0.2), duration=디버프지속(5).
/// </summary>
public sealed class IceShatterEffect : IceRuneEffectBase
{
    public override void OnHit(in HitInfo hit, PlayerController player)
    {
        if (hit.Target == null) return;
        var mb = hit.Target.GetComponentInParent<MonsterBase>();
        if (mb == null || !mb.Status.HasCc(ID_FREEZE)) return;

        float bonus = hit.Damage * Entry.value;
        if (bonus > 0f) CombatQuery.DealSynergyDamage(mb, bonus, player.gameObject);
    }
}

/// <summary>4단계 빙하 연쇄 — 마커. 빙결 해제 시 광역 공150% + 서리 1중첩(IceFreeze onExpire가 수행). value=배율(1.5).</summary>
public sealed class IceGlacierEffect : IceRuneEffectBase { }

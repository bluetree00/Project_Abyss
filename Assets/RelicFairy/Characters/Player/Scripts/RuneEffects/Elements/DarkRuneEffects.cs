using System.Collections.Generic;
using UnityEngine;
using RelicFairy.Monster;

// ═══════════════════════════════════════════════════════════════════════════
// 🌑 어둠 Dark — 피격 강화 (4단계 누적, 게이지 중심)
//
// 갭2(OnDamaged) 의존: PlayerController.TakeDamage → RuneEffects.NotifyDamaged → OnDamaged.
// 공용 시스템: RS 게이지+임계콜백(빛 광채와 동일), SL 동적 공격%/피해감소(별도 레이어), AQ/DM.
//
// 데이터 키(실데이터): DarkErosion / DarkRelease / DarkAfterimage / DarkAbyss. threshold=점유수(6/10/13/19).
// 게이지 = RS "darkGauge"(비감쇠, max=DarkErosion.max_stack=100). DarkErosion이 SL 공격%/피해감소 합산 소유자.
// ═══════════════════════════════════════════════════════════════════════════

public abstract class DarkRuneEffectBase : RuneElementEffectBase
{
    protected const string GAUGE          = "darkGauge";
    protected const string RELEASE_ATK    = "darkReleaseAtk";    // 암흑 공% 기여(float)
    protected const string RELEASE_DR     = "darkReleaseDR";     // 암흑 피해감소 기여(float)
    protected const string RELEASE_ACTIVE = "darkReleaseActive"; // 암흑 중(1/0) — 그림자 잔상 판정
    protected const string ABYSS_ATK         = "darkAbyssAtk";         // 심연각성 공% 버프(float)
    protected const string ABYSS_DR          = "darkAbyssDR";          // 심연각성 피해감소 버프(float)
    protected const string ABYSS_RELEASE_EXT = "darkAbyssReleaseExt";  // 심연각성 암흑 해방 연장(초)
}

/// <summary>
/// 1단계 잠식 — 피격 시 게이지 +10, 적중 시 +3(최대100). 게이지 비례 공격력 최대 +15%(SL).
/// SL 공격%/피해감소 합산 소유자: 잠식(게이지) + 암흑해방 + 심연각성 기여를 매 프레임 반영.
/// value=최대공%(0.15), value2=피격게이지(10), value3=적중게이지(3), max_stack=게이지max(100).
/// </summary>
public sealed class DarkErosionEffect : DarkRuneEffectBase
{
    private int Max => Entry.max_stack > 0 ? Entry.max_stack : 100;

    public override void OnDamaged(in HitInfo hit, PlayerController player)
    {
        Res(player)?.AddGauge(GAUGE, Mathf.Max(1, (int)Entry.value2), Max);
    }

    public override void OnHit(in HitInfo hit, PlayerController player)
    {
        Res(player)?.AddGauge(GAUGE, Mathf.Max(1, (int)Entry.value3), Max);
    }

    public override void Tick(float dt, PlayerController player)
    {
        var res   = Res(player);
        var stats = player?.RuntimeStats;
        if (res == null || stats == null) return;

        float ratio = Mathf.Clamp01(res.GetGauge(GAUGE) / (float)Max);
        float atk = ratio * Entry.value + res.GetFloat(RELEASE_ATK) + res.GetFloat(ABYSS_ATK);
        float dr  = res.GetFloat(RELEASE_DR) + res.GetFloat(ABYSS_DR);

        stats.SetSynergyDynamicAttackPercent(atk);
        stats.SetSynergyDynamicDamageReduction(dr);
    }

    public override void OnDeactivate()
    {
        var stats = CachedPlayer?.RuntimeStats;
        if (stats != null)
        {
            stats.SetSynergyDynamicAttackPercent(0f);
            stats.SetSynergyDynamicDamageReduction(0f);
        }
        CachedPlayer?.RuneEffects?.Resources?.RemoveSlot(GAUGE);   // 어둠 게이지 잔존 방지
    }
}

/// <summary>
/// 2단계 암흑 해방 — 게이지 100 도달 시(RS 임계콜백) 5초 암흑: 공 +25%(SL)·받피 -15%(SL). 발동 시 게이지 소모(리셋).
/// 심연각성(4단계) 활성 시 동시에 랜덤 버프 1개 부여.
/// value=공%(0.25), value2=피해감소(0.15), duration=암흑지속(5).
/// </summary>
public sealed class DarkReleaseEffect : DarkRuneEffectBase
{
    private float _releaseUntil;

    public override void OnActivate(PlayerController player)
    {
        base.OnActivate(player);
        var res = Res(player);
        if (res == null) return;
        int full = player.RuneEffects?.GetActive("DarkErosion")?.Entry?.max_stack ?? 100;
        res.RegisterThreshold(GAUGE, full, () => Trigger(player));
    }

    private void Trigger(PlayerController player)
    {
        var res = Res(player);
        if (res == null) return;

        float ext = res.GetFloat(ABYSS_RELEASE_EXT);   // 심연각성 연장 시간
        float dur = (Entry.duration > 0f ? Entry.duration : 5f) + ext;
        _releaseUntil = Time.time + dur;
        res.Consume(GAUGE);   // 게이지 리셋 → 임계 재무장

        // 심연각성(4단계): 확정 강화
        (player.RuneEffects?.GetActive("DarkAbyss") as DarkAbyssEffect)?.Grant(player, dur);
    }

    public override void Tick(float dt, PlayerController player)
    {
        var res = Res(player);
        if (res == null) return;
        bool active = Time.time < _releaseUntil;
        res.SetFloat(RELEASE_ATK, active ? Entry.value  : 0f);
        res.SetFloat(RELEASE_DR,  active ? Entry.value2 : 0f);
        res.SetRegister(RELEASE_ACTIVE, active ? 1 : 0);
    }

    public override void OnDeactivate()
    {
        var res = CachedPlayer?.RuneEffects?.Resources;
        if (res != null)
        {
            res.SetFloat(RELEASE_ATK, 0f);
            res.SetFloat(RELEASE_DR, 0f);
            res.SetRegister(RELEASE_ACTIVE, 0);
            res.RemoveThreshold(GAUGE);   // 단계 하강 시 stale 암흑 해방 발동 방지(1단계 잠식이 게이지 채워도)
        }
    }
}

/// <summary>
/// 3단계 그림자 잔상 — 암흑 중 적중 시 동일 공격의 40%를 0.5초 후 추가 타격(지연 큐, Tick 처리).
/// value=비율(0.4), duration=지연(0.5).
/// </summary>
public sealed class DarkAfterimageEffect : DarkRuneEffectBase
{
    private struct Pending { public float t; public GameObject target; public float dmg; }
    private readonly List<Pending> _pending = new();

    public override void OnHit(in HitInfo hit, PlayerController player)
    {
        if (hit.Target == null) return;
        var res = Res(player);
        if (res == null || res.GetRegister(RELEASE_ACTIVE) != 1) return;   // 암흑 중에만

        float dmg = hit.Damage * Entry.value;
        if (dmg <= 0f) return;
        float delay = Entry.duration > 0f ? Entry.duration : 0.5f;
        _pending.Add(new Pending { t = delay, target = hit.Target, dmg = dmg });
    }

    public override void Tick(float dt, PlayerController player)
    {
        if (_pending.Count == 0) return;
        var instigator = player != null ? player.gameObject : null;

        for (int i = _pending.Count - 1; i >= 0; i--)
        {
            var p = _pending[i];
            p.t -= dt;
            if (p.t <= 0f)
            {
                CombatQuery.DealSynergyDamage(p.target, p.dmg, instigator, element: RuneElement.Dark);
                _pending.RemoveAt(i);
            }
            else _pending[i] = p;
        }
    }

    public override void OnDeactivate() => _pending.Clear();
}

/// <summary>
/// 4단계 심연 각성 — 게이지 달성(암흑 해방)마다 확정 강화 3종:
///   1. 공격력 +25% 시한부 (SL, DarkErosion이 합산)
///   2. 주변 적 받는 피해 +20% (ApplyDamageTakenAmp, 암흑 지속과 동일)
///   3. 암흑 해방 지속시간 +2초 (DarkRelease가 ABYSS_RELEASE_EXT를 읽어 적용)
/// value=공%(0.25), value2=받뎀증폭(0.20), value3=해방연장초(2.0).
/// </summary>
public sealed class DarkAbyssEffect : DarkRuneEffectBase
{
    private const float NEARBY_RADIUS = 6f;
    private static readonly List<MonsterBase> s_buf = new();

    private float _atkUntil;

    public override void OnActivate(PlayerController player)
    {
        base.OnActivate(player);
        // 암흑 해방 연장을 항상 활성 상태로 유지 — 비활성화 시 DarkRelease가 기본 지속으로 복귀
        Res(player)?.SetFloat(ABYSS_RELEASE_EXT, Entry.value3 > 0f ? Entry.value3 : 2f);
    }

    /// <summary>공격력 + 주변 적 받뎀 증폭. DarkRelease.Trigger가 호출한다.</summary>
    public void Grant(PlayerController player, float duration)
    {
        var res = Res(player);
        if (res == null) return;

        // 공격력 +25% (시한부, DarkErosion Tick이 SL 합산)
        res.SetFloat(ABYSS_ATK, Entry.value > 0f ? Entry.value : 0.25f);
        _atkUntil = Time.time + duration;

        // 주변 적 받는 피해 증폭 (즉발)
        float vulnAmp = Entry.value2 > 0f ? Entry.value2 : 0.2f;
        int n = CombatQuery.GetNearbyEnemies(player.transform.position, NEARBY_RADIUS, null, 16, s_buf);
        for (int i = 0; i < n; i++) s_buf[i].ApplyDamageTakenAmp(vulnAmp, duration, "vulnerable");

        ElementVfxPlayer.PlayBurst(RuneElement.Dark, player.transform.position);
    }

    public override void Tick(float dt, PlayerController player)
    {
        if (_atkUntil <= 0f || Time.time < _atkUntil) return;
        _atkUntil = 0f;
        Res(player)?.SetFloat(ABYSS_ATK, 0f);
    }

    public override void OnDeactivate()
    {
        var res = CachedPlayer?.RuneEffects?.Resources;
        if (res != null)
        {
            res.SetFloat(ABYSS_ATK, 0f);
            res.SetFloat(ABYSS_DR, 0f);
            res.SetFloat(ABYSS_RELEASE_EXT, 0f);
        }
        _atkUntil = 0f;
    }
}

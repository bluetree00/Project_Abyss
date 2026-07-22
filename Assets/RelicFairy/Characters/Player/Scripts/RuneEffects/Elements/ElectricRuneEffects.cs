using System.Collections.Generic;
using UnityEngine;
using RelicFairy.Monster;

// ═══════════════════════════════════════════════════════════════════════════
// ⚡ 전기 Electric — 자기강화 루프 (4단계 누적, 단독완결)
//
// 단계 판정은 데이터(threshold = 점유 셀 수)로 구동 → 누적형이라 상위 단계 활성 시
// 하위 단계도 함께 활성 상태를 유지한다(예: 과부하 활성이면 정전기/방전/감전도 활성).
//
// 공용 시스템 사용:
//  RS  player.RuneEffects.Resources : 정전기 스택, 방전 소모량 레지스터, 대상 슬롯, 감전 공속 float
//  SL  PlayerRuntimeStats.SetSynergyDynamicAttackSpeed : 공속 동적 레이어(유물 슬롯과 분리)
//  DM  CombatQuery.DealSynergyDamage : 방어 우회 즉발 피해
//  AQ  CombatQuery.GetNearbyEnemies  : 체인/대상 질의
//  ST  MonsterBase.ApplyStun         : 감전(마비)
//
// 파라미터(positional) 매핑은 각 클래스 상단 주석 참조.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>전기 효과 공용 베이스 — 전기 전용 RS 키 정의(헬퍼는 RuneElementEffectBase).</summary>
public abstract class ElectricRuneEffectBase : RuneElementEffectBase
{
    protected const string KEY_STACK      = "ElecStatic";          // 정전기 스택
    protected const string KEY_CONSUMED   = "ElecConsumedStacks";  // 방전이 소모한 스택 수(감전이 읽음)
    protected const string KEY_TARGET     = "ElecDischargeTarget"; // 방전 대상(감전이 공유)
    protected const string KEY_SHOCK_AS   = "ElecShockAtkSpeed";   // 감전 중 공속 기여(정전기가 합산)
}

/// <summary>
/// 1단계 정전기 — 공속 +10% 기본 + 적중 시 스택+1(스택당 공속 +1%, 10초, 최대10).
/// value=기본공속(0.10), value2=스택당공속(0.01), max_stack=10, duration=10.
/// 또한 전기 공속 동적 레이어의 "합산 소유자" — 감전(3단계) 기여를 더해 매 프레임 반영한다.
/// </summary>
public sealed class ElecStaticEffect : ElectricRuneEffectBase
{
    public override void OnHit(in HitInfo hit, PlayerController player)
    {
        var res = Res(player);
        if (res == null) return;
        int max = Entry.max_stack > 0 ? Entry.max_stack : 10;
        float dur = Entry.duration > 0f ? Entry.duration : 10f;
        res.AddStack(KEY_STACK, max, dur);
    }

    public override void Tick(float dt, PlayerController player)
    {
        var res   = Res(player);
        var stats = player?.RuntimeStats;
        if (res == null || stats == null) return;

        int stacks   = res.GetStack(KEY_STACK);
        float own    = Entry.value + stacks * Entry.value2;        // 기본 + 스택당
        float total  = own + res.GetFloat(KEY_SHOCK_AS);           // 감전 중 +8% 합산
        stats.SetSynergyDynamicAttackSpeed(total);                 // 무변동 시 내부에서 재계산 생략
    }

    public override void OnDeactivate()
    {
        CachedPlayer?.RuntimeStats?.SetSynergyDynamicAttackSpeed(0f);
        CachedPlayer?.RuneEffects?.Resources?.RemoveSlot(KEY_STACK);   // 정전기 스택 잔존 방지
    }
}

/// <summary>
/// 2단계 방전 — 스킬 사용 시 정전기 스택 전량 소모 → 스택당 공격력 4% 번개 즉발(가장 가까운 적).
/// value=스택당공격%(0.04). 소모량/대상을 RS에 기록해 감전(3단계)이 공유한다.
/// </summary>
public sealed class ElecDischargeEffect : ElectricRuneEffectBase
{
    private const float DISCHARGE_RANGE = 14f;
    private readonly List<MonsterBase> _buf = new();

    public override void OnSkillUsed(PlayerController player)
    {
        var res = Res(player);
        if (res == null) return;

        int stacks = res.ConsumeStack(KEY_STACK);
        res.SetRegister(KEY_CONSUMED, stacks);   // 감전이 지속시간 계산에 사용
        res.SetTarget(KEY_TARGET, null);
        if (stacks <= 0) return;

        int found = CombatQuery.GetNearbyEnemies(player.transform.position, DISCHARGE_RANGE, null, 1, _buf);
        if (found == 0) return;

        var target = _buf[0];
        res.SetTarget(KEY_TARGET, target.gameObject);

        float dmg = stacks * Entry.value * GetEffectiveAttack(player);
        if (dmg > 0f) CombatQuery.DealSynergyDamage(target, dmg, player.gameObject, element: RuneElement.Electric);

        // [실제 VFX] 방전 볼트(플레이어→대상) 번개 아크 + 대상 임팩트
        ElementVfxPlayer.PlayBeam(RuneElement.Electric, player.transform.position + Vector3.up, target.transform.position + Vector3.up);
        ElementVfxPlayer.PlayBurst(RuneElement.Electric, target.transform.position);
        // [가이드라인 비주얼] 방전 볼트(개발 전용)
        GuidelineVisual.Chain(player.transform.position + Vector3.up, target.transform.position + Vector3.up);
    }
}

/// <summary>
/// 3단계 감전 — 방전 시 대상 감전(마비). 기본 1초 + 소모 스택당 가산, 최대 3초.
/// 감전 동안 플레이어 공속 +8%(정전기가 합산). value=기본지속(1), value2=최대지속(3), value3=공속(0.08).
/// 방전(2단계)과 동일 OnSkillUsed에 반응하되, 활성 순서상 방전이 먼저 소모/대상 기록 후 실행된다.
/// </summary>
public sealed class ElecShockEffect : ElectricRuneEffectBase
{
    private const int STATIC_MAX_STACKS = 10;   // 지속 보간 분모
    private float _shockUntil;

    public override void OnSkillUsed(PlayerController player)
    {
        var res = Res(player);
        if (res == null) return;

        int consumed = res.GetRegister(KEY_CONSUMED);
        if (consumed <= 0) return;

        float perStack = (Entry.value2 - Entry.value) / STATIC_MAX_STACKS;        // (3-1)/10 = 0.2s
        float dur = Mathf.Min(Entry.value2, Entry.value + consumed * perStack);

        var targetGo = res.GetTarget(KEY_TARGET);
        var mb = targetGo != null ? targetGo.GetComponentInParent<MonsterBase>() : null;
        if (mb != null) mb.ApplyStun(dur);

        _shockUntil = Time.time + dur;
    }

    public override void Tick(float dt, PlayerController player)
    {
        var res = Res(player);
        if (res == null) return;
        res.SetFloat(KEY_SHOCK_AS, Time.time < _shockUntil ? Entry.value3 : 0f);
    }

    public override void OnDeactivate()
    {
        CachedPlayer?.RuneEffects?.Resources?.SetFloat(KEY_SHOCK_AS, 0f);
    }
}

/// <summary>
/// 4단계 과부하 — 공격 시 체인 라이트닝: 주변 최대 2명에게 적중 피해의 50%.
/// 주변에 없으면 같은 적을 2회 추가 타격. value=피해비율(0.5), value2=체인수(2).
/// 체인 피해는 TakeSynergyDamage 경로라 OnPostDealDamage를 안 타 재귀 없음.
/// </summary>
public sealed class ElecOverloadEffect : ElectricRuneEffectBase
{
    private const float CHAIN_RADIUS = 5f;
    private readonly List<MonsterBase> _buf = new();

    public override void OnHit(in HitInfo hit, PlayerController player)
    {
        if (hit.Target == null) return;

        float dmg = hit.Damage * Entry.value;
        if (dmg <= 0f) return;

        int chainCount = Entry.value2 > 0f ? (int)Entry.value2 : 2;
        Vector3 origin = hit.Target.transform.position;

        int found = CombatQuery.GetNearbyEnemies(origin, CHAIN_RADIUS, hit.Target, chainCount, _buf);
        if (found > 0)
        {
            for (int i = 0; i < _buf.Count; i++)
            {
                CombatQuery.DealSynergyDamage(_buf[i], dmg, player.gameObject, element: RuneElement.Electric);
                // [실제 VFX] 원점→대상 번개 아크 + 대상 임팩트 버스트
                ElementVfxPlayer.PlayBeam(RuneElement.Electric, origin + Vector3.up, _buf[i].transform.position + Vector3.up);
                ElementVfxPlayer.PlayBurst(RuneElement.Electric, _buf[i].transform.position);
                // [가이드라인 비주얼] 체인 라이트닝(개발 전용)
                GuidelineVisual.Chain(origin + Vector3.up, _buf[i].transform.position + Vector3.up);
            }
        }
        else
        {
            // 주변에 없으면 같은 적 추가 타격
            for (int i = 0; i < chainCount; i++)
                CombatQuery.DealSynergyDamage(hit.Target, dmg, player.gameObject, element: RuneElement.Electric);
            ElementVfxPlayer.PlayBurst(RuneElement.Electric, hit.Target.transform.position);   // [실제 VFX] 임팩트
        }
    }
}

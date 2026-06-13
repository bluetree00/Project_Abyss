using System;
using System.Collections.Generic;
using UnityEngine;
using RelicFairy.Monster;

// ═══════════════════════════════════════════════════════════════════════════
// 🔥 불 Fire — 화염 증폭 (4단계 누적, 자기완결 / 적 상태 = 점화)
//
// ST의 DoT(점화) + 만료 콜백(작열)을 처음 쓰는 검증 케이스.
// 공용 시스템: DM(즉발 방어무시), ST DoT/HasDot/onExpire, AQ(작열 광역).
//
// 데이터 키(실데이터): FireEmber / FireIgnite / FireBlaze / FireScorch. threshold=점유 셀 수(6/10/13/19).
// 점화 상태 id = "ignite" (다른 속성이 HasDot("ignite")로 질의 가능).
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>전기와 동일하게 RuneElementEffectBase 헬퍼(CachedPlayer/Res/GetEffectiveAttack) 사용.</summary>
public abstract class FireRuneEffectBase : RuneElementEffectBase
{
    protected const string IGNITE_ID = "ignite";
}

/// <summary>
/// 1단계 잔불 — 적중 시 공격력 8% 화염 즉발 추가(DM, 방어무시).
/// value=화염비율(0.08).
/// </summary>
public sealed class FireEmberEffect : FireRuneEffectBase
{
    public override void OnHit(in HitInfo hit, PlayerController player)
    {
        if (hit.Target == null) return;
        float dmg = Entry.value * GetEffectiveAttack(player);
        if (dmg > 0f) CombatQuery.DealSynergyDamage(hit.Target, dmg, player.gameObject);
    }
}

/// <summary>
/// 2단계 점화 — 잔불 N회(value3=5)마다 적중 대상에 점화 DoT 부여.
/// 점화 = 지속(duration=3)초간 초당 공격력 16%(value) × 3틱(max_stack). ST DoT 사용.
/// 작열(4단계) 활성 시, 점화 만료 콜백으로 광역 폭발을 예약한다.
/// </summary>
public sealed class FireIgniteEffect : FireRuneEffectBase
{
    private int _emberCount;

    public override void OnHit(in HitInfo hit, PlayerController player)
    {
        if (hit.Target == null) return;
        var mb = hit.Target.GetComponentInParent<MonsterBase>();
        if (mb == null) return;

        int period = Entry.value3 > 0f ? (int)Entry.value3 : 5;
        if (++_emberCount < period) return;
        _emberCount = 0;

        int atk        = GetEffectiveAttack(player);
        float perTick  = Entry.value * atk;                              // 0.16 × 공격력
        int tickCount  = Entry.max_stack > 0 ? Entry.max_stack : 3;
        float dur      = Entry.duration > 0f ? Entry.duration : 3f;
        float interval = dur / tickCount;                               // 3/3 = 1초

        // 4단계 작열 활성 시 만료 폭발 예약 (누적형 — 작열 활성이면 점화도 활성)
        Action onExpire = null;
        var disp = player.RuneEffects;
        if (disp != null && disp.IsActive("FireScorch"))
        {
            float scorchMul = disp.GetActive("FireScorch")?.Entry?.value ?? 1.5f;
            float boom      = scorchMul * atk;                          // 공150% 광역
            var   targetGo  = mb.gameObject;
            Vector3 snapPos = mb.transform.position;
            var   instigator = player.gameObject;
            onExpire = () => FireScorchEffect.Explode(targetGo, snapPos, boom, instigator);
        }

        mb.Status.ApplyDot(IGNITE_ID, perTick, interval, tickCount, player.gameObject, 1f, onExpire);
    }
}

/// <summary>
/// 3단계 폭염 — 점화 상태 적 공격 시 화염 피해 +35%(적중 피해의 35%를 방어무시 추가).
/// value=증폭비율(0.35). 상태 질의 = MonsterStatusReceiver.HasDot("ignite").
/// </summary>
public sealed class FireBlazeEffect : FireRuneEffectBase
{
    public override void OnHit(in HitInfo hit, PlayerController player)
    {
        if (hit.Target == null) return;
        var mb = hit.Target.GetComponentInParent<MonsterBase>();
        if (mb == null || !mb.Status.HasDot(IGNITE_ID)) return;

        float bonus = hit.Damage * Entry.value;
        if (bonus > 0f) CombatQuery.DealSynergyDamage(mb, bonus, player.gameObject);
    }
}

/// <summary>
/// 4단계 작열 — 점화 "만료" 시 공격력 150% 광역 폭발(+남은 DoT 합산).
/// 본 효과는 마커(IsActive/Entry 제공)이고, 실제 폭발은 점화(2단계)의 만료 콜백이 Explode를 호출한다.
/// value=폭발 배율(1.5).
/// </summary>
public sealed class FireScorchEffect : FireRuneEffectBase
{
    private const float EXPLOSION_RADIUS = 4f;
    private static readonly List<MonsterBase> s_explodeBuf = new();

    /// <summary>점화 만료 지점 광역 폭발. 대상 생존 시 현재 위치, 아니면 스냅 위치 기준.</summary>
    public static void Explode(GameObject target, Vector3 snapPos, float damage, GameObject instigator)
    {
        if (damage <= 0f) return;
        Vector3 origin = (target != null && target.activeInHierarchy) ? target.transform.position : snapPos;

        int n = CombatQuery.GetNearbyEnemies(origin, EXPLOSION_RADIUS, null, 16, s_explodeBuf);
        for (int i = 0; i < n; i++)
            CombatQuery.DealSynergyDamage(s_explodeBuf[i], damage, instigator);
    }
}

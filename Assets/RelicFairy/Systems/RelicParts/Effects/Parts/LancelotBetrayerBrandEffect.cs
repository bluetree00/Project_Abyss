using RelicFairy.Monster;
using UnityEngine;

/// <summary>
/// lancelot_betrayer_brand(배신자의 낙인) — 코어 진화. 심판의 일격에 맞은 적이 체력 25% 이하면 처형한다.
/// 보스는 처형되지 않고 광기가 즉시 가득 찬다.
///
/// Q 스킬(심판의 일격) 타격만 걸러 판정한다. 처형은 오버킬 즉사, 보스는 광기 MAX 적립.
/// </summary>
public sealed class LancelotBetrayerBrandEffect : RelicPartEffect
{
    private const float ExecuteThreshold = 0.25f;
    private const float BossFrenzyExtend = 0.4f;   // 보스 심판 타격당 광란 연장(초) — 다타라 누적

    public LancelotBetrayerBrandEffect() : base("lancelot_betrayer_brand") { }

    public override void OnHit(in HitInfo hit, PlayerController player)
    {
        if (hit.ActionType != WeaponActionType.QSkill || hit.Target == null) return;

        var mb = hit.Target.GetComponentInParent<MonsterBase>();
        if (mb == null || mb.IsDead) return;

        if (mb.Grade == MonsterGrade.Boss)
        {
            // 보스: 처형 불가 — 심판 타격마다 광란을 연장해 광기를 유지한다.
            // (광란 중 스택은 동결 MAX라 AddStack이 무효이므로 지속 연장이 "광기 가득"의 실효 구현.)
            (player.RelicBehavior as LancelotMadnessRelic)?.Madness?.ExtendFrenzy(BossFrenzyExtend);
        }
        else if (mb.EffectiveMaxHp > 0 && (float)mb.CurrentHp / mb.EffectiveMaxHp <= ExecuteThreshold)
        {
            ElementVfxPlayer.PlayBurst(RuneElement.Dark, mb.transform.position, 1.5f);   // 배신 낙인 처형
            mb.TakeDamage(mb.CurrentHp * 10f, player.gameObject, 0.3f);                  // 처형(오버킬)
        }
    }
}

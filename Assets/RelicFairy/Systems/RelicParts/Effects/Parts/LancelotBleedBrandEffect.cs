using UnityEngine;

/// <summary>
/// lancelot_bleed_brand(출혈의 낙인) — 심판의 일격에 맞은 적은 출혈 상태가 되어 지속 피해를 입는다.
///
/// 심판의 일격은 Q 스킬 경로(WeaponActionType.QSkill)로 들어오므로 그 타격만 걸러 출혈을 부여한다.
/// 출혈 dps는 그 타격 피해의 일부를 초당으로 환산한다(별도 스탯 의존 없음).
/// </summary>
public sealed class LancelotBleedBrandEffect : RelicPartEffect
{
    private const float BleedDpsRatio = 0.3f;   // 타격 피해 대비 초당 출혈
    private const float BleedDuration = 4f;

    public LancelotBleedBrandEffect() : base("lancelot_bleed_brand") { }

    public override void OnHit(in HitInfo hit, PlayerController player)
    {
        if (hit.ActionType != WeaponActionType.QSkill || hit.Target == null) return;
        MonsterBleed.Apply(hit.Target, hit.Damage * BleedDpsRatio, BleedDuration, player.gameObject);
    }
}

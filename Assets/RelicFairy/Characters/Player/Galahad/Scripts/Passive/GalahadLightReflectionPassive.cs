using UnityEngine;

/// <summary>
/// 갈라하드 패시브 — 빛의 반사.
/// 피격 시 두 가지 동시 적용:
///   1) 받는 피해 10% 경감 (캐릭터 방어 보너스로 일괄 적용 — Galahad.InitPassives 참조)
///   2) 방어력 비례 반사 피해를 공격자에게 돌려줌 (피격 시점 Apply)
/// </summary>
public class GalahadLightReflectionPassive : CharacterPassiveBase
{
    private const float ReflectRatio = 0.5f; // 방어력의 50% 반사

    public override string         PassiveName => "빛의 반사";
    public override PassiveTrigger Trigger     => PassiveTrigger.OnTakeDamage;

    public override bool CanApply(PlayerController ctrl, in PassiveContext ctx)
        => ctx.damage > 0f && ctx.attacker != null;

    public override void Apply(PlayerController ctrl, in PassiveContext ctx)
    {
        if (!ctx.attacker.TryGetComponent<IDamageable>(out var target)) return;

        float reflectDmg = ctrl.RuntimeStats.Defense * ReflectRatio;
        if (reflectDmg <= 0f) return;

        target.TakeDamage(reflectDmg, ctrl.gameObject, knockbackMultiplier: 0f);
    }
}

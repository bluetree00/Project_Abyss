using UnityEngine;

namespace Abyss.Monster
{
/// <summary>
/// 성난 버섯 몬스터.
/// 특수 상태: HP 50% 이하일 때 반복 발동 — 즉시 범위 포자 폭발 (MushroomAngrySporeBlastState).
/// </summary>
public class MushroomAngryMonster : MonsterBase
{
    public const string PrefabAddress = "MushroomAngry/MushroomAngry";
    protected override string ConfigAddress => "MushroomAngry/MushroomAngryConfig";
    protected override string DataAddress   => string.Empty;

    public override void TakeDamage(float amount, GameObject instigator,
                                     float knockbackMultiplier = 1f,
                                     ElementType element = ElementType.None,
                                     float elementAmount = 0f)
    {
        if (_runtime == null) return;

        if (_runtime.IsDormant || _runtime.IsReturning)
            _runtime.HasBeenAttacked = true;

        base.TakeDamage(amount, instigator, knockbackMultiplier, element, elementAmount);
    }
}
}

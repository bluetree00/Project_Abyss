using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 선인장 몬스터.
/// 특수 상태: 피격 시마다 발동 — 즉시 주변 가시 반격 범위 공격 (CactusThornRetaliateState).
/// OnDamageTaken override로 매 피격 시 특수 상태를 트리거한다.
/// </summary>
public class CactusMonster : MonsterBase
{
    public const string PrefabAddress = "Cactus/Cactus";
    protected override string ConfigAddress => "Cactus/CactusConfig";
    protected override string DataAddress   => string.Empty;

    public override void TakeDamage(float amount, GameObject instigator,
                                     float knockbackMultiplier = 1f,
                                     ElementType element = ElementType.None,
                                     float elementAmount = 0f)
    {
        if (_runtime == null) return;

        if (_runtime.IsDormant)
            _runtime.HasBeenAttacked = true;

        base.TakeDamage(amount, instigator, knockbackMultiplier, element, elementAmount);
    }

    protected override void OnDamageTaken()
    {
        if (_runtime != null && _runtime.IsDormant)
            return;

        base.OnDamageTaken();
        if (_fsm?.CurrentConstraints == SpecialStateConstraint.None)
        {
            var s = GetSpecialState(0);
            if (s != null) _fsm.ChangeState(s);
        }
    }
}
}

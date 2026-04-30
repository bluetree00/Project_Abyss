using UnityEngine;

namespace Abyss.Monster
{
/// <summary>
/// Mimic chest monster. Patrol behavior is driven through config state
/// overrides so it can be controlled from the inspector.
/// </summary>
public class ChestMonster : MonsterBase
{
    public const string PrefabAddress = "ChestMonster/ChestMonster";
    protected override string ConfigAddress => "ChestMonster/ChestMonsterConfig";
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

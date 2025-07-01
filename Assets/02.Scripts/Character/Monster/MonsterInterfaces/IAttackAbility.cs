using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface IAttackAbility : IMonsterAbility
{
    Define.AttackStyle Style { get; }
    Define.AttackPurpose Purpose { get; }
}

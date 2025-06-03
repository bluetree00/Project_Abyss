using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface IMoveAbility<T> where T : CharacterBase
{
    void Move(T CharacterBase, Vector3 direction);
}

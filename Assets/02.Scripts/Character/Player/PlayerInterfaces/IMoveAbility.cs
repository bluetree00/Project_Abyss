using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface IMoveAbility<T> where T : CharacterController
{
    void Move(T controller, Vector3 direction);
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface IMoveAbility<T> where T : PlayerController
{
    void Move(T owner, Vector3 direction);
    void StepClimb(T owner, Vector3 moveDir);
}
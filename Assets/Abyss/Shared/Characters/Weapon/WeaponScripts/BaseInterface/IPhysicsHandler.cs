using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface IPhysicsHandler
{
    void ExecutePhysics(WeaponAbilitySO ability, Transform owner);
}
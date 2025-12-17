using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface IColliderHandler
{
    void ExecuteColliders(WeaponAbilitySO ability, int comboIndex, Transform owner, WeaponColliderPackageSO colliderPackage);
}
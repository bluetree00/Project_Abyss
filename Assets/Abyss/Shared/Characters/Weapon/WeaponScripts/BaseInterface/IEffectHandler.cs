using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface IEffectHandler
{
    void ExecuteEffects(WeaponAbilitySO ability, int comboIndex, Transform owner, WeaponEffectPackageSO effectPackage);
}
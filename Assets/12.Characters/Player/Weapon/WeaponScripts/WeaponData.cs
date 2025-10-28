// WeaponData.cs
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 런타임 Weapon 데이터
/// - ScriptableObject WeaponSO 기반 생성
/// - Effect / Collider 데이터는 런타임 복사본 사용
/// </summary>
[Serializable]
public class WeaponData
{
    public string WeaponDisplayKey;
    public string displayName;
    public string weaponPrefabKey;
    public string iconKey;
    public float baseAttack;
    public float baseDefense;

    public int groundEndCount;
    public int airEndCount;

    public WeaponAnimationSetSO animationSet;

    public WeaponEffectPackageSO effectPackage;
    public WeaponColliderPackageSO colliderPackage;

    public WeaponAbilitySetSO abilitySet;

    // Runtime copies of effect/collider data (deep copies)
    public List<WeaponEffectData> effectDataList;
    public List<WeaponColliderData> colliderDataList;

    public WeaponData(WeaponSO so)
    {
        if (so == null) throw new ArgumentNullException(nameof(so));

        WeaponDisplayKey = so.weaponDisplayKey;
        weaponPrefabKey = so.weaponKey;
        displayName = so.displayName;
        iconKey = so.iconKey;
        baseAttack = so.baseAttack;
        baseDefense = so.baseDefense;

        groundEndCount = so.groundEndCount;
        airEndCount = so.airEndCount;

        animationSet = so.animationSet;
        abilitySet = so.abilitySet;

        effectPackage = so.effectPackage;
        colliderPackage = so.colliderPackage;

        // ----------------- Effect 데이터 생성 -----------------
        effectDataList = new List<WeaponEffectData>();
        if (effectPackage != null)
        {
            foreach (var action in effectPackage.actions)
            {
                foreach (var effectSO in action.effects)
                {
                    if (effectSO != null)
                        effectDataList.Add(new WeaponEffectData(effectSO));
                }
            }
        }

        // ----------------- Collider 데이터 생성 -----------------
        colliderDataList = new List<WeaponColliderData>();
        if (colliderPackage != null)
        {
            foreach (var action in colliderPackage.actions)
            {
                foreach (var indexEntry in action.effectIndices)
                {
                    foreach (var stepEntry in indexEntry.steps)
                    {
                        if (stepEntry.colliderSO != null)
                            colliderDataList.Add(new WeaponColliderData(stepEntry.colliderSO));
                    }
                }
            }
        }
    }

    /// <summary>
    /// 서버에서 받은 런타임 데이터로 effect / collider override 적용
    /// </summary>
    public void ApplyServerOverrides(Dictionary<string, WeaponEffectData> effectOverrides,
                                     Dictionary<string, WeaponColliderData> colliderOverrides)
    {
        if (effectOverrides != null)
        {
            for (int i = 0; i < effectDataList.Count; i++)
            {
                var d = effectDataList[i];
                if (effectOverrides.TryGetValue(d.id, out var o))
                    d.ApplyServerData(o);
            }
        }

        if (colliderOverrides != null)
        {
            for (int i = 0; i < colliderDataList.Count; i++)
            {
                var d = colliderDataList[i];
                if (colliderOverrides.TryGetValue(d.id, out var o))
                    d.ApplyServerData(o);
            }
        }
    }
}

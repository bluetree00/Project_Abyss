using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WeaponContainer : MonoBehaviour
{
    public WeaponData data;

    [SerializeField]
    private Dictionary<WeaponData.WeaponType, Dictionary<WeaponData.WeaponRarity, WeaponData>> type_dic;

    [SerializeField]
    private Dictionary<WeaponData.WeaponRarity, WeaponData> rarity_dic;
}

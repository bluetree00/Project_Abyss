// WeaponInstance.cs (attach on weapon prefab)
using UnityEngine;

public class WeaponInstance : MonoBehaviour
{
    private WeaponData _data;

    public void Initialize(WeaponData data)
    {
        _data = data;
        // Optionally create visual/effects placeholders here
    }

    public void OnEquip()
    {
        // Play equip animation/visual
    }

    public void OnUnequip()
    {
        // Cleanup
    }

}

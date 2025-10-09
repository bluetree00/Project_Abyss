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

    public void ExecuteStep(WeaponAbilitySO.AbilityStep step)
    {
        if (_data == null) return;

        // Example: spawn effect(s) and collider(s) by payloadKey
        // effect spawn
        if (!string.IsNullOrEmpty(step.payloadKey))
        {
            // Find effect runtime data by id
            var eff = _data.effectDataList.Find(e => e.id == step.payloadKey);
            if (eff != null)
            {
             //   EffectComponent.Spawn(eff, transform);
            }

            var col = _data.colliderDataList.Find(c => c.id == step.payloadKey);
            if (col != null)
            {
             //   ColliderComponent.Spawn(col, transform);
            }
        }

        // Damage multipliers etc. can be applied when collider hits
    }
}

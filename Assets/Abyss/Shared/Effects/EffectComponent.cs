using UnityEngine;

public class EffectComponent : MonoBehaviour
{
    public WeaponEffectData effectData;

    public void Initialize(WeaponEffectSO so)
    {
        effectData = new WeaponEffectData(so);
    }

    public void PlayEffect()
    {
        // RuntimeData 기반으로 Prefab Instantiate 등
        Debug.Log($"Play effect {effectData.id} at {effectData.attachPoint}");
    }
}

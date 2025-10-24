using UnityEngine;

public class WeaponEffectHandler
{
    private PlayerController _player;

    public WeaponEffectHandler(PlayerController player)
    {
        _player = player;
    }

    public void PlayEffect(WeaponAnimGroup group, WeaponActionType actionType, int effectIndex, int step = 0)
    {
        if (_player == null)
        {
            Debug.LogError("[WeaponEffectHandler] Player reference is null!");
            return;
        }

        var weaponData = _player.WeaponManager?.CurrentWeaponData;
        var handTransform = _player.handTransform;

        if (weaponData == null || weaponData.effectPackage == null)
            return;

        var effectSO = weaponData.effectPackage.GetEffect(group, actionType, effectIndex, step);
        if (effectSO == null)
        {
            Debug.LogWarning($"EffectSO not found: {group}, {actionType}, {effectIndex}, {step}");
            return;
        }

        if (handTransform == null)
        {
            Debug.LogError("[WeaponEffectHandler] Hand transform is null!");
            return;
        }

        GameObject obj = Managers.ObjectPooler.SpawnFromPool(
            effectSO.prefabKey,
            handTransform.position + effectSO.spawnOffset,
            Quaternion.Euler(effectSO.defaultRotation)
        );
    }
}

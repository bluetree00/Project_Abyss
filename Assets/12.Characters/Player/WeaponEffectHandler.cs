using UnityEngine;

public class WeaponEffectHandler
{
    private WeaponData _weaponData;
    private Transform _handTransform; // 플레이어 손 위치 등

    /// <summary>
    /// 공격 시 효과 생성
    /// </summary>
    public void PlayEffect(WeaponAnimGroup group, WeaponActionType actionType, int effectIndex, int step = 0)
    {
        if (_weaponData == null || _weaponData.effectPackage == null)
            return;

        var effectSO = _weaponData.effectPackage.GetEffect(group, actionType, effectIndex, step);
        if (effectSO == null)
            return;

            GameObject obj = Managers.ObjectPooler.SpawnFromPool(
            effectSO.prefabKey,_handTransform.position + effectSO.spawnOffset,
            Quaternion.Euler(effectSO.defaultRotation)
        );


        // 필요한 초기화가 있으면 IPooledObject에서 처리
        // (ex: Damage 설정, Owner 설정 등)
    }
}

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
        if (_player == null) return;

        var weaponData = _player.WeaponManager?.CurrentWeaponData;
        if (weaponData == null) return;

        var ability = weaponData.abilitySet.GetAbility(actionType, effectIndex);
        var abilitySteps = ability?.GetSteps(step);
        if (abilitySteps == null || abilitySteps.Count == 0) return;

        var handTransform = _player.handTransform;
        if (handTransform == null) return;

        foreach (var s in abilitySteps)
        {
            // 1. Effect 생성 (패키지에서 Prefab 가져오기)
            if (s.effect != null)
            {
                var e = s.effect;
                var effectSO = weaponData.effectPackage.GetEffect(group, actionType, effectIndex, step);
                if (effectSO == null)
                {
                    Debug.LogWarning($"EffectSO not found: {group}, {actionType}, {effectIndex}, {step}");
                    continue;
                }

                GameObject effectObj = Managers.ObjectPooler.SpawnFromPool(
                    effectSO.prefabKey,
                    handTransform.position + e.positionOffset,
                    Quaternion.Euler(e.rotationEuler)
                );

                effectObj.transform.localScale *= e.scaleMultiplier;

                // 필요하면 Behavior 실행
                e.behavior?.ApplyEffectBehavior(effectObj, _player.transform);
            }

            // 2. Collider 생성
            if (s.collider != null)
            {
                var c = s.collider;
                GameObject colliderObj;

                if (!string.IsNullOrEmpty(c.colliderPrefabKey))
                {
                    // Prefab/Addressable에서 가져오기
                    colliderObj = Managers.ObjectPooler.SpawnFromPool(
                        c.colliderPrefabKey,
                        handTransform.position + c.positionOffset,
                        Quaternion.Euler(c.rotationEuler)
                    );
                }
                else
                {
                    // ColliderStep 데이터 기반 생성
                    colliderObj = new GameObject("RuntimeCollider");
                    colliderObj.transform.position = handTransform.position + c.positionOffset;
                    colliderObj.transform.rotation = Quaternion.Euler(c.rotationEuler);

                    var col = colliderObj.AddComponent<BoxCollider>(); // 필요에 따라 형태 변경
                    col.size = Vector3.one * c.sizeMultiplier;

                    var colliderInstance = colliderObj.AddComponent<ColliderInstance>();
                    colliderInstance.damage = c.damage;
                    colliderInstance.hitInterval = c.hitInterval;

                    // 필요하면 Behavior 실행
                    c.behavior?.ApplyColliderBehavior(colliderObj, _player.transform);
                }
            }
        }
    }
}

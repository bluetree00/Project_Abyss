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
            // 1. Effect 생성 (어빌리티 키 사용)
            if (s.effect != null)
            {
                var e = s.effect;
                if (!string.IsNullOrEmpty(e.payloadKey))
                {
                    GameObject effectObj = Managers.ObjectPooler.SpawnFromPool(
                        e.payloadKey,
                        handTransform.position + e.positionOffset,
                        Quaternion.Euler(e.rotationEuler)
                    );
                    effectObj.transform.localScale *= e.scaleMultiplier;

                    e.behavior?.ApplyEffectBehavior(effectObj, _player.transform);
                }
            }

            // 2. Collider 생성 (어빌리티 키 사용)
            if (s.collider != null)
            {
                var c = s.collider;
                GameObject colliderObj;

                if (!string.IsNullOrEmpty(c.colliderPrefabKey))
                {
                    colliderObj = Managers.ObjectPooler.SpawnFromPool(
                        c.colliderPrefabKey,
                        handTransform.position + c.positionOffset,
                        Quaternion.Euler(c.rotationEuler)
                    );
                }
                else
                {
                    colliderObj = new GameObject("RuntimeCollider");
                    colliderObj.transform.position = handTransform.position + c.positionOffset;
                    colliderObj.transform.rotation = Quaternion.Euler(c.rotationEuler);

                    var col = colliderObj.AddComponent<BoxCollider>();
                    col.size = Vector3.one * c.sizeMultiplier;

                    var colliderInstance = colliderObj.AddComponent<ColliderInstance>();
                    colliderInstance.damage = c.damage;
                    colliderInstance.hitInterval = c.hitInterval;
                }

                c.behavior?.ApplyColliderBehavior(colliderObj, _player.transform);
            }
        }
    }

}

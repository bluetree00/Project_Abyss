using UnityEngine;

public class WeaponEffectHandler
{
    private PlayerController _player;

    public WeaponEffectHandler(PlayerController player)
    {
        _player = player;
    }

    public void PlayEffect(WeaponActionType actionType, int effectIndex, int step = 0)
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

                    // EffectBehaviour가 없으면 자동으로 붙임
                    var effectBehaviour = effectObj.GetComponent<EffectBehaviour>();
                    if (effectBehaviour == null)
                    {
                        effectBehaviour = effectObj.AddComponent<EffectBehaviour>();
                    }

                    EffectBehaviorSO so = e.behavior;

                    effectBehaviour.Initialize(so, _player.transform, e.lifeTimeMultiplier);
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

                    switch (c.shape)
                    {
                        case WeaponAbilitySO.ColliderShape.Box:
                            var box = colliderObj.AddComponent<BoxCollider>();
                            box.size = Vector3.one * c.sizeMultiplier;
                            break;

                        case WeaponAbilitySO.ColliderShape.Sphere:
                            var sphere = colliderObj.AddComponent<SphereCollider>();
                            sphere.radius = 0.5f * c.sizeMultiplier;
                            break;

                        case WeaponAbilitySO.ColliderShape.Capsule:
                            var capsule = colliderObj.AddComponent<CapsuleCollider>();
                            capsule.radius = 0.5f * c.sizeMultiplier;
                            capsule.height = 2f * c.sizeMultiplier;
                            break;
                    }

                    var colliderInstance = colliderObj.AddComponent<ColliderInstance>();
                    colliderInstance.damage = c.damage;
                    colliderInstance.hitInterval = c.hitInterval;
                    colliderInstance.owner = _player.gameObject;
                    colliderInstance.actionType = actionType;
                    colliderInstance.payloadKey = c.payloadKey;
                    colliderInstance.knockbackMultiplier = c.durationMultiplier;
                    colliderInstance.duration = c.duration;
                    colliderInstance.gameObject.SetActive(true);
                }

                c.behavior?.ApplyColliderBehavior(colliderObj, _player.transform);
            }
        }
    }
}

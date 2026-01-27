using UnityEngine;
using Cysharp.Threading.Tasks;

public class WeaponEffectHandler
{
    private PlayerController _player;

    public WeaponEffectHandler(PlayerController player)
    {
        _player = player;
    }

    public async UniTaskVoid PlayEffect(WeaponActionType actionType,int currentComboIndex,int step = 0)
    {
        if (_player == null) return;

        var weaponData = _player.WeaponManager?.CurrentWeaponData;
        if (weaponData == null) return;

        var ability = weaponData.abilitySet.GetAbility(actionType, currentComboIndex);
        var abilitySteps = ability?.GetSteps(step);
        if (abilitySteps == null || abilitySteps.Count == 0) return;

        var handTransform = _player.handTransform;
        if (handTransform == null) return;

        foreach (var s in abilitySteps)
        {
            // =========================
            // 1. Effect (Pool Spawn)
            // =========================
            if (s.effect != null)
            {
                var e = s.effect;
                if (!string.IsNullOrEmpty(e.payloadKey))
                {
                    GameObject effectObj = await Managers.ObjectPooler.SpawnAsync(
                        e.payloadKey,
                        ObjectPoolerManager.PoolType.Effect,
                        _player.transform.position + e.positionOffset,
                        Quaternion.Euler(e.rotationEuler)
                    );

                    // 🔥 풀 재사용 대응 (누적 방지)
                    effectObj.transform.localScale = Vector3.one * e.scaleMultiplier;

                    var effectBehaviour = effectObj.GetComponent<EffectBehaviour>();
                    if (effectBehaviour == null)
                    {
                        effectBehaviour = effectObj.AddComponent<EffectBehaviour>();
                    }

                    effectBehaviour.Initialize(
                        e.behavior,
                        _player.transform,
                        e.lifeTimeMultiplier
                    );
                }
            }

            // =========================
            // 2. Collider (Pool Spawn)
            // =========================
            if (s.collider != null)
            {
                var c = s.collider;
                GameObject colliderObj;

                if (!string.IsNullOrEmpty(c.colliderPrefabKey))
                {
                    colliderObj = await Managers.ObjectPooler.SpawnAsync(
                        c.colliderPrefabKey,
                        ObjectPoolerManager.PoolType.Effect,
                        handTransform.position + c.positionOffset,
                        Quaternion.Euler(c.rotationEuler)
                    );


                    // 🔥 풀 재사용 대응
                    var colliderInstance = colliderObj.GetComponent<ColliderInstance>();
                    if (colliderInstance == null)
                    {
                        colliderInstance = colliderObj.AddComponent<ColliderInstance>();
                    }

                    colliderInstance.damage = c.damage;
                    colliderInstance.hitInterval = c.hitInterval;
                    colliderInstance.owner = _player.gameObject;
                    colliderInstance.actionType = actionType;
                    colliderInstance.payloadKey = c.payloadKey;
                    colliderInstance.knockbackMultiplier = c.durationMultiplier;
                    colliderInstance.duration = c.duration;

                    colliderObj.SetActive(true);
                }
                else
                {
                    // 기존 Runtime Collider 로직 유지
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

                    colliderObj.SetActive(true);
                }

                c.behavior?.ApplyColliderBehavior(colliderObj, _player.transform);
            }
        }
    }
}

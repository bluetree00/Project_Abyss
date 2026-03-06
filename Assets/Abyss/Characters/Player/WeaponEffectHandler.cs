using UnityEngine;
using Cysharp.Threading.Tasks;

public class WeaponEffectHandler
{
    private PlayerController _player;

    public WeaponEffectHandler(PlayerController player)
    {
        _player = player;
    }

    public async UniTaskVoid PlayEffect(WeaponActionType actionType, int currentComboIndex, int step = 0)
    {
        if (_player == null) return;

        if (_player.WeaponManager == null) return;
        var weaponData = _player.WeaponManager.CurrentWeaponData;
        if (weaponData == null || weaponData.abilitySet == null) return;

        var ability = weaponData.abilitySet.GetAbility(actionType, currentComboIndex);
        if (ability == null) return;
        var abilitySteps = ability.GetSteps(step);
        if (abilitySteps == null || abilitySteps.Count == 0) return;

        // await 이전에 Transform 캡처 (await 중 플레이어 소멸 대비)
        var playerTransform = _player.transform;
        var handTransform = _player.handTransform;
        if (handTransform == null) return;

        foreach (var s in abilitySteps)
        {
            // oneShot: 이미 발화된 스텝이면 건너뜀
            if (s.oneShot)
            {
                if (s.triggeredThisActivation) continue;
                s.triggeredThisActivation = true;
            }

            // rotateToMouse: 이 스텝 시작 시 마우스 방향으로 회전
            if (s.rotateToMouse)
                _player.RotateTowardsMousePosition();

            // selfMovement: 플레이어 로컬 방향으로 이동력 적용
            if (s.selfMovement != Vector3.zero && _player.Rigid != null)
                _player.Rigid.AddForce(
                    playerTransform.TransformDirection(s.selfMovement),
                    ForceMode.VelocityChange
                );

            // =========================
            // 1. Effect (Pool Spawn)
            // =========================
            if (s.effect != null && !string.IsNullOrEmpty(s.effect.payloadKey))
            {
                var e = s.effect;
                GameObject effectObj = await Managers.ObjectPooler.SpawnAsync(
                    e.payloadKey,
                    ObjectPoolerManager.PoolType.Effect,
                    playerTransform.position + e.positionOffset,
                    Quaternion.Euler(e.rotationEuler)
                );

                if (_player == null || effectObj == null) return;

                effectObj.transform.localScale = Vector3.one * e.scaleMultiplier;
                if (!effectObj.TryGetComponent<EffectBehaviour>(out var effectBehaviour))
                    effectBehaviour = effectObj.AddComponent<EffectBehaviour>();
                effectBehaviour.Initialize(e.behavior, playerTransform, e.lifeTimeMultiplier);
            }

            // =========================
            // 2. Collider (Spawned 모드만 — Trail은 WeaponTrailDetector가 처리)
            // =========================
            if (s.collider != null && s.collider.mode == WeaponAbilitySO.ColliderMode.Spawned)
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

                    if (_player == null || colliderObj == null) return;
                }
                else
                {
                    // prefabKey 없을 때 런타임 생성 (임시 fallback)
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
                }

                if (!colliderObj.TryGetComponent<ColliderInstance>(out var colliderInstance))
                    colliderInstance = colliderObj.AddComponent<ColliderInstance>();
                // baseDamage 우선, 없으면 ColliderStep.damage 사용
                colliderInstance.damage = s.baseDamage > 0f ? s.baseDamage : c.damage;
                colliderInstance.hitInterval = c.hitInterval;
                colliderInstance.owner = _player.gameObject;
                colliderInstance.actionType = actionType;
                colliderInstance.payloadKey = c.payloadKey;
                colliderInstance.knockbackMultiplier = s.knockbackMultiplier;
                colliderInstance.duration = c.duration;
                colliderObj.SetActive(true);

                if (c.behavior != null)
                    c.behavior.ApplyColliderBehavior(colliderObj, playerTransform);
            }
        }
    }
}

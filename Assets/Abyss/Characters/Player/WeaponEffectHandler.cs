using UnityEngine;
using Cysharp.Threading.Tasks;

public class WeaponEffectHandler
{
    private PlayerController _player;

    public WeaponEffectHandler(PlayerController player)
    {
        _player = player;
    }

    public async UniTaskVoid PlayEffect(WeaponActionType actionType, int currentComboIndex, int step = 0, AbilityExecution execution = null)
    {
        if (_player == null) return;
        if (_player.WeaponManager == null) return;

        var weaponData = _player.WeaponManager.CurrentWeaponData;
        if (weaponData == null || weaponData.abilitySet == null) return;

        var ability = weaponData.abilitySet.GetAbility(actionType, currentComboIndex);
        if (ability == null) return;

        var abilitySteps = ability.GetSteps(step);
        if (abilitySteps == null || abilitySteps.Count == 0) return;

        var playerTransform = _player.transform;
        var handTransform = _player.handTransform;
        if (handTransform == null) return;

        foreach (var s in abilitySteps)
        {
            if (s.oneShot && (execution == null || !execution.TryFireOneShot(s))) continue;

            if (s.rotateToMouse)
                _player.RotateTowardsMousePosition();

            if (s.selfMovement != Vector3.zero && _player.Rigid != null)
                _player.Rigid.AddForce(
                    playerTransform.TransformDirection(s.selfMovement),
                    ForceMode.VelocityChange
                );

            bool colliderHandledByCombined = false;

            // =====================================================
            // 1. 이펙트 스폰
            //    스폰 후 ColliderInstance가 있으면 → 합쳐진 프리팹 모드
            //    데미지 데이터를 주입하고 별도 콜라이더 스폰은 생략
            // =====================================================
            if (s.effect != null && !string.IsNullOrEmpty(s.effect.payloadKey))
            {
                var e = s.effect;
                var effectObj = await Managers.ObjectPooler.SpawnAsync(
                    e.payloadKey,
                    ObjectPoolerManager.PoolType.Effect,
                    playerTransform.TransformPoint(e.positionOffset),
                    Quaternion.Euler(e.rotationEuler)
                );

                if (_player == null || effectObj == null) return;

                effectObj.transform.localScale = Vector3.one * e.scaleMultiplier;

                if (!effectObj.TryGetComponent<EffectBehaviour>(out var effectBehaviour))
                {
                    Debug.LogWarning($"[WeaponEffectHandler] '{effectObj.name}'에 EffectBehaviour가 없습니다. 프리팹을 확인하세요.");
                    Managers.ObjectPooler.Despawn(effectObj);
                    continue;
                }
                effectBehaviour.Initialize(e.behavior, playerTransform, e.lifeTimeMultiplier);

                // 합쳐진 프리팹: 같은 오브젝트에 ColliderInstance가 있으면 데이터 주입
                if (s.collider != null && effectObj.TryGetComponent<ColliderInstance>(out var embedded))
                {
                    SetupColliderInstance(embedded, s, actionType);
                    colliderHandledByCombined = true;
                }

                execution?.RegisterEffect(effectObj);
            }

            // =====================================================
            // 2. 별도 콜라이더 스폰
            //    합쳐진 프리팹에서 이미 처리된 경우 생략
            //    Trail 모드는 WeaponTrailDetector가 처리하므로 생략
            // =====================================================
            if (!colliderHandledByCombined &&
                s.collider != null &&
                s.collider.mode == WeaponAbilitySO.ColliderMode.Spawned)
            {
                var c = s.collider;
                GameObject colliderObj;

                if (!string.IsNullOrEmpty(c.colliderPrefabKey))
                {
                    colliderObj = await Managers.ObjectPooler.SpawnAsync(
                        c.colliderPrefabKey,
                        ObjectPoolerManager.PoolType.Effect,
                        handTransform.TransformPoint(c.positionOffset),
                        Quaternion.Euler(c.rotationEuler)
                    );

                    if (_player == null || colliderObj == null) return;
                }
                else
                {
                    // prefabKey 없을 때 런타임 생성 (임시 fallback)
                    colliderObj = new GameObject("RuntimeCollider");
                    colliderObj.transform.position = handTransform.TransformPoint(c.positionOffset);
                    colliderObj.transform.rotation = Quaternion.Euler(c.rotationEuler);

                    switch (c.shape)
                    {
                        case WeaponAbilitySO.ColliderShape.Box:
                            var box = colliderObj.AddComponent<BoxCollider>();
                            box.isTrigger = true;
                            box.size = Vector3.one * c.sizeMultiplier;
                            break;
                        case WeaponAbilitySO.ColliderShape.Sphere:
                            var sphere = colliderObj.AddComponent<SphereCollider>();
                            sphere.isTrigger = true;
                            sphere.radius = 0.5f * c.sizeMultiplier;
                            break;
                        case WeaponAbilitySO.ColliderShape.Capsule:
                            var capsule = colliderObj.AddComponent<CapsuleCollider>();
                            capsule.isTrigger = true;
                            capsule.radius = 0.5f * c.sizeMultiplier;
                            capsule.height = 2f * c.sizeMultiplier;
                            break;
                    }
                }

                if (!colliderObj.TryGetComponent<ColliderInstance>(out var colliderInstance))
                {
                    Debug.LogWarning($"[WeaponEffectHandler] '{colliderObj.name}'에 ColliderInstance가 없습니다. 프리팹을 확인하세요.");
                    Managers.ObjectPooler.Despawn(colliderObj);
                    continue;
                }

                SetupColliderInstance(colliderInstance, s, actionType);
                colliderObj.SetActive(true);
                execution?.RegisterCollider(colliderObj);

                if (c.behavior != null)
                    c.behavior.ApplyColliderBehavior(colliderObj, playerTransform);
            }
        }
    }

    private void SetupColliderInstance(ColliderInstance ci, WeaponAbilitySO.AbilityStep s, WeaponActionType actionType)
    {
        var c = s.collider;
        ci.damage              = s.baseDamage > 0f ? s.baseDamage : c.damage;
        ci.knockbackMultiplier = s.knockbackMultiplier;
        ci.hitInterval         = c.hitInterval;
        ci.owner               = _player.gameObject;
        ci.actionType          = actionType;
        ci.payloadKey          = c.colliderPrefabKey;
        ci.duration            = c.duration;
    }
}

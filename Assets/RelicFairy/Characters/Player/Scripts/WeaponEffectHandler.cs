using UnityEngine;
using Cysharp.Threading.Tasks;

public class WeaponEffectHandler
{
    // 강공 차지 원형 AoE 반경 배율 범위(차지레벨 0→1 보간). Phase1 상수 — 밸런싱 시 weaponData 승격 가능.
    private const float HeavyChargeMinScale = 0.7f;
    private const float HeavyChargeMaxScale = 1.8f;

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
            // 1. 이펙트 스폰 — socket/space 기반
            // =====================================================
            if (s.effect != null && !string.IsNullOrEmpty(s.effect.payloadKey))
            {
                var e = s.effect;

                // 소켓 트랜스폼 결정
                Transform socketTransform = ResolveSocket(e.socket, playerTransform, handTransform);

                // 스폰 위치/회전 계산
                Vector3 spawnPos = socketTransform.TransformPoint(e.positionOffset);
                Quaternion spawnRot = socketTransform.rotation * Quaternion.Euler(e.rotationEuler);

                var effectObj = await Managers.ObjectPooler.SpawnAsync(
                    e.payloadKey,
                    ObjectPoolerManager.PoolType.Effect,
                    spawnPos,
                    spawnRot
                );

                if (_player == null || effectObj == null) return;

                // 강공(GroundHeavy)만 차지레벨로 원형 AoE 반경 스케일 — 스피어 콜라이더가 함께 커진다.
                float chargeScale = actionType == WeaponActionType.GroundHeavy
                    ? Mathf.Lerp(HeavyChargeMinScale, HeavyChargeMaxScale, _player.HeavyChargeLevel01)
                    : 1f;
                effectObj.transform.localScale = Vector3.one * e.scaleMultiplier * chargeScale;

                // Local space → 소켓에 부착
                if (e.space == WeaponAbilitySO.EffectSpace.Local)
                {
                    effectObj.transform.SetParent(socketTransform, true);
                }

                // BasicArrow 발사체 처리
                if (effectObj.TryGetComponent<BasicArrow>(out var arrow))
                {
                    Vector3 fireDir;

                    // 조준 방향은 '명령된' 정면(AimForward)에서 온다. transform.forward는 회전이
                    // FixedUpdate+보간을 거친 결과라 최대 한 물리 스텝 뒤처지고, 공격속도가 빠르면
                    // 그 지연 안에 발사가 끼어 화살이 겨냥한 곳보다 이전 방향으로 나갔다.
                    Vector3 aimForward = _player.AimForward;

                    // 총구 위치도 같은 기준을 써야 한다 — 위치는 옛 방향, 방향은 새 방향이면
                    // 화살이 몸 옆에서 비스듬히 튀어나온다.
                    Vector3 firePos = playerTransform.position + aimForward * 1f + Vector3.up * 1f
                                    + Quaternion.LookRotation(aimForward) * e.positionOffset;

                    if (!_player.IsGrounded())
                    {
                        // 공중: 마우스가 가리키는 지면 지점을 향해 발사
                        Vector3 targetPoint = GetMouseWorldPoint();
                        fireDir = (targetPoint - firePos).normalized;
                    }
                    else
                    {
                        // 지상: 수평 조준 방향
                        fireDir = aimForward;
                        fireDir.y = 0f;
                        fireDir.Normalize();
                    }

                    effectObj.transform.position = firePos;
                    effectObj.transform.rotation = Quaternion.LookRotation(fireDir);

                    // 화살은 '원거리' 공격력으로 계산한다. 예전엔 AttackPower(= Max(근접,원거리))라
                    // 근접 스탯이 더 높은 빌드에서 화살 데미지가 근접 스탯을 타고 올라갔다(활 빌드 오염).
                    // 근접/콜라이더 경로(SetupColliderInstance·합쳐진 프리팹)와 동일하게 무기 종류로 스탯을 고른다.
                    var arrowKind = _player.WeaponManager?.CurrentWeaponData?.weaponType.GetAttackStatKind()
                                    ?? AttackStatKind.Ranged;
                    float dmg = DamageFormula.Calculate(s.baseDamage, _player.RuntimeStats.GetEffectiveAttack(arrowKind));

                    // 발사는 전부 단일 퍼널을 지난다 — 원거리 파츠·아이템·방버프가 여기서 한 번에 얹힌다.
                    // (예전엔 부채꼴 15°·추가탄 ±8°/80ms가 각자 스폰 코드를 복제해 소스가 4갈래로 흩어져 있었다.)
                    var req = ProjectileRequest.Create(
                        e.payloadKey, firePos, fireDir, dmg, _player.gameObject,
                        _player.WeaponManager != null ? _player.WeaponManager.CurrentSlotIndex : 0,
                        e.scaleMultiplier);

                    CombatSpawner.SpawnProjectile(ref req, arrow, execution != null ? execution.RegisterEffect : null);
                    execution?.RegisterEffect(effectObj);

                    // 주 투사체 크기도 파츠(크기·위력) 반영 — 추가 갈래는 퍼널이 이미 적용한다.
                    effectObj.transform.localScale = Vector3.one * (e.scaleMultiplier * req.sizeMult);

                    // 공중 발사 시 짧은 체공
                    if (!_player.IsGrounded())
                        _player.StartAirHover();

                    continue;
                }

                if (!effectObj.TryGetComponent<EffectBehaviour>(out var effectBehaviour))
                {
                    Debug.LogWarning($"[WeaponEffectHandler] '{effectObj.name}'에 EffectBehaviour가 없습니다. 프리팹을 확인하세요.");
                    Managers.ObjectPooler.Despawn(effectObj);
                    continue;
                }

                // 소켓의 forward를 이동 방향으로 전달
                Vector3 spawnForward = socketTransform.forward;
                effectBehaviour.Initialize(e.behavior, playerTransform, e.lifeTimeMultiplier, spawnForward);

                // 합쳐진 프리팹: 같은 오브젝트에 ColliderInstance가 있으면 데미지 주입
                bool hasCI = effectObj.TryGetComponent<ColliderInstance>(out var embedded);
                if (hasCI)
                {
                    if (s.collider != null)
                        SetupColliderInstance(embedded, s, actionType);
                    else
                    {
                        embedded.damage = s.baseDamage;
                        embedded.knockbackMultiplier = s.knockbackMultiplier;
                        embedded.owner = _player.gameObject;
                        embedded.actionType = actionType;
                        embedded.hitEffectKey = s.hitEffectKey;
                        embedded.hitEffectScale = s.hitEffectScale;
                        embedded.attackId = _player.Combo != null ? _player.Combo.CurrentComboStep : 0;

                        var wd = _player.WeaponManager?.CurrentWeaponData;
                        if (wd != null)
                        {
                            var kind = wd.weaponType.GetAttackStatKind();
                            embedded.damage = DamageFormula.Calculate(s.baseDamage, _player.RuntimeStats.GetEffectiveAttack(kind));
                        }
                    }
                    embedded.Activate();
                    colliderHandledByCombined = true;
                }

                execution?.RegisterEffect(effectObj);
            }

            // =====================================================
            // 2. 별도 콜라이더 스폰
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
                        handTransform.rotation * Quaternion.Euler(c.rotationEuler)
                    );

                    if (_player == null || colliderObj == null) return;
                }
                else
                {
                    colliderObj = new GameObject("RuntimeCollider");
                    colliderObj.transform.position = handTransform.TransformPoint(c.positionOffset);
                    colliderObj.transform.rotation = playerTransform.rotation * Quaternion.Euler(c.rotationEuler);

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
                colliderInstance.Activate();
                colliderObj.SetActive(true);
                execution?.RegisterCollider(colliderObj);

                if (c.behavior != null)
                    c.behavior.ApplyColliderBehavior(colliderObj, playerTransform);
            }
        }
    }

    /// <summary>EffectSocket → 실제 Transform 변환</summary>
    private Transform ResolveSocket(WeaponAbilitySO.EffectSocket socket, Transform playerTransform, Transform handTransform)
    {
        switch (socket)
        {
            case WeaponAbilitySO.EffectSocket.WeaponMount:
                return handTransform;

            case WeaponAbilitySO.EffectSocket.WeaponTip:
                var wi = _player.WeaponManager?.GetCurrentWeaponComponent<WeaponInstance>();
                if (wi != null && wi.tipPoint != null) return wi.tipPoint;
                return handTransform;

            case WeaponAbilitySO.EffectSocket.WeaponRoot:
                var wiRoot = _player.WeaponManager?.GetCurrentWeaponComponent<WeaponInstance>();
                if (wiRoot != null && wiRoot.rootPoint != null) return wiRoot.rootPoint;
                return handTransform;

            case WeaponAbilitySO.EffectSocket.Player:
            default:
                return playerTransform;
        }
    }

    /// <summary>마우스 커서가 가리키는 월드 지점 (지면 레이캐스트)</summary>
    private Vector3 GetMouseWorldPoint()
    {
        var cam = Camera.main;
        if (cam == null) return _player.transform.position + _player.transform.forward * 10f;

        Ray ray = cam.ScreenPointToRay(UnityEngine.Input.mousePosition);
        if (Physics.Raycast(ray, out var hit, 200f))
            return hit.point;

        // 레이캐스트 실패 시 Y=0 평면과 교차
        var plane = new Plane(Vector3.up, Vector3.zero);
        if (plane.Raycast(ray, out float dist))
            return ray.GetPoint(dist);

        return _player.transform.position + _player.transform.forward * 10f;
    }

    private void SetupColliderInstance(ColliderInstance ci, WeaponAbilitySO.AbilityStep s, WeaponActionType actionType)
    {
        var c = s.collider;
        float rawDamage = s.baseDamage > 0f ? s.baseDamage : c.damage;

        var weaponData = _player.WeaponManager?.CurrentWeaponData;
        if (weaponData != null)
        {
            var kind = weaponData.weaponType.GetAttackStatKind();
            rawDamage = DamageFormula.Calculate(rawDamage, _player.RuntimeStats.GetEffectiveAttack(kind));
        }

        ci.damage              = rawDamage;
        ci.knockbackMultiplier = s.knockbackMultiplier;
        ci.hitInterval         = c.hitInterval;
        ci.owner               = _player.gameObject;
        ci.actionType          = actionType;
        ci.payloadKey          = c.colliderPrefabKey;
        ci.duration            = c.duration;
        ci.hitEffectKey        = s.hitEffectKey;
        ci.hitEffectScale      = s.hitEffectScale;
        ci.attackId            = _player.Combo != null ? _player.Combo.CurrentComboStep : 0;
    }

    // (제거됨) SpawnFanShots / SpawnExtraShots —
    //   부채꼴 15°·추가탄 ±8°/80ms 하드코딩을 각자 들고 있던 중복 스폰 경로였다.
    //   CombatSpawner의 count/spreadDeg로 흡수 — 투사체 증가 소스가 한 곳으로 모였다.

}

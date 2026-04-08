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

                effectObj.transform.localScale = Vector3.one * e.scaleMultiplier;

                // Local space → 소켓에 부착
                if (e.space == WeaponAbilitySO.EffectSpace.Local)
                {
                    effectObj.transform.SetParent(socketTransform, true);
                }

                // BasicArrow 발사체 처리
                if (effectObj.TryGetComponent<BasicArrow>(out var arrow))
                {
                    Vector3 fireDir;
                    Vector3 firePos = playerTransform.position + playerTransform.forward * 1f + Vector3.up * 1f
                                    + playerTransform.TransformDirection(e.positionOffset);

                    if (!_player.IsGrounded())
                    {
                        // 공중: 마우스가 가리키는 지면 지점을 향해 발사
                        Vector3 targetPoint = GetMouseWorldPoint();
                        fireDir = (targetPoint - firePos).normalized;
                    }
                    else
                    {
                        // 지상: 수평 forward
                        fireDir = playerTransform.forward;
                        fireDir.y = 0f;
                        fireDir.Normalize();
                    }

                    effectObj.transform.position = firePos;
                    effectObj.transform.rotation = Quaternion.LookRotation(fireDir);

                    float dmg = DamageFormula.Calculate(s.baseDamage, _player.RuntimeStats.AttackPower);
                    arrow.Fire(fireDir, _player.gameObject, dmg);
                    execution?.RegisterEffect(effectObj);

                    // 추가 발사 버프 (Q스킬 등)
                    if (_player.ExtraShotCount > 0)
                        SpawnExtraShots(e, s, firePos, fireDir, dmg, execution);

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
                Debug.Log($"[EffectHandler] Effect spawned: {effectObj.name}, hasColliderInstance={hasCI}, collider={s.collider != null}");
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

    /// <summary>ExtraShotCount 버프에 의한 추가 화살 발사</summary>
    private async void SpawnExtraShots(
        WeaponAbilitySO.EffectStep e,
        WeaponAbilitySO.AbilityStep s,
        Vector3 basePos, Vector3 baseDir, float dmg,
        AbilityExecution execution)
    {
        int count = _player.ExtraShotCount;
        for (int i = 0; i < count; i++)
        {
            try
            {
                await UniTask.Delay(80, cancellationToken: _player.gameObject.GetCancellationTokenOnDestroy());
            }
            catch (System.OperationCanceledException) { return; }

            if (_player == null) return;

            // 방향 + 위치 랜덤으로 여러발 나가는 느낌
            float spread = Random.Range(-8f, 8f);
            Vector3 dir = Quaternion.Euler(0f, spread, 0f) * baseDir;
            Vector3 right = Vector3.Cross(Vector3.up, baseDir).normalized;
            Vector3 spawnPos = basePos
                + right * Random.Range(-0.4f, 0.4f)
                + Vector3.up * Random.Range(-0.2f, 0.2f);

            var extraObj = await Managers.ObjectPooler.SpawnAsync(
                e.payloadKey, ObjectPoolerManager.PoolType.Effect, spawnPos, Quaternion.LookRotation(dir));
            if (extraObj == null || _player == null) return;

            extraObj.transform.localScale = Vector3.one * e.scaleMultiplier;

            if (extraObj.TryGetComponent<BasicArrow>(out var extraArrow))
            {
                extraArrow.Fire(dir, _player.gameObject, dmg);
                execution?.RegisterEffect(extraObj);
            }

            if (extraObj.TryGetComponent<EffectBehaviour>(out var eb))
                eb.Initialize(e.behavior, _player.transform, e.lifeTimeMultiplier);
        }
    }
}

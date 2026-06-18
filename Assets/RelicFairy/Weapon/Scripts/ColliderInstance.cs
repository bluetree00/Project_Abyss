using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using RelicFairy.Monster;

/// <summary>
/// 히트 판정 컴포넌트.
/// attackId로 공격 행위를 구분 — 같은 적이라도 다른 attackId면 다시 맞음.
/// </summary>
public class ColliderInstance : MonoBehaviour
{
    // hitEffectKey 미할당 시 사용할 공용 히트 VFX.
    private const string FallbackHitEffectKey = "HitEffect_02";

    public string payloadKey;
    public float damage;
    public float knockbackMultiplier;
    public float hitInterval;       // 0 = 단발, >0 = 주기적 피해
    public WeaponActionType actionType;
    public GameObject owner;
    public float duration = 1f;
    public int attackId;            // 공격 행위 ID (콤보 스텝 등으로 구분)

    [Header("Hit Effect")]
    public string hitEffectKey;
    public float hitEffectScale = 1f;

    private float _elapsedTime;
    private bool _standalone;
    private bool _active;
    private Vector3 _baseScale = Vector3.one;

    // 아이템 형태변형 추가타 질의 버퍼(재사용 — alloc 방지)
    private static readonly List<MonsterBase> s_shapeBuf = new();

    // key: 대상, value: 마지막으로 맞은 attackId
    private readonly Dictionary<GameObject, int> _hitRecord = new();
    // 주기적 피해용
    private readonly Dictionary<GameObject, float> _hitTimestamps = new();

    private void Awake()
    {
        _standalone = GetComponent<EffectBehaviour>() == null;
        _baseScale = transform.localScale;
    }

    private void OnEnable()
    {
        _elapsedTime = 0f;
        _active = false;
        _hitRecord.Clear();
        _hitTimestamps.Clear();
    }

    public void Activate()
    {
        _active = true;

        // 아이템 근접 사거리 변형(확장된 칼끝/기다림의 미학) — 근접 액션이면 콜라이더 스케일.
        // 변형이 없으면 기준 스케일 그대로(회귀 0). 풀 재사용 정합을 위해 매 Activate 절대 설정.
        float rangeMult = IsMeleeAction(actionType) ? ItemCombatMods.Current.meleeRangeMult : 0f;
        transform.localScale = rangeMult > 0f ? _baseScale * (1f + rangeMult) : _baseScale;

        // 콜라이더를 껐다 켜서 물리 엔진이 재감지하도록 강제
        var col = GetComponent<Collider>();
        if (col != null)
        {
            col.enabled = false;
            col.enabled = true;
        }
    }

    private void Update()
    {
        if (!_standalone) return;
        _elapsedTime += Time.deltaTime;
        if (_elapsedTime >= duration)
            ReturnToPool();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!_active) return;
        if (!CanHit(other.gameObject)) return;
        ApplyDamage(other);
        RecordHit(other.gameObject);
    }

    private void OnTriggerStay(Collider other)
    {
        if (!_active) return;
        if (hitInterval <= 0f) return;
        if (!CanHit(other.gameObject)) return;
        ApplyDamage(other);
        _hitTimestamps[other.gameObject] = Time.time;
    }

    private bool CanHit(GameObject target)
    {
        if (target == owner) return false;
        if (!target.TryGetComponent<IDamageable>(out _)) return false;

        // 같은 attackId로 이미 맞았으면 스킵
        if (_hitRecord.TryGetValue(target, out int lastId) && lastId == attackId)
        {
            // 주기적 피해인 경우 시간 체크
            if (hitInterval > 0f)
            {
                if (_hitTimestamps.TryGetValue(target, out float lastTime))
                    return Time.time - lastTime >= hitInterval;
                return true;
            }
            return false;
        }

        return true;
    }

    private void RecordHit(GameObject target)
    {
        _hitRecord[target] = attackId;
        _hitTimestamps[target] = Time.time;
    }

    private void ApplyDamage(Collider other)
    {
        if (!other.TryGetComponent<IDamageable>(out var damageable)) return;

        // 아이템 효과: 공격 전 데미지 수정
        var mgr = GameRunBootstrapper.Instance?.Run?.EffectManager;
        var weaponData = GameRunBootstrapper.Instance?.Run?.Player?.WeaponManager?.CurrentWeaponData;

        // 스킬 피해 % — Q/E/R 스킬 액션이고 플레이어 공격일 때만 기본 피해에 적용(SkillDamageBonus 소비처)
        float baseDamage = damage;
        if (owner != null
            && (actionType == WeaponActionType.QSkill || actionType == WeaponActionType.ESkill || actionType == WeaponActionType.RSkill)
            && owner.TryGetComponent<PlayerController>(out var skillOwner))
        {
            float skillBonus = skillOwner.RuntimeStats != null ? skillOwner.RuntimeStats.SkillDamageBonus : 0f;
            if (skillBonus != 0f) baseDamage *= 1f + skillBonus;
        }

        bool isMeleeAtk = IsMeleeAction(actionType);
        var pkt = new DamagePacket(baseDamage, owner, other.gameObject);
        mgr?.OnPreDealDamage(ref pkt, isMeleeAtk);

        float baseFinal = pkt.Negated ? 0f : pkt.FinalDamage;

        // 서약: 출력 피해 변형(아서 등). 플레이어 공격만, 크리티컬 전에 적용.
        if (baseFinal > 0f && owner != null && owner.TryGetComponent<PlayerController>(out _))
        {
            var covH = GameRunBootstrapper.Instance?.Run?.CovenantHandler;
            if (covH != null)
            {
                var cctx = new CombatContext
                {
                    Target     = other.gameObject,
                    Damage     = baseFinal,
                    WeaponType = weaponData != null ? weaponData.weaponType : default,
                };
                baseFinal = covH.ModifyOutgoing(baseFinal, cctx);
            }
        }

        // 아이템 타이밍형: 적 windup(예고) 중 적중 시 저스트가드(+피해)·섬광(공격 캔슬)
        var mods = ItemCombatMods.Current;
        bool isPlayerAtk = owner != null && owner.TryGetComponent<PlayerController>(out _);
        MonsterBase tgtMb = isPlayerAtk ? other.GetComponentInParent<MonsterBase>() : null;
        if (tgtMb != null && tgtMb.IsTelegraphingAttack)
        {
            if (mods.justGuardBonus > 0f) baseFinal *= 1f + mods.justGuardBonus;
            if (mods.attackInterrupt)     tgtMb.CancelTelegraphedAttack();
        }

        // 크리티컬 굴림 (확정크릿 소비는 근접 일반공격 한정)
        float finalDmg = CombatCalculator.RollCrit(weaponData, baseFinal, out bool isCrit, isMeleeAtk);
        pkt.IsCrit = isCrit;

        // 아이템 광기의 파동: 무장된 일반(근접) 공격 1타를 실제 방어무시로 적용(방어 우회 경로).
        // 팝업은 대상 측(MonsterBase 등)에서 자체적으로 표시 — isCrit 만 전달.
        bool penetrated = false;
        if (isPlayerAtk && tgtMb != null && finalDmg > 0f && IsMeleeAction(actionType))
        {
            var prs = GameRunBootstrapper.Instance?.Run?.Player?.RuntimeStats;
            if (prs != null && prs.ConsumePenetrateNextHit())
            {
                tgtMb.TakeSynergyDamage(finalDmg, owner, 1f, isCrit);   // 방어 완전 무시
                penetrated = true;
            }
        }
        if (!penetrated)
            damageable.TakeDamage(finalDmg, owner, knockbackMultiplier, isCrit);

        // 타격감 (HitFeedbackService 허브 경유 → 구독자 전파)
        Vector3 hitPoint = other.ClosestPoint(transform.position);

        Vector3 attackDir = owner != null
            ? (other.transform.position - owner.transform.position)
            : (other.transform.position - transform.position);

        var hitInfo = new HitInfo(
            attacker:        owner,
            target:          other.gameObject,
            hitPoint:        hitPoint,
            attackDirection: attackDir,
            damage:          finalDmg,
            isCritical:      isCrit,
            actionType:      actionType,
            weaponType:      weaponData != null ? weaponData.weaponType : WeaponType.None);

        HitFeedbackService.RaiseHit(hitInfo);

        // 아이템 효과: 적중 후 (흡혈, 독, 빙결 등)
        var report = new DamageReport
        {
            DamageDealt = finalDmg,
            Attacker = owner,
            Target = other.gameObject,
            IsCrit = isCrit,
            HitPosition = other.ClosestPoint(transform.position),
        };
        mgr?.OnPostDealDamage(report);

        // 아이템 형태변형: 다단히트(이중타격)·원형 충격파·스킬 추가 투사체(실제 오버랩/추가 적중)
        ApplyItemShapeExtras(other, finalDmg, in mods);

        // 캐릭터 패시브: 실제 적중 시점 (대상 + 데미지 정보 포함)
        if (owner != null && owner.TryGetComponent<PlayerController>(out var ownerCtrl) && finalDmg > 0f)
        {
            ownerCtrl.FirePassive(PassiveTrigger.OnAttackHit, new PassiveContext
            {
                target     = other.gameObject,
                damage     = finalDmg,
                comboStep  = attackId,
                weaponType = weaponData?.weaponType,
            });

            // 서약: 실제 적중 디스패치(근접). DealAoe 등 2차 피해는 이 경로를 안 타므로 재귀 없음.
            GameRunBootstrapper.Instance?.Run?.CovenantHandler?.OnAttackHit(other.gameObject, finalDmg);
        }

#if UNITY_EDITOR
        Debug.Log($"[EffectHit] {gameObject.name} → {other.name} | dmg={finalDmg:F0} | atk={actionType} | id={attackId}");
#endif

        string fxKey = !string.IsNullOrEmpty(hitEffectKey) ? hitEffectKey : FallbackHitEffectKey;
        SpawnHitEffect(other.ClosestPoint(transform.position), fxKey).Forget();
    }

    // UniTaskVoid + 수명 토큰: 비동기 로드 도중 콜라이더가 파괴/풀반환되면 후속 처리를 안전하게 취소.
    private async UniTaskVoid SpawnHitEffect(Vector3 hitPoint, string fxKey)
    {
        GameObject effectObj;
        try
        {
            effectObj = await Managers.ObjectPooler
                .SpawnAsync(fxKey, ObjectPoolerManager.PoolType.Effect, hitPoint, Quaternion.identity)
                .AttachExternalCancellation(this.GetCancellationTokenOnDestroy());
        }
        catch (OperationCanceledException) { return; }

        if (effectObj == null) return;

        effectObj.transform.localScale = Vector3.one * hitEffectScale;
        if (effectObj.TryGetComponent<EffectBehaviour>(out var eb))
            eb.Initialize(eb.behaviorSO, null, 1f);
    }

    private static bool IsMeleeAction(WeaponActionType a) =>
        a == WeaponActionType.GroundLight || a == WeaponActionType.GroundHeavy ||
        a == WeaponActionType.AirLight    || a == WeaponActionType.AirHeavy    ||
        a == WeaponActionType.AirPlunge;

    private static bool IsSkillAction(WeaponActionType a) =>
        a == WeaponActionType.QSkill || a == WeaponActionType.ESkill || a == WeaponActionType.RSkill;

    /// <summary>
    /// 아이템 형태변형 추가 판정 — 실제 오버랩/추가 적중으로 동작(가이드라인은 SynergyDamage 플래시).
    ///  • 근접: meleeExtraHits회 같은 대상 추가타 + meleeCircle 원형 오버랩.
    ///  • 스킬: skillExtraProjectiles만큼 인근 적에 추가 적중(투사체 근사).
    /// DealSynergyDamage 경로는 OnPostDealDamage 미재귀 → 안전.
    /// </summary>
    private void ApplyItemShapeExtras(Collider other, float finalDmg, in ItemCombatModifiers mods)
    {
        if (owner == null || finalDmg <= 0f || other == null) return;
        float ratio = mods.meleeExtraHitRatio > 0f ? mods.meleeExtraHitRatio : 0.8f;

        if (IsMeleeAction(actionType))
        {
            for (int i = 0; i < mods.meleeExtraHits; i++)
                CombatQuery.DealSynergyDamage(other.gameObject, finalDmg * ratio, owner, 0f, false);

            if (mods.meleeCircle && mods.meleeCircleRadius > 0f)
            {
                int n = CombatQuery.GetNearbyEnemies(owner.transform.position, mods.meleeCircleRadius,
                                                     other.gameObject, 12, s_shapeBuf);
                for (int i = 0; i < n; i++)
                    CombatQuery.DealSynergyDamage(s_shapeBuf[i], finalDmg * ratio, owner, 0f, false);
            }
        }
        else if (IsSkillAction(actionType) && mods.skillExtraProjectiles > 0)
        {
            int n = CombatQuery.GetNearbyEnemies(other.transform.position, 5f,
                                                 other.gameObject, mods.skillExtraProjectiles, s_shapeBuf);
            for (int i = 0; i < n; i++)
                CombatQuery.DealSynergyDamage(s_shapeBuf[i], finalDmg, owner, 0f, false);
        }
    }

    private void ReturnToPool()
    {
        if (!string.IsNullOrEmpty(payloadKey))
            Managers.ObjectPooler.Despawn(gameObject);
        else
            Destroy(gameObject);
    }

}

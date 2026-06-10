using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

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

    // key: 대상, value: 마지막으로 맞은 attackId
    private readonly Dictionary<GameObject, int> _hitRecord = new();
    // 주기적 피해용
    private readonly Dictionary<GameObject, float> _hitTimestamps = new();

    private void Awake()
    {
        _standalone = GetComponent<EffectBehaviour>() == null;
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
        var pkt = new DamagePacket(damage, owner, other.gameObject);
        mgr?.OnPreDealDamage(ref pkt);

        float baseFinal = pkt.Negated ? 0f : pkt.FinalDamage;

        // 크리티컬 굴림
        float finalDmg = CombatCalculator.RollCrit(weaponData, baseFinal, out bool isCrit);
        pkt.IsCrit = isCrit;

        // 팝업은 대상 측(MonsterBase 등)에서 자체적으로 표시 — isCrit 만 전달
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
            actionType:      actionType);

        HitFeedbackService.RaiseHit(hitInfo);

        // 아이템 효과: 적중 후 (흡혈, 독, 빙결 등)
        var report = new DamageReport
        {
            DamageDealt = finalDmg,
            Attacker = owner,
            Target = other.gameObject,
            HitPosition = other.ClosestPoint(transform.position),
        };
        mgr?.OnPostDealDamage(report);

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

    private void ReturnToPool()
    {
        if (!string.IsNullOrEmpty(payloadKey))
            Managers.ObjectPooler.Despawn(gameObject);
        else
            Destroy(gameObject);
    }

}

using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 히트 판정 컴포넌트.
/// attackId로 공격 행위를 구분 — 같은 적이라도 다른 attackId면 다시 맞음.
/// </summary>
public class ColliderInstance : MonoBehaviour
{
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
        damageable.TakeDamage(damage, owner, knockbackMultiplier);

        Debug.Log($"[EffectHit] {gameObject.name} → {other.name} | dmg={damage:F0} | atk={actionType} | id={attackId}");

        if (!string.IsNullOrEmpty(hitEffectKey))
            SpawnHitEffect(other.ClosestPoint(transform.position));
    }

    private async void SpawnHitEffect(Vector3 hitPoint)
    {
        var effectObj = await Managers.ObjectPooler.SpawnAsync(
            hitEffectKey, ObjectPoolerManager.PoolType.Effect, hitPoint, Quaternion.identity);
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

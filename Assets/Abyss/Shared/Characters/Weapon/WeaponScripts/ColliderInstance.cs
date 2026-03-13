using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 히트 판정 컴포넌트.
/// EffectBehaviour와 같은 오브젝트에 있으면 수명은 EffectBehaviour에 위임 (합쳐진 프리팹 모드).
/// 단독으로 사용할 경우 duration으로 자체 수명 관리.
/// </summary>
public class ColliderInstance : MonoBehaviour
{
    public string payloadKey;
    public float damage;
    public float knockbackMultiplier;
    public float hitInterval;       // 0 = 단발, >0 = 주기적 피해
    public WeaponActionType actionType;
    public GameObject owner;
    public float duration = 1f;     // 단독 모드에서만 사용

    private float _elapsedTime;
    private bool _standalone;       // EffectBehaviour 없을 때 true
    private readonly Dictionary<GameObject, float> _hitTimestamps = new();

    private void Awake()
    {
        // EffectBehaviour가 같은 오브젝트에 있으면 수명을 그쪽에 위임
        _standalone = GetComponent<EffectBehaviour>() == null;
    }

    private void OnEnable()
    {
        _elapsedTime = 0f;
        _hitTimestamps.Clear();
    }

    private void Update()
    {
        if (!_standalone) return; // 합쳐진 모드: EffectBehaviour가 수명 관리

        _elapsedTime += Time.deltaTime;
        if (_elapsedTime >= duration)
            ReturnToPool();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!CanHit(other.gameObject)) return;
        ApplyDamage(other);

        // hitInterval == 0 이면 단발
        if (hitInterval <= 0f)
            _hitTimestamps[other.gameObject] = float.MaxValue;
        else
            _hitTimestamps[other.gameObject] = Time.time;
    }

    private void OnTriggerStay(Collider other)
    {
        if (hitInterval <= 0f) return;
        if (!CanHit(other.gameObject)) return;
        ApplyDamage(other);
        _hitTimestamps[other.gameObject] = Time.time;
    }

    private bool CanHit(GameObject target)
    {
        if (target == owner) return false;
        if (!target.TryGetComponent<IDamageable>(out _)) return false;

        if (_hitTimestamps.TryGetValue(target, out float lastHit))
        {
            if (hitInterval <= 0f) return false;
            if (Time.time - lastHit < hitInterval) return false;
        }

        return true;
    }

    private void ApplyDamage(Collider other)
    {
        if (!other.TryGetComponent<IDamageable>(out var damageable)) return;
        damageable.TakeDamage(damage, owner, knockbackMultiplier);
    }

    private void ReturnToPool()
    {
        if (!string.IsNullOrEmpty(payloadKey))
            Managers.ObjectPooler.Despawn(gameObject);
        else
            Destroy(gameObject);
    }
}

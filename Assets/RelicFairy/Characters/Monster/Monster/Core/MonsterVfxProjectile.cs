using UnityEngine;

/// <summary>
/// VFX 기반 투사체 컴포넌트.
/// MonsterRangedAttackSO의 vfxProjectilePrefab을 사용할 때 런타임으로 부착된다.
/// Rigidbody·Collider 없이 매 프레임 거리 판정으로 플레이어 히트를 감지한다.
/// </summary>
public class MonsterVfxProjectile : MonoBehaviour
{
    // ── Private ────────────────────────────────────────────────────────────────
    private Vector3   _direction;
    private float     _speed;
    private float     _maxRange;
    private int       _damage;
    private float     _knockbackForce;
    private float     _slowScale;
    private float     _slowDuration;
    private float     _hitRadius;
    private Transform _playerTarget;
    private GameObject _hitEffectPrefab;
    private float      _hitEffectScale;

    private float _travelled;
    private bool  _done;

    // ── Public Methods ─────────────────────────────────────────────────────────

    public void Init(
        Vector3   direction,
        float     speed,
        float     maxRange,
        int       damage,
        float     knockbackForce,
        float     slowScale,
        float     slowDuration,
        float     hitRadius,
        Transform playerTarget,
        GameObject hitEffectPrefab = null,
        float      hitEffectScale  = 1f)
    {
        _direction       = direction.normalized;
        _speed           = speed;
        _maxRange        = maxRange;
        _damage          = damage;
        _knockbackForce  = knockbackForce;
        _slowScale       = slowScale;
        _slowDuration    = slowDuration;
        _hitRadius       = Mathf.Max(0.1f, hitRadius);
        _playerTarget    = playerTarget;
        _hitEffectPrefab = hitEffectPrefab;
        _hitEffectScale  = hitEffectScale;
    }

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    private void Start()
    {
        // ParticleSystem scalingMode 보정 — localScale 변경이 파티클에 적용되도록
        var systems = GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < systems.Length; i++)
        {
            var main = systems[i].main;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        }
    }

    private void Update()
    {
        if (_done) return;

        float step = _speed * Time.deltaTime;
        transform.position += _direction * step;
        _travelled += step;

        if (_travelled >= _maxRange)
        {
            Destroy(gameObject);
            return;
        }

        if (_playerTarget == null) return;

        // Y는 launchHeightOffset으로 차이가 생기므로 XZ 수평 거리만 판정
        Vector3 toPlayer = _playerTarget.position - transform.position;
        toPlayer.y = 0f;
        if (toPlayer.magnitude <= _hitRadius)
            Hit();
    }

    // ── Private Methods ────────────────────────────────────────────────────────

    private void Hit()
    {
        _done = true;

        if (_playerTarget != null)
        {
            var player = _playerTarget.GetComponent<PlayerController>();
            if (player != null)
            {
                player.TakeDamage(_damage);

                if (_slowDuration > 0f)
                    player.ApplySlow(_slowScale, _slowDuration);

                Vector3 dir = (_playerTarget.position - transform.position).normalized;
                dir.y = 0.3f;
                player.ApplyKnockback(dir.normalized * _knockbackForce);
            }
        }

        SpawnHitEffect();
        Destroy(gameObject);
    }

    private void SpawnHitEffect()
    {
        if (_hitEffectPrefab == null) return;

        var go = Instantiate(_hitEffectPrefab, transform.position, Quaternion.identity);
        go.transform.localScale = Vector3.one * Mathf.Max(0.001f, _hitEffectScale);

        var systems = go.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < systems.Length; i++)
        {
            var main = systems[i].main;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        }

        var ps = go.GetComponent<ParticleSystem>() ?? go.GetComponentInChildren<ParticleSystem>();
        float lifetime = ps != null ? ps.main.duration + ps.main.startLifetimeMultiplier + 0.3f : 2f;
        Destroy(go, lifetime);
    }
}

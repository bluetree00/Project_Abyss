using UnityEngine;

/// <summary>
/// 몬스터 원거리 발사체 컴포넌트.
/// MonsterRangedAttackSO.Execute() 에서 Instantiate 후 Init() 호출.
/// maxRange 거리를 이동하거나 플레이어에 충돌 시 자동 소멸.
///
/// ─ 충돌 감지 ──────────────────────────────────────────────
///  Init() 시 콜라이더를 isTrigger=true로 강제해 OnTriggerEnter를 보장한다.
///  비트리거 콜라이더 프리팹을 대비해 OnCollisionEnter도 함께 처리한다.
///
/// ─ 히트 이펙트 ────────────────────────────────────────────
///  _hitEffect(ParticleSystem)를 프리팹 인스펙터에서 할당하면
///  충돌 시 부모에서 분리 후 재생하고 자동 소멸한다.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class MonsterProjectile : MonoBehaviour
{
    [SerializeField] private ParticleSystem _hitEffect;

    private Vector3 _direction;
    private float   _speed;
    private float   _maxRange;
    private int     _damage;
    private float   _knockbackForce;

    private Vector3 _originPos;
    private bool    _initialized;
    private bool    _hit;

    public void Init(Vector3 direction, float speed, float maxRange, int damage, float knockbackForce)
    {
        _direction      = direction;
        _speed          = speed;
        _maxRange       = maxRange;
        _damage         = damage;
        _knockbackForce = knockbackForce;
        _originPos      = transform.position;
        _initialized    = true;
        _hit            = false;

        // isTrigger 강제: 프리팹 설정과 무관하게 OnTriggerEnter 보장
        var col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;

        var rb = GetComponent<Rigidbody>();
        rb.useGravity     = false;
        rb.isKinematic    = false;
        rb.linearVelocity = _direction * _speed;
    }

    private void Update()
    {
        if (!_initialized || _hit) return;
        if (Vector3.Distance(_originPos, transform.position) >= _maxRange)
            Destroy(gameObject);
    }

    // isTrigger=true 경로 (정상 경로)
    private void OnTriggerEnter(Collider other)
        => HandleHit(other.gameObject);

    // isTrigger=false 폴백 (프리팹 구성 이상 시 안전망)
    private void OnCollisionEnter(Collision collision)
        => HandleHit(collision.gameObject);

    // ── 공통 히트 처리 ────────────────────────────────────

    private void HandleHit(GameObject hitObject)
    {
        if (!_initialized || _hit) return;

        var player = hitObject.GetComponentInParent<PlayerController>();
        if (player == null) return;

        _hit = true;

        player.TakeDamage(_damage);

        Vector3 dir = (hitObject.transform.position - transform.position).normalized;
        dir.y = 0.3f;
        player.ApplyKnockback(dir.normalized * _knockbackForce);

        PlayHitEffect();
        Destroy(gameObject);
    }

    private void PlayHitEffect()
    {
        if (_hitEffect == null) return;

        // 부모에서 분리해 투사체 소멸 후에도 이펙트가 끝까지 재생되도록 한다
        _hitEffect.transform.SetParent(null);
        _hitEffect.Play();
        Destroy(_hitEffect.gameObject, _hitEffect.main.duration + 0.5f);
    }
}

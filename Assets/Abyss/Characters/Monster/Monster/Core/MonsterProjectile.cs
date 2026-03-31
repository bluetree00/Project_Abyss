using UnityEngine;

/// <summary>
/// 몬스터 원거리 발사체 컴포넌트.
/// MonsterRangedAttackSO.Execute() 에서 Instantiate 후 Init() 호출.
/// maxRange 거리를 이동하거나 플레이어에 충돌 시 자동 소멸.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class MonsterProjectile : MonoBehaviour
{
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

        var rb = GetComponent<Rigidbody>();
        rb.useGravity  = false;
        rb.isKinematic = false;
        rb.linearVelocity = _direction * _speed;
    }

    private void Update()
    {
        if (!_initialized || _hit) return;
        if (Vector3.Distance(_originPos, transform.position) >= _maxRange)
            Destroy(gameObject);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_hit) return;

        var player = other.GetComponentInParent<PlayerController>();
        if (player == null) return;

        _hit = true;
        player.TakeDamage(_damage);

        Vector3 dir = (other.transform.position - transform.position).normalized;
        dir.y = 0.3f;
        player.ApplyKnockback(dir.normalized * _knockbackForce);

        Destroy(gameObject);
    }
}

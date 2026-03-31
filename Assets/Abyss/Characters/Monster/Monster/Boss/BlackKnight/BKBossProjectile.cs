using UnityEngine;

/// <summary>
/// BlackKnight ScatterShot 투사체.
/// 초기화 후 직선으로 이동하며, 플레이어에 닿거나 최대 사거리 초과 시 풀에 반납된다.
/// </summary>
public class BKBossProjectile : MonoBehaviour
{
    public BKProjectilePool Pool;

    private float _speed;
    private float _maxRange;
    private int   _damage;
    private float _hitRadius;

    private float _traveled;
    private bool  _hit;

    public void Init(float speed, float range, int damage, float hitRadius = 0.25f)
    {
        _speed     = speed;
        _maxRange  = range;
        _damage    = damage;
        _hitRadius = hitRadius;
        _traveled  = 0f;
        _hit       = false;
    }

    private void Update()
    {
        if (_hit) return;

        float step = _speed * Time.deltaTime;
        transform.position += transform.forward * step;
        _traveled += step;

        // 사거리 초과
        if (_traveled >= _maxRange)
        {
            ReturnToPool();
            return;
        }

        var hits = Physics.OverlapSphere(transform.position, _hitRadius);
        foreach (var col in hits)
        {
            var player = col.GetComponent<PlayerController>()
                      ?? col.GetComponentInParent<PlayerController>();
            if (player == null) continue;

            _hit = true;
            player.TakeDamage(_damage);
            ReturnToPool();
            return;
        }
    }

    private void ReturnToPool()
    {
        _hit = true;
        if (Pool != null)
            Pool.Return(this);
        else
            Destroy(gameObject);
    }
}

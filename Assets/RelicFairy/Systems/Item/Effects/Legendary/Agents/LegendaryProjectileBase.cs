using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 레전드리 룬 투사체 에이전트 공통 베이스.
/// - 수평 이동 + 벽 반사 (Raycast, Layer "Wall")
/// - 일정 주기로 OverlapSphere를 통한 적 감지
/// - LegendaryRuntime에 등록 → 룸 전환 시 일괄 정리
/// </summary>
public abstract class LegendaryProjectileBase : MonoBehaviour
{
    private static int _wallMask = -1;
    protected static int WallMask
    {
        get
        {
            if (_wallMask < 0) _wallMask = LayerMask.GetMask("Wall", "Ground");
            return _wallMask;
        }
    }

    public const float FlyHeight = 1.0f;   // 화살 높이 기준

    private float _fixedY;

    protected ItemEffectContext _ctx;
    protected float _damage;
    protected float _speed;
    protected float _lifetime;
    protected RuneElement _element;
    protected int _maxBounces;
    protected bool _pierce;
    protected float _hitRadius;

    public object Owner { get; private set; }

    protected virtual float BounceNoiseAngle => 0f;

    private void ApplyBounceNoise()
    {
        float noise = BounceNoiseAngle;
        if (noise <= 0f) return;
        _dir = Quaternion.Euler(0f, Random.Range(-noise, noise), 0f) * _dir;
        _dir.y = 0f;
        _dir.Normalize();
    }

    protected Vector3 _dir;
    protected float _timer;
    protected int _bounceCount;

    protected readonly List<GameObject> _hitBuffer = new(16);

    public virtual void Init(object owner, ItemEffectContext ctx, float damage, float speed,
                             float lifetime, RuneElement element, Vector3 startDir,
                             int maxBounces = 20, bool pierce = false, float hitRadius = 0.5f)
    {
        Owner      = owner;
        _ctx       = ctx;
        _damage    = damage;
        _speed     = speed;
        _lifetime  = lifetime;
        _element   = element;
        _dir       = new Vector3(startDir.x, 0f, startDir.z).normalized;
        _maxBounces = maxBounces;
        _pierce    = pierce;
        _hitRadius = hitRadius;
        _timer     = 0f;
        _bounceCount = 0;
    }

    protected virtual void Start()
    {
        _fixedY = transform.position.y;   // 생성 위치의 y로 고정 (플레이어와 무관)
        if (_ctx?.Player != null && _ctx.Player.TryGetComponent<PlayerController>(out var pc))
            _speed *= pc.RuntimeStats.MoveSpeedMultiplier;
        LegendaryRuntime.Instance?.TrackAgent(gameObject);
        OnSpawnVfx();
    }

    protected virtual void OnDestroy()
    {
        LegendaryRuntime.Instance?.RemoveAgent(gameObject);
    }

    protected virtual void OnSpawnVfx() { }

    protected virtual void Update()
    {
        _timer += Time.deltaTime;
        if (_timer >= _lifetime) { Expire(); return; }

        var pos = transform.position;
        pos.y = _fixedY;
        transform.position = pos;

        TryBounce();
        Move();
        TryNavMeshBounce();
        DetectEnemies();
    }

    protected virtual void Move()
    {
        transform.position += _dir * _speed * Time.deltaTime;
    }

    protected virtual bool TryBounce()
    {
        float lookAhead = _speed * Time.deltaTime + 0.2f;
        if (!Physics.Raycast(transform.position, _dir, out var hit, lookAhead, WallMask))
            return false;

        // 수평 경계면(바닥/천장)은 무시하고 수직 벽만 반사
        if (Mathf.Abs(hit.normal.y) > 0.7f) return false;

        _dir = Vector3.Reflect(_dir, hit.normal);
        _dir.y = 0f;
        if (_dir.sqrMagnitude < 0.01f) _dir = Vector3.forward;
        _dir.Normalize();
        ApplyBounceNoise();

        _bounceCount++;
        if (_bounceCount >= _maxBounces) { Expire(); return true; }
        OnBounced(hit.point);
        return true;
    }

    protected virtual void OnBounced(Vector3 hitPoint) { }

    // NavMesh 밖으로 나갔을 때 경계 법선으로 반사하고 안쪽으로 스냅
    protected virtual void TryNavMeshBounce()
    {
        var pos = transform.position;
        if (NavMesh.SamplePosition(pos, out _, 0.3f, NavMesh.AllAreas)) return; // 안쪽이면 통과

        if (NavMesh.SamplePosition(pos, out var nearest, 5f, NavMesh.AllAreas))
        {
            var snapped = nearest.position;
            snapped.y = _fixedY;
            transform.position = snapped;

            if (NavMesh.FindClosestEdge(snapped, out var edgeHit, NavMesh.AllAreas))
            {
                var normal = new Vector3(edgeHit.normal.x, 0f, edgeHit.normal.z).normalized;
                _dir = normal.sqrMagnitude > 0.01f ? Vector3.Reflect(_dir, normal) : -_dir;
            }
            else
            {
                _dir = -_dir;
            }
            ApplyBounceNoise();
        }
        // NavMesh 자체가 없는 씬(Test씬 등)에서는 반사 없이 통과
    }

    private float _hitCheckTimer;
    private const float HitCheckInterval = 0.08f;

    protected virtual void DetectEnemies()
    {
        _hitCheckTimer += Time.deltaTime;
        if (_hitCheckTimer < HitCheckInterval) return;
        _hitCheckTimer = 0f;

        int n = CombatQuery.GetNearbyDamageables(
            transform.position, _hitRadius, _ctx?.Player?.gameObject, 5, _hitBuffer);

        for (int i = 0; i < n; i++)
        {
            OnHitEnemy(_hitBuffer[i]);
            if (!_pierce) { Expire(); return; }
        }
    }

    protected virtual void OnHitEnemy(GameObject target)
    {
        CombatQuery.DealSynergyDamage(target, _damage, _ctx?.Player?.gameObject, element: _element);
        OnSpawnHitVfx(target.transform.position);
    }

    protected virtual void OnSpawnHitVfx(Vector3 pos) { }

    protected virtual void Expire() => Destroy(gameObject);
}

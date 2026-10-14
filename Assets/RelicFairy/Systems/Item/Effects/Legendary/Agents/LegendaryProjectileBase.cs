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

    // 비전투 방 숨김(10-11 이펙트 톤 2차 V-10) — 궤도 투사체는 방에 들어가면 생겨 무한히 돌아, 대기방 · 상점 · 쉼터에서도
    // 맞을 것 없이 빛을 뿌렸다. 플레이어 둘레에 맞을 것이 없으면 보이는 것(자식 이펙트 · 자기 렌더러)만 끈다 — 움직임 · 판정은 그대로.
    private const float IdleCheckInterval = 0.5f;
    private const float IdleRadius        = 25f;
    private static readonly List<GameObject> s_idleBuf = new(1);
    private float _idleCheck;

    /// <summary>맞을 것이 없어 보이는 것을 숨긴 상태.</summary>
    protected bool IsIdle { get; private set; }

    protected ItemEffectContext _ctx;
    protected float _damage;
    protected float _speed;
    protected float _lifetime;
    protected RuneElement _element;
    protected int _maxBounces;
    protected bool _pierce;
    protected float _hitRadius;

    public object Owner { get; private set; }

    /// <summary>같은 대상 재타격 간격(초) — 0이면 판정 주기(0.08초)마다. 관통 투사체가 한 번 지나며 2 ~ 3타 넣던 것(10-14 룬 재설계 R3).</summary>
    public float PerTargetGap { get; set; }
    /// <summary>맞힐 때마다 피해를 다시 셈(그때 공격력 · 존 단계) — 없으면 Init 때의 고정값. 방 입장 때 공격력에 굳던 것(10-14 R3).</summary>
    public System.Func<float> DamageFn { get; set; }
    /// <summary>지금 한 번 맞힐 피해.</summary>
    protected float CurrentDamage => DamageFn != null ? DamageFn() : _damage;
    private readonly Dictionary<GameObject, float> _lastHitAt = new();

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
        UpdateIdle();
    }

    private void UpdateIdle()
    {
        _idleCheck -= Time.deltaTime;
        if (_idleCheck > 0f) return;
        _idleCheck = IdleCheckInterval;

        var player = _ctx?.Player;
        bool idle = player != null
                    && CombatQuery.GetNearbyDamageables(player.transform.position, IdleRadius, player.gameObject, 1, s_idleBuf) == 0;
        if (idle == IsIdle) return;
        IsIdle = idle;
        for (int i = 0; i < transform.childCount; i++) transform.GetChild(i).gameObject.SetActive(!idle);
        foreach (var r in GetComponents<Renderer>()) r.enabled = !idle;   // 독화살 궤적(TrailRenderer)은 자기 몸에 붙어 있다
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

        float now = Time.time;
        for (int i = 0; i < n; i++)
        {
            var go = _hitBuffer[i];
            if (PerTargetGap > 0f)
            {
                if (_lastHitAt.TryGetValue(go, out float at) && now - at < PerTargetGap) continue;
                _lastHitAt[go] = now;
            }
            OnHitEnemy(go);
            if (!_pierce) { Expire(); return; }
        }
    }

    protected virtual void OnHitEnemy(GameObject target)
    {
        CombatQuery.DealSynergyDamage(target, CurrentDamage, _ctx?.Player?.gameObject, element: _element);
        OnSpawnHitVfx(target.transform.position);
    }

    protected virtual void OnSpawnHitVfx(Vector3 pos) { }

    protected virtual void Expire() => Destroy(gameObject);
}

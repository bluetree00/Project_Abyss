using UnityEngine;
using UnityEngine.AI;

namespace Abyss.Monster
{
/// <summary>
/// Simple summoned minion used by the dragon boss summon pattern.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class MiniDragonController : MonoBehaviour, IDamageable
{
    [SerializeField] private int maxHp = 30;
    [SerializeField] private int attackPower = 10;
    [SerializeField] private float attackRange = 6f;
    [SerializeField] private float attackInterval = 2f;
    [SerializeField] private MonsterProjectile projectilePrefab;
    [SerializeField] private float projectileSpeed = 12f;
    [SerializeField] private float launchHeightOffset = 0.8f;
    [SerializeField] private float breathHitRadius = 1.1f;
    [SerializeField] private float breathMaxDistance = 7f;

    private int _currentHp;
    private NavMeshAgent _agent;
    private Transform _playerTarget;
    private DragonBossMonster _boss;
    private float _attackTimer;
    private bool _initialized;

    public static MiniDragonController CreateFallback(Vector3 position)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "MiniDragon";
        go.transform.position = position;
        go.transform.localScale = Vector3.one * 1.2f;

        var renderer = go.GetComponent<Renderer>();
        if (renderer != null)
            renderer.material.color = new Color(0.55f, 0.75f, 1f, 1f);

        if (go.GetComponent<NavMeshAgent>() == null)
            go.AddComponent<NavMeshAgent>();

        return go.AddComponent<MiniDragonController>();
    }

    public void Init(DragonBossMonster boss, Transform playerTarget)
    {
        _boss = boss;
        _playerTarget = playerTarget;
        _currentHp = maxHp;
        _agent = GetComponent<NavMeshAgent>();
        _agent.speed = 4f;
        _agent.stoppingDistance = attackRange * 0.65f;
        _attackTimer = 0.5f;
        _initialized = true;
    }

    private void Update()
    {
        if (!_initialized || _playerTarget == null || _currentHp <= 0) return;

        float dist = Vector3.Distance(transform.position, _playerTarget.position);
        if (dist > attackRange * 0.8f)
            _agent.SetDestination(_playerTarget.position);
        else
            _agent.ResetPath();

        _attackTimer -= Time.deltaTime;
        if (_attackTimer <= 0f)
        {
            _attackTimer = attackInterval;
            FireProjectile();
        }
    }

    private void FireProjectile()
    {
        if (_playerTarget == null) return;

        if (projectilePrefab == null)
        {
            TryBreathHit();
            return;
        }

        Vector3 origin = transform.position + Vector3.up * launchHeightOffset;
        Vector3 targetPos = _playerTarget.position + Vector3.up * launchHeightOffset;
        Vector3 dir = (targetPos - origin).normalized;

        var projObj = BossEffectPool.Spawn(
            projectilePrefab.gameObject,
            origin,
            Quaternion.LookRotation(dir));
        var proj = projObj != null ? projObj.GetComponent<MonsterProjectile>() : null;
        if (proj == null) return;

        proj.Init(dir, projectileSpeed, attackRange, attackPower, 3f);
    }

    private void TryBreathHit()
    {
        if (_playerTarget == null) return;

        Vector3 origin = transform.position + Vector3.up * launchHeightOffset;
        Vector3 dir = (_playerTarget.position + Vector3.up * launchHeightOffset - origin).normalized;

        if (Physics.SphereCast(origin, breathHitRadius, dir, out RaycastHit hit, breathMaxDistance))
        {
            var player = hit.collider.GetComponent<PlayerController>()
                ?? hit.collider.GetComponentInParent<PlayerController>();
            if (player != null)
            {
                player.TakeDamage(attackPower);
                player.ApplyKnockback(Vector3.zero, 0.2f);
            }
        }
    }

    public void TakeDamage(int damage)
    {
        if (_currentHp <= 0) return;

        _currentHp -= damage;
        if (_currentHp <= 0)
        {
            _currentHp = 0;
            _boss?.OnMiniDragonDied();
            Destroy(gameObject);
        }
    }

    void IDamageable.TakeDamage(float amount, GameObject instigator, float knockbackMultiplier, ElementType element, float elementAmount)
    {
        if (amount <= 0f) return;
        TakeDamage(Mathf.CeilToInt(amount));
    }
}
}

using UnityEngine;
using UnityEngine.AI;

namespace Abyss.Monster
{
/// <summary>
/// 드래곤 보스 소환 미니 드래곤.
/// Init(boss, player, element) 로 초기화한 뒤 독립적으로 플레이어를 추적하며 브레스 단타를 발사한다.
/// 사망 시 DragonBossMonster.OnMiniDragonDied() 를 호출해 보스를 귀환시킨다.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class MiniDragonController : MonoBehaviour, IDamageable
{
    [SerializeField] private int   maxHp              = 30;
    [SerializeField] private int   attackPower        = 10;
    [SerializeField] private float attackRange        = 8f;
    [SerializeField] private float attackInterval     = 2f;
    [SerializeField] private float launchHeightOffset = 0.8f;
    [SerializeField] private float breathHitRadius    = 1.1f;
    [SerializeField] private float breathMaxDist      = 10f;
    [SerializeField] private float breathBeamDuration = 0.25f;

    private int    _currentHp;
    private NavMeshAgent _agent;
    private Transform    _playerTarget;
    private DragonBossMonster _boss;
    private float  _attackTimer;
    private bool   _initialized;

    private DragonBossBlackboard.DragonElement _element;
    private Renderer[] _renderers;

    // ─── Fallback ───────────────────────────────────────────────────────────

    public static MiniDragonController CreateFallback(Vector3 position)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name             = "MiniDragon";
        go.transform.position   = position;
        go.transform.localScale = Vector3.one * 1.2f;

        if (go.GetComponent<NavMeshAgent>() == null)
            go.AddComponent<NavMeshAgent>();

        return go.AddComponent<MiniDragonController>();
    }

    // ─── Init ───────────────────────────────────────────────────────────────

    public void Init(
        DragonBossMonster boss,
        Transform playerTarget,
        DragonBossBlackboard.DragonElement element = DragonBossBlackboard.DragonElement.Ice)
    {
        _boss         = boss;
        _playerTarget = playerTarget;
        _element      = element;
        _currentHp    = maxHp;
        _agent        = GetComponent<NavMeshAgent>();
        _agent.speed  = 4f;
        _agent.stoppingDistance = attackRange * 0.65f;
        _attackTimer  = 0.5f;
        _initialized  = true;

        // 원소 색상 적용
        _renderers = GetComponentsInChildren<Renderer>(true);
        DragonBossVisualHelper.ApplyRendererTint(_renderers, DragonBossVisualHelper.GetElementColor(_element));
    }

    // ─── Lifecycle ──────────────────────────────────────────────────────────

    private void Update()
    {
        if (!_initialized || _playerTarget == null || _currentHp <= 0) return;

        float dist = Vector3.Distance(transform.position, _playerTarget.position);
        if (dist > attackRange * 0.8f)
        {
            if (_agent.isOnNavMesh) _agent.SetDestination(_playerTarget.position);
        }
        else
        {
            if (_agent.isOnNavMesh) _agent.ResetPath();
        }

        _attackTimer -= Time.deltaTime;
        if (_attackTimer <= 0f)
        {
            _attackTimer = attackInterval;
            FireBreath();
        }
    }

    // ─── 브레스 (단타 + 빔 VFX) ────────────────────────────────────────────

    private void FireBreath()
    {
        if (_playerTarget == null) return;

        Vector3 origin    = transform.position + Vector3.up * launchHeightOffset;
        Vector3 targetPos = _playerTarget.position + Vector3.up * launchHeightOffset;
        Vector3 dir       = (targetPos - origin).normalized;

        // 빔 VFX (얇은 실린더)
        SpawnBreathBeam(origin, origin + dir * breathMaxDist);

        // 히트 판정
        if (!Physics.SphereCast(origin, breathHitRadius, dir, out RaycastHit hit, breathMaxDist)) return;

        var player = hit.collider.GetComponent<PlayerController>()
            ?? hit.collider.GetComponentInParent<PlayerController>();
        if (player == null) return;

        player.TakeDamage(attackPower);
        switch (_element)
        {
            case DragonBossBlackboard.DragonElement.Ice:
                player.ApplyKnockback(Vector3.zero, 1f);
                break;
            case DragonBossBlackboard.DragonElement.Thunder:
                player.ApplyThunderGroggy(1f);
                break;
            default: // Fire
                player.ApplySlow(0.5f, 1.5f);
                break;
        }
    }

    private void SpawnBreathBeam(Vector3 from, Vector3 to)
    {
        var beamGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        beamGo.name = "[MiniBreath]";
        Object.Destroy(beamGo.GetComponent<Collider>());

        var mat = new Material(beamGo.GetComponent<Renderer>().sharedMaterial);
        var col = DragonBossVisualHelper.GetElementColor(_element);
        col.a   = 0.8f;
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", col);
        if (mat.HasProperty("_Color"))     mat.SetColor("_Color",     col);
        beamGo.GetComponent<Renderer>().material = mat;

        Vector3 dir = to - from;
        float   len = dir.magnitude;
        if (len < 0.01f) { Object.Destroy(beamGo); Object.Destroy(mat); return; }

        beamGo.transform.position   = (from + to) * 0.5f;
        beamGo.transform.up         = dir.normalized;
        beamGo.transform.localScale = new Vector3(0.25f, len * 0.5f, 0.25f);

        Object.Destroy(beamGo, breathBeamDuration);
        Object.Destroy(mat,    breathBeamDuration + 0.05f);
    }

    // ─── IDamageable ────────────────────────────────────────────────────────

    public void TakeDamage(int damage)
    {
        if (_currentHp <= 0) return;
        _currentHp -= damage;
        if (_currentHp > 0) return;

        _currentHp = 0;
        _boss?.OnMiniDragonDied();
        Destroy(gameObject);
    }

    void IDamageable.TakeDamage(float amount, GameObject instigator, float knockbackMultiplier,
        ElementType element, float elementAmount)
    {
        if (amount <= 0f) return;
        TakeDamage(Mathf.CeilToInt(amount));
    }
}
}

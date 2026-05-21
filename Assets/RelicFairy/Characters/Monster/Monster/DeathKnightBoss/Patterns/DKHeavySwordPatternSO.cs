using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// DeathKnight 강습 내려치기 (HeavySword) 패턴.
///
/// 흐름  : 경고 이펙트 스폰(warningDuration) → Attack3 애니메이션
///         → hitTime 에 원형 범위 판정 → 복귀
/// 조건  : 플레이어가 range 이내
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/Boss/DeathKnight/DK_HeavySwordPattern", fileName = "DK_HeavySwordPattern")]
public class DKHeavySwordPatternSO : BossPatternSO
{
    [Header("HeavySword — Range")]
    public float range = 3.5f;

    [Header("HeavySword — Timing")]
    [Tooltip("경고 이펙트 표시 시간 (초)")]
    public float warningDuration = 0.8f;
    [Tooltip("Attack3 애니메이션 시작 후 타격 판정까지 대기 (초)")]
    public float hitTime = 0.7f;
    [Tooltip("타격 후 복귀 대기 (초)")]
    public float recoveryTime = 0.6f;

    [Header("HeavySword — Damage")]
    public float damageMultiplier  = 2.0f;
    public float knockbackMultiplier = 2.5f;

    [Header("HeavySword — Visual")]
    [Tooltip("경고 이펙트 프리팹 (없으면 effectPrefab 사용)")]
    public GameObject warningPrefab;

    // ── 런타임 ───────────────────────────────────────────
    private DKHeavySwordState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new DKHeavySwordState(this);
    public override void OnRecycled()                      => _state = new DKHeavySwordState(this);

    public override bool CanExecute(BossPatternContext ctx)
    {
        if (ctx.Ctx.Runtime.PlayerTarget == null) return false;
        return ctx.Ctx.Runtime.DistToPlayer <= range;
    }

    public override SpecialStateBase GetRuntimeState() => _state;

    internal GameObject ResolveWarningPrefab() => warningPrefab != null ? warningPrefab : effectPrefab;
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// DKHeavySwordState
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class DKHeavySwordState : FullLockState<DKHeavySwordPatternSO>
{
    private const string AnimName = "Attack3";

    private enum Phase { Warning, Strike, Recovery }
    private Phase      _phase;
    private float      _timer;
    private bool       _hasDamaged;
    private GameObject _warningGO;
    private Vector3    _strikePos;

    public DKHeavySwordState(DKHeavySwordPatternSO data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        _phase      = Phase.Warning;
        _timer      = 0f;
        _hasDamaged = false;

        StopAgent(ctx);
        FacePlayer(ctx);

        _strikePos = ctx.Runtime.PlayerTarget != null
            ? ctx.Runtime.PlayerTarget.position
            : ctx.Transform.position + ctx.Transform.forward * Data.range;
        _strikePos.y = ctx.Transform.position.y;

        SpawnWarning(ctx);
    }

    public override void Update(MonsterContext ctx)
    {
        _timer += Time.deltaTime * AnimSpeed(ctx);

        switch (_phase)
        {
            case Phase.Warning:
                if (_timer >= Data.warningDuration)
                {
                    _timer = 0f;
                    _phase = Phase.Strike;
                    DespawnWarning();
                    PlayAnim(ctx, AnimName);
                }
                break;

            case Phase.Strike:
                if (!_hasDamaged && _timer >= Data.hitTime)
                {
                    _hasDamaged = true;
                    DealDamage(ctx);
                    SpawnImpactEffect(ctx);
                }
                if (_timer >= Data.hitTime + Data.recoveryTime)
                {
                    _phase = Phase.Recovery;
                    ctx.Monster.ChangeState<ChaseState>();
                }
                break;
        }
    }

    public override void Exit(MonsterContext ctx)
    {
        DespawnWarning();
        RestoreAgent(ctx);
    }

    private static void StopAgent(MonsterContext ctx)
    {
        if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
        {
            ctx.Agent.isStopped = true;
            ctx.Agent.ResetPath();
        }
    }

    private static void RestoreAgent(MonsterContext ctx)
    {
        if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
            ctx.Agent.isStopped = false;
    }

    private static void FacePlayer(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null) return;
        Vector3 dir = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.001f)
            ctx.Transform.rotation = Quaternion.LookRotation(dir);
    }

    private void SpawnWarning(MonsterContext ctx)
    {
        var prefab = Data.ResolveWarningPrefab();
        if (prefab == null) return;
        Vector3 pos = _strikePos;
        pos.y += 0.02f;
        _warningGO = Object.Instantiate(prefab, pos, Quaternion.identity);
    }

    private void DespawnWarning()
    {
        if (_warningGO == null) return;
        Object.Destroy(_warningGO);
        _warningGO = null;
    }

    private void SpawnImpactEffect(MonsterContext ctx)
    {
        if (Data.effectPrefab == null) return;
        Vector3 pos = _strikePos;
        pos.y += 0.02f;
        Object.Instantiate(Data.effectPrefab, pos, Quaternion.identity);
    }

    private void DealDamage(MonsterContext ctx)
    {
        if (ctx.Config?.stat == null || ctx.Runtime.PlayerTarget == null) return;

        float dist = Vector3.Distance(_strikePos, ctx.Runtime.PlayerTarget.position);
        if (dist > Data.range) return;

        var player = ctx.Runtime.PlayerTarget.GetComponent<PlayerController>();
        if (player == null) return;

        int dmg = Mathf.Max(1, (int)(ctx.Config.stat.attackPower * Data.damageMultiplier));
        player.TakeDamage(dmg);

        Vector3 knockDir = (ctx.Runtime.PlayerTarget.position - _strikePos);
        knockDir.y = 0.4f;
        if (knockDir.sqrMagnitude > 0.001f) knockDir.Normalize();
        player.ApplyKnockback(knockDir * ctx.Config.stat.knockbackForce * Data.knockbackMultiplier);
    }

    private static void PlayAnim(MonsterContext ctx, string stateName)
    {
        if (ctx.Animator == null) return;
        if (!ctx.Animator.HasState(0, Animator.StringToHash(stateName)))
        {
            Debug.LogWarning($"[DKHeavySword] Animator state not found: '{stateName}'", ctx.Monster);
            return;
        }
        ctx.Animator.speed = AnimSpeed(ctx);
        ctx.Animator.CrossFade(stateName, 0.05f, 0, 0f);
    }

    private static float AnimSpeed(MonsterContext ctx)
    {
        var dk = ctx.Monster as DeathKnightBossMonster;
        return dk?.DKBlackboard.AnimSpeedMult ?? 1f;
    }
}
}

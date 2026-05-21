using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// DeathKnight 회전 베기 (SpinBlade) 패턴.
///
/// 흐름  : 보스 위치 고정 → Attack4 애니메이션
///         → 360° 원형 범위 내 플레이어에게 타격 판정 (1회)
///         → 복귀
/// 조건  : 플레이어가 range 이내 (중거리 AOE, 격노 시 조건 없음 가능)
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/Boss/DeathKnight/DK_SpinBladePattern", fileName = "DK_SpinBladePattern")]
public class DKSpinBladePatternSO : BossPatternSO
{
    [Header("SpinBlade — Range")]
    [Tooltip("360° 회전 베기 반경 (m)")]
    public float range = 4.0f;

    [Header("SpinBlade — Timing")]
    [Tooltip("애니메이션 시작 후 타격 판정 시점 (초)")]
    public float hitTime = 0.6f;
    [Tooltip("타격 후 복귀 대기 (초)")]
    public float recoveryTime = 0.7f;

    [Header("SpinBlade — Damage")]
    public float damageMultiplier  = 1.5f;
    public float knockbackMultiplier = 1.8f;

    // ── 런타임 ───────────────────────────────────────────
    private DKSpinBladeState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new DKSpinBladeState(this);
    public override void OnRecycled()                      => _state = new DKSpinBladeState(this);

    public override bool CanExecute(BossPatternContext ctx)
    {
        if (ctx.Ctx.Runtime.PlayerTarget == null) return false;
        return ctx.Ctx.Runtime.DistToPlayer <= range;
    }

    public override SpecialStateBase GetRuntimeState() => _state;
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// DKSpinBladeState
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class DKSpinBladeState : FullLockState<DKSpinBladePatternSO>
{
    private const string AnimName = "Attack4";

    private float _timer;
    private bool  _hasDamaged;
    private GameObject _effectGO;

    public DKSpinBladeState(DKSpinBladePatternSO data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        _timer      = 0f;
        _hasDamaged = false;

        StopAgent(ctx);
        SpawnEffect(ctx);
        PlayAnim(ctx, AnimName);
    }

    public override void Update(MonsterContext ctx)
    {
        _timer += Time.deltaTime * AnimSpeed(ctx);

        if (!_hasDamaged && _timer >= Data.hitTime)
        {
            _hasDamaged = true;
            DealDamage(ctx);
        }

        if (_timer >= Data.hitTime + Data.recoveryTime)
            ctx.Monster.ChangeState<ChaseState>();
    }

    public override void Exit(MonsterContext ctx)
    {
        DespawnEffect();
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

    private void SpawnEffect(MonsterContext ctx)
    {
        if (Data.effectPrefab == null) return;
        _effectGO = Object.Instantiate(Data.effectPrefab, ctx.Transform.position, ctx.Transform.rotation);
        _effectGO.transform.SetParent(ctx.Transform, true);
    }

    private void DespawnEffect()
    {
        if (_effectGO == null) return;
        _effectGO.transform.SetParent(null);
        Object.Destroy(_effectGO, 1f);
        _effectGO = null;
    }

    private void DealDamage(MonsterContext ctx)
    {
        if (ctx.Config?.stat == null || ctx.Runtime.PlayerTarget == null) return;

        float dist = Vector3.Distance(ctx.Transform.position, ctx.Runtime.PlayerTarget.position);
        if (dist > Data.range) return;

        var player = ctx.Runtime.PlayerTarget.GetComponent<PlayerController>();
        if (player == null) return;

        int dmg = Mathf.Max(1, (int)(ctx.Config.stat.attackPower * Data.damageMultiplier));
        player.TakeDamage(dmg);

        Vector3 knockDir = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        knockDir.y = 0.3f;
        if (knockDir.sqrMagnitude > 0.001f) knockDir.Normalize();
        player.ApplyKnockback(knockDir * ctx.Config.stat.knockbackForce * Data.knockbackMultiplier);
    }

    private static void PlayAnim(MonsterContext ctx, string stateName)
    {
        if (ctx.Animator == null) return;
        if (!ctx.Animator.HasState(0, Animator.StringToHash(stateName)))
        {
            Debug.LogWarning($"[DKSpinBlade] Animator state not found: '{stateName}'", ctx.Monster);
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

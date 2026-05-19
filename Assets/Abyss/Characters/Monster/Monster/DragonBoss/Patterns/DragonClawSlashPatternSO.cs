using UnityEngine;

namespace Abyss.Monster
{
/// <summary>
/// Dragon boss pattern: leap to player, then alternate left/right claw slashes.
/// Each swing shows a danger-zone indicator at attack timing.
/// </summary>
[CreateAssetMenu(fileName = "DragonClawSlashPattern",
    menuName = "Abyss/Boss/Dragon/ClawSlashPattern")]
public class DragonClawSlashPatternSO : BossPatternSO
{
    [Header("Leap")]
    [SerializeField] private float _jumpDuration  = 1.2f;
    [SerializeField] private float _jumpArcHeight = 3f;

    [Header("Claw Attack")]
    [Tooltip("Number of swings per hand — 1 means left once + right once")]
    [SerializeField] private int   _clawCount        = 1;
    [SerializeField] private float _clawAnimDuration = 1.1f;
    [Tooltip("Seconds after swing start before danger zone appears")]
    [SerializeField] private float _warnDuration     = 0.15f;
    [Tooltip("Seconds after swing start when hit is applied")]
    [SerializeField] private float _hitTime          = 0.55f;
    [SerializeField] private float _attackRadius     = 2.5f;
    [SerializeField] private int   _attackDamage     = 20;

    [Header("Effect")]
    [Tooltip("Marker 7 Danger zone prefab for attack warning")]
    [SerializeField] private GameObject _dangerZonePrefab;

    [Header("Cooldown")]
    [SerializeField] private float _cooldown = 12f;

    [Header("Animator State Names")]
    [SerializeField] private string _jumpUpStateName = "JumpUp";
    [SerializeField] private string _clawLStateName  = "ClawAttackL";
    [SerializeField] private string _clawRStateName  = "ClawAttackR";

    public float       JumpDuration     => _jumpDuration;
    public float       JumpArcHeight    => _jumpArcHeight;
    public int         ClawCount        => _clawCount;
    public float       ClawAnimDuration => _clawAnimDuration;
    public float       WarnDuration     => _warnDuration;
    public float       HitTime          => _hitTime;
    public float       AttackRadius     => _attackRadius;
    public int         AttackDamage     => _attackDamage;
    public GameObject  DangerZonePrefab => _dangerZonePrefab;
    public float       Cooldown         => _cooldown;
    public string      JumpUpStateName  => _jumpUpStateName;
    public string      ClawLStateName   => _clawLStateName;
    public string      ClawRStateName   => _clawRStateName;

    private DragonClawSlashState _runtimeState;

    public override void Initialize(BossPatternContext ctx)
        => _runtimeState = new DragonClawSlashState(this);

    public override void OnRecycled()
        => _runtimeState?.Reset();

    public override bool CanExecute(BossPatternContext ctx)
    {
        if (ctx.Ctx.Runtime.PlayerTarget == null) return false;
        return ctx.Blackboard is DragonBossBlackboard bb
               && bb.BodyState == BodyState.Grounded
               && bb.LeapCooldown <= 0f;
    }

    public override SpecialStateBase GetRuntimeState() => _runtimeState;
}

// ────────────────────────────────────────────────────────────────────────────
// Runtime state
// ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// FullLockState: uninterruptible + movement locked.
/// Phases: Jumping → Attacking → (exits to ChaseState)
/// </summary>
internal sealed class DragonClawSlashState : FullLockState<DragonClawSlashPatternSO>
{
    private enum Phase { Jumping, Attacking, Done }

    private Phase   _phase;
    private Vector3 _startPos;
    private Vector3 _targetPos;
    private float   _timer;
    private float   _arcHeight;
    private int     _swingIndex;
    private int     _totalSwings;
    private bool    _hitApplied;
    private bool    _dangerShown;

    internal DragonClawSlashState(DragonClawSlashPatternSO data) : base(data) { }

    internal void Reset()
    {
        _phase      = Phase.Done;
        _swingIndex = 0;
        _timer      = 0f;
        _arcHeight  = 0f;
    }

    public override void Enter(MonsterContext ctx)
    {
        if (ctx.Agent != null) ctx.Agent.enabled = false;

        _phase      = Phase.Jumping;
        _timer      = 0f;
        _startPos   = ctx.Transform.position;
        _targetPos  = ctx.Runtime.PlayerTarget != null
            ? ctx.Runtime.PlayerTarget.position
            : _startPos;

        // 수평 거리에 비례한 포물선 높이 — 가까우면 낮게, 멀면 최대값으로
        float horizontalDist = Vector3.Distance(
            new Vector3(_startPos.x, 0f, _startPos.z),
            new Vector3(_targetPos.x, 0f, _targetPos.z));
        _arcHeight = Mathf.Min(Data.JumpArcHeight, horizontalDist * 0.35f);

        PlayAnim(ctx, Data.JumpUpStateName);

        var bb = GetBlackboard(ctx);
        if (bb != null) bb.LeapCooldown = Data.Cooldown;
    }

    public override void Update(MonsterContext ctx)
    {
        _timer += Time.deltaTime;

        switch (_phase)
        {
            case Phase.Jumping:   UpdateJumping(ctx);   break;
            case Phase.Attacking: UpdateAttacking(ctx); break;
        }
    }

    public override void Exit(MonsterContext ctx)
    {
        if (ctx.Agent == null) return;
        if (!ctx.Agent.enabled)
        {
            ctx.Agent.enabled = true;
            ctx.Agent.Warp(ctx.Transform.position);
        }
        if (ctx.Agent.isOnNavMesh) ctx.Agent.isStopped = false;
    }

    // ── Jump ────────────────────────────────────────────────────────────────

    private void UpdateJumping(MonsterContext ctx)
    {
        float t = Mathf.Clamp01(_timer / Data.JumpDuration);

        Vector3 flat = Vector3.Lerp(_startPos, _targetPos, t);
        float   arc  = Mathf.Sin(t * Mathf.PI) * _arcHeight;
        ctx.Transform.position = new Vector3(flat.x, flat.y + arc, flat.z);

        Vector3 dir = _targetPos - ctx.Transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.01f)
            ctx.Transform.rotation = Quaternion.RotateTowards(
                ctx.Transform.rotation,
                Quaternion.LookRotation(dir),
                360f * Time.deltaTime);

        if (t >= 1f)
            StartAttacking(ctx);
    }

    private void StartAttacking(MonsterContext ctx)
    {
        if (ctx.Agent != null && !ctx.Agent.enabled)
        {
            ctx.Agent.enabled = true;
            ctx.Agent.Warp(ctx.Transform.position);
        }
        if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
            ctx.Agent.isStopped = true;

        // Face player exactly before attacking
        if (ctx.Runtime.PlayerTarget != null)
        {
            Vector3 dir = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.01f)
                ctx.Transform.rotation = Quaternion.LookRotation(dir);
        }

        _phase       = Phase.Attacking;
        _timer       = 0f;
        _swingIndex  = 0;
        _totalSwings = Data.ClawCount * 2;
        _hitApplied  = false;
        _dangerShown = false;

        PlayCurrentSwingAnim(ctx);
    }

    // ── Claw attack loop ─────────────────────────────────────────────────────

    private void UpdateAttacking(MonsterContext ctx)
    {
        if (_swingIndex >= _totalSwings) { FinishPattern(ctx); return; }

        if (!_dangerShown && _timer >= Data.WarnDuration)
        {
            _dangerShown = true;
            SpawnDangerZone(ctx);
        }

        if (!_hitApplied && _timer >= Data.HitTime)
        {
            _hitApplied = true;
            ApplyHit(ctx);
        }

        if (_timer >= Data.ClawAnimDuration)
        {
            _swingIndex++;
            _timer       = 0f;
            _hitApplied  = false;
            _dangerShown = false;

            if (_swingIndex < _totalSwings)
                PlayCurrentSwingAnim(ctx);
            else
                FinishPattern(ctx);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void PlayCurrentSwingAnim(MonsterContext ctx)
    {
        string name = (_swingIndex % 2 == 0) ? Data.ClawLStateName : Data.ClawRStateName;
        PlayAnim(ctx, name);
    }

    private static void PlayAnim(MonsterContext ctx, string stateName)
    {
        if (ctx.Animator != null && ctx.Animator.HasState(0, Animator.StringToHash(stateName)))
            ctx.Animator.CrossFade(stateName, 0.1f, 0, 0f);
    }

    private void SpawnDangerZone(MonsterContext ctx)
    {
        Vector3 pos = ctx.Transform.position
            + ctx.Transform.forward * (Data.AttackRadius * 0.6f);
        pos.y = ctx.Transform.position.y;
        DragonBossWarningZone.CreateCircle(
            "DragonClawWarning",
            pos,
            Data.AttackRadius,
            new Color(1f, 0.25f, 0.18f, 0.85f),
            Data.HitTime + 0.2f);
    }

    private void ApplyHit(MonsterContext ctx)
    {
        var hits = Physics.OverlapSphere(ctx.Transform.position, Data.AttackRadius);
        foreach (var col in hits)
        {
            var player = col.GetComponent<PlayerController>()
                      ?? col.GetComponentInParent<PlayerController>();
            if (player == null) continue;
            player.TakeDamage(Data.AttackDamage);
            break;
        }
    }

    private void FinishPattern(MonsterContext ctx)
    {
        _phase = Phase.Done;
        if (ctx.Agent != null && ctx.Agent.isOnNavMesh) ctx.Agent.isStopped = false;
        ctx.Monster.ChangeState<ChaseState>();
    }

    private static BossAttackBlackboard GetBlackboard(MonsterContext ctx)
        => (ctx.Monster as IBoss)?.Blackboard;
}
}

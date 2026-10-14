using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// Dragon boss pattern: leap to player, then alternate left/right claw slashes.
/// Each swing shows a danger-zone indicator at attack timing.
/// </summary>
[CreateAssetMenu(fileName = "DragonClawSlashPattern",
    menuName = "RelicFairy/Boss/Dragon/ClawSlashPattern")]
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
    [Tooltip("Seconds after swing start when the arm-swing motion visually appears — Craw 사운드 재생 시점")]
    [SerializeField] private float _swingSfxDelay    = 0.3f;
    [SerializeField] private float _attackRadius     = 2.5f;
    [SerializeField] private int   _attackDamage     = 20;
    [SerializeField] private float _damageMultiplier = 1.5f;

    [Header("Effect")]
    [Tooltip("Marker 7 Danger zone prefab for attack warning")]
    [SerializeField] private GameObject _dangerZonePrefab;
    [Tooltip("할퀴기 스윙 시작 시 재생할 사운드")]
    [SerializeField] private AudioClip _clawSfx;

    [Header("EndPose (반격 창)")]
    [SerializeField] private float _endPoseDuration = 0.4f;

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
    public float       SwingSfxDelay    => _swingSfxDelay;
    public float       AttackRadius     => _attackRadius;
    public int         AttackDamage     => _attackDamage;
    public float       DamageMultiplier => _damageMultiplier;
    public GameObject  DangerZonePrefab => _dangerZonePrefab;
    public AudioClip    ClawSfx          => _clawSfx;
    public float       EndPoseDuration  => _endPoseDuration;
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
               && bb.ClawSlashCooldown <= 0f;
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
    private enum Phase { Jumping, Attacking, EndPose, Done }

    private const float GuideLead     = 0.5f;   // 판정 이만큼 전에 발밑 원 예고(10-03 개선 1-4 · 설계 규칙 R3 · 0.4 → 0.5 10-13 검증)
    private const float GuideHitFlash = 0.15f;  // 판정 순간 판정 색으로 잠깐 남았다 사라진다
    private static readonly Color GuideColor = new Color(1f, 0.28f, 0.18f, 0.85f);   // 공중 물기 예고와 같은 색

    // 10-14 화룡 위협 — 2초 동안 바닥을 미끄러져 와 자기 발밑 원(2.5 m)만 휘둘렀다(플레이어가 거기 없으면 끝)
    private const float LandRadius    = 3f;     // 착지 충격 — 예고 원은 도약 내내 차오른다
    private const float LandDamageMul = 1f;
    private const float JumpLeadShare = 0.5f;   // 도약 목표 = 도약 시간의 절반 뒤 플레이어 자리
    private const float SwingRadius   = 3f;     // 휘두르기 판정 원 — 정면으로 SwingForward만큼 앞
    private const float SwingForward  = 1.5f;
    private const float StepMax       = 1.5f;   // 휘두를 때마다 플레이어 쪽으로 한 걸음
    private const float StepKeep      = 3f;     // 플레이어와 이만큼은 둔다
    private const float StepTime      = 0.2f;

    private Phase   _phase;
    private Vector3 _startPos;
    private Vector3 _targetPos;
    private Vector3 _stepFrom, _stepTo;
    private DragonBossWarningZone _landGuide;
    private float   _timer;
    private float   _arcHeight;
    private int     _swingIndex;
    private int     _totalSwings;
    private bool    _hitApplied;
    private bool    _swingSfxPlayed;
    private bool    _guideShown;
    private DragonBossWarningZone _guide;
    private DragonClawTrailController _clawTrail;

    internal DragonClawSlashState(DragonClawSlashPatternSO data) : base(data) { }

    internal void Reset()
    {
        _phase      = Phase.Done;
        _swingIndex = 0;
        _timer      = 0f;
        _arcHeight  = 0f;
        _clawTrail  = null;
        _guide      = null;   // 남은 예고는 수명이 다하면 스스로 사라진다
    }

    public override void Enter(MonsterContext ctx)
    {
        if (ctx.Agent != null) ctx.Agent.enabled = false;
        _clawTrail = (ctx.Monster as DragonBossMonster)?.ClawTrailController;

        _phase      = Phase.Jumping;
        _timer      = 0f;
        _startPos   = ctx.Transform.position;
        _targetPos  = ctx.Runtime.PlayerTarget != null
            ? DragonPatternFloorUtils.ClampInsideWalls(DragonThreat.PredictPlayer(ctx, Data.JumpDuration * JumpLeadShare), 2f)
            : _startPos;
        // 착지 자리 예고 — 도약 내내 차오르고 다 차는 순간(착지)이 판정
        Vector3 landGuidePos = _targetPos;
        landGuidePos.y = DragonPatternFloorUtils.GetFloorY(_targetPos, ctx.Runtime.SpawnPosition.y);
        _landGuide = DragonBossWarningZone.CreateCircle("DragonClawLandWarning", landGuidePos, LandRadius, GuideColor, Data.JumpDuration + 0.3f);
        _landGuide.BeginFill(Mathf.Max(0.05f, Data.JumpDuration));

        // 수평 거리에 비례한 포물선 높이 — 가까우면 낮게, 멀면 최대값으로
        float horizontalDist = Vector3.Distance(
            new Vector3(_startPos.x, 0f, _startPos.z),
            new Vector3(_targetPos.x, 0f, _targetPos.z));
        // 바닥 아래로는 안 간다 — 에셋 −1이 그대로 들어가 도약 한가운데 몸이 바닥 1 m 아래로 꺼졌다(10-08 점검)
        _arcHeight = Mathf.Max(0f, Mathf.Min(Data.JumpArcHeight, horizontalDist * 0.35f));

        PlayAnim(ctx, Data.JumpUpStateName);

    }

    public override void Update(MonsterContext ctx)
    {
        _timer += Time.deltaTime;

        switch (_phase)
        {
            case Phase.Jumping:   UpdateJumping(ctx);   break;
            case Phase.Attacking: UpdateAttacking(ctx); break;
            case Phase.EndPose:   UpdateEndPose(ctx);   break;
        }
    }

    public override void Exit(MonsterContext ctx)
    {
        if (_landGuide != null) { UnityEngine.Object.Destroy(_landGuide.gameObject); _landGuide = null; }   // 도약 중에 끊겼다 — 헛예고를 지운다
        if (GetBlackboard(ctx) is DragonBossBlackboard exitBb)
            exitBb.ClawSlashCooldown = Data.Cooldown;
        _clawTrail?.StopAll();
        if (ctx.Agent == null) return;
        DragonPatternFloorUtils.EnsureAgentOnNavMesh(ctx);
        if (ctx.Agent.isOnNavMesh) ctx.Agent.isStopped = false;
    }

    // ── Jump ────────────────────────────────────────────────────────────────

    private void UpdateJumping(MonsterContext ctx)
    {
        float t = Mathf.Clamp01(_timer / Data.JumpDuration);

        Vector3 flat = Vector3.Lerp(_startPos, _targetPos, t);
        float   arc  = Mathf.Sin(t * Mathf.PI) * _arcHeight;
        // 착지 지점의 Y는 플레이어 위치(_targetPos.y)가 아닌 실제 바닥 높이를 사용 —
        // 그렇지 않으면 NavMeshAgent.Warp이 바닥과 어긋난 위치에서 실패해 착지 후 이동이 안 됨
        float groundY = DragonPatternFloorUtils.GetFloorY(flat, ctx.Runtime.SpawnPosition.y);
        ctx.Transform.position = new Vector3(flat.x, groundY + arc, flat.z);

        Vector3 dir = _targetPos - ctx.Transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.01f)
            ctx.Transform.rotation = Quaternion.RotateTowards(
                ctx.Transform.rotation,
                Quaternion.LookRotation(dir),
                360f * Time.deltaTime);

        if (t >= 1f)
        {
            Land(ctx);
            StartAttacking(ctx);
        }
    }

    // 착지 충격 — 예고 원 안의 플레이어(수평)
    private void Land(MonsterContext ctx)
    {
        if (_landGuide != null) _landGuide.TransitionToHitPhase(GuideHitFlash);
        _landGuide = null;
        Vector3 at = ctx.Transform.position;
        RunFx.Play(RunFxSlot.Ring, at + Vector3.up * 0.1f, LandRadius / 1.36f, new Color(0.62f, 0.5f, 0.38f, 1f));   // 충격파가 판정 원까지(Ring 배율 1당 1.36 m — 10-13 실측)
        BossImpactFeedback.TriggerCameraShake(0.08f, 0.25f);   // 살짝
        var p = ctx.Runtime.PlayerTarget;
        if (p == null) return;
        Vector3 d = p.position - at; d.y = 0f;
        if (d.magnitude <= LandRadius)
            p.GetComponent<PlayerController>()?.TakeDamage(
                Mathf.RoundToInt(ctx.Config.stat.attackPower * Data.DamageMultiplier * LandDamageMul), ctx.Monster.gameObject, false, HitWeight.Heavy);
    }

    private void StartAttacking(MonsterContext ctx)
    {
        if (ctx.Agent != null)
        {
            DragonPatternFloorUtils.EnsureAgentOnNavMesh(ctx);
            if (ctx.Agent.isOnNavMesh)
                ctx.Agent.isStopped = true;
        }

        // Face player exactly before attacking
        if (ctx.Runtime.PlayerTarget != null)
        {
            Vector3 dir = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.01f)
                ctx.Transform.rotation = Quaternion.LookRotation(dir);
        }

        _phase          = Phase.Attacking;
        _timer          = 0f;
        _swingIndex     = 0;
        _totalSwings    = Data.ClawCount * 2;
        _hitApplied     = false;
        _swingSfxPlayed = false;
        _guideShown     = false;

        BeginStep(ctx);
        PlayCurrentSwingAnim(ctx);
    }

    // 휘두를 때마다 플레이어 쪽으로 돌아서 한 걸음(최대 1.5 m · 3 m는 둔다) — 예고 원은 걸음 끝 기준으로 그린다
    private void BeginStep(MonsterContext ctx)
    {
        _stepFrom = ctx.Transform.position;
        _stepTo   = _stepFrom;
        var p = ctx.Runtime.PlayerTarget;
        if (p == null) return;
        Vector3 to = p.position - _stepFrom; to.y = 0f;
        if (to.sqrMagnitude < 0.01f) return;
        ctx.Transform.rotation = Quaternion.LookRotation(to);
        float step = Mathf.Clamp(to.magnitude - StepKeep, 0f, StepMax);
        Vector3 want = _stepFrom + to.normalized * step;
        want = DragonPatternFloorUtils.ClampInsideWalls(want, 2f);
        want.y = _stepFrom.y;
        _stepTo = want;
    }

    private Vector3 SwingCenter(MonsterContext ctx)
    {
        Vector3 fwd = ctx.Transform.forward; fwd.y = 0f;
        Vector3 c = _stepTo + (fwd.sqrMagnitude > 0.01f ? fwd.normalized : Vector3.forward) * SwingForward;
        c.y = DragonPatternFloorUtils.GetFloorY(c, ctx.Runtime.SpawnPosition.y);
        return c;
    }

    // ── Claw attack loop ─────────────────────────────────────────────────────

    private void UpdateAttacking(MonsterContext ctx)
    {
        if (_swingIndex >= _totalSwings) { FinishPattern(ctx); return; }
        if (_timer <= StepTime + Time.deltaTime && (_stepTo - _stepFrom).sqrMagnitude > 0.0001f)
        {
            Vector3 next = Vector3.Lerp(_stepFrom, _stepTo, Mathf.Clamp01(_timer / StepTime));
            if (ctx.Agent != null && ctx.Agent.isActiveAndEnabled && ctx.Agent.isOnNavMesh) ctx.Agent.Move(next - ctx.Transform.position);
            else ctx.Transform.position = next;
        }

        if (!_swingSfxPlayed && _timer >= Data.SwingSfxDelay)
        {
            _swingSfxPlayed = true;
            Managers.Sound?.PlayEffectAt(Data.ClawSfx, ctx.Transform.position);
        }

        // 판정 GuideLead초 전 발밑 원 예고 — 바닥 가이드 없는 근접 2타라 해방기 2페이지에서 3번 중 2번 다 맞았다(10-03 개선 1-4)
        if (!_guideShown && _timer >= Data.HitTime - GuideLead)
        {
            _guideShown = true;
            ShowGuide(ctx);
        }

        if (!_hitApplied && _timer >= Data.HitTime)
        {
            _hitApplied = true;
            if (_guide != null) _guide.TransitionToHitPhase(GuideHitFlash);
            _guide = null;
            ApplyHit(ctx);
        }

        if (_timer >= Data.ClawAnimDuration)
        {
            _clawTrail?.StopTrail(_swingIndex % 2 == 0);
            _swingIndex     = _swingIndex + 1;
            _timer          = 0f;
            _hitApplied     = false;
            _swingSfxPlayed = false;
            _guideShown     = false;

            if (_swingIndex < _totalSwings)
            {
                BeginStep(ctx);
                PlayCurrentSwingAnim(ctx);
            }
            else
                StartEndPose();
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void PlayCurrentSwingAnim(MonsterContext ctx)
    {
        bool isLeft = (_swingIndex % 2 == 0);
        string name = isLeft ? Data.ClawLStateName : Data.ClawRStateName;
        PlayAnim(ctx, name);
        _clawTrail?.StartTrail(isLeft);
    }

    private static void PlayAnim(MonsterContext ctx, string stateName)
    {
        if (ctx.Animator != null && ctx.Animator.HasState(0, Animator.StringToHash(stateName)))
            ctx.Animator.CrossFade(stateName, 0.1f, 0, 0f);
    }

    private void ApplyHit(MonsterContext ctx)
    {
        var hits = Physics.OverlapSphere(SwingCenter(ctx), SwingRadius);   // 정면 1.5 m 앞 원(10-14)
        foreach (var col in hits)
        {
            var player = col.GetComponent<PlayerController>()
                      ?? col.GetComponentInParent<PlayerController>();
            if (player == null) continue;
            player.TakeDamage(Mathf.RoundToInt(ctx.Config.stat.attackPower * Data.DamageMultiplier), ctx.Monster.gameObject);
            break;
        }
    }

    /// <summary>판정과 같은 원(화룡 발밑 · AttackRadius) — 다 차는 순간이 판정. 공중 물기 예고와 같은 화룡 가이드.</summary>
    private void ShowGuide(MonsterContext ctx)
    {
        Vector3 pos = SwingCenter(ctx);
        _guide = DragonBossWarningZone.CreateCircle(
            "DragonClawSlashWarning", pos, SwingRadius, GuideColor, GuideLead + 0.3f);
        _guide.BeginFill(Mathf.Max(0.05f, Data.HitTime - _timer));
    }

    private void StartEndPose()
    {
        _phase = Phase.EndPose;
        _timer = 0f;
    }

    private void UpdateEndPose(MonsterContext ctx)
    {
        if (_timer >= Data.EndPoseDuration)
            FinishPattern(ctx);
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

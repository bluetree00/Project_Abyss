using UnityEngine;
using UnityEngine.AI;
using Random = UnityEngine.Random;

namespace RelicFairy.Monster
{
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// Idle
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class DKIdleState : IMonsterState
{
    public void Enter(MonsterContext ctx)
    {
        if (ctx.Agent != null && ctx.Agent.isOnNavMesh) ctx.Agent.ResetPath();
        PlayAnim(ctx, ctx.Animation.idleStateName);
    }

    public void Update(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null || ctx.Monster.IsPlayerDead()) return;
        // 추적 없이 즉시 공격 대기 상태로 전환
        ctx.Monster.ChangeState<AttackReadyState>();
    }

    public void Exit(MonsterContext ctx) { }

    private static void PlayAnim(MonsterContext ctx, string stateName)
    {
        if (ctx.Animator == null || string.IsNullOrEmpty(stateName)) return;
        if (!ctx.Animator.HasState(0, Animator.StringToHash(stateName))) return;
        ctx.Animator.speed = 1f;
        ctx.Animator.CrossFade(stateName, ctx.Animation.crossFadeDuration);
    }
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// Chase — Run/Walk1 거리 기반 속도 + StrafeLeft/Right 견제
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class DKChaseState : IMonsterState
{
    private const float FaceSpeed       = 8f;
    private const float EngageDistance  = 2.5f;
    private const float ChaseStopDist   = 1.0f;

    // Run → Walk1 전환 거리
    private const float RunThreshold    = 8f;
    // 스트레이프 진입 허용 최대 거리
    private const float StrafeMaxRange  = 8f;
    // 모드별 NavMesh 속도 배율
    private const float WalkSpeedMult   = 0.6f;
    private const float StrafeSpeedMult = 0.5f;
    // 모드 유지 시간 범위
    private const float ApproachMinTime = 2.0f;
    private const float ApproachMaxTime = 4.5f;
    private const float StrafeMinTime   = 1.2f;
    private const float StrafeMaxTime   = 2.8f;

    private const string WalkAnim        = "Walk1";
    private const string StrafeLeftAnim  = "StrafeLeft";
    private const string StrafeRightAnim = "StrafeRight";

    private enum ChaseMode { Approaching, Strafing }

    private ChaseMode _mode;
    private float     _modeTimer;
    private float     _modeExpiry;
    private int       _strafeDir;   // +1=오른쪽, -1=왼쪽
    private string    _currentAnim;

    public void Enter(MonsterContext ctx)
    {
        if (ctx.Agent != null)
        {
            ctx.Agent.enabled = true;
            if (!ctx.Agent.isOnNavMesh) ctx.Monster.TrySnapAgentToNavMesh();
            ctx.Agent.updatePosition  = true;
            ctx.Agent.updateRotation  = false;
            if (ctx.Agent.isOnNavMesh)
            {
                ctx.Agent.isStopped        = false;
                ctx.Agent.stoppingDistance = ChaseStopDist;
                ctx.Agent.speed            = ctx.Stat.moveSpeed * ctx.Runtime.SpeedMultiplier;
            }
        }

        _mode        = ChaseMode.Approaching;
        _modeTimer   = 0f;
        _modeExpiry  = Random.Range(ApproachMinTime, ApproachMaxTime);
        _strafeDir   = 1;
        _currentAnim = ctx.Animation.chaseStateName;

        ApplyAnimSpeed(ctx);
        // 즉시 시작 — 블렌딩 중 멈춤 버그 방지
        PlayAnimImmediate(ctx, ctx.Animation.chaseStateName);
    }

    public void Update(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null || ctx.Monster.IsPlayerDead())
        {
            ctx.Monster.ChangeState<PatrolState>();
            return;
        }

        if (ctx.Runtime.DistToPlayer <= EngageDistance)
        {
            ctx.Monster.ChangeState<AttackReadyState>();
            return;
        }

        _modeTimer += Time.deltaTime;
        FacePlayer(ctx);

        float dist = ctx.Runtime.DistToPlayer;

        if (_mode == ChaseMode.Approaching)
            UpdateApproaching(ctx, dist);
        else
            UpdateStrafing(ctx, dist);
    }

    public void Exit(MonsterContext ctx)
    {
        if (ctx.Agent != null)
        {
            ctx.Agent.updateRotation = true;
            if (ctx.Agent.isOnNavMesh) ctx.Agent.ResetPath();
        }
    }

    // ── Approaching ──────────────────────────────────────

    private void UpdateApproaching(MonsterContext ctx, float dist)
    {
        bool isRunRange  = dist > RunThreshold;
        string nextAnim  = isRunRange ? ctx.Animation.chaseStateName : WalkAnim;
        float  speedMult = isRunRange ? 1f : WalkSpeedMult;

        if (_currentAnim != nextAnim)
        {
            _currentAnim = nextAnim;
            PlayAnimBlend(ctx, nextAnim);
        }

        if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
        {
            ctx.Agent.speed = ctx.Stat.moveSpeed * ctx.Runtime.SpeedMultiplier * speedMult;
            ctx.Agent.SetDestination(ctx.Runtime.PlayerTarget.position);
        }

        // 걷기 범위 안에서 일정 시간 후 스트레이프 전환
        if (_modeTimer >= _modeExpiry && !isRunRange)
            EnterStrafeMode(ctx);
    }

    // ── Strafing ─────────────────────────────────────────

    private void UpdateStrafing(MonsterContext ctx, float dist)
    {
        if (_modeTimer >= _modeExpiry || dist > StrafeMaxRange)
        {
            _mode       = ChaseMode.Approaching;
            _modeTimer  = 0f;
            _modeExpiry = Random.Range(ApproachMinTime, ApproachMaxTime);

            bool   isRun     = dist > RunThreshold;
            string nextAnim  = isRun ? ctx.Animation.chaseStateName : WalkAnim;
            _currentAnim = nextAnim;
            PlayAnimBlend(ctx, nextAnim);

            if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
                ctx.Agent.speed = ctx.Stat.moveSpeed * ctx.Runtime.SpeedMultiplier *
                                  (isRun ? 1f : WalkSpeedMult);
            return;
        }

        // 플레이어 방향 수직으로 이동 — 원형 견제
        if (ctx.Runtime.PlayerTarget != null && ctx.Agent != null && ctx.Agent.isOnNavMesh)
        {
            Vector3 toPlayer = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
            toPlayer.y = 0f;
            if (toPlayer.sqrMagnitude > 0.001f)
            {
                Vector3 perp = Vector3.Cross(Vector3.up, toPlayer.normalized) * _strafeDir;
                ctx.Agent.SetDestination(ctx.Transform.position + perp * 2.5f);
            }
        }
    }

    private void EnterStrafeMode(MonsterContext ctx)
    {
        _mode       = ChaseMode.Strafing;
        _modeTimer  = 0f;
        _modeExpiry = Random.Range(StrafeMinTime, StrafeMaxTime);
        _strafeDir  = Random.value > 0.5f ? 1 : -1;

        string strafeAnim = _strafeDir > 0 ? StrafeRightAnim : StrafeLeftAnim;
        _currentAnim = strafeAnim;
        PlayAnimBlend(ctx, strafeAnim);

        if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
            ctx.Agent.speed = ctx.Stat.moveSpeed * ctx.Runtime.SpeedMultiplier * StrafeSpeedMult;
    }

    // ── 헬퍼 ──────────────────────────────────────────────

    private static void FacePlayer(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null) return;
        Vector3 dir = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f) return;
        ctx.Transform.rotation = Quaternion.Slerp(
            ctx.Transform.rotation, Quaternion.LookRotation(dir), Time.deltaTime * FaceSpeed);
    }

    private static void ApplyAnimSpeed(MonsterContext ctx)
    {
        if (ctx.Animator == null) return;
        ctx.Animator.speed = (ctx.Monster as DeathKnightBossMonster)?.DKBlackboard.AnimSpeedMult ?? 1f;
    }

    private static void PlayAnimImmediate(MonsterContext ctx, string stateName)
    {
        if (ctx.Animator == null || string.IsNullOrEmpty(stateName)) return;
        if (!ctx.Animator.HasState(0, Animator.StringToHash(stateName))) return;
        ctx.Animator.speed = (ctx.Monster as DeathKnightBossMonster)?.DKBlackboard.AnimSpeedMult ?? 1f;
        ctx.Animator.Play(stateName, 0, 0f);
    }

    private static void PlayAnimBlend(MonsterContext ctx, string stateName)
    {
        if (ctx.Animator == null || string.IsNullOrEmpty(stateName)) return;
        if (!ctx.Animator.HasState(0, Animator.StringToHash(stateName))) return;
        ctx.Animator.speed = (ctx.Monster as DeathKnightBossMonster)?.DKBlackboard.AnimSpeedMult ?? 1f;
        ctx.Animator.CrossFade(stateName, 0.15f);
    }
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// AttackReady
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class DKAttackReadyState : IMonsterState
{
    private const float FaceSpeed   = 5f;
    private const float SettleDelay = 0.35f;

    public void Enter(MonsterContext ctx)
    {
        if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
        {
            ctx.Agent.isStopped = true;
            ctx.Agent.velocity  = Vector3.zero;
            ctx.Agent.ResetPath();
        }

        (ctx.Monster as DeathKnightBossMonster)?.EnsurePatternDelay(SettleDelay);
        // 패턴 대기 중에는 Idle 애니메이션 유지 — 플레이어 방향 추적 없음
        PlayAnim(ctx, ctx.Animation.idleStateName);
    }

    public void Update(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null || ctx.Monster.IsPlayerDead())
        {
            ctx.Monster.ChangeState<PatrolState>();
            return;
        }
        // 패턴 사용 중에만 플레이어를 바라봄 — 대기 중에는 회전 없음
    }

    public void Exit(MonsterContext ctx)
    {
        if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
            ctx.Agent.isStopped = false;
    }

    private static void FacePlayer(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null) return;
        Vector3 dir = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f) return;
        ctx.Transform.rotation = Quaternion.Slerp(
            ctx.Transform.rotation, Quaternion.LookRotation(dir), Time.deltaTime * FaceSpeed);
    }

    private static void PlayAnim(MonsterContext ctx, string stateName)
    {
        if (ctx.Animator == null || string.IsNullOrEmpty(stateName)) return;
        if (!ctx.Animator.HasState(0, Animator.StringToHash(stateName))) return;
        var dk = ctx.Monster as DeathKnightBossMonster;
        ctx.Animator.speed = dk?.DKBlackboard.AnimSpeedMult ?? 1f;
        ctx.Animator.CrossFade(stateName, ctx.Animation.crossFadeDuration);
    }
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// Attack (안전망 — 패턴이 처리하므로 AttackReady 로 위임)
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class DKAttackState : IMonsterState
{
    public void Enter(MonsterContext ctx)  => ctx.Monster.ChangeState<AttackReadyState>();
    public void Update(MonsterContext ctx) { }
    public void Exit(MonsterContext ctx)   { }
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// GetHit — gethit1/2/3 랜덤 재생
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class DKGetHitState : GetHitState
{
    private static readonly string[] HitAnims = { "GetHit1", "GetHit2", "GetHit3" };
    private const float StaggerDuration = 0.5f;

    public override void Enter(MonsterContext ctx)
    {
        if (ctx.Agent != null && ctx.Agent.isActiveAndEnabled && ctx.Agent.isOnNavMesh)
        {
            ctx.Agent.isStopped = true;
            ctx.Agent.velocity  = Vector3.zero;
            ctx.Agent.ResetPath();
        }

        ctx.Runtime.StateTimer = StaggerDuration;
        PlayRandomHitAnim(ctx);

        // 경직 중 콤보 러너 차단
        (ctx.Monster as DeathKnightBossMonster)?.SetStagger(true);
    }

    public override void Update(MonsterContext ctx)
    {
        // 매 프레임 speed=1f 강제 — 외부에서 speed를 변경해도 즉시 정상화
        if (ctx.Animator != null) ctx.Animator.speed = 1f;

        ctx.Runtime.StateTimer -= Time.deltaTime;
        if (ctx.Runtime.StateTimer > 0f) return;

        RestoreAgent(ctx);

        if (ctx.Runtime.PlayerTarget != null && !ctx.Monster.IsPlayerDead())
            ctx.Monster.ChangeState<AttackReadyState>();
        else
            ctx.Monster.ChangeState<PatrolState>();
    }

    public override void Exit(MonsterContext ctx)
    {
        if (ctx.Agent != null && ctx.Agent.isActiveAndEnabled && ctx.Agent.isOnNavMesh)
            ctx.Agent.isStopped = false;

        (ctx.Monster as DeathKnightBossMonster)?.DKBlackboard.ClearArmorBroken();

        // 경직 해제
        (ctx.Monster as DeathKnightBossMonster)?.SetStagger(false);
    }

    private static void PlayRandomHitAnim(MonsterContext ctx)
    {
        if (ctx.Animator == null) return;
        string anim = HitAnims[Random.Range(0, HitAnims.Length)];
        if (!ctx.Animator.HasState(0, Animator.StringToHash(anim))) return;
        // CrossFade 대신 Play — 블렌딩 없이 즉시 전환 (블렌딩이 슬로우처럼 보이는 현상 제거)
        ctx.Animator.speed = 1f;
        ctx.Animator.Play(anim, 0, 0f);
    }
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// Die
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class DKDieState : DieState
{
    public override void Enter(MonsterContext ctx)
    {
        if (ctx.Monster is DeathKnightBossMonster dk)
            dk.UnbindBossHudIfBoundPublic();

        // 사망 시 DK 전용 카메라 오빗 복원
        GameCameraController.Instance?.DeactivateDKPlayerOrbit(1.5f);

        base.Enter(ctx);
    }
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// Phase2 순간이동 — 공격 패턴 직전에 플레이어 주변 랜덤 위치로 이동
// 순서: 현재 위치에 VFX 스폰 → vfxDelay 대기 → 순간이동 → VFX 해제 → 다음 패턴 실행
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

/// <summary>
/// Phase2 순간이동 데코레이터 상태.
///
/// phase1FixedPosition이 지정된 경우 → 해당 위치로 순간이동 (광역 패턴 전)
/// null인 경우 → 플레이어 주변 랜덤 위치로 순간이동 (기본 근접 공격 전)
/// </summary>
public class DKPhase2TeleportState : SpecialStateBase
{
    public override SpecialStateConstraint Constraints =>
        SpecialStateConstraint.UnInterruptible | SpecialStateConstraint.MovementLocked;

    private readonly IMonsterState _nextState;
    private readonly GameObject    _vfxPrefab;
    private readonly float         _teleportDist;
    private readonly float         _vfxDelay;
    private readonly float         _postTeleportDelay;
    private readonly Vector3?      _phase1FixedPosition;
    private readonly AudioClip     _teleportInSfx;
    private readonly AudioClip     _teleportOutSfx;
    private readonly System.Action _onTeleportStart;    // 텔레포트 진입 시점에 호출 (전신 오라 끄기 등)
    private readonly System.Action _onTeleportComplete; // 텔레포트 완료(또는 스킵) 시점에 검 등장 트리거

    private GameObject _spawnedVfx;   // 출발 위치 VFX (텔레포트 시점에 제거)
    private float      _timer;
    private bool       _teleported;
    private Vector3    _telePos;      // Enter()에서 미리 계산한 목적지
    private bool       _usePhase1Pos; // 카메라 전환 방향 결정용
    private bool       _skipTeleport; // PlayerTarget 없을 때 텔레포트 스킵
    private readonly float _vfxHeightOffset;
    private readonly float _vfxScale;
    private readonly float _vfxFadeInDuration;

    public DKPhase2TeleportState(IMonsterState nextState, GameObject vfxPrefab,
                                  float teleportDist, float vfxDelay,
                                  Vector3? phase1FixedPosition = null,
                                  float postTeleportDelay = 0.35f,
                                  float vfxHeightOffset = 1.5f,
                                  float vfxScale = 3f,
                                  float vfxFadeInDuration = 0.3f,
                                  AudioClip teleportInSfx = null,
                                  AudioClip teleportOutSfx = null,
                                  System.Action onTeleportStart = null,
                                  System.Action onTeleportComplete = null)
    {
        _nextState           = nextState;
        _vfxPrefab           = vfxPrefab;
        _teleportDist        = teleportDist;
        _vfxDelay            = vfxDelay;
        _postTeleportDelay   = postTeleportDelay;
        _phase1FixedPosition = phase1FixedPosition;
        _vfxHeightOffset     = vfxHeightOffset;
        _vfxScale            = vfxScale;
        _vfxFadeInDuration   = vfxFadeInDuration;
        _teleportInSfx       = teleportInSfx;
        _teleportOutSfx      = teleportOutSfx;
        _onTeleportStart     = onTeleportStart;
        _onTeleportComplete  = onTeleportComplete;
    }

    public override void Enter(MonsterContext ctx)
    {
        _onTeleportStart?.Invoke();
        _timer        = 0f;
        _teleported   = false;
        _skipTeleport = false;

        if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
        {
            ctx.Agent.isStopped = true;
            ctx.Agent.velocity  = Vector3.zero;
            ctx.Agent.ResetPath();
        }

        // 목적지를 Enter에서 미리 계산 → 출발·도착 VFX 동시 스폰 가능
        if (_phase1FixedPosition.HasValue)
        {
            _telePos      = _phase1FixedPosition.Value;
            _usePhase1Pos = true;
        }
        else if (ctx.Runtime.PlayerTarget != null)
        {
            float   angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            Vector3 dir   = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
            _telePos      = ctx.Runtime.PlayerTarget.position + dir * _teleportDist;
            // 플레이어가 점프 중이어도 보스는 항상 지면에 스폰되도록 Y를 바닥 높이로 스냅
            _telePos.y    = DragonPatternFloorUtils.GetFloorY(_telePos, ctx.Transform.position.y);
            _usePhase1Pos = false;
        }
        else
        {
            _skipTeleport = true;
        }

        // 도착 위치에만 스폰 (출발 위치는 스폰하지 않음)
        // EffectBehaviour가 ObjectPooler.Despawn으로 수명을 자체 관리하므로 BossEffectPool 미사용
        if (!_skipTeleport && _vfxPrefab != null)
            SpawnVfx(_telePos);

        // 출발 지점 사운드 (TPIN)
        if (!_skipTeleport)
            Managers.Sound?.PlayEffectAt(_teleportInSfx, ctx.Transform.position);
    }

    public override void Update(MonsterContext ctx)
    {
        _timer += Time.deltaTime;
        if (_timer < _vfxDelay) return;

        // ── 텔레포트 실행 (1회만) ──────────────────────────
        if (!_teleported)
        {
            _teleported = true;

            if (_skipTeleport)
            {
                _onTeleportComplete?.Invoke();
                ctx.Monster.ChangeState(_nextState);
                return;
            }

            // 출발 VFX 제거 (빈 자리에 남으면 안 됨)
            if (_spawnedVfx != null)
            {
                Object.Destroy(_spawnedVfx);
                _spawnedVfx = null;
            }

            // 카메라 전환
            if (_usePhase1Pos)
                GameCameraController.Instance?.ActivateDKPlayerOrbit(1.0f);
            else
                GameCameraController.Instance?.DeactivateDKPlayerOrbit(1.0f);

            if (ctx.Agent != null)
                ctx.Agent.Warp(_telePos);
            else
                ctx.Transform.position = _telePos;

            // 도착 지점 사운드 (TPOUT)
            Managers.Sound?.PlayEffectAt(_teleportOutSfx, _telePos);

            // 플레이어를 향해 바라봄
            if (ctx.Runtime.PlayerTarget != null)
            {
                Vector3 look = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
                look.y = 0f;
                if (look.sqrMagnitude > 0.001f)
                    ctx.Transform.rotation = Quaternion.LookRotation(look);
            }

            // 텔레포트 완료 직후에 검 등장 → 출발 위치부터 트레일이 남는 문제 방지
            _onTeleportComplete?.Invoke();
        }

        // ── 텔레포트 후 짧은 텀 → 공격 시작 ──────────────
        if (_timer >= _vfxDelay + _postTeleportDelay)
            ctx.Monster.ChangeState(_nextState);
    }

    public override void Exit(MonsterContext ctx)
    {
        if (_spawnedVfx != null)
        {
            Object.Destroy(_spawnedVfx);
            _spawnedVfx = null;
        }

        if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
            ctx.Agent.isStopped = false;
    }

    private GameObject SpawnVfx(Vector3 basePos)
    {
        var go          = Object.Instantiate(_vfxPrefab, basePos + Vector3.up * _vfxHeightOffset, Quaternion.identity);
        var targetScale = Vector3.one * _vfxScale;
        go.AddComponent<VfxFadeIn>().Init(targetScale, _vfxFadeInDuration);
        return go;
    }
}
}

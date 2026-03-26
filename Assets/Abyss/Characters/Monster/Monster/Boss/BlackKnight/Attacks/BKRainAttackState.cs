using System.Collections.Generic;
using Abyss.Monster;
using UnityEngine;

/// <summary>
/// BT 리프 노드 — RainAttack (낙하 공격).
/// FullLockState: 실행 중 피격 차단.
///
/// CanExecute: RainCooldown ≤ 0
///
/// ━━ 구조 ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
/// 메인: 2라운드 × 3 추적 낙하 (항상 실행)
///
/// 동시 실행 (bb.RainPhase 기반):
///   Phase ≥ 1: 랜덤 간격마다 rainPhase1/2Count 개 운석을 스태거로 낙하
///   Phase ≥ 2: rainConcurrentInterval 마다 보스 기준 +/X 패턴 낙하
///
/// 동시 낙하는 BKConcurrentDrop (fire-and-forget) 으로 처리.
/// 추적 낙하: 경고 시간 70% 동안 플레이어 추적 → 30% 고정 → 피해.
/// </summary>
public class BKRainAttackState : FullLockState<BKRainAttackPatternSO>
{
    private readonly BossAttackBlackboard _bb;

    // 운석 패턴 중 완전 무적
    public override SpecialStateConstraint Constraints =>
        SpecialStateConstraint.UnInterruptible |
        SpecialStateConstraint.MovementLocked  |
        SpecialStateConstraint.Invincible;

    // ── 방어막 ────────────────────────────────────────────
    private GameObject _barrier;

    // ── 메인 낙하 단계 ────────────────────────────────────
    private enum DropPhase { Tracking, Locked, Interval, RoundDelay, Done, Cooldown, Draining }

    private float TrackRatio        => Data.rainTrackRatio;
    private float LockRatio         => 1f - Data.rainTrackRatio;
    private float MeteorSpawnHeight => Data.rainMeteorSpawnHeight;

    private DropPhase _dropPhase;
    private float     _timer;
    private int       _currentRound;
    private int       _dropInRound;
    private Vector3   _lockPos;

    // ── 동시 진행 타이머 ──────────────────────────────────
    private float _concurrentRandomTimer;
    private float _concurrentPatternTimer;
    private bool  _patternIsPlusPhase;

    // 패턴 기준 위치 (Enter 시 보스 위치 고정)
    private Vector3 _bossPos;

    private BossWarningIndicator _indicator;

    // ── 풀 컨텍스트 ───────────────────────────────────────
    private BKDropContext _dropCtx;

    // ── 미반납 Hit VFX 추적 (SpawnHitVfx 용) ─────────────
    private readonly List<(GameObject go, float returnAt)> _pendingVfxReturns = new();

    // ── 팔 방향 단위벡터 ─────────────────────────────────
    private static readonly Vector2[] PlusDirs =
    {
        new( 0,  1), new( 1,  0), new( 0, -1), new(-1,  0)
    };
    private static readonly Vector2[] XDirs =
    {
        new( 0.707f,  0.707f), new( 0.707f, -0.707f),
        new(-0.707f,  0.707f), new(-0.707f, -0.707f)
    };

    public BKRainAttackState(BKRainAttackPatternSO data, BossAttackBlackboard bb) : base(data)
    {
        _bb = bb;
    }

    /// <summary>BlackKnightBoss 에서 풀 생성 후 주입.</summary>
    public void SetDropContext(BKDropContext ctx) => _dropCtx = ctx;

    // ── BT 조건 ──────────────────────────────────────────
    public bool CanExecute(MonsterContext ctx)
        => _bb.RainCooldown <= 0f && ctx.Runtime.PlayerTarget != null;

    // ── Enter ────────────────────────────────────────────
    public override void Enter(MonsterContext ctx)
    {
        _indicator    = ctx.Transform.GetComponent<BossWarningIndicator>();
        _currentRound = 0;
        _dropInRound  = 0;
        _dropPhase    = DropPhase.Tracking;

        float halfInterval = Data.rainConcurrentInterval * 0.5f;
        _concurrentRandomTimer  = Random.Range(Data.rainRandomMinInterval, Data.rainRandomMaxInterval) * 0.5f;
        _concurrentPatternTimer = halfInterval;
        _patternIsPlusPhase     = true;

        _bossPos = ctx.Transform.position;

        ctx.Agent.ResetPath();
        FacePlayer(ctx);
        ctx.Animator?.CrossFade(Data.rainAnimState, 0.1f);

        _bb.AudioPool?.Play(ctx.Transform.position, Data.rainSfx, 0.5f);

        // 방어막 활성화 (보스 자식 → 보스와 함께 이동)
        if (_barrier == null)
        {
            _barrier = BKShieldBarrier.Create(
                ctx.Transform,
                Data.barrierRadius,
                Data.barrierColor,
                Data.barrierPeakColor,
                Data.barrierPulsePeriod).gameObject;
        }

        StartTrackingDrop(ctx);
    }

    // ── Update ───────────────────────────────────────────
    public override void Update(MonsterContext ctx)
    {
        _timer -= Time.deltaTime;
        TickVfxReturns();

        // 운석 패턴 전체 구간 동안 애니메이션이 다른 상태로 전환되지 않도록 강제 유지
        if (ctx.Animator != null && !ctx.Animator.IsInTransition(0))
        {
            if (!ctx.Animator.GetCurrentAnimatorStateInfo(0).IsName(Data.rainAnimState))
                ctx.Animator.CrossFade(Data.rainAnimState, 0.05f);
        }

        // ── 메인 낙하 상태머신 ─────────────────────────
        switch (_dropPhase)
        {
            case DropPhase.Tracking:
                if (_timer <= 0f) LockCurrentDrop(ctx);
                break;

            case DropPhase.Locked:
                if (_timer <= 0f)
                {
                    DealDropDamage(ctx, _lockPos);
                    SpawnHitVfx(_lockPos);
                    _indicator?.HideLeapTarget();
                    _dropPhase = DropPhase.Interval;
                    _timer     = Data.rainDropInterval;
                }
                break;

            case DropPhase.Interval:
                if (_timer <= 0f) AdvanceDrop(ctx);
                break;

            case DropPhase.RoundDelay:
                if (_timer <= 0f)
                {
                    _dropInRound = 0;
                    StartTrackingDrop(ctx);
                }
                break;

            case DropPhase.Done:
                // 메인 운석 완료 — 베리어 페이드아웃 후 쿨다운
                if (_barrier != null)
                {
                    _barrier.GetComponent<BKShieldBarrier>()?.FadeOut(Data.rainBarrierFadeDuration);
                    _barrier = null; // 이후 Exit에서 중복 Destroy 방지
                }
                _dropPhase = DropPhase.Cooldown;
                _timer     = Data.rainCooldownDelay;
                break;

            case DropPhase.Cooldown:
                // 3초 대기 후 ChaseState 복귀.
                // return으로 빠져나가 동시 운석 추가 스폰을 차단한다.
                if (_timer <= 0f)
                    ctx.Monster.ChangeState<ChaseState>();
                return;

            case DropPhase.Draining:
                // 안전망
                ctx.Monster.ChangeState<ChaseState>();
                return;
        }

        // ── 동시 낙하 ──────────────────────────────────
        int dmg = Mathf.RoundToInt(ctx.Config.stat.attackPower
                                   * Data.rainDamageMul
                                   * ctx.Runtime.AttackMultiplier);

        if (_bb.RainPhase >= 1)
            TickConcurrentRandom(ctx, dmg);

        if (_bb.RainPhase >= 2)
            TickConcurrentPattern(dmg);
    }

    // ── Exit ─────────────────────────────────────────────
    public override void Exit(MonsterContext ctx)
    {
        _indicator?.HideLeapTarget();
        _bb.RainCooldown = Data.rainCooldown;

        // 방어막 제거
        if (_barrier != null)
        {
            Object.Destroy(_barrier);
            _barrier = null;
        }

        // 미반납 Hit VFX 즉시 회수
        foreach (var (go, _) in _pendingVfxReturns)
            _dropCtx.HitVfxPool?.Return(go);
        _pendingVfxReturns.Clear();
    }

    // ── 추적 드롭 시작 ────────────────────────────────────
    private void StartTrackingDrop(MonsterContext ctx)
    {
        _indicator?.ShowLeapTarget(ctx.Runtime.PlayerTarget, Data.rainHitRadius,
                                   Data.rainWarningTime + 0.1f);
        _dropPhase = DropPhase.Tracking;
        _timer     = Data.rainWarningTime * TrackRatio;
    }

    private void LockCurrentDrop(MonsterContext ctx)
    {
        _lockPos = ctx.Runtime.PlayerTarget != null
            ? ctx.Runtime.PlayerTarget.position
            : ctx.Transform.position + ctx.Transform.forward * 4f;
        _lockPos.y = ctx.Transform.position.y;

        _indicator?.LockLeapTarget(_lockPos);
        SpawnFallingMeteor(_lockPos, Data.rainWarningTime * LockRatio);

        _dropPhase = DropPhase.Locked;
        _timer     = Data.rainWarningTime * LockRatio;
    }

    // ── 메인 드롭 진행 ────────────────────────────────────
    private void AdvanceDrop(MonsterContext ctx)
    {
        _dropInRound++;
        if (_dropInRound < Mathf.Max(1, Data.rainDropsPerRound))
        {
            StartTrackingDrop(ctx);
        }
        else
        {
            _currentRound++;
            if (_currentRound < Mathf.Max(1, Data.rainRounds))
            {
                _dropPhase = DropPhase.RoundDelay;
                _timer     = Data.rainRoundDelay;
            }
            else
            {
                _dropPhase = DropPhase.Done;
            }
        }
    }

    // ── 동시 랜덤 낙하 (스태거, 페이즈별 개수) ──────────
    private void TickConcurrentRandom(MonsterContext ctx, int dmg)
    {
        _concurrentRandomTimer -= Time.deltaTime;
        if (_concurrentRandomTimer > 0f) return;

        // 다음 인터벌: 랜덤 범위에서 선택
        _concurrentRandomTimer = Random.Range(Data.rainRandomMinInterval, Data.rainRandomMaxInterval);

        Vector3 playerPos = ctx.Runtime.PlayerTarget != null
            ? ctx.Runtime.PlayerTarget.position
            : ctx.Transform.position;

        int   count        = _bb.RainPhase >= 2 ? Data.rainPhase2Count : Data.rainPhase1Count;
        float fallDuration = Data.rainWarningTime * LockRatio;

        for (int i = 0; i < Mathf.Max(1, count); i++)
        {
            Vector2 rnd     = Random.insideUnitCircle.normalized * Random.Range(Data.rainRandomRadiusMin, Data.rainRandomRadius);
            Vector3 dropPos = playerPos + new Vector3(rnd.x, 0, rnd.y);
            dropPos.y       = playerPos.y;

            float delay = Random.Range(0f, Data.rainRandomStaggerSpread);

            BKConcurrentDrop.Spawn(
                dropPos,
                Data.rainWarningTime,
                fallDuration,
                Data.rainHitRadius,
                dmg,
                _dropCtx,
                delay);
        }
    }

    // ── 동시 패턴 낙하 (+/X 교대, 팔 방향 동시) ──────────
    private void TickConcurrentPattern(int dmg)
    {
        _concurrentPatternTimer -= Time.deltaTime;
        if (_concurrentPatternTimer > 0f) return;

        _concurrentPatternTimer = Data.rainConcurrentInterval;

        Vector2[] dirs   = _patternIsPlusPhase ? PlusDirs : XDirs;
        int       count  = Mathf.Max(1, Data.rainPatternCount);
        float     start  = Data.rainPatternRadius;
        float     gap    = Data.rainPatternSpacing;

        var positions = new Vector3[dirs.Length * count];
        int idx = 0;
        foreach (var dir in dirs)
        {
            for (int j = 0; j < count; j++)
            {
                float   dist = start + gap * j;
                Vector3 pos  = _bossPos + new Vector3(dir.x * dist, 0, dir.y * dist);
                pos.y        = _bossPos.y;
                positions[idx++] = pos;
            }
        }

        _patternIsPlusPhase = !_patternIsPlusPhase;

        BKConcurrentDrop.Spawn(
            positions,
            Data.rainWarningTime,
            Data.rainWarningTime * LockRatio,
            Data.rainHitRadius,
            dmg,
            _dropCtx);
    }

    // ── 운석 VFX (메인 추적 낙하용) ──────────────────────
    private void SpawnFallingMeteor(Vector3 lockPos, float fallDuration)
    {
        Vector3    spawnPos = lockPos + Vector3.up * MeteorSpawnHeight;
        GameObject meteor;

        if (_dropCtx.MeteorPool != null)
        {
            meteor = _dropCtx.MeteorPool.Get(spawnPos, Quaternion.identity);
        }
        else if (Data.rainMeteorVfxPrefab != null)
        {
            meteor = Object.Instantiate(Data.rainMeteorVfxPrefab, spawnPos, Quaternion.identity);
            HalfAudioVolumes(meteor);
        }
        else return;

        var falling = meteor.GetComponent<BKFallingObject>()
                   ?? meteor.AddComponent<BKFallingObject>();
        falling.Pool = _dropCtx.MeteorPool;
        falling.Init(lockPos, fallDuration);
    }

    // ── 착지 Hit VFX (메인 추적 낙하용, 3초 후 반납) ─────
    private void SpawnHitVfx(Vector3 pos)
    {
        if (_dropCtx.HitVfxPool != null)
        {
            var vfx = _dropCtx.HitVfxPool.Get(pos, Quaternion.identity);
            _pendingVfxReturns.Add((vfx, Time.time + Data.rainHitVfxDuration));
        }
        else if (Data.rainHitVfxPrefab != null)
        {
            var hitVfx = Object.Instantiate(Data.rainHitVfxPrefab, pos, Quaternion.identity);
            HalfAudioVolumes(hitVfx);
            Object.Destroy(hitVfx, Data.rainHitVfxDuration);
        }
    }

    // ── 대기 중인 VFX 반납 체크 ──────────────────────────
    private void TickVfxReturns()
    {
        if (_pendingVfxReturns.Count == 0) return;
        float now = Time.time;
        for (int i = _pendingVfxReturns.Count - 1; i >= 0; i--)
        {
            if (now >= _pendingVfxReturns[i].returnAt)
            {
                _dropCtx.HitVfxPool?.Return(_pendingVfxReturns[i].go);
                _pendingVfxReturns.RemoveAt(i);
            }
        }
    }

    // ── 피해 ─────────────────────────────────────────────
    private void DealDropDamage(MonsterContext ctx, Vector3 worldPos)
    {
        var cols = Physics.OverlapSphere(worldPos, Data.rainHitRadius);
        foreach (var col in cols)
        {
            var player = col.GetComponent<PlayerController>()
                      ?? col.GetComponentInParent<PlayerController>();
            if (player == null) continue;

            int dmg = Mathf.RoundToInt(ctx.Config.stat.attackPower
                                       * Data.rainDamageMul
                                       * ctx.Runtime.AttackMultiplier);
            player.TakeDamage(dmg);
        }
    }

    private static void FacePlayer(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null) return;
        Vector3 dir = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.001f)
            ctx.Transform.rotation = Quaternion.LookRotation(dir);
    }

    private static void HalfAudioVolumes(GameObject go)
    {
        foreach (var src in go.GetComponentsInChildren<AudioSource>(true))
            src.volume *= 0.5f;
    }
}

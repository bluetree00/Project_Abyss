using Abyss.Monster;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// BT 리프 노드 — LeapSlam (점프 내려찍기).
/// FullLockState: 실행 중 피격 차단.
///
/// CanExecute: LeapCooldown ≤ 0
/// 흐름:
///   ① LeapUp (0.3 초) — 보스 위로 이동, 렌더러 숨김, 착지 추적 원 시작
///   ② Track  (leapTrackDuration) — 착지 원이 플레이어를 따라감 (흰→주황)
///   ③ Locked (leapLockDuration)  — 착지 원 고정, 붉게 점등
///   ④ Slam   (순간)  — 착지 위치로 이동, 렌더러 복구, VFX, AoE 피해
///   ⑤ Recovery (0.5 초) — 착지 후 짧은 경직
///   ⑥ ChaseState 복귀
/// </summary>
public class BKLeapSlamState : FullLockState<BKLeapSlamPatternSO>
{
    private readonly BossAttackBlackboard _bb;

    private enum Phase { LeapUp, Track, Locked, Slam, Recovery, Done }

    private float LeapUpDuration   => Data.leapUpDuration;
    private float RecoveryDuration => Data.leapRecoveryDuration;

    private Phase   _phase;
    private float   _timer;

    private Vector3    _leapStartPos;
    private Vector3    _lockedSlamPos;
    private bool       _lockedPosSet;   // Exit 에서 NavMesh 기준점 판단용
    private Renderer[] _renderers;

    private BossWarningIndicator _indicator;

    private BKEffectPool _vfxPool;
    private GameObject   _pendingVfx;
    private float        _vfxReturnAt;

    public void SetVfxPool(BKEffectPool pool) => _vfxPool = pool;

    public BKLeapSlamState(BKLeapSlamPatternSO data, BossAttackBlackboard bb) : base(data)
    {
        _bb = bb;
    }

    // ── BT 조건 ──────────────────────────────────────────
    public bool CanExecute(MonsterContext ctx) => _bb.LeapCooldown <= 0f
                                               && ctx.Runtime.PlayerTarget != null;

    // ── FSM Enter/Update/Exit ────────────────────────────
    public override void Enter(MonsterContext ctx)
    {
        _indicator    = ctx.Transform.GetComponent<BossWarningIndicator>();
        _renderers    = ctx.Transform.GetComponentsInChildren<Renderer>(true);
        _leapStartPos = ctx.Transform.position;
        _lockedPosSet = false;

        ctx.Agent.ResetPath();
        ctx.Agent.enabled = false;

        _bb.AudioPool?.Play(ctx.Transform.position, Data.leapJumpSfx, 0.5f);

        ctx.Animator?.CrossFade(Data.leapAnimState, 0.1f);

        _phase = Phase.LeapUp;
        _timer = LeapUpDuration;
    }

    public override void Update(MonsterContext ctx)
    {
        float dt = Time.deltaTime;
        _timer -= dt;

        switch (_phase)
        {
            // ── 도약 ──────────────────────────────────────
            case Phase.LeapUp:
            {
                float t = 1f - Mathf.Clamp01(_timer / LeapUpDuration);
                ctx.Transform.position = Vector3.Lerp(
                    _leapStartPos,
                    _leapStartPos + Vector3.up * Data.leapJumpHeight,
                    t);

                if (_timer <= 0f)
                {
                    SetRenderersVisible(false);

                    // 착지 추적 원 시작
                    _indicator?.ShowLeapTarget(
                        ctx.Runtime.PlayerTarget,
                        Data.leapSlamRadius,
                        Data.leapTrackDuration);

                    _phase = Phase.Track;
                    _timer = Data.leapTrackDuration;
                }
                break;
            }

            // ── 추적 ──────────────────────────────────────
            case Phase.Track:
            {
                if (_timer <= 0f)
                {
                    // 현재 플레이어 위치에 착지 원 고정
                    _lockedSlamPos = ctx.Runtime.PlayerTarget != null
                        ? ctx.Runtime.PlayerTarget.position
                        : ctx.Transform.position;

                    // NavMesh 에서 정확한 지면 Y 를 구한다
                    if (NavMesh.SamplePosition(_lockedSlamPos, out NavMeshHit hit, 3f, NavMesh.AllAreas))
                        _lockedSlamPos.y = hit.position.y;
                    else
                        _lockedSlamPos.y = _leapStartPos.y;

                    _lockedPosSet = true;
                    _indicator?.LockLeapTarget(_lockedSlamPos);

                    _phase = Phase.Locked;
                    _timer = Data.leapLockDuration;
                }
                break;
            }

            // ── 고정 (착지 직전) ──────────────────────────
            case Phase.Locked:
            {
                if (_timer <= 0f)
                {
                    // 착지 위치로 텔레포트 + NavMesh 즉시 복구 (Recovery 동안 부유 방지)
                    Vector3 slamPos = _lockedSlamPos;
                    if (NavMesh.SamplePosition(slamPos, out NavMeshHit slamHit, 4f, NavMesh.AllAreas))
                        slamPos = slamHit.position;
                    _lockedSlamPos         = slamPos;
                    ctx.Transform.position = slamPos;

                    if (ctx.Agent != null)
                    {
                        ctx.Agent.enabled = true;
                        ctx.Agent.Warp(slamPos);
                        ctx.Agent.ResetPath(); // Recovery 동안 이동 없음
                    }

                    SetRenderersVisible(true);

                    // 착지 사운드
                    _bb.AudioPool?.Play(_lockedSlamPos, Data.leapLandSfx, 0.5f);

                    // VFX
                    if (_vfxPool != null)
                    {
                        _pendingVfx   = _vfxPool.Get(_lockedSlamPos, Quaternion.identity);
                        _vfxReturnAt  = Time.time + Data.leapVfxDuration;
                    }
                    else if (Data.leapSlamVfxPrefab != null)
                    {
                        var vfx = Object.Instantiate(Data.leapSlamVfxPrefab, _lockedSlamPos,
                                                     Quaternion.identity);
                        HalfAudioVolumes(vfx);
                        Object.Destroy(vfx, Data.leapVfxDuration);
                    }

                    // 피해
                    DealSlamDamage(ctx);

                    _indicator?.HideLeapTarget();

                    _phase = Phase.Slam;
                    _timer = 0f; // 즉시 Recovery 로
                }
                break;
            }

            case Phase.Slam:
            {
                _phase = Phase.Recovery;
                _timer = RecoveryDuration;
                break;
            }

            case Phase.Recovery:
            {
                // 풀 VFX 회수 타이머
                if (_pendingVfx != null && Time.time >= _vfxReturnAt)
                {
                    _vfxPool?.Return(_pendingVfx);
                    _pendingVfx = null;
                }

                if (_timer <= 0f)
                    _phase = Phase.Done;
                break;
            }

            case Phase.Done:
                ctx.Monster.ChangeState<ChaseState>();
                break;
        }
    }

    public override void Exit(MonsterContext ctx)
    {
        _indicator?.HideLeapTarget();
        SetRenderersVisible(true);

        // 미회수 VFX 즉시 반납
        if (_pendingVfx != null)
        {
            _vfxPool?.Return(_pendingVfx);
            _pendingVfx = null;
        }

        // NavMesh 복구 — Slam 단계에서 이미 Agent 활성화; 비정상 종료(LeapUp/Track/Locked 중 Exit) 대비
        if (ctx.Agent != null && !ctx.Agent.enabled)
        {
            Vector3 navRef = _lockedPosSet ? _lockedSlamPos : ctx.Transform.position;
            if (NavMesh.SamplePosition(navRef, out NavMeshHit hit, 4f, NavMesh.AllAreas))
                navRef = hit.position;
            ctx.Transform.position = navRef;
            ctx.Agent.enabled      = true;
            ctx.Agent.Warp(navRef);
        }

        _bb.LeapCooldown = Data.leapCooldown;
    }

    // ── 피해 ─────────────────────────────────────────────
    private void DealSlamDamage(MonsterContext ctx)
    {
        var cols = Physics.OverlapSphere(_lockedSlamPos, Data.leapSlamRadius);
        foreach (var col in cols)
        {
            var player = col.GetComponent<PlayerController>()
                      ?? col.GetComponentInParent<PlayerController>();
            if (player == null) continue;

            int dmg = Mathf.RoundToInt(ctx.Config.stat.attackPower
                                       * Data.leapDamageMul
                                       * ctx.Runtime.AttackMultiplier);
            player.TakeDamage(dmg);

            var rb = col.GetComponent<Rigidbody>() ?? col.GetComponentInParent<Rigidbody>();
            if (rb != null)
            {
                Vector3 dir = (col.transform.position - _lockedSlamPos).normalized;
                dir.y = Data.leapKnockbackY;
                rb.AddForce(dir.normalized * ctx.Config.stat.knockbackForce * Data.leapKnockbackMul, ForceMode.Impulse);
            }
        }
    }

    // ── 렌더러 가시성 ────────────────────────────────────
    private void SetRenderersVisible(bool visible)
    {
        if (_renderers == null) return;
        foreach (var r in _renderers)
            if (r != null) r.enabled = visible;
    }

    // ── VFX 오디오 볼륨 50% 감소 ────────────────────────
    private static void HalfAudioVolumes(GameObject go)
    {
        foreach (var src in go.GetComponentsInChildren<AudioSource>(true))
            src.volume *= 0.5f;
    }
}

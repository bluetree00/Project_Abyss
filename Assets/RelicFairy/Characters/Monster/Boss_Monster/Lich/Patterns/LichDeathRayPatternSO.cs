using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 리치 데스레이 (Death Ray) — Phase 2 전용 채널링 빔.
///
/// 흐름: 채널링(channelDuration) — 플레이어 방향 천천히 회전 추적 + 전방 콘 틱 데미지
///       → 복귀(recoveryDuration)
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/Boss/Lich/Lich_DeathRayPattern", fileName = "Lich_DeathRayPattern")]
public class LichDeathRayPatternSO : BossPatternSO
{
    [Header("Death Ray — Timing")]
    [Tooltip("빔 채널링 총 시간 (초)")]
    public float channelDuration = 3.0f;
    [Tooltip("채널링 종료 후 복귀 시간 (초)")]
    public float recoveryDuration = 0.8f;

    [Header("Death Ray — Beam")]
    [Tooltip("빔 사정거리 (m)")]
    public float beamRange = 20f;
    [Tooltip("빔 판정 콘 반각 (도). 20도 = 전방 40도 부채꼴.")]
    public float coneHalfAngle = 20f;
    [Tooltip("플레이어 방향 추적 최대 회전 속도 (도/초)")]
    public float trackingSpeed = 60f;

    [Header("Death Ray — Damage")]
    [Tooltip("틱 데미지 적용 간격 (초)")]
    public float tickInterval = 0.25f;
    [Tooltip("기본 attackPower에 곱할 틱 데미지 배율")]
    public float tickDamageMultiplier = 0.3f;

    [Header("Death Ray — Cooldown")]
    [Tooltip("패턴 완료 후 재사용 대기 시간 (초)")]
    public float patternCooldown = 10f;

    // ── 런타임 ───────────────────────────────────────────
    private LichDeathRayState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new LichDeathRayState(this);
    public override void OnRecycled()                       => _state = new LichDeathRayState(this);

    public override bool CanExecute(BossPatternContext ctx)
    {
        if (ctx.Ctx.Runtime.PlayerTarget == null) return false;
        var lichBB = (ctx.Boss as LichMonster)?.LichBB;
        return lichBB == null || lichBB.DeathRayCooldown <= 0f;
    }

    public override SpecialStateBase GetRuntimeState() => _state;
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// LichDeathRayState — MovementLocked (이동 잠금, 중단 가능)
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class LichDeathRayState : MovementLockedState<LichDeathRayPatternSO>
{
    private enum Phase { Channel, Recovery }

    private Phase _phase;
    private float _channelTimer;
    private float _tickTimer;

    public LichDeathRayState(LichDeathRayPatternSO data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        _phase        = Phase.Channel;
        _channelTimer = 0f;
        _tickTimer    = 0f;

        if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
        {
            ctx.Agent.isStopped = true;
            ctx.Agent.ResetPath();
        }
    }

    public override void Update(MonsterContext ctx)
    {
        float dt = Time.deltaTime;

        if (_phase == Phase.Channel)
        {
            _channelTimer += dt;

            TrackPlayer(ctx, dt);

            _tickTimer += dt;
            if (_tickTimer >= Data.tickInterval)
            {
                _tickTimer -= Data.tickInterval;
                DealTickDamage(ctx);
            }

            if (_channelTimer >= Data.channelDuration)
            {
                _phase        = Phase.Recovery;
                _channelTimer = 0f;
            }
        }
        else
        {
            _channelTimer += dt;
            if (_channelTimer >= Data.recoveryDuration)
                ctx.Monster.ChangeState<ChaseState>();
        }
    }

    public override void Exit(MonsterContext ctx)
    {
        if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
            ctx.Agent.isStopped = false;

        var lich = ctx.Monster as LichMonster;
        if (lich?.LichBB != null)
            lich.LichBB.DeathRayCooldown = Data.patternCooldown;
    }

    private void TrackPlayer(MonsterContext ctx, float dt)
    {
        if (ctx.Runtime.PlayerTarget == null) return;

        Vector3 dir = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f) return;

        Quaternion target = Quaternion.LookRotation(dir);
        ctx.Transform.rotation = Quaternion.RotateTowards(
            ctx.Transform.rotation,
            target,
            Data.trackingSpeed * dt);
    }

    private void DealTickDamage(MonsterContext ctx)
    {
        if (ctx.Config?.stat == null || ctx.Runtime.PlayerTarget == null) return;

        Vector3 toPlayer = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        float   dist     = toPlayer.magnitude;

        if (dist > Data.beamRange) return;

        // 전방 콘 판정 (y축 무시)
        Vector3 flatToPlayer = toPlayer;
        flatToPlayer.y = 0f;
        if (flatToPlayer.sqrMagnitude > 0.001f &&
            Vector3.Angle(ctx.Transform.forward, flatToPlayer) > Data.coneHalfAngle)
            return;

        var player = ctx.Runtime.PlayerTarget.GetComponent<PlayerController>();
        if (player == null) return;

        int dmg = Mathf.Max(1, (int)(ctx.Config.stat.attackPower * Data.tickDamageMultiplier));
        player.TakeDamage(dmg);
    }
}
}

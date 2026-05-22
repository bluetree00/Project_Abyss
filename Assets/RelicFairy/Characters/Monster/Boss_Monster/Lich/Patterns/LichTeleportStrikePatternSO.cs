using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 리치 순간이동 타격 (Teleport Strike) 패턴 — Phase 1.
///
/// 흐름: 사라짐 연출(vanishDuration) → 플레이어 측면으로 워프
///       → 선딜(strikeDelay) → 근접 타격 → 복귀(recoveryDuration)
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/Boss/Lich/Lich_TeleportStrikePattern", fileName = "Lich_TeleportStrikePattern")]
public class LichTeleportStrikePatternSO : BossPatternSO
{
    [Header("Teleport Strike — Range")]
    [Tooltip("이 패턴 발동을 위한 최대 플레이어 거리 (m)")]
    public float maxTriggerRange = 30f;

    [Header("Teleport Strike — Timing")]
    [Tooltip("사라지는 연출 시간 (초)")]
    public float vanishDuration = 0.4f;
    [Tooltip("워프 후 타격까지 선딜레이 (초)")]
    public float strikeDelay = 0.3f;
    [Tooltip("타격 판정 지속 시간 (초)")]
    public float strikeDuration = 0.5f;
    [Tooltip("타격 후 복귀 시간 (초)")]
    public float recoveryDuration = 0.5f;

    [Header("Teleport Strike — Teleport")]
    [Tooltip("플레이어 기준 측면 오프셋 거리 (m). 좌우 무작위 선택.")]
    public float sideOffset = 2.5f;
    [Tooltip("워프 이펙트 프리팹. null이면 이펙트 없음.")]
    public GameObject teleportVfxPrefab;

    [Header("Teleport Strike — Damage")]
    [Tooltip("기본 attackPower에 곱할 배율")]
    public float damageMultiplier = 1.4f;
    [Tooltip("타격 판정 반경 (m)")]
    public float hitRadius = 2.5f;
    [Tooltip("넉백 힘 배율")]
    public float knockbackMultiplier = 1.8f;

    [Header("Teleport Strike — Cooldown")]
    [Tooltip("패턴 완료 후 재사용 대기 시간 (초)")]
    public float patternCooldown = 6f;

    // ── 런타임 ───────────────────────────────────────────
    private LichTeleportStrikeState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new LichTeleportStrikeState(this);
    public override void OnRecycled()                       => _state = new LichTeleportStrikeState(this);

    public override bool CanExecute(BossPatternContext ctx)
    {
        if (ctx.Ctx.Runtime.PlayerTarget == null) return false;
        var lichBB = (ctx.Boss as LichMonster)?.LichBB;
        if (lichBB != null && lichBB.TeleportStrikeCooldown > 0f) return false;
        return Vector3.Distance(ctx.Ctx.Transform.position, ctx.Ctx.Runtime.PlayerTarget.position) <= maxTriggerRange;
    }

    public override SpecialStateBase GetRuntimeState() => _state;
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// LichTeleportStrikeState — FullLock (이동 + 중단 불가)
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class LichTeleportStrikeState : FullLockState<LichTeleportStrikePatternSO>
{
    private enum Phase { Vanish, StrikeDelay, Strike, Recovery }

    private Phase _phase;
    private float _timer;
    private bool  _hasDealt;

    public LichTeleportStrikeState(LichTeleportStrikePatternSO data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        _phase   = Phase.Vanish;
        _timer   = 0f;
        _hasDealt = false;

        if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
        {
            ctx.Agent.isStopped = true;
            ctx.Agent.ResetPath();
        }

        SpawnVfx(ctx, ctx.Transform.position);
    }

    public override void Update(MonsterContext ctx)
    {
        _timer += Time.deltaTime;

        switch (_phase)
        {
            case Phase.Vanish:
                if (_timer >= Data.vanishDuration)
                {
                    TeleportToPlayer(ctx);
                    _phase = Phase.StrikeDelay;
                    _timer = 0f;
                }
                break;

            case Phase.StrikeDelay:
                FacePlayer(ctx);
                if (_timer >= Data.strikeDelay)
                {
                    _phase = Phase.Strike;
                    _timer = 0f;
                }
                break;

            case Phase.Strike:
                if (!_hasDealt)
                {
                    _hasDealt = true;
                    DealDamage(ctx);
                }
                if (_timer >= Data.strikeDuration)
                {
                    _phase = Phase.Recovery;
                    _timer = 0f;
                }
                break;

            case Phase.Recovery:
                if (_timer >= Data.recoveryDuration)
                    ctx.Monster.ChangeState<ChaseState>();
                break;
        }
    }

    public override void Exit(MonsterContext ctx)
    {
        if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
            ctx.Agent.isStopped = false;

        var lich = ctx.Monster as LichMonster;
        if (lich?.LichBB != null)
            lich.LichBB.TeleportStrikeCooldown = Data.patternCooldown;
    }

    private void TeleportToPlayer(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null) return;

        Vector3 playerPos = ctx.Runtime.PlayerTarget.position;
        // 플레이어의 좌/우 무작위 선택
        Vector3 playerRight = ctx.Runtime.PlayerTarget.right;
        Vector3 sideDir     = (Random.value > 0.5f) ? playerRight : -playerRight;
        Vector3 targetPos   = playerPos + sideDir * Data.sideOffset;
        targetPos.y         = playerPos.y;

        SpawnVfx(ctx, targetPos);

        if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
            ctx.Agent.Warp(targetPos);
        else
            ctx.Transform.position = targetPos;
    }

    private void DealDamage(MonsterContext ctx)
    {
        if (ctx.Config?.stat == null || ctx.Runtime.PlayerTarget == null) return;

        float dist = Vector3.Distance(ctx.Transform.position, ctx.Runtime.PlayerTarget.position);
        if (dist > Data.hitRadius) return;

        var player = ctx.Runtime.PlayerTarget.GetComponent<PlayerController>();
        if (player == null) return;

        int dmg = Mathf.Max(1, (int)(ctx.Config.stat.attackPower * Data.damageMultiplier));
        player.TakeDamage(dmg);

        Vector3 dir = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        dir.y = 0.3f;
        if (dir.sqrMagnitude > 0.001f) dir.Normalize();
        player.ApplyKnockback(dir * ctx.Config.stat.knockbackForce * Data.knockbackMultiplier);
    }

    private void SpawnVfx(MonsterContext ctx, Vector3 pos)
    {
        if (Data.teleportVfxPrefab == null) return;
        var go = Object.Instantiate(Data.teleportVfxPrefab, pos, ctx.Transform.rotation);
        Object.Destroy(go, 2f);
    }

    private static void FacePlayer(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null) return;
        Vector3 dir = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.001f)
            ctx.Transform.rotation = Quaternion.LookRotation(dir);
    }
}
}

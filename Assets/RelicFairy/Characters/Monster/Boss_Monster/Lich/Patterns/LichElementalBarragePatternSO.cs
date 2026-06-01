using RelicFairy.UI;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 리치 원소 난사 (Elemental Barrage) 패턴 — Phase 1 일반 공격.
///
/// 흐름: Cast(조준 선딜, 180°/s 추적) → Shoot(shotCount발, Active 프레임: 방향 고정)
///       → Recovery → ChaseState.
///
/// 각 존은 telegraphDuration 동안 노란 disc로 예고한 뒤 activeDuration 동안 충돌 판정으로 전환.
/// Dark Souls UX 원칙: Startup(Cast)에서는 각속도 제한 추적,
///                     Active(Shoot)에서는 방향을 완전히 고정해 플레이어가 옆으로 피할 수 있게 한다.
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/Boss/Lich/Lich_ElementalBarragePattern", fileName = "Lich_ElementalBarragePattern")]
public class LichElementalBarragePatternSO : BossPatternSO
{
    [Header("Elemental Barrage — Range")]
    [Tooltip("유효 사정거리 (m)")]
    public float maxRange = 30f;

    [Header("Elemental Barrage — Timing")]
    [Tooltip("첫 발사 전 시전 시간 (초)")]
    public float castDuration = 0.6f;
    [Tooltip("발사 간 간격 (초)")]
    public float shotInterval = 0.5f;
    [Tooltip("마지막 발사 후 복귀 대기 시간 (초)")]
    public float recoveryDuration = 0.5f;

    [Header("Elemental Barrage — Shots")]
    [Tooltip("연속 발사 횟수")]
    public int shotCount = 3;

    [Header("Elemental Barrage — Zone")]
    [Tooltip("노란 예고 존 표시 시간 (초) — 플레이어가 피할 시간")]
    public float telegraphDuration = 1.0f;
    [Tooltip("빨간 충돌 존 유지 시간 (초)")]
    public float activeDuration = 0.6f;
    [Tooltip("충돌 판정 반경 (m)")]
    public float zoneRadius = 1.2f;
    [Tooltip("바닥 기준 Y 오프셋")]
    public float groundYOffset = 0.05f;

    [Header("Elemental Barrage — Damage")]
    [Tooltip("공격력 대비 데미지 배율")]
    public float damageMultiplier = 0.7f;
    [Tooltip("존 안에 있는 동안 데미지 틱 간격 (초)")]
    public float tickInterval = 0.5f;

    [Header("Elemental Barrage — Scatter")]
    [Tooltip("발사 1회당 소환 존 수. 1이면 기존 동작.")]
    public int zonesPerShot = 5;
    [Tooltip("플레이어 위치에서 랜덤으로 흩어지는 최대 반경 (m). 0이면 정확한 위치.")]
    public float scatterRadius = 3f;

    [Header("Elemental Barrage — Cooldown")]
    [Tooltip("패턴 완료 후 재사용 대기 시간 (초)")]
    public float patternCooldown = 5f;

    // ── 런타임 ───────────────────────────────────────────
    private LichElementalBarrageState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new LichElementalBarrageState(this);
    public override void OnRecycled()                       => _state = new LichElementalBarrageState(this);

    public override bool CanExecute(BossPatternContext ctx)
    {
        if (ctx.Ctx.Runtime.PlayerTarget == null) return false;
        var lichBB = (ctx.Boss as LichMonster)?.LichBB;
        if (lichBB != null && lichBB.ElementalBarrageCooldown > 0f) return false;
        return Vector3.Distance(ctx.Ctx.Transform.position, ctx.Ctx.Runtime.PlayerTarget.position) <= maxRange;
    }

    public override SpecialStateBase GetRuntimeState() => _state;
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// LichElementalBarrageState — UnInterruptible
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class LichElementalBarrageState : UnInterruptibleState<LichElementalBarragePatternSO>
{
    private enum Phase { Cast, Shoot, Recovery }

    private Phase _phase;
    private float _timer;
    private int   _shotsFired;

    public LichElementalBarrageState(LichElementalBarragePatternSO data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        _phase      = Phase.Cast;
        _timer      = 0f;
        _shotsFired = 0;

        ctx.Animator?.CrossFade("ElementalBarrage", 0.1f);

        var mc = (ctx.Monster as LichMonster)?.MovementController;
        mc?.RequestMovementState(LichMovementState.CircleStrafe);
        mc?.SetLocked(true);

        UI_BossBark.Show("원소여, 쏟아져라!", BossBarkType.PatternAnnounce);
        FacePlayerTracking(ctx, 360f);
    }

    public override void Update(MonsterContext ctx)
    {
        _timer += Time.deltaTime;

        switch (_phase)
        {
            case Phase.Cast:
                // Startup 프레임: 각속도 제한 추적 (180°/s)
                FacePlayerTracking(ctx, 180f);
                if (_timer >= Data.castDuration)
                {
                    SpawnZone(ctx);
                    _shotsFired = 1;
                    _phase      = Phase.Shoot;
                    _timer      = 0f;
                }
                break;

            case Phase.Shoot:
                // Active 프레임: 방향 고정 — 플레이어가 옆으로 이동해 회피 가능
                if (_timer >= Data.shotInterval)
                {
                    if (_shotsFired < Data.shotCount)
                    {
                        SpawnZone(ctx);
                        _shotsFired++;
                        _timer = 0f;
                    }
                    else
                    {
                        _phase = Phase.Recovery;
                        _timer = 0f;
                    }
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
        (ctx.Monster as LichMonster)?.MovementController?.SetLocked(false);

        var lich = ctx.Monster as LichMonster;
        if (lich?.LichBB != null)
            lich.LichBB.ElementalBarrageCooldown = Data.patternCooldown;
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 헬퍼
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private void SpawnZone(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null) return;

        Vector3 basePos = ctx.Runtime.PlayerTarget.position;
        int dmg = Mathf.Max(1, (int)(ctx.Config.stat.attackPower * Data.damageMultiplier));
        int count = Mathf.Max(1, Data.zonesPerShot);

        for (int i = 0; i < count; i++)
        {
            Vector2 rand2D = Data.scatterRadius > 0f
                ? Random.insideUnitCircle * Data.scatterRadius
                : Vector2.zero;
            Vector3 pos = basePos + new Vector3(rand2D.x, Data.groundYOffset, rand2D.y);
            LichDarkRainZone.Spawn(pos, Data.zoneRadius, Data.telegraphDuration, Data.activeDuration,
                dmg, Data.tickInterval);
        }
    }

    /// <summary>Startup 프레임용 각속도 제한 추적.</summary>
    private static void FacePlayerTracking(MonsterContext ctx, float angularDegPerSec)
    {
        if (ctx.Runtime.PlayerTarget == null) return;
        Vector3 dir = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f) return;
        ctx.Transform.rotation = Quaternion.RotateTowards(
            ctx.Transform.rotation,
            Quaternion.LookRotation(dir),
            angularDegPerSec * Time.deltaTime);
    }
}
}

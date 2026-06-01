using System.Collections.Generic;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 리치 해골 소환수 — LichSkeletonSummonPattern이 소환하는 일반 몬스터.
/// Lich 프리팹을 기반으로 의상·무기를 OnInitialized에서 비활성화해 뼈대만 표시한다.
/// NavMesh가 없는 환경(테스트씬 등)에서도 플레이어를 직접 추적한다.
/// </summary>
public class LichSkeletonMonster : MonsterBase
{
    // ── 상수 ─────────────────────────────────────────────
    public const string PrefabAddress = "LichSkeleton/LichSkeleton";

    // ── MonsterBase 추상 멤버 ─────────────────────────────
    protected override string ConfigAddress   => "LichSkeleton/LichSkeletonConfig";
    protected override string DataAddress     => string.Empty;
    protected override string HeadBoneName    => null;
    protected override float  HPBarHeadOffset => 0.2f;

    // ── Private ────────────────────────────────────────────
    private static readonly HashSet<string> HiddenObjectNames = new()
    {
        "SK_BookOpen Equip",
        "SK_Scythe Equip",
        "Bookss",
        "Clothing",
        "SkirtSeparate",
        "HoodDown",
        "HoodUp",
    };

    // ── FSM 오버라이드 ────────────────────────────────────

    protected override void RegisterStates()
    {
        base.RegisterStates();
        // NavMesh 없을 때도 직접 이동하는 ChaseState로 교체
        _fsm.RegisterAs<ChaseState>(new SkeletonDirectChaseState());
    }

    // NavMesh 유무와 관계없이 detectionRange 기준으로 감지
    public override bool ShouldStartChase(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null || IsPlayerDead()) return false;
        return ctx.Runtime.DistToPlayer <= ctx.Detection.detectionRange;
    }

    public override bool ShouldGiveUpChase(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null || IsPlayerDead()) return true;
        return ctx.Runtime.DistToPlayer > ctx.Detection.chaseGiveUpRange;
    }

    // ── OnInitialized ─────────────────────────────────────

    protected override void OnInitialized()
    {
        // 의상·무기 비활성화 — 뼈대(Phase2 스타일) 노출
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            if (HiddenObjectNames.Contains(t.name))
                t.gameObject.SetActive(false);
        }
    }
}

/// <summary>
/// NavMesh 없는 환경에서도 Transform을 직접 이동시키는 ChaseState.
/// NavMesh가 있으면 기존 NavMeshAgent 경로 탐색으로 동작한다.
/// </summary>
public class SkeletonDirectChaseState : ChaseState
{
    public override void Update(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null || ctx.Monster.IsPlayerDead())
        {
            ctx.Monster.ChangeState<PatrolState>();
            return;
        }

        if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
        {
            base.Update(ctx);
            return;
        }

        // NavMesh 없음 — 직접 이동
        if (ctx.Monster.ShouldGiveUpChase(ctx))
        {
            ctx.Monster.ChangeState<PatrolState>();
            return;
        }

        if (ctx.Monster.ShouldEnterAttackReady(ctx))
        {
            ctx.Monster.ChangeState<AttackReadyState>();
            return;
        }

        Vector3 dir = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.001f)
        {
            float speed = ctx.Stat.moveSpeed * ctx.Runtime.SpeedMultiplier;
            ctx.Transform.position += dir.normalized * speed * Time.deltaTime;
            ctx.Transform.rotation = Quaternion.Slerp(
                ctx.Transform.rotation,
                Quaternion.LookRotation(dir),
                Time.deltaTime * 10f);
        }

        KeepChaseAnimation(ctx);
    }
}
}

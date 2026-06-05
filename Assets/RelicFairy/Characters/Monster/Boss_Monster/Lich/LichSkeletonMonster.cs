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
    private const int   MaxConcurrent = 8;     // 동시 생존 상한 — 초과 시 가장 오래된 비봉인 해골 정리
    private const float Lifetime      = 25f;   // 개체 수명(초). 봉인 해골은 면제.

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

    // 살아있는 해골 레지스트리 — 동시 상한·수명·전투 종료 정리에 사용.
    private static readonly List<LichSkeletonMonster> Live = new();

    private float _spawnTime;
    private bool  _lifetimeExpired;

    // ── 수명주기 ──────────────────────────────────────────

    protected override void OnEnable()
    {
        base.OnEnable();
        _spawnTime       = Time.time;
        _lifetimeExpired = false;
        Live.Add(this);
        EnforceCap();
    }

    protected override void OnDisable()
    {
        Live.Remove(this);
        base.OnDisable();
    }

    protected override void Update()
    {
        base.Update();
        if (_lifetimeExpired) return;
        if (_runtime != null && _runtime.IsDead) return;
        if (Lifetime > 0f && Time.time - _spawnTime >= Lifetime)
        {
            _lifetimeExpired = true;
            // 봉인 해골은 수명 면제 — 시간 초과로 사라지면 SealBreaker가 영구 무적(소프트락)된다.
            if (TryGetComponent<SealSkeletonMarker>(out _)) return;
            Cull(this);
        }
    }

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

    // ── 정적 관리 ──────────────────────────────────────────

    /// <summary>전투 종료(보스 퇴각·사망) 시 생존 해골을 모두 정리한다.</summary>
    public static void DespawnAll()
    {
        for (int i = Live.Count - 1; i >= 0; i--)
        {
            var s = Live[i];
            if (s != null) Managers.ObjectPooler.Despawn(s.gameObject);
        }
        Live.Clear();
    }

    /// <summary>동시 상한 초과 시 가장 오래된 비봉인 해골부터 정리.</summary>
    private static void EnforceCap()
    {
        int idx = 0;
        while (Live.Count > MaxConcurrent && idx < Live.Count)
        {
            var s = Live[idx];
            if (s == null)                                    { Live.RemoveAt(idx); continue; }
            if (s.TryGetComponent<SealSkeletonMarker>(out _)) { idx++; continue; } // 봉인 해골 면제
            Cull(s);
        }
    }

    private static void Cull(LichSkeletonMonster s)
    {
        Live.Remove(s);
        if (s != null) Managers.ObjectPooler.Despawn(s.gameObject);
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

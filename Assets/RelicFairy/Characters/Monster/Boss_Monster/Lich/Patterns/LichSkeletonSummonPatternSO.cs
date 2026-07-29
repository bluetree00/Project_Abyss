using UnityEngine;
using UnityEngine.AI;

namespace RelicFairy.Monster
{
/// <summary>
/// 리치 해골 소환 (Skeleton Summon) 패턴 — 일반 공격 (Phase 1/2 공용).
///
/// 흐름: 이동 유지 (RetreatFloat 힌트) → 시전(castDuration) → 소환(spawnCount마리)
///       → 복귀(recoveryDuration)
/// 소환 프리팹 null 시 바닥 Disc 가이드만 표시 (테스트용).
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/Boss/Lich/Lich_SkeletonSummonPattern", fileName = "Lich_SkeletonSummonPattern")]
public class LichSkeletonSummonPatternSO : BossPatternSO
{
    [Header("Skeleton Summon — Timing")]
    [Tooltip("시전 자세 유지 시간 (초)")]
    public float castDuration = 1.2f;
    [Tooltip("소환 후 복귀 대기 시간 (초)")]
    public float recoveryDuration = 0.5f;

    [Header("Skeleton Summon — Spawn")]
    [Tooltip("소환할 해골 수")]
    public int spawnCount = 2;
    [Tooltip("해골 소환 프리팹. null이면 Disc 가이드만 표시.")]
    public GameObject skeletonPrefab;
    [Tooltip("보스 주변 소환 반경 (m)")]
    public float spawnRadius = 4f;

    [Header("Skeleton Summon — Cooldown")]
    [Tooltip("패턴 완료 후 재사용 대기 시간 (초)")]
    public float patternCooldown = 12f;

    // ── 런타임 ───────────────────────────────────────────
    private LichSkeletonSummonState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new LichSkeletonSummonState(this);
    public override void OnRecycled()                       => _state = new LichSkeletonSummonState(this);

    public override bool CanExecute(BossPatternContext ctx)
    {
        if (ctx.Ctx.Runtime.PlayerTarget == null) return false;
        var lichBB = (ctx.Boss as LichMonster)?.LichBB;
        return lichBB == null || lichBB.SkeletonSummonCooldown <= 0f;
    }

    public override SpecialStateBase GetRuntimeState() => _state;
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// LichSkeletonSummonState — UnInterruptible (이동 자유)
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class LichSkeletonSummonState : UnInterruptibleState<LichSkeletonSummonPatternSO>
{
    private enum Phase { Cast, Recovery }

    private Phase        _phase;
    private float        _timer;
    private bool         _summoned;
    private GameObject[] _spawnGuides;

    public LichSkeletonSummonState(LichSkeletonSummonPatternSO data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        _phase    = Phase.Cast;
        _timer    = 0f;
        _summoned = false;

        ctx.Animator?.CrossFade("SkeletonSummon", 0.1f);

        var mc = (ctx.Monster as LichMonster)?.MovementController;
        mc?.RequestMovementState(LichMovementState.RetreatFloat);
        mc?.SetLocked(true);

        // Cast 중 소환 예정 위치를 Summon(보라) disc로 미리 표시
        float groundY = (ctx.Monster as LichMonster)?.SpawnGroundY ?? 0f;
        _spawnGuides = new GameObject[Data.spawnCount];
        for (int i = 0; i < Data.spawnCount; i++)
        {
            float   angle    = i * (360f / Data.spawnCount);
            Vector3 offset   = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * Data.spawnRadius;
            Vector3 guidePos = ctx.Transform.position + offset;
            if (NavMesh.SamplePosition(guidePos, out NavMeshHit navHit, 10f, NavMesh.AllAreas))
                guidePos = navHit.position;
            else
                guidePos.y = groundY;
            _spawnGuides[i] = PatternGuideHelper.Disc(guidePos, 0.8f, PatternGuideHelper.Summon);
        }
    }

    public override void Update(MonsterContext ctx)
    {
        _timer += Time.deltaTime;

        if (_phase == Phase.Cast)
        {
            if (!_summoned && _timer >= Data.castDuration)
            {
                _summoned = true;
                // 소환 완료 — 예고 가이드 즉시 제거 (Recovery 동안 잔존 방지)
                if (_spawnGuides != null)
                {
                    for (int i = 0; i < _spawnGuides.Length; i++)
                        if (_spawnGuides[i] != null) Object.Destroy(_spawnGuides[i]);
                    _spawnGuides = null;
                }
                Summon(ctx);
                _phase = Phase.Recovery;
                _timer = 0f;
            }
        }
        else
        {
            if (_timer >= Data.recoveryDuration)
                ctx.Monster.ChangeState<ChaseState>();
        }
    }

    public override void Exit(MonsterContext ctx)
    {
        if (_spawnGuides != null)
        {
            for (int i = 0; i < _spawnGuides.Length; i++)
                if (_spawnGuides[i] != null) Object.Destroy(_spawnGuides[i]);
            _spawnGuides = null;
        }

        (ctx.Monster as LichMonster)?.MovementController?.SetLocked(false);

        var lich = ctx.Monster as LichMonster;
        if (lich?.LichBB != null)
            lich.LichBB.SkeletonSummonCooldown = Data.patternCooldown;
    }

    private void Summon(MonsterContext ctx)
    {
        float groundY = (ctx.Monster as LichMonster)?.SpawnGroundY ?? 0f;
        for (int i = 0; i < Data.spawnCount; i++)
        {
            float   angle  = i * (360f / Data.spawnCount);
            Vector3 offset = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * Data.spawnRadius;
            Vector3 pos    = ctx.Transform.position + offset;
            if (NavMesh.SamplePosition(pos, out NavMeshHit navHit, 10f, NavMesh.AllAreas))
                pos = navHit.position;
            else
                pos.y = groundY;

            if (Data.skeletonPrefab != null)
            {
                Object.Instantiate(Data.skeletonPrefab, pos, Quaternion.identity);
            }
            else
            {
                PatternGuideHelper.Disc(pos, 0.8f, PatternGuideHelper.Summon, lifetime: 1.5f);
            }
        }
    }
}
}

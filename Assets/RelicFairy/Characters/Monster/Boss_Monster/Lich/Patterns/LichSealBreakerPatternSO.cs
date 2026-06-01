using System.Collections.Generic;
using RelicFairy.UI;
using UnityEngine;
using UnityEngine.AI;

namespace RelicFairy.Monster
{
/// <summary>
/// 봉인 해골 (Seal Breaker) 패턴 — 메카닉 타입, 리치 무적.
///
/// 흐름: 리치가 봉인 시전 → sealCount마리의 봉인 해골 소환(파란 마커 + SealSkeletonMarker 부착)
///             + additionalSummonCount마리의 방해 해골 소환
///       → 플레이어가 sealActiveTime 내에 봉인 해골을 모두 처치하면 패턴 성공 → ChaseState
///       → 시간 초과 시 벌칙 데미지 → ChaseState (즉사/좌절 없음)
///
/// 리치는 InvincibleState 내에 있으므로 봉인 해골이 모두 죽을 때까지 데미지를 받지 않는다.
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/Boss/Lich/Lich_SealBreakerPattern", fileName = "Lich_SealBreakerPattern")]
public class LichSealBreakerPatternSO : BossPatternSO
{
    [Header("Seal Breaker — Seals")]
    [Tooltip("봉인 해골 수. 모두 처치해야 패턴 해제.")]
    public int sealCount = 3;
    [Tooltip("봉인 해골 소환 반경 (m)")]
    public float sealRadius = 5f;
    [Tooltip("봉인 유지 시간 (초). 0 이하면 제한 없음 — 해골을 모두 처치할 때까지 무한 유지.")]
    public float sealActiveTime = 0f;
    [Tooltip("시간 초과 시 플레이어에게 주는 벌칙 데미지 (공격력 대비 배율)")]
    public float punishmentMultiplier = 1.2f;
    [Tooltip("봉인 해골 프리팹. null이면 Disc 가이드만 표시 (테스트용).")]
    public GameObject sealSkeletonPrefab;

    [Header("Seal Breaker — Support Skeletons")]
    [Tooltip("방해 해골 추가 소환 수")]
    public int additionalSummonCount = 2;
    [Tooltip("방해 해골 소환 반경 (m)")]
    public float additionalSummonRadius = 7f;
    [Tooltip("방해 해골 프리팹. null이면 소환 없음.")]
    public GameObject additionalSkeletonPrefab;

    [Header("Seal Breaker — Visuals")]
    [Tooltip("봉인 해골 바닥 마커 Y 오프셋")]
    public float groundYOffset = 0.05f;

    [Header("Seal Breaker — Periodic Attack")]
    [Tooltip("봉인 중 주기적 광역 공격 간격 (초). 0이면 미사용.")]
    public float periodicAttackInterval = 2.5f;
    [Tooltip("광역 공격 1회당 소환 존 수")]
    public int periodicZoneCount = 3;
    [Tooltip("플레이어 위치 기준 존 산개 반경 (m)")]
    public float periodicScatterRadius = 4f;
    [Tooltip("광역 공격 존 반경 (m)")]
    public float periodicZoneRadius = 1.5f;
    [Tooltip("광역 공격 예고 시간 (초)")]
    public float periodicTelegraphDuration = 0.8f;
    [Tooltip("광역 공격 판정 유지 시간 (초)")]
    public float periodicActiveDuration = 0.5f;
    [Tooltip("광역 공격 데미지 배율 (공격력 대비)")]
    public float periodicDamageMultiplier = 0.5f;

    [Header("Seal Breaker — Aimed Attack")]
    [Tooltip("봉인 중 주기 공격 시 플레이어 위치에 직접 조준하는 소규모 존 수")]
    public int aimedZoneCount = 1;
    [Tooltip("조준 존 반경 (m). 직접 타격감.")]
    public float aimedZoneRadius = 1.2f;
    [Tooltip("조준 존 데미지 배율 (공격력 대비)")]
    public float aimedDamageMultiplier = 0.7f;

    [Header("Seal Breaker — Cooldown")]
    [Tooltip("패턴 완료 후 재사용 대기 시간 (초)")]
    public float patternCooldown = 35f;

    // ── 런타임 ───────────────────────────────────────────
    private LichSealBreakerState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new LichSealBreakerState(this);
    public override void OnRecycled()                       => _state = new LichSealBreakerState(this);

    public override bool CanExecute(BossPatternContext ctx)
    {
        if (ctx.Ctx.Runtime.PlayerTarget == null) return false;
        var lichBB = (ctx.Boss as LichMonster)?.LichBB;
        return lichBB == null || lichBB.SealBreakerCooldown <= 0f;
    }

    public override SpecialStateBase GetRuntimeState() => _state;
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// LichSealBreakerState — Invincible (모든 데미지 무시)
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class LichSealBreakerState : InvincibleState<LichSealBreakerPatternSO>
{
    private static readonly Color InvincibleRingColor = new Color(0.55f, 0f, 1f, 0.6f);

    private static readonly string[] CastAnimations = { "ArcaneOrb", "ElementalBarrage", "MagicBolt" };

    private float      _elapsed;
    private float      _periodicTimer;
    private int        _sealsRemaining;
    private bool       _finished;
    private int        _attackCycle;
    private GameObject _invincibilityGuide;
    private readonly List<SealSkeletonMarker> _sealMarkers = new();

    public LichSealBreakerState(LichSealBreakerPatternSO data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        _elapsed        = 0f;
        _periodicTimer  = 0f;
        _sealsRemaining = Data.sealCount;
        _finished       = false;
        _attackCycle    = 0;

        ctx.Animator?.CrossFade("SkeletonSummon", 0.1f);

        var mc = (ctx.Monster as LichMonster)?.MovementController;
        mc?.RequestMovementState(LichMovementState.IdleHover);
        mc?.SetLocked(true);

        UI_BossBark.Show("봉인이여, 깨어나라!", BossBarkType.PatternAnnounce);
        UI_BossBark.Show($"리치가 무적이 됐다! 빛나는 봉인 해골을 모두 처치하라! (0 / {Data.sealCount})", BossBarkType.MerlinNarration);

        _invincibilityGuide = PatternGuideHelper.Disc(
            ctx.Transform.position,
            Data.sealRadius + 0.5f,
            InvincibleRingColor);

        Debug.Log($"[SealBreaker] 시작 — 봉인 해골 {Data.sealCount}마리, 제한 시간 {Data.sealActiveTime}s");

        SpawnSealSkeletons(ctx);
        SpawnAdditionalSkeletons(ctx);
    }

    public override void Update(MonsterContext ctx)
    {
        if (_finished) return;

        _elapsed += Time.deltaTime;

        // 봉인 중 항상 플레이어 방향으로 회전
        if (ctx.Runtime.PlayerTarget != null)
            (ctx.Monster as LichMonster)?.MovementController?.FaceTowards(
                ctx.Runtime.PlayerTarget.position, Time.deltaTime);

        if (_sealsRemaining <= 0)
        {
            FinishPattern(ctx, success: true);
            return;
        }

        if (Data.sealActiveTime > 0f && _elapsed >= Data.sealActiveTime)
        {
            FinishPattern(ctx, success: false);
            return;
        }

        if (Data.periodicAttackInterval > 0f)
        {
            _periodicTimer += Time.deltaTime;
            if (_periodicTimer >= Data.periodicAttackInterval)
            {
                _periodicTimer = 0f;
                SpawnPeriodicAttack(ctx);
            }
        }
    }

    public override void Exit(MonsterContext ctx)
    {
        PatternGuideHelper.SafeDestroy(ref _invincibilityGuide);

        foreach (var m in _sealMarkers)
            if (m != null) m.OnKilled -= OnSealKilled;
        _sealMarkers.Clear();

        (ctx.Monster as LichMonster)?.MovementController?.SetLocked(false);

        var lich = ctx.Monster as LichMonster;
        if (lich?.LichBB != null)
            lich.LichBB.SealBreakerCooldown = Data.patternCooldown;
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 헬퍼
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private void SpawnSealSkeletons(MonsterContext ctx)
    {
        float angleStep = 360f / Data.sealCount;
        Vector3 origin  = ctx.Transform.position;
        float groundY   = (ctx.Monster as LichMonster)?.SpawnGroundY ?? 0f;

        for (int i = 0; i < Data.sealCount; i++)
        {
            float   angle  = i * angleStep;
            Vector3 offset = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * Data.sealRadius;
            Vector3 pos    = origin + offset;
            if (NavMesh.SamplePosition(pos, out NavMeshHit navHit, 10f, NavMesh.AllAreas))
                pos = new Vector3(navHit.position.x, navHit.position.y + Data.groundYOffset, navHit.position.z);
            else
                pos.y = groundY + Data.groundYOffset;

            if (Data.sealSkeletonPrefab != null)
            {
                var go     = Object.Instantiate(Data.sealSkeletonPrefab, pos, Quaternion.identity);
                var marker = go.AddComponent<SealSkeletonMarker>();
                marker.OnKilled += OnSealKilled;
                _sealMarkers.Add(marker);
            }
            else
            {
                PatternGuideHelper.Disc(pos, 0.7f, PatternGuideHelper.Seal, lifetime: Data.sealActiveTime);
            }
        }
    }

    private void SpawnAdditionalSkeletons(MonsterContext ctx)
    {
        if (Data.additionalSkeletonPrefab == null || Data.additionalSummonCount <= 0) return;

        float   angleStep = 360f / Data.additionalSummonCount;
        Vector3 origin    = ctx.Transform.position;
        float   groundY   = (ctx.Monster as LichMonster)?.SpawnGroundY ?? 0f;

        for (int i = 0; i < Data.additionalSummonCount; i++)
        {
            float   angle  = i * angleStep + 30f;
            Vector3 offset = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * Data.additionalSummonRadius;
            Vector3 pos    = origin + offset;
            if (NavMesh.SamplePosition(pos, out NavMeshHit navHit, 10f, NavMesh.AllAreas))
                pos = navHit.position;
            else
                pos.y = groundY;
            Object.Instantiate(Data.additionalSkeletonPrefab, pos, Quaternion.identity);
        }
    }

    private void SpawnPeriodicAttack(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null || ctx.Config?.stat == null) return;

        // 마법 시전 애니메이션 재생 (순환)
        string animName = CastAnimations[_attackCycle % CastAnimations.Length];
        ctx.Animator?.CrossFade(animName, 0.1f);
        _attackCycle++;

        Vector3 basePos = ctx.Runtime.PlayerTarget.position;
        int scatterDmg = Mathf.Max(1, (int)(ctx.Config.stat.attackPower * Data.periodicDamageMultiplier));
        int aimedDmg   = Mathf.Max(1, (int)(ctx.Config.stat.attackPower * Data.aimedDamageMultiplier));

        // 플레이어 위치 직접 조준 존 — 짧은 예고로 긴장감 증가
        for (int i = 0; i < Data.aimedZoneCount; i++)
        {
            LichDarkRainZone.Spawn(basePos + new Vector3(0f, 0.05f, 0f), Data.aimedZoneRadius,
                Data.periodicTelegraphDuration * 0.6f, Data.periodicActiveDuration,
                aimedDmg, 0.5f);
        }

        // 산개 존 — 추가 압박
        for (int i = 0; i < Data.periodicZoneCount; i++)
        {
            Vector2 rand2D = Random.insideUnitCircle * Data.periodicScatterRadius;
            Vector3 pos = basePos + new Vector3(rand2D.x, 0.05f, rand2D.y);
            LichDarkRainZone.Spawn(pos, Data.periodicZoneRadius,
                Data.periodicTelegraphDuration, Data.periodicActiveDuration,
                scatterDmg, 0.5f);
        }
    }

    private void OnSealKilled()
    {
        _sealsRemaining--;
        int killed = Data.sealCount - _sealsRemaining;
        if (_sealsRemaining > 0)
            UI_BossBark.Show($"봉인 파괴! ({killed} / {Data.sealCount})", BossBarkType.PatternAnnounce);
        Debug.Log($"[SealBreaker] 봉인 해골 처치 — 남은 봉인 {_sealsRemaining}/{Data.sealCount}");
    }

    private void FinishPattern(MonsterContext ctx, bool success)
    {
        _finished = true;
        PatternGuideHelper.SafeDestroy(ref _invincibilityGuide);

        if (!success)
        {
            Debug.Log($"[SealBreaker] 시간 초과 — 벌칙 데미지 x{Data.punishmentMultiplier}");
            if (ctx.Config?.stat != null && ctx.Runtime.PlayerTarget != null)
            {
                var player = ctx.Runtime.PlayerTarget.GetComponent<PlayerController>();
                if (player != null)
                {
                    int dmg = Mathf.Max(1, (int)(ctx.Config.stat.attackPower * Data.punishmentMultiplier));
                    player.TakeDamage(dmg);
                    Debug.Log($"[SealBreaker] 벌칙 데미지 {dmg} 적용");
                }
            }
            UI_BossBark.Show("봉인이 완성됐다...", BossBarkType.PatternAnnounce);
        }
        else
        {
            Debug.Log("[SealBreaker] 봉인 전부 파괴 — Phase2 진입 트리거");
            UI_BossBark.Show("봉인 파괴! 리치의 무적이 해제됐다!", BossBarkType.PatternAnnounce);
            PatternGuideHelper.Disc(
                ctx.Transform.position,
                Data.sealRadius + 1f,
                PatternGuideHelper.Safe,
                lifetime: 0.8f);

        }

        ctx.Monster.ChangeState<ChaseState>();
    }
}
}

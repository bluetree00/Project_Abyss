using System.Collections.Generic;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// DeathKnight 영혼 소환 패턴 (Phase 2 진입 직후 최초 1회 자동 발동).
///
/// 흐름:
///  MovingToCenter  → Walk1, NavMesh로 그리드 중앙 이동 (max moveToCenterTime)
///  Summoning       → Idle2, IsInvincible=true, 보스에 방패 VFX 부착
///  PillarWait      → 좌·우 그룹에서 각 1개 씩 기둥을 소환 (총 2개).
///                    기둥 피격 데미지는 보스 HP에 직결(기둥 HP 한도).
///                    waitTimeout(10s) 이내 2개 모두 파괴 → 패턴 종료.
///                    타임아웃 시: 남은 기둥 HP만큼 보스 HP 회복 후 패턴 종료.
///  Recovery        → 무적 해제, 방패 VFX 제거, recoveryTime 후 AttackReadyState
/// </summary>
[CreateAssetMenu(menuName = "Abyss/Boss/DeathKnight/DK_SoulSummonPattern",
                 fileName = "DK_SoulSummonPattern")]
public class DKSoulSummonPatternSO : BossPatternSO
{
    [Header("기둥 설정")]
    [Tooltip("영혼 기둥 VFX 프리팹")]
    public GameObject pillarVfxPrefab;
    [Tooltip("기둥 파괴 시 VFX")]
    public GameObject pillarDestroyVfxPrefab;
    [Tooltip("파괴 VFX 로컬 스케일 배율")]
    public float pillarDestroyVfxScale = 0.4f;
    public float pillarHp             = 300f;
    public float pillarColliderHeight = 3.5f;
    public float pillarColliderRadius = 0.5f;
    [Tooltip("스폰 시 기둥 VFX 로컬 스케일 배율 (기본 1 = 프리팹 원본 크기)")]
    public float pillarVfxScale       = 0.4f;

    [Header("보스 VFX")]
    [Tooltip("무적 방패 VFX")]
    public GameObject bossShieldVfxPrefab;

    [Header("사운드")]
    [Tooltip("기둥 파괴 시 재생할 SFX")]
    public AudioClip pillarDestroySfx;

    [Header("타이밍")]
    public float moveToCenterTime    = 3.0f;
    public float summoningDuration   = 1.5f;
    public float waitTimeout         = 10f;
    public float recoveryTime        = 0.5f;
    [Tooltip("타임아웃 시 보스 HP 서서히 회복되는 지속 시간 (초)")]
    public float gradualHealDuration = 2.5f;
    [Tooltip("타임아웃 시 기둥 1개당 보스 최대 HP 회복 비율 (0.25 = 25%)")]
    public float pillarHealPercentPerPillar = 0.25f;

    private DKSoulSummonState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new DKSoulSummonState(this);
    public override void OnRecycled()                       => _state = new DKSoulSummonState(this);

    public override bool CanExecute(BossPatternContext ctx)
    {
        var dk = ctx.Ctx.Monster as DeathKnightBossMonster;
        if (dk == null) return false;
        // _soulGateCleared=true: 기둥 성공 파괴 → HP 55% 이상 회복 전까지 재발동 불가
        if (dk.SoulGateCleared) return false;
        return dk.HpRatio <= 0.5f && !dk.DKBlackboard.IsPhase2;
    }

    public override SpecialStateBase GetRuntimeState() => _state;
}

public class DKSoulSummonState : FullLockState<DKSoulSummonPatternSO>
{
    private const string AnimWalk  = "Walk1";
    private const string AnimIdle2 = "Idle2";

    // 씬에 배치된 기둥 오브젝트 이름 (좌/우 그룹 각 4개)
    private static readonly string[] LeftPillarNames = {
        "SM_Pillar_Base_02a9", "SM_Pillar_Base_02a10",
        "SM_Pillar_Base_02a11", "SM_Pillar_Base_02a12"
    };
    private static readonly string[] RightPillarNames = {
        "SM_Pillar_Base_02a58", "SM_Pillar_Base_02a59",
        "SM_Pillar_Base_02a60", "SM_Pillar_Base_02a61"
    };

    private enum Phase { Summoning, PillarWait, Recovery }

    private Phase              _phase;
    private float              _timer;
    private bool               _pillarsSpawned;
    private bool               _patternEnded;
    private GameObject         _shieldVfxGo;
    private readonly List<DKSoulPillar> _pillars = new List<DKSoulPillar>();
    private int                _destroyedCount;

    public DKSoulSummonState(DKSoulSummonPatternSO data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        // 이전 사이클의 잔여 힐 비동기 작업 취소 (타임아웃 힐이 다음 사이클까지 이어지는 것 방지)
        (ctx.Monster as DeathKnightBossMonster)?.CancelGradualHeal();

        _phase          = Phase.Summoning;
        _timer          = 0f;
        _pillarsSpawned = false;
        _patternEnded   = false;
        _shieldVfxGo    = null;
        _destroyedCount = 0;
        _pillars.Clear();

        // 보스는 초기 위치 고정 — 이동 없이 즉시 소환 페이즈 진입
        StopAgent(ctx);
        PlayAnim(ctx, AnimIdle2);
        (ctx.Monster as DeathKnightBossMonster)?.DKBlackboard.SetInvincible(true);

        if (Data.bossShieldVfxPrefab != null)
        {
            _shieldVfxGo = BossEffectPool.Spawn(Data.bossShieldVfxPrefab,
                ctx.Transform.position, Quaternion.identity);
            if (_shieldVfxGo != null)
            {
                _shieldVfxGo.transform.SetParent(ctx.Transform, worldPositionStays: true);
                var dkBoss = ctx.Monster as DeathKnightBossMonster;
                DKSwordColor sc = dkBoss?.DKBlackboard.SwordColor ?? DKSwordColor.White;
                DKGridPatternHelper.TintShieldVfx(_shieldVfxGo, sc);
            }
        }
    }

    public override void Update(MonsterContext ctx)
    {
        _timer += Time.deltaTime;

        switch (_phase)
        {
            case Phase.Summoning:  UpdateSummoning(ctx);  break;
            case Phase.PillarWait: UpdatePillarWait(ctx); break;
            case Phase.Recovery:   UpdateRecovery(ctx);   break;
        }
    }

    public override void Exit(MonsterContext ctx)
    {
        _patternEnded = true;
        Cleanup(ctx);
        RestoreAgent(ctx);
        (ctx.Monster as DeathKnightBossMonster)?.NotifySoulSummonCompleted();
    }

    // ── Phase: Summoning ───────────────────────────────────────

    private void UpdateSummoning(MonsterContext ctx)
    {
        if (_pillarsSpawned || _timer < Data.summoningDuration) return;

        _pillarsSpawned = true;
        SpawnAllPillars(ctx);
        _phase = Phase.PillarWait;
        _timer = 0f;
    }

    // ── Phase: PillarWait ──────────────────────────────────────

    private void UpdatePillarWait(MonsterContext ctx)
    {
        bool allGone = _pillars.Count > 0 && _destroyedCount >= _pillars.Count;
        if (allGone)
        {
            BeginRecovery(ctx);
            return;
        }

        if (_timer >= Data.waitTimeout)
        {
            HealBossFromRemainingPillars(ctx);
            DestroyRemainingPillars();
            BeginRecovery(ctx);
        }
    }

    // ── Phase: Recovery ────────────────────────────────────────

    private void UpdateRecovery(MonsterContext ctx)
    {
        if (_timer >= Data.recoveryTime)
            ctx.Monster.ChangeState<AttackReadyState>();
    }

    // ── 헬퍼 ─────────────────────────────────────────────────────

    private void BeginRecovery(MonsterContext ctx)
    {
        (ctx.Monster as DeathKnightBossMonster)?.DKBlackboard.SetInvincible(false);
        DetachShield();
        _phase = Phase.Recovery;
        _timer = 0f;
    }

    private void SpawnAllPillars(MonsterContext ctx)
    {
        GameObject leftTarget  = FindRandomPillarGo(LeftPillarNames);
        GameObject rightTarget = FindRandomPillarGo(RightPillarNames);

        if (leftTarget  != null) SpawnOnePillar(leftTarget,  ctx);
        if (rightTarget != null) SpawnOnePillar(rightTarget, ctx);

        if (leftTarget == null)
            Debug.LogWarning("[DKSoulSummon] 좌측 기둥 오브젝트를 씬에서 찾지 못했습니다.");
        if (rightTarget == null)
            Debug.LogWarning("[DKSoulSummon] 우측 기둥 오브젝트를 씬에서 찾지 못했습니다.");
    }

    private static GameObject FindRandomPillarGo(string[] names)
    {
        int startIdx = Random.Range(0, names.Length);
        for (int i = 0; i < names.Length; i++)
        {
            var go = GameObject.Find(names[(startIdx + i) % names.Length]);
            if (go != null) return go;
        }
        return null;
    }

    private void SpawnOnePillar(GameObject visualPillarGo, MonsterContext ctx)
    {
        Vector3 position = visualPillarGo.transform.position;

        GameObject vfxInstance = null;
        if (Data.pillarVfxPrefab != null)
        {
            vfxInstance = BossEffectPool.Spawn(Data.pillarVfxPrefab, position, Quaternion.identity);
            if (vfxInstance != null)
            {
                vfxInstance.transform.SetParent(visualPillarGo.transform, worldPositionStays: true);
                if (Data.pillarVfxScale != 1f)
                    vfxInstance.transform.localScale = Vector3.one * Data.pillarVfxScale;
            }
        }

        // 씬 오브젝트에 직접 컴포넌트 추가 → 기존 BoxCollider로 피격 감지
        var pillar = visualPillarGo.AddComponent<DKSoulPillar>();
        pillar.Initialize(
            Data.pillarHp,
            Data.pillarDestroyVfxPrefab,
            Data.pillarDestroyVfxScale,
            Data.pillarDestroySfx,
            ctx,
            OnPillarDestroyed,
            vfxInstance,
            visualPillarGo);

        _pillars.Add(pillar);
    }

    private void OnPillarDestroyed()
    {
        if (_patternEnded) return;
        _destroyedCount++;
    }

    private void HealBossFromRemainingPillars(MonsterContext ctx)
    {
        var boss = ctx.Monster as DeathKnightBossMonster;
        if (boss == null) return;

        int remaining = 0;
        foreach (var p in _pillars)
        {
            if (p != null && !p.IsDestroyed) remaining++;
        }

        if (remaining <= 0) return;

        int totalHeal = Mathf.RoundToInt(boss.EffectiveMaxHp * Data.pillarHealPercentPerPillar * remaining);
        boss.SoulPillarHealBossGradual(totalHeal, Data.gradualHealDuration);
        Debug.Log($"[DKSoulSummon] 타임아웃 — 남은 기둥 {remaining}개 × {Data.pillarHealPercentPerPillar * 100f}% = {totalHeal} 회복", ctx.Monster);
    }

    private void DestroyRemainingPillars()
    {
        foreach (var p in _pillars)
        {
            if (p == null || p.IsDestroyed) continue;
            p.ReleaseVfx();
            UnityEngine.Object.Destroy(p); // 씬 GO가 아닌 컴포넌트만 제거
        }
        _destroyedCount = _pillars.Count;
    }

    private void Cleanup(MonsterContext ctx)
    {
        (ctx.Monster as DeathKnightBossMonster)?.DKBlackboard.SetInvincible(false);
        DetachShield();
        DestroyRemainingPillars();
    }

    private void DetachShield()
    {
        if (_shieldVfxGo == null) return;
        _shieldVfxGo.transform.SetParent(null);
        BossEffectPool.Release(_shieldVfxGo);
        _shieldVfxGo = null;
    }

    private static void StopAgent(MonsterContext ctx)
    {
        if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
        {
            ctx.Agent.isStopped = true;
            ctx.Agent.velocity  = Vector3.zero;
            ctx.Agent.ResetPath();
        }
    }

    private static void RestoreAgent(MonsterContext ctx)
    {
        if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
        {
            ctx.Agent.isStopped        = false;
            ctx.Agent.stoppingDistance = 1.0f;
        }
    }

    private static void PlayAnim(MonsterContext ctx, string stateName)
    {
        if (ctx.Animator == null) return;
        if (!ctx.Animator.HasState(0, Animator.StringToHash(stateName))) return;
        float speed = (ctx.Monster as DeathKnightBossMonster)?.DKBlackboard.AnimSpeedMult ?? 1f;
        ctx.Animator.speed = speed;
        ctx.Animator.CrossFade(stateName, 0.15f);
    }
}
}

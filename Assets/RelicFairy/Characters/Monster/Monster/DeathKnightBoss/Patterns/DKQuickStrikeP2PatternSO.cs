using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace RelicFairy.Monster
{
/// <summary>
/// DeathKnight Phase2 변형 — 3연속 십자 공격 패턴.
///
/// 1페이즈 DKQuickStrikePattern(1회 십자)을 3회 연속으로 발동한다.
/// 각 타격 사이 간격은 [minStrikeInterval, maxStrikeInterval] 에서 랜덤하게 결정된다.
/// 매 타격마다 플레이어 현재 위치를 새로 캡처해 공정성을 유지한다.
///
/// ━━ 흐름 ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
///  [Windup]   Animation → warningDuration → 십자 타일 생성
///             → hitTime → 타일 제거 + VFX
///             → hitTime+hitDuration → 피격 → [Gap]
///  [Gap]      랜덤 대기 → 다음 [Windup] 또는 AttackReady
/// </summary>
[CreateAssetMenu(menuName = "Abyss/Boss/DeathKnight/Phase2/DK_P2_QuickStrikePattern",
                 fileName = "DK_P2_QuickStrikePattern")]
public class DKQuickStrikeP2PatternSO : BossPatternSO
{
    [Header("Grid Tiles")]
    public GameObject whiteTilePrefab;
    public GameObject blackTilePrefab;

    [Header("VFX")]
    [Tooltip("Sword Slash 15 — 십자 임팩트 이펙트")]
    public GameObject impactVfxPrefab;
    [Tooltip("스윙 이펙트")]
    public GameObject swingVfxPrefab;

    [Header("타이밍 (1회 공격)")]
    [Tooltip("Opening Pose 종료 — 경고 타일 생성 시점. 약공격 ≥0.35s")]
    public float warningDuration = 0.35f;
    [Tooltip("타일 제거 + VFX 시점")]
    public float hitTime         = 0.55f;
    [Tooltip("VFX 스폰 후 실제 피격까지 대기")]
    public float hitDuration     = 0.4f;

    [Header("3연속 설정")]
    [Tooltip("연속 공격 횟수")]
    public int   strikeCount        = 3;
    [Tooltip("피격 후 다음 공격 시작까지 최소 간격 (s)")]
    public float minStrikeInterval  = 0.3f;
    [Tooltip("피격 후 다음 공격 시작까지 최대 간격 (s)")]
    public float maxStrikeInterval  = 0.7f;
    [Tooltip("마지막 공격 후 AttackReady까지 대기 (s)")]
    public float recoveryTime       = 0.3f;

    [Header("Border")]
    [Tooltip("경계 테두리 엣지 프리팹")]
    public GameObject edgePrefab;

    [Header("데미지")]
    public float damageMultiplier    = 1.2f;
    public float knockbackMultiplier = 1f;

    [Header("사운드")]
    [Tooltip("Big Slash — Sword Slash 15 이펙트가 뜨는 위치에서 재생")]
    public AudioClip bigSlashSfx;

    private DKQuickStrikeP2State _state;

    public override void Initialize(BossPatternContext ctx) => _state = new DKQuickStrikeP2State(this);
    public override void OnRecycled()                       => _state = new DKQuickStrikeP2State(this);

    public override bool CanExecute(BossPatternContext ctx)
        => ctx.Ctx.Runtime.PlayerTarget != null;

    public override SpecialStateBase GetRuntimeState() => _state;
}

public class DKQuickStrikeP2State : FullLockState<DKQuickStrikeP2PatternSO>
{
    private const string AnimName = "Attack2";

    private enum Phase { Windup, Gap }

    private Phase   _phase;
    private float   _timer;
    private float   _gapDuration;
    private int     _strikeIndex;

    private bool              _tilesSpawned;
    private bool              _tilesDestroyed;
    private bool              _vfxSpawned;
    private bool              _hitDone;
    private List<DKTileInfo>  _tiles = new List<DKTileInfo>();
    private List<GameObject>  _edges = new List<GameObject>();
    private Vector2Int        _playerCell;

    public DKQuickStrikeP2State(DKQuickStrikeP2PatternSO data) : base(data) { }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // Enter / Exit
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    public override void Enter(MonsterContext ctx)
    {
        _strikeIndex = 0;
        StopAgent(ctx);
        BeginNextStrike(ctx);
    }

    public override void Exit(MonsterContext ctx)
    {
        DKGridPatternHelper.DestroyEdges(_edges);
        DKGridPatternHelper.DestroyTiles(_tiles);
        RestoreAgent(ctx);
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // Update
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    public override void Update(MonsterContext ctx)
    {
        _timer += Time.deltaTime * AnimSpeed(ctx);

        if (_phase == Phase.Windup)
            UpdateWindup(ctx);
        else
            UpdateGap(ctx);
    }

    // ── Windup ────────────────────────────────────────────

    private void UpdateWindup(MonsterContext ctx)
    {
        // ① Opening Pose → 경고 타일 생성
        if (!_tilesSpawned && _timer >= Data.warningDuration)
        {
            _tilesSpawned = true;
            DKSwordColor sc = GetSwordColor(ctx);
            _tiles = DKGridPatternHelper.SpawnTiles(ColorRule(sc), Data.whiteTilePrefab, Data.blackTilePrefab);
            _edges = DKGridPatternHelper.SpawnBoundaryEdges(_tiles, Data.edgePrefab);
        }

        // ③ 타일 제거 + 스윙 VFX
        if (!_tilesDestroyed && _timer >= Data.hitTime)
        {
            _tilesDestroyed = true;
            DKGridPatternHelper.DestroyEdges(_edges);
            DKGridPatternHelper.DestroyTiles(_tiles);
            SpawnSwingVfx(ctx);
        }

        // ③ 십자 임팩트 VFX
        if (_tilesDestroyed && !_vfxSpawned && _timer >= Data.hitTime + 0.05f)
        {
            _vfxSpawned = true;
            // Sword Slash 15 이펙트가 스폰되는 시점에 맞춰 재생. 클립 앞 무음 구간은 건너뛰어 0.5초부터 재생
            Managers.Sound?.PlayEffectAt(
                Data.bigSlashSfx, DKBossRoomContext.CellToWorld(_playerCell.x, _playerCell.y, 0f), startTime: 0.5f);
            DKSwordColor sc = GetSwordColor(ctx);
            DKGridPatternHelper.SpawnCrossVfx(Data.impactVfxPrefab, _playerCell, sc);
        }

        // ③ 피격 판정
        if (!_hitDone && _timer >= Data.hitTime + Data.hitDuration)
        {
            _hitDone = true;
            DKSwordColor sc = GetSwordColor(ctx);
            DKGridPatternHelper.TriggerDamage(
                ctx, ColorRule(sc), sc, Data.damageMultiplier, Data.knockbackMultiplier);

            BossImpactFeedback.TriggerHitStop(0.1f);
            BossImpactFeedback.TriggerCameraShake(0.12f, 0.25f);

            // 다음 단계 전환
            _strikeIndex++;
            bool allDone = _strikeIndex >= Data.strikeCount;

            _phase       = Phase.Gap;
            _timer       = 0f;
            _gapDuration = allDone
                ? Data.recoveryTime
                : Random.Range(Data.minStrikeInterval, Data.maxStrikeInterval);
        }
    }

    // ── Gap ───────────────────────────────────────────────

    private void UpdateGap(MonsterContext ctx)
    {
        if (_timer < _gapDuration) return;

        if (_strikeIndex >= Data.strikeCount)
        {
            ctx.Monster.ChangeState<AttackReadyState>();
        }
        else
        {
            BeginNextStrike(ctx);
        }
    }

    // ── 다음 공격 준비 ─────────────────────────────────────

    private void BeginNextStrike(MonsterContext ctx)
    {
        _phase          = Phase.Windup;
        _timer          = 0f;
        _tilesSpawned   = false;
        _tilesDestroyed = false;
        _vfxSpawned     = false;
        _hitDone        = false;
        _tiles.Clear();
        _edges.Clear();

        // 매 타격마다 플레이어 현재 위치 재캡처 (공정성)
        _playerCell = ctx.Runtime.PlayerTarget != null
            ? DKBossRoomContext.WorldToCell(ctx.Runtime.PlayerTarget.position)
            : Vector2Int.zero;

        FacePlayer(ctx);
        PlayAnim(ctx, AnimName);
    }

    // ── 색상 규칙 ─────────────────────────────────────────

    private Func<int, int, DKSwordColor> ColorRule(DKSwordColor sc)
    {
        int px = _playerCell.x;
        int pz = _playerCell.y;
        return (x, z) => (x == px || z == pz) ? sc : Opposite(sc);
    }

    // ── 헬퍼 ──────────────────────────────────────────────

    private void SpawnSwingVfx(MonsterContext ctx)
    {
        if (Data.swingVfxPrefab == null) return;
        Transform swordTf = (ctx.Monster as DeathKnightBossMonster)?.SwordTransform;
        Vector3    pos = swordTf != null ? swordTf.position : ctx.Transform.position;
        Quaternion rot = swordTf != null ? swordTf.rotation : ctx.Transform.rotation;
        BossEffectPool.SpawnOneShot(Data.swingVfxPrefab, pos, rot, fallbackLifetime: 2f);
    }

    private static DKSwordColor GetSwordColor(MonsterContext ctx)
        => (ctx.Monster as DeathKnightBossMonster)?.DKBlackboard.SwordColor ?? DKSwordColor.White;

    private static DKSwordColor Opposite(DKSwordColor c)
        => c == DKSwordColor.White ? DKSwordColor.Black : DKSwordColor.White;

    private static float AnimSpeed(MonsterContext ctx)
        => (ctx.Monster as DeathKnightBossMonster)?.DKBlackboard.AnimSpeedMult ?? 1f;

    private static void PlayAnim(MonsterContext ctx, string stateName)
    {
        if (ctx.Animator == null) return;
        if (!ctx.Animator.HasState(0, Animator.StringToHash(stateName))) return;
        ctx.Animator.speed = AnimSpeed(ctx);
        ctx.Animator.CrossFade(stateName, 0.05f, 0, 0f);
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
            ctx.Agent.isStopped = false;
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

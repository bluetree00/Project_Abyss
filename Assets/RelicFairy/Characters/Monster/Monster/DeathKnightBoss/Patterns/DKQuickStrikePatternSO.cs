using System.Collections.Generic;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// DeathKnight 빠른 찌르기 (QuickStrike / Attack2) 패턴.
///
/// 흐름:
///  Enter         → 즉시 Attack2 애니메이션 재생
///  warningDuration → 경고 타일 생성 (플레이어 셀 기준 십자 방향)
///  hitTime        → 타일 제거 + Sword Slash 15 VFX
///  hitTime+hitDuration → 피격
///  hitTime+hitDuration+recoveryTime → AttackReadyState
/// </summary>
[CreateAssetMenu(menuName = "Abyss/Boss/DeathKnight/DK_QuickStrikePattern", fileName = "DK_QuickStrikePattern")]
public class DKQuickStrikePatternSO : BossPatternSO
{
    [Header("Grid Tiles")]
    public GameObject whiteTilePrefab;
    public GameObject blackTilePrefab;

    [Header("VFX")]
    [Tooltip("Sword Slash 15 — 한 줄을 덮는 임팩트 이펙트")]
    public GameObject impactVfxPrefab;
    [Tooltip("스윙 이펙트 (Sword Slash mirror)")]
    public GameObject swingVfxPrefab;

    [Header("Timing")]
    [Tooltip("Attack 애니메이션 시작 후 경고 타일이 생성되는 시점")]
    public float warningDuration = 0.2f;
    [Tooltip("타일 제거 + VFX 스폰 시점")]
    public float hitTime         = 0.5f;
    [Tooltip("VFX 스폰 후 실제 피격까지 대기 시간")]
    public float hitDuration     = 0.4f;
    public float recoveryTime    = 0.2f;

    [Header("Damage")]
    public float damageMultiplier    = 1.2f;
    public float knockbackMultiplier = 1f;

    private DKQuickStrikeState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new DKQuickStrikeState(this);
    public override void OnRecycled()                      => _state = new DKQuickStrikeState(this);

    public override bool CanExecute(BossPatternContext ctx)
        => ctx.Ctx.Runtime.PlayerTarget != null;

    public override SpecialStateBase GetRuntimeState() => _state;
}

public class DKQuickStrikeState : FullLockState<DKQuickStrikePatternSO>
{
    private const string AnimName = "Attack2";

    private float            _timer;
    private bool             _swingVfxSpawned;
    private bool             _tilesSpawned;
    private bool             _tilesDestroyed;
    private bool             _crossVfxSpawned;
    private bool             _hitDone;
    private List<DKTileInfo> _tiles;
    private Vector2Int       _playerCell;

    public DKQuickStrikeState(DKQuickStrikePatternSO data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        _timer            = 0f;
        _swingVfxSpawned  = false;
        _tilesSpawned     = false;
        _tilesDestroyed   = false;
        _crossVfxSpawned  = false;
        _hitDone          = false;
        _tiles            = new List<DKTileInfo>();

        // 플레이어 셀 Enter 시점에 캡처
        _playerCell = ctx.Runtime.PlayerTarget != null
            ? DKBossRoomContext.WorldToCell(ctx.Runtime.PlayerTarget.position)
            : Vector2Int.zero;

        StopAgent(ctx);
        FacePlayer(ctx);
        PlayAnim(ctx, AnimName);
    }

    public override void Update(MonsterContext ctx)
    {
        _timer += Time.deltaTime * AnimSpeed(ctx);

        if (!_swingVfxSpawned)
        {
            _swingVfxSpawned = true;
            SpawnSwingVfx(ctx);
        }

        if (!_tilesSpawned && _timer >= Data.warningDuration)
        {
            _tilesSpawned = true;
            DKSwordColor sc = GetSwordColor(ctx);
            _tiles = DKGridPatternHelper.SpawnTiles(
                ColorRule(sc), Data.whiteTilePrefab, Data.blackTilePrefab);
        }

        // hitTime: 타일 제거
        if (!_tilesDestroyed && _timer >= Data.hitTime)
        {
            _tilesDestroyed = true;
            DKGridPatternHelper.DestroyTiles(_tiles);
        }

        // hitTime + 0.05s: 타일 제거 확인 후 십자가에만 VFX (1회)
        if (_tilesDestroyed && !_crossVfxSpawned && _timer >= Data.hitTime + 0.05f)
        {
            _crossVfxSpawned = true;
            DKSwordColor sc = GetSwordColor(ctx);
            DKGridPatternHelper.SpawnCrossVfx(Data.impactVfxPrefab, _playerCell, sc);
        }

        // hitTime + hitDuration: 피격
        if (!_hitDone && _timer >= Data.hitTime + Data.hitDuration)
        {
            _hitDone = true;
            DKSwordColor sc = GetSwordColor(ctx);
            DKGridPatternHelper.TriggerDamage(
                ctx, ColorRule(sc), sc,
                Data.damageMultiplier, Data.knockbackMultiplier);
        }

        if (_timer >= Data.hitTime + Data.hitDuration + Data.recoveryTime)
            ctx.Monster.ChangeState<AttackReadyState>();
    }

    public override void Exit(MonsterContext ctx)
    {
        DKGridPatternHelper.DestroyTiles(_tiles);
        RestoreAgent(ctx);
    }

    private System.Func<int, int, DKSwordColor> ColorRule(DKSwordColor sc)
    {
        int px = _playerCell.x;
        int pz = _playerCell.y;
        return (x, z) => (x == px || z == pz) ? sc : Opposite(sc);
    }

    private void SpawnSwingVfx(MonsterContext ctx)
    {
        if (Data.swingVfxPrefab == null) return;
        BossEffectPool.SpawnOneShot(
            Data.swingVfxPrefab, ctx.Transform.position, ctx.Transform.rotation, fallbackLifetime: 2f);
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
        if (!ctx.Animator.HasState(0, Animator.StringToHash(stateName)))
        {
            Debug.LogWarning($"[DKQuickStrike] '{stateName}' not found", ctx.Monster);
            return;
        }
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

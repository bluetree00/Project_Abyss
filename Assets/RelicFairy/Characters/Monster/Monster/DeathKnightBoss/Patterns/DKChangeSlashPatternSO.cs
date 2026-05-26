using System.Collections.Generic;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// DeathKnight 색상 변환 베기 (ChangeSlash / Attack1) 패턴.
///
/// 흐름:
///  Enter         → 즉시 Attack1 애니메이션 재생
///  warningDuration → 경고 타일 생성 (검이 휘두르는 중)
///  hitTime        → 타일 제거 + Sword Slash 15 VFX 스폰 (검이 내리치는 순간)
///  hitTime+hitDuration → 피격 (VFX 재생 완료 후)
///  hitTime+hitDuration+recoveryTime → FlipSwordColor → AttackReadyState
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/Boss/DeathKnight/DK_ChangeSlashPattern", fileName = "DK_ChangeSlashPattern")]
public class DKChangeSlashPatternSO : BossPatternSO
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
    public float warningDuration = 0.5f;
    [Tooltip("타일 제거 + VFX 스폰 시점 (warningDuration 보다 커야 함)")]
    public float hitTime         = 1.5f;
    [Tooltip("VFX 스폰 후 실제 피격까지 대기 시간 (VFX 재생 완료 대기)")]
    public float hitDuration     = 0.7f;
    public float recoveryTime    = 0.3f;

    [Header("Damage")]
    public float damageMultiplier   = 1f;
    public float knockbackMultiplier = 1f;

    private DKChangeSlashState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new DKChangeSlashState(this);
    public override void OnRecycled()                      => _state = new DKChangeSlashState(this);

    public override bool CanExecute(BossPatternContext ctx)
        => ctx.Ctx.Runtime.PlayerTarget != null;

    public override SpecialStateBase GetRuntimeState() => _state;
}

public class DKChangeSlashState : FullLockState<DKChangeSlashPatternSO>
{
    private const string AnimName = "Attack1";

    private const float VfxDelay = 0.05f; // 타일 제거 확정 후 VFX 스폰까지 최소 대기

    private float            _timer;
    private bool             _swingVfxSpawned;
    private bool             _tilesSpawned;
    private bool             _tilesDestroyed;
    private bool             _vfxSpawned;
    private bool             _hitDone;
    private List<DKTileInfo> _tiles;

    public DKChangeSlashState(DKChangeSlashPatternSO data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        _timer           = 0f;
        _swingVfxSpawned = false;
        _tilesSpawned    = false;
        _tilesDestroyed  = false;
        _vfxSpawned      = false;
        _hitDone         = false;
        _tiles           = new List<DKTileInfo>();

        StopAgent(ctx);
        FacePlayer(ctx);
        PlayAnim(ctx, AnimName);  // 즉시 공격 애니메이션 시작
    }

    public override void Update(MonsterContext ctx)
    {
        _timer += Time.deltaTime * AnimSpeed(ctx);

        // 스윙 시작과 동시에 SwingVfx 1회
        if (!_swingVfxSpawned)
        {
            _swingVfxSpawned = true;
            SpawnSwingVfx(ctx);
        }

        // warningDuration: 경고 타일 생성 (검이 휘두르는 중)
        if (!_tilesSpawned && _timer >= Data.warningDuration)
        {
            _tilesSpawned = true;
            DKSwordColor sc = GetSwordColor(ctx);
            _tiles = DKGridPatternHelper.SpawnTiles(
                (x, z) => x % 2 == 1 ? sc : Opposite(sc),
                Data.whiteTilePrefab, Data.blackTilePrefab);
        }

        // hitTime: 타일 제거만 (VFX는 아직 아님)
        if (!_tilesDestroyed && _timer >= Data.hitTime)
        {
            _tilesDestroyed = true;
            DKGridPatternHelper.DestroyTiles(_tiles);
        }

        // hitTime + VfxDelay: 타일이 확실히 사라진 후 VFX 스폰
        if (_tilesDestroyed && !_vfxSpawned && _timer >= Data.hitTime + VfxDelay)
        {
            _vfxSpawned = true;
            DKSwordColor sc = GetSwordColor(ctx);
            DKGridPatternHelper.SpawnHitVfx(
                (x, z) => x % 2 == 1 ? sc : Opposite(sc),
                sc, Data.impactVfxPrefab, DKVfxGroupMode.PerColumn);
        }

        // hitTime + hitDuration: 피격 (VFX 재생 완료 후)
        if (!_hitDone && _timer >= Data.hitTime + Data.hitDuration)
        {
            _hitDone = true;
            DKSwordColor sc = GetSwordColor(ctx);
            DKGridPatternHelper.TriggerDamage(
                ctx, (x, z) => x % 2 == 1 ? sc : Opposite(sc),
                sc, Data.damageMultiplier, Data.knockbackMultiplier);
        }

        if (_timer >= Data.hitTime + Data.hitDuration + Data.recoveryTime)
        {
            (ctx.Monster as DeathKnightBossMonster)?.FlipSwordColor();
            ctx.Monster.ChangeState<AttackReadyState>();
        }
    }

    public override void Exit(MonsterContext ctx)
    {
        DKGridPatternHelper.DestroyTiles(_tiles);
        RestoreAgent(ctx);
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
            Debug.LogWarning($"[DKChangeSlash] '{stateName}' not found", ctx.Monster);
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

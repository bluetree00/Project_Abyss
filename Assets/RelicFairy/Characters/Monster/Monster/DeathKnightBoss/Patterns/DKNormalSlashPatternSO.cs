using System.Collections.Generic;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// DeathKnight 일반 베기 (NormalSlash / Attack3) 패턴.
///
/// 흐름:
///  Enter               → 즉시 Attack3 애니메이션 재생
///  warningDuration     → 경고 타일 생성 (z%2==1 → swordColor)
///  hitTime             → 타일 제거, 순차 타격 시작
///  hitTime + n*hitStep → 매칭 행(z=1,3,5,...) 을 위→아래 순서로
///                        한 줄씩 Sword Slash 15 VFX + 피격
///  마지막 행 이후 + recoveryTime → AttackReadyState
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/Boss/DeathKnight/DK_NormalSlashPattern", fileName = "DK_NormalSlashPattern")]
public class DKNormalSlashPatternSO : BossPatternSO
{
    [Header("Grid Tiles")]
    public GameObject whiteTilePrefab;
    public GameObject blackTilePrefab;

    [Header("VFX")]
    public GameObject impactVfxPrefab;
    public GameObject swingVfxPrefab;

    [Header("Timing")]
    [Tooltip("Attack 애니메이션 시작 후 경고 타일이 생성되는 시점")]
    public float warningDuration = 0.1f;
    [Tooltip("타일 제거 + 순차 타격 시작 시점")]
    public float hitTime         = 0.7f;
    [Tooltip("줄마다 VFX + 피격 간격 (초)")]
    public float hitStep         = 0.08f;
    public float recoveryTime    = 0.3f;

    [Header("Damage")]
    public float damageMultiplier    = 1f;
    public float knockbackMultiplier = 1f;

    [Header("Sound")]
    [Tooltip("Big Slash — Sword Slash 15 이펙트가 뜨는 위치에서 재생")]
    public AudioClip bigSlashSfx;

    [Header("Border")]
    [Tooltip("경계 테두리 엣지 프리팹 (DK_WarnBorder)")]
    public GameObject edgePrefab;

    private DKNormalSlashState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new DKNormalSlashState(this);
    public override void OnRecycled()                      => _state = new DKNormalSlashState(this);

    public override bool CanExecute(BossPatternContext ctx)
        => ctx.Ctx.Runtime.PlayerTarget != null;

    public override SpecialStateBase GetRuntimeState() => _state;
}

public class DKNormalSlashState : FullLockState<DKNormalSlashPatternSO>
{
    private const string AnimName  = "Attack3";
    private const float  VfxDelay  = 0.05f; // 타일 제거 후 첫 VFX 까지 최소 대기

    private float             _timer;
    private bool              _tilesSpawned;
    private bool              _tilesDestroyed;
    private List<DKTileInfo>  _tiles;
    private List<GameObject>  _edges;

    // 순차 타격
    private List<int>         _matchingRows; // 위→아래 정렬된 매칭 행 z 목록
    private int               _rowIndex;     // 다음 타격 대상 인덱스
    private DKSwordColor      _swordColor;   // Enter 시 캡처

    public DKNormalSlashState(DKNormalSlashPatternSO data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        _timer        = 0f;
        _tilesSpawned = false;
        _tilesDestroyed  = false;
        _tiles           = new List<DKTileInfo>();
        _edges           = new List<GameObject>();
        _matchingRows    = new List<int>();
        _rowIndex        = 0;
        _swordColor      = GetSwordColor(ctx);

        StopAgent(ctx);
        FacePlayer(ctx);
        PlayAnim(ctx, AnimName);
    }

    public override void Update(MonsterContext ctx)
    {
        _timer += Time.deltaTime * AnimSpeed(ctx);

        // 경고 타일 생성
        if (!_tilesSpawned && _timer >= Data.warningDuration)
        {
            _tilesSpawned = true;
            _tiles = DKGridPatternHelper.SpawnTiles(
                (x, z) => z % 2 == 1 ? _swordColor : Opposite(_swordColor),
                Data.whiteTilePrefab, Data.blackTilePrefab);
            _edges = DKGridPatternHelper.SpawnBoundaryEdges(_tiles, Data.edgePrefab);

            // 매칭 행 목록 사전 계산 (z=27,25,...,1 — 내림차순 = 위→아래)
            for (int z = DKBossRoomContext.Height - 2; z >= 1; z--)
                if (z % 2 == 1) _matchingRows.Add(z);
        }

        // hitTime: 타일 제거
        if (!_tilesDestroyed && _timer >= Data.hitTime)
        {
            _tilesDestroyed = true;
            DKGridPatternHelper.DestroyEdges(_edges);
            DKGridPatternHelper.DestroyTiles(_tiles);
        }

        // hitTime + VfxDelay + n*hitStep: 줄마다 VFX + 피격
        if (_tilesDestroyed && _rowIndex < _matchingRows.Count)
        {
            float rowFireTime = Data.hitTime + VfxDelay + _rowIndex * Data.hitStep;
            if (_timer >= rowFireTime)
            {
                int rowZ = _matchingRows[_rowIndex];
                DKGridPatternHelper.SpawnSingleRowVfx(Data.impactVfxPrefab, rowZ, _swordColor);
                DKGridPatternHelper.TriggerSingleRowDamage(
                    ctx, rowZ, Data.damageMultiplier, Data.knockbackMultiplier);
                // 행마다 Sword Slash 15 이펙트 위치에서 재생 — 거리 감쇄로 먼 행은 자연스럽게 작게 들림.
                // 클립 앞 무음 구간은 건너뛰어 0.5초부터 재생
                Vector3 rowSoundPos = DKBossRoomContext.CellToWorld(DKBossRoomContext.Width / 2, rowZ, 0.1f);
                Managers.Sound?.PlayEffectAt(Data.bigSlashSfx, rowSoundPos, startTime: 0.5f);
                // §3 타격감 — 첫 행에서 히트스톱, 이후 행은 쉐이크만
                if (_rowIndex == 0)
                    BossImpactFeedback.TriggerHitStop(0.08f);
                BossImpactFeedback.TriggerCameraShake(0.1f, 0.2f);
                _rowIndex++;
            }
        }

        // 모든 행 처리 완료 후 recoveryTime 대기
        if (_tilesDestroyed && _rowIndex >= _matchingRows.Count && _matchingRows.Count > 0)
        {
            float allDone = Data.hitTime + VfxDelay + _matchingRows.Count * Data.hitStep;
            if (_timer >= allDone + Data.recoveryTime)
                ctx.Monster.ChangeState<AttackReadyState>();
        }
    }

    public override void Exit(MonsterContext ctx)
    {
        DKGridPatternHelper.DestroyEdges(_edges);
        DKGridPatternHelper.DestroyTiles(_tiles);
        RestoreAgent(ctx);
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
            Debug.LogWarning($"[DKNormalSlash] '{stateName}' not found", ctx.Monster);
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

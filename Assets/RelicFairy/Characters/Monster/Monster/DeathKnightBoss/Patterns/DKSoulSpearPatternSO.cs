using System.Collections.Generic;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// DeathKnight 영혼 창 투척 (SoulSpear / Attack2) 패턴.
///
/// Phase 1 콤보용 FullLockState 흐름:
///  Windup (0.5s) → Attack2 애니메이션
///  3회 반복:
///    플레이어 현재 행 캡처 → 해당 행 검 색 · 위아래 이웃 행 반대 색 경고 타일 + 경계 테두리 (10-03)
///    warningDuration(0.6s) 후 행 전체 폭 빔 VFX 출현 (경고 타일은 판정까지 유지)
///    beamHitDelay(0.45s) 후 피격 판정 (플레이어 회피 창) → 경고 타일 제거
///    nextRoundDelay(0.3s) 대기
///  Recovery (0.4s)
///
/// 빔 VFX는 행 왼쪽 벽 끝에서 +X로, localScale.z = (Width-2)*CellSize (방 실제 가로 폭).
/// Phase 2 패시브 발동 공유 데이터: passiveWarningDuration, passiveBeamHitDelay, passiveCooldownMin/Max
/// </summary>
[CreateAssetMenu(menuName = "Abyss/Boss/DeathKnight/DK_SoulSpearPattern",
                 fileName = "DK_SoulSpearPattern")]
public class DKSoulSpearPatternSO : BossPatternSO
{
    [Header("그리드 타일")]
    public GameObject whiteTilePrefab;
    public GameObject blackTilePrefab;
    [Tooltip("경계 테두리 엣지 프리팹 (DK_WarnBorder) — 노리는 줄 · 이웃 줄 경계 (10-03)")]
    public GameObject edgePrefab;

    [Header("VFX")]
    [Tooltip("행 전체를 가로지르는 빔 VFX (Laser beam 8 soul)")]
    public GameObject impactVfxPrefab;

    [Header("사운드")]
    public AudioClip beamSfx;

    [Header("타이밍 — Phase 1 콤보")]
    public float windupDuration   = 0.5f;
    [Tooltip("경고 장판 지속 시간")]
    public float warningDuration  = 0.6f;
    [Tooltip("빔 출현 → 피격 판정까지 회피 창 (초)")]
    public float beamHitDelay     = 0.45f;
    [Tooltip("피격 후 다음 타격까지 인터벌")]
    public float nextRoundDelay   = 0.3f;
    public int   strikeCount      = 3;
    public float recoveryTime     = 0.4f;

    [Header("데미지")]
    public float damageMultiplier    = 1f;
    public float knockbackMultiplier = 1f;

    [Header("패시브 (Phase 2 DKP2PassiveAttackRunner)")]
    [Tooltip("패시브 경고 장판 지속 시간")]
    public float passiveWarningDuration   = 0.6f;
    [Tooltip("패시브 빔 출현 → 피격까지 회피 창")]
    public float passiveBeamHitDelay      = 0.4f;
    [Tooltip("버스트 내 공격과 공격 사이 대기 시간 최소 (s)")]
    public float passiveBurstIntervalMin  = 1.0f;
    [Tooltip("버스트 내 공격과 공격 사이 대기 시간 최대 (s)")]
    public float passiveBurstIntervalMax  = 2.0f;
    public float passiveCooldownMin       = 5f;
    public float passiveCooldownMax       = 8f;

    private DKSoulSpearState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new DKSoulSpearState(this);
    public override void OnRecycled()                       => _state = new DKSoulSpearState(this);

    public override bool CanExecute(BossPatternContext ctx)
    {
        if (ctx.Ctx.Runtime.PlayerTarget == null) return false;
        var dk = ctx.Ctx.Monster as DeathKnightBossMonster;
        return dk == null || !dk.DKBlackboard.IsPhase2;
    }

    public override SpecialStateBase GetRuntimeState() => _state;
}

public class DKSoulSpearState : FullLockState<DKSoulSpearPatternSO>
{
    private const string AnimName = "Attack2";
    private const int    BoundStrikes = 2;      // 봉인기: 사슬이 2타 뒤 검을 붙잡는다(10-03 S2)
    private const float  YankStagger  = 0.8f;
    private const float  RowTileY     = 0.2f;   // 줄 예고 높이 — 예고 높이 규칙(0.2). 0.05는 바닥 잔해에 묻혔다(10-03)
    private const float  RowEdgeY     = 0.27f;  // 경계 테두리는 예고 타일 위로(기본 0.12는 타일 0.05 기준)
    private const float  BeamY        = 0.4f;   // 빔은 예고 타일 위로 — 타일이 판정 순간까지 남는다

    private enum Phase { Windup, Striking, Recovery }

    private Phase            _phase;
    private float            _timer;
    private int              _strikeIndex;
    private bool             _beamVisible;       // 빔 출현 완료(경고 타일은 판정까지 남는다)
    private bool             _strikeApplied;     // 피격 판정 완료
    private List<DKTileInfo> _tiles;
    private List<GameObject> _edges;
    private int              _currentRowZ;
    private DKSwordColor     _swordColor;
    private float            _extraRecovery;

    public DKSoulSpearState(DKSoulSpearPatternSO data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        _phase        = Phase.Windup;
        _timer        = 0f;
        _strikeIndex  = 0;
        _beamVisible  = false;
        _strikeApplied = false;
        _extraRecovery = 0f;
        _tiles        = new List<DKTileInfo>();
        _edges        = new List<GameObject>();
        _swordColor   = GetSwordColor(ctx);

        StopAgent(ctx);
        FacePlayer(ctx);
        PlayAnim(ctx, AnimName);
    }

    public override void Update(MonsterContext ctx)
    {
        _timer += Time.deltaTime * AnimSpeed(ctx);

        switch (_phase)
        {
            case Phase.Windup:
                if (_timer >= Data.windupDuration)
                {
                    _phase = Phase.Striking;
                    _timer = 0f;
                    BeginStrike(ctx);
                }
                break;

            case Phase.Striking:
                // Stage 1: 경고 타일 → 빔 출현
                if (!_beamVisible && _timer >= Data.warningDuration)
                {
                    _beamVisible = true;
                    SpawnRowBeam(Data.impactVfxPrefab, _currentRowZ, _swordColor);   // 경고 타일은 판정 순간까지 남긴다(10-03)
                    Managers.Sound?.PlayEffectAt(Data.beamSfx, DKBossRoomContext.CellToWorld(DKBossRoomContext.Width / 2, _currentRowZ, 0.1f));
                }
                // Stage 2: 빔 출현 → 피격 (회피 창)
                if (_beamVisible && !_strikeApplied && _timer >= Data.warningDuration + Data.beamHitDelay)
                {
                    _strikeApplied = true;
                    DKGridPatternHelper.TriggerSingleRowDamage(
                        ctx, _currentRowZ, Data.damageMultiplier, Data.knockbackMultiplier);
                    ClearTiles();
                    BossImpactFeedback.TriggerCameraShake(0.08f, 0.15f);
                }
                // Stage 3: 다음 타격으로
                if (_strikeApplied && _timer >= Data.warningDuration + Data.beamHitDelay + Data.nextRoundDelay)
                {
                    _strikeIndex++;
                    int strikes = IsBound(ctx) ? Mathf.Min(BoundStrikes, Data.strikeCount) : Data.strikeCount;
                    if (_strikeIndex >= strikes)
                    {
                        if (strikes < Data.strikeCount)
                        {
                            // 봉인기 — 옛 봉인 사슬이 검을 붙잡는다
                            _extraRecovery = BossBinding.Of(ctx.Monster).Yank(YankStagger);
                            PlayAnim(ctx, "GetHit");
                        }
                        _phase = Phase.Recovery;
                        _timer = 0f;
                    }
                    else
                    {
                        _timer = 0f;
                        BeginStrike(ctx);
                    }
                }
                break;

            case Phase.Recovery:
                if (_timer >= Data.recoveryTime + _extraRecovery)
                    ctx.Monster.ChangeState<AttackReadyState>();
                break;
        }
    }

    public override void Exit(MonsterContext ctx)
    {
        ClearTiles();
        RestoreAgent(ctx);
    }

    private void BeginStrike(MonsterContext ctx)
    {
        _beamVisible   = false;
        _strikeApplied = false;
        _timer         = 0f;

        if (ctx.Runtime.PlayerTarget == null) return;

        var playerCell       = DKBossRoomContext.WorldToCell(ctx.Runtime.PlayerTarget.position);
        _currentRowZ         = playerCell.y;
        _swordColor          = GetSwordColor(ctx);

        ClearTiles();
        SpawnRowGuide(Data, _currentRowZ, _swordColor, _tiles, _edges);
        if (_strikeIndex == 0)   // 첫 타에만
            (ctx.Monster as DeathKnightBossMonster)?.CueSwordFloor();   // 검 색 ↔ 바닥 신호(10-03 개선 2-2)
    }

    private void ClearTiles()
    {
        DKGridPatternHelper.DestroyEdges(_edges);
        DKGridPatternHelper.DestroyTiles(_tiles);
    }

    /// <summary>
    /// 줄 예고 — 노리는 줄은 검 색, 위아래 이웃 줄은 반대 색으로 깔고 경계 테두리를 두른다(10-03 개선 2-1).
    /// 어두운 바닥 위 검은 줄 하나는 보이지 않았다 — 반대 색 이웃과 테두리가 줄을 가른다. 2페이즈 패시브도 같이 쓴다.
    /// </summary>
    private const float RowEdgeThickness = 0.25f;   // 기본 0.133의 약 2배 — 원근으로 납작해지는 가로줄에서도 선이 보이게

    public static void SpawnRowGuide(DKSoulSpearPatternSO data, int rowZ, DKSwordColor sc,
                                     List<DKTileInfo> tiles, List<GameObject> edges)
    {
        DKSwordColor other = sc == DKSwordColor.White ? DKSwordColor.Black : DKSwordColor.White;
        SpawnRowTiles(data, rowZ,     sc,    tiles);   // 노리는 줄 먼저 — 공유 경계 테두리가 이 줄 색을 따른다
        SpawnRowTiles(data, rowZ - 1, other, tiles);
        SpawnRowTiles(data, rowZ + 1, other, tiles);
        // 테두리는 바닥색과 상관없이 밝게 — 흰 검 = 흰색, 검은 검 = 밝은 보라(10-03 실측: 어두운 테두리라 줄이 안 갈렸다)
        Color edge = sc == DKSwordColor.White ? new Color(1f, 1f, 1f, 0.95f) : new Color(0.8f, 0.45f, 1f, 0.95f);
        edges.AddRange(DKGridPatternHelper.SpawnBoundaryEdges(tiles, data.edgePrefab, RowEdgeThickness, RowEdgeY, edge));
    }

    private static void SpawnRowTiles(DKSoulSpearPatternSO data, int z, DKSwordColor color, List<DKTileInfo> tiles)
    {
        GameObject prefab = color == DKSwordColor.White ? data.whiteTilePrefab : data.blackTilePrefab;
        if (prefab == null) return;

        for (int x = 1; x <= DKBossRoomContext.Width - 2; x++)
        {
            if (!DKBossRoomContext.IsInterior(x, z)) continue;
            var go = BossEffectPool.Spawn(prefab,
                DKBossRoomContext.CellToWorld(x, z, RowTileY),
                Quaternion.Euler(-90f, 0f, 0f));
            if (go == null) continue;
            go.transform.localScale = Vector3.one * DKBossRoomContext.CellSize;
            tiles.Add(new DKTileInfo { Cell = new Vector2Int(x, z), Color = color, GO = go });
        }
    }

    /// <summary>
    /// 빔 VFX를 줄 왼쪽 벽 끝에서 +X로 줄 전체를 가로지르게 스폰한다(10-03 개선 2-1).
    /// 예전엔 플레이어 왼쪽 1칸에서 7 m만 나가 시작점이 안 보였고, 검은 검이면 DarkTint가 가산 파트를 지워 빔 자체가 묻혔다.
    /// </summary>
    public static void SpawnRowBeam(GameObject prefab, int rowZ, DKSwordColor sc)
    {
        if (prefab == null) return;
        float   cs  = DKBossRoomContext.CellSize;
        Vector3 pos = DKBossRoomContext.CellToWorld(1, rowZ, BeamY) + Vector3.left * (cs * 0.5f);
        var go = BossEffectPool.SpawnOneShot(prefab, pos, Quaternion.Euler(0f, 90f, 0f), fallbackLifetime: 2f);
        if (go == null) return;
        go.transform.localScale = new Vector3(cs, 1f, (DKBossRoomContext.Width - 2) * cs);   // z = 줄 실제 길이(m)
        DKGridPatternHelper.TintVfx(go, sc == DKSwordColor.White ? Color.white : DKGridPatternHelper.DarkReadableTint);
    }

    // ── 헬퍼 ──────────────────────────────────────────────────

    private static DKSwordColor GetSwordColor(MonsterContext ctx)
        => (ctx.Monster as DeathKnightBossMonster)?.DKBlackboard.SwordColor ?? DKSwordColor.White;

    /// <summary>봉인기에 옛 봉인 사슬이 감겨 있는가(10-03 S2).</summary>
    private static bool IsBound(MonsterContext ctx)
        => !StoryProgress.IsLiberated && BossBinding.Of(ctx.Monster)?.IsBound == true;

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

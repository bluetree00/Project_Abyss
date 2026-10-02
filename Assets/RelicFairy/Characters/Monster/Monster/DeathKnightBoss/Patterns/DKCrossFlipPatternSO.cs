using System.Collections.Generic;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 죽음의 기사 2페이지 KL1 「흑백 교차」(09-28 설계 확정 §5).
///
/// 흐름:
///  Enter            → Idle2(채널) · 플레이어 구역 전체에 흑백 타일(blockCells칸 묶음 체커)
///  flipInterval마다 → 모든 칸이 흑↔백으로 뒤집힌다(flipCount번)
///  마지막 뒤집힘     → 경계 테두리로 마지막 배치를 굳힌다
///  +strikeDelay     → 기사 검 색과 같은 칸 전부 베기(기사 격자 규칙 그대로) — 마지막 색의 반대 칸만 안전
///
/// 조준 없음(구역 전체 규칙형). 무너진 가운데 3×3엔 타일을 깔지 않는다(그 칸은 무대가 이미 친다).
/// 시작 배치(짝 · 홀)는 매번 무작위 — 외워서는 못 피하고 마지막 뒤집힘을 읽어야 한다.
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/Boss/DeathKnight/Page2/DK_KL1_CrossFlip", fileName = "DK_KL1_CrossFlip")]
public class DKCrossFlipPatternSO : BossPatternSO
{
    [Header("격자 타일")]
    public GameObject whiteTilePrefab;
    public GameObject blackTilePrefab;
    [Tooltip("경계 테두리 엣지 프리팹 (DK_WarnBorder) — 마지막 배치에만 깐다")]
    public GameObject edgePrefab;
    [Tooltip("흑백 한 덩어리의 크기(격자 칸 수). 2 = 4 m 체커")]
    [Min(1)] public int blockCells = 2;

    [Header("타이밍 (초)")]
    [Tooltip("흑↔백 뒤집히는 간격")]
    public float flipInterval = 0.6f;
    [Tooltip("뒤집히는 횟수")]
    [Min(1)] public int flipCount = 3;
    [Tooltip("마지막 뒤집힘 → 베기. 반대 칸으로 옮길 시간(≥0.6 권장)")]
    public float strikeDelay = 0.9f;
    [Tooltip("공격 모션(Attack2)에서 칼이 내려오는 순간 — 베기보다 이만큼 먼저 모션을 건다")]
    public float swingLead = 0.35f;
    public float recoveryTime = 0.5f;

    [Header("VFX · 사운드")]
    [Tooltip("베인 칸 이펙트 (Sword Slash 15)")]
    public GameObject impactVfxPrefab;
    [Tooltip("플레이어 둘레 이 반경 안의 베인 덩어리에만 이펙트 — 구역 전체에 뿌리면 수백 개")]
    public float impactVfxRadius = 8f;
    public AudioClip flipSfx;
    public AudioClip bigSlashSfx;

    [Header("데미지")]
    public float damageMultiplier    = 1.2f;
    public float knockbackMultiplier = 1f;

    private DKCrossFlipState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new DKCrossFlipState(this);
    public override void OnRecycled()                      => _state = new DKCrossFlipState(this);

    public override bool CanExecute(BossPatternContext ctx)
        => ctx.Ctx.Runtime.PlayerTarget != null && ctx.Ctx.Monster is DeathKnightBossMonster;

    public override SpecialStateBase GetRuntimeState() => _state;
}

public class DKCrossFlipState : FullLockState<DKCrossFlipPatternSO>
{
    private const string ChannelAnim = "Idle2";
    private const string StrikeAnim  = "Attack2";
    private const float  TileY       = 0.05f;   // 기사 격자 타일 높이(기존 패턴과 같게)

    private readonly List<DKTileInfo> _tiles = new List<DKTileInfo>(512);
    private List<GameObject> _edges = new List<GameObject>();
    private DKPage2Zone  _zone;
    private DKSwordColor _swordColor;
    private int          _parity;
    private int          _shownFlips;
    private float        _timer;
    private bool         _swung;
    private bool         _struck;

    public DKCrossFlipState(DKCrossFlipPatternSO data) : base(data) { }

    private float StrikeTime => Data.flipCount * Data.flipInterval + Data.strikeDelay;

    public override void Enter(MonsterContext ctx)
    {
        _zone       = (ctx.Monster as DeathKnightBossMonster)?.Page2Zone;
        _swordColor = DKPage2Zone.SwordColor(ctx);
        _parity     = Random.Range(0, 2);
        _shownFlips = 0;
        _timer      = 0f;
        _swung      = false;
        _struck     = false;

        DKPage2Zone.StopAgent(ctx);
        DKPage2Zone.FacePlayer(ctx);
        DKPage2Zone.PlayAnim(ctx, ChannelAnim);
        ShowLayout(inverted: false);
        (ctx.Monster as DeathKnightBossMonster)?.HintCrossFlip();   // 첫 번째에만 규칙 자막(10-03 개선 2-2)
    }

    public override void Update(MonsterContext ctx)
    {
        _timer += Time.deltaTime;

        // 뒤집힘 — 짝수 번째면 처음 배치, 홀수 번째면 반전
        if (_shownFlips < Data.flipCount && _timer >= (_shownFlips + 1) * Data.flipInterval)
        {
            _shownFlips++;
            ShowLayout(inverted: (_shownFlips & 1) == 1);
            if (Data.flipSfx != null) Managers.Sound?.PlayEffectAt(Data.flipSfx, ctx.Transform.position);
            if (_shownFlips == Data.flipCount)
                _edges = DKGridPatternHelper.SpawnBoundaryEdges(_tiles, Data.edgePrefab);   // 마지막 배치를 굳힌다
        }

        if (!_swung && _timer >= StrikeTime - Data.swingLead)
        {
            _swung = true;
            DKPage2Zone.PlayAnim(ctx, StrikeAnim);
        }

        if (!_struck && _timer >= StrikeTime)
        {
            _struck = true;
            Strike(ctx);
        }

        if (_struck && _timer >= StrikeTime + Data.recoveryTime)
            ctx.Monster.ChangeState<AttackReadyState>();
    }

    public override void Exit(MonsterContext ctx)
    {
        DKGridPatternHelper.DestroyEdges(_edges);
        DKGridPatternHelper.DestroyTiles(_tiles);
        DKPage2Zone.RestoreAgent(ctx);
    }

    // ── 배치 ───────────────────────────────────────────────

    /// <summary>이 칸의 색 — blockCells칸 덩어리 체커, 반전이면 흑백을 바꾼다.</summary>
    private DKSwordColor ColorAt(int x, int z, bool inverted)
    {
        int bx = (x - _zone.MinX) / Data.blockCells;
        int bz = (z - _zone.MinZ) / Data.blockCells;
        bool even = ((bx + bz + _parity) & 1) == 0;
        return even != inverted ? _swordColor : DKPage2Zone.Opposite(_swordColor);
    }

    private void ShowLayout(bool inverted)
    {
        DKGridPatternHelper.DestroyEdges(_edges);
        DKGridPatternHelper.DestroyTiles(_tiles);
        if (_zone == null) return;

        float cs = DKBossRoomContext.CellSize;
        for (int z = _zone.MinZ; z <= _zone.MaxZ; z++)
        for (int x = _zone.MinX; x <= _zone.MaxX; x++)
        {
            if (_zone.IsCollapsed(x, z)) continue;
            DKSwordColor color  = ColorAt(x, z, inverted);
            GameObject   prefab = color == DKSwordColor.White ? Data.whiteTilePrefab : Data.blackTilePrefab;
            if (prefab == null) continue;

            Vector3    pos = _zone.CellCenter(x, z) + Vector3.up * TileY;
            GameObject go  = BossEffectPool.Spawn(prefab, pos, Quaternion.Euler(-90f, 0f, 0f));
            if (go == null) continue;
            go.transform.localScale = Vector3.one * cs;
            _tiles.Add(new DKTileInfo { Cell = new Vector2Int(x, z), Color = color, GO = go });
        }
    }

    // ── 베기 ───────────────────────────────────────────────

    private void Strike(MonsterContext ctx)
    {
        bool finalInverted = (Data.flipCount & 1) == 1;
        DKGridPatternHelper.DestroyEdges(_edges);
        DKGridPatternHelper.DestroyTiles(_tiles);

        Vector3 playerPos = ctx.Runtime.PlayerTarget != null ? ctx.Runtime.PlayerTarget.position : ctx.Transform.position;
        Managers.Sound?.PlayEffectAt(Data.bigSlashSfx, playerPos, startTime: 0.5f);
        SpawnImpactVfx(playerPos, finalInverted);

        if (_zone != null && DKPage2Zone.TryPlayerCell(ctx, out var pc) && _zone.Contains(pc) && !_zone.IsCollapsed(pc.x, pc.y)
            && ColorAt(pc.x, pc.y, finalInverted) == _swordColor)
        {
            DKPage2Zone.HitPlayer(ctx, Data.damageMultiplier, Data.knockbackMultiplier, HitWeight.Auto, _zone.CellCenter(pc));
        }

        BossImpactFeedback.TriggerHitStop(0.1f);
        BossImpactFeedback.TriggerCameraShake(0.12f, 0.3f);
    }

    /// <summary>플레이어 둘레의 베인 덩어리마다 이펙트 하나(덩어리 중심 · 덩어리 길이).</summary>
    private void SpawnImpactVfx(Vector3 around, bool inverted)
    {
        if (_zone == null || Data.impactVfxPrefab == null) return;
        float cs    = DKBossRoomContext.CellSize;
        float half  = (Data.blockCells - 1) * 0.5f * cs;
        float r2    = Data.impactVfxRadius * Data.impactVfxRadius;

        for (int z = _zone.MinZ; z <= _zone.MaxZ; z += Data.blockCells)
        for (int x = _zone.MinX; x <= _zone.MaxX; x += Data.blockCells)
        {
            if (ColorAt(x, z, inverted) != _swordColor) continue;
            Vector3 c = _zone.CellCenter(x, z) + new Vector3(half, 0f, half);
            float dx = c.x - around.x, dz = c.z - around.z;
            if (dx * dx + dz * dz > r2) continue;
            _zone.SpawnLineVfx(Data.impactVfxPrefab, c, alongX: false, Data.blockCells);
        }
    }
}
}

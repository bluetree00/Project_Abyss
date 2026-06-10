using System;
using System.Collections.Generic;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// DeathKnight 3회 동심원 스트라이크 (Strike / Attack4) 패턴.
///
/// 흐름:
///  Enter       → 즉시 Attack4 재생
///  hitTime1    → 패턴1 타일 (짝수 링=검 색) + SwingVfx
///  hitTime2    → 패턴2 타일 (홀수 링=검 색, 반전) + SwingVfx
///  hitTime3    → 패턴3 타일 (패턴1 동일) + SwingVfx
///  hitTime3+holdDuration → 타일 제거, Hit 페이즈 시작
///
///  Hit 페이즈: 3세트(패턴1→패턴2→패턴1), 각 세트마다
///              안쪽 링 → 바깥쪽 링 순서로 ringStep 간격으로 순차
///              → 각 링 테두리에 VFX 스폰 + 플레이어 피격
///  세트 간 hitGap 대기, 마지막 세트 후 recoveryTime → AttackReady
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/Boss/DeathKnight/DK_StrikePattern", fileName = "DK_StrikePattern")]
public class DKStrikePatternSO : BossPatternSO
{
    [Header("Grid Tiles")]
    public GameObject whiteTilePrefab;
    public GameObject blackTilePrefab;

    [Header("VFX")]
    public GameObject impactVfxPrefab;
    public GameObject swingVfxPrefab;

    [Header("Tile Show Timings")]
    public float hitTime1 = 0.5f;
    public float hitTime2 = 1.2f;
    public float hitTime3 = 1.9f;
    [Tooltip("hitTime3 이후 타일 유지 시간")]
    public float holdDuration = 0.3f;

    [Header("Hit Phase")]
    [Tooltip("링 간 VFX 딜레이 (안쪽→바깥쪽)")]
    public float ringStep     = 0.1f;
    [Tooltip("3세트 사이 대기 시간")]
    public float hitGap       = 0.4f;
    public float recoveryTime = 0.5f;

    [Header("Ring Count")]
    [Tooltip("사용할 총 링 수 (피라미드 층수). 6 = 각 색 3개씩")]
    public int ringCount = 6;

    [Header("Damage")]
    public float damageMultiplier    = 1.5f;
    public float knockbackMultiplier = 1.5f;

    private DKStrikeState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new DKStrikeState(this);
    public override void OnRecycled()                      => _state = new DKStrikeState(this);

    public override bool CanExecute(BossPatternContext ctx)
        => ctx.Ctx.Runtime.PlayerTarget != null;

    public override SpecialStateBase GetRuntimeState() => _state;
}

public class DKStrikeState : FullLockState<DKStrikePatternSO>
{
    private const string AnimName = "Attack4";

    private enum Phase { Tiles, Hits, Done }
    private Phase            _phase;
    private float            _timer;
    private bool             _swingVfxSpawned;
    private List<DKTileInfo> _tiles;
    private bool             _shown1, _shown2, _shown3, _cleared;

    // Hit 페이즈: 링별 순차 발동 목록
    private struct RingEntry { public float fireTime; public int ring; public bool isPattern2; }
    private List<RingEntry> _ringList;
    private int             _ringIdx;
    private float           _hitPhaseEnd;

    private Vector2Int   _bossCell;
    private DKSwordColor _swordColor;

    public DKStrikeState(DKStrikePatternSO data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        _phase           = Phase.Tiles;
        _timer           = 0f;
        _swingVfxSpawned = false;
        _tiles           = new List<DKTileInfo>();
        _shown1 = _shown2 = _shown3 = _cleared = false;
        _ringList        = null;
        _ringIdx         = 0;

        _bossCell   = DKBossRoomContext.WorldToCell(ctx.Transform.position);
        _swordColor = GetSwordColor(ctx);

        StopAgent(ctx);
        FacePlayer(ctx);
        PlayAnim(ctx, AnimName);
    }

    public override void Update(MonsterContext ctx)
    {
        _timer += Time.deltaTime * AnimSpeed(ctx);

        if (!_swingVfxSpawned) { _swingVfxSpawned = true; SpawnSwingVfx(ctx); }

        if (_phase == Phase.Tiles) UpdateTilePhase(ctx);
        else if (_phase == Phase.Hits) UpdateHitPhase(ctx);
    }

    // ── 타일 페이즈 ────────────────────────────────────────

    private void UpdateTilePhase(MonsterContext ctx)
    {
        int maxRing = Data.ringCount - 1; // ringCount=6 → 링 0~5

        if (!_shown1 && _timer >= Data.hitTime1)
        {
            _shown1 = true;
            DKGridPatternHelper.DestroyTiles(_tiles);
            _tiles = SpawnRingTiles(Pattern1(), maxRing);
            SpawnSwingVfx(ctx);
        }
        if (!_shown2 && _timer >= Data.hitTime2)
        {
            _shown2 = true;
            DKGridPatternHelper.DestroyTiles(_tiles);
            _tiles = SpawnRingTiles(Pattern2(), maxRing);
            SpawnSwingVfx(ctx);
        }
        if (!_shown3 && _timer >= Data.hitTime3)
        {
            _shown3 = true;
            DKGridPatternHelper.DestroyTiles(_tiles);
            _tiles = SpawnRingTiles(Pattern1(), maxRing);
            SpawnSwingVfx(ctx);
        }
        if (_shown3 && !_cleared && _timer >= Data.hitTime3 + Data.holdDuration)
        {
            _cleared = true;
            DKGridPatternHelper.DestroyTiles(_tiles);
            _timer  = 0f;
            BuildRingList();
            _phase  = Phase.Hits;
        }
    }

    // ── Hit 페이즈 ─────────────────────────────────────────

    private void BuildRingList()
    {
        _ringList = new List<RingEntry>();
        float t   = 0f;

        int maxRing = Data.ringCount - 1; // ringCount=6 → 링 0~5

        var evens = new List<int>();
        var odds  = new List<int>();

        for (int r = 0; r <= maxRing; r++)
        {
            if (!HasInteriorOnRing(r)) break;
            if (r % 2 == 0) evens.Add(r);
            else             odds.Add(r);
        }

        // 세트1: 짝수 링 (Pattern1)
        foreach (int r in evens) { _ringList.Add(new RingEntry { fireTime = t, ring = r, isPattern2 = false }); t += Data.ringStep; }
        t += Data.hitGap;

        // 세트2: 홀수 링 (Pattern2)
        foreach (int r in odds) { _ringList.Add(new RingEntry { fireTime = t, ring = r, isPattern2 = true  }); t += Data.ringStep; }
        t += Data.hitGap;

        // 세트3: 짝수 링 다시 (Pattern1)
        foreach (int r in evens) { _ringList.Add(new RingEntry { fireTime = t, ring = r, isPattern2 = false }); t += Data.ringStep; }

        _hitPhaseEnd = t + Data.recoveryTime;
    }

    private void UpdateHitPhase(MonsterContext ctx)
    {
        while (_ringIdx < _ringList.Count && _timer >= _ringList[_ringIdx].fireTime)
        {
            var e = _ringList[_ringIdx];
            DKGridPatternHelper.SpawnRingPerimeterVfx(
                Data.impactVfxPrefab, _bossCell, e.ring, _swordColor);
            DKGridPatternHelper.TriggerRingDamage(
                ctx, _bossCell, e.ring, Data.damageMultiplier, Data.knockbackMultiplier);
            _ringIdx++;
        }

        if (_ringIdx >= _ringList.Count && _timer >= _hitPhaseEnd)
        {
            _phase = Phase.Done;
            ctx.Monster.ChangeState<AttackReadyState>();
        }
    }

    // ── 색상 규칙 ────────────────────────────────────────────

    private Func<int, int, DKSwordColor> Pattern1()
        => (x, z) => Ring(x, z) % 2 == 0 ? _swordColor : Opposite(_swordColor);

    private Func<int, int, DKSwordColor> Pattern2()
        => (x, z) => Ring(x, z) % 2 == 1 ? _swordColor : Opposite(_swordColor);

    private int Ring(int x, int z)
        => Mathf.Max(Mathf.Abs(x - _bossCell.x), Mathf.Abs(z - _bossCell.y));

    private bool HasInteriorOnRing(int r)
    {
        int bx = _bossCell.x, bz = _bossCell.y;
        if (r == 0) return DKBossRoomContext.IsInterior(bx, bz);
        for (int x = bx - r; x <= bx + r; x++)
        {
            if (DKBossRoomContext.IsInterior(x, bz + r)) return true;
            if (DKBossRoomContext.IsInterior(x, bz - r)) return true;
        }
        for (int z = bz - r + 1; z < bz + r; z++)
        {
            if (DKBossRoomContext.IsInterior(bx - r, z)) return true;
            if (DKBossRoomContext.IsInterior(bx + r, z)) return true;
        }
        return false;
    }

    private static DKSwordColor Opposite(DKSwordColor c)
        => c == DKSwordColor.White ? DKSwordColor.Black : DKSwordColor.White;

    /// <summary>ring 거리가 maxRing 이하인 셀에만 타일을 스폰한다.</summary>
    private List<DKTileInfo> SpawnRingTiles(Func<int, int, DKSwordColor> rule, int maxRing)
    {
        var result = new List<DKTileInfo>();
        foreach (var cell in DKBossRoomContext.GetInteriorCells())
        {
            if (Ring(cell.x, cell.y) > maxRing) continue;

            DKSwordColor color  = rule(cell.x, cell.y);
            GameObject   prefab = color == DKSwordColor.White
                ? Data.whiteTilePrefab : Data.blackTilePrefab;
            if (prefab == null) continue;

            Vector3    pos = DKBossRoomContext.CellToWorld(cell.x, cell.y, 0.05f);
            GameObject go  = BossEffectPool.Spawn(prefab, pos, Quaternion.Euler(-90f, 0f, 0f));
            if (go == null) continue;
            result.Add(new DKTileInfo { Cell = cell, Color = color, GO = go });
        }
        return result;
    }

    // ── 헬퍼 ─────────────────────────────────────────────────

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

    private static float AnimSpeed(MonsterContext ctx)
        => (ctx.Monster as DeathKnightBossMonster)?.DKBlackboard.AnimSpeedMult ?? 1f;

    private static void PlayAnim(MonsterContext ctx, string stateName)
    {
        if (ctx.Animator == null) return;
        if (!ctx.Animator.HasState(0, Animator.StringToHash(stateName)))
        {
            Debug.LogWarning($"[DKStrike] '{stateName}' not found", ctx.Monster);
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

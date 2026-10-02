using System.Collections.Generic;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 죽음의 기사 2페이지 간판 KL-S 「원탁의 무덤」(09-28 설계 확정 §5) — 2페이지 체력 50%에서 한 번.
///
/// 흐름:
///  Enter           → 기사 무적 채널링(Idle2 · 방패 이펙트)
///  summonDelay     → 플레이어 구역 네 귀퉁이(사분면 가운데)에 무덤 기둥 넷(각 체력) — 청록 = 칠 수 있음
///  채널링 동안      → 먼 끝 줄부터 기사 쪽으로 검 색 타일이 한 줄씩 차오른다(검 소환 Strike와 같은 시계) — 다 차면 곧 벤다
///  기둥이 부서지면  → 그 자리에 흰 원(안전지대)이 남는다
///  +channelSeconds → 전역 베기 — 안전지대 안이 아니면 최대 체력의 failDamageRatio(즉사 아님)
///
/// 간판 규약: 발동 조건 = <see cref="BossPages.SignatureDue"/>, Enter에서 <see cref="BossPages.MarkSignatureDone"/>.
/// 약점은 기둥이라 보스 무적은 FSM 무적 제약이 아니라 기사 자체 무적(<see cref="DeathKnightBossBlackboard.SetInvincible"/>)으로 건다.
/// 보스가 직접 건다(<see cref="DeathKnightBossMonster"/> Update) — 콤보 러너는 강제 항목도 콤보가 끝나야 낸다.
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/Boss/DeathKnight/Page2/DK_KLS_RoundTableTomb", fileName = "DK_KLS_RoundTableTomb")]
public class DKRoundTableTombPatternSO : BossPatternSO
{
    [Header("무덤 기둥")]
    public float pillarHp     = 240f;
    public float pillarRadius = 0.7f;
    public float pillarHeight = 3f;
    [Tooltip("플레이어 구역 가운데에서 기둥까지 — 좌우(m)")]
    public float pillarOffsetX = 7f;
    [Tooltip("플레이어 구역 가운데에서 기둥까지 — 앞뒤(m)")]
    public float pillarOffsetZ = 5.5f;
    [Tooltip("기둥 발밑 이펙트(선택)")]
    public GameObject pillarVfxPrefab;
    public float      pillarVfxScale = 0.4f;
    public GameObject pillarBreakVfxPrefab;
    public float      pillarBreakVfxScale = 0.4f;
    public AudioClip  pillarBreakSfx;

    [Header("안전지대")]
    [Tooltip("부서진 기둥 자리 안전 반경(m)")]
    public float safeRadius = 3.5f;
    [Tooltip("안전지대 보호막 이펙트(선택) — 검 소환(Strike)의 GuardianShield와 같은 모양이라 「여기가 안전」이 바로 읽힌다")]
    public GameObject safeVfxPrefab;

    [Header("타이밍 (초)")]
    public float summonDelay    = 0.8f;
    [Tooltip("기둥이 선 뒤 전역 베기까지 — 설계 10초")]
    public float channelSeconds = 10f;
    [Tooltip("타일이 구역을 다 채우는 시점(베기 전 초)")]
    public float lockLead       = 0.4f;
    [Tooltip("공격 모션(Attack2)에서 칼이 내려오는 순간 — 베기보다 이만큼 먼저 모션을 건다")]
    public float swingLead      = 0.35f;
    public float recoveryTime   = 0.8f;

    [Header("격자 타일 (채널링 시계)")]
    public GameObject whiteTilePrefab;
    public GameObject blackTilePrefab;

    [Header("보스 VFX")]
    [Tooltip("채널링 무적 방패 이펙트")]
    public GameObject bossShieldVfxPrefab;

    [Header("전역 베기")]
    [Tooltip("줄 베기 이펙트 (Sword Slash 15) — 두 줄마다 하나")]
    public GameObject slashVfxPrefab;
    public AudioClip  bigSlashSfx;
    [Tooltip("안전지대 밖이면 플레이어 최대 체력의 이 비율 — 풀피에서 즉사하지 않게 1 미만")]
    [Range(0f, 0.95f)] public float failDamageRatio = 0.5f;
    public float knockbackMultiplier = 1.5f;

    private DKRoundTableTombState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new DKRoundTableTombState(this);
    public override void OnRecycled()                      => _state = new DKRoundTableTombState(this);

    public override bool CanExecute(BossPatternContext ctx)
        => ctx?.Ctx?.Monster is DeathKnightBossMonster dk && dk.Pages.SignatureDue(dk.CurrentHp);

    public override bool CanForceInterrupt(BossPatternContext ctx) => CanExecute(ctx);

    public override SpecialStateBase GetRuntimeState() => _state ??= new DKRoundTableTombState(this);
}

public class DKRoundTableTombState : FullLockState<DKRoundTableTombPatternSO>
{
    private const string ChannelAnim = "Idle2";
    private const string SlashAnim   = "Attack2";
    private const int    PillarCount = 4;
    private const float  TileY       = 0.05f;
    private const float  ShieldVfxSphereUnit = 15f;   // Effect_09_GuardianShield 스케일 1 기준 반경(DKStrikeState와 같은 값)

    private readonly List<DKTombPillar> _pillars   = new List<DKTombPillar>(PillarCount);
    private readonly List<Vector3>      _safeSpots = new List<Vector3>(PillarCount);
    private readonly List<GameObject>   _safeDiscs = new List<GameObject>(PillarCount);
    private readonly List<GameObject>   _safeVfx   = new List<GameObject>(PillarCount);
    private readonly List<DKTileInfo>   _tiles     = new List<DKTileInfo>(512);
    private DeathKnightBossMonster _dk;
    private DKPage2Zone _zone;
    private GameObject  _shieldVfx;
    private DKSwordColor _swordColor;
    private int         _rowsFilled;
    private float       _timer;
    private bool        _summoned;
    private bool        _swung;
    private bool        _slashed;

    public DKRoundTableTombState(DKRoundTableTombPatternSO data) : base(data) { }

    private float SlashTime => Data.summonDelay + Data.channelSeconds;

    public override void Enter(MonsterContext ctx)
    {
        _dk      = ctx.Monster as DeathKnightBossMonster;
        _zone    = _dk != null ? _dk.Page2Zone : null;
        _timer   = 0f;
        _summoned = _swung = _slashed = false;
        _rowsFilled = 0;
        _swordColor = DKPage2Zone.SwordColor(ctx);
        _pillars.Clear();
        _safeSpots.Clear();
        _safeDiscs.Clear();
        _safeVfx.Clear();

        _dk?.Pages.MarkSignatureDone();
        _dk?.DKBlackboard.SetInvincible(true);

        DKPage2Zone.StopAgent(ctx);
        DKPage2Zone.FacePlayer(ctx);
        DKPage2Zone.PlayAnim(ctx, ChannelAnim);
        SpawnShield(ctx);
        Debug.Log("[DK] 간판 「원탁의 무덤」 — 2페이지 50%", ctx.Monster);
    }

    public override void Update(MonsterContext ctx)
    {
        _timer += Time.deltaTime;
        if (_zone == null) { ctx.Monster.ChangeState<AttackReadyState>(); return; }

        if (!_summoned && _timer >= Data.summonDelay)
        {
            _summoned = true;
            SpawnPillars();
        }

        // 채널링 시계 — 먼 끝 줄부터 한 줄씩, lockLead 전에 다 찬다
        if (_summoned && _rowsFilled < _zone.Rows)
        {
            float k = (_timer - Data.summonDelay) / Mathf.Max(0.01f, Data.channelSeconds - Data.lockLead);
            int target = Mathf.Min(_zone.Rows, Mathf.CeilToInt(Mathf.Clamp01(k) * _zone.Rows));
            while (_rowsFilled < target) FillRow(_rowsFilled++);
        }

        if (!_swung && _timer >= SlashTime - Data.swingLead)
        {
            _swung = true;
            DKPage2Zone.PlayAnim(ctx, SlashAnim);
        }

        if (!_slashed && _timer >= SlashTime)
        {
            _slashed = true;
            GlobalSlash(ctx);
        }

        if (_slashed && _timer >= SlashTime + Data.recoveryTime)
            ctx.Monster.ChangeState<AttackReadyState>();
    }

    public override void Exit(MonsterContext ctx)
    {
        Cleanup();
        (ctx.Monster as DeathKnightBossMonster)?.DKBlackboard.SetInvincible(false);
        DKPage2Zone.RestoreAgent(ctx);
    }

    // ── 기둥 · 안전지대 ────────────────────────────────────

    /// <summary>플레이어 구역 가운데(앵커)를 중심으로 네 사분면 가운데에 기둥을 세운다.</summary>
    private void SpawnPillars()
    {
        Vector3 c = _zone.CenterWorld;
        for (int i = 0; i < PillarCount; i++)
        {
            float sx = (i & 1) == 0 ? -1f : 1f;
            float sz = (i & 2) == 0 ? -1f : 1f;
            Vector3 pos = new Vector3(c.x + sx * Data.pillarOffsetX, c.y, c.z + sz * Data.pillarOffsetZ);
            var pillar = DKTombPillar.Create(pos, Data.pillarHp, Data.pillarRadius, Data.pillarHeight,
                                             Data.pillarVfxPrefab, Data.pillarVfxScale,
                                             Data.pillarBreakVfxPrefab, Data.pillarBreakVfxScale, Data.pillarBreakSfx,
                                             OnPillarBroken);
            _pillars.Add(pillar);
        }
    }

    private void OnPillarBroken(DKTombPillar pillar)
    {
        if (_slashed || pillar == null) return;
        Vector3 p = pillar.transform.position;
        _safeSpots.Add(p);
        _safeDiscs.Add(PatternGuideHelper.Disc(p, Data.safeRadius, DKPage2Zone.SafeWhite));
        if (Data.safeVfxPrefab == null) return;
        var fx = BossEffectPool.Spawn(Data.safeVfxPrefab, p, Quaternion.identity);
        if (fx == null) return;
        fx.transform.localScale = Vector3.one * (Data.safeRadius / ShieldVfxSphereUnit);
        DKGridPatternHelper.TintShieldVfx(fx, DKSwordColor.White);   // 흰 보호막 = 안전
        _safeVfx.Add(fx);
    }

    private bool InSafeSpot(Vector3 pos)
    {
        float r2 = Data.safeRadius * Data.safeRadius;
        for (int i = 0; i < _safeSpots.Count; i++)
        {
            float dx = pos.x - _safeSpots[i].x, dz = pos.z - _safeSpots[i].z;
            if (dx * dx + dz * dz <= r2) return true;
        }
        return false;
    }

    /// <summary>먼 끝에서 <paramref name="index"/>번째 줄에 검 색 타일을 깐다(무너진 칸은 건너뛴다).</summary>
    private void FillRow(int index)
    {
        GameObject prefab = _swordColor == DKSwordColor.White ? Data.whiteTilePrefab : Data.blackTilePrefab;
        if (prefab == null) return;
        int z = _zone.TowardKnight > 0 ? _zone.MinZ + index : _zone.MaxZ - index;
        for (int x = _zone.MinX; x <= _zone.MaxX; x++)
        {
            if (_zone.IsCollapsed(x, z)) continue;
            GameObject go = BossEffectPool.Spawn(prefab, _zone.CellCenter(x, z) + Vector3.up * TileY, Quaternion.Euler(-90f, 0f, 0f));
            if (go == null) continue;
            go.transform.localScale = Vector3.one * DKBossRoomContext.CellSize;
            _tiles.Add(new DKTileInfo { Cell = new Vector2Int(x, z), Color = _swordColor, GO = go });
        }
    }

    // ── 전역 베기 ──────────────────────────────────────────

    private void GlobalSlash(MonsterContext ctx)
    {
        int midCol = (_zone.MinX + _zone.MaxX) / 2;
        for (int z = _zone.MinZ; z <= _zone.MaxZ; z += 2)
            _zone.SpawnLineVfx(Data.slashVfxPrefab, _zone.CellCenter(midCol, z), alongX: true, _zone.Columns);
        Managers.Sound?.PlayEffectAt(Data.bigSlashSfx, _zone.CenterWorld, startTime: 0.5f);

        var player = ctx.Runtime.CachedPlayer;
        if (player != null && player.RuntimeStats != null && !InSafeSpot(player.transform.position))
        {
            int dmg = BossMaxHpDamage.Raw(player, Data.failDamageRatio);   // 최대 체력 비율 — 방어로 다시 깎이지 않게(10-01)
            player.TakeDamage(dmg, ctx.Monster.gameObject, false, HitWeight.Heavy);
            Vector3 away = player.transform.position - _zone.CenterWorld;
            away.y = 0f;
            Vector3 dir = away.sqrMagnitude > 0.001f ? away.normalized : -ctx.Transform.forward;
            dir.y = 0.2f;
            player.ApplyKnockback(dir * ctx.Config.stat.knockbackForce * Data.knockbackMultiplier);
        }

        BossImpactFeedback.TriggerHitStop(0.2f);
        BossImpactFeedback.TriggerCameraShake(0.2f, 0.4f);
        BossImpactFeedback.TriggerScreenFlash(new Color(0.85f, 0.85f, 0.9f, 0.5f), 0.1f);

        // 베기 뒤 — 남은 기둥 · 예고를 걷고 무적을 푼다(회복 모션 동안 칠 수 있다)
        Cleanup();
        _dk?.DKBlackboard.SetInvincible(false);
    }

    // ── 정리 ───────────────────────────────────────────────

    private void SpawnShield(MonsterContext ctx)
    {
        if (Data.bossShieldVfxPrefab == null) return;
        _shieldVfx = BossEffectPool.Spawn(Data.bossShieldVfxPrefab, ctx.Transform.position, Quaternion.identity);
        if (_shieldVfx == null) return;
        _shieldVfx.transform.SetParent(ctx.Transform, worldPositionStays: true);
        DKGridPatternHelper.TintShieldVfx(_shieldVfx, DKPage2Zone.SwordColor(ctx));
    }

    private void Cleanup()
    {
        foreach (var p in _pillars) if (p != null) p.Dismiss();
        _pillars.Clear();
        for (int i = 0; i < _safeDiscs.Count; i++)
        {
            var d = _safeDiscs[i];
            PatternGuideHelper.SafeDestroy(ref d);
        }
        _safeDiscs.Clear();
        foreach (var fx in _safeVfx) if (fx != null) BossEffectPool.Release(fx);
        _safeVfx.Clear();
        DKGridPatternHelper.DestroyTiles(_tiles);
        if (_shieldVfx != null)
        {
            _shieldVfx.transform.SetParent(null);
            BossEffectPool.Release(_shieldVfx);
            _shieldVfx = null;
        }
    }
}
}

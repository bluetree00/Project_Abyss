using System.Collections.Generic;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 죽음의 기사 2페이지 KL2 「카믈란의 창」(09-28 설계 확정 §5).
///
/// 흐름: 네 벽에서 창이 시계 방향으로 한 줄씩 — 북(유리벽 · 기사 쪽) 세로줄 → 동 가로줄 → 남 세로줄 → 서 가로줄.
///  lineInterval마다 → 줄 하나가 나타나 lineAim초 동안 플레이어의 열/행을 따라온다(회색이 창끝 쪽으로 차오른다)
///  조준 고정       → 판정 색(빨강)으로 굳는다 → lineTelegraph초(0.5) 뒤 그 줄 전체를 꿰뚫는다
///  마지막 줄 고정  → 세로줄 × 가로줄 교차점(최대 4곳)에 원 예고 → crossDelay초 뒤 교차점이 터진다
///
/// 09-28 R3 — 조준은 판정 lineTelegraph(0.5 ≥ 0.4)초 전에 멈추고, 멈추는 순간 가이드를 판정 색으로 바꾼다.
/// 교차점도 터지기 lockLead(0.4)초 전에 판정 색으로 굳는다. 줄 사이 칸이 안전하고, 교차점은 마지막에 한 번 더 터진다.
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/Boss/DeathKnight/Page2/DK_KL2_CamlannSpear", fileName = "DK_KL2_CamlannSpear")]
public class DKCamlannSpearPatternSO : BossPatternSO
{
    [Header("타이밍 (초)")]
    [Tooltip("줄이 나타나는 간격 — 시계 방향 네 줄")]
    public float lineInterval  = 0.45f;
    [Tooltip("줄이 플레이어의 열/행을 따라오는 시간(회색 예고)")]
    public float lineAim       = 0.2f;
    [Tooltip("조준 고정(판정 색) → 꿰뚫기. R3: 0.4 이상")]
    public float lineTelegraph = 0.5f;
    [Tooltip("마지막 줄 고정 → 교차점 폭발")]
    public float crossDelay    = 0.9f;
    [Tooltip("교차점이 판정 색으로 굳는 시점(폭발 전 초). R3: 0.4 이상")]
    public float lockLead      = 0.4f;
    public float recoveryTime  = 0.5f;

    [Header("판정")]
    [Tooltip("교차점 폭발 반경(m)")]
    public float crossRadius = 2.2f;

    [Header("VFX · 사운드")]
    [Tooltip("줄을 꿰뚫는 이펙트 (Sword Slash 15)")]
    public GameObject lineVfxPrefab;
    [Tooltip("교차점 폭발 이펙트")]
    public GameObject crossVfxPrefab;
    public float      crossVfxScale = 1f;
    public AudioClip  spearSfx;
    public AudioClip  crossSfx;

    [Header("데미지")]
    public float lineDamageMultiplier  = 1f;
    public float crossDamageMultiplier = 1.3f;
    public float knockbackMultiplier   = 1f;

    private DKCamlannSpearState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new DKCamlannSpearState(this);
    public override void OnRecycled()                      => _state = new DKCamlannSpearState(this);

    public override bool CanExecute(BossPatternContext ctx)
        => ctx.Ctx.Runtime.PlayerTarget != null && ctx.Ctx.Monster is DeathKnightBossMonster;

    public override SpecialStateBase GetRuntimeState() => _state;
}

public class DKCamlannSpearState : FullLockState<DKCamlannSpearPatternSO>
{
    private const string CastAnim    = "Attack4";   // 검을 세 번 치켜든다(창을 부른다)
    private const int    LineCount   = 4;
    private const float  GuideLinger = 0.15f;       // 판정 뒤 판정 색을 잠깐 남긴다

    private struct Line
    {
        public bool       Shown;
        public bool       Locked;
        public bool       Fired;
        public bool       AlongX;     // true = 가로줄(행), false = 세로줄(열)
        public int        Index;      // 행 z 또는 열 x
        public float      ShowTime;
        public GameObject Guide;
        public float      KillAt;
    }

    private readonly Line[]           _lines       = new Line[LineCount];
    private readonly List<Vector3>    _crosses     = new List<Vector3>(4);
    private readonly List<GameObject> _crossGuides = new List<GameObject>(4);
    private DKPage2Zone _zone;
    private float       _timer;
    private bool        _crossShown;
    private bool        _crossLocked;
    private bool        _crossFired;
    private float       _crossShowTime;
    private float       _crossKillAt;

    public DKCamlannSpearState(DKCamlannSpearPatternSO data) : base(data) { }

    private float LockTime(in Line l)  => l.ShowTime + Data.lineAim;
    private float FireTime(in Line l)  => LockTime(l) + Data.lineTelegraph;
    private float CrossTime            => _crossShowTime + Data.crossDelay;

    public override void Enter(MonsterContext ctx)
    {
        _zone        = (ctx.Monster as DeathKnightBossMonster)?.Page2Zone;
        _timer       = 0f;
        _crossShown  = false;
        _crossLocked = false;
        _crossFired  = false;
        for (int i = 0; i < LineCount; i++)
            _lines[i] = new Line { AlongX = (i & 1) == 1, ShowTime = i * Data.lineInterval };
        _crosses.Clear();
        _crossGuides.Clear();

        DKPage2Zone.StopAgent(ctx);
        DKPage2Zone.FacePlayer(ctx);
        DKPage2Zone.PlayAnim(ctx, CastAnim);
    }

    public override void Update(MonsterContext ctx)
    {
        _timer += Time.deltaTime;
        if (_zone == null) { ctx.Monster.ChangeState<AttackReadyState>(); return; }

        bool allFired = true;
        for (int i = 0; i < LineCount; i++)
        {
            ref Line line = ref _lines[i];
            if (!line.Shown && _timer >= line.ShowTime) ShowLine(ctx, ref line, i);
            if (line.Shown && !line.Locked)
            {
                // 조준 — 플레이어의 열/행을 따라오며 회색이 차오른다
                AimLine(ctx, ref line, i);
                PatternGuideHelper.SetProgress(line.Guide, (_timer - line.ShowTime) / Mathf.Max(0.01f, Data.lineAim));
                if (_timer >= LockTime(line))
                {
                    line.Locked = true;
                    PatternGuideHelper.Arm(line.Guide);   // R3 — 조준을 멈추는 순간 판정 색
                }
            }
            if (line.Locked && !line.Fired && _timer >= FireTime(line)) FireLine(ctx, ref line);
            if (line.Fired && line.Guide != null && _timer >= line.KillAt) PatternGuideHelper.SafeDestroy(ref line.Guide);
            allFired &= line.Fired;
        }

        if (!_crossShown && _lines[LineCount - 1].Locked) ShowCrosses();
        if (_crossShown && !_crossFired)
        {
            float t = (_timer - _crossShowTime) / Mathf.Max(0.01f, Data.crossDelay - Data.lockLead);
            if (!_crossLocked)
            {
                for (int i = 0; i < _crossGuides.Count; i++) PatternGuideHelper.SetProgress(_crossGuides[i], t);
                if (_timer >= CrossTime - Data.lockLead)
                {
                    _crossLocked = true;
                    for (int i = 0; i < _crossGuides.Count; i++) PatternGuideHelper.Arm(_crossGuides[i]);
                }
            }
            if (_timer >= CrossTime) FireCrosses(ctx);
        }
        if (_crossFired && _timer >= _crossKillAt) DestroyCrossGuides();

        if (allFired && _crossFired && _timer >= CrossTime + Data.recoveryTime)
            ctx.Monster.ChangeState<AttackReadyState>();
    }

    public override void Exit(MonsterContext ctx)
    {
        for (int i = 0; i < LineCount; i++) PatternGuideHelper.SafeDestroy(ref _lines[i].Guide);
        DestroyCrossGuides();
        DKPage2Zone.RestoreAgent(ctx);
    }

    // ── 줄 ─────────────────────────────────────────────────

    private void ShowLine(MonsterContext ctx, ref Line line, int order)
    {
        line.Shown = true;
        float cs = DKBossRoomContext.CellSize;
        line.Guide = PatternGuideHelper.Prepare(
            PatternGuideHelper.Beam(Vector3.zero, Vector3.forward, cs, cs * 0.95f, DKPage2Zone.Grey), DKPage2Zone.Grey);
        AimLine(ctx, ref line, order);
        if (Data.spearSfx != null) Managers.Sound?.PlayEffectAt(Data.spearSfx, ctx.Transform.position, 0.6f);
    }

    /// <summary>
    /// 시계 방향 order번째 줄을 플레이어가 지금 선 열/행에 놓는다 —
    /// 0 북(기사 쪽 벽 → 세로줄), 1 동(가로줄), 2 남(세로줄), 3 서(가로줄). 창끝(화살표 머리)이 날아갈 쪽.
    /// </summary>
    private void AimLine(MonsterContext ctx, ref Line line, int order)
    {
        Vector2Int pc = DKPage2Zone.TryPlayerCell(ctx, out var cell) ? cell : _zone.Center;
        pc.x = Mathf.Clamp(pc.x, _zone.MinX, _zone.MaxX);
        pc.y = Mathf.Clamp(pc.y, _zone.MinZ, _zone.MaxZ);
        line.Index = line.AlongX ? pc.y : pc.x;

        float   cs    = DKBossRoomContext.CellSize;
        float   north = _zone.TowardKnight;   // 기사 쪽 z 부호
        float   east  = _zone.TowardKnight;   // 기사를 바라볼 때 오른쪽 x 부호
        Vector3 origin, dir;
        float   range;
        if (!line.AlongX)
        {
            // 세로줄 — 북(0)은 앞줄에서 먼 끝으로, 남(2)은 먼 끝에서 앞줄로
            bool fromNorth = order == 0;
            int  startRow  = fromNorth ? _zone.FrontRow : (_zone.TowardKnight > 0 ? _zone.MinZ : _zone.MaxZ);
            dir    = new Vector3(0f, 0f, fromNorth ? -north : north);
            origin = _zone.CellCenter(line.Index, startRow) - dir * (cs * 0.5f);
            range  = _zone.Rows * cs;
        }
        else
        {
            // 가로줄 — 동(1)은 동쪽 끝에서 서쪽으로, 서(3)는 서쪽 끝에서 동쪽으로
            bool fromEast = order == 1;
            int  startCol = fromEast == (east > 0f) ? _zone.MaxX : _zone.MinX;
            dir    = new Vector3(fromEast ? -east : east, 0f, 0f);
            origin = _zone.CellCenter(startCol, line.Index) - dir * (cs * 0.5f);
            range  = _zone.Columns * cs;
        }
        PatternGuideHelper.PlaceBeam(line.Guide, origin, dir, range, cs * 0.95f);
    }

    private void FireLine(MonsterContext ctx, ref Line line)
    {
        line.Fired  = true;
        line.KillAt = _timer + GuideLinger;

        Vector3 center = line.AlongX
            ? _zone.CellCenter((_zone.MinX + _zone.MaxX) / 2, line.Index)
            : _zone.CellCenter(line.Index, (_zone.MinZ + _zone.MaxZ) / 2);
        _zone.SpawnLineVfx(Data.lineVfxPrefab, center, line.AlongX, line.AlongX ? _zone.Columns : _zone.Rows);
        if (Data.spearSfx != null) Managers.Sound?.PlayEffectAt(Data.spearSfx, center);

        if (DKPage2Zone.TryPlayerCell(ctx, out var pc) && _zone.Contains(pc)
            && (line.AlongX ? pc.y == line.Index : pc.x == line.Index))
        {
            DKPage2Zone.HitPlayer(ctx, Data.lineDamageMultiplier, Data.knockbackMultiplier, HitWeight.Auto, _zone.CellCenter(pc));
        }
        BossImpactFeedback.TriggerCameraShake(0.08f, 0.18f);
    }

    // ── 교차점 ─────────────────────────────────────────────

    /// <summary>세로줄(0 · 2) × 가로줄(1 · 3) 교차점 — 네 줄이 모두 고정된 뒤. 겹치면 하나로.</summary>
    private void ShowCrosses()
    {
        _crossShown    = true;
        _crossShowTime = _timer;
        for (int c = 0; c < LineCount; c += 2)
        for (int r = 1; r < LineCount; r += 2)
        {
            Vector3 p = _zone.CellCenter(_lines[c].Index, _lines[r].Index);
            bool dup = false;
            for (int i = 0; i < _crosses.Count; i++)
                if ((_crosses[i] - p).sqrMagnitude < 0.01f) { dup = true; break; }
            if (dup) continue;
            _crosses.Add(p);
            _crossGuides.Add(PatternGuideHelper.Prepare(
                PatternGuideHelper.Disc(p, Data.crossRadius, DKPage2Zone.Grey), DKPage2Zone.Grey));
        }
    }

    private void FireCrosses(MonsterContext ctx)
    {
        _crossFired  = true;
        _crossKillAt = _timer + GuideLinger * 1.5f;

        Vector3 playerPos = ctx.Runtime.PlayerTarget != null ? ctx.Runtime.PlayerTarget.position : Vector3.positiveInfinity;
        float   r2        = Data.crossRadius * Data.crossRadius;
        bool    hit       = false;
        Vector3 hitFrom   = Vector3.zero;
        for (int i = 0; i < _crosses.Count; i++)
        {
            Vector3 p = _crosses[i];
            if (Data.crossVfxPrefab != null)
            {
                var fx = BossEffectPool.SpawnOneShot(Data.crossVfxPrefab, p, Quaternion.identity, fallbackLifetime: 2.5f);
                if (fx != null) fx.transform.localScale = Vector3.one * Data.crossVfxScale;
            }
            float dx = playerPos.x - p.x, dz = playerPos.z - p.z;
            if (!hit && dx * dx + dz * dz <= r2) { hit = true; hitFrom = p; }
        }
        if (_crosses.Count > 0 && Data.crossSfx != null) Managers.Sound?.PlayEffectAt(Data.crossSfx, _crosses[0]);

        if (hit) DKPage2Zone.HitPlayer(ctx, Data.crossDamageMultiplier, Data.knockbackMultiplier, HitWeight.Auto, hitFrom);
        BossImpactFeedback.TriggerHitStop(0.08f);
        BossImpactFeedback.TriggerCameraShake(0.14f, 0.3f);
    }

    private void DestroyCrossGuides()
    {
        for (int i = 0; i < _crossGuides.Count; i++)
        {
            var g = _crossGuides[i];
            PatternGuideHelper.SafeDestroy(ref g);
        }
        _crossGuides.Clear();
    }
}
}

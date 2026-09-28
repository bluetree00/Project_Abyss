using System.Collections.Generic;
using RelicFairy.UI;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// M6 「해골 군단」 — 숨통 구간. 리치 설계서 §3-2 · §13.
///
/// O 소환(castDuration) — 리치가 제단 중앙 상공으로 올라가고, 제단 가장자리 네 방향 바닥이 어둡게 끓는다(표식 + 이펙트)
/// → A 군단(최대 legionDuration) — 방향마다 해골 perSide마리가 솟아 쫓아온다. 리치는 중앙 상공을 천천히 돌며
///   도중에 약한 마력탄 한 발만 쏜다. 해골이 모두 쓰러지면 일찍 끝난다
/// → R 복귀(recoveryDuration) — 원래 고도로 내려온다
/// 위협은 낮고, 해골을 처치하는 재미가 역할이다.
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/Boss/Lich/Lich_SkeletonSummonPattern", fileName = "Lich_SkeletonSummonPattern")]
public class LichSkeletonSummonPatternSO : BossPatternSO
{
    [Header("M6 — 발동")]
    public float patternCooldown = 25f;

    [Header("M6 — 타이밍 (초)")]
    public float castDuration     = 1.5f;
    public float legionDuration   = 12f;
    public float recoveryDuration = 0.5f;

    [Header("M6 — 소환")]
    [Tooltip("해골 프리팹(LichSkeletonMonster)")]
    public GameObject skeletonPrefab;
    [Tooltip("가장자리 한 방향에서 솟는 해골 수")]
    public int   perSide     = 2;
    [Tooltip("제단 가장자리에서 안쪽으로 이만큼 들어온 곳에 솟는다 (m)")]
    public float edgeInset   = 3f;
    [Tooltip("같은 방향 해골 사이 간격 (m)")]
    public float sideSpacing = 2.5f;
    public float markRadius  = 2.2f;

    [Header("M6 — 리치 움직임")]
    [Tooltip("기본 고도 위로 더 올라가는 높이 (m)")]
    public float hoverHeight = 4f;
    [Tooltip("중앙 상공에서 도는 반경 (m)")]
    public float orbitRadius = 3f;
    [Tooltip("한 바퀴의 1/4을 도는 시간 (초)")]
    public float orbitLeg    = 2.5f;

    [Header("M6 — 약한 마력탄 (한 발)")]
    public float boltAt        = 5f;
    public float boltWarn      = 0.6f;
    public float boltRadius    = 1.8f;
    public float boltDamage    = 0.6f;

    // ── 런타임 ───────────────────────────────────────────
    private LichSkeletonSummonState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new LichSkeletonSummonState(this);
    public override void OnRecycled()                       => _state = new LichSkeletonSummonState(this);

    public override bool CanExecute(BossPatternContext ctx)
    {
        if (ctx.Ctx.Runtime.PlayerTarget == null) return false;
        var lichBB = (ctx.Boss as LichMonster)?.LichBB;
        return lichBB == null || lichBB.SkeletonSummonCooldown <= 0f;
    }

    public override SpecialStateBase GetRuntimeState() => _state;
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// LichSkeletonSummonState — 중단 불가(피해는 받음)
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class LichSkeletonSummonState : UnInterruptibleState<LichSkeletonSummonPatternSO>
{
    private enum Phase { Cast, Legion, Recovery }

    private const int    Sides         = 4;
    private const string LegionBarkKey = "Lich_Legion";   // 대사 CSV — 없으면 조용히 넘어간다

    private readonly List<Vector3>     _spawnPoints = new(8);
    private readonly List<GameObject>  _marks       = new(4);
    private readonly List<GameObject>  _markVfx     = new(4);
    private readonly List<MonsterBase> _spawned     = new(8);

    private Phase      _phase;
    private float      _timer;
    private int        _aliveCount;
    private float      _orbitTimer;
    private int        _orbitStep;
    private bool       _boltFired;
    private bool       _boltLanded;
    private Vector3    _boltTarget;
    private GameObject _boltDisc;
    private GameObject _boltVfx;
    private Vector3    _boltFrom;

    public LichSkeletonSummonState(LichSkeletonSummonPatternSO data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        _phase      = Phase.Cast;
        _timer      = 0f;
        _orbitTimer = 0f;
        _orbitStep  = 0;
        _boltFired  = false;
        _boltLanded = false;
        _spawnPoints.Clear();
        _spawned.Clear();
        _aliveCount = 0;

        ctx.Animator?.CrossFade("SkeletonSummon", 0.1f);

        var     mc     = LichPatternUtil.Mover(ctx);
        Vector3 center = mc != null ? mc.ArenaCenter : ctx.Transform.position;
        float   radius = mc != null ? mc.ArenaRadius : 12f;
        if (mc != null)
        {
            mc.SetLocked(true);
            mc.SetAltitudeOffset(Data.hoverHeight);
            mc.ScriptMove(center, Data.castDuration, 2f, facePlayer: true);
        }

        // 가장자리 네 방향 — 플레이어 쪽을 기준으로 돌려 한 방향이 늘 플레이어 가까이 오게.
        Vector3 toPlayer = LichPatternUtil.PlayerFloorPos(ctx) - center;
        toPlayer.y = 0f;
        float baseAngle = toPlayer.sqrMagnitude > 0.01f ? Mathf.Atan2(toPlayer.x, toPlayer.z) * Mathf.Rad2Deg : 0f;
        for (int s = 0; s < Sides; s++)
        {
            Quaternion rot  = Quaternion.Euler(0f, baseAngle + s * 90f, 0f);
            Vector3    edge = LichPatternUtil.OnFloor(ctx, center + rot * Vector3.forward * Mathf.Max(2f, radius - Data.edgeInset));
            Vector3    side = rot * Vector3.right;
            for (int k = 0; k < Data.perSide; k++)
                _spawnPoints.Add(edge + side * ((k - (Data.perSide - 1) * 0.5f) * Data.sideSpacing));

            _marks.Add(LichPatternUtil.PrepareTelegraph(
                PatternGuideHelper.Disc(edge, Data.markRadius, PatternGuideHelper.Summon), PatternGuideHelper.Summon));
            _markVfx.Add(LichVfx.PlayLoop(LichVfxSlot.SummonGround, edge, rot, Data.markRadius / 2.2f));
            ArenaTileGrid.Active?.Tremble(edge, Data.markRadius + 3f, Data.castDuration);   // 난간이 흔들린다 — 넘어온다
        }

        UI_BossBark.ShowDialogue(LegionBarkKey);
        LichSfx.Play(LichSfxSlot.DarkOrb, ctx.Transform.position);
        LichPatternUtil.Lich(ctx)?.PulseBook(Data.castDuration);
    }

    public override void Update(MonsterContext ctx)
    {
        float dt = Time.deltaTime;
        _timer += dt;

        switch (_phase)
        {
            case Phase.Cast:
                for (int i = 0; i < _marks.Count; i++)
                    PatternGuideHelper.SetProgress(_marks[i], _timer / Mathf.Max(0.01f, Data.castDuration));
                if (_timer >= Data.castDuration)
                {
                    SpawnLegion(ctx);
                    ClearMarks(0.8f);
                    _phase = Phase.Legion;
                    _timer = 0f;
                }
                break;

            case Phase.Legion:
                Orbit(ctx, dt);
                TickBolt(ctx);
                if (_timer >= Data.legionDuration || (_timer > 1f && _aliveCount <= 0))
                {
                    LichPatternUtil.Mover(ctx)?.SetAltitudeOffset(0f);
                    _phase = Phase.Recovery;
                    _timer = 0f;
                }
                break;

            case Phase.Recovery:
                if (_timer >= Data.recoveryDuration)
                    ctx.Monster.ChangeState<ChaseState>();
                break;
        }
    }

    public override void Exit(MonsterContext ctx)
    {
        ClearMarks(0f);
        PatternGuideHelper.SafeDestroy(ref _boltDisc);
        LichVfx.Stop(ref _boltVfx);
        // 해골은 패턴보다 오래 산다(수명·동시 상한은 LichSkeletonMonster가 관리) — 구독만 푼다.
        for (int i = 0; i < _spawned.Count; i++)
            if (_spawned[i] != null) _spawned[i].OnDied -= OnSkeletonDied;
        _spawned.Clear();

        var lich = LichPatternUtil.Lich(ctx);
        var mc   = lich?.MovementController;
        mc?.SetAltitudeOffset(0f);
        mc?.SetLocked(false);
        if (lich?.LichBB != null)
            lich.LichBB.SkeletonSummonCooldown = Data.patternCooldown;
    }

    // ── 소환 ────────────────────────────────────────────

    private void SpawnLegion(MonsterContext ctx)
    {
        if (Data.skeletonPrefab == null) return;
        for (int i = 0; i < _spawnPoints.Count; i++)
        {
            Vector3 p  = _spawnPoints[i];
            Vector3 to = LichPatternUtil.PlayerFloorPos(ctx) - p;
            to.y = 0f;
            var go = Object.Instantiate(Data.skeletonPrefab, p,
                                        to.sqrMagnitude > 0.01f ? Quaternion.LookRotation(to) : Quaternion.identity);
            if (go.TryGetComponent<MonsterBase>(out var m))
            {
                m.OnDied += OnSkeletonDied;
                _spawned.Add(m);
                _aliveCount++;
            }
            LichVfx.Play(LichVfxSlot.DarkOrbImpact, p, Quaternion.identity, 0.8f);
        }
        if (_spawnPoints.Count > 0) LichSfx.Play(LichSfxSlot.Collapse, _spawnPoints[0], 0.6f);
    }

    // 막 생성된 해골은 초기화가 끝나기 전 HP가 0이다 — HP가 아니라 사망 이벤트로 센다.
    private void OnSkeletonDied(MonsterBase m)
    {
        m.OnDied -= OnSkeletonDied;
        _aliveCount--;
    }

    private void ClearMarks(float fade)
    {
        for (int i = 0; i < _marks.Count; i++)
        {
            var d = _marks[i];
            PatternGuideHelper.SafeDestroy(ref d);
        }
        _marks.Clear();
        for (int i = 0; i < _markVfx.Count; i++)
        {
            var v = _markVfx[i];
            LichVfx.Stop(ref v, fade);
        }
        _markVfx.Clear();
    }

    // ── 리치: 중앙 상공 선회 + 약한 마력탄 한 발 ──────────

    private void Orbit(MonsterContext ctx, float dt)
    {
        var mc = LichPatternUtil.Mover(ctx);
        if (mc == null || mc.IsScriptMoving) return;

        _orbitTimer += dt;
        if (_orbitTimer < 0.05f) return;
        _orbitTimer = 0f;

        _orbitStep++;
        float   a    = _orbitStep * 90f * Mathf.Deg2Rad;
        Vector3 dest = mc.ArenaCenter + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * Data.orbitRadius;
        mc.ScriptMove(dest, Data.orbitLeg, Data.orbitRadius * 0.4f, facePlayer: true);
    }

    private void TickBolt(MonsterContext ctx)
    {
        if (!_boltFired && _timer >= Data.boltAt)
        {
            _boltFired  = true;
            _boltTarget = LichPatternUtil.PlayerFloorPos(ctx);
            _boltDisc   = LichPatternUtil.PrepareTelegraph(
                PatternGuideHelper.Disc(_boltTarget, Data.boltRadius, LichPatternUtil.Arcane), LichPatternUtil.Arcane);
            var       lich = LichPatternUtil.Lich(ctx);
            Transform hand = lich != null ? lich.CastPoint : ctx.Transform;
            _boltFrom = hand.position;
            _boltVfx  = LichVfx.PlayLoop(LichVfxSlot.BoltProjectile, _boltFrom,
                                         Quaternion.LookRotation(_boltTarget - _boltFrom));
            ctx.Animator?.CrossFade("MagicBolt", 0.1f);
            LichSfx.Play(LichSfxSlot.BoltFire, _boltFrom);
        }

        if (!_boltFired || _boltLanded) return;

        float t = Mathf.Clamp01((_timer - Data.boltAt) / Mathf.Max(0.05f, Data.boltWarn));
        PatternGuideHelper.SetProgress(_boltDisc, t);
        if (_boltVfx != null)
            _boltVfx.transform.position = Vector3.Lerp(_boltFrom, _boltTarget, t) + Vector3.up * (4f * t * (1f - t));
        if (t < 1f) return;

        _boltLanded = true;
        LichVfx.Stop(ref _boltVfx);
        PatternGuideHelper.SetColor(_boltDisc, LichPatternUtil.Lethal);
        if (_boltDisc != null) Object.Destroy(_boltDisc, 0.12f);
        _boltDisc = null;
        LichVfx.Play(LichVfxSlot.BoltImpact, _boltTarget, Quaternion.identity, Data.boltRadius / 2f);
        LichSfx.Play(LichSfxSlot.BoltImpact, _boltTarget);
        LichPatternUtil.Impact(LichImpact.Light, LichPatternUtil.HitCircle(ctx, _boltTarget, Data.boltRadius, Data.boltDamage));
    }
}
}

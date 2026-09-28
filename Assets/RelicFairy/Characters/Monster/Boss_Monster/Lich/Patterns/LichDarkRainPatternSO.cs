using System.Collections.Generic;
using RelicFairy.UI;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 어둠의 비 — 해방판 R5 / 봉인판 C5 「옅은 어둠의 비」. 리치 설계서 §3-4 · §4-4 · §13.
/// 같은 클래스, 에셋 두 개(값만 다르다: 낙하 밀도 · 안전원 크기).
///
/// O 상승(chargeDuration) — 리치가 떠오르고 제단 상공에 어둠 구름. 흰 안전원 safeCount개가 뜬다
/// → A 비(rainDuration) — 초당 dropsPerSecond발이 제단 전역에 떨어진다. 낙하점은 dropWarn초 동안 차오른다.
///   안전원 안에는 떨어지지 않고, safeMoveInterval마다 안전원 safeMoveCount개가 다른 자리로 옮겨간다(서 있는 패턴이 아니다).
///   옮겨갈 자리는 safeMovePreview초 먼저 옅게 뜨고, 그동안 옛 자리와 새 자리 모두 안전하다
/// → E 반격창(endDuration) → R 복귀(원래 고도)
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/Boss/Lich/Lich_DarkRainPattern", fileName = "Lich_DarkRainPattern")]
public class LichDarkRainPatternSO : BossPatternSO
{
    [Header("어둠의 비 — 발동")]
    public float patternCooldown = 25f;

    [Header("어둠의 비 — 타이밍 (초)")]
    public float chargeDuration   = 1.5f;
    public float rainDuration     = 5f;
    public float endDuration      = 0.5f;
    public float recoveryDuration = 0.4f;

    [Header("어둠의 비 — 낙하")]
    public float dropsPerSecond   = 12f;
    public float dropWarn         = 0.8f;
    public float dropRadius       = 1.5f;
    public float damageMultiplier = 0.8f;
    [Tooltip("낙하점을 플레이어 둘레에 몰아주는 비율 (0 = 제단 전역 균등)")]
    [Range(0f, 1f)]
    public float nearPlayerBias   = 0.35f;
    public float nearPlayerRadius = 8f;

    [Header("어둠의 비 — 안전원")]
    public int   safeCount        = 4;
    public float safeRadius       = 3f;
    public float safeMoveInterval = 1.8f;
    public int   safeMoveCount    = 2;
    [Tooltip("옮겨갈 안전원이 먼저 옅게 떠 있는 시간 — 그동안 옛 자리도 안전")]
    public float safeMovePreview  = 0.6f;

    [Header("어둠의 비 — 연출")]
    public float riseHeight  = 5f;
    public float cloudHeight = 12f;

    // ── 런타임 ───────────────────────────────────────────
    private LichDarkRainState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new LichDarkRainState(this);
    public override void OnRecycled()                       => _state = new LichDarkRainState(this);

    public override bool CanExecute(BossPatternContext ctx)
    {
        if (ctx.Ctx.Runtime.PlayerTarget == null) return false;
        var lichBB = (ctx.Boss as LichMonster)?.LichBB;
        return lichBB == null || lichBB.DarkRainCooldown <= 0f;
    }

    public override SpecialStateBase GetRuntimeState() => _state;
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// LichDarkRainState — 중단 불가(피해는 받음)
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class LichDarkRainState : UnInterruptibleState<LichDarkRainPatternSO>
{
    private enum Phase { Charge, Rain, End, Recovery }

    private const string BarkKey        = "Lich_DarkRain";
    private const float  FallSeconds    = 0.35f;
    private const float  FallHeight     = 12f;
    private const int    MaxSpawnTries  = 8;
    private const float  SignalSeconds  = 0.15f;
    private const float  ImpactSoundGap = 0.2f;   // 착탄 소리를 이 간격으로 묶는다
    private const float  PreviewGlow    = 0.45f;

    private struct Drop
    {
        public Vector3    Pos;
        public float      Timer;
        public GameObject Disc;
        public GameObject Fall;
        public bool       Signaled;
    }

    /// <summary>안전원 하나 — 옮겨가는 동안은 옛 자리(Pos)와 새 자리(NextPos) 둘 다 안전.</summary>
    private struct SafeSpot
    {
        public Vector3    Pos;
        public GameObject Disc;
        public Vector3    NextPos;
        public GameObject NextDisc;
        public float      Preview;   // > 0이면 옮겨가는 중
    }

    private readonly List<Drop>     _drops = new(32);
    private readonly List<SafeSpot> _safe  = new(6);

    private Phase      _phase;
    private float      _timer;
    private float      _spawnAcc;
    private float      _moveTimer;
    private Vector3    _center;
    private float      _radius;
    private GameObject  _cloud;
    private AudioSource _rainLoop;
    private float       _lastImpactSound;

    public LichDarkRainState(LichDarkRainPatternSO data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        _phase     = Phase.Charge;
        _timer     = 0f;
        _spawnAcc  = 0f;
        _moveTimer = 0f;
        _drops.Clear();
        _safe.Clear();
        _lastImpactSound = -1f;

        var mc = LichPatternUtil.Mover(ctx);
        _center = LichPatternUtil.OnFloor(ctx, mc != null ? mc.ArenaCenter : ctx.Transform.position);
        _radius = mc != null ? mc.ArenaRadius : 14f;
        if (mc != null)
        {
            mc.SetLocked(true);
            mc.SetAltitudeOffset(Data.riseHeight);
            mc.RequestMovementState(LichMovementState.AltitudeDescend);   // 수평은 멈춘다
        }

        ctx.Animator?.CrossFade("ArcaneOrb", 0.1f);
        UI_BossBark.ShowDialogue(BarkKey, BossBarkType.PatternAnnounce);
        _cloud = LichVfx.PlayLoop(LichVfxSlot.DarkCloud, _center + Vector3.up * Data.cloudHeight, Quaternion.identity, _radius / 12f);
        LichSfx.Play(LichSfxSlot.RainStart, _center);
        LichPatternUtil.Lich(ctx)?.PulseBook(Data.chargeDuration);

        for (int i = 0; i < Data.safeCount; i++)
        {
            var p = RandomSafeSpot(ctx);
            _safe.Add(new SafeSpot { Pos = p, Disc = SafeDisc(p, 1f) });
        }
    }

    public override void Update(MonsterContext ctx)
    {
        float dt = Time.deltaTime;
        _timer += dt;
        TickDrops(ctx, dt);

        switch (_phase)
        {
            case Phase.Charge:
                if (_timer >= Data.chargeDuration)
                {
                    _rainLoop = LichSfx.PlayLoop(LichSfxSlot.RainLoop, _center);
                    Next(Phase.Rain);
                }
                break;

            case Phase.Rain:
                if (_timer < Data.rainDuration)
                {
                    _spawnAcc += Data.dropsPerSecond * dt;
                    while (_spawnAcc >= 1f)
                    {
                        _spawnAcc -= 1f;
                        SpawnDrop(ctx);
                    }

                    _moveTimer += dt;
                    if (_moveTimer >= Data.safeMoveInterval)
                    {
                        _moveTimer = 0f;
                        MoveSafeCircles(ctx);
                    }
                    TickSafe(dt);
                }
                else if (_drops.Count == 0)   // 비는 그쳤다 — 떨어지는 중인 것까지 끝나면
                {
                    ClearSafe();
                    LichVfx.Stop(ref _cloud, 1f);
                    LichSfx.StopLoop(ref _rainLoop);
                    LichPatternUtil.Lich(ctx)?.NotifyVulnerableWindow(Data.endDuration);
                    Next(Phase.End);
                }
                break;

            case Phase.End:
                if (_timer >= Data.endDuration)
                {
                    LichPatternUtil.Mover(ctx)?.SetAltitudeOffset(0f);
                    Next(Phase.Recovery);
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
        for (int i = 0; i < _drops.Count; i++)
        {
            var d = _drops[i];
            PatternGuideHelper.SafeDestroy(ref d.Disc);
            LichVfx.Stop(ref d.Fall);
        }
        _drops.Clear();
        ClearSafe();
        LichVfx.Stop(ref _cloud, 0.5f);
        LichSfx.StopLoop(ref _rainLoop);

        var lich = LichPatternUtil.Lich(ctx);
        var mc   = lich?.MovementController;
        mc?.SetAltitudeOffset(0f);
        mc?.SetLocked(false);
        if (lich?.LichBB != null)
            lich.LichBB.DarkRainCooldown = Data.patternCooldown;
    }

    private void Next(Phase phase)
    {
        _phase = phase;
        _timer = 0f;
    }

    // ── 낙하 ────────────────────────────────────────────

    private void SpawnDrop(MonsterContext ctx)
    {
        for (int tries = 0; tries < MaxSpawnTries; tries++)
        {
            Vector3 p;
            if (Random.value < Data.nearPlayerBias)
            {
                Vector2 r = Random.insideUnitCircle * Data.nearPlayerRadius;
                p = LichPatternUtil.PlayerFloorPos(ctx) + new Vector3(r.x, 0f, r.y);
            }
            else
            {
                Vector2 r = Random.insideUnitCircle * _radius;
                p = _center + new Vector3(r.x, 0f, r.y);
            }
            if (LichPatternUtil.FlatDistance(p, _center) > _radius || InSafe(p, Data.dropRadius)) continue;
            if (NearPendingDrop(p)) continue;   // 같은 자리 연타 방지 — 떨어질 낙하와 겹치지 않게

            p = LichPatternUtil.OnFloor(ctx, p);
            _drops.Add(new Drop
            {
                Pos  = p,
                Disc = LichPatternUtil.PrepareTelegraph(
                    PatternGuideHelper.Disc(p, Data.dropRadius, LichPatternUtil.Arcane), LichPatternUtil.Arcane),
            });
            return;
        }
    }

    private void TickDrops(MonsterContext ctx, float dt)
    {
        for (int i = _drops.Count - 1; i >= 0; i--)
        {
            var d = _drops[i];
            d.Timer += dt;
            LichPatternUtil.TickTelegraph(d.Disc, d.Timer, Data.dropWarn, SignalSeconds, ref d.Signaled);

            // 착탄 FallSeconds 전에 하늘에서 떨어지기 시작한다.
            float fallStart = Data.dropWarn - FallSeconds;
            if (d.Timer >= fallStart)
            {
                float t = Mathf.Clamp01((d.Timer - fallStart) / FallSeconds);
                Vector3 pos = d.Pos + Vector3.up * (FallHeight * (1f - t));
                if (d.Fall == null)
                    d.Fall = LichVfx.PlayLoop(LichVfxSlot.DarkRainDrop, pos, Quaternion.LookRotation(Vector3.down), 0.6f);
                else
                    d.Fall.transform.position = pos;
            }

            if (d.Timer >= Data.dropWarn)
            {
                LichVfx.Stop(ref d.Fall);
                PatternGuideHelper.SafeDestroy(ref d.Disc);
                LichVfx.Play(LichVfxSlot.DarkRainImpact, d.Pos + Vector3.up * 0.1f, Quaternion.identity, Data.dropRadius / 1.5f);
                if (Time.time - _lastImpactSound >= ImpactSoundGap)
                {
                    _lastImpactSound = Time.time;
                    LichSfx.Play(LichSfxSlot.BoltImpact, d.Pos, 0.35f);
                }
                // 안전원은 늘 믿을 수 있어야 한다 — 낙하 중심이 안전원 안이거나(그 사이 옮겨 옴)
                // 플레이어가 안전원 안에 서 있으면 맞히지 않는다(안전원 가장자리 1.5 m 띠에서 맞던 것, 09-18 감사).
                if (!InSafe(d.Pos, 0f) && !InSafe(LichPatternUtil.PlayerFloorPos(ctx), 0f))
                    LichPatternUtil.Impact(LichImpact.Light,
                                           LichPatternUtil.HitCircle(ctx, d.Pos, Data.dropRadius, Data.damageMultiplier));
                _drops.RemoveAt(i);
                continue;
            }
            _drops[i] = d;
        }
    }

    // ── 안전원 ──────────────────────────────────────────

    /// <summary>옮겨갈 안전원을 먼저 옅게 띄운다 — safeMovePreview 뒤에 옛 자리가 사라진다.</summary>
    private void MoveSafeCircles(MonsterContext ctx)
    {
        for (int k = 0; k < Data.safeMoveCount && _safe.Count > 0; k++)
        {
            int i = Random.Range(0, _safe.Count);
            var s = _safe[i];
            if (s.Preview > 0f) continue;   // 이미 옮겨가는 중

            s.NextPos  = RandomSafeSpot(ctx);
            s.NextDisc = SafeDisc(s.NextPos, PreviewGlow);
            PatternGuideHelper.SetProgress(s.NextDisc, 0f);
            s.Preview  = Mathf.Max(0.01f, Data.safeMovePreview);
            _safe[i]   = s;
        }
    }

    private void TickSafe(float dt)
    {
        for (int i = 0; i < _safe.Count; i++)
        {
            var s = _safe[i];
            if (s.Preview <= 0f) continue;

            s.Preview -= dt;
            PatternGuideHelper.SetProgress(s.NextDisc, 1f - s.Preview / Mathf.Max(0.01f, Data.safeMovePreview));
            if (s.Preview <= 0f)
            {
                PatternGuideHelper.SafeDestroy(ref s.Disc);
                s.Pos      = s.NextPos;
                s.Disc     = s.NextDisc;
                s.NextDisc = null;
                s.Preview  = 0f;
                PatternGuideHelper.SetIntensity(s.Disc, 1f);
            }
            _safe[i] = s;
        }
    }

    private GameObject SafeDisc(Vector3 p, float glow)
    {
        var disc = LichPatternUtil.PrepareTelegraph(
            PatternGuideHelper.Disc(p, Data.safeRadius, LichPatternUtil.SafeWhite), LichPatternUtil.SafeWhite);
        PatternGuideHelper.SetProgress(disc, 1f);
        PatternGuideHelper.SetIntensity(disc, glow);
        return disc;
    }

    private Vector3 RandomSafeSpot(MonsterContext ctx)
    {
        Vector2 r = Random.insideUnitCircle * Mathf.Max(0f, _radius - Data.safeRadius - 1f);
        return LichPatternUtil.OnFloor(ctx, _center + new Vector3(r.x, 0f, r.y));
    }

    private bool NearPendingDrop(Vector3 p)
    {
        for (int i = 0; i < _drops.Count; i++)
            if (LichPatternUtil.FlatDistance(p, _drops[i].Pos) < Data.dropRadius) return true;
        return false;
    }

    private bool InSafe(Vector3 p, float margin)
    {
        float r = Data.safeRadius + margin;
        for (int i = 0; i < _safe.Count; i++)
        {
            var s = _safe[i];
            if (LichPatternUtil.FlatDistance(p, s.Pos) <= r) return true;
            if (s.Preview > 0f && LichPatternUtil.FlatDistance(p, s.NextPos) <= r) return true;
        }
        return false;
    }

    private void ClearSafe()
    {
        for (int i = 0; i < _safe.Count; i++)
        {
            var s = _safe[i];
            PatternGuideHelper.SafeDestroy(ref s.Disc);
            PatternGuideHelper.SafeDestroy(ref s.NextDisc);
        }
        _safe.Clear();
    }
}
}

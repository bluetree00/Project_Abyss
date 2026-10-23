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
/// 10-22 E5 페이지2 — 넓은 안전원 넷 + 성긴 비라 서 있기만 해도 됐다(실측 0/3): 안전원은 둘 · 좁게 · 플레이어 몇 걸음 곁에 뜨고,
///   플레이어 둘레에는 packInterval마다 낙하점을 빽빽이 깐다(첫 점 = 플레이어 자리 · 틈 1.0~2.4 m 무작위). 흩뿌림 비는 그 밖에만.
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

    [Header("어둠의 비 — 비 동안 낫 투척 (10-22 E2 L1 동시 연계)")]
    [Tooltip("비가 내린 지 이만큼(초) 뒤 그때 플레이어 자리로 낫을 한 번 던진다(곧게 왕복 · 한 번만 맞는다). 음수면 없음")]
    [SerializeField] private float _throwAt = -1f;
    [Tooltip("던지는 낫의 값(조준 · 신호 · 비행 시간 · 판정)을 빌릴 낫 투척 에셋. 피해는 사슬 둘째 칸이라 ×0.8")]
    [SerializeField] private LichScytheThrowPatternSO _throwSource;

    [Header("어둠의 비 — 촘촘한 비 (10-22 E5 페이지2)")]
    [Tooltip("이 간격(초)마다 플레이어 둘레(nearPlayerRadius)에 낙하점을 빽빽이 깐다 — 첫 점은 플레이어 자리(안전원 곁이면 없음). 0 이하면 옛 방식(nearPlayerBias)")]
    [SerializeField] private float   _packInterval = 1.0f;
    [Tooltip("낙하 원 가장자리 사이 빈틈 (m, 최소 · 최대) — 점마다 무작위라 좁고 매번 다르다")]
    [SerializeField] private Vector2 _packGap      = new Vector2(1.0f, 2.4f);
    [Tooltip("한 차례에 까는 낙하점 최대 수")]
    [SerializeField] private int     _packMax      = 12;
    [Tooltip("안전원이 뜨는 자리 — 플레이어에서 이 거리(m) 사이. y ≤ 0이면 옛 방식(제단 어디든)")]
    [SerializeField] private Vector2 _safeNear     = new Vector2(3f, 6f);

    // ── 런타임 ───────────────────────────────────────────
    private LichDarkRainState _state;

    public float                    ThrowAt     => _throwAt;
    public LichScytheThrowPatternSO ThrowSource => _throwSource;
    public float                    PackInterval => _packInterval;
    public Vector2                  PackGap      => _packGap;
    public int                      PackMax      => Mathf.Max(1, _packMax);
    public Vector2                  SafeNear     => _safeNear;

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

    // 비 동안 낫 투척(10-22 E2 L1) — 안전원으로 몰린 자리를 노린다
    private float      _throwT = -1f;   // 투척 진행(초) — 음수면 없음
    private bool       _throwStarted;
    private bool       _throwHit;
    private bool       _throwSignaled;
    private Vector3    _throwFrom;
    private Vector3    _throwDir;
    private float      _throwLen;
    private GameObject _throwGuide;
    private GameObject _throwSpin;

    // 촘촘한 비(10-22 E5 페이지2) — 플레이어 둘레에 낙하점을 빽빽이 깐다
    private readonly List<Vector3>             _pack = new(16);
    private readonly System.Predicate<Vector3> _packBlocked;
    private float                              _packTimer;

    public LichDarkRainState(LichDarkRainPatternSO data) : base(data) => _packBlocked = PackBlocked;

    public override void Enter(MonsterContext ctx)
    {
        _phase     = Phase.Charge;
        _timer     = 0f;
        _spawnAcc  = 0f;
        _moveTimer = 0f;
        _drops.Clear();
        _safe.Clear();
        _lastImpactSound = -1f;
        _throwT          = -1f;
        _throwStarted    = false;
        _packTimer       = Data.PackInterval;   // 비가 시작되는 순간 첫 차례(10-22 E5 페이지2)

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
        TickThrow(ctx, dt);

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
                    if (Data.PackInterval > 0f)
                    {
                        _packTimer += dt;
                        if (_packTimer >= Data.PackInterval)
                        {
                            _packTimer -= Data.PackInterval;
                            PackDrops(ctx);
                        }
                    }

                    _moveTimer += dt;
                    if (_moveTimer >= Data.safeMoveInterval)
                    {
                        _moveTimer = 0f;
                        MoveSafeCircles(ctx);
                    }
                    TickSafe(dt);
                    if (!_throwStarted && Data.ThrowSource != null && Data.ThrowAt >= 0f && _timer >= Data.ThrowAt)
                        BeginThrow(ctx);
                }
                else if (_drops.Count == 0 && _throwT < 0f)   // 비는 그쳤다 — 떨어지는 중인 것 · 던진 낫까지 끝나면
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
                if (_timer >= LichPatternUtil.Recovery(ctx, Data.recoveryDuration))
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
        EndThrow(ctx);
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

    // ── 비 동안 낫 투척(10-22 E2 L1 동시 연계) ──────────

    /// <summary>
    /// 그때 플레이어 자리로 낫을 곧게 던진다 — 줄(폭 = 판정 폭)이 조준 + 신호 동안 차오르고(방향은 뜰 때 굳는다),
    /// 낫이 줄 끝까지 갔다 돌아온다. 안전원 안이어도 맞는다(안전원은 비만 막는다) · 한 번만 맞는다.
    /// </summary>
    private void BeginThrow(MonsterContext ctx)
    {
        var src = Data.ThrowSource;
        _throwStarted  = true;
        _throwT        = 0f;
        _throwHit      = false;
        _throwSignaled = false;
        _throwFrom     = LichPatternUtil.OnFloor(ctx, ctx.Transform.position);
        Vector3 dir = LichPatternUtil.PlayerFloorPos(ctx) - _throwFrom;
        dir.y = 0f;
        _throwDir = dir.sqrMagnitude > 0.001f ? dir.normalized : ctx.Transform.forward;
        _throwLen = Mathf.Max(2f, Mathf.Min(src.maxDistance, LichPatternUtil.DistanceToArenaEdge(ctx, _throwFrom, _throwDir)));

        // 곧은 왕복 — 발밑 뒤(판정 반경)부터 끝(+판정 반경)까지(낫 투척 봉인판과 같은 줄)
        _throwGuide = LichPatternUtil.PrepareTelegraph(
            PatternGuideHelper.Beam(_throwFrom - _throwDir * src.hitRadius, _throwDir, _throwLen + src.hitRadius * 2f,
                                    src.hitRadius * 2f, LichPatternUtil.Crimson),
            LichPatternUtil.Crimson);
        LichPatternUtil.FaceToward(ctx, _throwFrom + _throwDir);
        LichPatternUtil.Swing(ctx, LichSwing.RightToLeft, src.castDuration + src.signalDuration, 0.04f);
    }

    private void TickThrow(MonsterContext ctx, float dt)
    {
        if (_throwT < 0f) return;
        var src = Data.ThrowSource;
        _throwT += dt;
        float aim = src.castDuration + src.signalDuration;
        if (_throwT < aim)
        {
            LichPatternUtil.TickTelegraph(_throwGuide, _throwT, aim, src.signalDuration, ref _throwSignaled);
            return;
        }

        float fly      = _throwT - aim;
        bool  outbound = fly < src.outSeconds;
        float k;
        if (outbound)
            k = fly / Mathf.Max(0.01f, src.outSeconds);
        else if (fly < src.outSeconds + src.returnSeconds)
            k = 1f - (fly - src.outSeconds) / Mathf.Max(0.01f, src.returnSeconds);
        else
        {
            EndThrow(ctx);
            return;
        }

        Vector3    floorPos = _throwFrom + _throwDir * (_throwLen * k);
        Vector3    pos      = floorPos + Vector3.up * src.spinHeight;
        Quaternion rot      = Quaternion.LookRotation(Vector3.up, outbound ? _throwDir : -_throwDir);   // 도는 원반을 눕힌다(낫 투척과 같게)
        if (_throwSpin == null)
        {
            LichPatternUtil.Lich(ctx)?.SetScytheVisible(false);
            _throwSpin = LichVfx.PlayLoop(LichVfxSlot.ScytheSpin, pos, rot);
            LichSfx.Play(LichSfxSlot.ScytheThrow, _throwFrom);
        }
        else
        {
            _throwSpin.transform.SetPositionAndRotation(pos, rot);
        }

        if (_throwHit) return;
        bool damaged = LichPatternUtil.HitCircle(ctx, floorPos, src.hitRadius, src.damageMultiplier * LichPatternUtil.ChainLinkDamage,
                                                 out bool contact, src.knockbackMultiplier);
        if (contact) _throwHit = true;   // 흘렸어도(무효화 · 저스트 회피) 이 낫은 쓰였다 — 매 프레임 다시 굴리지 않게
        if (damaged) LichPatternUtil.Impact(LichImpact.Slash, true);
    }

    /// <summary>낫을 받아 든다(또는 패턴이 끝났다) — 줄 · 원반을 거두고 낫을 다시 보인다.</summary>
    private void EndThrow(MonsterContext ctx)
    {
        if (_throwT < 0f) return;
        _throwT = -1f;
        PatternGuideHelper.SafeDestroy(ref _throwGuide);
        if (_throwSpin != null)
        {
            LichVfx.Stop(ref _throwSpin, 0.2f);
            LichSfx.Play(LichSfxSlot.ScytheCatch, ctx.Transform.position);
        }
        LichPatternUtil.Lich(ctx)?.SetScytheVisible(true);
    }

    // ── 낙하 ────────────────────────────────────────────

    private void SpawnDrop(MonsterContext ctx)
    {
        for (int tries = 0; tries < MaxSpawnTries; tries++)
        {
            Vector3 p;
            if (Data.PackInterval <= 0f && Random.value < Data.nearPlayerBias)   // 촘촘한 비를 켜면 둘레는 PackDrops가 맡는다(10-22 E5 페이지2)
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
            if (Data.PackInterval > 0f
                && LichPatternUtil.FlatDistance(p, LichPatternUtil.PlayerFloorPos(ctx)) < Data.nearPlayerRadius + Data.dropRadius)
                continue;   // 둘레는 촘촘한 비만 — 틈이 메워지지 않게(10-22 E5 페이지2)

            AddDrop(ctx, p);
            return;
        }
    }

    private void AddDrop(MonsterContext ctx, Vector3 p)
    {
        p = LichPatternUtil.OnFloor(ctx, p);
        _drops.Add(new Drop
        {
            Pos  = p,
            Disc = LichPatternUtil.PrepareTelegraph(
                PatternGuideHelper.Disc(p, Data.dropRadius, LichPatternUtil.Arcane), LichPatternUtil.Arcane),
        });
    }

    /// <summary>
    /// 촘촘한 비 한 차례(10-22 E5 페이지2) — 플레이어 둘레 nearPlayerRadius 안에 낙하점을 빽빽이 깐다(첫 점 = 플레이어 자리).
    /// 가장자리 사이 빈틈은 점마다 무작위(좁고 매번 다르다) · 안전원과 떨어지는 중인 낙하는 비킨다 — 원 사이 틈이나 안전원으로 비킨다.
    /// </summary>
    private void PackDrops(MonsterContext ctx)
    {
        Vector3 player = LichPatternUtil.PlayerFloorPos(ctx);
        _pack.Clear();
        for (int i = 0; i < _drops.Count; i++) _pack.Add(_drops[i].Pos);
        int first = _pack.Count;
        LichPatternUtil.PackDiscs(_pack, player, Data.nearPlayerRadius, Data.dropRadius, Data.PackGap, Data.PackMax, player, _packBlocked);
        for (int i = first; i < _pack.Count; i++) AddDrop(ctx, _pack[i]);
    }

    /// <summary>낙하점을 두지 않는 자리 — 제단 밖 · 안전원(옮겨 갈 자리 포함)에 닿는 자리.</summary>
    private bool PackBlocked(Vector3 p)
        => LichPatternUtil.FlatDistance(p, _center) > _radius || InSafe(p, Data.dropRadius);

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
        float room = Mathf.Max(0f, _radius - Data.safeRadius - 1f);
        if (Data.SafeNear.y > 0f)   // 플레이어 몇 걸음 곁(10-22 E5 페이지2) — 제단 건너편에 뜨면 쫓아갈 수 없고, 넓게 뜨면 서 있기만 해도 됐다
        {
            Vector3 player = LichPatternUtil.PlayerFloorPos(ctx);
            for (int tries = 0; tries < MaxSpawnTries; tries++)
            {
                Vector3 p = player + Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward
                                     * Random.Range(Data.SafeNear.x, Data.SafeNear.y);
                if (LichPatternUtil.FlatDistance(p, _center) <= room) return LichPatternUtil.OnFloor(ctx, p);
            }
        }
        Vector2 r = Random.insideUnitCircle * room;
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

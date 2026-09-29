using System.Collections.Generic;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 낫 투척 — 해방판 R2 「부메랑 낫」 / 봉인판 C2 「사슬 낫 투척」. 리치 설계서 §3-4 · §4-4 · §13.
/// 같은 클래스, 에셋 두 개(값만 다르다).
///
/// O 조준(castDuration) — 낫을 뒤로 당기고, 날아갈 줄(폭 = 판정 폭)이 바닥에 뜬다
/// → S 신호(signalDuration) — 줄이 빨갛게
/// → A 비행 — 해방판: 제단 끝까지 옆으로 휘어 날아갔다가 반대편으로 휘어 돌아온다(지나간 자리에 균열)
///            봉인판: 사슬 길이(maxDistance)만큼 곧게 나갔다 곧게 돌아온다(사슬이 손과 낫을 잇는다)
///   나갈 때 한 번, 돌아올 때 한 번 맞힐 수 있다
/// → E 반격창(endDuration) — 낫을 받아 든 직후 → R 복귀
///
/// 사슬 결박(§12-5): 사슬 낫에 맞으면 bindSeconds 동안 묶이고, 묶인 자리에 폭발 예고가 바로 차오른다 —
/// 풀린 뒤 bindFollowAfter초에 터진다(돌아오는 낫은 묶인 사람을 다시 치지 않는다).
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/Boss/Lich/Lich_ScytheThrowPattern", fileName = "Lich_ScytheThrowPattern")]
public class LichScytheThrowPatternSO : BossPatternSO
{
    [Header("낫 투척 — 발동")]
    public float maxRange        = 28f;
    public float patternCooldown = 8f;

    [Header("낫 투척 — 타이밍 (초)")]
    public float castDuration     = 0.7f;
    public float signalDuration   = 0.15f;
    public float outSeconds       = 1.0f;
    public float returnSeconds    = 1.0f;
    public float endDuration      = 0.5f;
    public float recoveryDuration = 0.4f;

    [Header("낫 투척 — 궤적")]
    [Tooltip("날아가는 최대 거리 (m) — 제단 끝을 넘지 않는다. 봉인판 = 사슬 길이")]
    public float maxDistance = 30f;
    [Tooltip("옆으로 휘는 정도 (m). 0이면 곧게 왕복")]
    public float curve       = 6f;
    public float spinHeight  = 1.2f;
    [Tooltip("이 거리마다 지나간 자리에 균열 (m). 0이면 없음")]
    public float crackSpacing = 3f;
    [Tooltip("봉인판: 손과 낫을 잇는 금빛 사슬")]
    public bool  chainTether  = false;

    [Header("낫 투척 — 판정")]
    public float hitRadius           = 1.5f;
    public float damageMultiplier    = 1.3f;
    public float knockbackMultiplier = 1.0f;

    [Header("낫 투척 — 사슬 결박 (연출·UX 시나리오 §12-5)")]
    [Tooltip("맞으면 묶이는 시간 (초). 0이면 결박 없음")]
    public float bindSeconds      = 0f;
    public float bindFollowRadius = 2.6f;
    [Tooltip("풀린 뒤 이만큼 지나 폭발 (초)")]
    public float bindFollowAfter  = 0.5f;
    public float bindFollowDamage = 0.9f;

    // ── 런타임 ───────────────────────────────────────────
    private LichScytheThrowState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new LichScytheThrowState(this);
    public override void OnRecycled()                       => _state = new LichScytheThrowState(this);

    public override bool CanExecute(BossPatternContext ctx)
    {
        if (ctx.Ctx.Runtime.PlayerTarget == null) return false;
        var lichBB = (ctx.Boss as LichMonster)?.LichBB;
        if (lichBB == null || !lichBB.IsPhase2) return false;
        if (lichBB.ScytheThrowCooldown > 0f) return false;
        return Vector3.Distance(ctx.Ctx.Transform.position, ctx.Ctx.Runtime.PlayerTarget.position) <= maxRange;
    }

    public override SpecialStateBase GetRuntimeState() => _state;
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// LichScytheThrowState — 중단 불가(피해는 받음)
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class LichScytheThrowState : UnInterruptibleState<LichScytheThrowPatternSO>
{
    private enum Phase { Cast, Signal, Out, Return, End, Recovery }

    private const float TetherWidth = 0.7f;   // 사슬 텍스처 — 보이는 고리 굵기 약 0.25 m
    private const int   MinCurveDots = 6;      // 휘는 궤적 예고 — 나가는 길·돌아오는 길 각각 원 몇 개로 찍는다
    private const int   MaxCurveDots = 24;
    private const float DotOverlap   = 0.85f;  // 원 사이 간격 = 지름 × 이 값(틈 없이 겹치게, 09-19 감사)

    private static readonly Color TetherColor = new Color(1.00f, 0.80f, 0.25f);

    private Phase      _phase;
    private float      _timer;
    private Vector3    _start;
    private Vector3    _end;
    private Vector3    _side;
    private float      _nextCrack;
    private float      _travelled;
    private Vector3    _lastPos;
    private bool       _hitOut;
    private bool       _hitBack;
    private GameObject    _guide;
    private GameObject    _spin;
    private LichChainLine _tether;
    // 휘는 궤적(해방판)은 곧은 줄이 맞지 않는다 — 실제 경로 위 원들로 예고한다(09-18 감사: 판정이 예고 밖으로 벗어남).
    private readonly List<GameObject> _dots = new();

    public LichScytheThrowState(LichScytheThrowPatternSO data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        _phase     = Phase.Cast;
        _timer     = 0f;
        _hitOut    = false;
        _hitBack   = false;
        _travelled = 0f;
        _nextCrack = Data.crackSpacing;

        Vector3 player = LichPatternUtil.PlayerFloorPos(ctx);
        LichPatternUtil.FaceInstant(ctx, player);
        LichPatternUtil.HoldFacing(ctx, ctx.Transform.eulerAngles.y);
        // 던지는 스윙의 접촉 프레임 = 낫이 손을 떠나는 순간(시전 + 신호 뒤).
        LichPatternUtil.Swing(ctx, LichSwing.RightToLeft, Data.castDuration + Data.signalDuration, 0.04f);

        var mc = LichPatternUtil.Mover(ctx);
        mc?.SetLocked(true);
        mc?.RequestMovementState(LichMovementState.AltitudeDescend);

        _start = LichPatternUtil.OnFloor(ctx, ctx.Transform.position);
        Vector3 dir = player - _start;
        dir.y = 0f;
        dir = dir.sqrMagnitude > 0.001f ? dir.normalized : ctx.Transform.forward;
        float len = Mathf.Min(Data.maxDistance, LichPatternUtil.DistanceToArenaEdge(ctx, _start, dir));
        _end  = _start + dir * Mathf.Max(2f, len);
        _side = Vector3.Cross(Vector3.up, dir) * (Random.value < 0.5f ? -1f : 1f);

        if (Data.curve <= 0.01f)
        {
            // 곧은 왕복 — 리치 발밑부터 끝(+판정 반경)까지.
            _guide = LichPatternUtil.PrepareTelegraph(
                PatternGuideHelper.Beam(_start, dir, Vector3.Distance(_start, _end) + Data.hitRadius, Data.hitRadius * 2f,
                                        LichPatternUtil.Crimson),
                LichPatternUtil.Crimson);
        }
        else
        {
            _dots.Clear();
            int count = DotCount(_start, _end, _side);
            for (int i = 0; i < count; i++)
            {
                float t = (i + 1f) / count;
                _dots.Add(MakeDot(Curve(_start, _end, _side, t)));
                _dots.Add(MakeDot(Curve(_end, _start, -_side, t)));
            }
        }
    }

    public override void Update(MonsterContext ctx)
    {
        float dt = Time.deltaTime;
        _timer += dt;

        switch (_phase)
        {
            case Phase.Cast:
            {
                LichPatternUtil.FaceInstant(ctx, _end);
                float p = _timer / Mathf.Max(0.01f, Data.castDuration);
                PatternGuideHelper.SetProgress(_guide, p);   // 날아갈 쪽으로 흐른다
                for (int i = 0; i < _dots.Count; i++) PatternGuideHelper.SetProgress(_dots[i], p);
                if (_timer >= Data.castDuration)
                {
                    SignalGuide(_guide);
                    for (int i = 0; i < _dots.Count; i++) SignalGuide(_dots[i]);
                    Next(Phase.Signal);
                }
                break;
            }

            case Phase.Signal:
                if (_timer >= Data.signalDuration)
                {
                    // 예고는 낫이 돌아올 때까지 남긴다 — 날아가는 동안에도 어디가 위험한지 보인다.
                    Release(ctx);
                    Next(Phase.Out);
                }
                break;

            case Phase.Out:
            {
                float t = Mathf.Clamp01(_timer / Data.outSeconds);
                Fly(ctx, Curve(_start, _end, _side, t), ref _hitOut, true);
                if (t >= 1f) Next(Phase.Return);
                break;
            }

            case Phase.Return:
            {
                float   t    = Mathf.Clamp01(_timer / Data.returnSeconds);
                Vector3 back = LichPatternUtil.OnFloor(ctx, ctx.Transform.position);
                Fly(ctx, Curve(_end, back, -_side, t), ref _hitBack, false);   // 반대쪽으로 휘어 돌아와 고리를 그린다
                if (t >= 1f)
                {
                    Catch(ctx);
                    Next(Phase.End);
                }
                break;
            }

            case Phase.End:
                if (_timer >= Data.endDuration) Next(Phase.Recovery);
                break;

            case Phase.Recovery:
                if (_timer >= Data.recoveryDuration)
                    ctx.Monster.ChangeState<ChaseState>();
                break;
        }
    }

    public override void Exit(MonsterContext ctx)
    {
        ClearGuides();
        DropTether();
        LichVfx.Stop(ref _spin);

        var lich = LichPatternUtil.Lich(ctx);
        lich?.SetScytheVisible(true);
        lich?.MovementController?.SetLocked(false);
        if (lich?.LichBB != null)
            lich.LichBB.ScytheThrowCooldown = Data.patternCooldown;
    }

    private void Next(Phase phase)
    {
        _phase = phase;
        _timer = 0f;
    }

    private void Release(MonsterContext ctx)
    {
        var lich = LichPatternUtil.Lich(ctx);
        lich?.SetScytheVisible(false);
        _lastPos = _start + Vector3.up * Data.spinHeight;
        _spin    = LichVfx.PlayLoop(LichVfxSlot.ScytheSpin, _lastPos, SpinRotation(_end - _start));
        LichSfx.Play(LichSfxSlot.ScytheThrow, _start);
        if (Data.chainTether)
        {
            _tether = LichChainLine.Create(Hand(ctx), _lastPos, TetherWidth, TetherColor);
            _tether.Slack = 0.04f;
        }
    }

    private void Fly(MonsterContext ctx, Vector3 floorPos, ref bool hit, bool outbound)
    {
        Vector3 pos = floorPos + Vector3.up * Data.spinHeight;
        if (_spin != null)
        {
            _spin.transform.position = pos;
            // 도는 원반이 휘는 길을 따라 방향을 튼다(한 번만 정하면 곡선에서 옆으로 미끄러져 보인다).
            Vector3 tangent = pos - _lastPos;
            if (tangent.sqrMagnitude > 0.0001f) _spin.transform.rotation = SpinRotation(tangent);
        }
        if (_tether != null) _tether.SetEnds(Hand(ctx), pos);

        if (outbound && Data.crackSpacing > 0f)
        {
            _travelled += Vector3.Distance(_lastPos, pos);
            if (_travelled >= _nextCrack)
            {
                _nextCrack += Data.crackSpacing;
                LichCrack.Spawn(floorPos, Data.hitRadius * 1.5f);
            }
        }
        _lastPos = pos;

        if (!hit && LichPatternUtil.HitCircle(ctx, floorPos, Data.hitRadius, Data.damageMultiplier, Data.knockbackMultiplier))
        {
            hit = true;
            LichPatternUtil.Impact(LichImpact.Slash, true);
            TryBind(ctx);
        }
    }

    /// <summary>사슬에 걸렸다 — 묶고, 묶인 자리에 폭발 예고를 건다.</summary>
    private void TryBind(MonsterContext ctx)
    {
        if (Data.bindSeconds <= 0f) return;
        var lich = LichPatternUtil.Lich(ctx);
        if (!LichHazards.ChainBind(ctx, lich != null ? lich.CastPoint : ctx.Transform, Data.bindSeconds)) return;

        _hitBack = true;   // 묶인 사람을 돌아오는 낫이 또 치지 않는다
        if (_tether != null) _tether.Tension();
        LichHazards.DelayedBlast(ctx, LichPatternUtil.PlayerFloorPos(ctx), Data.bindFollowRadius,
                                 Data.bindSeconds + Data.bindFollowAfter, Data.bindFollowDamage,
                                 LichVfxSlot.TwinBlast, Data.bindFollowRadius / 3f);
    }

    private void DropTether()
    {
        if (_tether != null) _tether.Dispose();
        _tether = null;
    }

    private void Catch(MonsterContext ctx)
    {
        ClearGuides();
        LichVfx.Stop(ref _spin, 0.2f);
        DropTether();
        var lich = LichPatternUtil.Lich(ctx);
        lich?.SetScytheVisible(true);
        lich?.NotifyVulnerableWindow(Data.endDuration);
        LichSfx.Play(LichSfxSlot.ScytheCatch, ctx.Transform.position);
        ctx.Animator?.CrossFade("ScytheCombo1", 0.05f, 0, 0.6f);
    }

    private GameObject MakeDot(Vector3 floorPos)
        => LichPatternUtil.PrepareTelegraph(PatternGuideHelper.Disc(floorPos, Data.hitRadius, LichPatternUtil.Crimson),
                                            LichPatternUtil.Crimson);

    private static void SignalGuide(GameObject guide)
    {
        if (guide == null) return;
        PatternGuideHelper.SetColor(guide, LichPatternUtil.Lethal);
        PatternGuideHelper.SetFlow(guide, LichPatternUtil.Lethal);
        PatternGuideHelper.SetIntensity(guide, 2f);
    }

    private void ClearGuides()
    {
        PatternGuideHelper.SafeDestroy(ref _guide);
        for (int i = 0; i < _dots.Count; i++)
        {
            var dot = _dots[i];
            PatternGuideHelper.SafeDestroy(ref dot);
        }
        _dots.Clear();
    }

    /// <summary>2차 베지어 — 가운데가 <paramref name="side"/> × curve만큼 옆으로 부푼다.</summary>
    private Vector3 Curve(Vector3 a, Vector3 b, Vector3 side, float t)
    {
        Vector3 c = (a + b) * 0.5f + side * Data.curve;
        float   u = 1f - t;
        return u * u * a + 2f * u * t * c + t * t * b;
    }

    /// <summary>한 구간에 찍을 원 수 — 곡선 길이를 지름보다 좁은 간격으로 나눈다.</summary>
    private int DotCount(Vector3 a, Vector3 b, Vector3 side)
    {
        float len  = 0f;
        Vector3 prev = a;
        for (int i = 1; i <= 16; i++)
        {
            Vector3 p = Curve(a, b, side, i / 16f);
            len += Vector3.Distance(prev, p);
            prev = p;
        }
        float spacing = Mathf.Max(0.5f, Data.hitRadius * 2f * DotOverlap);
        return Mathf.Clamp(Mathf.CeilToInt(len / spacing), MinCurveDots, MaxCurveDots);
    }

    /// <summary>도는 낫 원반을 눕힌다 — 카메라(위)에서 원반 면이 보이게, 진행 방향을 원반의 위쪽 축으로.</summary>
    private static Quaternion SpinRotation(Vector3 travel)
    {
        travel.y = 0f;
        return travel.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(Vector3.up, travel.normalized) : Quaternion.identity;
    }

    private static Vector3 Hand(MonsterContext ctx)
    {
        var lich = LichPatternUtil.Lich(ctx);
        return lich != null ? lich.CastPoint.position : ctx.Transform.position + Vector3.up * 1.5f;
    }
}
}

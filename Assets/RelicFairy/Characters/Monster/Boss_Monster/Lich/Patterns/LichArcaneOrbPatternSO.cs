using System.Collections.Generic;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// M3 「비전 구체」 — 반경 3 m 구체 셋이 바닥을 굴러간다. 리치 설계서 §3-2 · §13.
///
/// O 궤적(castDuration) — 리치 앞 부채꼴로 보라 화살표 세 줄(구체 폭)이 바닥에 뜬다
/// → S 신호(signalDuration) — 화살표가 빨개지고 구체가 떨어진다, 리치는 반동으로 뒤로 밀린다
/// → A 굴러감(최대 rollDuration) — 초속 orbSpeed(달리기보다 느림), 지나간 자리에 균열, 제단 가장자리에서 터진다
/// → E 반격창(endDuration) → R 복귀
/// 구체는 한 개당 한 번만 맞힌다. 사이 틈으로 빠지거나 앞질러 달리면 피한다.
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/Boss/Lich/Lich_ArcaneOrbPattern", fileName = "Lich_ArcaneOrbPattern")]
public class LichArcaneOrbPatternSO : BossPatternSO
{
    [Header("M3 — 발동")]
    public float maxRange        = 25f;
    public float patternCooldown = 6f;

    [Header("M3 — 타이밍 (초)")]
    [Tooltip("궤적 화살표가 떠 있는 시간")]
    public float castDuration     = 1.0f;
    public float signalDuration   = 0.2f;
    [Tooltip("구체가 구르는 최대 시간 — 가장자리에 먼저 닿으면 그때 터진다")]
    public float rollDuration     = 4.0f;
    public float endDuration      = 0.8f;
    public float recoveryDuration = 0.4f;

    [Header("M3 — 구체")]
    public int   orbCount      = 3;
    [Tooltip("부채꼴 전체 각도 (도)")]
    public float fanAngle      = 120f;
    public float orbRadius     = 3f;
    public float orbSpeed      = 6f;
    [Tooltip("리치 발밑에서 이만큼 앞에 떨어진다 (m)")]
    public float spawnDistance = 3f;
    [Tooltip("이 거리마다 바닥 균열 (m)")]
    public float crackSpacing  = 2.5f;

    [Header("M3 — 판정")]
    public float damageMultiplier    = 1.0f;
    public float knockbackMultiplier = 1.2f;

    [Header("M3 — 움직임")]
    [Tooltip("구체를 떨어뜨리는 반동으로 뒤로 밀리는 거리 (m)")]
    public float recoilDistance = 3f;
    public float recoilSeconds  = 0.6f;

    [Header("M3⁺ — 악몽판(완전설계 §4-2): 가장자리에서 튕긴다")]
    [Tooltip("가장자리에 닿으면 이 횟수만큼 플레이어 쪽으로 튕겨 다시 구른다(0이면 터진다 · 악몽판 1). rollDuration도 늘려야 한다")]
    public int   bounces        = 0;

    // ── 런타임 ───────────────────────────────────────────
    private LichArcaneOrbState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new LichArcaneOrbState(this);
    public override void OnRecycled()                       => _state = new LichArcaneOrbState(this);

    public override bool CanExecute(BossPatternContext ctx)
    {
        if (ctx.Ctx.Runtime.PlayerTarget == null) return false;
        var lichBB = (ctx.Boss as LichMonster)?.LichBB;
        if (lichBB != null && lichBB.ArcaneOrbCooldown > 0f) return false;
        return Vector3.Distance(ctx.Ctx.Transform.position, ctx.Ctx.Runtime.PlayerTarget.position) <= maxRange;
    }

    public override SpecialStateBase GetRuntimeState() => _state;
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// LichArcaneOrbState — 중단 불가(피해는 받음)
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class LichArcaneOrbState : UnInterruptibleState<LichArcaneOrbPatternSO>
{
    private enum Phase { Cast, Signal, Roll, End, Recovery }

    private struct Orb
    {
        public GameObject Vfx;
        public GameObject Arrow;
        public Vector3    Start;
        public Vector3    Dir;
        public float      Length;     // 가장자리까지
        public float      Travelled;
        public float      NextCrack;
        public bool       Hit;
        public bool       Alive;
        public int        Bounces;    // 남은 튕김(M3⁺)
        public bool       EdgeBound;  // 가장자리에 막혀 멈추는 궤적인가(튕길 수 있는가)
    }

    private const float SharedHitGap = 0.35f;   // 구체끼리 판정 간격 — 한 번 맞으면 이만큼은 다른 구체도 못 친다

    private readonly List<Orb> _orbs = new(4);

    private Phase       _phase;
    private float       _timer;
    private float       _lastHitTime = -1f;
    private GameObject  _castFlare;
    private AudioSource _rollLoop;

    public LichArcaneOrbState(LichArcaneOrbPatternSO data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        _phase       = Phase.Cast;
        _timer       = 0f;
        _lastHitTime = -1f;
        _orbs.Clear();

        var mc = LichPatternUtil.Mover(ctx);
        mc?.SetLocked(true);
        mc?.RequestMovementState(LichMovementState.AltitudeDescend);   // 수평은 멈춰 서서 시전

        Vector3 aim = LichPatternUtil.PlayerFloorPos(ctx);
        LichPatternUtil.FaceInstant(ctx, aim);
        ctx.Animator?.CrossFade("ArcaneOrb", 0.1f);

        var       lich = LichPatternUtil.Lich(ctx);
        Transform hand = lich != null ? lich.CastPoint : ctx.Transform;
        _castFlare = LichVfx.PlayLoop(LichVfxSlot.CastFlare, hand.position, hand.rotation, 1.2f, hand);
        lich?.PulseBook(Data.castDuration);
        LichSfx.Play(LichSfxSlot.CastCharge, hand.position, 0.8f);

        // 부채꼴 궤적 — 플레이어 쪽이 가운데.
        Vector3 fwd = aim - ctx.Transform.position;
        fwd.y = 0f;
        fwd = fwd.sqrMagnitude > 0.001f ? fwd.normalized : ctx.Transform.forward;
        Vector3 origin = LichPatternUtil.OnFloor(ctx, ctx.Transform.position);

        int   count = Mathf.Max(1, Data.orbCount);
        float step  = count > 1 ? Data.fanAngle / (count - 1) : 0f;
        float first = count > 1 ? -Data.fanAngle * 0.5f : 0f;
        for (int i = 0; i < count; i++)
        {
            Vector3 dir   = Quaternion.Euler(0f, first + step * i, 0f) * fwd;
            Vector3 start = origin + dir * Data.spawnDistance;
            float   len   = Mathf.Min(Data.orbSpeed * Data.rollDuration, LichPatternUtil.DistanceToArenaEdge(ctx, start, dir));
            _orbs.Add(new Orb
            {
                Start     = start,
                Dir       = dir,
                Length    = Mathf.Max(0f, len),
                NextCrack = Data.crackSpacing,
                Bounces   = Data.bounces,
                EdgeBound = len < Data.orbSpeed * Data.rollDuration - 0.01f,
                // 굴러갈 방향으로 흐르며 차오르는 궤적(시전 시간 동안).
                Arrow     = LichPatternUtil.PrepareTelegraph(
                                PatternGuideHelper.Beam(origin, dir, Data.spawnDistance + Mathf.Max(0.5f, len) + Data.orbRadius,
                                                        Data.orbRadius * 2f, LichPatternUtil.Arcane),
                                LichPatternUtil.Arcane),
            });
        }
    }

    public override void Update(MonsterContext ctx)
    {
        float dt = Time.deltaTime;
        _timer += dt;

        switch (_phase)
        {
            case Phase.Cast:
                for (int i = 0; i < _orbs.Count; i++)
                    PatternGuideHelper.SetProgress(_orbs[i].Arrow, _timer / Mathf.Max(0.01f, Data.castDuration));
                if (_timer >= Data.castDuration)
                {
                    for (int i = 0; i < _orbs.Count; i++)
                    {
                        PatternGuideHelper.SetColor(_orbs[i].Arrow, LichPatternUtil.Lethal);
                        PatternGuideHelper.SetFlow(_orbs[i].Arrow, LichPatternUtil.Lethal);
                        PatternGuideHelper.SetIntensity(_orbs[i].Arrow, 2f);
                    }
                    Next(Phase.Signal);
                }
                break;

            case Phase.Signal:
                if (_timer >= Data.signalDuration)
                {
                    Release(ctx);
                    Next(Phase.Roll);
                }
                break;

            case Phase.Roll:
                if (!TickOrbs(ctx, dt) || _timer >= Data.rollDuration)
                {
                    BurstAll();
                    Next(Phase.End);
                }
                break;

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
        LichVfx.Stop(ref _castFlare);
        LichSfx.StopLoop(ref _rollLoop);
        for (int i = 0; i < _orbs.Count; i++)
        {
            var o = _orbs[i];
            LichVfx.Stop(ref o.Vfx);
            PatternGuideHelper.SafeDestroy(ref o.Arrow);
        }
        _orbs.Clear();

        var lich = LichPatternUtil.Lich(ctx);
        lich?.MovementController?.SetLocked(false);
        if (lich?.LichBB != null)
            lich.LichBB.ArcaneOrbCooldown = Data.patternCooldown;
    }

    private void Next(Phase phase)
    {
        _phase = phase;
        _timer = 0f;
    }

    /// <summary>구체를 떨어뜨리고 반동으로 뒤로 밀린다.</summary>
    private void Release(MonsterContext ctx)
    {
        LichVfx.Stop(ref _castFlare, 0.2f);
        float lift = Data.orbRadius * 0.8f;
        for (int i = 0; i < _orbs.Count; i++)
        {
            var o = _orbs[i];
            PatternGuideHelper.SetProgress(o.Arrow, 1f);
            PatternGuideHelper.SetColor(o.Arrow, LichPatternUtil.Lethal);
            PatternGuideHelper.SetFlow(o.Arrow, LichPatternUtil.Lethal, 0.6f);
            o.Vfx   = LichVfx.PlayLoop(LichVfxSlot.ArcaneOrb, o.Start + Vector3.up * lift, Quaternion.LookRotation(o.Dir), Data.orbRadius / 3f);
            o.Alive = true;
            LichCrack.Spawn(o.Start, Data.orbRadius * 1.6f);
            _orbs[i] = o;
        }

        var mc = LichPatternUtil.Mover(ctx);
        if (mc != null && Data.recoilDistance > 0f)
            mc.ScriptMove(ctx.Transform.position - ctx.Transform.forward * Data.recoilDistance, Data.recoilSeconds, 0f, facePlayer: true);
        LichSfx.Play(LichSfxSlot.DarkOrb, ctx.Transform.position);
        _rollLoop = LichSfx.PlayLoop(LichSfxSlot.ZoneHum, LichPatternUtil.OnFloor(ctx, ctx.Transform.position), 0.6f);
    }

    /// <summary>구체를 굴린다. 하나라도 살아 있으면 true.</summary>
    private bool TickOrbs(MonsterContext ctx, float dt)
    {
        bool any  = false;
        float lift = Data.orbRadius * 0.8f;
        for (int i = 0; i < _orbs.Count; i++)
        {
            var o = _orbs[i];
            if (!o.Alive) continue;

            o.Travelled += Data.orbSpeed * dt;
            Vector3 center = o.Start + o.Dir * Mathf.Min(o.Travelled, o.Length);
            if (o.Vfx != null) o.Vfx.transform.position = center + Vector3.up * lift;

            if (o.Travelled >= o.NextCrack)
            {
                o.NextCrack += Data.crackSpacing;
                LichCrack.Spawn(center, Data.orbRadius * 1.3f);   // 지나간 줄에 균열(설계: 흔적)
            }

            if (!o.Hit && Time.time - _lastHitTime >= SharedHitGap
                && LichPatternUtil.HitCircle(ctx, center, Data.orbRadius, Data.damageMultiplier, Data.knockbackMultiplier))
            {
                o.Hit        = true;
                _lastHitTime = Time.time;
                LichVfx.Play(LichVfxSlot.ArcaneOrbBurst, center + Vector3.up * lift, Quaternion.identity, 0.35f);
                LichPatternUtil.Impact(LichImpact.Medium, true);
            }

            if (o.Travelled >= o.Length)
            {
                if (o.Bounces > 0 && o.EdgeBound) Bounce(ctx, ref o, center);
                else                              Burst(ref o, center);
                any |= o.Alive;
            }
            else
            {
                any = true;
            }
            _orbs[i] = o;
        }
        return any;
    }

    /// <summary>M3⁺ — 가장자리에서 튕겨 지금 플레이어 쪽으로 다시 구른다. 새 궤적은 곧장 빨강(속도가 느려 피할 수 있다).</summary>
    private void Bounce(MonsterContext ctx, ref Orb o, Vector3 center)
    {
        o.Bounces--;
        Vector3 to = LichPatternUtil.PlayerFloorPos(ctx) - center;
        to.y = 0f;
        Vector3 dir = to.sqrMagnitude > 0.01f ? to.normalized : -o.Dir;
        float room  = Data.orbSpeed * Mathf.Max(0.5f, Data.rollDuration - _timer);
        float len   = Mathf.Min(room, LichPatternUtil.DistanceToArenaEdge(ctx, center + dir * 0.1f, dir));

        o.Start     = center;
        o.Dir       = dir;
        o.Travelled = 0f;
        o.Length    = Mathf.Max(0.5f, len);
        o.EdgeBound = len < room - 0.01f;
        o.NextCrack = Data.crackSpacing;
        o.Hit       = false;

        PatternGuideHelper.SafeDestroy(ref o.Arrow);
        o.Arrow = PatternGuideHelper.Beam(center, dir, o.Length + Data.orbRadius, Data.orbRadius * 2f, LichPatternUtil.Lethal);
        PatternGuideHelper.SetFlow(o.Arrow, LichPatternUtil.Lethal, 0.6f);
        LichVfx.Play(LichVfxSlot.ArcaneOrbBurst, center + Vector3.up * 0.5f, Quaternion.identity, 0.4f);
        LichSfx.Play(LichSfxSlot.BoltImpact, center, 0.7f);
        LichCrack.Spawn(center, Data.orbRadius * 1.6f);
    }

    private void BurstAll()
    {
        LichSfx.StopLoop(ref _rollLoop);
        for (int i = 0; i < _orbs.Count; i++)
        {
            var o = _orbs[i];
            if (!o.Alive) continue;
            Burst(ref o, o.Start + o.Dir * Mathf.Min(o.Travelled, o.Length));
            _orbs[i] = o;
        }
    }

    private void Burst(ref Orb o, Vector3 center)
    {
        o.Alive = false;
        LichVfx.Stop(ref o.Vfx, 0.3f);
        PatternGuideHelper.SafeDestroy(ref o.Arrow);
        LichVfx.Play(LichVfxSlot.ArcaneOrbBurst, center + Vector3.up * 0.5f, Quaternion.identity, Data.orbRadius / 3f);
        LichSfx.Play(LichSfxSlot.BoltImpact, center);
    }
}
}

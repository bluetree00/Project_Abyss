using System.Collections.Generic;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// M2 「원소 포격」 — 리치 둘레의 원소 마법진 넷이 차례로 포격한다. 리치 설계서 §3-2 · §13.
///
/// O 시전(castDuration) — 리치가 떠오르며 뒤로 물러나고, 마법진 넷이 <b>발사 순서대로</b> 한 번씩 밝아진다(순서가 곧 예고)
/// → S 신호(signalDuration)
/// → A 포격(원소마다 elementSlot초, 순서는 매번 무작위)
///     불    — 리치→플레이어 방향 폭 3 m × 30 m 관통 광선. 0.6초 전에 붉은 줄
///     얼음  — 플레이어 주변 6 m 안에 창 5개(반경 1.5 m, 하나는 발밑), 맞은 자리에 감속 장판 4초
///     번개  — 0.3초 간격 낙뢰 3번이 그때그때 플레이어 발밑을 노린다 — 멈추면 맞는다
///     어둠  — 느린 추적 구체(초속 4 m, 6초) — 달리면 뿌리친다. 패턴이 끝나도 산다
/// → E 반격창(endDuration) → R 복귀
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/Boss/Lich/Lich_ElementalBarragePattern", fileName = "Lich_ElementalBarragePattern")]
public class LichElementalBarragePatternSO : BossPatternSO
{
    [Header("M2 — 발동")]
    public float maxRange        = 30f;
    public float patternCooldown = 8f;

    [Header("M2 — 타이밍 (초)")]
    public float castDuration     = 1.2f;
    public float signalDuration   = 0.2f;
    [Tooltip("원소 하나가 차지하는 시간 — 넷이면 포격 전체가 이 값 × 4")]
    public float elementSlot      = 0.9f;
    [Tooltip("한 번에 쏘는 원소 수 — 1이면 하나씩, 악몽판(M2⁺)은 2(둘씩 동시, 마법진이 쌍으로 밝아진다)")]
    public int   elementsPerSlot  = 1;
    public float endDuration      = 0.6f;
    public float recoveryDuration = 0.4f;

    [Header("M2 — 마법진")]
    [Tooltip("리치 중심에서 마법진까지 (m)")]
    public float circleOrbit     = 2.2f;
    public float circleHeight    = 1.2f;
    public float circleIdleScale = 0.7f;
    public float circleLitScale  = 1.5f;

    [Header("M2 — 불: 관통 광선")]
    public float fireWarn      = 0.6f;
    [Tooltip("광선 이펙트가 떠 있는 시간 — 팩 광선은 초속 50 m로 뻗으니 30 m면 0.6초가 걸린다")]
    public float fireBeamSeconds = 1.2f;
    public float fireLength    = 30f;
    public float fireWidth     = 3f;
    public float fireDamage    = 1.3f;
    public float fireKnockback = 1.0f;
    [Tooltip("광선이 지나간 줄에 남는 불길(여운 장판) 시간 (초). 0이면 없음 — 연출·UX 시나리오 §12-5")]
    public float fireScorchSeconds = 0f;
    [Tooltip("불길 장판 사이 간격 (m) — 반경은 광선 폭의 절반")]
    public float fireScorchSpacing = 5f;
    [Tooltip("불길 한 틱 피해 배율")]
    public float fireScorchDamage  = 0.1f;

    [Header("M2 — 얼음: 창")]
    public int   iceCount       = 5;
    public float iceArea        = 6f;
    public float iceRadius      = 1.5f;
    public float iceWarn        = 0.6f;
    public float iceDamage      = 0.8f;
    [Range(0f, 1f)]
    public float iceSlowScale   = 0.6f;
    public float iceSlowSeconds = 4f;

    [Header("M2 — 번개: 따라오는 낙뢰")]
    public int   boltCount    = 3;
    public float boltInterval = 0.3f;
    public float boltWarn     = 0.35f;
    public float boltRadius   = 1.8f;
    public float boltDamage   = 0.9f;

    [Header("M2 — 어둠: 추적 구체")]
    public float orbSpeed     = 4f;
    public float orbTurn      = 90f;
    public float orbSeconds   = 6f;
    public float orbRadius    = 1.2f;
    public float orbDamage    = 1.0f;
    public float orbKnockback = 0.5f;

    [Header("M2 — 움직임")]
    [Tooltip("시전하며 기본 고도 위로 더 떠오르는 높이 (m)")]
    public float riseHeight      = 2.5f;
    public float retreatDistance = 4f;
    public float retreatSeconds  = 1.2f;

    // ── 런타임 ───────────────────────────────────────────
    private LichElementalBarrageState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new LichElementalBarrageState(this);
    public override void OnRecycled()                       => _state = new LichElementalBarrageState(this);

    public override bool CanExecute(BossPatternContext ctx)
    {
        if (ctx.Ctx.Runtime.PlayerTarget == null) return false;
        var lichBB = (ctx.Boss as LichMonster)?.LichBB;
        if (lichBB != null && lichBB.ElementalBarrageCooldown > 0f) return false;
        return Vector3.Distance(ctx.Ctx.Transform.position, ctx.Ctx.Runtime.PlayerTarget.position) <= maxRange;
    }

    public override SpecialStateBase GetRuntimeState() => _state;
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// LichElementalBarrageState — 중단 불가(피해는 받음)
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class LichElementalBarrageState : UnInterruptibleState<LichElementalBarragePatternSO>
{
    private enum Phase   { Cast, Signal, Barrage, End, Recovery }
    private enum Element { Fire, Ice, Lightning, Dark }
    private enum StrikeKind { FireBeam, IceSpear, LightningMark, LightningBolt }

    private const int ElementCount = 4;

    private static readonly LichVfxSlot[] CircleSlots =
        { LichVfxSlot.CircleFire, LichVfxSlot.CircleIce, LichVfxSlot.CircleLightning, LichVfxSlot.CircleDark };

    /// <summary>시간이 되면 터지는 판정 하나(예고 표식 포함).</summary>
    private struct Strike
    {
        public StrikeKind Kind;
        public float      At;
        public Vector3    Pos;
        public Vector3    Dir;
        public GameObject Guide;
        public float      Start;      // 예고가 뜬 시각 — 차오름 = (지금 − Start) / (At − Start)
        public bool       Signaled;
    }

    private const float SignalSeconds = 0.15f;
    private const float BeamGrowSpeed = 50f;   // 불 광선 이펙트(PixPlays)가 뻗는 속도 (m/s)

    private readonly Element[]      _order   = { Element.Fire, Element.Ice, Element.Lightning, Element.Dark };
    private readonly GameObject[]   _circles = new GameObject[ElementCount];
    private readonly Vector3[]      _circleBase = new Vector3[ElementCount];
    private readonly List<Strike>   _strikes = new(12);

    private Phase _phase;
    private float _timer;
    private float _barrageTime;
    private int   _fired;
    private int   _slot;
    private int   _previewed;

    private int Per   => Mathf.Clamp(Data.elementsPerSlot, 1, ElementCount);
    private int Slots => (ElementCount + Per - 1) / Per;

    public LichElementalBarrageState(LichElementalBarragePatternSO data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        _phase       = Phase.Cast;
        _timer       = 0f;
        _barrageTime = 0f;
        _fired       = 0;
        _slot        = 0;
        _previewed   = 0;
        _strikes.Clear();
        Shuffle(_order);

        Vector3 player = LichPatternUtil.PlayerFloorPos(ctx);
        LichPatternUtil.FaceInstant(ctx, player);
        ctx.Animator?.CrossFade("ElementalBarrage", 0.1f);
        LichSfx.Play(LichSfxSlot.CastCharge, ctx.Transform.position);
        LichPatternUtil.Lich(ctx)?.PulseBook(Data.castDuration);

        // 떠오르며 뒤로 물러난다 — 포대가 아니라 거리를 벌리는 대마법사.
        var mc = LichPatternUtil.Mover(ctx);
        if (mc != null)
        {
            mc.SetLocked(true);
            mc.SetAltitudeOffset(Data.riseHeight);
            if (Data.retreatDistance > 0f)
                mc.ScriptMove(ctx.Transform.position - ctx.Transform.forward * Data.retreatDistance,
                              Data.retreatSeconds, 0f, facePlayer: true);
        }

        // 마법진 넷 — 리치를 따라다니게 자식으로 붙인다(원소 순서대로 등 뒤에서 둘러싼다).
        for (int i = 0; i < ElementCount; i++)
        {
            float   a     = (i * 90f + 45f) * Mathf.Deg2Rad;
            Vector3 local = new Vector3(Mathf.Cos(a) * Data.circleOrbit, Data.circleHeight, Mathf.Sin(a) * Data.circleOrbit - 0.5f);
            int     e     = (int)_order[i];
            _circles[i] = LichVfx.PlayLoop(CircleSlots[e], ctx.Transform.TransformPoint(local),
                                           ctx.Transform.rotation, Data.circleIdleScale, ctx.Transform);
            _circleBase[i] = _circles[i] != null ? _circles[i].transform.localScale : Vector3.one;
        }
    }

    public override void Update(MonsterContext ctx)
    {
        float dt = Time.deltaTime;
        _timer += dt;
        TickStrikes(ctx);

        switch (_phase)
        {
            case Phase.Cast:
                // 발사 순서 예고 — 시전 시간을 칸 수로 나눠 차례로 한 번씩 부풀린다(둘씩이면 쌍으로).
                PulseCircles(_timer / Data.castDuration * Slots, _previewed, Per);
                if (_previewed < Slots && _timer >= Data.castDuration * _previewed / Slots)
                    _previewed++;
                if (_timer >= Data.castDuration)
                {
                    ResetCircleScales();
                    Next(Phase.Signal);
                }
                break;

            case Phase.Signal:
                if (_timer >= Data.signalDuration) Next(Phase.Barrage);
                break;

            case Phase.Barrage:
            {
                int per   = Per;
                int slots = Slots;
                _barrageTime += dt;
                PulseCircles(_barrageTime / Data.elementSlot, _slot, per);
                if (_slot < slots && _barrageTime >= Data.elementSlot * _slot)
                {
                    for (int j = 0; j < per && _fired < ElementCount; j++)
                    {
                        FireElement(ctx, _fired);
                        _fired++;
                    }
                    _slot++;
                }
                if (_slot >= slots && _barrageTime >= Data.elementSlot * slots && _strikes.Count == 0)
                    Next(Phase.End);
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
        for (int i = 0; i < ElementCount; i++)
            LichVfx.Stop(ref _circles[i]);
        for (int i = 0; i < _strikes.Count; i++)
        {
            var s = _strikes[i];
            PatternGuideHelper.SafeDestroy(ref s.Guide);
        }
        _strikes.Clear();

        var lich = LichPatternUtil.Lich(ctx);
        var mc   = lich?.MovementController;
        mc?.SetAltitudeOffset(0f);
        mc?.SetLocked(false);
        if (lich?.LichBB != null)
            lich.LichBB.ElementalBarrageCooldown = Data.patternCooldown;
    }

    // ── 원소 ────────────────────────────────────────────

    private void FireElement(MonsterContext ctx, int index)
    {
        float now = _barrageTime;
        LichPatternUtil.Lich(ctx)?.PulseBook(0.5f);
        switch (_order[index])
        {
            case Element.Fire:
            {
                Vector3 origin = LichPatternUtil.OnFloor(ctx, ctx.Transform.position);
                Vector3 dir    = LichPatternUtil.PlayerFloorPos(ctx) - origin;
                dir.y = 0f;
                dir   = dir.sqrMagnitude > 0.001f ? dir.normalized : ctx.Transform.forward;
                // 붉은 줄이 리치 쪽에서 끝까지 흘러간다(설계: 0.6초 전 붉은 줄).
                AddStrike(StrikeKind.FireBeam, now + Data.fireWarn, origin, dir,
                          LichPatternUtil.PrepareTelegraph(
                              PatternGuideHelper.Beam(origin, dir, Data.fireLength, Data.fireWidth, LichPatternUtil.Lethal),
                              LichPatternUtil.Lethal));
                LichSfx.Play(LichSfxSlot.CastShort, origin);
                break;
            }

            case Element.Ice:
            {
                Vector3 player = LichPatternUtil.PlayerFloorPos(ctx);
                for (int i = 0; i < Data.iceCount; i++)
                {
                    Vector3 p = player;
                    if (i > 0)
                    {
                        Vector2 r = Random.insideUnitCircle * Data.iceArea;
                        p = LichPatternUtil.OnFloor(ctx, LichPatternUtil.Mover(ctx)?.ClampToArena(player + new Vector3(r.x, 0f, r.y))
                                                         ?? player + new Vector3(r.x, 0f, r.y));
                    }
                    AddStrike(StrikeKind.IceSpear, now + Data.iceWarn, p, Vector3.zero,
                              LichPatternUtil.PrepareTelegraph(
                                  PatternGuideHelper.Disc(p, Data.iceRadius, LichPatternUtil.Arcane), LichPatternUtil.Arcane));
                }
                LichSfx.Play(LichSfxSlot.IceCast, player);
                break;
            }

            case Element.Lightning:
                // 표식은 그때그때 플레이어 발밑에 — 첫 표식만 지금, 나머지는 간격마다(LightningMark가 만든다).
                for (int i = 0; i < Data.boltCount; i++)
                    AddStrike(StrikeKind.LightningMark, now + Data.boltInterval * i, Vector3.zero, Vector3.zero, null);
                break;

            case Element.Dark:
            {
                var       lich = LichPatternUtil.Lich(ctx);
                Transform hand = lich != null ? lich.CastPoint : ctx.Transform;
                Vector3   from = LichPatternUtil.OnFloor(ctx, hand.position) + Vector3.up * 1.2f;
                LichHazards.HomingOrb(ctx, from, Data.orbSpeed, Data.orbTurn, Data.orbSeconds,
                                      Data.orbRadius, Data.orbDamage, Data.orbKnockback);
                LichSfx.Play(LichSfxSlot.DarkOrb, from);
                break;
            }
        }
    }

    private void AddStrike(StrikeKind kind, float at, Vector3 pos, Vector3 dir, GameObject guide)
        => _strikes.Add(new Strike { Kind = kind, At = at, Pos = pos, Dir = dir, Guide = guide, Start = _barrageTime });

    private void TickStrikes(MonsterContext ctx)
    {
        for (int i = _strikes.Count - 1; i >= 0; i--)
        {
            var s = _strikes[i];
            if (_barrageTime < s.At)
            {
                if (s.Guide != null)
                {
                    LichPatternUtil.TickTelegraph(s.Guide, _barrageTime - s.Start, s.At - s.Start, SignalSeconds, ref s.Signaled);
                    _strikes[i] = s;
                }
                continue;
            }
            _strikes.RemoveAt(i);

            switch (s.Kind)
            {
                case StrikeKind.FireBeam:
                {
                    PatternGuideHelper.SafeDestroy(ref s.Guide);
                    var       lich = LichPatternUtil.Lich(ctx);
                    Transform hand = lich != null ? lich.CastPoint : ctx.Transform;
                    LichVfx.PlayBeam(LichVfxSlot.FireBeam, hand.position, s.Pos + s.Dir * Data.fireLength, Data.fireWidth / 3f, Data.fireBeamSeconds);   // 목록 배율은 굵기 3 m 기준
                    LichSfx.Play(LichSfxSlot.FireBeam, s.Pos);
                    LichCrack.SpawnLine(s.Pos + s.Dir * 2f, s.Pos + s.Dir * Data.fireLength, Data.fireWidth * 0.8f, 3.5f);
                    // 판정은 광선 앞머리를 따라간다(이펙트가 초속 BeamGrowSpeed로 뻗는다 — 예전엔 닿기 전에 맞았다).
                    LichHazards.BeamFront(ctx, s.Pos, s.Dir, Data.fireLength, Data.fireWidth * 0.5f,
                                          BeamGrowSpeed, Data.fireDamage, Data.fireKnockback);
                    LichPatternUtil.Impact(LichImpact.Heavy);
                    SpawnScorch(ctx, s.Pos, s.Dir);
                    break;
                }

                case StrikeKind.IceSpear:
                    PatternGuideHelper.SafeDestroy(ref s.Guide);
                    LichVfx.Play(LichVfxSlot.IceSpear, s.Pos, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), Data.iceRadius / 1.5f);
                    LichVfx.Play(LichVfxSlot.IceImpact, s.Pos, Quaternion.identity, Data.iceRadius / 1.5f);
                    LichSfx.Play(LichSfxSlot.BoltImpact, s.Pos, 0.5f);
                    LichPatternUtil.Impact(LichImpact.Light, LichPatternUtil.HitCircle(ctx, s.Pos, Data.iceRadius, Data.iceDamage));
                    LichHazards.SlowZone(ctx, s.Pos, Data.iceRadius, Data.iceSlowSeconds, Data.iceSlowScale, LichVfxSlot.ZoneIce);
                    break;

                case StrikeKind.LightningMark:
                {
                    Vector3 p = LichPatternUtil.PlayerFloorPos(ctx);
                    AddStrike(StrikeKind.LightningBolt, _barrageTime + Data.boltWarn, p, Vector3.zero,
                              LichPatternUtil.PrepareTelegraph(
                                  PatternGuideHelper.Disc(p, Data.boltRadius, LichPatternUtil.Arcane), LichPatternUtil.Arcane));
                    break;
                }

                case StrikeKind.LightningBolt:
                    PatternGuideHelper.SetColor(s.Guide, LichPatternUtil.Lethal);
                    if (s.Guide != null) Object.Destroy(s.Guide, 0.12f);
                    LichVfx.Play(LichVfxSlot.LightningStrike, s.Pos, Quaternion.identity, Data.boltRadius / 1.8f);
                    LichSfx.Play(LichSfxSlot.LightningCast, s.Pos);
                    LichCrack.Spawn(s.Pos, Data.boltRadius * 1.3f);
                    LichPatternUtil.Impact(LichImpact.Light, LichPatternUtil.HitCircle(ctx, s.Pos, Data.boltRadius, Data.boltDamage));
                    break;
            }
        }
    }

    // ── 마법진 연출 ──────────────────────────────────────

    /// <summary><paramref name="progress"/>의 소수부로 <paramref name="index"/>번째 마법진 하나만 부풀렸다 되돌린다.</summary>
    /// <summary>지금 쏘는 칸의 마법진이 부풀었다 가라앉는다 — 둘씩이면 쌍으로.</summary>
    private void PulseCircles(float progress, int slot, int per)
    {
        int active = Mathf.Max(0, slot - 1);
        float t    = Mathf.Clamp01(progress - active);
        float k    = Mathf.Lerp(1f, Data.circleLitScale / Data.circleIdleScale, Mathf.Sin(t * Mathf.PI));
        int from = active * per;
        int to   = Mathf.Min(ElementCount, from + per);
        for (int i = 0; i < ElementCount; i++)
        {
            if (_circles[i] == null) continue;
            _circles[i].transform.localScale = _circleBase[i] * (i >= from && i < to ? k : 1f);
        }
    }

    private void ResetCircleScales()
    {
        for (int i = 0; i < ElementCount; i++)
            if (_circles[i] != null) _circles[i].transform.localScale = _circleBase[i];
    }

    private void Next(Phase phase)
    {
        _phase = phase;
        _timer = 0f;
    }

    /// <summary>광선이 지나간 줄 — 불길 장판이 간격마다 남는다(서 있으면 0.4초마다 탄다).</summary>
    private void SpawnScorch(MonsterContext ctx, Vector3 origin, Vector3 dir)
    {
        if (Data.fireScorchSeconds <= 0f || Data.fireScorchSpacing <= 0f) return;
        float radius = Data.fireWidth * 0.5f;
        for (float d = radius + 1f; d < Data.fireLength; d += Data.fireScorchSpacing)
            LichHazards.LingerPool(ctx, LichPatternUtil.OnFloor(ctx, origin + dir * d), radius,
                                   Data.fireScorchSeconds, Data.fireScorchDamage);
    }

    private static void Shuffle(Element[] a)
    {
        for (int i = a.Length - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (a[i], a[j]) = (a[j], a[i]);
        }
    }
}
}

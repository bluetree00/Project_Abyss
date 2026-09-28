using System.Collections.Generic;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// M1 「성흔 탄막」 — 조준 마력탄 연발. 리치 설계서 §3-2 · §13.
///
/// O 시전(castDuration) — 책이 열리고 손에 보라 섬광, 리치는 옆으로 흘러간다
/// → 연발(boltCount × boltInterval) — 발사 순간 <b>그때의 플레이어 발밑</b>을 조준(추적 없음), 착탄점에 보라 원이
///   비행 시간(flightTime)만큼 먼저 뜨고, 닿는 순간 빨강으로 바뀌며 원 판정
/// → E 반격창(endDuration) — 책을 닫는다 → R 복귀
/// 좌우로 한 번 크게 움직이면 셋 다 피한다.
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/Boss/Lich/Lich_MagicBoltPattern", fileName = "Lich_MagicBoltPattern")]
public class LichMagicBoltPatternSO : BossPatternSO
{
    [Header("M1 — 발동")]
    [Tooltip("이 거리 안에서만 쓴다 (m)")]
    public float maxRange = 25f;
    [Tooltip("패턴 완료 후 재사용 대기 (초)")]
    public float patternCooldown = 4f;

    [Header("M1 — 타이밍 (초)")]
    public float castDuration = 0.8f;
    public int   boltCount    = 3;
    public float boltInterval = 0.35f;
    [Tooltip("발사에서 착탄까지 — 착탄 원이 이만큼 먼저 뜬다")]
    public float flightTime   = 0.35f;
    [Tooltip("마지막 탄 착탄 뒤 반격창")]
    public float endDuration  = 0.5f;
    public float recoveryDuration = 0.3f;

    [Header("M1 — 판정")]
    public float impactRadius     = 2f;
    public float damageMultiplier = 1.1f;
    public float knockbackMultiplier = 0.4f;
    [Tooltip("투사체가 그리는 호의 높이 (m)")]
    public float arcHeight = 1.5f;

    [Header("M1 — 움직임")]
    [Tooltip("시전하는 동안 옆으로 흘러가는 거리 (m)")]
    public float driftDistance = 2f;

    [Header("M1 — 난도 · 여운 (연출·UX 시나리오 §12-5 · §12-7)")]
    [Tooltip("마지막 탄은 플레이어가 움직이는 방향 앞을 노린다(비행 시간 × 이 배율만큼 앞). 0이면 자리")]
    public float lastBoltLead    = 0f;
    [Tooltip("앞을 노릴 때 최대 거리 (m)")]
    public float lastBoltLeadMax = 4f;
    [Tooltip("착탄 자리에 남는 여운 장판 반경 (m). 0이면 없음")]
    public float lingerRadius    = 0f;
    public float lingerSeconds   = 1.6f;
    [Tooltip("장판 한 틱 피해 배율")]
    public float lingerDamage    = 0.12f;

    [Header("M1⁺ — 악몽판(완전설계 §4-2): 착탄 뒤 좌우로 갈라지는 작은 탄")]
    [Tooltip("착탄마다 옆으로 갈라지는 작은 폭발 수(0이면 없음 · 악몽판 2)")]
    public int   splitCount    = 0;
    [Tooltip("착탄점에서 옆으로 (m) — 비행 방향의 수직")]
    public float splitDistance = 2.6f;
    public float splitRadius   = 1.3f;
    public float splitWarn     = 0.45f;
    public float splitDamage   = 0.5f;

    // ── 런타임 ───────────────────────────────────────────
    private LichMagicBoltState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new LichMagicBoltState(this);
    public override void OnRecycled()                       => _state = new LichMagicBoltState(this);

    public override bool CanExecute(BossPatternContext ctx)
    {
        if (ctx.Ctx.Runtime.PlayerTarget == null) return false;
        var lichBB = (ctx.Boss as LichMonster)?.LichBB;
        if (lichBB != null && lichBB.MagicBoltCooldown > 0f) return false;
        return Vector3.Distance(ctx.Ctx.Transform.position, ctx.Ctx.Runtime.PlayerTarget.position) <= maxRange;
    }

    public override SpecialStateBase GetRuntimeState() => _state;
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// LichMagicBoltState — 중단 불가(피해는 받음), 스크립트 이동
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class LichMagicBoltState : UnInterruptibleState<LichMagicBoltPatternSO>
{
    private enum Phase { Cast, Volley, End, Recovery }

    private struct Bolt
    {
        public GameObject Vfx;
        public GameObject Disc;
        public Vector3    From;
        public Vector3    To;
        public float      Time;
        public bool       Signaled;
    }

    private const float DiscFlashSeconds = 0.12f;
    private const float SignalSeconds    = 0.15f;
    private const float CrackScale       = 1.4f;   // 착탄 반경 대비 균열 지름
    private const float CrackSeconds     = 8f;

    private readonly List<Bolt> _bolts = new(4);

    private Phase      _phase;
    private float      _timer;
    private int        _fired;
    private GameObject _castFlare;
    private Vector3    _prevPlayer;
    private Vector3    _playerVel;     // 수평 속도(부드럽게) — 마지막 탄의 앞을 노릴 때

    public LichMagicBoltState(LichMagicBoltPatternSO data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        _phase      = Phase.Cast;
        _timer      = 0f;
        _fired      = 0;
        _playerVel  = Vector3.zero;
        _prevPlayer = LichPatternUtil.PlayerFloorPos(ctx);
        _bolts.Clear();

        ctx.Animator?.CrossFade("MagicBolt", 0.1f);

        var lich = LichPatternUtil.Lich(ctx);
        var mc   = lich?.MovementController;
        mc?.SetLocked(true);

        LichPatternUtil.FaceInstant(ctx, LichPatternUtil.PlayerFloorPos(ctx));

        // 시전 내내 옆으로 흘러간다 — 한자리에 선 포대가 아니라 움직이며 쏘는 시전자.
        if (mc != null && Data.driftDistance > 0f)
        {
            float total = Data.castDuration + Data.boltCount * Data.boltInterval + Data.endDuration;
            Vector3 side = ctx.Transform.right * (Random.value < 0.5f ? -1f : 1f);
            mc.ScriptMove(ctx.Transform.position + side * Data.driftDistance, total, 0f, facePlayer: true);
        }

        Transform hand = lich != null ? lich.CastPoint : ctx.Transform;
        _castFlare = LichVfx.PlayLoop(LichVfxSlot.CastFlare, hand.position, hand.rotation, 1f, hand);
        lich?.PulseBook(Data.castDuration);
        LichSfx.Play(LichSfxSlot.CastShort, hand.position);
    }

    public override void Update(MonsterContext ctx)
    {
        float dt = Time.deltaTime;
        _timer += dt;
        TickBolts(ctx, dt);
        TrackPlayer(ctx, dt);

        switch (_phase)
        {
            case Phase.Cast:
                if (_timer >= Data.castDuration)
                {
                    LichVfx.Stop(ref _castFlare, 0.3f);
                    _phase = Phase.Volley;
                    _timer = Data.boltInterval;   // 첫 발은 바로
                }
                break;

            case Phase.Volley:
                if (_fired < Data.boltCount && _timer >= Data.boltInterval)
                {
                    _timer = 0f;
                    Fire(ctx);
                }
                if (_fired >= Data.boltCount && _bolts.Count == 0)
                {
                    _phase = Phase.End;
                    _timer = 0f;
                }
                break;

            case Phase.End:
                if (_timer >= Data.endDuration)
                {
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
        LichVfx.Stop(ref _castFlare);
        for (int i = 0; i < _bolts.Count; i++)
        {
            var b = _bolts[i];
            LichVfx.Stop(ref b.Vfx);
            PatternGuideHelper.SafeDestroy(ref b.Disc);
        }
        _bolts.Clear();

        var lich = LichPatternUtil.Lich(ctx);
        lich?.MovementController?.SetLocked(false);
        if (lich?.LichBB != null)
            lich.LichBB.MagicBoltCooldown = Data.patternCooldown;
    }

    private void Fire(MonsterContext ctx)
    {
        _fired++;
        var lich = LichPatternUtil.Lich(ctx);
        Transform hand = lich != null ? lich.CastPoint : ctx.Transform;

        Vector3 target = LichPatternUtil.PlayerFloorPos(ctx);
        if (_fired == Data.boltCount && Data.lastBoltLead > 0f)
        {
            // 마지막 탄 — 달아나는 방향 앞. 제자리면 자리 그대로.
            Vector3 lead = Vector3.ClampMagnitude(_playerVel * (Data.flightTime * Data.lastBoltLead), Data.lastBoltLeadMax);
            var mc = LichPatternUtil.Mover(ctx);
            target = LichPatternUtil.OnFloor(ctx, mc != null ? mc.ClampToArena(target + lead) : target + lead);
        }
        LichPatternUtil.FaceInstant(ctx, target);
        ctx.Animator?.CrossFade("MagicBolt", 0.05f, 0, 0.35f);   // 연발마다 손을 다시 뻗는다

        Vector3 from = hand.position;
        Vector3 dir  = target - from;
        var rot = dir.sqrMagnitude > 0.001f ? Quaternion.LookRotation(dir) : ctx.Transform.rotation;

        LichVfx.Play(LichVfxSlot.BoltMuzzle, from, rot);
        LichSfx.Play(LichSfxSlot.BoltFire, from);
        _bolts.Add(new Bolt
        {
            Vfx  = LichVfx.PlayLoop(LichVfxSlot.BoltProjectile, from, rot),
            // 착탄 원은 비행 시간 동안 차오르고, 닿기 직전 빨강(예고 = 바닥 + 날아오는 탄).
            Disc = LichPatternUtil.PrepareTelegraph(
                PatternGuideHelper.Disc(target, Data.impactRadius, LichPatternUtil.Arcane), LichPatternUtil.Arcane),
            From = from,
            To   = target,
            Time = 0f,
        });
    }

    private void TickBolts(MonsterContext ctx, float dt)
    {
        for (int i = _bolts.Count - 1; i >= 0; i--)
        {
            var b = _bolts[i];
            b.Time += dt;
            float t = Mathf.Clamp01(b.Time / Mathf.Max(0.01f, Data.flightTime));
            LichPatternUtil.TickTelegraph(b.Disc, b.Time, Data.flightTime, SignalSeconds, ref b.Signaled);

            if (b.Vfx != null)
            {
                Vector3 pos  = Vector3.Lerp(b.From, b.To, t) + Vector3.up * (Data.arcHeight * 4f * t * (1f - t));
                Vector3 next = Vector3.Lerp(b.From, b.To, Mathf.Min(1f, t + 0.05f));
                b.Vfx.transform.position = pos;
                if ((next - pos).sqrMagnitude > 0.0001f)
                    b.Vfx.transform.rotation = Quaternion.LookRotation(next - pos);
            }

            if (t >= 1f)
            {
                Impact(ctx, b);
                _bolts.RemoveAt(i);
                continue;
            }
            _bolts[i] = b;
        }
    }

    private void Impact(MonsterContext ctx, Bolt b)
    {
        LichVfx.Stop(ref b.Vfx);
        LichVfx.Play(LichVfxSlot.BoltImpact, b.To, Quaternion.identity, Data.impactRadius / 2f);
        LichSfx.Play(LichSfxSlot.BoltImpact, b.To);
        LichCrack.Spawn(b.To, Data.impactRadius * CrackScale, CrackSeconds);

        PatternGuideHelper.SetColor(b.Disc, LichPatternUtil.Lethal);
        if (b.Disc != null) Object.Destroy(b.Disc, DiscFlashSeconds);

        bool hit = LichPatternUtil.HitCircle(ctx, b.To, Data.impactRadius, Data.damageMultiplier, Data.knockbackMultiplier);
        LichPatternUtil.Impact(LichImpact.Light, hit);
        if (Data.lingerRadius > 0f)
            LichHazards.LingerPool(ctx, b.To, Data.lingerRadius, Data.lingerSeconds, Data.lingerDamage);
        if (Data.splitCount > 0)
            Split(ctx, b);
    }

    /// <summary>M1⁺ — 착탄점에서 비행 방향의 좌우로 작은 탄이 갈라져 지연 폭발한다.</summary>
    private void Split(MonsterContext ctx, Bolt b)
    {
        Vector3 dir = b.To - b.From;
        dir.y = 0f;
        Vector3 side = dir.sqrMagnitude > 0.001f ? Vector3.Cross(Vector3.up, dir.normalized) : ctx.Transform.right;
        for (int i = 0; i < Data.splitCount; i++)
        {
            float sign = i % 2 == 0 ? 1f : -1f;
            float dist = Data.splitDistance * (1 + i / 2);
            Vector3 at = LichPatternUtil.OnFloor(ctx, b.To + side * (sign * dist));
            LichHazards.DelayedBlast(ctx, at, Data.splitRadius, Data.splitWarn, Data.splitDamage,
                                     LichVfxSlot.BoltImpact, LichPatternUtil.BoltScale(Data.splitRadius), crack: false);
        }
    }

    /// <summary>플레이어 수평 속도를 부드럽게 잰다(순간 회피 한 번에 끌려가지 않게).</summary>
    private void TrackPlayer(MonsterContext ctx, float dt)
    {
        if (dt <= 0f) return;
        Vector3 p   = LichPatternUtil.PlayerFloorPos(ctx);
        Vector3 v   = (p - _prevPlayer) / dt;
        v.y         = 0f;
        _playerVel  = Vector3.Lerp(_playerVel, v, Mathf.Clamp01(dt * 6f));
        _prevPlayer = p;
    }
}
}

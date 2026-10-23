using System.Collections.Generic;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 패턴보다 오래 남는 리치 장판·투사체(감속 장판·추적 구체·여운 장판·지연 폭발·사슬 결박). 패턴 상태는 끝나도 이것들은 제 수명을 산다.
/// 리치가 꺼지거나(풀 반환·씬 전환) 전투가 끝나면 <see cref="Clear"/>로 한꺼번에 거둔다.
/// </summary>
public static class LichHazards
{
    private const float BindCooldown = 4f;   // 결박이 풀린 뒤 다시 묶일 수 있을 때까지(연속 결박 방지)
    private const float ThreatMargin = 0.5f; // ThreatNear — 몸 반경 + 조금

    private static LichHazardHost s_host;
    private static BoundChainsHazard s_bound;
    private static ChainBindHazard s_bind;
    private static ElementCycleHazard s_cycle;
    private static float s_nextBindAt;

    private static LichHazardHost Host
    {
        get
        {
            if (s_host != null) return s_host;
            s_host = new GameObject("[LichHazards]").AddComponent<LichHazardHost>();
            return s_host;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        s_host       = null;
        s_bound      = null;
        s_bind       = null;
        s_cycle      = null;
        s_nextBindAt = 0f;
    }

    /// <summary>감속 장판 — 안에 있는 동안 이동 속도 × <paramref name="slowScale"/>.</summary>
    public static void SlowZone(MonsterContext ctx, Vector3 center, float radius, float seconds, float slowScale, LichVfxSlot vfx)
        => Host.Add(new SlowZoneHazard(ctx, center, radius, seconds, slowScale, vfx));

    /// <summary>느린 추적 구체 — 닿으면 한 번 터진다. 달리기로 뿌리칠 수 있는 속도로 쓴다.</summary>
    public static void HomingOrb(MonsterContext ctx, Vector3 from, float speed, float turnDegPerSec, float seconds,
                                 float radius, float damageMult, float knockbackMult)
        => Host.Add(new HomingOrbHazard(ctx, from, speed, turnDegPerSec, seconds, radius, damageMult, knockbackMult));

    public enum QuadrantElement { Fire, Ice, Lightning, Dark }

    /// <summary>
    /// 원소 순환 폭발(M8 원소 재편 · 10-22 E2 L2 안 A) 설정. 사분면 i = 방위 [BaseAngle + 90·i, +90) (도, +Z 기준 시계 방향).
    /// </summary>
    public struct CycleSettings
    {
        public Vector3           Center;
        public float             Radius;
        public float             BaseAngle;
        public QuadrantElement[] Elements;
        public float             Interval;            // 칸 하나가 터지는 간격(초) — 네 칸이면 × 4
        public float             Warn;                // 터질 칸이 차오르는 예고(초)
        public float             FireDamage;
        public float             FireGroundSeconds;
        public float             FireGroundDamage;    // 불 장판 한 틱(0.4초)
        public float             IceDamage;
        public float             IceSlowScale;
        public float             IceSlowSeconds;
        public float             BoltDamage;
        public float             DarkDamage;
        public float             DarkVignetteSeconds;
        public int               Waves;               // 일제 발동 횟수(10-22 E2 L2 2차)
        public float             SafeGap;             // 원 가장자리 사이 빈틈(m) — 좁을수록 「틈을 찾아 비키는」 회피
        public float             DiscRadius;          // 얼음 · 번개 · 어둠 원 반경(m) — 세 칸이 한 격자(10-23)
        public float             FireWedge;           // 불 줄기 폭(도)
        public float             FireGap;             // 불 줄기 사이 틈(도)
    }

    /// <summary>원소 일제 발동 — 네 칸의 원소 모양이 한꺼번에 터진다(Waves번 · 10-22 E2 L2 2차).</summary>
    public static void ElementCycle(MonsterContext ctx, CycleSettings settings)
    {
        s_cycle = new ElementCycleHazard(ctx, settings);
        Host.Add(s_cycle);
    }

    /// <summary>
    /// 봉인 사슬(T1 이후 봉인기 2페이지) — 제단 바깥 세 곳에서 리치까지 금빛 사슬 세 줄. 리치를 따라 매 프레임 다시 잇는다.
    /// 끝은 <see cref="Clear"/>(봉인 의식·전투 종료) 또는 <paramref name="seconds"/>(0 이하면 무기한).
    /// </summary>
    public static void BoundChains(Transform target, Vector3 center, float radius, float seconds = -1f)
    {
        s_bound = new BoundChainsHazard(target, center, radius, seconds);
        Host.Add(s_bound);
    }

    /// <summary>봉인 사슬 세 줄을 한꺼번에 팽팽하게(C3 사슬 끊기). 사슬이 없으면 아무 일도 없다.</summary>
    public static void PulseBoundChains() => s_bound?.PulseAll();

    /// <summary>
    /// 여운 장판 — <paramref name="tickSeconds"/>마다 소량 피해. 회피 무적(0.3초)으로는 못 버틴다 → 밖으로 나가야 한다.
    /// 첫 피해는 <paramref name="tickSeconds"/> 뒤(생기는 순간 서 있던 것만으로 맞지 않게).
    /// </summary>
    public static void LingerPool(MonsterContext ctx, Vector3 center, float radius, float seconds, float tickDamageMult,
                                  float tickSeconds = 0.4f)
        => Host.Add(new LingerPoolHazard(ctx, center, radius, seconds, tickDamageMult, tickSeconds));

    /// <summary>
    /// 한 줄로 이어 터지는 지연 폭발 묶음(낫의 대마법) — 묶음 안 폭발 하나에 닿으면 나머지는 그 플레이어를 치지 않는다.
    /// 원이 겹친 띠에 서 있으면 0.2초 간격으로 두 번 맞았다(10-08 점검).
    /// </summary>
    public sealed class BlastRow
    {
        internal bool Touched;
    }

    /// <summary>지연 폭발 — 판정 크기의 원이 <paramref name="warn"/>초 동안 차오른 뒤 터진다(마법 추격 · 결박 추격).</summary>
    public static void DelayedBlast(MonsterContext ctx, Vector3 center, float radius, float warn, float damageMult,
                                    LichVfxSlot impact, float impactScale = 1f, bool crack = true, BlastRow row = null,
                                    bool armed = false)
        => Host.Add(new DelayedBlastHazard(ctx, center, radius, warn, damageMult, impact, impactScale, crack, row, armed));

    /// <summary>
    /// 뻗어 나가는 광선 — 앞머리가 <paramref name="speed"/>(m/s)로 나아가며, 앞머리가 지난 자리에서만 맞는다.
    /// 광선 이펙트가 뻗는 속도와 맞춰 「아직 안 닿았는데 맞았다」를 없앤다(M2 불 광선, 09-18 감사).
    /// </summary>
    public static void BeamFront(MonsterContext ctx, Vector3 origin, Vector3 dir, float length, float halfWidth,
                                 float speed, float damageMult, float knockbackMult)
        => Host.Add(new BeamFrontHazard(ctx, origin, dir, length, halfWidth, speed, damageMult, knockbackMult));

    /// <summary>지금 사슬에 묶여 있는가.</summary>
    public static bool IsBinding => s_bind != null;

    /// <summary>원소 순환 폭발이 도는 중인가 — 그동안 리치는 성흔 탄막 · 비전 구체만 고른다(10-22 E2 L2 · LichMonster).</summary>
    public static bool ElementCycleActive => s_cycle != null;

    /// <summary>지금 묶을 수 있는가 — <see cref="ChainBind"/>와 같은 조건(묶여 있지 않고, 풀린 지 <see cref="BindCooldown"/>초가 지났다).</summary>
    public static bool CanBind => s_bind == null && Time.time >= s_nextBindAt;

    /// <summary>
    /// <paramref name="pos"/>에 선 채로 <paramref name="seconds"/>초 안에 남은 장판 · 투사체에 맞을 수 있는가 — 묶는 공격(속박탄)이
    /// 「묶인 채 피할 수 없이 맞는」 자리를 피한다. 여운 장판 안 · 곧 터질 지연 폭발 안 · 그 시간 안에 닿을 추적 구체 · 곧 터질 원소 칸.
    /// </summary>
    public static bool ThreatNear(Vector3 pos, float seconds) => s_host != null && s_host.AnyThreat(pos, seconds);

    /// <summary>
    /// 사슬 결박 — <paramref name="seconds"/> 동안 이동·회피·공격 불가(입력 차단). <paramref name="from"/>(리치 손)에서 금빛 사슬이 이어진다.
    /// 이미 묶여 있거나 풀린 지 <see cref="BindCooldown"/>초가 안 됐으면 묶지 않고 false.
    /// </summary>
    public static bool ChainBind(MonsterContext ctx, Transform from, float seconds)
    {
        if (s_bind != null || Time.time < s_nextBindAt) return false;
        var player = ctx.Runtime.CachedPlayer;
        if (player == null || IsDown(player)) return false;

        s_bind = new ChainBindHazard(player, from, seconds);
        Host.Add(s_bind);
        Debug.Log($"[Lich] 사슬 결박 — {seconds:0.0}초");
        return true;
    }

    private static bool IsDown(PlayerController player) => player.RuntimeStats != null && player.RuntimeStats.Hp <= 0;

    public static void Clear()
    {
        s_bound = null;
        s_cycle = null;
        if (s_host != null) s_host.ClearAll();
        LichDarkRainZone.ClearAll();   // 결계 폭격 장판도 함께 — 사망 · 페이지 전환 뒤에 남아 터졌다(10-08 점검)
    }

    // ── 장판·투사체 ─────────────────────────────────────────

    internal abstract class Hazard
    {
        /// <summary>false를 돌려주면 끝.</summary>
        public abstract bool Tick(float dt);
        public abstract void Dispose();
        /// <summary><paramref name="pos"/>가 <paramref name="seconds"/>초 안에 이것에 맞을 수 있는가(<see cref="ThreatNear"/>). 기본 = 아니다.</summary>
        public virtual bool Threatens(Vector3 pos, float seconds) => false;
    }

    private sealed class SlowZoneHazard : Hazard
    {
        private const float RefreshSeconds = 0.3f;

        private readonly MonsterContext _ctx;
        private readonly Vector3        _center;
        private readonly float          _radius;
        private readonly float          _slowScale;
        private float      _remaining;
        private GameObject _vfx;

        public SlowZoneHazard(MonsterContext ctx, Vector3 center, float radius, float seconds, float slowScale, LichVfxSlot vfx)
        {
            _ctx       = ctx;
            _center    = center;
            _radius    = radius;
            _slowScale = slowScale;
            _remaining = seconds;
            _vfx       = LichVfx.PlayLoop(vfx, center + Vector3.up * 0.03f, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), radius / 5f);
        }

        public override bool Tick(float dt)
        {
            _remaining -= dt;
            if (_remaining <= 0f) return false;

            var target = _ctx.Runtime.PlayerTarget;
            if (target != null && LichPatternUtil.FlatDistance(_center, target.position) <= _radius)
            {
                var player = _ctx.Runtime.CachedPlayer;
                if (player != null) player.ApplySlow(_slowScale, RefreshSeconds);
            }
            return true;
        }

        public override void Dispose() => LichVfx.Stop(ref _vfx, 0.5f);
    }

    private sealed class HomingOrbHazard : Hazard
    {
        private readonly MonsterContext _ctx;
        private readonly float          _speed;
        private readonly float          _turn;
        private readonly float          _radius;
        private readonly float          _damage;
        private readonly float          _knockback;
        private Vector3    _pos;
        private Vector3    _dir;
        private float      _remaining;
        private GameObject _vfx;
        private GameObject _marker;   // 바닥 판정 원 — 공중 구체가 어디를 치는지(09-18 감사: 예고 없음)

        public HomingOrbHazard(MonsterContext ctx, Vector3 from, float speed, float turn, float seconds,
                               float radius, float damage, float knockback)
        {
            _ctx       = ctx;
            _pos       = from;
            _speed     = speed;
            _turn      = turn;
            _radius    = radius;
            _damage    = damage;
            _knockback = knockback;
            _remaining = seconds;

            _dir = ctx.Transform.forward;
            var target = ctx.Runtime.PlayerTarget;
            if (target != null)
            {
                Vector3 to = target.position - from;
                to.y = 0f;
                if (to.sqrMagnitude > 0.001f) _dir = to.normalized;
            }
            _vfx = LichVfx.PlayLoop(LichVfxSlot.DarkOrb, _pos, Quaternion.LookRotation(_dir));
            _marker = PatternGuideHelper.Disc(LichPatternUtil.OnFloor(ctx, _pos), radius, LichPatternUtil.Arcane);
            PatternGuideHelper.SetFlow(_marker, LichPatternUtil.Arcane, 0.6f);
            PatternGuideHelper.SetProgress(_marker, 1f);
        }

        public override bool Tick(float dt)
        {
            _remaining -= dt;
            if (_remaining <= 0f)
            {
                LichVfx.Play(LichVfxSlot.DarkOrbImpact, _pos, Quaternion.identity, 0.6f);
                return false;
            }

            var target = _ctx.Runtime.PlayerTarget;
            if (target != null)
            {
                Vector3 to = target.position - _pos;
                to.y = 0f;
                if (to.sqrMagnitude > 0.001f)
                    _dir = Vector3.RotateTowards(_dir, to.normalized, _turn * Mathf.Deg2Rad * dt, 0f);
            }

            _pos += _dir * (_speed * dt);
            if (_vfx != null) _vfx.transform.SetPositionAndRotation(_pos, Quaternion.LookRotation(_dir));
            if (_marker != null) _marker.transform.position = LichPatternUtil.OnFloor(_ctx, _pos) + Vector3.up * 0.03f;

            // 무적이 아니던 플레이어에 닿으면 터진다 — 무효화 · 저스트 회피로 피해가 0이어도(매 프레임 다시 굴리지 않게, 10-08 점검).
            LichPatternUtil.HitCircle(_ctx, _pos, _radius, _damage, out bool contact, _knockback);
            if (contact)
            {
                LichVfx.Play(LichVfxSlot.DarkOrbImpact, _pos, Quaternion.identity, 0.35f);
                return false;
            }
            return true;
        }

        public override void Dispose()
        {
            LichVfx.Stop(ref _vfx, 0.3f);
            PatternGuideHelper.SafeDestroy(ref _marker);
        }

        public override bool Threatens(Vector3 pos, float seconds)
            => LichPatternUtil.FlatDistance(_pos, pos) <= _radius + _speed * seconds + ThreatMargin;
    }

    private sealed class BoundChainsHazard : Hazard
    {
        private const float Width      = 0.9f;    // 사슬 텍스처는 가운데 약 30%만 사슬 — 보이는 굵기 약 0.3 m
        private const float AnchorLift = 7f;
        private const float BodyLift   = 1.6f;
        private const float PulseEvery = 2.4f;    // 가끔 팽팽해지며 번쩍 — 「묶여 있다」

        // 숲 · 불 · 검 — 먼저 봉인한 세 보스의 빛깔을 금빛에 섞는다.
        private static readonly Color[] Tints =
        {
            new Color(0.75f, 0.85f, 0.35f),
            new Color(1.00f, 0.60f, 0.20f),
            new Color(0.95f, 0.90f, 0.70f),
        };

        private readonly Transform       _target;
        private readonly Vector3[]       _anchors = new Vector3[3];
        private readonly LichChainLine[] _links   = new LichChainLine[3];
        private readonly bool            _timed;
        private float      _remaining;
        private float      _pulseTimer;
        private int        _pulseIndex;
        private GameObject _wrap;

        public BoundChainsHazard(Transform target, Vector3 center, float radius, float seconds)
        {
            _target    = target;
            _timed     = seconds > 0f;
            _remaining = seconds;

            float baseYaw = target != null ? target.eulerAngles.y : 0f;
            for (int i = 0; i < 3; i++)
            {
                Vector3 dir = Quaternion.Euler(0f, baseYaw + 60f + i * 120f, 0f) * Vector3.forward;
                _anchors[i] = center + dir * (radius + 2f) + Vector3.up * AnchorLift;
                _links[i]   = LichChainLine.Create(_anchors[i], Body(), Width, Tints[i]);
                _links[i].Follow(null, Vector3.zero, target, Vector3.up * BodyLift);
            }

            // 몸에 감긴 금빛 사슬 고리(반복)
            if (target != null)
                _wrap = LichVfx.PlayLoop(LichVfxSlot.ChainGold, target.position + Vector3.up * BodyLift, Quaternion.identity, 1f, target);
        }

        public override bool Tick(float dt)
        {
            if (_target == null || !_target.gameObject.activeInHierarchy) return false;
            if (_timed && (_remaining -= dt) <= 0f) return false;

            _pulseTimer += dt;
            if (_pulseTimer >= PulseEvery)
            {
                _pulseTimer = 0f;
                var link = _links[_pulseIndex++ % 3];
                if (link != null)
                {
                    link.Tension(0.6f);
                    link.Flash(0.3f);
                }
            }
            return true;
        }

        public override void Dispose()
        {
            for (int i = 0; i < 3; i++)
            {
                if (_links[i] != null) _links[i].Dispose();
                _links[i] = null;
            }
            LichVfx.Stop(ref _wrap, 0.3f);
        }

        /// <summary>모든 사슬을 한꺼번에 당긴다(사슬 끊기 C3의 충격파 순간).</summary>
        public void PulseAll()
        {
            foreach (var link in _links)
            {
                if (link == null) continue;
                link.Tension(1f);
                link.Flash(0.35f);
            }
        }

        private Vector3 Body() => _target != null ? _target.position + Vector3.up * BodyLift : _anchors[0];
    }

    /// <summary>
    /// 원소 일제 발동(M8 원소 재편 · 10-22 E2 L2 2차 — 사용자 「4가지 한 번에 · 대신 가이드라인 · 틈이 넓으면 피하는 느낌이 안 난다」).
    /// 사분면 배치(옅은 원소 색 · 판정 없음)는 시전 때 정하고, 네 칸의 원소 모양 가이드가 <b>동시에</b> <see cref="CycleSettings.Warn"/>초 차오른 뒤
    /// 그 모양 자리만 터진다(예고 = 판정). 모양은 빽빽이 — 원끼리는 가장자리 사이 <see cref="CycleSettings.SafeGap"/>만 비고, 불 줄기 사이도 좁은 틈뿐이라
    /// 「틈을 찾아 비키는」 회피가 된다. 얼음 = 원 + 빙결 감속 · 번개 = 작은 원 · 어둠 = 큰 원 + 시야 축소 · 불 = 중심에서 번갈아 뻗는 부채꼴 줄기 + 불 장판(플레이어 쪽 줄기).
    /// 플레이어가 선 칸은 첫 모양이 지금 자리를 노린다. 한 번 발동에 한 번만 맞는다. <see cref="CycleSettings.Waves"/>번(간격 <see cref="CycleSettings.Interval"/>초) —
    /// 그사이 리치는 성흔 탄막 · 비전 구체를 잇는다(원소 재편 → 성흔 탄막 연계 · LichMonster 관문).
    /// </summary>
    private sealed class ElementCycleHazard : Hazard
    {
        private const int   QuadrantCount     = 4;
        private const float IdleGlow          = 0.35f;   // 배치 칸 — 옅게(판정 없음)
        private const float SignalSeconds     = 0.15f;
        private const float FlashSeconds      = 0.12f;
        private const float BurstSeconds      = 1f;
        private const float BurstFade         = 0.5f;
        private const float FireGroundRadius  = 1.5f;
        private const float FireGroundSpacing = 5f;
        private const float EdgeMargin        = 6f;      // 부채꼴 줄기는 칸 경계에서 이만큼(도) 안쪽부터
        private const int   MaxDiscs          = 72;      // 원 칸 셋 합 — 제단 반경 24 m · 원 r2.5면 35개 남짓
        private const float SeamSpill         = 0.6f;    // 불 칸 경계에서 원 반경 × 이만큼까지 이웃 원 칸이 넘어와 이음매 빈자리를 메운다

        private struct Shape
        {
            public QuadrantElement Element;
            public Vector3         Pos;      // 원 중심 · 줄기는 제단 중심
            public float           Radius;   // 원 반경 · 줄기는 반폭 각(도)
            public float           Yaw;      // 줄기 가운데 방위(도)
            public GameObject      Guide;
            public bool            Signaled;
        }

        private static readonly Color[] Tints =
        {
            new Color(1.00f, 0.58f, 0.12f),   // 불   (가산 셰이더라 어두운 색은 거의 안 보였다 — 실측) · 호박빛(10-23 — 붉은 주홍은 공격 색과 겹쳤다)
            new Color(0.25f, 0.65f, 0.95f),   // 얼음
            new Color(0.95f, 0.85f, 0.20f),   // 번개
            new Color(0.50f, 0.15f, 0.90f),   // 어둠
        };

        private static readonly LichVfxSlot[] Zones =
            { LichVfxSlot.ZoneFire, LichVfxSlot.ZoneIce, LichVfxSlot.ZoneLightning, LichVfxSlot.ZoneDark };

        private static readonly LichSfxSlot[] Sounds =
            { LichSfxSlot.FireBeam, LichSfxSlot.IceCast, LichSfxSlot.LightningCast, LichSfxSlot.DarkOrb };

        private readonly MonsterContext   _ctx;
        private readonly CycleSettings    _s;
        private readonly GameObject[]     _decals = new GameObject[QuadrantCount];
        private readonly GameObject[]     _burst  = new GameObject[QuadrantCount];
        private readonly List<Shape>      _shapes = new();
        private readonly List<Vector3>    _pack   = new();
        private readonly System.Predicate<Vector3> _packBlocked;
        private float _packRadius;
        private int   _wave;       // 터진 횟수
        private float _t;
        private float _burstLeft;
        private bool  _armed;      // 이번 발동 모양을 깔았다

        private int Waves => Mathf.Max(1, _s.Waves);

        public ElementCycleHazard(MonsterContext ctx, CycleSettings s)
        {
            _ctx = ctx;
            _s   = s;
            _packBlocked = p => DiscQuadrant(p) < 0;
            for (int q = 0; q < QuadrantCount; q++)
            {
                int e = (int)s.Elements[q];
                _decals[q] = PatternGuideHelper.Sector(s.Center, s.Radius, 90f, Mid(q), Tints[e]);
                PatternGuideHelper.SetFlow(_decals[q], Tints[e]);
                PatternGuideHelper.SetProgress(_decals[q], 1f);
                PatternGuideHelper.SetIntensity(_decals[q], IdleGlow);
            }
        }

        private float EruptAt(int wave) => _s.Warn + _s.Interval * wave;

        public override bool Tick(float dt)
        {
            _t += dt;
            TickBurst(dt);
            if (_wave >= Waves) return _burstLeft > 0f;   // 마지막 발동 이펙트가 끝날 때까지

            float eruptAt = EruptAt(_wave);
            float openAt  = eruptAt - _s.Warn;
            if (_t < openAt) return true;

            if (!_armed) Arm();
            for (int i = 0; i < _shapes.Count; i++)
            {
                var sh = _shapes[i];
                LichPatternUtil.TickTelegraph(sh.Guide, _t - openAt, _s.Warn, SignalSeconds, ref sh.Signaled);
                _shapes[i] = sh;
            }
            if (_t < eruptAt) return true;

            EruptAll();
            _wave++;
            _armed = false;
            if (_wave >= Waves)   // 마지막 발동 — 배치 칸도 거둔다
                for (int q = 0; q < QuadrantCount; q++) PatternGuideHelper.SafeDestroy(ref _decals[q]);
            return true;
        }

        public override void Dispose()
        {
            if (s_cycle == this) s_cycle = null;
            for (int i = 0; i < _decals.Length; i++) PatternGuideHelper.SafeDestroy(ref _decals[i]);
            for (int i = 0; i < _shapes.Count; i++)
            {
                var g = _shapes[i].Guide;
                PatternGuideHelper.SafeDestroy(ref g);
            }
            _shapes.Clear();
            StopBurst();
        }

        /// <summary>곧 터질 모양 — 속박탄이 「묶인 채 피할 수 없는」 자리에 묶지 않게.</summary>
        public override bool Threatens(Vector3 pos, float seconds)
        {
            if (!_armed || _wave >= Waves || EruptAt(_wave) - _t > seconds) return false;
            for (int i = 0; i < _shapes.Count; i++)
                if (Inside(_shapes[i], pos, ThreatMargin)) return true;
            return false;
        }

        /// <summary>이번 발동 모양을 깐다 — 네 칸 동시 · 플레이어가 선 칸은 첫 모양이 지금 자리.</summary>
        private void Arm()
        {
            _armed = true;
            _shapes.Clear();
            var  target = _ctx.Runtime.PlayerTarget;
            int  pq     = target != null ? QuadrantIndex(target.position) : -1;
            bool onFire = pq >= 0 && _s.Elements[pq] == QuadrantElement.Fire;
            for (int q = 0; q < QuadrantCount; q++)
                if (_s.Elements[q] == QuadrantElement.Fire) ArmFire(q, onFire ? target.position : (Vector3?)null);
            ArmDiscs(pq >= 0 && !onFire ? target.position : (Vector3?)null);
        }

        /// <summary>
        /// 불 — 중심에서 뻗는 부채꼴 줄기를 번갈아 깐다. 줄기 폭 · 틈은 줄기마다 무작위(폭 FireWedge × 0.8~1.3 · 틈 FireGap × 0.6~1.6)
        /// — 규칙적이면 외워 버려 지루하다(사용자). 플레이어 칸이면 첫 줄기가 플레이어 방위를 가운데로 덮고 양쪽으로 이어 깐다.
        /// </summary>
        private void ArmFire(int q, Vector3? aim)
        {
            float lo0  = _s.BaseAngle + 90f * q + EdgeMargin;
            float hi0  = _s.BaseAngle + 90f * q + 90f - EdgeMargin;
            if (aim.HasValue)
            {
                Vector3 d   = aim.Value - _s.Center;
                float   c   = lo0 + Mathf.Repeat(Mathf.DeltaAngle(lo0, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg), 360f);
                float   w   = WedgeWidth();
                AddWedge(c - w * 0.5f, c + w * 0.5f, lo0, hi0);
                for (float a = c + w * 0.5f + WedgeGap(); a < hi0; ) { float ww = WedgeWidth(); AddWedge(a, a + ww, lo0, hi0); a += ww + WedgeGap(); }
                for (float b = c - w * 0.5f - WedgeGap(); b > lo0; ) { float ww = WedgeWidth(); AddWedge(b - ww, b, lo0, hi0); b -= ww + WedgeGap(); }
                return;
            }
            for (float a = lo0 + Random.Range(0f, _s.FireGap); a < hi0; )
            {
                float ww = WedgeWidth();
                AddWedge(a, a + ww, lo0, hi0);
                a += ww + WedgeGap();
            }
        }

        private float WedgeWidth() => _s.FireWedge * Random.Range(0.8f, 1.3f);
        private float WedgeGap()   => _s.FireGap   * Random.Range(0.6f, 1.6f);

        private void AddWedge(float lo, float hi, float min, float max)
        {
            lo = Mathf.Max(lo, min);
            hi = Mathf.Min(hi, max);
            if (hi - lo < _s.FireWedge * 0.4f) return;   // 경계에 잘린 조각은 버린다
            float yaw   = (lo + hi) * 0.5f;
            float width = hi - lo;
            var guide = LichPatternUtil.PrepareTelegraph(
                PatternGuideHelper.Sector(_s.Center, _s.Radius, width, yaw, LichPatternUtil.Arcane), LichPatternUtil.Arcane);
            _shapes.Add(new Shape { Element = QuadrantElement.Fire, Pos = _s.Center, Radius = width * 0.5f, Yaw = yaw, Guide = guide });
        }

        /// <summary>
        /// 얼음 · 번개 · 어둠 — 세 칸을 한 흔든 육각 격자로 빽빽이(<see cref="LichPatternUtil.PackDiscs"/>). 격자 방향 · 간격(가장자리 사이 SafeGap × 0.8~1.6)은
        /// 발동마다, 자리는 원마다 조금씩 흔든다 — 틈이 좁고 매번 다르다(사용자). 원 크기는 발동마다 × 0.9~1.15.
        /// 칸마다 빈자리를 무작위로 찾던 방식은 칸 경계 · 가장자리에 큰 빈자리가 남았다(10-23 오프라인 기하 — 플레이어 8 m 안 반경 3.5 m).
        /// </summary>
        private void ArmDiscs(Vector3? aim)
        {
            _packRadius = _s.DiscRadius * Random.Range(0.9f, 1.15f);
            _pack.Clear();
            LichPatternUtil.PackDiscs(_pack, _s.Center, _s.Radius - _packRadius * 0.3f, _packRadius,
                                      new Vector2(_s.SafeGap * 0.8f, _s.SafeGap * 1.6f), MaxDiscs, aim, _packBlocked);
            for (int i = 0; i < _pack.Count; i++)
            {
                int q = DiscQuadrant(_pack[i]);
                if (q >= 0) AddDisc(_s.Elements[q], _pack[i], _packRadius);
            }
        }

        /// <summary>
        /// 원이 맡을 칸 — 원 칸이면 그 칸. 불 칸은 경계에서 원 반경 × <see cref="SeamSpill"/> 안쪽만 가까운 이웃 칸이 맡고(이음매 빈자리 메우기)
        /// 그보다 깊으면 -1(불 줄기 몫). 제단 밖도 -1.
        /// </summary>
        private int DiscQuadrant(Vector3 p)
        {
            int q = QuadrantIndex(p);
            if (q < 0 || _s.Elements[q] != QuadrantElement.Fire) return q;
            Vector3 d = p - _s.Center;
            d.y = 0f;
            float a     = Mathf.Repeat(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg - _s.BaseAngle - 90f * q, 360f);
            float toLo  = d.magnitude * Mathf.Sin(a * Mathf.Deg2Rad);
            float toHi  = d.magnitude * Mathf.Sin((90f - a) * Mathf.Deg2Rad);
            float spill = _packRadius * SeamSpill;
            if (toLo <= toHi) return toLo <= spill ? (q + QuadrantCount - 1) % QuadrantCount : -1;
            return toHi <= spill ? (q + 1) % QuadrantCount : -1;
        }

        private void AddDisc(QuadrantElement e, Vector3 p, float r)
        {
            p = LichPatternUtil.OnFloor(_ctx, p);
            var guide = LichPatternUtil.PrepareTelegraph(PatternGuideHelper.Disc(p, r, LichPatternUtil.Arcane), LichPatternUtil.Arcane);
            _shapes.Add(new Shape { Element = e, Pos = p, Radius = r, Guide = guide });
        }

        private bool Inside(Shape sh, Vector3 pos, float margin)
        {
            if (sh.Element != QuadrantElement.Fire)
                return LichPatternUtil.FlatDistance(sh.Pos, pos) <= sh.Radius + margin;
            Vector3 d = pos - _s.Center;
            d.y = 0f;
            if (d.magnitude > _s.Radius + margin) return false;
            float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            return Mathf.Abs(Mathf.DeltaAngle(sh.Yaw, yaw)) <= sh.Radius + Mathf.Rad2Deg * margin / Mathf.Max(1f, d.magnitude);
        }

        /// <summary>네 칸이 한꺼번에 터진다 — 모양마다 판정(한 번 발동에 한 번만 맞는다) · 원소 성질은 맞았을 때만.</summary>
        private void EruptAll()
        {
            var  target  = _ctx.Runtime.PlayerTarget;
            bool hitOnce = false;
            QuadrantElement hitBy = QuadrantElement.Fire;
            int  fireNearest = -1;
            float fireBest   = float.MaxValue;
            for (int i = 0; i < _shapes.Count; i++)
            {
                var sh = _shapes[i];
                if (!hitOnce && target != null && Inside(sh, target.position, 0f))
                {
                    float dmg = DamageOf(sh.Element);
                    hitOnce = sh.Element == QuadrantElement.Fire
                        ? LichPatternUtil.HitPlayer(_ctx, _s.Center, dmg)
                        : LichPatternUtil.HitCircle(_ctx, sh.Pos, sh.Radius, dmg);
                    if (hitOnce) hitBy = sh.Element;
                }
                if (sh.Element == QuadrantElement.Lightning)
                    LichVfx.Play(LichVfxSlot.LightningStrike, sh.Pos, Quaternion.identity, sh.Radius / 1.8f);
                if (sh.Element == QuadrantElement.Fire && target != null)
                {
                    Vector3 d = target.position - _s.Center;
                    float off = Mathf.Abs(Mathf.DeltaAngle(sh.Yaw, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg));
                    if (off < fireBest) { fireBest = off; fireNearest = i; }
                }
                var g = sh.Guide;
                FlashAndDrop(ref g);
            }
            if (fireNearest >= 0) SpawnFireGround(_shapes[fireNearest].Yaw);   // 불 장판은 플레이어 쪽 줄기 하나만(이펙트 절제)
            _shapes.Clear();

            StopBurst();
            for (int q = 0; q < QuadrantCount; q++)
            {
                var     e   = _s.Elements[q];
                Vector3 mid = _s.Center + Quaternion.Euler(0f, Mid(q), 0f) * Vector3.forward * (_s.Radius * 0.5f);
                _burst[q] = LichVfx.PlayLoop(Zones[(int)e], mid, Quaternion.Euler(0f, Mid(q), 0f), _s.Radius * 0.25f / 5f);
                LichSfx.Play(Sounds[(int)e], mid);
            }
            _burstLeft = Mathf.Max(BurstSeconds, _s.FireGroundSeconds);

            if (hitOnce && hitBy == QuadrantElement.Ice)
            {
                var player = _ctx.Runtime.CachedPlayer;
                if (player != null) player.ApplySlow(_s.IceSlowScale, _s.IceSlowSeconds);
            }
            if (hitOnce && hitBy == QuadrantElement.Dark) ThunderGroggyVignetteView.Trigger(_s.DarkVignetteSeconds);
            LichPatternUtil.Impact(LichImpact.Medium, hitOnce);
            Debug.Log($"[Lich] 원소 일제 발동 {_wave + 1}/{Waves} — 플레이어 {(hitOnce ? hitBy + "에 맞음" : "피함")}");
        }

        private float DamageOf(QuadrantElement e) => e switch
        {
            QuadrantElement.Fire      => _s.FireDamage,
            QuadrantElement.Ice       => _s.IceDamage,
            QuadrantElement.Lightning => _s.BoltDamage,
            _                         => _s.DarkDamage,
        };

        /// <summary>불 줄기 가운데 줄을 따라 불 장판(원소 포격 불길과 같은 크기 · 간격)이 잠깐 남는다.</summary>
        private void SpawnFireGround(float yaw)
        {
            if (_s.FireGroundSeconds <= 0f) return;
            Vector3 dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            for (float d = FireGroundRadius + 1f; d < _s.Radius; d += FireGroundSpacing)
                LingerPool(_ctx, LichPatternUtil.OnFloor(_ctx, _s.Center + dir * d), FireGroundRadius,
                           _s.FireGroundSeconds, _s.FireGroundDamage);
        }

        private void TickBurst(float dt)
        {
            if (_burstLeft <= 0f) return;
            _burstLeft -= dt;
            if (_burstLeft <= 0f) StopBurst();
        }

        private void StopBurst()
        {
            for (int k = 0; k < _burst.Length; k++) LichVfx.Stop(ref _burst[k], BurstFade);
        }

        /// <summary>판정 순간 빨강으로 번쩍이고 거둔다.</summary>
        private static void FlashAndDrop(ref GameObject guide)
        {
            if (guide == null) return;
            PatternGuideHelper.SetColor(guide, LichPatternUtil.Lethal);
            Object.Destroy(guide, FlashSeconds);
            guide = null;
        }

        private float Mid(int q) => _s.BaseAngle + 45f + 90f * q;

        /// <summary><paramref name="p"/>가 선 사분면(0~3). 제단 밖이면 -1.</summary>
        private int QuadrantIndex(Vector3 p)
        {
            Vector3 d = p - _s.Center;
            d.y = 0f;
            if (d.sqrMagnitude > _s.Radius * _s.Radius) return -1;
            float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            return Mathf.Clamp((int)(Mathf.Repeat(yaw - _s.BaseAngle, 360f) / 90f), 0, QuadrantCount - 1);
        }
    }

    private sealed class BeamFrontHazard : Hazard
    {
        private readonly MonsterContext _ctx;
        private readonly Vector3        _origin;
        private readonly Vector3        _dir;
        private readonly float          _length;
        private readonly float          _halfWidth;
        private readonly float          _speed;
        private readonly float          _damage;
        private readonly float          _knockback;
        private float _front;

        public BeamFrontHazard(MonsterContext ctx, Vector3 origin, Vector3 dir, float length, float halfWidth,
                               float speed, float damage, float knockback)
        {
            _ctx       = ctx;
            _origin    = origin;
            _dir       = dir;
            _length    = length;
            _halfWidth = halfWidth;
            _speed     = Mathf.Max(1f, speed);
            _damage    = damage;
            _knockback = knockback;
        }

        public override bool Tick(float dt)
        {
            _front = Mathf.Min(_length, _front + _speed * dt);
            bool damaged = LichPatternUtil.HitBeam(_ctx, _origin, _dir, _front, _halfWidth, _damage, out bool contact, _knockback);
            if (contact)
            {
                if (damaged) LichPatternUtil.Impact(LichImpact.Medium, true);
                return false;   // 한 번만 — 흘렸어도(무효화 · 저스트 회피) 이 광선은 쓰였다(10-08 점검)
            }
            return _front < _length;
        }

        public override void Dispose() { }
    }

    private sealed class LingerPoolHazard : Hazard
    {
        private const float FadeSeconds = 0.3f;

        private readonly MonsterContext _ctx;
        private readonly Vector3        _center;
        private readonly float          _radius;
        private readonly float          _damage;
        private readonly float          _interval;
        private float      _remaining;
        private float      _tick;
        private GameObject _vfx;
        private GameObject _rim;

        public LingerPoolHazard(MonsterContext ctx, Vector3 center, float radius, float seconds, float damage, float interval)
        {
            _ctx       = ctx;
            _center    = center;
            _radius    = radius;
            _damage    = damage;
            _interval  = Mathf.Max(0.35f, interval);   // 회피 무적(0.3초)보다 길게 — 한 번 피해도 다음 틱은 맞는다
            _remaining = seconds;
            _tick      = _interval;
            _vfx       = LichVfx.PlayLoop(LichVfxSlot.LingerPool, center + Vector3.up * 0.03f,
                                          Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), radius);
            // 판정 크기 그대로의 옅은 테두리 — 이펙트 모양과 상관없이 어디까지가 장판인지 보이게.
            _rim = PatternGuideHelper.Disc(center, radius, LichPatternUtil.Arcane);
            PatternGuideHelper.SetFlow(_rim, LichPatternUtil.Arcane, 0.35f);
            PatternGuideHelper.SetProgress(_rim, 1f);
        }

        public override bool Tick(float dt)
        {
            _remaining -= dt;
            if (_remaining <= 0f) return false;
            if (_remaining < FadeSeconds) PatternGuideHelper.SetIntensity(_rim, _remaining / FadeSeconds);

            _tick -= dt;
            if (_tick > 0f) return true;
            _tick += _interval;
            LichPatternUtil.HitCircle(_ctx, _center, _radius, _damage);
            return true;
        }

        public override void Dispose()
        {
            LichVfx.Stop(ref _vfx, 0.4f);
            PatternGuideHelper.SafeDestroy(ref _rim);
        }

        public override bool Threatens(Vector3 pos, float seconds)
            => LichPatternUtil.FlatDistance(_center, pos) <= _radius + ThreatMargin;
    }

    private sealed class DelayedBlastHazard : Hazard
    {
        private const float SignalSeconds = 0.15f;

        private readonly MonsterContext _ctx;
        private readonly Vector3        _center;
        private readonly float          _radius;
        private readonly float          _warn;
        private readonly float          _damage;
        private readonly LichVfxSlot    _impact;
        private readonly float          _impactScale;
        private readonly bool           _crack;
        private readonly BlastRow       _row;
        private readonly bool           _armed;   // 앞선 예고를 꽉 찬 빨강으로 이어받았다
        private float      _t;
        private bool       _signaled;
        private GameObject _disc;

        public DelayedBlastHazard(MonsterContext ctx, Vector3 center, float radius, float warn, float damage,
                                  LichVfxSlot impact, float impactScale, bool crack, BlastRow row, bool armed)
        {
            _row         = row;
            _ctx         = ctx;
            _center      = center;
            _radius      = radius;
            _warn        = Mathf.Max(0.3f, warn);
            _damage      = damage;
            _impact      = impact;
            _impactScale = impactScale;
            _crack       = crack;
            _disc        = LichPatternUtil.PrepareTelegraph(
                PatternGuideHelper.Disc(center, radius, LichPatternUtil.Arcane), LichPatternUtil.Arcane);
            _armed       = armed;
            if (armed)
            {
                // 앞선 예고(꽉 찬 빨강)를 이어받는다 — 비운 채 다시 차오르면 예고가 거꾸로 간다(10-13 검증 · 낫의 대마법)
                PatternGuideHelper.SetProgress(_disc, 1f);
                LichPatternUtil.TickTelegraph(_disc, _warn, _warn, SignalSeconds, ref _signaled);
            }
        }

        public override bool Tick(float dt)
        {
            _t += dt;
            LichPatternUtil.TickTelegraph(_disc, _armed ? _warn : _t, _warn, SignalSeconds, ref _signaled);   // 이어받은 예고는 꽉 찬 채
            if (_t < _warn) return true;

            LichVfx.Play(_impact, _center, Quaternion.identity, _impactScale);
            LichSfx.Play(LichSfxSlot.BoltImpact, _center);
            // 묶음에서 이미 닿았으면 판정하지 않는다 — 겹친 원에 두 번 맞지 않게(10-08 점검).
            bool damaged = false;
            if (_row == null || !_row.Touched)
            {
                damaged = LichPatternUtil.HitCircle(_ctx, _center, _radius, _damage, out bool contact);
                if (contact && _row != null) _row.Touched = true;
            }
            LichPatternUtil.Impact(LichImpact.Light, damaged);
            if (_crack) LichCrack.Spawn(_center, _radius * 1.6f, 6f);
            return false;
        }

        public override void Dispose() => PatternGuideHelper.SafeDestroy(ref _disc);

        public override bool Threatens(Vector3 pos, float seconds)
            => _warn - _t <= seconds && LichPatternUtil.FlatDistance(_center, pos) <= _radius + ThreatMargin;
    }

    /// <summary>
    /// 사슬 결박 — 입력을 막고(컷신 채널) 금빛 사슬로 리치 손과 잇는다. 끝나거나 거둬질 때 반드시 입력을 돌려준다.
    /// 묶는 동안 받는 추격은 호출한 패턴이 따로 건다(예고가 묶이는 순간 시작 · 풀린 뒤에 착탄).
    /// </summary>
    private sealed class ChainBindHazard : Hazard
    {
        private const float ChainWidth = 0.35f;
        private static readonly Vector3 ChestOffset = new Vector3(0f, 1.1f, 0f);

        private readonly PlayerController _player;
        private float         _remaining;
        private LichChainLine _chain;
        private GameObject    _ring;
        private bool          _released;

        public ChainBindHazard(PlayerController player, Transform from, float seconds)
        {
            _player    = player;
            _remaining = seconds;

            _player.SetInputEnabled(false);
            Vector3 feet = player.transform.position;
            _chain = LichChainLine.Create(from != null ? from.position : feet, feet + ChestOffset,
                                          ChainWidth, PatternGuideHelper.PlayerSeal);
            if (from != null) _chain.Follow(from, Vector3.zero, player.transform, ChestOffset);
            _chain.Tension();
            _ring = LichVfx.PlayLoop(LichVfxSlot.BindRing, feet + Vector3.up * 0.05f, Quaternion.identity);
            LichSfx.Play(LichSfxSlot.BindSnap, feet);
        }

        public override bool Tick(float dt)
        {
            _remaining -= dt;
            if (_player == null || IsDown(_player)) return false;
            if (_ring != null) _ring.transform.position = _player.transform.position + Vector3.up * 0.05f;
            return _remaining > 0f;
        }

        public override void Dispose()
        {
            if (_released) return;
            _released = true;
            if (_player != null) _player.SetInputEnabled(true);
            if (_chain != null)
            {
                _chain.Flash(0.15f);
                _chain.Dispose();
            }
            _chain = null;
            LichVfx.Stop(ref _ring, 0.3f);
            if (s_bind == this) s_bind = null;
            s_nextBindAt = Time.time + BindCooldown;
        }
    }
}

/// <summary><see cref="LichHazards"/>의 틱 담당. 씬에 하나, 씬과 함께 사라진다.</summary>
public sealed class LichHazardHost : MonoBehaviour
{
    private readonly List<LichHazards.Hazard> _hazards = new();

    private void Update()
    {
        float dt = Time.deltaTime;
        for (int i = _hazards.Count - 1; i >= 0; i--)
        {
            if (_hazards[i].Tick(dt)) continue;
            _hazards[i].Dispose();
            _hazards.RemoveAt(i);
        }
    }

    private void OnDestroy() => ClearAll();

    internal void Add(LichHazards.Hazard hazard) => _hazards.Add(hazard);

    internal bool AnyThreat(Vector3 pos, float seconds)
    {
        for (int i = 0; i < _hazards.Count; i++)
            if (_hazards[i].Threatens(pos, seconds)) return true;
        return false;
    }

    public void ClearAll()
    {
        for (int i = 0; i < _hazards.Count; i++) _hazards[i].Dispose();
        _hazards.Clear();
    }
}
}

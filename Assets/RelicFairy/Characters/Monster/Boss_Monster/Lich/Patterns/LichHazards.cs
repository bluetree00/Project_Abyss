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

    /// <summary>원소 재편(M8) 사분면 설정. 사분면 i = 방위 [BaseAngle + 90·i, +90) (도, +Z 기준 시계 방향).</summary>
    public struct QuadrantSettings
    {
        public Vector3           Center;
        public float             Radius;
        public float             BaseAngle;
        public QuadrantElement[] Elements;
        public float             Seconds;
        public float             FireDps;
        public float             IceSlowScale;
        public float             ChainWarn;
        public float             ChainRadius;
        public float             ChainDamage;
        public float             ChainCooldown;
        public float             DarkVignette;
    }

    /// <summary>원소 사분면 — 바닥 색으로 상시 표시, 플레이어가 선 사분면의 성질을 입힌다.</summary>
    public static void ElementQuadrants(MonsterContext ctx, QuadrantSettings settings)
        => Host.Add(new QuadrantHazard(ctx, settings));

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

    /// <summary>지연 폭발 — 판정 크기의 원이 <paramref name="warn"/>초 동안 차오른 뒤 터진다(마법 추격 · 결박 추격).</summary>
    public static void DelayedBlast(MonsterContext ctx, Vector3 center, float radius, float warn, float damageMult,
                                    LichVfxSlot impact, float impactScale = 1f, bool crack = true)
        => Host.Add(new DelayedBlastHazard(ctx, center, radius, warn, damageMult, impact, impactScale, crack));

    /// <summary>
    /// 뻗어 나가는 광선 — 앞머리가 <paramref name="speed"/>(m/s)로 나아가며, 앞머리가 지난 자리에서만 맞는다.
    /// 광선 이펙트가 뻗는 속도와 맞춰 「아직 안 닿았는데 맞았다」를 없앤다(M2 불 광선, 09-18 감사).
    /// </summary>
    public static void BeamFront(MonsterContext ctx, Vector3 origin, Vector3 dir, float length, float halfWidth,
                                 float speed, float damageMult, float knockbackMult)
        => Host.Add(new BeamFrontHazard(ctx, origin, dir, length, halfWidth, speed, damageMult, knockbackMult));

    /// <summary>지금 사슬에 묶여 있는가.</summary>
    public static bool IsBinding => s_bind != null;

    /// <summary>지금 묶을 수 있는가 — <see cref="ChainBind"/>와 같은 조건(묶여 있지 않고, 풀린 지 <see cref="BindCooldown"/>초가 지났다).</summary>
    public static bool CanBind => s_bind == null && Time.time >= s_nextBindAt;

    /// <summary>
    /// <paramref name="pos"/>에 선 채로 <paramref name="seconds"/>초 안에 남은 장판 · 투사체에 맞을 수 있는가 — 묶는 공격(속박탄)이
    /// 「묶인 채 피할 수 없이 맞는」 자리를 피한다. 여운 장판 안 · 곧 터질 지연 폭발 안 · 그 시간 안에 닿을 추적 구체 · 불/번개 사분면.
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
        if (s_host != null) s_host.ClearAll();
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

            if (LichPatternUtil.HitCircle(_ctx, _pos, _radius, _damage, _knockback))
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

    private sealed class QuadrantHazard : Hazard
    {
        private const float FireTick       = 1f;
        private const int   VfxPerQuadrant = 2;
        private const float FillSeconds    = 2f;
        private static readonly Color FireTickTint = new Color(1f, 0.45f, 0.15f, 1f);

        private static readonly Color[] Tints =
        {
            new Color(0.95f, 0.30f, 0.08f),   // 불   (가산 셰이더라 어두운 색은 거의 안 보였다 — 실측)
            new Color(0.25f, 0.65f, 0.95f),   // 얼음
            new Color(0.95f, 0.85f, 0.20f),   // 번개
            new Color(0.50f, 0.15f, 0.90f),   // 어둠
        };

        private static readonly LichVfxSlot[] Zones =
            { LichVfxSlot.ZoneFire, LichVfxSlot.ZoneIce, LichVfxSlot.ZoneLightning, LichVfxSlot.ZoneDark };

        private readonly MonsterContext   _ctx;
        private readonly QuadrantSettings _s;
        private readonly GameObject[]     _decals = new GameObject[4];
        private readonly GameObject[]     _vfx    = new GameObject[4 * VfxPerQuadrant];
        private PlayerController _player;
        private float      _remaining;
        private float      _age;
        private float      _fireTimer;
        private float      _chainCooldown;
        private float      _chainTimer = -1f;
        private Vector3    _chainPos;
        private GameObject _chainDisc;

        public QuadrantHazard(MonsterContext ctx, QuadrantSettings s)
        {
            _ctx       = ctx;
            _s         = s;
            _remaining = s.Seconds;

            for (int q = 0; q < 4; q++)
            {
                int   e   = (int)s.Elements[q];
                float mid = s.BaseAngle + 45f + 90f * q;
                _decals[q] = LichPatternUtil.PrepareTelegraph(
                    PatternGuideHelper.Sector(s.Center, s.Radius, 90f, mid, Tints[e]), Tints[e]);

                Vector3 dir = Quaternion.Euler(0f, mid, 0f) * Vector3.forward;
                for (int k = 0; k < VfxPerQuadrant; k++)
                {
                    Vector3 p = s.Center + dir * (s.Radius * (0.3f + 0.35f * k));
                    _vfx[q * VfxPerQuadrant + k] = LichVfx.PlayLoop(Zones[e], p, Quaternion.Euler(0f, mid, 0f), s.Radius * 0.25f / 5f);
                }
            }

            _player = ctx.Runtime.CachedPlayer;
            if (_player != null) _player.OnDamageTaken += OnPlayerDamaged;
        }

        public override bool Tick(float dt)
        {
            _remaining -= dt;
            if (_remaining <= 0f) return false;
            if (_age < FillSeconds)
            {
                _age += dt;
                for (int i = 0; i < _decals.Length; i++)
                    PatternGuideHelper.SetProgress(_decals[i], _age / FillSeconds);
            }
            if (_chainCooldown > 0f) _chainCooldown -= dt;
            TickChain(dt);

            // 바닥이 다 채워지기 전엔 성질을 입히지 않는다 — 어느 사분면이 불인지 보이기 전에 타던 것(09-18 감사).
            if (_age < FillSeconds) return true;

            switch (PlayerQuadrant(out var target))
            {
                case QuadrantElement.Fire:
                    _fireTimer += dt;
                    if (_fireTimer >= FireTick)
                    {
                        _fireTimer = 0f;
                        if (LichPatternUtil.HitCircle(_ctx, target, 1f, _s.FireDps))
                            LichVfx.PlayTinted(LichVfxSlot.BoltImpact, target, Quaternion.identity, 0.35f, FireTickTint);
                    }
                    break;

                case QuadrantElement.Ice:
                    _fireTimer = 0f;
                    if (_player != null) _player.ApplySlow(_s.IceSlowScale, 0.3f);
                    break;

                case QuadrantElement.Dark:
                    _fireTimer = 0f;
                    ThunderGroggyVignetteView.Trigger(_s.DarkVignette);
                    break;

                default:
                    _fireTimer = 0f;
                    break;
            }
            return true;
        }

        public override void Dispose()
        {
            if (_player != null) _player.OnDamageTaken -= OnPlayerDamaged;
            _player = null;
            for (int i = 0; i < _decals.Length; i++) PatternGuideHelper.SafeDestroy(ref _decals[i]);
            for (int i = 0; i < _vfx.Length; i++) LichVfx.Stop(ref _vfx[i], 1f);
            PatternGuideHelper.SafeDestroy(ref _chainDisc);
        }

        /// <summary>불 사분면(틱) · 번개 사분면(맞으면 낙뢰가 따라온다) · 곧 떨어질 낙뢰 자리.</summary>
        public override bool Threatens(Vector3 pos, float seconds)
        {
            if (_chainTimer >= 0f && LichPatternUtil.FlatDistance(_chainPos, pos) <= _s.ChainRadius + ThreatMargin) return true;
            var q = QuadrantAt(pos);
            return q == QuadrantElement.Fire || q == QuadrantElement.Lightning;
        }

        /// <summary>플레이어가 선 사분면. 제단 밖이면 -1(원소 없음).</summary>
        private QuadrantElement PlayerQuadrant(out Vector3 target)
        {
            target = default;
            var t = _ctx.Runtime.PlayerTarget;
            if (t == null) return (QuadrantElement)(-1);
            target = t.position;
            return QuadrantAt(target);
        }

        /// <summary><paramref name="p"/>가 선 사분면. 제단 밖이면 -1(원소 없음).</summary>
        private QuadrantElement QuadrantAt(Vector3 p)
        {
            Vector3 d = p - _s.Center;
            d.y = 0f;
            if (d.sqrMagnitude > _s.Radius * _s.Radius) return (QuadrantElement)(-1);

            float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            int   q   = Mathf.Clamp((int)(Mathf.Repeat(yaw - _s.BaseAngle, 360f) / 90f), 0, 3);
            return _s.Elements[q];
        }

        /// <summary>번개 사분면에서 맞으면 낙뢰가 한 번 더 따라온다(재발동 대기 있음 — 낙뢰 자신이 다시 부르지 않게).</summary>
        private void OnPlayerDamaged()
        {
            if (_chainTimer >= 0f || _chainCooldown > 0f) return;
            if (PlayerQuadrant(out var target) != QuadrantElement.Lightning) return;

            _chainCooldown = _s.ChainCooldown;
            _chainTimer    = _s.ChainWarn;
            _chainPos      = LichPatternUtil.OnFloor(_ctx, target);
            _chainDisc     = PatternGuideHelper.Disc(_chainPos, _s.ChainRadius, LichPatternUtil.Arcane);
        }

        private void TickChain(float dt)
        {
            if (_chainTimer < 0f) return;
            _chainTimer -= dt;
            if (_chainTimer > 0f) return;

            _chainTimer = -1f;
            PatternGuideHelper.SafeDestroy(ref _chainDisc);
            LichVfx.Play(LichVfxSlot.LightningStrike, _chainPos, Quaternion.identity, _s.ChainRadius / 1.8f);
            LichPatternUtil.HitCircle(_ctx, _chainPos, _s.ChainRadius, _s.ChainDamage);
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
            if (LichPatternUtil.HitBeam(_ctx, _origin, _dir, _front, _halfWidth, _damage, _knockback))
            {
                LichPatternUtil.Impact(LichImpact.Medium, true);
                return false;   // 한 번만
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
        private float      _t;
        private bool       _signaled;
        private GameObject _disc;

        public DelayedBlastHazard(MonsterContext ctx, Vector3 center, float radius, float warn, float damage,
                                  LichVfxSlot impact, float impactScale, bool crack)
        {
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
        }

        public override bool Tick(float dt)
        {
            _t += dt;
            LichPatternUtil.TickTelegraph(_disc, _t, _warn, SignalSeconds, ref _signaled);
            if (_t < _warn) return true;

            LichVfx.Play(_impact, _center, Quaternion.identity, _impactScale);
            LichSfx.Play(LichSfxSlot.BoltImpact, _center);
            LichPatternUtil.Impact(LichImpact.Light, LichPatternUtil.HitCircle(_ctx, _center, _radius, _damage));
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

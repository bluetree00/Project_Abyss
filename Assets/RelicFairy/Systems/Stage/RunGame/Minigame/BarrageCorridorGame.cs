using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using UnityEngine;

/// <summary>
/// 탄막의 회랑 — 피하기(20초, 10-01 설계 §3-1). 「맞지 마라」.
/// 물결이 일정 간격으로 온다 — 낙뢰 셋 · 가르는 빛 · 조이는 고리(틈으로 빠져나가기) · 쫓는 구체. 같은 물결이 연달아 안 나온다.
/// 마지막 5초 = 최후의 물결(간격 ×0.7). 등급 = 맞은 횟수 0 / 1 / 2~3 / 4~5 / 6 이상.
/// 10-29 40 → 20초(「빨리 끝나는 이벤트」). 보상 = 등급만큼 정수(EventRewards).
/// 10-29 사용자 「압도적으로 많게 사방에서 오지만 탄속은 조금 느리게」 — 놀이의 몸통은 <b>느린 탄</b>이다.
///   · 흘림: 판 가장자리 아무 데서나 0.24~0.18초마다 한 발, 플레이어 둘레 2.5 m 안 아무 데를 향해 — 사방에서 온다
///   · 꽃잎: 3.2초마다 가장자리 둘레에서 16~22발이 한꺼번에 가운데로 — 한 화면에 50발 남짓
///   · 탄속 2.6~3.1 m/s(예전 쫓는 구체 3.0~3.6보다 조금 느리게) · 막 생긴 탄은 0.35초 동안 커지며 맞히지 않는다
///   · 맞은 뒤 1초(예전 0.6)는 닿은 탄이 그냥 사라진다 — 빽빽한 탄에 한꺼번에 여러 번 맞지 않게
///   · 낙뢰 · 가르는 빛 · 고리 · 쫓는 구체는 양념으로 4.8~4.2초마다(예전 3.0~2.7) · 쫓는 구체도 탄과 같은 빠르기
///   · 판이 좁으면(방 자리가 막혔다) 탄 수를 줄인다
/// 판정은 자체 거리 계산(원 · 띠 · 고리 · 구체). 회피 무적 중엔 맞지 않는다(대시로 뚫고 지나가도 된다).
/// </summary>
public sealed class BarrageCorridorGame : EventMinigame
{
    // ── Constants ──────────────────────────────────────────────
    private const float PlayTime     = 20f;
    private const float FinalStretch = 5f;
    private const float FinalFactor  = 0.7f;
    private const float HitCooldown  = 1.0f;   // 연속 피격 방지 — 10-29 0.6 → 1.0(탄이 빽빽해졌다)
    private const float NearMargin   = 0.6f;   // 이 안을 스쳤는데 안 맞으면 「간발!」

    private const float BoltRadius   = 2.2f;
    private const float BoltWarn     = 0.9f;
    private const float SweepWidth   = 2.4f;
    private const float SweepWarn    = 1.0f;
    private const float SweepActive  = 0.4f;
    private const float RingWarn     = 0.8f;
    private const float RingClose    = 3.0f;
    private const float RingGap      = 60f;
    private const float RingBand     = 0.45f;
    private const float OrbLife      = 6f;
    private const float OrbRadius    = 0.45f;
    private const float OrbHitDist   = 0.8f;

    // 10-29 느린 탄(「압도적으로 많게 사방에서 · 탄속은 조금 느리게」)
    private const float BulletRadius  = 0.28f;
    private const float BulletHitDist = 0.55f;   // 플레이어 중심까지 평면 거리(탄 0.28 + 몸 약 0.27)
    private const float BulletHeight  = 0.9f;
    private const float BulletFadeIn  = 0.35f;   // 막 생긴 탄은 이만큼 커지는 동안 맞히지 않는다(가장자리에 선 사람 몫)
    private const float StreamSpread  = 2.5f;    // 흘림이 노리는 곳 = 플레이어 둘레 이만큼 안 아무 데
    private const float BloomGap      = 3.2f;

    private enum Wave { Bolts, Sweep, Ring, Orb }
    private static readonly Wave[] AllWaves = { Wave.Bolts, Wave.Sweep, Wave.Ring, Wave.Orb };

    // ── Private ────────────────────────────────────────────────
    private readonly List<Hazard> _hazards = new();
    private readonly Stack<BulletHazard> _bulletPool = new(64);   // 다 쓴 탄 — 한 판에 수백 발을 만들고 부수지 않게 되쓴다
    private float _nextWave, _nextStream, _nextBloom;
    private Wave  _lastWave = (Wave)(-1);
    private int   _hits, _nearMisses, _demoHitParity;
    private float _hitCooldown;

    // ── Properties ─────────────────────────────────────────────
    public override string Id => "barrage";
    protected override string Title => "탄막의 회랑";
    protected override string Rule => "맞지 마라";
    protected override string GradeHint => "안 맞으면 플래티넘 · 1번 골드 · 3번까지 실버 · 5번까지 브론즈";
    protected override float Duration => PlayTime;
    protected override float WantRadius => 9f;
    public override ChallengeGrade CurrentGrade => GradeOf(_hits);

    protected override string StatusLine
    {
        get
        {
            var g = CurrentGrade;
            if (g == ChallengeGrade.Fail) return $"맞은 횟수 {_hits}";
            int slack = SlackBeforeDrop(_hits, 0, 1, 3, 5);
            return $"맞은 횟수 {_hits} · {slack}번 더 맞으면 {MinigameHud.GradeName(g - 1)}";
        }
    }

    protected override string ResultLine => _hits == 0 ? $"한 번도 안 맞았다 · 간발 {_nearMisses}" : $"맞은 횟수 {_hits} · 간발 {_nearMisses}";

    private float Interval    => MinigameTheme.Pick(Tier, 4.8f, 4.6f, 4.4f, 4.2f) * (Remaining <= FinalStretch ? FinalFactor : 1f);
    private float BulletSpeed => MinigameTheme.Pick(Tier, 2.6f, 2.75f, 2.9f, 3.1f);
    private float StreamGap   => MinigameTheme.Pick(Tier, 0.24f, 0.22f, 0.20f, 0.18f) / Fill * (Remaining <= FinalStretch ? FinalFactor : 1f);
    private int   BloomCount  => Mathf.Max(8, Mathf.RoundToInt(MinigameTheme.Pick(Tier, 16f, 18f, 20f, 22f) * Fill));
    /// <summary>판이 좁으면 탄 수를 줄인다 — 같은 수면 좁은 판이 더 빽빽하다.</summary>
    private float Fill        => Mathf.Clamp(Radius / WantRadius, 0.6f, 1f);

    // ── Public Methods ─────────────────────────────────────────

    /// <summary>맞은 횟수 → 등급(0 / 1 / 2~3 / 4~5 / 6 이상).</summary>
    public static ChallengeGrade GradeOf(int hits) => GradeByMax(hits, 0, 1, 3, 5);

    // ── Stage ──────────────────────────────────────────────────

    protected override async UniTask BuildStageAsync(CancellationToken ct)
    {
        // 놀이판 테두리 — 옅은 고리가 바닥에서 번지듯 커진다
        var edge = MinigameFx.CreateRing(new Color(Theme.r, Theme.g, Theme.b, 0.3f), 0.1f);
        Own(edge.gameObject);
        float t = 0f;
        while (t < 1f)
        {
            t = Mathf.Min(1f, t + Time.deltaTime / 0.6f);
            MinigameFx.SetRing(edge, Center, Radius * (0.2f + 0.8f * (1f - (1f - t) * (1f - t))), 0f, 0f);
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }
        RunFx.Play(RunFxSlot.Ring, Center, 1.2f, Theme);
    }

    protected override void OnPlayStart()
    {
        _nextStream = 0.3f;
        _nextBloom  = 1.5f;
        _nextWave   = 1.2f;
    }

    protected override void TickPlay(float dt)
    {
        _hitCooldown -= dt;

        _nextWave -= dt;
        if (_nextWave <= 0f)
        {
            SpawnWave(PickWave());
            _nextWave = Interval;
        }

        _nextStream -= dt;
        if (_nextStream <= 0f)
        {
            SpawnStream();
            _nextStream = StreamGap;
        }

        _nextBloom -= dt;
        if (_nextBloom <= 0f)
        {
            SpawnBloom();
            _nextBloom = BloomGap;
        }

        for (int i = _hazards.Count - 1; i >= 0; i--)
        {
            var h = _hazards[i];
            h.Tick(dt);
            if (h.Done)
            {
                h.Clear();
                _hazards.RemoveAt(i);
            }
        }
    }

    protected override void OnPlayEnd()
    {
        // 남은 위험은 조용히 걷는다 — 위험마다 고리를 내면 끝나는 순간 같은 뜻의 고리가 여럿 겹쳤다(10-11 이펙트 톤 2차 V-14 · 결과는 HUD가 알린다)
        for (int i = 0; i < _hazards.Count; i++) _hazards[i].Clear();
        _hazards.Clear();
        // 무피격 빛기둥은 플레이어 발밑이라 작게(0.08 = 충격파 지름 약 4 m → 0.05, 「살짝」 상한 — V-4)
        if (_hits == 0 && PlayerT != null)
            RunFx.Play(RunFxSlot.Pillar, PlayerT.position, 0.05f, MinigameHud.GradeColor(ChallengeGrade.Platinum));
    }

    protected override void DemoTick(MinigameDemo mode, float dt) { }   // 시연은 판정 쪽(Absorb)에서 흉내 낸다

    /// <summary>10-29 특별 보상 — 버틴 성적(등급)만큼 정수.</summary>
    protected override void GrantReward(ChallengeGrade grade, Vector3 at)
    {
        int essence = EventRewards.Essence(grade, EventRewards.Chapter(Run));
        EventRewards.DropEssence(at, essence);
        EventRewards.Notice($"탄막의 회랑 — 정수 <color=#73D9C7>+{essence}</color>");
    }

    // ── Private Methods ────────────────────────────────────────

    private Wave PickWave()
    {
        Wave w;
        do { w = AllWaves[Rng.Next(AllWaves.Length)]; } while (w == _lastWave);
        _lastWave = w;
        return w;
    }

    private void SpawnWave(Wave w)
    {
        Vector3 p = PlayerPos();
        switch (w)
        {
            case Wave.Bolts:
                _hazards.Add(new BoltHazard(this, ClampToArena(p)));
                _hazards.Add(new BoltHazard(this, ClampToArena(p + RandomFlat(2.6f, 4.5f))));
                _hazards.Add(new BoltHazard(this, ClampToArena(p + RandomFlat(2.6f, 4.5f))));
                break;
            case Wave.Sweep:
            {
                float yaw = (float)Rng.NextDouble() * 180f;
                Vector3 through = p + RandomFlat(0f, 1.5f);
                _hazards.Add(new SweepHazard(this, through, yaw));
                if (Tier >= 2) _hazards.Add(new SweepHazard(this, through, yaw + 90f));   // 뒤 챕터는 십자
                break;
            }
            case Wave.Ring:
                _hazards.Add(new RingHazard(this, (float)Rng.NextDouble() * 360f));
                break;
            case Wave.Orb:
            {
                int n = Tier >= 2 ? 2 : 1;
                for (int i = 0; i < n; i++)
                {
                    Vector3 away = Center - (p - Center).normalized * Radius * 0.9f;
                    away += RandomFlat(0f, 2f);
                    _hazards.Add(new OrbHazard(this, ClampToArena(away)));
                }
                break;
            }
        }
    }

    /// <summary>흘림 — 가장자리 아무 데서나 한 발, 플레이어 언저리를 향해(사방에서 온다).</summary>
    private void SpawnStream()
    {
        Vector3 from   = RimPoint((float)Rng.NextDouble() * 360f);
        Vector3 dir    = PlayerPos() + RandomFlat(0f, StreamSpread) - from; dir.y = 0f;
        Vector3 inward = Center - from; inward.y = 0f;
        // 플레이어가 그 가장자리에 붙어 서 있으면 바깥으로 나가는 탄이 된다 — 그땐 가운데로
        if (dir.sqrMagnitude < 0.01f || Vector3.Dot(dir, inward) <= 0f) dir = inward;
        LaunchBullet(from, dir.normalized);
    }

    /// <summary>꽃잎 — 가장자리 둘레에서 한꺼번에 가운데로. 탄 사이 틈은 가장자리 쪽이 넓다.</summary>
    private void SpawnBloom()
    {
        int   n    = BloomCount;
        float yaw0 = (float)Rng.NextDouble() * 360f;
        for (int i = 0; i < n; i++)
        {
            Vector3 from = RimPoint(yaw0 + i * 360f / n);
            Vector3 dir  = Center - from; dir.y = 0f;
            LaunchBullet(from, dir.normalized);
        }
    }

    private void LaunchBullet(Vector3 from, Vector3 dir)
    {
        var b = _bulletPool.Count > 0 ? _bulletPool.Pop() : new BulletHazard(this);
        b.Launch(from, dir, BulletSpeed);
        _hazards.Add(b);
    }

    private void ReturnBullet(BulletHazard b) => _bulletPool.Push(b);

    private Vector3 RimPoint(float yaw) => Center + Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * Radius;

    /// <summary>맞음 한 번(쿨다운 · 회피 무적 · 시연 흡수 반영). 실제로 셌으면 true.</summary>
    private bool TryHit()
    {
        if (_hitCooldown > 0f) return false;
        if (Absorb()) { NearMiss(); return false; }
        if (!HurtPlayer()) return false;
        _hits++;
        _hitCooldown = HitCooldown;
        return true;
    }

    private void NearMiss()
    {
        _nearMisses++;
        Hud?.Float("간발!", Color.white);
        MinigameFx.Tick(1.5f);
    }

    /// <summary>에디터 시연 — 완벽은 다 피한 셈, 보통은 절반을 피한 셈.</summary>
    private bool Absorb() => DebugDemo switch
    {
        MinigameDemo.Perfect => true,
        MinigameDemo.Normal  => (_demoHitParity++ & 1) == 0,
        _                    => false,
    };

    private Vector3 PlayerPos() => PlayerT != null ? PlayerT.position : Center;

    private Vector3 RandomFlat(float minR, float maxR)
    {
        float yaw = (float)Rng.NextDouble() * 360f;
        float r   = Mathf.Lerp(minR, maxR, (float)Rng.NextDouble());
        return Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * r;
    }

    private Vector3 ClampToArena(Vector3 p)
    {
        Vector3 d = p - Center; d.y = 0f;
        if (d.magnitude > Radius) d = d.normalized * Radius;
        return new Vector3(Center.x + d.x, Center.y, Center.z + d.z);
    }

    private static float FlatDist(Vector3 a, Vector3 b)
    {
        a.y = b.y = 0f;
        return Vector3.Distance(a, b);
    }

    // ── 위험 ───────────────────────────────────────────────────

    private abstract class Hazard
    {
        protected readonly BarrageCorridorGame G;
        public bool    Done;
        public Vector3 Anchor;
        protected Hazard(BarrageCorridorGame g) => G = g;
        public abstract void Tick(float dt);
        public abstract void Clear();
    }

    /// <summary>낙뢰 — 원 예고 0.9초 → 터짐. 원 안이면 맞음, 가장자리를 스치면 간발.</summary>
    private sealed class BoltHazard : Hazard
    {
        private GameObject _disc;
        private float _t;
        private bool  _struck;

        public BoltHazard(BarrageCorridorGame g, Vector3 at) : base(g)
        {
            Anchor = at;
            _disc = PatternGuideHelper.Prepare(PatternGuideHelper.Disc(at, BoltRadius, g.Theme), g.Theme);
            PatternGuideHelper.SetProgress(_disc, 0f);
        }

        public override void Tick(float dt)
        {
            _t += dt;
            if (!_struck)
            {
                PatternGuideHelper.SetProgress(_disc, _t / BoltWarn);
                if (_t < BoltWarn) return;
                _struck = true;
                PatternGuideHelper.Arm(_disc);
                RunFx.Play(RunFxSlot.Ring, Anchor, 0.55f, G.Theme);
                float d = FlatDist(G.PlayerPos(), Anchor);
                if (d <= BoltRadius) G.TryHit();
                else if (d <= BoltRadius + NearMargin) G.NearMiss();
                return;
            }
            if (_t >= BoltWarn + 0.15f) Done = true;
        }

        public override void Clear() => PatternGuideHelper.SafeDestroy(ref _disc);
    }

    /// <summary>가르는 빛 — 판을 가로지르는 띠 예고 1.0초 → 0.4초 지속. 띠 안이면 맞음.</summary>
    private sealed class SweepHazard : Hazard
    {
        private GameObject _beam;
        private readonly Vector3 _origin, _dir;
        private readonly float _length;
        private float _t;
        private bool  _active, _tried, _near;

        public SweepHazard(BarrageCorridorGame g, Vector3 through, float yaw) : base(g)
        {
            _dir    = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            _length = g.Radius * 2.4f;
            _origin = through - _dir * (_length * 0.5f);
            Anchor  = through;
            _beam = PatternGuideHelper.Prepare(PatternGuideHelper.Beam(_origin, _dir, _length, SweepWidth, g.Theme), g.Theme);
            PatternGuideHelper.SetProgress(_beam, 0f);
        }

        public override void Tick(float dt)
        {
            _t += dt;
            if (_t < SweepWarn)
            {
                PatternGuideHelper.SetProgress(_beam, _t / SweepWarn);
                return;
            }
            if (!_active)
            {
                _active = true;
                PatternGuideHelper.Arm(_beam);
                MinigameFx.Sound(SoundKey.Sfx.PlayerHitMedium, 0.45f, 1.6f);
            }

            // 띠 안에 든 첫 순간 한 번만 판정 — 회피 무적으로 그 순간을 넘기면 피한 것
            float off = Offset();
            if (!_tried && off <= SweepWidth * 0.5f) { _tried = true; G.TryHit(); }
            else if (off <= SweepWidth * 0.5f + NearMargin) _near = true;

            if (_t >= SweepWarn + SweepActive)
            {
                if (!_tried && _near) G.NearMiss();
                Done = true;
            }
        }

        private float Offset()
        {
            Vector3 p = G.PlayerPos() - _origin; p.y = 0f;
            float along = Vector3.Dot(p, _dir);
            if (along < 0f || along > _length) return float.MaxValue;
            return (p - _dir * along).magnitude;
        }

        public override void Clear() => PatternGuideHelper.SafeDestroy(ref _beam);
    }

    /// <summary>조이는 고리 — 바깥에서 안으로 좁혀 오고, 틈(60°) 한 곳만 뚫려 있다. 고리가 지나갈 때 틈 밖이면 맞음.</summary>
    private sealed class RingHazard : Hazard
    {
        private LineRenderer _ring;
        private readonly float _gapYaw;
        private float _t;
        private bool  _passed;

        public RingHazard(BarrageCorridorGame g, float gapYaw) : base(g)
        {
            _gapYaw = gapYaw;
            Anchor  = g.Center;
            _ring   = MinigameFx.CreateRing(new Color(g.Theme.r, g.Theme.g, g.Theme.b, 0.35f), 0.5f);
            MinigameFx.SetRing(_ring, g.Center, g.Radius, gapYaw, RingGap);
            // 틈 방향에 짧은 표시 — 어디로 빠질지 읽히게
            PatternGuideHelper.Disc(g.Center + Quaternion.Euler(0f, gapYaw, 0f) * Vector3.forward * (g.Radius - 0.6f), 0.6f, Color.white, RingWarn + RingClose);
        }

        public override void Tick(float dt)
        {
            _t += dt;
            if (_t < RingWarn) return;

            float k = Mathf.Clamp01((_t - RingWarn) / RingClose);
            float r = Mathf.Lerp(G.Radius, 0.4f, k);
            _ring.startColor = _ring.endColor = G.Theme;
            MinigameFx.SetRing(_ring, G.Center, r, _gapYaw, RingGap);

            if (!_passed)
            {
                Vector3 d = G.PlayerPos() - G.Center; d.y = 0f;
                if (Mathf.Abs(d.magnitude - r) <= RingBand)
                {
                    _passed = true;
                    float delta = Mathf.Abs(Mathf.DeltaAngle(MinigameFx.Yaw(d), _gapYaw));
                    if (delta > RingGap * 0.5f) G.TryHit();
                    else if (delta > RingGap * 0.5f - 12f) G.NearMiss();
                }
            }
            if (k >= 1f) Done = true;
        }

        public override void Clear()
        {
            if (_ring != null) Object.Destroy(_ring.gameObject);
            _ring = null;
        }
    }

    /// <summary>느린 탄 — 가장자리에서 곧게 날아와 판을 지나간다. 닿으면 사라진다(맞은 뒤 쿨다운 중이면 세지 않고 사라진다).</summary>
    private sealed class BulletHazard : Hazard
    {
        private readonly GameObject _ball;
        private Vector3 _dir;
        private float   _speed, _t;

        public BulletHazard(BarrageCorridorGame g) : base(g)
        {
            _ball = g.Own(PatternGuideHelper.Sphere(g.Center, BulletRadius, g.Theme));
            _ball.SetActive(false);
        }

        public void Launch(Vector3 from, Vector3 dir, float speed)
        {
            Anchor = from + Vector3.up * BulletHeight;
            _dir   = dir;
            _speed = speed;
            _t     = 0f;
            Done   = false;
            if (_ball == null) return;
            _ball.transform.position   = Anchor;
            _ball.transform.localScale = Vector3.zero;
            _ball.SetActive(true);
        }

        public override void Tick(float dt)
        {
            _t     += dt;
            Anchor += _dir * (_speed * dt);
            if (_ball != null)
            {
                _ball.transform.position   = Anchor;
                _ball.transform.localScale = Vector3.one * (BulletRadius * 2f * Mathf.Clamp01(_t / BulletFadeIn));
            }
            if (_t < BulletFadeIn) return;

            if (FlatDist(G.PlayerPos(), Anchor) <= BulletHitDist) { G.TryHit(); Done = true; return; }
            if (FlatDist(Anchor, G.Center) > G.Radius + 0.5f) Done = true;   // 판을 다 지나갔다
        }

        public override void Clear()
        {
            if (_ball != null) _ball.SetActive(false);
            G.ReturnBullet(this);
        }
    }

    /// <summary>쫓는 구체 — 느리게 따라온다(6초). 닿으면 맞고 터진다.</summary>
    private sealed class OrbHazard : Hazard
    {
        private GameObject _orb;
        private readonly float _speed;
        private float _t, _closest = float.MaxValue;

        public OrbHazard(BarrageCorridorGame g, Vector3 at) : base(g)
        {
            Anchor = at + Vector3.up * 0.9f;
            _speed = g.BulletSpeed;   // 10-29 3.0~3.6 → 탄과 같은 2.6~3.1(「탄속은 조금 느리게」)
            _orb   = PatternGuideHelper.Sphere(Anchor, OrbRadius, g.Theme);
        }

        public override void Tick(float dt)
        {
            _t += dt;
            Vector3 target = G.PlayerPos() + Vector3.up * 0.9f;
            Anchor = Vector3.MoveTowards(Anchor, target, _speed * dt);
            if (_orb != null)
            {
                _orb.transform.position = Anchor;
                float s = OrbRadius * 2f * (1f + 0.12f * Mathf.Sin(_t * 12f));
                _orb.transform.localScale = Vector3.one * s;
            }

            float d = FlatDist(Anchor, target);
            _closest = Mathf.Min(_closest, d);
            if (d <= OrbHitDist)
            {
                if (G.TryHit()) RunFx.Play(RunFxSlot.Ring, Anchor, 0.4f, G.Theme);
                Done = true;
                return;
            }
            if (_t >= OrbLife)
            {
                if (_closest <= OrbHitDist + NearMargin + 0.4f) G.NearMiss();
                Done = true;
            }
        }

        public override void Clear() => PatternGuideHelper.SafeDestroy(ref _orb);
    }
}

using System.Collections.Generic;
using RelicFairy.UI;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// C3 「사슬 끊기」 — 봉인기 전용. 리치 설계서 §3-4 · §13.
///
/// O 당김(castDuration) — 리치가 사슬을 힘껏 당긴다
/// → A 충격파(waveCount × waveInterval) — 사슬이 팽팽해질 때마다 리치 발밑에서 금빛 원형 충격파(두께 ringThickness)가
///   초속 ringSpeed로 퍼진다. 링을 <b>회피로 넘는다</b>. 링 하나는 한 번만 맞힌다
/// → E 휘청(staggerDuration) — 사슬이 끊기지 않고 리치가 휘청인다(가장 긴 딜 구간)
/// → R 복귀
/// 서사: 「묶여 있다 — 하지만 곧 풀린다」. 해방판은 없다.
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/Boss/Lich/Lich_ChainBreakPattern", fileName = "Lich_ChainBreakPattern")]
public class LichChainBreakPatternSO : BossPatternSO
{
    [Header("C3 — 발동")]
    public float maxRange        = 25f;
    public float patternCooldown = 18f;

    [Header("C3 — 타이밍 (초)")]
    public float castDuration     = 1.0f;
    public int   waveCount        = 3;
    public float waveInterval     = 1.0f;
    public float staggerDuration  = 2.0f;
    public float recoveryDuration = 0.5f;

    [Header("C3 — 충격파")]
    public float ringSpeed          = 10f;
    public float ringThickness      = 1.5f;
    public float ringMaxRadius      = 24f;
    public float damageMultiplier   = 1.0f;
    public float knockbackMultiplier = 1.0f;

    // ── 런타임 ───────────────────────────────────────────
    private LichChainBreakState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new LichChainBreakState(this);
    public override void OnRecycled()                       => _state = new LichChainBreakState(this);

    public override bool CanExecute(BossPatternContext ctx)
    {
        if (ctx.Ctx.Runtime.PlayerTarget == null) return false;
        var lichBB = (ctx.Boss as LichMonster)?.LichBB;
        if (lichBB == null || !lichBB.IsPhase2 || lichBB.IsNightmare) return false;
        if (lichBB.ChainBreakCooldown > 0f) return false;
        return Vector3.Distance(ctx.Ctx.Transform.position, ctx.Ctx.Runtime.PlayerTarget.position) <= maxRange;
    }

    public override SpecialStateBase GetRuntimeState() => _state;
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// LichChainBreakState — 중단 불가(피해는 받음)
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class LichChainBreakState : UnInterruptibleState<LichChainBreakPatternSO>
{
    private enum Phase { Cast, Waves, Stagger, Recovery }

    private const string BarkKey      = "Lich_ChainBreak";
    private const int    RingSegments = 64;
    private const float  RingWidth    = 0.9f;    // 사슬 텍스처 — 보이는 고리 굵기 약 0.3 m (텍스처가 없으면 단색 선)
    private const float  PulseLead    = 0.3f;    // 사슬이 팽팽해지는 신호 → 링까지

    private static readonly Color RingColor = new Color(1.00f, 0.80f, 0.25f, 1f);   // 금빛 — 플레이어의 봉인 사슬
    // 판정 띠 — 사슬(보이는 굵기 약 0.3 m)보다 판정(ringThickness)이 넓어 「안 닿았는데 맞았다」가 나던 것(09-18 감사).
    // 판정 두께 그대로의 옅은 금빛 띠를 사슬 아래 깐다.
    private static readonly Color BandColor = new Color(1.00f, 0.80f, 0.25f, 0.28f);
    private static Material s_ringMaterial;

    private sealed class Ring
    {
        public GameObject   Go;
        public LineRenderer Line;
        public LineRenderer Band;
        public Vector3      Center;
        public float        Radius;
        public bool         Hit;
    }

    private readonly List<Ring> _rings = new(4);

    private Phase _phase;
    private float _timer;
    private int   _spawned;
    private int   _pulsed;

    public LichChainBreakState(LichChainBreakPatternSO data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        _phase   = Phase.Cast;
        _timer   = 0f;
        _spawned = 0;
        _pulsed  = 0;
        _rings.Clear();

        LichPatternUtil.FaceInstant(ctx, LichPatternUtil.PlayerFloorPos(ctx));
        ctx.Animator?.CrossFade("Phase2Entry", 0.1f);
        LichPatternUtil.Mover(ctx)?.SetLocked(true);
        LichPatternUtil.Mover(ctx)?.RequestMovementState(LichMovementState.AltitudeDescend);
        UI_BossBark.ShowDialogue(BarkKey, BossBarkType.PatternAnnounce);
        LichSfx.Play(LichSfxSlot.CastCharge, ctx.Transform.position, 0.7f);
    }

    public override void Update(MonsterContext ctx)
    {
        float dt = Time.deltaTime;
        _timer += dt;
        TickRings(ctx, dt);

        switch (_phase)
        {
            case Phase.Cast:
                if (_pulsed == 0 && _timer >= Data.castDuration - PulseLead) PulseChains(ctx);
                if (_timer >= Data.castDuration)
                {
                    _phase = Phase.Waves;
                    _timer = Data.waveInterval;   // 첫 링은 바로
                }
                break;

            case Phase.Waves:
                if (_pulsed <= _spawned && _spawned < Data.waveCount && _timer >= Data.waveInterval - PulseLead)
                    PulseChains(ctx);
                if (_spawned < Data.waveCount && _timer >= Data.waveInterval)
                {
                    _timer = 0f;
                    SpawnRing(ctx);
                }
                if (_spawned >= Data.waveCount && _rings.Count == 0)
                {
                    ctx.Animator?.CrossFade("GetHit", 0.05f);   // 끊기지 않은 사슬에 휘청
                    LichPatternUtil.Lich(ctx)?.NotifyVulnerableWindow(Data.staggerDuration);
                    _phase = Phase.Stagger;
                    _timer = 0f;
                }
                break;

            case Phase.Stagger:
                if (_timer >= Data.staggerDuration)
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
        for (int i = 0; i < _rings.Count; i++)
            if (_rings[i].Go != null) Object.Destroy(_rings[i].Go);
        _rings.Clear();

        var lich = LichPatternUtil.Lich(ctx);
        lich?.MovementController?.SetLocked(false);
        if (lich?.LichBB != null)
            lich.LichBB.ChainBreakCooldown = Data.patternCooldown;
    }

    private void SpawnRing(MonsterContext ctx)
    {
        _spawned++;
        Vector3 center = LichPatternUtil.OnFloor(ctx, ctx.Transform.position);

        var go   = new GameObject("LichChainRing");
        go.transform.position = center + Vector3.up * 0.15f;
        var line = go.AddComponent<LineRenderer>();
        var chainMat = LichVfx.ChainMaterial;
        line.loop              = true;
        line.useWorldSpace     = false;
        line.positionCount     = RingSegments;
        line.widthMultiplier   = chainMat != null ? RingWidth : 0.35f;
        line.sharedMaterial    = chainMat != null ? chainMat : RingMaterial;
        line.textureMode       = LineTextureMode.Tile;
        line.textureScale      = new Vector2(1f / RingWidth, 1f);
        line.alignment         = LineAlignment.TransformZ;
        go.transform.rotation  = Quaternion.Euler(-90f, 0f, 0f);  // 선 면이 위를 보게 눕힌다(로컬 Z = 위)
        line.startColor        = RingColor;
        line.endColor          = RingColor;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows    = false;
        var bandGo = new GameObject("Band");
        bandGo.transform.SetParent(go.transform, false);
        bandGo.transform.localPosition = new Vector3(0f, 0f, -0.05f);   // 사슬 바로 아래(로컬 Z = 위)
        var band = bandGo.AddComponent<LineRenderer>();
        band.loop              = true;
        band.useWorldSpace     = false;
        band.positionCount     = RingSegments;
        band.widthMultiplier   = Data.ringThickness;
        band.sharedMaterial    = RingMaterial;
        band.alignment         = LineAlignment.TransformZ;
        band.startColor        = BandColor;
        band.endColor          = BandColor;
        band.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        band.receiveShadows    = false;

        var ring = new Ring { Go = go, Line = line, Band = band, Center = center, Radius = 0.5f };
        LichSfx.Play(LichSfxSlot.ChainPulse, center);
        PlaceRing(ring);
        _rings.Add(ring);

        LichVfx.Play(LichVfxSlot.SealBurst, ctx.Transform.position + Vector3.up, Quaternion.identity, 0.6f);
    }

    /// <summary>사슬이 팽팽해진다 — 링이 나가기 직전의 신호.</summary>
    private void PulseChains(MonsterContext ctx)
    {
        // 사슬을 잡아채는 스윙 — 접촉 프레임 = 링이 터져 나가는 순간(PulseLead 뒤). 좌우를 번갈아.
        LichPatternUtil.Swing(ctx, _pulsed % 2 == 0 ? LichSwing.RightToLeft : LichSwing.LeftToRight, PulseLead, 0.05f);
        _pulsed++;
        LichHazards.PulseBoundChains();
        LichPatternUtil.Lich(ctx)?.PulseBook(0.3f);
    }

    private void TickRings(MonsterContext ctx, float dt)
    {
        var target = ctx.Runtime.PlayerTarget;
        for (int i = _rings.Count - 1; i >= 0; i--)
        {
            var r = _rings[i];
            r.Radius += Data.ringSpeed * dt;
            PlaceRing(r);

            if (!r.Hit && target != null)
            {
                float d = LichPatternUtil.FlatDistance(r.Center, target.position);
                if (Mathf.Abs(d - r.Radius) <= Data.ringThickness * 0.5f
                    && LichPatternUtil.HitCircle(ctx, r.Center, r.Radius + Data.ringThickness, Data.damageMultiplier, Data.knockbackMultiplier))
                {
                    r.Hit = true;
                    LichPatternUtil.Impact(LichImpact.Light, true);
                }
            }

            if (r.Radius >= Data.ringMaxRadius)
            {
                if (r.Go != null) Object.Destroy(r.Go);
                _rings.RemoveAt(i);
            }
        }
    }

    /// <summary>선 두께는 그대로 두고 점만 반경에 맞춰 옮긴다(transform 배율은 선 두께까지 키울 수 있어서).</summary>
    private static void PlaceRing(Ring r)
    {
        if (r.Line == null) return;
        // 오브젝트가 눕혀져(X −90°) 있어 로컬 XY 평면이 바닥이다.
        for (int i = 0; i < RingSegments; i++)
        {
            float   a = i * Mathf.PI * 2f / RingSegments;
            Vector3 p = new Vector3(Mathf.Cos(a) * r.Radius, Mathf.Sin(a) * r.Radius, 0f);
            r.Line.SetPosition(i, p);
            if (r.Band != null) r.Band.SetPosition(i, p);
        }
    }

    private static Material RingMaterial
    {
        get
        {
            if (s_ringMaterial == null)
                s_ringMaterial = new Material(Shader.Find("Sprites/Default")) { hideFlags = HideFlags.DontSave };
            return s_ringMaterial;
        }
    }
}
}

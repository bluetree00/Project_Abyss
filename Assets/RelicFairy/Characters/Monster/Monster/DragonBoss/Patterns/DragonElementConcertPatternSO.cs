using System.Collections.Generic;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// DL2 「원소 합주」 — 2페이지(심연) 일반 패턴. 1페이지에 쓴 세 원소를 한 번씩 몰아친다.
///   얼음(플레이어 둘레 원 6개) → 번개(플레이어를 가로지르는 X자 두 줄) → 불(플레이어 주변 원 8개).
///   원소마다 예고 1.0초(원소 색으로 차오름) → 판정(빨강) · 다음 원소는 0.8초 뒤 시작해 예고가 겹친다.
/// 자리는 원소 예고가 시작되는 순간의 플레이어 위치로 정해지고 따라오지 않는다(조준형 아님 — 원소 색을 읽고 한 번씩 비킨다).
/// 지상 · 공중 어디서든 쓴다. 거리 제한 없음.
/// </summary>
[CreateAssetMenu(fileName = "DragonElementConcertPattern",
    menuName = "RelicFairy/Boss/Dragon/ElementConcertPattern")]
public class DragonElementConcertPatternSO : BossPatternSO
{
    [Header("쿨다운")]
    [SerializeField] private float _cooldown = 18f;

    [Header("박자")]
    [Tooltip("원소마다 예고 시간 (초)")]
    [SerializeField] private float _telegraphSeconds = 1.0f;
    [Tooltip("다음 원소 예고가 시작되는 간격 (초)")]
    [SerializeField] private float _stepInterval     = 0.8f;
    [Tooltip("판정 뒤 판정 색 가이드를 남겨 두는 시간 (초)")]
    [SerializeField] private float _armHoldSeconds   = 0.25f;

    [Header("얼음 — 플레이어 둘레 원")]
    [SerializeField] private int                  _iceCount       = 6;
    [SerializeField] private float                _iceRingRadius  = 3.2f;
    [SerializeField] private float                _iceDiscRadius  = 2f;
    [SerializeField] private float                _iceDamageMult  = 0.9f;
    [SerializeField] private PlayerStatusEffectSO _iceStatus;
    [SerializeField] private GameObject           _iceVfxPrefab;
    [SerializeField] private float                _iceVfxScale    = 1f;
    [SerializeField] private AudioClip            _iceSfx;

    [Header("번개 — 플레이어를 가로지르는 X자 두 줄")]
    [SerializeField] private float                _thunderHalfLength = 14f;
    [SerializeField] private float                _thunderWidth      = 2.2f;
    [Tooltip("두 줄이 보스→플레이어 방향과 이루는 각 (도) — 45면 직각으로 교차")]
    [SerializeField] private float                _thunderAngle      = 45f;
    [SerializeField] private float                _thunderDamageMult = 0.9f;
    [SerializeField] private PlayerStatusEffectSO _thunderStatus;
    [SerializeField] private GameObject           _thunderVfxPrefab;
    [SerializeField] private float                _thunderVfxScale   = 1f;
    [SerializeField] private float                _thunderVfxSpacing = 3.5f;
    [SerializeField] private AudioClip            _thunderSfx;

    [Header("불 — 플레이어 주변 원")]
    [SerializeField] private int                  _fireCount         = 8;
    [Tooltip("첫 원은 플레이어 자리, 나머지는 이 반경 안에 흩어진다 (m)")]
    [SerializeField] private float                _fireScatterRadius = 6.5f;
    [SerializeField] private float                _fireDiscRadius    = 1.8f;
    [Tooltip("원끼리 최소 간격 (m) — 사이로 빠질 길이 남게")]
    [SerializeField] private float                _fireMinSeparation = 2.6f;
    [SerializeField] private float                _fireDamageMult    = 0.9f;
    [SerializeField] private PlayerStatusEffectSO _fireStatus;
    [SerializeField] private GameObject           _fireVfxPrefab;
    [SerializeField] private float                _fireVfxScale      = 1f;
    [SerializeField] private AudioClip            _fireSfx;

    [Header("애니메이션")]
    [SerializeField] private string _groundStateName = "UAttackWindHighStart";
    [SerializeField] private string _airStateName    = "Airborne_Hover";
    [SerializeField] private float  _endPoseSeconds  = 0.4f;

    public float                Cooldown          => _cooldown;
    public float                TelegraphSeconds  => Mathf.Max(0.1f, _telegraphSeconds);
    public float                StepInterval      => Mathf.Max(0.1f, _stepInterval);
    public float                ArmHoldSeconds    => _armHoldSeconds;
    public int                  IceCount          => Mathf.Clamp(_iceCount, 1, 12);
    public float                IceRingRadius     => _iceRingRadius;
    public float                IceDiscRadius     => _iceDiscRadius;
    public float                IceDamageMult     => _iceDamageMult;
    public PlayerStatusEffectSO IceStatus         => _iceStatus;
    public GameObject           IceVfxPrefab      => _iceVfxPrefab;
    public float                IceVfxScale       => _iceVfxScale;
    public AudioClip            IceSfx            => _iceSfx;
    public float                ThunderHalfLength => _thunderHalfLength;
    public float                ThunderWidth      => _thunderWidth;
    public float                ThunderAngle      => _thunderAngle;
    public float                ThunderDamageMult => _thunderDamageMult;
    public PlayerStatusEffectSO ThunderStatus     => _thunderStatus;
    public GameObject           ThunderVfxPrefab  => _thunderVfxPrefab;
    public float                ThunderVfxScale   => _thunderVfxScale;
    public float                ThunderVfxSpacing => Mathf.Max(1f, _thunderVfxSpacing);
    public AudioClip            ThunderSfx        => _thunderSfx;
    public int                  FireCount         => Mathf.Clamp(_fireCount, 1, 12);
    public float                FireScatterRadius => _fireScatterRadius;
    public float                FireDiscRadius    => _fireDiscRadius;
    public float                FireMinSeparation => _fireMinSeparation;
    public float                FireDamageMult    => _fireDamageMult;
    public PlayerStatusEffectSO FireStatus        => _fireStatus;
    public GameObject           FireVfxPrefab     => _fireVfxPrefab;
    public float                FireVfxScale      => _fireVfxScale;
    public AudioClip            FireSfx           => _fireSfx;
    public string               GroundStateName   => _groundStateName;
    public string               AirStateName      => _airStateName;
    public float                EndPoseSeconds    => _endPoseSeconds;

    private DragonElementConcertState _runtimeState;

    public override void Initialize(BossPatternContext ctx) => _runtimeState = new DragonElementConcertState(this);

    public override void OnRecycled() => _runtimeState?.Reset();

    public override bool CanExecute(BossPatternContext ctx)
        => ctx.Ctx.Runtime.PlayerTarget != null
           && ctx.Blackboard is DragonBossBlackboard bb
           && bb.ElementConcertCooldown <= 0f;

    public override SpecialStateBase GetRuntimeState() => _runtimeState ??= new DragonElementConcertState(this);
}

// ────────────────────────────────────────────────────────────────────────────
// Runtime state
// ────────────────────────────────────────────────────────────────────────────

/// <summary>세 원소 박자를 겹쳐 돌린다 — 얼음(0초) · 번개(0.8초) · 불(1.6초), 각자 예고 1.0초 → 판정 → 치움.</summary>
internal sealed class DragonElementConcertState : FullLockState<DragonElementConcertPatternSO>
{
    private const int StepCount = 3;   // 0 얼음 · 1 번개 · 2 불

    /// <summary>원소 한 박자 — 예고 가이드와 판정 도형.</summary>
    private sealed class Step
    {
        public bool Started, Resolved, Cleared;
        public readonly List<GameObject> Guides  = new();
        public readonly List<Vector3>    Centers = new();   // 원: 중심 / 줄: 시작점
        public readonly List<Vector3>    Dirs    = new();   // 줄만
        public readonly List<float>      Lengths = new();   // 줄만

        public void Clear()
        {
            for (int i = 0; i < Guides.Count; i++)
            {
                var g = Guides[i];
                PatternGuideHelper.SafeDestroy(ref g);
            }
            Guides.Clear();
            Centers.Clear();
            Dirs.Clear();
            Lengths.Clear();
            Started = Resolved = Cleared = false;
        }
    }

    private readonly Step[] _steps = { new Step(), new Step(), new Step() };
    private float   _timer;
    private bool    _airborne;
    private Vector3 _hoverPos;
    private bool    _finished;
    private float   _finishTimer;

    internal DragonElementConcertState(DragonElementConcertPatternSO data) : base(data) { }

    internal void Reset()
    {
        foreach (var s in _steps) s.Clear();
        _finished = false;
    }

    public override void Enter(MonsterContext ctx)
    {
        GameCameraController.Instance?.DeactivateDragonTopDownView(0.8f);
        foreach (var s in _steps) s.Clear();
        _timer       = 0f;
        _finished    = false;
        _finishTimer = 0f;
        _airborne    = DragonAbyssFx.IsAirborne(ctx);
        _hoverPos    = ctx.Transform.position;
        if (!_airborne && ctx.Agent != null && ctx.Agent.isOnNavMesh) ctx.Agent.isStopped = true;

        if (DragonAbyssFx.Blackboard(ctx) is { } bb) bb.ElementConcertCooldown = Data.Cooldown;
        DragonAbyssFx.PlayAnim(ctx, _airborne ? Data.AirStateName : Data.GroundStateName, 0.15f);
    }

    public override void Update(MonsterContext ctx)
    {
        _timer += Time.deltaTime;
        if (_airborne) ctx.Transform.position = _hoverPos;
        if (ctx.Runtime.PlayerTarget != null)
            DragonAbyssFx.Face(ctx, ctx.Runtime.PlayerTarget.position - ctx.Transform.position, 90f);

        bool allCleared = true;
        for (int i = 0; i < StepCount; i++)
        {
            var   step  = _steps[i];
            float start = i * Data.StepInterval;
            float t     = _timer - start;
            if (t < 0f) { allCleared = false; continue; }

            if (!step.Started) BeginStep(ctx, i, step);
            if (!step.Resolved)
            {
                float progress = t / Data.TelegraphSeconds;
                foreach (var g in step.Guides) PatternGuideHelper.SetProgress(g, progress);
                if (t >= Data.TelegraphSeconds) ResolveStep(ctx, i, step);
            }
            if (step.Resolved && !step.Cleared && t >= Data.TelegraphSeconds + Data.ArmHoldSeconds)
            {
                for (int g = 0; g < step.Guides.Count; g++)
                {
                    var guide = step.Guides[g];
                    PatternGuideHelper.SafeDestroy(ref guide);
                }
                step.Guides.Clear();
                step.Cleared = true;
            }
            if (!step.Cleared) allCleared = false;
        }

        if (!allCleared) return;
        if (!_finished)
        {
            _finished    = true;
            _finishTimer = 0f;
        }
        _finishTimer += Time.deltaTime;
        if (_finishTimer < Data.EndPoseSeconds) return;
        DragonAbyssFx.ReturnToCombat(ctx);
    }

    public override void Exit(MonsterContext ctx)
    {
        foreach (var s in _steps) s.Clear();
    }

    // ── 박자 시작 — 자리 정하기 · 예고 ────────────────────────────────────────

    private void BeginStep(MonsterContext ctx, int index, Step step)
    {
        step.Started = true;
        Vector3 player = ctx.Runtime.PlayerTarget != null ? ctx.Runtime.PlayerTarget.position : ctx.Transform.position;
        player = DragonAbyssFx.OnFloor(ctx, DragonAbyssFx.ClampToArena(player, 1f));

        switch (index)
        {
            case 0: BuildIce(ctx, step, player);     break;
            case 1: BuildThunder(ctx, step, player); break;
            default: BuildFire(ctx, step, player);   break;
        }
    }

    /// <summary>얼음 — 플레이어 둘레 고리. 원끼리 겹쳐 닫힌 고리가 되고 한가운데만 비어 있다(가만히 서거나 고리 밖으로).</summary>
    private void BuildIce(MonsterContext ctx, Step step, Vector3 player)
    {
        Color c      = DragonAbyssFx.WithAlpha(DragonBossVisualHelper.GetElementColor(DragonBossBlackboard.DragonElement.Ice), 0.85f);
        int   count  = Data.IceCount;
        float offset = Random.Range(0f, 360f);
        for (int i = 0; i < count; i++)
        {
            float   a   = (offset + i * 360f / count) * Mathf.Deg2Rad;
            Vector3 pos = player + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * Data.IceRingRadius;
            AddDisc(ctx, step, pos, Data.IceDiscRadius, c);
        }
    }

    /// <summary>번개 — 플레이어 자리에서 교차하는 두 줄(보스→플레이어 방향 ± 각).</summary>
    private void BuildThunder(MonsterContext ctx, Step step, Vector3 player)
    {
        Color   c    = DragonAbyssFx.WithAlpha(DragonBossVisualHelper.GetElementColor(DragonBossBlackboard.DragonElement.Thunder), 0.85f);
        Vector3 axis = DragonAbyssFx.FlatDir(ctx.Transform.position, player, ctx.Transform.forward);
        for (int i = 0; i < 2; i++)
        {
            Vector3 dir   = Quaternion.Euler(0f, i == 0 ? Data.ThunderAngle : -Data.ThunderAngle, 0f) * axis;
            float   fwd   = DragonPatternFloorUtils.DistanceToFloorEdge(player,  dir, Data.ThunderHalfLength);
            float   back  = DragonPatternFloorUtils.DistanceToFloorEdge(player, -dir, Data.ThunderHalfLength);
            Vector3 start = player - dir * back;
            float   len   = Mathf.Max(1f, fwd + back);

            step.Centers.Add(start);
            step.Dirs.Add(dir);
            step.Lengths.Add(len);
            step.Guides.Add(PatternGuideHelper.Prepare(PatternGuideHelper.Beam(start, dir, len, Data.ThunderWidth, c), c));
        }
    }

    /// <summary>불 — 한 개는 플레이어 자리, 나머지는 주변에 흩뿌린다(원끼리 간격을 둬 사이 길이 남는다).</summary>
    private void BuildFire(MonsterContext ctx, Step step, Vector3 player)
    {
        Color c = DragonAbyssFx.WithAlpha(DragonBossVisualHelper.GetElementColor(DragonBossBlackboard.DragonElement.Fire), 0.85f);
        AddDisc(ctx, step, player, Data.FireDiscRadius, c);

        float minSepSq = Data.FireMinSeparation * Data.FireMinSeparation;
        for (int i = 1; i < Data.FireCount; i++)
        {
            Vector3 pos = player;
            for (int tries = 0; tries < 12; tries++)
            {
                float a = Random.Range(0f, Mathf.PI * 2f);
                float r = Random.Range(Data.FireDiscRadius * 1.2f, Mathf.Max(Data.FireDiscRadius * 1.3f, Data.FireScatterRadius));
                pos = DragonAbyssFx.ClampToArena(player + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r, 1f);
                if (!TooClose(step, pos, minSepSq)) break;
            }
            AddDisc(ctx, step, pos, Data.FireDiscRadius, c);
        }
    }

    private static bool TooClose(Step step, Vector3 pos, float minSepSq)
    {
        foreach (var c in step.Centers)
        {
            float dx = c.x - pos.x, dz = c.z - pos.z;
            if (dx * dx + dz * dz < minSepSq) return true;
        }
        return false;
    }

    private static void AddDisc(MonsterContext ctx, Step step, Vector3 pos, float radius, Color c)
    {
        pos = DragonAbyssFx.OnFloor(ctx, pos);
        step.Centers.Add(pos);
        step.Guides.Add(PatternGuideHelper.Prepare(PatternGuideHelper.Disc(pos, radius, c), c));
    }

    // ── 판정 ─────────────────────────────────────────────────────────────────

    private void ResolveStep(MonsterContext ctx, int index, Step step)
    {
        step.Resolved = true;
        foreach (var g in step.Guides) PatternGuideHelper.Arm(g);

        var     player  = DragonAbyssFx.Player(ctx);
        Vector3 p       = player != null ? player.transform.position : new Vector3(float.MaxValue, 0f, float.MaxValue);
        bool    inside  = false;

        switch (index)
        {
            case 0:
                for (int i = 0; i < step.Centers.Count; i++)
                {
                    DragonAbyssFx.Burst(Data.IceVfxPrefab, step.Centers[i], Quaternion.identity, Data.IceVfxScale);
                    inside |= DragonAbyssFx.InDisc(p, step.Centers[i], Data.IceDiscRadius);
                }
                if (step.Centers.Count > 0) Managers.Sound?.PlayEffectAt(Data.IceSfx, step.Centers[0]);
                if (inside && DragonAbyssFx.HitPlayer(ctx, Data.IceDamageMult, HitWeight.Medium)) Data.IceStatus?.Apply(player);
                break;

            case 1:
                for (int i = 0; i < step.Centers.Count; i++)
                {
                    for (float d = Data.ThunderVfxSpacing * 0.5f; d < step.Lengths[i]; d += Data.ThunderVfxSpacing)
                        DragonAbyssFx.Burst(Data.ThunderVfxPrefab, step.Centers[i] + step.Dirs[i] * d, Quaternion.identity, Data.ThunderVfxScale);
                    inside |= DragonAbyssFx.InStrip(p, step.Centers[i], step.Dirs[i], step.Lengths[i], Data.ThunderWidth * 0.5f);
                }
                if (step.Centers.Count > 0) Managers.Sound?.PlayEffectAt(Data.ThunderSfx, p);
                if (inside && DragonAbyssFx.HitPlayer(ctx, Data.ThunderDamageMult, HitWeight.Medium)) Data.ThunderStatus?.Apply(player);
                break;

            default:
                for (int i = 0; i < step.Centers.Count; i++)
                {
                    DragonAbyssFx.Burst(Data.FireVfxPrefab, step.Centers[i], Quaternion.identity, Data.FireVfxScale);
                    inside |= DragonAbyssFx.InDisc(p, step.Centers[i], Data.FireDiscRadius);
                }
                if (step.Centers.Count > 0) Managers.Sound?.PlayEffectAt(Data.FireSfx, step.Centers[0]);
                if (inside && DragonAbyssFx.HitPlayer(ctx, Data.FireDamageMult, HitWeight.Medium)) Data.FireStatus?.Apply(player);
                break;
        }
    }
}
}

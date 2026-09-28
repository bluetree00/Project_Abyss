using System.Collections.Generic;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// DL3 「공허 낙하」 — 2페이지(심연) 일반 패턴 · 위협 5.
///   아레나 한가운데에 거대 운석 예고(4초) → 가운데에서 9~12 m 떨어진 자리에 얼음 기둥 3개가 솟는다(몸으로 막는 진짜 기둥)
///   → 기둥 뒤(운석 반대쪽) <b>흰 부채꼴만 안전</b>, 나머지는 큰 피해.
/// 기둥 배치는 플레이어 방위를 기준으로 조금 흔들어 돌린다 — 어디에 있든 가장 가까운 부채꼴이 몇 초 안에 닿는다.
/// 운석 · 기둥은 조준하지 않는다(자리 고정). 지상 · 공중 어디서든 쓰고, 보스는 제자리에서 부른다.
/// 이 패턴 동안 수동 운석은 쉰다(숨은 자리에 운석이 떨어지지 않게).
/// </summary>
[CreateAssetMenu(fileName = "DragonVoidFallPattern",
    menuName = "RelicFairy/Boss/Dragon/VoidFallPattern")]
public class DragonVoidFallPatternSO : BossPatternSO
{
    [Header("쿨다운")]
    [SerializeField] private float _cooldown = 32f;

    [Header("운석")]
    [Tooltip("예고부터 충돌까지 (초)")]
    [SerializeField] private float _telegraphSeconds = 4f;
    [Tooltip("판정 반경 (m) — 0이면 아레나 전체")]
    [SerializeField] private float _blastRadius      = 0f;
    [SerializeField] private float _damageMult       = 2.4f;
    [SerializeField] private float _knockbackMult    = 1.2f;
    [Tooltip("판정 직전 예고 원을 판정 색으로 바꾸는 시간 (초)")]
    [SerializeField] private float _armLeadSeconds   = 0.4f;

    [Header("운석 연출")]
    [Tooltip("떨어지는 거대 운석 (Effect_10_BigMeteor) — 딸린 이동 스크립트는 끄고 코드가 떨어뜨린다")]
    [SerializeField] private GameObject _meteorPrefab;
    [SerializeField] private float      _meteorScale       = 3f;
    [SerializeField] private float      _meteorFallSeconds = 1.2f;
    [SerializeField] private float      _meteorFallHeight  = 30f;
    [Tooltip("충돌 폭발 (Meteor hit 2)")]
    [SerializeField] private GameObject _impactVfxPrefab;
    [SerializeField] private float      _impactVfxScale    = 4f;
    [SerializeField] private AudioClip  _castSfx;
    [SerializeField] private AudioClip  _impactSfx;

    [Header("얼음 기둥")]
    [SerializeField] private int        _pillarCount       = 3;
    [SerializeField] private float      _pillarRingMin     = 9f;
    [SerializeField] private float      _pillarRingMax     = 12f;
    [SerializeField] private float      _pillarRadius      = 1.4f;
    [SerializeField] private float      _pillarHeight      = 6f;
    [SerializeField] private float      _pillarRiseSeconds = 0.6f;
    [Tooltip("기둥 고리를 플레이어 방위에서 이만큼(±도) 흔들어 돌린다")]
    [SerializeField] private float      _pillarAimJitter   = 25f;
    [Tooltip("기둥에 덧입히는 얼음 이펙트 (없으면 기둥 도형만)")]
    [SerializeField] private GameObject _pillarVfxPrefab;
    [SerializeField] private float      _pillarVfxScale    = 1f;
    [Tooltip("충돌 뒤 기둥이 부서질 때")]
    [SerializeField] private GameObject _pillarShatterVfxPrefab;
    [SerializeField] private float      _pillarShatterVfxScale = 1.5f;

    [Header("안전 부채꼴")]
    [Tooltip("기둥 중심을 꼭짓점으로 운석 반대쪽으로 벌리는 반각 (도)")]
    [SerializeField] private float _safeHalfAngle = 20f;

    [Header("애니메이션")]
    [SerializeField] private string _groundStateName = "UAttackWindHighStart";
    [SerializeField] private string _airStateName    = "Airborne_Hover";
    [SerializeField] private float  _endPoseSeconds  = 0.8f;

    public float      Cooldown               => _cooldown;
    public float      TelegraphSeconds       => Mathf.Max(1f, _telegraphSeconds);
    public float      BlastRadius            => _blastRadius;
    public float      DamageMult             => _damageMult;
    public float      KnockbackMult          => _knockbackMult;
    public float      ArmLeadSeconds         => _armLeadSeconds;
    public GameObject MeteorPrefab           => _meteorPrefab;
    public float      MeteorScale            => _meteorScale;
    public float      MeteorFallSeconds      => Mathf.Clamp(_meteorFallSeconds, 0.1f, TelegraphSeconds);
    public float      MeteorFallHeight       => _meteorFallHeight;
    public GameObject ImpactVfxPrefab        => _impactVfxPrefab;
    public float      ImpactVfxScale         => _impactVfxScale;
    public AudioClip  CastSfx                => _castSfx;
    public AudioClip  ImpactSfx              => _impactSfx;
    public int        PillarCount            => Mathf.Clamp(_pillarCount, 1, 6);
    public float      PillarRingMin          => _pillarRingMin;
    public float      PillarRingMax          => Mathf.Max(_pillarRingMin, _pillarRingMax);
    public float      PillarRadius           => _pillarRadius;
    public float      PillarHeight           => _pillarHeight;
    public float      PillarRiseSeconds      => Mathf.Max(0.05f, _pillarRiseSeconds);
    public float      PillarAimJitter        => _pillarAimJitter;
    public GameObject PillarVfxPrefab        => _pillarVfxPrefab;
    public float      PillarVfxScale         => _pillarVfxScale;
    public GameObject PillarShatterVfxPrefab => _pillarShatterVfxPrefab;
    public float      PillarShatterVfxScale  => _pillarShatterVfxScale;
    public float      SafeHalfAngle          => Mathf.Clamp(_safeHalfAngle, 1f, 60f);
    public string     GroundStateName        => _groundStateName;
    public string     AirStateName           => _airStateName;
    public float      EndPoseSeconds         => _endPoseSeconds;

    private DragonVoidFallState _runtimeState;

    public override void Initialize(BossPatternContext ctx) => _runtimeState = new DragonVoidFallState(this);

    public override void OnRecycled() => _runtimeState?.Reset();

    public override bool CanExecute(BossPatternContext ctx)
        => ctx.Ctx.Runtime.PlayerTarget != null
           && ctx.Blackboard is DragonBossBlackboard bb
           && bb.VoidFallCooldown <= 0f;

    public override SpecialStateBase GetRuntimeState() => _runtimeState ??= new DragonVoidFallState(this);
}

// ────────────────────────────────────────────────────────────────────────────
// Runtime state
// ────────────────────────────────────────────────────────────────────────────

/// <summary>Telegraph(기둥 솟음 · 부채꼴 · 운석 낙하) → Impact → EndPose → 복귀. 중단 불가 · 이동 잠금.</summary>
internal sealed class DragonVoidFallState : FullLockState<DragonVoidFallPatternSO>
{
    private enum Phase { Telegraph, EndPose, Done }

    private sealed class Pillar
    {
        public GameObject Root;
        public GameObject Sector;
        public Vector3    Base;
        public Vector3    Out;   // 운석 중심 → 기둥 (수평 단위)
    }

    private const float PillarNudgeDegrees = 15f;   // 기둥이 플레이어 발밑에 솟지 않게 비켜 주는 각

    private readonly List<Pillar> _pillars = new();
    private Phase      _phase;
    private float      _timer;
    private bool       _airborne;
    private Vector3    _hoverPos;
    private Vector3    _center;
    private float      _blastRadius;
    private GameObject _disc;
    private GameObject _meteor;
    private Vector3    _meteorFrom;
    private bool       _armed;
    private bool       _sectorsShown;

    internal DragonVoidFallState(DragonVoidFallPatternSO data) : base(data) { }

    internal void Reset()
    {
        _phase = Phase.Done;
        Cleanup();
    }

    public override void Enter(MonsterContext ctx)
    {
        GameCameraController.Instance?.DeactivateDragonTopDownView(0.8f);
        _phase        = Phase.Telegraph;
        _timer        = 0f;
        _armed        = false;
        _sectorsShown = false;
        _airborne     = DragonAbyssFx.IsAirborne(ctx);
        _hoverPos     = ctx.Transform.position;
        if (!_airborne && ctx.Agent != null && ctx.Agent.isOnNavMesh) ctx.Agent.isStopped = true;

        var bb = DragonAbyssFx.Blackboard(ctx);
        if (bb != null)
        {
            bb.VoidFallCooldown        = Data.Cooldown;
            bb.PassiveMeteorSuppressed = true;
        }

        _center      = DragonAbyssFx.OnFloor(ctx, DragonBossRoomContext.WorldCenter);
        _blastRadius = Data.BlastRadius > 0f ? Data.BlastRadius : DragonAbyssFx.ArenaHalfDiagonal() + 1f;

        Color abyss = DragonAbyssFx.WithAlpha(DragonAbyssFx.Abyss, 0.55f);
        _disc = PatternGuideHelper.Prepare(PatternGuideHelper.Disc(_center, _blastRadius, abyss), abyss);

        SpawnPillars(ctx);
        DragonAbyssFx.PlayAnim(ctx, _airborne ? Data.AirStateName : Data.GroundStateName, 0.15f);
        Managers.Sound?.PlayEffectAt(Data.CastSfx, ctx.Transform.position);
    }

    public override void Update(MonsterContext ctx)
    {
        _timer += Time.deltaTime;
        if (_airborne) ctx.Transform.position = _hoverPos;

        switch (_phase)
        {
            case Phase.Telegraph: UpdateTelegraph(ctx); break;
            case Phase.EndPose:
                if (_timer >= Data.EndPoseSeconds)
                {
                    _phase = Phase.Done;
                    DragonAbyssFx.ReturnToCombat(ctx);
                }
                break;
        }
    }

    public override void Exit(MonsterContext ctx)
    {
        Cleanup();
        var bb = DragonAbyssFx.Blackboard(ctx);
        if (bb != null) bb.PassiveMeteorSuppressed = false;
    }

    // ── Telegraph ────────────────────────────────────────────────────────────

    private void UpdateTelegraph(MonsterContext ctx)
    {
        float total = Data.TelegraphSeconds;

        // 기둥이 솟는다 — 루트를 세로로 키워 판정 기둥도 같이 자란다
        float rise = Mathf.Clamp01(_timer / Data.PillarRiseSeconds);
        foreach (var p in _pillars)
            if (p.Root != null) p.Root.transform.localScale = new Vector3(1f, Mathf.Max(0.05f, rise), 1f);
        if (!_sectorsShown && rise >= 1f) ShowSectors(ctx);

        PatternGuideHelper.SetProgress(_disc, _timer / total);
        if (!_armed && _timer >= total - Data.ArmLeadSeconds)
        {
            _armed = true;
            PatternGuideHelper.Arm(_disc);
        }

        TickMeteor(ctx, total);
        if (_timer < total) return;
        Impact(ctx);
    }

    private void TickMeteor(MonsterContext ctx, float total)
    {
        float fallStart = total - Data.MeteorFallSeconds;
        if (_timer < fallStart) return;

        if (_meteor == null && Data.MeteorPrefab != null)
        {
            _meteorFrom = _center + Vector3.up * Data.MeteorFallHeight;
            _meteor     = DragonAbyssFx.SpawnVisual(Data.MeteorPrefab, _meteorFrom, Quaternion.LookRotation(Vector3.down), Data.MeteorScale);
        }
        if (_meteor != null)
            _meteor.transform.position = Vector3.Lerp(_meteorFrom, _center, Mathf.Clamp01((_timer - fallStart) / Data.MeteorFallSeconds));
    }

    // ── 기둥 · 부채꼴 ─────────────────────────────────────────────────────────

    private void SpawnPillars(MonsterContext ctx)
    {
        int     count  = Data.PillarCount;
        Vector3 player = ctx.Runtime.PlayerTarget != null ? ctx.Runtime.PlayerTarget.position : _center + Vector3.forward;
        float   baseDeg = DragonAbyssFx.Yaw(DragonAbyssFx.FlatDir(_center, player, Vector3.forward))
                          + Random.Range(-Data.PillarAimJitter, Data.PillarAimJitter);

        // 기둥이 플레이어 발밑에서 솟으면 끼인다 — 겹치면 고리를 조금 돌린다
        float[] radii = new float[count];
        for (int i = 0; i < count; i++) radii[i] = Random.Range(Data.PillarRingMin, Data.PillarRingMax);
        for (int attempt = 0; attempt < 4 && PillarOnPlayer(baseDeg, radii, player); attempt++)
            baseDeg += PillarNudgeDegrees;

        Color ice = DragonBossVisualHelper.GetElementColor(DragonBossBlackboard.DragonElement.Ice);
        for (int i = 0; i < count; i++)
        {
            Vector3 dir  = Quaternion.Euler(0f, baseDeg + i * 360f / count, 0f) * Vector3.forward;
            Vector3 bpos = DragonAbyssFx.OnFloor(ctx, DragonAbyssFx.ClampToArena(_center + dir * radii[i], Data.PillarRadius + 1f));
            _pillars.Add(new Pillar
            {
                Root = CreatePillar(bpos, ice),
                Base = bpos,
                Out  = DragonAbyssFx.FlatDir(_center, bpos, dir),
            });
        }
    }

    private bool PillarOnPlayer(float baseDeg, float[] radii, Vector3 player)
    {
        float clear = Data.PillarRadius + 1f;
        for (int i = 0; i < radii.Length; i++)
        {
            Vector3 dir = Quaternion.Euler(0f, baseDeg + i * 360f / radii.Length, 0f) * Vector3.forward;
            if (DragonAbyssFx.InDisc(player, _center + dir * radii[i], clear)) return true;
        }
        return false;
    }

    /// <summary>얼음 기둥 — 루트 캡슐 콜라이더가 몸을 막는다(기본 레이어). 모양은 기둥 도형 + 선택 이펙트.</summary>
    private GameObject CreatePillar(Vector3 basePos, Color ice)
    {
        var root = new GameObject("DragonVoidPillar");
        root.transform.position   = basePos;
        root.transform.localScale = new Vector3(1f, 0.05f, 1f);

        var col    = root.AddComponent<CapsuleCollider>();
        col.radius = Data.PillarRadius;
        col.height = Data.PillarHeight;
        col.center = Vector3.up * (Data.PillarHeight * 0.5f);

        var shape = PatternGuideHelper.Pillar(basePos, Data.PillarRadius, Data.PillarHeight, ice);
        shape.transform.SetParent(root.transform, true);

        var vfx = DragonAbyssFx.SpawnVisual(Data.PillarVfxPrefab, basePos, Quaternion.identity, Data.PillarVfxScale, root.transform, forceLoop: true);
        if (vfx != null) DragonBossVisualHelper.ApplyEffectTint(vfx, ice);
        return root;
    }

    /// <summary>기둥이 다 솟은 뒤 — 기둥 뒤(운석 반대쪽)로 흰 부채꼴. 큰 예고 원 위에 그린다.</summary>
    private void ShowSectors(MonsterContext ctx)
    {
        _sectorsShown = true;
        float maxExtent = DragonPatternFloorUtils.GetRoomMaxExtent();
        foreach (var p in _pillars)
        {
            float len = Mathf.Max(2f, DragonPatternFloorUtils.DistanceToFloorEdge(p.Base, p.Out, maxExtent));
            p.Sector = PatternGuideHelper.Sector(p.Base, len, Data.SafeHalfAngle * 2f, DragonAbyssFx.Yaw(p.Out), Color.white);
            PatternGuideHelper.SetFlow(p.Sector, Color.white);
            PatternGuideHelper.SetProgress(p.Sector, 1f);
            DragonAbyssFx.DrawOnTop(p.Sector);
        }
    }

    /// <summary>기둥 뒤 안전 — 기둥 중심을 꼭짓점으로 한 부채꼴 안이거나, 기둥 바로 뒤(기둥 폭 안)면 안전.</summary>
    private bool IsSheltered(Vector3 pos)
    {
        float cosHalf = Mathf.Cos(Data.SafeHalfAngle * Mathf.Deg2Rad);
        foreach (var p in _pillars)
        {
            Vector3 v = pos - p.Base;
            v.y = 0f;
            float along = Vector3.Dot(v, p.Out);
            if (along <= 0f) continue;
            float lateralSq = v.sqrMagnitude - along * along;
            if (lateralSq <= Data.PillarRadius * Data.PillarRadius) return true;
            if (along >= cosHalf * v.magnitude) return true;
        }
        return false;
    }

    // ── 충돌 ─────────────────────────────────────────────────────────────────

    private void Impact(MonsterContext ctx)
    {
        if (_meteor != null) Object.Destroy(_meteor);
        _meteor = null;
        DragonAbyssFx.Burst(Data.ImpactVfxPrefab, _center, Quaternion.identity, Data.ImpactVfxScale, 4f);
        Managers.Sound?.PlayEffectAt(Data.ImpactSfx, _center);
        LichCinematics.Flash(DragonAbyssFx.WithAlpha(DragonAbyssFx.Abyss, 1f), 0.35f, 0.35f);

        var player = DragonAbyssFx.Player(ctx);
        if (player != null)
        {
            Vector3 pos = player.transform.position;
            bool sheltered = IsSheltered(pos);
            Debug.Log($"[DragonVoidFall] 충돌 — 플레이어 {(sheltered ? "기둥 뒤(안전)" : "노출")}", ctx.Monster);
            if (!sheltered && DragonAbyssFx.InDisc(pos, _center, _blastRadius))
                DragonAbyssFx.HitPlayer(ctx, Data.DamageMult, HitWeight.Heavy, ignorePoise: true,
                                        knockbackMult: Data.KnockbackMult, knockFrom: _center);
        }

        foreach (var p in _pillars)
            DragonAbyssFx.Burst(Data.PillarShatterVfxPrefab, p.Base + Vector3.up * (Data.PillarHeight * 0.5f),
                                Quaternion.identity, Data.PillarShatterVfxScale);
        Cleanup();

        _phase = Phase.EndPose;
        _timer = 0f;
    }

    private void Cleanup()
    {
        foreach (var p in _pillars)
        {
            if (p.Root != null) Object.Destroy(p.Root);
            PatternGuideHelper.SafeDestroy(ref p.Sector);
        }
        _pillars.Clear();
        PatternGuideHelper.SafeDestroy(ref _disc);
        if (_meteor != null) Object.Destroy(_meteor);
        _meteor = null;
    }
}
}

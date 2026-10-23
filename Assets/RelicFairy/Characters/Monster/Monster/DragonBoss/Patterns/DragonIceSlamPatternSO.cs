using System.Collections.Generic;
using UnityEngine;

namespace RelicFairy.Monster
{
[CreateAssetMenu(fileName = "DragonIceSlamPattern",
    menuName = "RelicFairy/Boss/Dragon/IceSlamPattern")]
public class DragonIceSlamPatternSO : BossPatternSO
{
    [Header("Cooldown")]
    [SerializeField] private float _cooldown = 35f;

    [Header("Flight to Center")]
    [SerializeField] private float _hoverHeight = 10f;
    [SerializeField] private float _moveSpeed = 7f;
    [SerializeField] private float _centerReachedDistance = 0.6f;
    [SerializeField] private float _airRotationSpeed = 4f;

    [Header("Warning Phase")]
    [SerializeField] private float _warningDuration = 2f;
    [SerializeField] private GameObject _dangerZonePrefab;
    [SerializeField] private float _dangerZoneScale = 15f;
    [SerializeField] private float _dangerZoneHeightOffset = 0.05f;

    [Header("Ice Pillars")]
    [SerializeField] private GameObject _icePillarPrefab;
    [SerializeField] private int _pillarCount = 8;
    [SerializeField] private float _pillarRadius = 5f;
    [SerializeField] private Color _pillarTintColor = new Color(0.5f, 0.85f, 1.0f);

    [Header("Slam")]
    [SerializeField] private float _flyDownSpeed = 14f;
    [SerializeField] private int _slamDamage = 40;
    [SerializeField] private float _damageMultiplier = 1.5f;
    [SerializeField] private float _slamRadius = 7f;
    [SerializeField] private GameObject _slamEffectPrefab;
    [SerializeField] private float _slamEffectScale = 4f;
    [SerializeField] private AudioClip _slamSfx;

    [Header("충격 고리 (10-22 E3 D3)")]
    [Tooltip("착지점 = 플레이어의 이만큼 뒤 자리 (초) — 아레나 안으로 당긴다")]
    [SerializeField] private float _landLeadSeconds = 0.6f;
    [Tooltip("고리가 퍼지는 끝 반경 (m) — 착지점에서 이보다 멀면 안전")]
    [SerializeField] private float _ringMaxRadius = 30f;
    [Tooltip("고리 두께 (m) — 그림 띠 = 판정 띠")]
    [SerializeField] private float _ringThickness = 2.5f;
    [Tooltip("고리가 0 → 끝 반경까지 퍼지는 시간 (초)")]
    [SerializeField] private float _ringSeconds = 1.2f;

    [Header("Animator State Names")]
    [SerializeField] private string _takeoffStateName = "Takeoff";
    [SerializeField] private string _airChaseStateName = "AirChase";
    [SerializeField] private string _flyDownStateName = "URFly Down";
    [SerializeField] private string _landingStateName = "Landing";

    public float       Cooldown                => _cooldown;
    public float       HoverHeight             => _hoverHeight;
    public float       MoveSpeed               => _moveSpeed;
    public float       CenterReachedDistance   => _centerReachedDistance;
    public float       AirRotationSpeed        => _airRotationSpeed;
    public float       WarningDuration         => _warningDuration;
    public GameObject  DangerZonePrefab        => _dangerZonePrefab;
    public float       DangerZoneScale         => _dangerZoneScale;
    public float       DangerZoneHeightOffset  => _dangerZoneHeightOffset;
    public GameObject  IcePillarPrefab         => _icePillarPrefab;
    public int         PillarCount             => _pillarCount;
    public float       PillarRadius            => _pillarRadius;
    public Color       PillarTintColor         => _pillarTintColor;
    public float       FlyDownSpeed            => _flyDownSpeed;
    public int         SlamDamage              => _slamDamage;
    public float       DamageMultiplier        => _damageMultiplier;
    public float       SlamRadius              => _slamRadius;
    public GameObject  SlamEffectPrefab        => _slamEffectPrefab;
    public float       SlamEffectScale         => _slamEffectScale;
    public AudioClip   SlamSfx                 => _slamSfx;
    public float       LandLeadSeconds         => Mathf.Max(0f, _landLeadSeconds);
    public float       RingMaxRadius           => Mathf.Max(1f, _ringMaxRadius);
    public float       RingThickness           => Mathf.Clamp(_ringThickness, 0.5f, RingMaxRadius);
    public float       RingSeconds             => Mathf.Max(0.1f, _ringSeconds);
    public string      TakeoffStateName        => _takeoffStateName;
    public string      AirChaseStateName       => _airChaseStateName;
    public string      FlyDownStateName        => _flyDownStateName;
    public string      LandingStateName        => _landingStateName;

    private DragonIceSlamState _runtimeState;

    public override void Initialize(BossPatternContext ctx)
        => _runtimeState = new DragonIceSlamState(this);

    public override void OnRecycled()
        => _runtimeState?.Reset();

    public override bool CanExecute(BossPatternContext ctx)
    {
        if (ctx.Ctx.Runtime.PlayerTarget == null) return false;
        if (ctx.Blackboard is not DragonBossBlackboard bb) return false;
        return bb.IceSlamCooldown <= 0f;
    }

    public override SpecialStateBase GetRuntimeState() => _runtimeState;
}

// ────────────────────────────────────────────────────────────────────────────
// Runtime state
// ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Phases: Takeoff → MoveToCenter(착지점 위로) → Warning (고리 자리 예고 + ice pillars) →
///         FlyDown (vertical descent) → Landing (퍼지는 충격 고리 · 다 퍼지면 복귀) → Done
/// 10-22 E3 D3 — 착지점 = 플레이어 예측 자리(예전 아레나 중앙 고정) · 판정 = 바깥으로 퍼지는 고리(두께 2.5 m · 1.2초에 r0 → 30).
/// 경기장 전체 판정이라 걸어서 못 피하던 것을 「고리가 오는 순간 대시로 넘기 · 30 m 밖」으로 바꾼다.
/// </summary>
internal sealed class DragonIceSlamState : FullLockState<DragonIceSlamPatternSO>
{
    private enum Phase { Takeoff, MoveToCenter, Warning, FlyDown, Landing, Done }

    private const float LandSnap = 0.1f;   // 바닥 이만큼 위면 닿은 것으로 — 이 순간이 강타 판정

    private Phase                  _phase;
    private float                  _phaseTimer;
    private float                  _targetY;
    private Vector3                _centerPos;
    private int                    _takeoffHash;
    private int                    _flyDownHash;
    private int                    _landingHash;
    private readonly List<GameObject> _spawnedPillars = new();
    private bool                   _damageApplied;
    private GameObject             _ringGuide;       // 고리 자리(반경 RingMaxRadius) — 예고 땐 차오르고, 착지 뒤엔 판정 띠가 바깥으로 퍼진다(10-22 E3 D3)
    private float                  _fillTimer;
    private float                  _fillSeconds;
    private Vector3                _ringCenter;
    private float                  _ringTimer;
    private float                  _ringPrevRadius;
    private bool                   _ringActive;
    private bool                   _ringResolved;    // 이번 내려찍기 판정을 이미 했다(맞음 · 회피 무적 모두 한 번)

    internal DragonIceSlamState(DragonIceSlamPatternSO data) : base(data) { }

    internal void Reset()
    {
        _phase         = Phase.Done;
        _phaseTimer    = 0f;
        _damageApplied = false;
        _spawnedPillars.Clear();
        PatternGuideHelper.SafeDestroy(ref _ringGuide);
        _ringActive   = false;
        _ringResolved = false;
    }

    public override void Enter(MonsterContext ctx)
    {
        GameCameraController.Instance?.DeactivateDragonTopDownView(0.8f);

        if (ctx.Agent != null) ctx.Agent.enabled = false;

        bool alreadyAirborne = (ctx.Monster as IBoss)?.Blackboard is DragonBossBlackboard dragonBb
                               && dragonBb.BodyState == BodyState.Airborne;
        _phase         = alreadyAirborne ? Phase.MoveToCenter : Phase.Takeoff;
        _phaseTimer    = 0f;
        _damageApplied = false;
        _spawnedPillars.Clear();

        float groundY = ctx.Runtime.SpawnPosition.y;
        _targetY   = groundY + Data.HoverHeight;
        _ringActive   = false;
        _ringResolved = false;
        ResolveLandPoint(ctx);   // 땅에서 떠오르면 날아가기 직전에 한 번 더 잡는다(StartMoveToCenter)

        _takeoffHash = Animator.StringToHash(Data.TakeoffStateName);
        _flyDownHash = Animator.StringToHash(Data.FlyDownStateName);
        _landingHash = Animator.StringToHash(Data.LandingStateName);

        if (ctx.Monster is DragonBossMonster dragon)
            dragon.DragonBlackboard.IsAirborne = true;

        if ((ctx.Monster as IBoss)?.Blackboard is DragonBossBlackboard bb)
        {
            bb.IceSlamCooldown = Data.Cooldown;
            // 경기장 전체 내려찍기를 무적 타이밍으로 피하는 판 — 노림 · 큰 메테오(10-14)까지 겹쳐 내려찍기와 0.07초 차로 함께 맞았다(10-15 반응 실측). Exit에서 푼다
            bb.PassiveMeteorSuppressed = true;
        }

        if (alreadyAirborne)
        {
            Vector3 pos = ctx.Transform.position;
            pos.y = _targetY;
            ctx.Transform.position = pos;
            PlayAnim(ctx, Data.AirChaseStateName, 0.1f);
        }
        else
        {
            PlayAnim(ctx, Data.TakeoffStateName, 0.1f);
        }
    }

    public override void Update(MonsterContext ctx)
    {
        _phaseTimer += Time.deltaTime;

        switch (_phase)
        {
            case Phase.Takeoff:      UpdateTakeoff(ctx);      break;
            case Phase.MoveToCenter: UpdateMoveToCenter(ctx); break;
            case Phase.Warning:      UpdateWarning(ctx);      break;
            case Phase.FlyDown:      UpdateFlyDown(ctx);      break;
            case Phase.Landing:      UpdateLanding(ctx);      break;
        }
    }

    public override void Exit(MonsterContext ctx)
    {
        RestoreAgent(ctx);
        PatternGuideHelper.SafeDestroy(ref _ringGuide);
        _ringActive = false;
        CleanupPillars();
        if (ctx.Monster is DragonBossMonster dragon)
            dragon.DragonBlackboard.IsAirborne = false;
        if ((ctx.Monster as IBoss)?.Blackboard is DragonBossBlackboard exitBb)
            exitBb.PassiveMeteorSuppressed = false;
    }

    // ── Phase updates ────────────────────────────────────────────────────────

    private void UpdateTakeoff(MonsterContext ctx)
    {
        FaceCenter(ctx);

        Vector3 p = ctx.Transform.position;
        p.y = Mathf.MoveTowards(p.y, _targetY, ctx.Stat.moveSpeed * 2f * Time.deltaTime);
        ctx.Transform.position = p;

        if (!IsAnimNearEnd(ctx, _takeoffHash)) return;

        p   = ctx.Transform.position;
        p.y = _targetY;
        ctx.Transform.position = p;
        StartMoveToCenter(ctx);
    }

    private void UpdateMoveToCenter(MonsterContext ctx)
    {
        FaceCenter(ctx);

        ctx.Transform.position = Vector3.MoveTowards(
            ctx.Transform.position, _centerPos, Data.MoveSpeed * Time.deltaTime);

        Vector3 cur    = ctx.Transform.position;
        Vector3 center = _centerPos;
        float   flatDist = Mathf.Sqrt(
            (cur.x - center.x) * (cur.x - center.x) +
            (cur.z - center.z) * (cur.z - center.z));

        if (flatDist > Data.CenterReachedDistance) return;

        ctx.Transform.position = _centerPos;
        StartWarning(ctx);
    }

    private void UpdateWarning(MonsterContext ctx)
    {
        TickFill();
        if (_phaseTimer >= Data.WarningDuration)
            StartFlyDown(ctx);
    }

    private void UpdateFlyDown(MonsterContext ctx)
    {
        // 착지 지점의 Y는 Spawn Y가 아닌 실제 바닥 높이를 사용 —
        // 그렇지 않으면 NavMeshAgent.Warp이 바닥과 어긋난 위치에서 실패해 착지 후 이동이 안 됨
        float groundY = DragonPatternFloorUtils.GetFloorY(_centerPos, ctx.Runtime.SpawnPosition.y);
        Vector3 pos = ctx.Transform.position;
        pos.y = Mathf.MoveTowards(pos.y, groundY, Data.FlyDownSpeed * Time.deltaTime);
        ctx.Transform.position = pos;

        // 수직 낙하 자세: 코를 아래로 회전
        Vector3 euler = ctx.Transform.eulerAngles;
        float targetPitch = 90f;
        float newPitch = Mathf.MoveTowardsAngle(euler.x, targetPitch, 270f * Time.deltaTime);
        ctx.Transform.rotation = Quaternion.Euler(newPitch, euler.y, euler.z);

        TickFill();
        if (pos.y > groundY + LandSnap) return;

        StartLanding(ctx);
    }

    private void UpdateLanding(MonsterContext ctx)
    {
        TickRing(ctx);
        // 고리가 다 퍼질 때까지 기다린다 — 착지 클립이 더 짧아도 판정이 끝까지 돈다(수동 운석 멈춤도 그때까지)
        if (_ringActive || !IsAnimNearEnd(ctx, _landingHash)) return;

        RestoreAgent(ctx);
        ReturnToCombat(ctx);
    }

    // ── Phase transitions ────────────────────────────────────────────────────

    private void StartMoveToCenter(MonsterContext ctx)
    {
        _phase      = Phase.MoveToCenter;
        _phaseTimer = 0f;
        ResolveLandPoint(ctx);   // 떠오르는 사이 플레이어가 움직였다 — 날아가기 직전 자리로
        PlayAnim(ctx, Data.AirChaseStateName, 0.15f);
    }

    private void StartWarning(MonsterContext ctx)
    {
        _phase      = Phase.Warning;
        _phaseTimer = 0f;
        SpawnDangerZone(ctx);
        SpawnIcePillars(ctx);
    }

    private void StartFlyDown(MonsterContext ctx)
    {
        _phase      = Phase.FlyDown;
        _phaseTimer = 0f;
        // HasState 가드 없이 강제 CrossFade — URFly Down은 수직 낙하 전용 클립
        if (ctx.Animator != null && !string.IsNullOrEmpty(Data.FlyDownStateName))
        {
            ctx.Animator.speed = 1f;
            ctx.Animator.CrossFade(Data.FlyDownStateName, 0.05f, 0, 0f);
        }
    }

    private void StartLanding(MonsterContext ctx)
    {
        _phase      = Phase.Landing;
        _phaseTimer = 0f;
        // 착지 직전 회전 원상복구 (수직→수평)
        Vector3 euler = ctx.Transform.eulerAngles;
        ctx.Transform.rotation = Quaternion.Euler(0f, euler.y, 0f);
        PlayAnim(ctx, Data.LandingStateName, 0.1f);

        // 바닥에 닿았다 — 고리가 퍼지는 동안 지상(근접 가능 · 10-22 E3 D1·D3). 예전엔 착지 클립이 끝날 때까지 공중 판정이었다
        if (ctx.Monster is DragonBossMonster dragon)
            dragon.DragonBlackboard.IsAirborne = false;

        CleanupPillars();
        StartRing(ctx);
        SpawnSlamEffect(ctx);
        Managers.Sound?.PlayEffectAt(Data.SlamSfx, ctx.Transform.position);
    }

    // ── Effects ──────────────────────────────────────────────────────────────

    private void SpawnDangerZone(MonsterContext ctx)
    {
        float groundY = DragonPatternFloorUtils.GetFloorY(_centerPos, ctx.Runtime.SpawnPosition.y);
        _ringCenter = new Vector3(_centerPos.x, groundY, _centerPos.z);
        Color c       = PatternGuideHelper.AttackFill;   // (10-23 가이드 색 통일) 얼음 색은 기둥 · 이펙트만
        // 예고 = 고리가 지나갈 자리(착지점 둘레 RingMaxRadius) — 다 차는 순간 착지, 그때부터 판정 띠가 바깥으로 퍼진다(10-22 E3 D3)
        PatternGuideHelper.SafeDestroy(ref _ringGuide);
        _ringGuide = PatternGuideHelper.Prepare(
            PatternGuideHelper.Disc(_ringCenter + Vector3.up * Data.DangerZoneHeightOffset, Data.RingMaxRadius, c), c);
        // 채움은 몸이 바닥에 닿는 순간(= 고리 시작)에 끝난다 — 예고 + 내려꽂기(남은 높이 ÷ 낙하 속도)(10-08 점검: 다 찬 뒤 0.57초에 맞았다)
        float descent = Mathf.Max(0f, ctx.Transform.position.y - groundY - LandSnap) / Mathf.Max(0.01f, Data.FlyDownSpeed);
        _fillSeconds = Data.WarningDuration + descent;
        _fillTimer   = 0f;
    }

    private void SpawnIcePillars(MonsterContext ctx)
    {
        if (Data.IcePillarPrefab == null) return;

        int count = Mathf.Max(1, Data.PillarCount);

        for (int i = 0; i < count; i++)
        {
            float   angle  = i * (360f / count) * Mathf.Deg2Rad;
            Vector3 offset = new Vector3(
                Mathf.Cos(angle) * Data.PillarRadius, 0f,
                Mathf.Sin(angle) * Data.PillarRadius);

            Vector3 xzPos  = new Vector3(_centerPos.x + offset.x, 0f, _centerPos.z + offset.z);
            float   groundY = DragonPatternFloorUtils.GetFloorY(xzPos, ctx.Runtime.SpawnPosition.y);
            Vector3 pos    = new Vector3(xzPos.x, groundY, xzPos.z);
            var     pillar = BossEffectPool.Spawn(Data.IcePillarPrefab, pos, Quaternion.identity);
            if (pillar == null) continue;

            TintParticles(pillar, DragonBossVisualHelper.GetElementColor(DragonBossBlackboard.DragonElement.Ice));
            _spawnedPillars.Add(pillar);
        }
    }

    private void TickFill()
    {
        _fillTimer += Time.deltaTime;
        PatternGuideHelper.SetProgress(_ringGuide, _fillTimer / Mathf.Max(0.01f, _fillSeconds));
    }

    /// <summary>고리 시작 — 지나갈 자리는 옅게 남기고 판정 띠(두께 RingThickness)만 판정 색으로 바깥으로 퍼진다(예고 = 판정 · 10-22 E3 D3).</summary>
    private void StartRing(MonsterContext ctx)
    {
        _ringActive     = true;
        _ringResolved   = false;
        _ringTimer      = 0f;
        _ringPrevRadius = 0f;
        PatternGuideHelper.SetFlow(_ringGuide, PatternGuideHelper.Active, Data.RingThickness / Data.RingMaxRadius);
        PatternGuideHelper.SetProgress(_ringGuide, 0f);
        TickRing(ctx);   // 착지 순간 — 발밑(반경 = 두께)부터
    }

    /// <summary>고리를 RingSeconds에 걸쳐 0 → RingMaxRadius로 넓힌다. 다 퍼지면 데칼을 치운다.</summary>
    private void TickRing(MonsterContext ctx)
    {
        if (!_ringActive) return;
        _ringTimer += Time.deltaTime;
        float k = Mathf.Clamp01(_ringTimer / Data.RingSeconds);
        float r = Data.RingMaxRadius * k;
        PatternGuideHelper.SetProgress(_ringGuide, k);
        if (!_ringResolved) CheckRingHit(ctx, _ringPrevRadius, r);
        _ringPrevRadius = r;
        if (k < 1f) return;
        _ringActive = false;
        PatternGuideHelper.SafeDestroy(ref _ringGuide);
    }

    /// <summary>
    /// 판정 = 이번 프레임 띠 [r − 두께, r](앞 프레임 바깥 가장자리부터 — 긴 프레임에 건너뛰지 않게) · 수평 거리.
    /// 한 번만 판정한다(회피 무적으로 넘겼어도 이번 고리는 끝). 빙결은 맞았을 때만.
    /// </summary>
    private void CheckRingHit(MonsterContext ctx, float prevRadius, float radius)
    {
        var player = DragonAbyssFx.Player(ctx);
        if (player == null) return;
        Vector3 d = player.transform.position - _ringCenter;
        d.y = 0f;
        float dist  = d.magnitude;
        float inner = Mathf.Max(0f, Mathf.Min(radius - Data.RingThickness, prevRadius));
        if (dist < inner || dist > radius) return;

        _ringResolved = true;
        if (DragonAbyssFx.HitPlayer(ctx, Data.DamageMultiplier, HitWeight.Heavy))   // 충격 고리 — 강
            Data.playerStatusEffect?.Apply(player);
    }

    private void SpawnSlamEffect(MonsterContext ctx)
    {
        var prefab = Data.SlamEffectPrefab;
        if (prefab == null) return;

        float   groundY = DragonPatternFloorUtils.GetFloorY(_centerPos, ctx.Runtime.SpawnPosition.y);
        Vector3 pos     = new Vector3(_centerPos.x, groundY + 0.1f, _centerPos.z);

        var effect = BossEffectPool.Spawn(prefab, pos, Quaternion.identity);
        if (effect == null) return;

        // 착지 충격 = 발밑 띠 크기(두께) — 판정은 퍼지는 고리라 경기장 크기로 키우지 않는다(10-22 E3 D3 · 예전 × 경기장 반경/SlamRadius)
        float scale = Data.SlamRadius > 0.01f
            ? Data.SlamEffectScale * (Data.RingThickness / Data.SlamRadius)
            : Data.SlamEffectScale;
        effect.transform.localScale = Vector3.one * scale;
        BossEffectPool.ScheduleRelease(effect, 3f);
    }

    private void CleanupPillars()
    {
        foreach (var pillar in _spawnedPillars)
        {
            if (pillar != null)
                BossEffectPool.Release(pillar);
        }
        _spawnedPillars.Clear();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>착지점 = 플레이어의 LandLeadSeconds초 뒤 자리 — 몸이 벽 안면 안에 들어오게 당긴다(10-22 E3 D3 · 예전엔 아레나 중앙 고정).</summary>
    private void ResolveLandPoint(MonsterContext ctx)
    {
        Vector3 p = ctx.Runtime.PlayerTarget != null
            ? DragonThreat.PredictPlayer(ctx, Data.LandLeadSeconds)
            : DragonBossRoomContext.WorldCenter;
        p = DragonPatternFloorUtils.ClampInsideWalls(p, DragonPatternFloorUtils.BodyRadiusOf(ctx));
        _centerPos = new Vector3(p.x, _targetY, p.z);
    }

    private void FaceCenter(MonsterContext ctx)
    {
        Vector3 dir = _centerPos - ctx.Transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f) return;

        ctx.Transform.rotation = Quaternion.Slerp(
            ctx.Transform.rotation,
            Quaternion.LookRotation(dir),
            Data.AirRotationSpeed * Time.deltaTime);
    }

    private static void TintParticles(GameObject go, Color color)
    {
        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
        {
            var main = ps.main;
            main.startColor = new ParticleSystem.MinMaxGradient(color);

            var col = ps.colorOverLifetime;
            if (col.enabled)
            {
                var existing = col.color.gradient ?? col.color.gradientMax;
                var alphaKeys = existing != null
                    ? existing.alphaKeys
                    : new GradientAlphaKey[] { new(1f, 0f), new(0f, 1f) };

                var grad = new Gradient();
                grad.SetKeys(
                    new GradientColorKey[] { new(color, 0f), new(color, 1f) },
                    alphaKeys);
                col.color = new ParticleSystem.MinMaxGradient(grad);
            }
        }

        // 회색 텍스처를 가진 메시 파티클(FlyingRocks 등)은 BaseColor 곱셈만으로는
        // 얼음색이 나오지 않으므로 EmissionColor로 강제 발광 추가
        Color emissionColor = new Color(color.r * 1.2f, color.g * 1.2f, color.b * 1.5f, 1f);
        var mpb = new MaterialPropertyBlock();
        foreach (var rend in go.GetComponentsInChildren<Renderer>(true))
        {
            rend.GetPropertyBlock(mpb);
            mpb.SetColor("_BaseColor", color);
            mpb.SetColor("_Color", color);
            mpb.SetColor("_EmissionColor", emissionColor);
            rend.SetPropertyBlock(mpb);
        }
    }

    private static void PlayAnim(MonsterContext ctx, string stateName, float fadeDuration)
    {
        if (ctx.Animator == null || string.IsNullOrEmpty(stateName)) return;
        int hash = Animator.StringToHash(stateName);
        if (!ctx.Animator.HasState(0, hash)) return;
        ctx.Animator.speed = 1f;
        ctx.Animator.CrossFade(stateName, fadeDuration, 0, 0f);
    }

    private static bool IsAnimNearEnd(MonsterContext ctx, int hash)
    {
        if (ctx.Animator == null) return true;
        if (!ctx.Animator.HasState(0, hash)) return true;
        if (ctx.Animator.IsInTransition(0)) return false;
        var info = ctx.Animator.GetCurrentAnimatorStateInfo(0);
        if (info.shortNameHash != hash) return true;
        return info.normalizedTime >= 0.9f;
    }

    private static void RestoreAgent(MonsterContext ctx)
        => DragonPatternFloorUtils.SnapToFloorAndRestoreAgent(ctx);

    private static void ReturnToCombat(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null || ctx.Monster.IsPlayerDead())
        {
            ctx.Monster.ChangeState<PatrolState>();
            return;
        }

        if (ctx.Monster is not DragonBossMonster dragon)
        {
            ctx.Monster.ChangeState<ChaseState>();
            return;
        }

        if (ctx.Runtime.DistToPlayer > dragon.WalkToRunThreshold)
            ctx.Monster.ChangeState<DragonRunChaseState>();
        else
            ctx.Monster.ChangeState<ChaseState>();
    }
}
}

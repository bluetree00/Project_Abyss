using System.Collections.Generic;
using UnityEngine;

namespace Abyss.Monster
{
[CreateAssetMenu(fileName = "DragonIceSlamPattern",
    menuName = "Abyss/Boss/Dragon/IceSlamPattern")]
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
    [SerializeField] private Color _pillarTintColor = new Color(0.4f, 0.8f, 1.0f);

    [Header("Slam")]
    [SerializeField] private float _flyDownSpeed = 14f;
    [SerializeField] private int _slamDamage = 40;
    [SerializeField] private float _slamRadius = 7f;
    [SerializeField] private GameObject _slamEffectPrefab;
    [SerializeField] private float _slamEffectScale = 4f;

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
    public float       SlamRadius              => _slamRadius;
    public GameObject  SlamEffectPrefab        => _slamEffectPrefab;
    public float       SlamEffectScale         => _slamEffectScale;
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
/// Phases: Takeoff → MoveToCenter → Warning (danger zone + ice pillars) →
///         FlyDown (vertical descent) → Landing (AoE damage + cleanup) → Done
/// </summary>
internal sealed class DragonIceSlamState : FullLockState<DragonIceSlamPatternSO>
{
    private enum Phase { Takeoff, MoveToCenter, Warning, FlyDown, Landing, Done }

    private Phase                  _phase;
    private float                  _phaseTimer;
    private float                  _targetY;
    private Vector3                _centerPos;
    private int                    _takeoffHash;
    private int                    _flyDownHash;
    private int                    _landingHash;
    private readonly List<GameObject> _spawnedPillars = new();
    private bool                   _damageApplied;

    internal DragonIceSlamState(DragonIceSlamPatternSO data) : base(data) { }

    internal void Reset()
    {
        _phase         = Phase.Done;
        _phaseTimer    = 0f;
        _damageApplied = false;
        _spawnedPillars.Clear();
    }

    public override void Enter(MonsterContext ctx)
    {
        if (ctx.Agent != null) ctx.Agent.enabled = false;

        _phase         = Phase.Takeoff;
        _phaseTimer    = 0f;
        _damageApplied = false;
        _spawnedPillars.Clear();

        float groundY = ctx.Runtime.SpawnPosition.y;
        _targetY   = groundY + Data.HoverHeight;
        _centerPos = new Vector3(ctx.Runtime.SpawnPosition.x, _targetY, ctx.Runtime.SpawnPosition.z);

        _takeoffHash = Animator.StringToHash(Data.TakeoffStateName);
        _flyDownHash = Animator.StringToHash(Data.FlyDownStateName);
        _landingHash = Animator.StringToHash(Data.LandingStateName);

        if (ctx.Monster is DragonBossMonster dragon)
            dragon.DragonBlackboard.IsAirborne = true;

        if ((ctx.Monster as IBoss)?.Blackboard is DragonBossBlackboard bb)
            bb.IceSlamCooldown = Data.Cooldown;

        PlayAnim(ctx, Data.TakeoffStateName, 0.1f);
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
        CleanupPillars();
        if (ctx.Monster is DragonBossMonster dragon)
            dragon.DragonBlackboard.IsAirborne = false;
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
        if (_phaseTimer >= Data.WarningDuration)
            StartFlyDown(ctx);
    }

    private void UpdateFlyDown(MonsterContext ctx)
    {
        float groundY = ctx.Runtime.SpawnPosition.y;
        Vector3 pos = ctx.Transform.position;
        pos.y = Mathf.MoveTowards(pos.y, groundY, Data.FlyDownSpeed * Time.deltaTime);
        ctx.Transform.position = pos;

        // 수직 낙하 자세: 코를 아래로 회전
        Vector3 euler = ctx.Transform.eulerAngles;
        float targetPitch = 90f;
        float newPitch = Mathf.MoveTowardsAngle(euler.x, targetPitch, 270f * Time.deltaTime);
        ctx.Transform.rotation = Quaternion.Euler(newPitch, euler.y, euler.z);

        if (pos.y > groundY + 0.1f) return;

        StartLanding(ctx);
    }

    private void UpdateLanding(MonsterContext ctx)
    {
        if (!IsAnimNearEnd(ctx, _landingHash)) return;

        RestoreAgent(ctx);
        ReturnToCombat(ctx);
    }

    // ── Phase transitions ────────────────────────────────────────────────────

    private void StartMoveToCenter(MonsterContext ctx)
    {
        _phase      = Phase.MoveToCenter;
        _phaseTimer = 0f;
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

        CleanupPillars();
        ApplySlamDamage(ctx);
        SpawnSlamEffect(ctx);
    }

    // ── Effects ──────────────────────────────────────────────────────────────

    private void SpawnDangerZone(MonsterContext ctx)
    {
        if (Data.DangerZonePrefab == null) return;

        float   groundY  = ctx.Runtime.SpawnPosition.y;
        Vector3 pos      = new Vector3(_centerPos.x, groundY + Data.DangerZoneHeightOffset, _centerPos.z);
        float   lifetime = Data.WarningDuration + 5f;

        var zone = BossEffectPool.SpawnOneShot(
            Data.DangerZonePrefab, pos, Quaternion.identity,
            fallbackLifetime: lifetime);

        if (zone != null)
            zone.transform.localScale = Vector3.one * Data.DangerZoneScale;
    }

    private void SpawnIcePillars(MonsterContext ctx)
    {
        if (Data.IcePillarPrefab == null) return;

        float groundY = ctx.Runtime.SpawnPosition.y;
        int   count   = Mathf.Max(1, Data.PillarCount);

        for (int i = 0; i < count; i++)
        {
            float   angle  = i * (360f / count) * Mathf.Deg2Rad;
            Vector3 offset = new Vector3(
                Mathf.Cos(angle) * Data.PillarRadius, 0f,
                Mathf.Sin(angle) * Data.PillarRadius);

            Vector3 pos    = new Vector3(_centerPos.x + offset.x, groundY, _centerPos.z + offset.z);
            var     pillar = BossEffectPool.Spawn(Data.IcePillarPrefab, pos, Quaternion.identity);
            if (pillar == null) continue;

            TintParticles(pillar, Data.PillarTintColor);
            _spawnedPillars.Add(pillar);
        }
    }

    private void ApplySlamDamage(MonsterContext ctx)
    {
        float   groundY    = ctx.Runtime.SpawnPosition.y;
        Vector3 slamCenter = new Vector3(_centerPos.x, groundY + 0.5f, _centerPos.z);

        var hits = Physics.OverlapSphere(slamCenter, Data.SlamRadius);
        foreach (var col in hits)
        {
            var player = col.GetComponent<PlayerController>()
                      ?? col.GetComponentInParent<PlayerController>();
            if (player == null) continue;

            player.TakeDamage(Data.SlamDamage);
            Data.playerStatusEffect?.Apply(player);
            break;
        }
    }

    private void SpawnSlamEffect(MonsterContext ctx)
    {
        var prefab = Data.SlamEffectPrefab;
        if (prefab == null) return;

        float   groundY = ctx.Runtime.SpawnPosition.y;
        Vector3 pos     = new Vector3(_centerPos.x, groundY + 0.1f, _centerPos.z);

        var effect = BossEffectPool.Spawn(prefab, pos, Quaternion.identity);
        if (effect == null) return;

        effect.transform.localScale = Vector3.one * Data.SlamEffectScale;
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
    {
        if (ctx.Agent == null || ctx.Agent.enabled) return;
        ctx.Agent.enabled = true;
        ctx.Agent.Warp(ctx.Transform.position);
    }

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

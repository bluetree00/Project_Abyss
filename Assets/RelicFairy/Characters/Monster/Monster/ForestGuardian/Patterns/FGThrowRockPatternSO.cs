using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// ForestGuardian 돌 던지기 (ThrowRock) 패턴.
///
/// 흐름  : Windup(집어들기+조준) → PostThrow(비행 감시) → Recovery
/// 타이밍: MagicAttack1 클립 전체 길이 기준. throwNormalizedTime 에 발사.
/// 착지  : MonsterProjectile 소멸 감지 → 경고장판 즉시 제거 + 이펙트 생성.
///         타임아웃 안전망으로 _postThrowDuration 후에도 진행.
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/Boss/ForestGuardian/FG_ThrowRockPattern", fileName = "FG_ThrowRockPattern")]
public class FGThrowRockPatternSO : BossPatternSO
{
    // ── 범위 ──────────────────────────────────────────────
    [Header("ThrowRock — Range")]
    [Tooltip("최소 발동 거리 (m)")]
    public float minRange = 5f;

    [Tooltip("최대 발동 거리 (m)")]
    public float maxRange = 10f;

    // ── 애니메이션 타이밍 ─────────────────────────────────
    [Header("ThrowRock — Animation Timing")]
    [Tooltip("MagicAttack1 클립 내 돌 발사 기준 시점 (0~1). 팔을 뻗는 모션 시작점 기준.")]
    public float throwNormalizedTime = 0.30f;

    [Tooltip("throwNormalizedTime 도달 후 실제 발사까지 추가 대기 시간 (초). 양손 잡기 모션이 끝나는 지점에 맞게 조정.")]
    public float throwExtraDelay = 1.0f;

    [Tooltip("클립 재생 완료 후 ChaseState 전환까지 추가 대기 시간 (초)")]
    public float recoveryDuration = 0.4f;

    // ── 투사체 ────────────────────────────────────────────
    [Header("ThrowRock — Projectile")]
    [Tooltip("투사체 비행 속도 (m/s)")]
    public float projectileSpeed = 15f;

    [Tooltip("던지는 손 본 이름 (Generic 아바타용, 예: TreantLPalm)")]
    public string throwHandBoneName = "Hand_R";

    [Tooltip("손이 바위에 이 거리 이내로 들어오면 잡기 (m)")]
    public float grabThreshold = 1.0f;

    // ── 데미지 ────────────────────────────────────────────
    [Header("ThrowRock — Damage")]
    [Tooltip("기본 attackPower 배율")]
    public float damageMultiplier = 1.5f;

    [Tooltip("넉백 힘 배율")]
    public float knockbackMultiplier = 2f;

    // ── 비주얼 ────────────────────────────────────────────
    [Header("ThrowRock — Visual")]
    [Tooltip("돌 투사체 프리팹 (MonsterProjectile 컴포넌트 포함)")]
    public GameObject rockPrefab;

    [Tooltip("투사체 스케일 (XYZ)")]
    public Vector3 rockScale = Vector3.one;

    [Tooltip("착지 경고장판 프리팹 (DiscMeshWarning 포함). null이면 effectPrefab 사용.")]
    public GameObject warningZonePrefab;

    [Tooltip("경고장판 반경 (m)")]
    public float warningRadius = 2.5f;

    [Tooltip("돌 착지 시 재생할 이펙트 프리팹 (VFX/파티클 등). null이면 재생 안 함.")]
    public GameObject impactEffectPrefab;

    // ── 런타임 ────────────────────────────────────────────
    private FGThrowRockState _state;

    public override void Initialize(BossPatternContext ctx)  => _state = new FGThrowRockState(this);
    public override void OnRecycled()                        => _state = new FGThrowRockState(this);

    public override bool CanExecute(BossPatternContext ctx)
    {
        if (ctx.Ctx.Runtime.PlayerTarget == null) return false;
        float dist = Vector3.Distance(
            ctx.Ctx.Transform.position,
            ctx.Ctx.Runtime.PlayerTarget.position);
        return dist >= minRange && dist <= maxRange;
    }

    public override SpecialStateBase GetRuntimeState() => _state;

    internal GameObject ResolveWarningPrefab()
        => warningZonePrefab != null ? warningZonePrefab : effectPrefab;
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// FGThrowRockState — FullLock (이동 + 중단 불가)
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class FGThrowRockState : FullLockState<FGThrowRockPatternSO>
{
    private const string AnimThrow         = "MagicAttack1";
    private const float  WindupFallback    = 0.8f;
    private const float  PostThrowFallback = 0.8f;

    private enum Phase { Windup, PostThrow, Recovery }

    private Phase              _phase;
    private float              _timer;
    private GameObject         _rockGO;
    private GameObject         _warningGO;
    private MonsterProjectile  _proj;           // 발사 후 소멸 감지용 레퍼런스
    private Vector3            _rockSpawnPos;
    private Vector3            _landingPos;     // SpawnWarning 시 저장 → 착지 이펙트 위치
    private float              _windupDuration;
    private float              _postThrowDuration;
    private float              _launchDist;
    private Vector3            _warningTargetScale;
    private float              _warningGrowDuration; // 경고 장판 확장 기준 시간
    private float              _warningGrowTimer;    // 확장 시작 시점부터의 경과 시간
    private Transform          _handBone;           // 손 본 캐시 (Generic 아바타용)
    private bool               _grabbed;            // 바위가 손에 부착됐는지

    public FGThrowRockState(FGThrowRockPatternSO data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        _phase    = Phase.Windup;
        _timer    = 0f;
        _proj     = null;
        _grabbed  = false;
        _handBone = FindBoneRecursive(ctx.Transform, Data.throwHandBoneName);

        if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
        {
            ctx.Agent.isStopped = true;
            ctx.Agent.ResetPath();
        }

        float clipLen = GetClipLength(ctx, AnimThrow);
        if (clipLen > 0f)
        {
            float t            = Mathf.Clamp01(Data.throwNormalizedTime);
            // throwExtraDelay 를 더해 실제 발사 시점을 늦춤 (양손 잡기 모션 완료 후)
            _windupDuration    = clipLen * t + Data.throwExtraDelay;
            _postThrowDuration = Mathf.Max(0.2f, clipLen * (1f - t) - Data.throwExtraDelay);
        }
        else
        {
            _windupDuration    = WindupFallback + Data.throwExtraDelay;
            _postThrowDuration = PostThrowFallback;
        }

        FacePlayer(ctx);
        SpawnRock(ctx);
        PlayAnim(ctx, AnimThrow);
    }

    public override void Update(MonsterContext ctx)
    {
        _timer += Time.deltaTime * SpeedMult(ctx);

        switch (_phase)
        {
            case Phase.Windup:
                TryGrabRock();   // 손이 바위에 충분히 가까워지면 부착
                FacePlayer(ctx);

                if (_timer >= _windupDuration)
                {
                    LaunchRock(ctx);
                    // 비행 시간을 PostThrow 최소값으로 보장 (안전망)
                    float flightTime = Data.projectileSpeed > 0f ? _launchDist / Data.projectileSpeed : 0f;
                    _postThrowDuration = Mathf.Max(_postThrowDuration, flightTime + 0.15f);
                    _timer = 0f;
                    _phase = Phase.PostThrow;
                }
                break;

            case Phase.PostThrow:
                // 경고 장판 서서히 커지기 (착지 위치 중심에서 서서히 확장)
                if (_warningGO != null && _warningGrowDuration > 0f)
                {
                    _warningGrowTimer += Time.deltaTime * SpeedMult(ctx);
                    float wt = Mathf.Clamp01(_warningGrowTimer / _warningGrowDuration);
                    _warningGO.transform.localScale = Vector3.Lerp(Vector3.zero, _warningTargetScale, wt);
                }

                // 투사체 소멸 감지 — Destroy 호출 직후 Unity null 체크가 true
                bool rockLanded = _proj == null;
                // 타임아웃 안전망 (투사체가 소멸하지 않아도 진행)
                bool timedOut   = _timer >= _postThrowDuration;

                if (rockLanded || timedOut)
                    OnRockLanded();
                break;

            case Phase.Recovery:
                if (_timer >= Data.recoveryDuration)
                    ctx.Monster.ChangeState<ChaseState>();
                break;
        }
    }

    public override void Exit(MonsterContext ctx)
    {
        DespawnRock();
        DespawnWarning();

        if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
            ctx.Agent.isStopped = false;
    }

    // ── 착지 처리 (경고장판 제거 + 이펙트) ─────────────────
    private void OnRockLanded()
    {
        SpawnImpactEffect();
        DespawnWarning();
        _proj  = null;
        _timer = 0f;
        _phase = Phase.Recovery;
    }

    // ── 돌 생성 (보스 앞 바닥에 배치) ──────────────────────
    private void SpawnRock(MonsterContext ctx)
    {
        if (Data.rockPrefab == null) return;

        _rockSpawnPos   = ctx.Transform.position + ctx.Transform.forward * 1.5f;
        _rockSpawnPos.y = ctx.Transform.position.y + 0.3f;
        _rockGO = Object.Instantiate(Data.rockPrefab, _rockSpawnPos, Quaternion.identity);

        Vector3 s = Data.rockScale;
        if (s.x > 0f && s.y > 0f && s.z > 0f)
            _rockGO.transform.localScale = s;
    }

    // ── 손이 바위에 닿으면 부착 ──────────────────────────
    private void TryGrabRock()
    {
        if (_grabbed || _rockGO == null || _handBone == null) return;

        float dist = Vector3.Distance(_rockGO.transform.position, _handBone.position);
        if (dist > Data.grabThreshold) return;

        _rockGO.transform.SetParent(_handBone, worldPositionStays: true);
        _grabbed = true;
    }

    // ── 투사체 발사 ─────────────────────────────────────
    private void LaunchRock(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null) return;

        _landingPos = ctx.Runtime.PlayerTarget.position;
        SpawnWarning(_landingPos);

        if (_rockGO == null) return;

        // 손 본에서 분리하여 독립 투사체로 전환
        _rockGO.transform.SetParent(null);

        Vector3 toTarget = _landingPos - _rockGO.transform.position;
        _launchDist = toTarget.magnitude;
        Vector3 dir  = _launchDist > 0.001f ? toTarget / _launchDist : ctx.Transform.forward;

        if (!_rockGO.TryGetComponent<MonsterProjectile>(out var proj)) return;

        int   dmg = ctx.Config?.stat != null
            ? Mathf.Max(1, (int)(ctx.Config.stat.attackPower * Data.damageMultiplier))
            : 1;
        float kb  = (ctx.Config?.stat?.knockbackForce ?? 8f) * Data.knockbackMultiplier;

        // maxRange = 실제 거리 → 경고장판 중앙 도달 시 자동 소멸
        proj.Init(dir, Data.projectileSpeed, _launchDist, dmg, kb);
        _proj  = proj;   // 소멸 감지용 레퍼런스 보존
        _rockGO = null;  // 이후 Transform 조작 차단
    }

    // ── 경고장판 ─────────────────────────────────────────
    private void SpawnWarning(Vector3 landPos)
    {
        var prefab = Data.ResolveWarningPrefab();
        if (prefab == null) return;

        Vector3 pos = landPos;
        pos.y += 0.02f;
        float s = Data.warningRadius;
        _warningTargetScale  = new Vector3(s, 1f, s);
        _warningGrowTimer    = 0f;
        // 비행 시간만큼 장판이 서서히 커지도록 확장 시간 설정 (최소 0.3s)
        _warningGrowDuration = Mathf.Max(0.3f, Data.projectileSpeed > 0f ? _launchDist / Data.projectileSpeed : 0.5f);
        _warningGO = Object.Instantiate(prefab, pos, Quaternion.identity);
        _warningGO.transform.localScale = Vector3.zero;  // 처음엔 0 → PostThrow에서 서서히 확장
    }

    private void DespawnWarning()
    {
        if (_warningGO == null) return;
        Object.Destroy(_warningGO);
        _warningGO = null;
    }

    private void DespawnRock()
    {
        if (_rockGO == null) return;
        Object.Destroy(_rockGO);
        _rockGO = null;
    }

    // ── 착지 이펙트 ──────────────────────────────────────
    private void SpawnImpactEffect()
    {
        if (Data.impactEffectPrefab == null) return;
        var go = Object.Instantiate(Data.impactEffectPrefab, _landingPos, Quaternion.identity);

        float lifetime = 3f;
        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
        {
            var main = ps.main;
            main.loop = false;
            float end = main.duration + main.startLifetime.constantMax;
            if (end > lifetime) lifetime = end;
        }

        Object.Destroy(go, lifetime);
    }

    // ── 유틸 ─────────────────────────────────────────────
    private static void FacePlayer(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null) return;
        Vector3 dir = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.001f)
            ctx.Transform.rotation = Quaternion.LookRotation(dir);
    }

    private static void PlayAnim(MonsterContext ctx, string stateName)
    {
        if (ctx.Animator == null) return;
        if (!ctx.Animator.HasState(0, Animator.StringToHash(stateName)))
        {
            Debug.LogWarning($"[FGThrowRock] Animator state not found: '{stateName}'", ctx.Monster);
            return;
        }
        ctx.Animator.CrossFade(stateName, 0.1f, 0, 0f);
    }

    private static float GetClipLength(MonsterContext ctx, string clipName)
    {
        if (ctx.Animator == null) return 0f;
        var clips = ctx.Animator.runtimeAnimatorController?.animationClips;
        if (clips == null) return 0f;
        foreach (var clip in clips)
            if (clip.name == clipName) return clip.length;
        return 0f;
    }

    private static float SpeedMult(MonsterContext ctx)
    {
        var fg = ctx.Monster as ForestGuardianMonster;
        return fg?.FGBlackboard.AnimSpeedMult ?? 1f;
    }

    private static Transform FindBoneRecursive(Transform parent, string boneName)
    {
        if (string.IsNullOrEmpty(boneName)) return null;
        foreach (Transform child in parent)
        {
            if (child.name == boneName) return child;
            var found = FindBoneRecursive(child, boneName);
            if (found != null) return found;
        }
        return null;
    }
}
}

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

    [Tooltip("MagicAttack1 클립 내 돌을 강제로 손에 붙이는 시점 (0~1). 이 시점에 근접 여부 무관하게 손에 부착.")]
    public float grabNormalizedTime = 0.15f;

    [Tooltip("손이 바위에 이 거리 이내로 들어오면 조기 잡기 (m). grabNormalizedTime 이전에 손이 가까워지면 먼저 잡음.")]
    public float grabThreshold = 1.0f;

    // ── 데미지 ────────────────────────────────────────────
    [Header("ThrowRock — Damage")]
    [Tooltip("기본 attackPower 배율")]
    public float damageMultiplier = 1.5f;

    [Tooltip("넉백 힘 배율")]
    public float knockbackMultiplier = 2f;

    // ── 비주얼 ────────────────────────────────────────────
    [Header("ThrowRock — Sound")]
    [Tooltip("돌이 바닥에 착지할 때 재생할 사운드 (메인 + 파편 공용)")]
    public AudioClip stoneLandSfx;

    [Header("ThrowRock — Fragment Shards")]
    [Tooltip("파편 3개의 방향 각도 (도). 인덱스 0~2가 각 파편에 대응. VFX 파편 방향에 맞게 개별 조정.")]
    public float[] fragmentAngles = { 0f, 120f, 240f };
    [Tooltip("전체 파편 방향을 일괄 회전하는 오프셋 (도). 양수 = 시계방향. VFX와 맞지 않을 때 이 값만 조정.")]
    public float fragmentAngleOffset = 0f;
    [Tooltip("메인 착지점에서 각 파편 착지점까지 거리 (m). VFX 파편 비행 거리에 맞게 조정.")]
    public float fragmentSpreadRadius = 3.5f;
    [Tooltip("파편 경고장판 반경 (m)")]
    public float fragmentWarningRadius = 1.5f;
    [Tooltip("메인 착지 후 경고장판이 나타나기까지 지연 시간 (초). VFX가 파편을 발사하는 타이밍에 맞게 조정.")]
    public float fragmentWarningStartDelay = 0.3f;
    [Tooltip("경고장판 출현 후 파편이 착지할 때까지 시간 (초). VFX 파편 비행 시간에 맞게 조정.")]
    public float fragmentLandDelay = 1.2f;
    [Tooltip("파편 attackPower 배율")]
    public float fragmentDamageMultiplier = 0.8f;
    [Tooltip("파편 착지 시 재생할 이펙트 프리팹 (null이면 없음)")]
    public GameObject fragmentImpactEffectPrefab;

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

    private enum Phase { Windup, PostThrow, Fragment, Recovery }

    private const int FragmentCount = 3;

    private Phase              _phase;
    private float              _timer;
    private GameObject         _rockGO;
    private GameObject         _warningGO;
    private MonsterProjectile  _proj;
    private Vector3            _rockSpawnPos;
    private Vector3            _landingPos;
    private float              _windupDuration;
    private float              _grabDuration;
    private float              _postThrowDuration;
    private float              _launchDist;
    private Vector3            _warningTargetScale;
    private float              _warningGrowDuration;
    private float              _warningGrowTimer;
    private Transform          _handBone;
    private bool               _grabbed;
    private readonly GameObject[] _fragmentWarnings  = new GameObject[FragmentCount];
    private readonly Vector3[]    _fragmentPositions = new Vector3[FragmentCount];
    private float                 _fragmentTimer;
    private bool                  _fragmentWarningsSpawned;
    private bool                  _fragmentDamaged;

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
            _grabDuration      = clipLen * Mathf.Clamp01(Data.grabNormalizedTime);
            _windupDuration    = clipLen * t + Data.throwExtraDelay;
            _postThrowDuration = Mathf.Max(0.2f, clipLen * (1f - t) - Data.throwExtraDelay);
        }
        else
        {
            _grabDuration      = WindupFallback * Mathf.Clamp01(Data.grabNormalizedTime);
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
                TryGrabRock();
                if (!_grabbed && _timer >= _grabDuration)
                    ForceGrabRock();  // grabNormalizedTime 도달 시 거리 무관하게 강제 부착
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
                    OnRockLanded(ctx);
                break;

            case Phase.Fragment:
                _fragmentTimer += Time.deltaTime * SpeedMult(ctx);

                // 경고장판 등장 딜레이 — VFX가 파편을 '발사'하는 타이밍
                if (!_fragmentWarningsSpawned && _fragmentTimer >= Data.fragmentWarningStartDelay)
                {
                    _fragmentWarningsSpawned = true;
                    SpawnFragmentWarnings();
                }

                // 경고장판 서서히 확장
                if (_fragmentWarningsSpawned && Data.fragmentLandDelay > 0f)
                {
                    float growElapsed = _fragmentTimer - Data.fragmentWarningStartDelay;
                    float ft = Mathf.Clamp01(growElapsed / Data.fragmentLandDelay);
                    float fs = Data.fragmentWarningRadius;
                    for (int i = 0; i < FragmentCount; i++)
                    {
                        if (_fragmentWarnings[i] != null)
                            _fragmentWarnings[i].transform.localScale = Vector3.Lerp(Vector3.zero, new Vector3(fs, 1f, fs), ft);
                    }
                }

                // 파편 착지 — 피격판정 + 사운드 + 이펙트
                if (!_fragmentDamaged && _fragmentTimer >= Data.fragmentWarningStartDelay + Data.fragmentLandDelay)
                {
                    _fragmentDamaged = true;
                    for (int i = 0; i < FragmentCount; i++)
                    {
                        if (Data.stoneLandSfx != null)
                            Managers.Sound?.PlayEffectAt(Data.stoneLandSfx, _fragmentPositions[i]);
                        SpawnFragmentImpactEffect(_fragmentPositions[i]);
                        TryDealFragmentDamage(ctx, _fragmentPositions[i]);
                        DespawnFragmentWarning(i);
                    }
                    _timer = 0f;
                    _phase = Phase.Recovery;
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
        DespawnRock();
        DespawnWarning();
        DespawnAllFragmentWarnings();

        if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
            ctx.Agent.isStopped = false;
    }

    // ── 착지 처리 (경고장판 제거 + 이펙트 + 사운드 → 파편 페이즈) ──
    private void OnRockLanded(MonsterContext ctx)
    {
        SpawnImpactEffect();
        if (Data.stoneLandSfx != null)
            Managers.Sound?.PlayEffectAt(Data.stoneLandSfx, _landingPos);
        DespawnWarning();
        _proj                    = null;
        _fragmentTimer           = 0f;
        _fragmentWarningsSpawned = false;
        _fragmentDamaged         = false;
        _timer = 0f;
        _phase = Phase.Fragment;
    }

    // ── 돌 생성 (보스 앞 바닥에 배치) ──────────────────────
    private void SpawnRock(MonsterContext ctx)
    {
        if (Data.rockPrefab == null) return;

        _rockSpawnPos   = ctx.Transform.position + ctx.Transform.forward * 1.5f;
        _rockSpawnPos.y = ctx.Transform.position.y + 0.3f;
        _rockGO = Object.Instantiate(Data.rockPrefab, _rockSpawnPos, Quaternion.identity);

        // 발사(Init) 전까지 중력/충돌로 위치가 밀리면 TryGrabRock 거리 체크 실패 → kinematic으로 고정
        if (_rockGO.TryGetComponent<Rigidbody>(out var rb))
            rb.isKinematic = true;

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

    // ── grabNormalizedTime 도달 시 거리 무관 강제 부착 ──
    private void ForceGrabRock()
    {
        if (_grabbed || _rockGO == null) return;

        if (_handBone != null)
        {
            _rockGO.transform.SetParent(_handBone, worldPositionStays: false);
            _rockGO.transform.localPosition = Vector3.zero;
        }
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

    // ── 파편 경고장판 ────────────────────────────────────
    private void SpawnFragmentWarnings()
    {
        var   prefab  = Data.ResolveWarningPrefab();
        float r       = Data.fragmentSpreadRadius;
        var   angles  = Data.fragmentAngles;
        for (int i = 0; i < FragmentCount; i++)
        {
            float angle = ((angles != null && i < angles.Length) ? angles[i] : i * (360f / FragmentCount)) + Data.fragmentAngleOffset;
            float rad   = angle * Mathf.Deg2Rad;
            Vector3 offset = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad)) * r;
            Vector3 pos    = _landingPos + offset;
            _fragmentPositions[i] = pos;

            if (prefab == null) continue;
            pos.y += 0.02f;
            _fragmentWarnings[i] = Object.Instantiate(prefab, pos, Quaternion.identity);
            _fragmentWarnings[i].transform.localScale = Vector3.zero;
        }
    }

    private void SpawnFragmentImpactEffect(Vector3 pos)
    {
        if (Data.fragmentImpactEffectPrefab == null) return;
        var go = Object.Instantiate(Data.fragmentImpactEffectPrefab, pos, Quaternion.identity);
        float lifetime = 2f;
        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
        {
            var main = ps.main;
            main.loop = false;
            float end = main.duration + main.startLifetime.constantMax;
            if (end > lifetime) lifetime = end;
        }
        Object.Destroy(go, lifetime);
    }

    private void DespawnFragmentWarning(int i)
    {
        if (_fragmentWarnings[i] == null) return;
        Object.Destroy(_fragmentWarnings[i]);
        _fragmentWarnings[i] = null;
    }

    private void DespawnAllFragmentWarnings()
    {
        for (int i = 0; i < FragmentCount; i++)
            DespawnFragmentWarning(i);
    }

    private void TryDealFragmentDamage(MonsterContext ctx, Vector3 pos)
    {
        if (ctx.Config?.stat == null || ctx.Runtime.PlayerTarget == null) return;
        Vector3 playerPos = ctx.Runtime.PlayerTarget.position;
        float dx = playerPos.x - pos.x;
        float dz = playerPos.z - pos.z;
        if (Mathf.Sqrt(dx * dx + dz * dz) > Data.fragmentWarningRadius) return;

        var player = ctx.Runtime.PlayerTarget.GetComponent<PlayerController>();
        if (player == null) return;

        int dmg = Mathf.Max(1, (int)(ctx.Config.stat.attackPower * Data.fragmentDamageMultiplier));
        player.TakeDamage(dmg);

        Vector3 dir = playerPos - pos;
        dir.y = 0.3f;
        if (dir.sqrMagnitude > 0.001f) dir.Normalize();
        player.ApplyKnockback(dir * (ctx.Config.stat.knockbackForce * Data.knockbackMultiplier));
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
        ctx.Animator.speed = SpeedMult(ctx);
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

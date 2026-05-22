using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// ForestGuardian 브레스 (Breath) 패턴.
///
/// ━━ 조건 ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
///  2페이즈 + 플레이어가 minRange 이상 거리
///
/// ━━ 흐름 ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
///  Warning     : AttackReady + 직사각형 경고장판 서서히 표시 + 플레이어 추적 회전
///  BreathStart : 경고장판 제거 + MagicAttackS (시작 모션)
///  Breathing   : MagicAttackL 루프 + EarthBeam(왼손 기준) + 60°/s 플레이어 추적 + 지속 데미지
///  BreathEnd   : MagicAttackE (종료 모션) + EarthBeam 서서히 소멸
///
/// ━━ 이동 ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
///  FullLock — 보스 위치 고정, Y축 회전만 허용
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/Boss/ForestGuardian/FG_BreathPattern", fileName = "FG_BreathPattern")]
public class FGBreathPatternSO : BossPatternSO
{
    // ── 조건 ──────────────────────────────────────────────
    [Header("Breath — Condition")]
    [Tooltip("패턴 발동 최소 거리 (m). 이 거리 이상일 때 발동.")]
    public float minRange = 6f;

    // ── 타이밍 ────────────────────────────────────────────
    [Header("Breath — Timing")]
    [Tooltip("경고 장판 표시 시간 (초)")]
    public float warningDuration = 1.0f;

    [Tooltip("MagicAttackS(시작 애니) 재생 시간 (초)")]
    public float breathStartDuration = 0.8f;

    [Tooltip("브레스 지속 시간 (초) — MagicAttackL 루프 구간")]
    public float breathDuration = 3.5f;

    [Tooltip("MagicAttackE(종료 애니) 재생 시간 (초)")]
    public float breathEndDuration = 0.6f;

    [Tooltip("지속 데미지 체크 주기 (초)")]
    public float damageTick = 0.3f;

    // ── 범위 ──────────────────────────────────────────────
    [Header("Breath — Range")]
    [Tooltip("브레스 사정거리 (m)")]
    public float range = 10f;

    [Tooltip("브레스 폭 (m)")]
    public float width = 1f;

    // ── 이동 ──────────────────────────────────────────────
    [Header("Breath — Rotation")]
    [Tooltip("플레이어 추적 회전 속도 (°/s)")]
    public float rotationSpeed = 60f;

    // ── 데미지 ────────────────────────────────────────────
    [Header("Breath — Damage")]
    [Tooltip("기본 attackPower 배율 (틱당)")]
    public float damageMultiplier = 0.5f;

    [Tooltip("넉백 힘 배율")]
    public float knockbackMultiplier = 0.3f;

    // ── 비주얼 ────────────────────────────────────────────
    [Header("Breath — Visual")]
    [Tooltip("직사각형 경고 장판 프리팹 (RectWarning 컴포넌트 포함). null이면 표시 안 함.")]
    public GameObject warningPrefab;

    [Tooltip("브레스 이펙트 프리팹 (EarthBeam 등). 보스 왼손 위치에 배치. null이면 재생 안 함.")]
    public GameObject breathVfxPrefab;

    // ── 런타임 ────────────────────────────────────────────
    private FGBreathState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new FGBreathState(this);
    public override void OnRecycled()                       => _state = new FGBreathState(this);

    public override bool CanExecute(BossPatternContext ctx)
    {
        if (ctx.Ctx.Runtime.PlayerTarget == null) return false;

        float dist = Vector3.Distance(
            ctx.Ctx.Transform.position,
            ctx.Ctx.Runtime.PlayerTarget.position);
        return dist >= minRange;
    }

    public override SpecialStateBase GetRuntimeState() => _state;
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// FGBreathState — FullLock (위치 고정, 회전만 허용)
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class FGBreathState : FullLockState<FGBreathPatternSO>
{
    private const string AnimAttackReady = "AttackReady";
    private const string AnimBreathStart = "MagicAttackS";
    private const string AnimBreathLoop  = "MagicAttackL";
    private const string AnimBreathEnd   = "MagicAttackE";

    private enum Phase { Warning, BreathStart, Breathing, BreathEnd }

    private Phase       _phase;
    private float       _timer;
    private float       _damageTimer;
    private GameObject  _warningGO;
    private RectWarning _rectWarning;
    private GameObject  _breathVfxGO;

    public FGBreathState(FGBreathPatternSO data) : base(data) { }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 생명주기
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    public override void Enter(MonsterContext ctx)
    {
        _phase       = Phase.Warning;
        _timer       = 0f;
        _damageTimer = 0f;

        if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
        {
            ctx.Agent.isStopped = true;
            ctx.Agent.ResetPath();
        }

        // 초기 방향 설정 후 경고장판 생성
        FacePlayerImmediate(ctx);
        SpawnWarning(ctx);
        PlayAnim(ctx, AnimAttackReady, 0.1f);
    }

    public override void Update(MonsterContext ctx)
    {
        float dt = Time.deltaTime * SpeedMult(ctx);
        _timer += dt;

        switch (_phase)
        {
            // ── Warning : 경고장판 서서히 표시, 보스가 플레이어 방향 추적 ──
            case Phase.Warning:
                RotateTowardPlayer(ctx);
                UpdateWarningTransform(ctx);

                if (_rectWarning != null && Data.warningDuration > 0f)
                    _rectWarning.SetFillProgress(_timer / Data.warningDuration);

                if (_timer >= Data.warningDuration)
                {
                    DespawnWarning();
                    _timer = 0f;
                    _phase = Phase.BreathStart;
                    PlayAnim(ctx, AnimBreathStart, 0.1f);
                }
                break;

            // ── BreathStart : MagicAttackS 재생 ──────────────────────────
            case Phase.BreathStart:
                if (_timer >= Data.breathStartDuration)
                {
                    _timer       = 0f;
                    _damageTimer = 0f;
                    _phase       = Phase.Breathing;
                    SpawnBreathVfx(ctx);
                    PlayAnim(ctx, AnimBreathLoop, 0.05f);
                }
                break;

            // ── Breathing : MagicAttackL 루프 + 플레이어 추적 + 지속 데미지 ─
            case Phase.Breathing:
                // 클립 루프 (normalizedTime >= 1 에서 재시작)
                if (ctx.Animator != null)
                {
                    var si = ctx.Animator.GetCurrentAnimatorStateInfo(0);
                    if (si.IsName(AnimBreathLoop) && si.normalizedTime >= 1f)
                        ctx.Animator.Play(AnimBreathLoop, 0, 0f);
                }

                RotateTowardPlayer(ctx);
                UpdateBreathVfxTransform(ctx);

                _damageTimer += dt;
                if (_damageTimer >= Data.damageTick)
                {
                    _damageTimer = 0f;
                    TryDealDamage(ctx);
                }

                if (_timer >= Data.breathDuration)
                {
                    _timer = 0f;
                    _phase = Phase.BreathEnd;
                    StopBreathVfx();
                    PlayAnim(ctx, AnimBreathEnd, 0.1f);
                }
                break;

            // ── BreathEnd : MagicAttackE 재생 후 복귀 ─────────────────────
            case Phase.BreathEnd:
                if (_timer >= Data.breathEndDuration)
                    ctx.Monster.ChangeState<ChaseState>();
                break;
        }
    }

    public override void Exit(MonsterContext ctx)
    {
        DespawnWarning();
        StopBreathVfx();

        if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
        {
            ctx.Agent.isStopped = false;
            ctx.Agent.Warp(ctx.Transform.position);
        }
    }

    private static float SpeedMult(MonsterContext ctx)
    {
        var fg = ctx.Monster as ForestGuardianMonster;
        return fg?.FGBlackboard.AnimSpeedMult ?? 1f;
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 회전
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private static void FacePlayerImmediate(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null) return;
        Vector3 dir = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.001f)
            ctx.Transform.rotation = Quaternion.LookRotation(dir);
    }

    private void RotateTowardPlayer(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null) return;
        Vector3 dir = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f) return;

        Quaternion target = Quaternion.LookRotation(dir);
        ctx.Transform.rotation = Quaternion.RotateTowards(
            ctx.Transform.rotation, target, Data.rotationSpeed * Time.deltaTime);
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 데미지
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private void TryDealDamage(MonsterContext ctx)
    {
        if (ctx.Config?.stat == null || ctx.Runtime.PlayerTarget == null) return;

        Vector3 origin    = ctx.Transform.position;
        Vector3 toPlayer  = ctx.Runtime.PlayerTarget.position - origin;
        float   along     = Vector3.Dot(toPlayer, ctx.Transform.forward);
        float   perpDist  = (toPlayer - ctx.Transform.forward * along).magnitude;

        if (along < 0f || along > Data.range) return;
        if (perpDist > Data.width * 0.5f) return;

        var player = ctx.Runtime.PlayerTarget.GetComponent<PlayerController>();
        if (player == null) return;

        int dmg = Mathf.Max(1, (int)(ctx.Config.stat.attackPower * Data.damageMultiplier));
        player.TakeDamage(dmg);

        if (Data.knockbackMultiplier > 0f)
        {
            Vector3 knock = new Vector3(
                ctx.Transform.forward.x, 0.1f, ctx.Transform.forward.z).normalized;
            player.ApplyKnockback(knock * ctx.Config.stat.knockbackForce * Data.knockbackMultiplier);
        }
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 경고 장판
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private void SpawnWarning(MonsterContext ctx)
    {
        if (Data.warningPrefab == null) return;

        Vector3 pos = ctx.Transform.position;
        pos.y += 0.02f;

        _warningGO = Object.Instantiate(Data.warningPrefab, pos, ctx.Transform.rotation);
        // 피벗이 근거리 끝 → scale Z = 사정거리, X = 폭
        _warningGO.transform.localScale = new Vector3(Data.width, 1f, Data.range);

        _rectWarning = _warningGO.GetComponent<RectWarning>();
        _rectWarning?.SetFillProgress(0f);
    }

    /// <summary>Warning 중 보스 회전에 따라 경고 장판 위치·회전 갱신.</summary>
    private void UpdateWarningTransform(MonsterContext ctx)
    {
        if (_warningGO == null) return;
        Vector3 pos = ctx.Transform.position;
        pos.y += 0.02f;
        _warningGO.transform.SetPositionAndRotation(pos, ctx.Transform.rotation);
    }

    private void DespawnWarning()
    {
        if (_warningGO == null) return;
        Object.Destroy(_warningGO);
        _warningGO   = null;
        _rectWarning = null;
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 브레스 VFX
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private void SpawnBreathVfx(MonsterContext ctx)
    {
        if (Data.breathVfxPrefab == null) return;

        _breathVfxGO = Object.Instantiate(Data.breathVfxPrefab);
        UpdateBreathVfxTransform(ctx);

        // 루트에 ParticleSystem이 없는 BeamVfx 구조도 지원: 자식까지 포함해 Play
        foreach (var ps in _breathVfxGO.GetComponentsInChildren<ParticleSystem>(true))
            ps.Play(withChildren: false);
    }

    /// <summary>보스 왼손 위치에 VFX를 배치하고 보스 정면 방향으로 회전.</summary>
    private void UpdateBreathVfxTransform(MonsterContext ctx)
    {
        if (_breathVfxGO == null || ctx.Animator == null) return;

        // Humanoid 리그일 때만 GetBoneTransform 사용, Generic 리그는 이름으로 검색
        Transform handBone = ctx.Animator.isHuman
            ? ctx.Animator.GetBoneTransform(HumanBodyBones.LeftHand)
            : FindBoneByName(ctx.Transform, "TreantLPalm", "LeftHand", "Hand_L", "L_Hand", "hand_l", "hand.L");

        Vector3 origin = handBone != null
            ? handBone.position
            : ctx.Transform.position + ctx.Transform.right * -0.5f + Vector3.up * 1.2f;

        _breathVfxGO.transform.SetPositionAndRotation(
            origin,
            Quaternion.LookRotation(ctx.Transform.forward));

        // BeamBody Z 스케일을 사정거리에 맞춰 설정 (BeamVfx 코루틴 없이 빔 길이 직접 제어)
        var beamBody = _breathVfxGO.transform.Find("BeamBody");
        if (beamBody != null)
        {
            Vector3 s = beamBody.localScale;
            s.z = Data.range;
            beamBody.localScale = s;
        }
    }

    private void StopBreathVfx()
    {
        if (_breathVfxGO == null) return;

        float maxLifetime = 0f;
        foreach (var ps in _breathVfxGO.GetComponentsInChildren<ParticleSystem>(true))
        {
            ps.Stop(withChildren: false, stopBehavior: ParticleSystemStopBehavior.StopEmitting);
            maxLifetime = Mathf.Max(maxLifetime, ps.main.startLifetime.constantMax);
        }

        Object.Destroy(_breathVfxGO, maxLifetime > 0f ? maxLifetime + 0.5f : 0f);
        _breathVfxGO = null;
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 애니메이션
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private static void PlayAnim(MonsterContext ctx, string stateName, float crossFade)
    {
        if (ctx.Animator == null) return;
        if (!ctx.Animator.HasState(0, Animator.StringToHash(stateName)))
        {
            Debug.LogWarning($"[FGBreath] Animator state not found: '{stateName}'", ctx.Monster);
            return;
        }
        ctx.Animator.CrossFade(stateName, crossFade, 0, 0f);
    }

    /// <summary>Generic 리그에서 후보 이름 중 일치하는 첫 번째 본을 반환한다.</summary>
    private static Transform FindBoneByName(Transform root, params string[] names)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            foreach (var n in names)
            {
                if (t.name.Equals(n, System.StringComparison.OrdinalIgnoreCase))
                    return t;
            }
        }
        return null;
    }
}
}

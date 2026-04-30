using UnityEngine;

namespace Abyss.Monster
{
/// <summary>
/// ForestGuardian 근접 펀치 패턴.
///
/// 조건  : 플레이어가 range 이내 근접 시 (CanExecute에서 체크)
/// 범위  : 전방 180도 부채꼴 (arcHalfAngle = 90도)
/// 이동  : FullLock — 보스 고정, 중단 불가
/// 흐름  : 경고 장판 표시(warningDuration) → 타격 → 복귀(recoveryDuration) → ChaseState
/// 손 선택: 플레이어가 보스 기준 왼쪽이면 PunchSwingLeft, 오른쪽이면 PunchSwingRight
/// </summary>
[CreateAssetMenu(menuName = "Abyss/Boss/ForestGuardian/FG_PunchPattern", fileName = "FG_PunchPattern")]
public class FGPunchPatternSO : BossPatternSO
{
    // ── 범위 ──────────────────────────────────────────────
    [Header("Range")]
    [Tooltip("최대 공격 사정거리 (m)")]
    public float range = 4f;

    [Tooltip("공격 판정 부채꼴 반각 (90 = 전방 180도 부채꼴)")]
    public float arcHalfAngle = 90f;

    // ── 타이밍 ────────────────────────────────────────────
    [Header("Timing")]
    [Tooltip("경고 장판 표시 시간 — 이 시간 후 타격 판정")]
    public float warningDuration = 0.7f;

    [Tooltip("타격 후 자세 복귀 시간")]
    public float recoveryDuration = 0.5f;

    // ── 데미지 ────────────────────────────────────────────
    [Header("Damage")]
    [Tooltip("기본 attackPower에 곱할 배율")]
    public float damageMultiplier = 1.2f;

    [Tooltip("넉백 힘 배율")]
    public float knockbackMultiplier = 1.5f;

    // ── 경고 장판 ─────────────────────────────────────────
    [Header("Warning Zone")]
    [Tooltip("경고 장판 전용 프리팹. null이면 effectPrefab 사용.")]
    public GameObject warningZonePrefab;

    // ── 런타임 ───────────────────────────────────────────
    private FGPunchState _state;

    public override void Initialize(BossPatternContext ctx)
    {
        _state = new FGPunchState(this);
    }

    public override void OnRecycled()
    {
        _state = new FGPunchState(this);
    }

    public override bool CanExecute(BossPatternContext ctx)
    {
        if (ctx.Ctx.Runtime.PlayerTarget == null) return false;
        float dist = Vector3.Distance(
            ctx.Ctx.Transform.position,
            ctx.Ctx.Runtime.PlayerTarget.position);
        return dist <= range;
    }

    public override SpecialStateBase GetRuntimeState() => _state;

    internal GameObject ResolveWarningPrefab()
        => warningZonePrefab != null ? warningZonePrefab : effectPrefab;
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// FGPunchState — FullLock (이동 + 중단 불가)
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class FGPunchState : FullLockState<FGPunchPatternSO>
{
    private const string AnimAttackReady = "AttackReady";
    private const string AnimLeft        = "PunchSwingLeft";
    private const string AnimRight       = "PunchSwingRight";

    private float      _timer;
    private bool       _hasDealt;
    private bool       _isLeftHand;
    private bool       _attackAnimPlayed;
    private GameObject _warningGO;
    private Vector3    _warningTargetScale;

    public FGPunchState(FGPunchPatternSO data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        _timer            = 0f;
        _hasDealt         = false;
        _attackAnimPlayed = false;

        if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
        {
            ctx.Agent.isStopped = true;
            ctx.Agent.ResetPath();
        }

        FacePlayer(ctx);
        _isLeftHand = IsPlayerOnLeft(ctx);
        SpawnWarning(ctx);
        PlayAnim(ctx, AnimAttackReady);  // 경고 장판 채우기 동안 준비 자세
    }

    public override void Update(MonsterContext ctx)
    {
        _timer += Time.deltaTime * SpeedMult(ctx);

        // 경고 장판 서서히 커지기 (보스 기준 부채꼴 → 보스에서부터 서서히 확장)
        if (_warningGO != null && Data.warningDuration > 0f)
        {
            float t = Mathf.Clamp01(_timer / Data.warningDuration);
            _warningGO.transform.localScale = Vector3.Lerp(Vector3.zero, _warningTargetScale, t);
        }

        if (!_attackAnimPlayed && _timer >= Data.warningDuration)
        {
            _attackAnimPlayed = true;
            DespawnWarning();
            PlayAnim(ctx, _isLeftHand ? AnimLeft : AnimRight);  // 경고 종료 → 타격 애니메이션
        }

        if (!_hasDealt && _attackAnimPlayed)
        {
            _hasDealt = true;
            DealFanDamage(ctx);
        }

        if (_timer >= Data.warningDuration + Data.recoveryDuration)
            ctx.Monster.ChangeState<ChaseState>();
    }

    public override void Exit(MonsterContext ctx)
    {
        DespawnWarning();

        if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
            ctx.Agent.isStopped = false;
    }

    // ── 좌/우 손 결정 ─────────────────────────────────────
    private static bool IsPlayerOnLeft(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null) return false;
        Vector3 toPlayer = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        toPlayer.y = 0f;
        return Vector3.Dot(ctx.Transform.right, toPlayer.normalized) < 0f;
    }

    // ── 플레이어 방향으로 즉시 회전 ───────────────────────
    private static void FacePlayer(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null) return;
        Vector3 dir = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.001f)
            ctx.Transform.rotation = Quaternion.LookRotation(dir);
    }

    // ── 경고 장판 ─────────────────────────────────────────
    private void SpawnWarning(MonsterContext ctx)
    {
        var prefab = Data.ResolveWarningPrefab();
        if (prefab == null) return;

        Vector3 spawnPos = ctx.Transform.position;
        spawnPos.y += 0.02f;
        _warningTargetScale = new Vector3(Data.range, 1f, Data.range);
        _warningGO = Object.Instantiate(prefab, spawnPos, ctx.Transform.rotation);
        _warningGO.transform.localScale = Vector3.zero;  // 처음엔 0 → Update에서 서서히 확장
    }

    private void DespawnWarning()
    {
        if (_warningGO == null) return;
        Object.Destroy(_warningGO);
        _warningGO = null;
    }

    // ── 부채꼴 데미지 판정 ─────────────────────────────────
    private void DealFanDamage(MonsterContext ctx)
    {
        if (ctx.Config?.stat == null || ctx.Runtime.PlayerTarget == null) return;

        Vector3 toPlayer = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        toPlayer.y = 0f;

        if (toPlayer.sqrMagnitude > Data.range * Data.range) return;
        if (toPlayer.sqrMagnitude > 0.001f &&
            Vector3.Angle(ctx.Transform.forward, toPlayer) > Data.arcHalfAngle) return;

        var player = ctx.Runtime.PlayerTarget.GetComponent<PlayerController>();
        if (player == null) return;

        int dmg = Mathf.Max(1, (int)(ctx.Config.stat.attackPower * Data.damageMultiplier));
        player.TakeDamage(dmg);

        Vector3 knockDir = toPlayer.sqrMagnitude > 0.001f
            ? toPlayer.normalized
            : ctx.Transform.forward;
        knockDir.y = 0.3f;
        knockDir.Normalize();
        player.ApplyKnockback(knockDir * ctx.Config.stat.knockbackForce * Data.knockbackMultiplier);
    }

    // ── 애니메이션 재생 ─────────────────────────────────────
    private static void PlayAnim(MonsterContext ctx, string stateName)
    {
        if (ctx.Animator == null) return;
        if (!ctx.Animator.HasState(0, Animator.StringToHash(stateName)))
        {
            Debug.LogWarning($"[FGPunch] Animator state not found: '{stateName}'", ctx.Monster);
            return;
        }
        ctx.Animator.CrossFade(stateName, 0.1f, 0, 0f);
    }

    private static float SpeedMult(MonsterContext ctx)
    {
        var fg = ctx.Monster as ForestGuardianMonster;
        return fg?.FGBlackboard.AnimSpeedMult ?? 1f;
    }
}
}

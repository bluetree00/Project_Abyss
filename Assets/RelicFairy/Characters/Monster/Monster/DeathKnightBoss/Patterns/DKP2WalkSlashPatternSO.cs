using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// DeathKnight Phase2 걷기 콤보 베기 — Attack4 (2.667s).
///
/// 보스가 플레이어를 향해 3걸음 걸어오면서 걸음마다 대각선으로 1회씩 베기.
///   Step1 (hitTime1): 오른쪽 대각선 (step1AngleOffset=+45°)
///   Step2 (hitTime2): 왼쪽 대각선 (step2AngleOffset=-45°)
///   Step3 (hitTime3): 정면 (step3AngleOffset=0°)
///
/// ━━ BossDesign §2 5단계 ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
///  ① Opening Pose: 0~hitTime1 (걸어오는 예비 동작)
///  ③ Attack      : 각 hitTime (대각선 베기 히트 판정)
///  ④ End Pose    : hitTime3~+recoveryTime (마지막 베기 후 반격 창)
/// </summary>
[CreateAssetMenu(menuName = "Abyss/Boss/DeathKnight/Phase2/DK_P2_WalkSlashPattern",
                 fileName = "DK_P2_WalkSlashPattern")]
public class DKP2WalkSlashPatternSO : BossPatternSO
{
    [Header("타이밍")]
    [Tooltip("1번째 베기 시점 (s). Opening Pose 종료")]
    public float hitTime1    = 0.7f;
    [Tooltip("2번째 베기 시점 (s)")]
    public float hitTime2    = 1.5f;
    [Tooltip("3번째 베기 시점 (s)")]
    public float hitTime3    = 2.2f;
    [Tooltip("마지막 베기 후 End Pose + Return")]
    public float recoveryTime = 0.35f;

    [Header("이동")]
    [Tooltip("3걸음 동안 전진 거리 (m)")]
    public float totalMoveDistance = 3f;

    [Header("히트박스")]
    [Tooltip("각 베기 범위 (m)")]
    public float hitRange = 2.5f;
    [Tooltip("1번 베기 방향 각도 오프셋 (+ = 오른쪽, - = 왼쪽)")]
    public float step1AngleOffset =  45f;
    public float step2AngleOffset = -45f;
    public float step3AngleOffset =   0f;
    [Tooltip("각 베기의 허용 반각 (도)")]
    public float hitHalfAngle = 60f;

    [Header("피드백 VFX")]
    public GameObject swingVfxPrefab;

    [Header("데미지 (3회 각각 적용)")]
    public float damageMultiplier    = 0.8f;
    public float knockbackMultiplier = 0.8f;

    private DKP2WalkSlashState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new DKP2WalkSlashState(this);
    public override void OnRecycled()                       => _state = new DKP2WalkSlashState(this);

    public override bool CanExecute(BossPatternContext ctx)
        => ctx.Ctx.Runtime.PlayerTarget != null;

    public override SpecialStateBase GetRuntimeState() => _state;
}

// FullLockState 대신 UnInterruptibleState 사용 — MovementLocked 제약이 NavMesh 에이전트를
// 매 프레임 강제 정지시켜 보스가 걷지 못하는 버그를 방지한다.
// Attack 2/3과 동일하게 Update에서 매 프레임 agent.Warp()로 직접 위치 제어.
// SetDestination은 텔레포트 직후 NavMesh 상태에서 경로 계산이 실패할 수 있어 사용하지 않는다.
public class DKP2WalkSlashState : UnInterruptibleState<DKP2WalkSlashPatternSO>
{
    private const string AnimName = "Attack4";

    private float   _timer;
    private bool    _hit1, _hit2, _hit3;
    private Vector3 _startPos;
    private Vector3 _endPos;

    public DKP2WalkSlashState(DKP2WalkSlashPatternSO data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        _timer = 0f;
        _hit1 = _hit2 = _hit3 = false;

        StopAgent(ctx);
        FacePlayer(ctx);
        PlayAnim(ctx, AnimName);

        // 이동 구간 캡처 (Attack 2/3의 _dashStart/_dashDir 방식과 동일)
        _startPos = ctx.Transform.position;
        Vector3 moveDir = ctx.Runtime.PlayerTarget != null
            ? (ctx.Runtime.PlayerTarget.position - ctx.Transform.position)
            : ctx.Transform.forward;
        moveDir.y = 0f;
        if (moveDir.sqrMagnitude > 0.001f) moveDir.Normalize();
        else moveDir = ctx.Transform.forward;

        _endPos = _startPos + moveDir * Data.totalMoveDistance;
        _endPos.y = _startPos.y;
    }

    public override void Update(MonsterContext ctx)
    {
        _timer += Time.deltaTime * AnimSpeed(ctx);

        // 매 프레임 Warp로 직접 전진 (hitTime3까지 선형 이동)
        if (_timer < Data.hitTime3)
        {
            float t      = _timer / Data.hitTime3;
            Vector3 pos  = Vector3.Lerp(_startPos, _endPos, t);
            if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
                ctx.Agent.Warp(pos);
            else
                ctx.Transform.position = pos;
        }

        if (!_hit1 && _timer >= Data.hitTime1)
        {
            _hit1 = true;
            TriggerSlash(ctx, Data.step1AngleOffset);
        }
        if (!_hit2 && _timer >= Data.hitTime2)
        {
            _hit2 = true;
            TriggerSlash(ctx, Data.step2AngleOffset);
        }
        if (!_hit3 && _timer >= Data.hitTime3)
        {
            _hit3 = true;
            TriggerSlash(ctx, Data.step3AngleOffset);
        }

        if (_timer >= Data.hitTime3 + Data.recoveryTime)
            ctx.Monster.ChangeState<AttackReadyState>();
    }

    public override void Exit(MonsterContext ctx) => RestoreAgent(ctx);

    // ── 베기 발동 ─────────────────────────────────────────────

    private void TriggerSlash(MonsterContext ctx, float angleOffset)
    {
        SpawnSwingVfx(ctx);
        if (CheckHitZone(ctx, angleOffset))
            ApplyDamage(ctx);
        else
            BossImpactFeedback.TriggerCameraShake(0.07f, 0.18f);
    }

    private bool CheckHitZone(MonsterContext ctx, float angleOffset)
    {
        if (ctx.Runtime.PlayerTarget == null) return false;
        Vector3 toPlayer = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude > Data.hitRange * Data.hitRange) return false;

        Vector3 slashDir = Quaternion.AngleAxis(angleOffset, Vector3.up) * ctx.Transform.forward;
        return Vector3.Angle(slashDir, toPlayer) <= Data.hitHalfAngle;
    }

    private void ApplyDamage(MonsterContext ctx)
    {
        var player = ctx.Runtime.PlayerTarget.GetComponent<PlayerController>();
        if (player == null) return;

        int dmg = Mathf.Max(1, (int)(ctx.Config.stat.attackPower * Data.damageMultiplier));
        player.TakeDamage(dmg);

        Vector3 kb = (ctx.Runtime.PlayerTarget.position - ctx.Transform.position).normalized;
        kb.y = 0.2f;
        player.ApplyKnockback(kb * ctx.Config.stat.knockbackForce * Data.knockbackMultiplier);

        BossImpactFeedback.TriggerHitStop(0.08f);
        BossImpactFeedback.TriggerCameraShake(0.12f, 0.25f);
    }

    // ── 헬퍼 ──────────────────────────────────────────────────

    private void SpawnSwingVfx(MonsterContext ctx)
    {
        if (Data.swingVfxPrefab == null) return;
        Transform swordTf = (ctx.Monster as DeathKnightBossMonster)?.SwordTransform;
        Vector3    pos = swordTf != null ? swordTf.position : ctx.Transform.position;
        Quaternion rot = swordTf != null ? swordTf.rotation : ctx.Transform.rotation;
        BossEffectPool.SpawnOneShot(Data.swingVfxPrefab, pos, rot, fallbackLifetime: 1.5f);
    }

    private static float AnimSpeed(MonsterContext ctx)
        => (ctx.Monster as DeathKnightBossMonster)?.DKBlackboard.AnimSpeedMult ?? 1f;

    private static void PlayAnim(MonsterContext ctx, string stateName)
    {
        if (ctx.Animator == null) return;
        if (!ctx.Animator.HasState(0, Animator.StringToHash(stateName))) return;
        ctx.Animator.speed = AnimSpeed(ctx);
        ctx.Animator.CrossFade(stateName, 0.05f, 0, 0f);
    }

    private static void StopAgent(MonsterContext ctx)
    {
        if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
        {
            ctx.Agent.isStopped = true;
            ctx.Agent.velocity  = Vector3.zero;
            ctx.Agent.ResetPath();
        }
    }

    private static void RestoreAgent(MonsterContext ctx)
    {
        if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
            ctx.Agent.isStopped = false;
    }

    private static void FacePlayer(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null) return;
        Vector3 dir = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.001f)
            ctx.Transform.rotation = Quaternion.LookRotation(dir);
    }
}
}

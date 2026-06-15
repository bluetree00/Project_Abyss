using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// DeathKnight Phase2 기본 근접 공격 — Attack1 / Attack2 / Attack3 공용 SO.
///
/// Attack1 (animName="Attack1"): 제자리 크게 가로 1회 베기 (2.458s)
///   windupDuration=0.6s, hitTime=1.0s, endPoseTime=0.6s, hitHalfAngle=75°, doesDash=false
///
/// Attack2 (animName="Attack2"): 앞으로 전진하며 빠르게 1회 베기 (1.208s)
///   windupDuration=0.35s, hitTime=0.55s, endPoseTime=0.35s, hitHalfAngle=45°,
///   doesDash=true, dashDistance=1.5, dashDuration=0.25s
///
/// Attack3 (animName="Attack3"): 앞으로 전진하며 빠르게 1회 찌르기 (1.208s)
///   windupDuration=0.35s, hitTime=0.55s, endPoseTime=0.35s, hitHalfAngle=25°,
///   doesDash=true, dashDistance=1.5, dashDuration=0.25s
///
/// ━━ BossDesign §2 5단계 준수 ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
///  ① Opening Pose : 0~windupDuration
///  ② Attack Signal: windupDuration 직후 ~ hitTime
///  ③ Attack       : hitTime (hit 판정)
///  ④ End Pose     : hitTime ~ hitTime+endPoseTime (반격 창)
///  ⑤ Return       : +recoveryTime → AttackReadyState
/// </summary>
[CreateAssetMenu(menuName = "Abyss/Boss/DeathKnight/Phase2/DK_P2_SlashPattern",
                 fileName = "DK_P2_SlashPattern")]
public class DKP2SlashPatternSO : BossPatternSO
{
    [Header("애니메이션")]
    public string animName = "Attack1";

    [Header("타이밍 — §2 5단계")]
    [Tooltip("Opening Pose 종료 시점 (예비동작). 약공격 ≥0.3s")]
    public float windupDuration = 0.6f;
    [Tooltip("실제 피격 판정 시점")]
    public float hitTime        = 1.0f;
    [Tooltip("End Pose (반격 창). 최소 0.3s")]
    public float endPoseTime    = 0.6f;
    [Tooltip("Return → AttackReady 대기")]
    public float recoveryTime   = 0.25f;

    [Header("히트박스")]
    [Tooltip("근접 공격 범위 (m)")]
    public float hitRange      = 2.8f;
    [Tooltip("공격 방향 허용 반각 (도). Attack1=75, Attack2=45, Attack3=25")]
    public float hitHalfAngle  = 75f;

    [Header("대시 (Attack2/3)")]
    [Tooltip("공격 시 전진 여부")]
    public bool  doesDash      = false;
    [Tooltip("전진 거리 (m)")]
    public float dashDistance  = 1.5f;
    [Tooltip("전진 소요 시간 (s)")]
    public float dashDuration  = 0.25f;

    [Header("피드백 VFX")]
    public GameObject swingVfxPrefab;

    [Header("데미지")]
    public float damageMultiplier    = 1f;
    public float knockbackMultiplier = 1f;

    private DKP2SlashState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new DKP2SlashState(this);
    public override void OnRecycled()                       => _state = new DKP2SlashState(this);

    public override bool CanExecute(BossPatternContext ctx)
        => ctx.Ctx.Runtime.PlayerTarget != null;

    public override SpecialStateBase GetRuntimeState() => _state;
}

public class DKP2SlashState : FullLockState<DKP2SlashPatternSO>
{
    private float   _timer;
    private bool    _hitApplied;
    private Vector3 _dashStart;
    private Vector3 _dashDir;

    public DKP2SlashState(DKP2SlashPatternSO data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        _timer      = 0f;
        _hitApplied = false;

        StopAgent(ctx);
        FacePlayer(ctx);
        PlayAnim(ctx, Data.animName);

        if (Data.doesDash)
        {
            _dashStart = ctx.Transform.position;
            _dashDir   = ctx.Transform.forward;
        }
    }

    public override void Update(MonsterContext ctx)
    {
        _timer += Time.deltaTime * AnimSpeed(ctx);

        // ③ 대시 이동 (windupDuration → windupDuration+dashDuration)
        if (Data.doesDash &&
            _timer >= Data.windupDuration &&
            _timer <  Data.windupDuration + Data.dashDuration)
        {
            float t      = (_timer - Data.windupDuration) / Data.dashDuration;
            Vector3 dest = _dashStart + _dashDir * Data.dashDistance;
            Vector3 pos  = Vector3.Lerp(_dashStart, dest, t);
            if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
                ctx.Agent.Warp(pos);
            else
                ctx.Transform.position = pos;
        }

        // ③ 피격 판정
        if (!_hitApplied && _timer >= Data.hitTime)
        {
            _hitApplied = true;
            SpawnSwingVfx(ctx);
            if (CheckHitZone(ctx))
                ApplyDamage(ctx);
            else
            {
                // 빗나가도 스윙 임팩트 느낌은 유지
                BossImpactFeedback.TriggerCameraShake(0.08f, 0.2f);
            }
        }

        // ⑤ Return → AttackReady
        if (_timer >= Data.hitTime + Data.endPoseTime + Data.recoveryTime)
            ctx.Monster.ChangeState<AttackReadyState>();
    }

    public override void Exit(MonsterContext ctx) => RestoreAgent(ctx);

    // ── 히트 판정 ─────────────────────────────────────────────

    private bool CheckHitZone(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null) return false;
        Vector3 toPlayer = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude > Data.hitRange * Data.hitRange) return false;
        return Vector3.Angle(ctx.Transform.forward, toPlayer) <= Data.hitHalfAngle;
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

        // §3 타격감 3요소
        BossImpactFeedback.TriggerHitStop(0.1f);
        BossImpactFeedback.TriggerCameraShake(0.15f, 0.3f);
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

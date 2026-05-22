using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// ForestGuardian 회전 킥 (SpinKick) 패턴.
///
/// 조건  : 플레이어가 range 이내 근접 시
/// 범위  : 360° 원형 (보스 중심)
/// 이동  : UnInterruptible — 보스가 플레이어 방향으로 이동하면서 공격, 중단 불가
/// 흐름  : 경고 장판 표시(warningDuration) → 스핀킥 이동 + 지속 데미지(spinDuration) → 복귀
/// 경고  : DiscMeshWarning 컴포넌트가 붙은 프리팹 사용 (Smash와 동일 방식)
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/Boss/ForestGuardian/FG_SpinKickPattern", fileName = "FG_SpinKickPattern")]
public class FGSpinKickPatternSO : BossPatternSO
{
    // ── 범위 ──────────────────────────────────────────────
    [Header("SpinKick — Range")]
    [Tooltip("패턴 발동 거리 / 데미지 판정 반경 (m)")]
    public float range = 4f;

    // ── 타이밍 ────────────────────────────────────────────
    [Header("SpinKick — Timing")]
    [Tooltip("경고 장판 표시 시간 (초)")]
    public float warningDuration = 0.5f;

    [Tooltip("스핀킥 지속 시간 (초)")]
    public float spinDuration = 3f;

    [Tooltip("스핀킥 종료 후 자세 복귀 시간 (초)")]
    public float recoveryDuration = 0.4f;

    [Tooltip("스핀킥 중 데미지 체크 주기 (초)")]
    public float damageTick = 0.5f;

    // ── 이동 ──────────────────────────────────────────────
    [Header("SpinKick — Movement")]
    [Tooltip("스핀킥 중 이동 속도 (m/s)")]
    public float moveSpeed = 3f;

    // ── 데미지 ────────────────────────────────────────────
    [Header("SpinKick — Damage")]
    [Tooltip("기본 attackPower에 곱할 배율")]
    public float damageMultiplier = 1.0f;

    [Tooltip("넉백 힘 배율")]
    public float knockbackMultiplier = 1.2f;

    // ── 비주얼 ────────────────────────────────────────────
    [Header("SpinKick — Visual")]
    [Tooltip("경고 장판 프리팹 (DiscMeshWarning 컴포넌트 포함). null이면 effectPrefab 사용.")]
    public GameObject warningZonePrefab;

    // ── 런타임 ────────────────────────────────────────────
    private FGSpinKickState _state;

    public override void Initialize(BossPatternContext ctx)
    {
        _state = new FGSpinKickState(this);
    }

    public override void OnRecycled()
    {
        _state = new FGSpinKickState(this);
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
// FGSpinKickState — UnInterruptible (중단 불가, 이동 허용)
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class FGSpinKickState : UnInterruptibleState<FGSpinKickPatternSO>
{
    private const string AnimAttackReady = "AttackReady";
    private const string AnimSpinKick    = "360SpinKick";

    private enum Phase { Warning, Spinning, Recovery }

    private Phase      _phase;
    private float      _timer;
    private float      _damageTickTimer;
    private GameObject _warningGO;
    private Vector3    _warningTargetScale;

    public FGSpinKickState(FGSpinKickPatternSO data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        _phase           = Phase.Warning;
        _timer           = 0f;
        _damageTickTimer = 0f;

        if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
        {
            ctx.Agent.isStopped = true;
            ctx.Agent.ResetPath();
        }

        FacePlayer(ctx);
        _warningGO = SpawnWarning(ctx);
        PlayAnim(ctx, AnimAttackReady);  // 경고 장판 채우기 동안 준비 자세
    }

    public override void Update(MonsterContext ctx)
    {
        float dt = Time.deltaTime * SpeedMult(ctx);
        _timer += dt;

        switch (_phase)
        {
            case Phase.Warning:
                // 경고 장판 서서히 커지기 (보스 기준 원형 → 보스에서부터 서서히 확장)
                if (_warningGO != null && Data.warningDuration > 0f)
                {
                    float t = Mathf.Clamp01(_timer / Data.warningDuration);
                    _warningGO.transform.localScale = Vector3.Lerp(Vector3.zero, _warningTargetScale, t);
                }
                if (_timer >= Data.warningDuration)
                {
                    _timer = 0f;
                    _damageTickTimer = 0f;
                    _phase = Phase.Spinning;
                    DespawnWarning();
                    PlayAnim(ctx, AnimSpinKick);  // 경고 종료 → 스핀킥 애니메이션

                    if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
                    {
                        ctx.Agent.isStopped = false;
                        ctx.Agent.speed = Data.moveSpeed;
                    }
                }
                break;

            case Phase.Spinning:
                // 클립(1.83s)이 spinDuration(3s)보다 짧으므로 종료 시 재시작
                if (ctx.Animator != null)
                {
                    var si = ctx.Animator.GetCurrentAnimatorStateInfo(0);
                    if (si.IsName(AnimSpinKick) && si.normalizedTime >= 1f)
                        ctx.Animator.Play(AnimSpinKick, 0, 0f);
                }

                MoveTowardPlayer(ctx);

                _damageTickTimer += dt;
                if (_damageTickTimer >= Data.damageTick)
                {
                    _damageTickTimer = 0f;
                    TryDealDamage(ctx);
                }

                if (_timer >= Data.spinDuration)
                {
                    _timer = 0f;
                    _phase = Phase.Recovery;

                    if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
                    {
                        ctx.Agent.isStopped = true;
                        ctx.Agent.ResetPath();
                    }
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
        DespawnWarning();

        if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
        {
            ctx.Agent.isStopped = false;
            ctx.Agent.speed = ctx.Config != null ? ctx.Config.stat.moveSpeed : ctx.Agent.speed;
        }
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

    // ── 플레이어를 향해 이동 ───────────────────────────────
    private void MoveTowardPlayer(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null) return;

        Vector3 toPlayer = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude < 0.001f) return;

        Vector3 moveDir = toPlayer.normalized;
        ctx.Transform.rotation = Quaternion.LookRotation(moveDir);

        Vector3 delta = moveDir * Data.moveSpeed * Time.deltaTime;
        if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
            ctx.Agent.Warp(ctx.Transform.position + delta);
        else
            ctx.Transform.position += delta;
    }

    // ── 360° 원형 데미지 판정 ─────────────────────────────
    private void TryDealDamage(MonsterContext ctx)
    {
        if (ctx.Config?.stat == null || ctx.Runtime.PlayerTarget == null) return;

        float dist = Vector3.Distance(ctx.Transform.position, ctx.Runtime.PlayerTarget.position);
        if (dist > Data.range) return;

        var player = ctx.Runtime.PlayerTarget.GetComponent<PlayerController>();
        if (player == null) return;

        int dmg = Mathf.Max(1, (int)(ctx.Config.stat.attackPower * Data.damageMultiplier));
        player.TakeDamage(dmg);

        Vector3 knockDir = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        knockDir.y = 0.3f;
        if (knockDir.sqrMagnitude > 0.001f) knockDir.Normalize();
        player.ApplyKnockback(knockDir * ctx.Config.stat.knockbackForce * Data.knockbackMultiplier);
    }

    // ── 경고 장판 (360° 원형 디스크) ──────────────────────
    private GameObject SpawnWarning(MonsterContext ctx)
    {
        var prefab = Data.ResolveWarningPrefab();
        if (prefab == null) return null;

        Vector3 pos = ctx.Transform.position;
        pos.y += 0.02f;
        float s = Data.range;
        _warningTargetScale = new Vector3(s, 1f, s);
        var go = Object.Instantiate(prefab, pos, Quaternion.identity);
        go.transform.localScale = Vector3.zero;  // 처음엔 0 → Update에서 서서히 확장
        return go;
    }

    private void DespawnWarning()
    {
        if (_warningGO == null) return;
        Object.Destroy(_warningGO);
        _warningGO = null;
    }

    // ── 애니메이션 재생 ─────────────────────────────────────
    private static void PlayAnim(MonsterContext ctx, string stateName)
    {
        if (ctx.Animator == null) return;
        if (!ctx.Animator.HasState(0, Animator.StringToHash(stateName)))
        {
            Debug.LogWarning($"[FGSpinKick] Animator state not found: '{stateName}'", ctx.Monster);
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

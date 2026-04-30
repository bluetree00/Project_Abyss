using UnityEngine;

namespace Abyss.Monster
{
/// <summary>
/// ForestGuardian 돌진 (Charge) 패턴.
///
/// 흐름  : AttackReady(경고장판 채우기) → Charge 1회 돌진(경고장판 길이) → Recovery
/// 이동  : FullLock — agent 비활성, Warp 이동
/// </summary>
[CreateAssetMenu(menuName = "Abyss/Boss/ForestGuardian/FG_ChargePattern", fileName = "FG_ChargePattern")]
public class FGChargePatternSO : BossPatternSO
{
    [Header("Charge — Condition")]
    [Tooltip("돌진 최소 발동 거리 (m)")]
    public float minDistance = 8f;
    [Tooltip("돌진 최대 발동 거리 (m)")]
    public float maxDistance = 18f;

    [Header("Charge — Warning")]
    [Tooltip("경고 장판 채우기 시간 (초)")]
    public float warningDuration = 1.2f;
    [Tooltip("공격 폭 (m)")]
    public float chargeWidth = 3f;
    [Tooltip("직사각형 경고 장판 프리팹 (RectWarning)")]
    public GameObject warningPrefab;

    [Header("Charge — Movement")]
    [Tooltip("돌진 속도 (m/s)")]
    public float chargeSpeed = 12f;

    [Header("Charge — Damage")]
    [Tooltip("기본 attackPower 배율")]
    public float damageMultiplier = 1.2f;
    [Tooltip("넉백 힘 배율")]
    public float knockbackMultiplier = 1.8f;

    [Header("Charge — Recovery")]
    [Tooltip("돌진 완료 후 ChaseState 전환까지 대기 시간 (초). Chase 애니로 크로스페이드 후 전환.")]
    public float recoverDuration = 0.15f;

    [Header("Charge — VFX")]
    [Tooltip("돌진 중 보스 몸에 부착할 바람 이펙트 프리팹. null이면 재생 안 함.")]
    public GameObject chargeWindVfxPrefab;
    [Tooltip("보스 피벗 기준 이펙트 로컬 오프셋 (몸 중심 조정용, Y값으로 높이 조절)")]
    public Vector3 vfxBodyOffset = Vector3.zero;
    [Tooltip("이펙트 로컬 스케일")]
    public Vector3 vfxScale = Vector3.one;

    private FGChargeState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new FGChargeState(this);
    public override void OnRecycled()                       => _state = new FGChargeState(this);

    public override bool CanExecute(BossPatternContext ctx)
    {
        if (ctx.Ctx.Runtime.PlayerTarget == null) return false;
        float dist = Vector3.Distance(
            ctx.Ctx.Transform.position,
            ctx.Ctx.Runtime.PlayerTarget.position);
        return dist >= minDistance && dist <= maxDistance;
    }

    public override SpecialStateBase GetRuntimeState() => _state;
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// FGChargeState — FullLock (이동 + 중단 불가)
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class FGChargeState : FullLockState<FGChargePatternSO>
{
    private const string AnimAttackReady = "AttackReady";
    private const string AnimCharge      = "Charge";

    private enum Phase { Warning, Charging, Recover }

    private Phase       _phase;
    private float       _timer;
    private bool        _hasDamaged;
    private Vector3     _chargeDir;
    private Vector3     _chargeStartPos;
    private Vector3     _chargeEndPos;
    private float       _targetDist;
    private GameObject  _warningGO;
    private RectWarning _rectWarning;
    private GameObject  _windVfxGO;

    public FGChargeState(FGChargePatternSO data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        _phase      = Phase.Warning;
        _timer      = 0f;
        _hasDamaged = false;

        if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
        {
            ctx.Agent.isStopped = true;
            ctx.Agent.ResetPath();
        }

        if (ctx.Runtime.PlayerTarget != null)
        {
            Vector3 toPlayer = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
            toPlayer.y = 0f;
            _chargeDir = toPlayer.sqrMagnitude > 0.001f ? toPlayer.normalized : ctx.Transform.forward;
        }
        else
        {
            _chargeDir = ctx.Transform.forward;
        }

        ctx.Transform.rotation = Quaternion.LookRotation(_chargeDir);
        _chargeStartPos = ctx.Transform.position;

        SpawnWarning(ctx);
        PlayAnim(ctx, AnimAttackReady, 0.1f);
    }

    public override void Update(MonsterContext ctx)
    {
        _timer += Time.deltaTime * SpeedMult(ctx);

        switch (_phase)
        {
            case Phase.Warning:
                _rectWarning?.SetFillProgress(_timer / Data.warningDuration);

                if (_timer >= Data.warningDuration)
                {
                    DespawnWarning();
                    _chargeStartPos = ctx.Transform.position;
                    _timer = 0f;
                    _phase = Phase.Charging;
                    SpawnWindVfx(ctx);
                    PlayAnim(ctx, AnimCharge, 0.05f);
                }
                break;

            case Phase.Charging:
                Vector3 delta = _chargeDir * Data.chargeSpeed * Time.deltaTime;
                if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
                    ctx.Agent.Warp(ctx.Transform.position + delta);
                else
                    ctx.Transform.position += delta;

                if (!_hasDamaged && ctx.Runtime.PlayerTarget != null)
                    TryDamage(ctx);

                float traveled = Vector3.Distance(_chargeStartPos, ctx.Transform.position);
                if (traveled >= _targetDist)
                {
                    _timer = 0f;
                    _phase = Phase.Recover;
                    DespawnWindVfx();

                    // 즉시 Agent 재개 + Chase 애니 크로스페이드 → 경직 없이 자연스럽게 연결
                    if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
                    {
                        ctx.Agent.Warp(ctx.Transform.position);
                        ctx.Agent.isStopped = false;
                    }
                    PlayAnim(ctx, ctx.Animation.chaseStateName, 0.2f);
                }
                break;

            case Phase.Recover:
                if (_timer >= Data.recoverDuration)
                    ctx.Monster.ChangeState<ChaseState>();
                break;
        }
    }

    public override void Exit(MonsterContext ctx)
    {
        DespawnWarning();
        DespawnWindVfx();
        if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
        {
            ctx.Agent.Warp(ctx.Transform.position);
            ctx.Agent.isStopped = false;
        }
    }

    // ── 위치 고정 ─────────────────────────────────────────────
    private void AnchorPosition(MonsterContext ctx)
    {
        if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
            ctx.Agent.Warp(_chargeEndPos);
        else
            ctx.Transform.position = _chargeEndPos;
    }

    // ── 데미지 판정 ───────────────────────────────────────────
    private void TryDamage(MonsterContext ctx)
    {
        Vector3 toPlayer = ctx.Runtime.PlayerTarget.position - _chargeStartPos;
        float along    = Vector3.Dot(toPlayer, _chargeDir);
        float perpDist = (toPlayer - _chargeDir * along).magnitude;

        if (along < 0f || along > _targetDist) return;
        if (perpDist > Data.chargeWidth * 0.5f) return;

        float traveled = Vector3.Distance(_chargeStartPos, ctx.Transform.position);
        if (traveled >= along)
        {
            _hasDamaged = true;
            DealDamage(ctx);
        }
    }

    private void DealDamage(MonsterContext ctx)
    {
        if (ctx.Config?.stat == null || ctx.Runtime.PlayerTarget == null) return;
        var player = ctx.Runtime.PlayerTarget.GetComponent<PlayerController>();
        if (player == null) return;

        int dmg = Mathf.Max(1, (int)(ctx.Config.stat.attackPower * Data.damageMultiplier));
        player.TakeDamage(dmg);

        Vector3 dir = new Vector3(_chargeDir.x, 0.2f, _chargeDir.z).normalized;
        player.ApplyKnockback(dir * ctx.Config.stat.knockbackForce * Data.knockbackMultiplier);
    }

    // ── 경고 장판 ─────────────────────────────────────────────
    private void SpawnWarning(MonsterContext ctx)
    {
        if (Data.warningPrefab == null) return;

        _targetDist = ctx.Runtime.PlayerTarget != null
            ? Vector3.Distance(ctx.Transform.position, ctx.Runtime.PlayerTarget.position)
            : Data.chargeSpeed * 1.5f;

        Vector3 pos = ctx.Transform.position;
        pos.y += 0.02f;

        _warningGO = Object.Instantiate(Data.warningPrefab, pos, Quaternion.LookRotation(_chargeDir));
        _warningGO.transform.localScale = new Vector3(Data.chargeWidth, 1f, _targetDist);

        _rectWarning = _warningGO.GetComponent<RectWarning>();
        _rectWarning?.SetFillProgress(0f);
    }

    private void DespawnWarning()
    {
        if (_warningGO == null) return;
        Object.Destroy(_warningGO);
        _warningGO   = null;
        _rectWarning = null;
    }

    // ── 바람 이펙트 (돌진 중 보스 몸 부착) ───────────────────────
    private void SpawnWindVfx(MonsterContext ctx)
    {
        if (Data.chargeWindVfxPrefab == null) return;

        _windVfxGO = Object.Instantiate(
            Data.chargeWindVfxPrefab,
            ctx.Transform.position,
            ctx.Transform.rotation,
            ctx.Transform);

        _windVfxGO.transform.localPosition = Data.vfxBodyOffset;
        _windVfxGO.transform.localScale    = Data.vfxScale;

        if (_windVfxGO.TryGetComponent<ParticleSystem>(out var ps))
            ps.Play(withChildren: true);
    }

    private void DespawnWindVfx()
    {
        if (_windVfxGO == null) return;

        // 파티클이 있으면 정지 후 남은 파티클이 자연스럽게 소멸하도록 부모 해제 후 Destroy
        if (_windVfxGO.TryGetComponent<ParticleSystem>(out var ps))
        {
            _windVfxGO.transform.SetParent(null);
            ps.Stop(withChildren: true, stopBehavior: ParticleSystemStopBehavior.StopEmitting);
            Object.Destroy(_windVfxGO, ps.main.startLifetime.constantMax + 0.5f);
        }
        else
        {
            Object.Destroy(_windVfxGO);
        }

        _windVfxGO = null;
    }

    // ── 애니메이션 ─────────────────────────────────────────────
    private static void PlayAnim(MonsterContext ctx, string stateName, float crossFade)
    {
        if (ctx.Animator == null) return;
        if (!ctx.Animator.HasState(0, Animator.StringToHash(stateName)))
        {
            Debug.LogWarning($"[FGCharge] Animator state not found: '{stateName}'", ctx.Monster);
            return;
        }
        ctx.Animator.CrossFade(stateName, crossFade, 0, 0f);
    }

    private static float SpeedMult(MonsterContext ctx)
    {
        var fg = ctx.Monster as ForestGuardianMonster;
        return fg?.FGBlackboard.AnimSpeedMult ?? 1f;
    }
}
}

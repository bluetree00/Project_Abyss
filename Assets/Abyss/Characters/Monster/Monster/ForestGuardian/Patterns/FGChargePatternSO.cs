using UnityEngine;

namespace Abyss.Monster
{
/// <summary>
/// FG 돌진 패턴.
///
/// ━━ 흐름 ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
///  Warning  : 추적 정지 + AttackReady + 경고장판(chargeRange 길이) 채우기
///  Charging : Root Motion 비활성화 → 제자리 Charge 애니 재생
///             → 클립 종료 시 chargeRange 거리 지점으로 워프
/// </summary>
[CreateAssetMenu(menuName = "Abyss/Boss/ForestGuardian/FG_ChargePattern", fileName = "FG_ChargePattern")]
public class FGChargePatternSO : BossPatternSO
{
    [Header("Condition")]
    [Tooltip("최소 발동 거리 (m)")]
    public float minDistance = 8f;
    [Tooltip("최대 발동 거리 (m)")]
    public float maxDistance = 18f;

    [Header("Warning")]
    [Tooltip("경고장판 채우기 시간 (초) — 이 시간 동안 AttackReady 애니 재생")]
    public float warningDuration = 1.2f;
    [Tooltip("경고장판 폭 (m)")]
    public float chargeWidth = 3f;
    [Tooltip("RectWarning 프리팹")]
    public GameObject warningPrefab;

    [Header("Charge")]
    [Tooltip("돌진 거리 (m) — 경고장판 길이이자 클립 종료 시 워프 목표 거리.\n" +
             "애니메이션 클립의 실제 Root Motion 이동량에 맞게 조정하세요.")]
    public float chargeRange = 10f;
    [Tooltip("Charge 클립 길이 감지 실패 시 사용하는 폴백 시간 (초)")]
    public float chargeDurationFallback = 0.8f;

    [Header("Charge VFX")]
    [Tooltip("돌진 중 보스 몸에 붙는 바람 이펙트 프리팹. null이면 재생 안 함.")]
    public GameObject chargeWindVfxPrefab;
    [Tooltip("보스 중심 기준 오프셋 (몸통 중앙에 맞게 조정)")]
    public Vector3 vfxBodyOffset = Vector3.zero;
    [Tooltip("이펙트 스케일")]
    public Vector3 vfxScale = new Vector3(3f, 3f, 3f);

    [Header("Damage")]
    public float damageMultiplier    = 1.2f;
    public float knockbackMultiplier = 1.8f;

    private FGChargeState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new FGChargeState(this);
    public override void OnRecycled()                       => _state = new FGChargeState(this);

    public override bool CanExecute(BossPatternContext ctx)
    {
        if (ctx.Ctx.Runtime.PlayerTarget == null) return false;
        float dist = Vector3.Distance(ctx.Ctx.Transform.position, ctx.Ctx.Runtime.PlayerTarget.position);
        return dist >= minDistance && dist <= maxDistance;
    }

    public override SpecialStateBase GetRuntimeState() => _state;
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// FGChargeState
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class FGChargeState : FullLockState<FGChargePatternSO>
{
    private const string AnimAttackReady = "AttackReady";
    private const string AnimCharge      = "Charge";

    private enum Phase { Warning, Charging }

    private Phase       _phase;
    private float       _timer;
    private Vector3     _chargeDir;
    private Vector3     _chargeStartPos;
    private Vector3     _chargeEndPos;
    private float       _chargeDuration;
    private GameObject  _warningGO;
    private RectWarning _rectWarning;
    private GameObject  _windVfxGO;
    private Vector3     _vfxStartPos;
    private Vector3     _vfxEndPos;

    public FGChargeState(FGChargePatternSO data) : base(data) { }

    // ── 진입 ────────────────────────────────────────────────────
    public override void Enter(MonsterContext ctx)
    {
        _phase = Phase.Warning;
        _timer = 0f;

        if (ctx.Agent != null && ctx.Agent.isActiveAndEnabled && ctx.Agent.isOnNavMesh)
        {
            ctx.Agent.isStopped = true;
            ctx.Agent.ResetPath();
        }

        // 플레이어 방향 계산 (Y 제외)
        Vector3 toPlayer = ctx.Runtime.PlayerTarget != null
            ? ctx.Runtime.PlayerTarget.position - ctx.Transform.position
            : ctx.Transform.forward;
        toPlayer.y = 0f;
        float playerDist = toPlayer.magnitude;
        _chargeDir = playerDist > 0.001f ? toPlayer / playerDist : ctx.Transform.forward;

        ctx.Transform.rotation = Quaternion.LookRotation(_chargeDir);
        _chargeStartPos = ctx.Transform.position;
        // chargeRange 거리로 도착지점 고정 — 플레이어 거리와 무관
        _chargeEndPos = _chargeStartPos + _chargeDir * Data.chargeRange;

        // Warning 중 AttackReady 애니 재생
        PlayAnim(ctx, AnimAttackReady, 0.1f);
        SpawnWarning(ctx);
    }

    // ── 매 프레임 ────────────────────────────────────────────────
    public override void Update(MonsterContext ctx)
    {
        _timer += Time.deltaTime * SpeedMult(ctx);

        switch (_phase)
        {
            case Phase.Warning:
                _rectWarning?.SetFillProgress(_timer / Data.warningDuration);
                if (_timer >= Data.warningDuration)
                    BeginCharge(ctx);
                break;

            case Phase.Charging:
                // VFX를 클립 진행률에 맞춰 시작→도착 위치로 이동
                if (_windVfxGO != null && _chargeDuration > 0f)
                {
                    float vfxT = Mathf.Clamp01(_timer / _chargeDuration);
                    _windVfxGO.transform.position = Vector3.Lerp(_vfxStartPos, _vfxEndPos, vfxT);
                }

                if (_timer >= _chargeDuration)
                    EndCharge(ctx);
                break;
        }
    }

    // ── 종료 ────────────────────────────────────────────────────
    public override void Exit(MonsterContext ctx)
    {
        DespawnWarning();
        StopWindVfx();
        RestoreRootMotion(ctx);
        RestoreAgent(ctx);
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 페이즈 전환
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private void BeginCharge(MonsterContext ctx)
    {
        DespawnWarning();
        _timer = 0f;
        _phase = Phase.Charging;

        float clipLen = GetClipLength(ctx, AnimCharge);
        _chargeDuration = clipLen > 0.01f ? clipLen : Data.chargeDurationFallback;

        if (ctx.Agent != null && ctx.Agent.isActiveAndEnabled && ctx.Agent.isOnNavMesh)
        {
            ctx.Agent.isStopped = true;
            ctx.Agent.velocity  = Vector3.zero;
            ctx.Agent.ResetPath();
        }

        // 애니메이션 종료까지 제자리 재생 — 클립 완료 후 텔레포트
        if (ctx.Animator != null)
            ctx.Animator.applyRootMotion = false;

        SpawnWindVfx(ctx);
        PlayAnim(ctx, AnimCharge, 0.05f);
    }

    private void EndCharge(MonsterContext ctx)
    {
        // Root Motion 비활성화 상태 그대로 워프 (복원은 Exit에서 처리)
        if (ctx.Agent != null && ctx.Agent.enabled && ctx.Agent.isOnNavMesh)
            ctx.Agent.Warp(_chargeEndPos);
        else
            ctx.Transform.position = _chargeEndPos;

        TryDamageOnArrival(ctx);
        ctx.Monster.ChangeState<ChaseState>();
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 데미지 (경고장판 경로 내 플레이어 판정)
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private void TryDamageOnArrival(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null || ctx.Config?.stat == null) return;

        Vector3 toPlayer = ctx.Runtime.PlayerTarget.position - _chargeStartPos;
        float along    = Vector3.Dot(toPlayer, _chargeDir);
        float perpDist = (toPlayer - _chargeDir * along).magnitude;

        if (along < 0f || along > Data.chargeRange) return;
        if (perpDist > Data.chargeWidth * 0.5f) return;

        var player = ctx.Runtime.PlayerTarget.GetComponent<PlayerController>();
        if (player == null) return;

        int dmg = Mathf.Max(1, (int)(ctx.Config.stat.attackPower * Data.damageMultiplier));
        player.TakeDamage(dmg);

        Vector3 kbDir = new Vector3(_chargeDir.x, 0.2f, _chargeDir.z).normalized;
        player.ApplyKnockback(kbDir * ctx.Config.stat.knockbackForce * Data.knockbackMultiplier);
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 경고장판
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private void SpawnWarning(MonsterContext ctx)
    {
        if (Data.warningPrefab == null) return;

        Vector3 pos = ctx.Transform.position;
        pos.y += 0.02f;

        _warningGO = Object.Instantiate(Data.warningPrefab, pos, Quaternion.LookRotation(_chargeDir));
        // chargeRange를 장판 길이로 사용 — 플레이어 거리와 무관
        _warningGO.transform.localScale = new Vector3(Data.chargeWidth, 1f, Data.chargeRange);

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

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 바람 VFX
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private void SpawnWindVfx(MonsterContext ctx)
    {
        if (Data.chargeWindVfxPrefab == null) return;

        // 보스 회전 기준 월드 오프셋 계산 (vfxBodyOffset은 로컬 좌표)
        Vector3 worldOffset = ctx.Transform.rotation * Data.vfxBodyOffset;
        _vfxStartPos = _chargeStartPos + worldOffset;
        _vfxEndPos   = _chargeEndPos   + worldOffset;

        // 부모 없이 월드 좌표로 생성 — Update에서 직접 이동
        _windVfxGO = Object.Instantiate(
            Data.chargeWindVfxPrefab,
            _vfxStartPos,
            ctx.Transform.rotation);
        _windVfxGO.transform.localScale = Data.vfxScale;

        foreach (var ps in _windVfxGO.GetComponentsInChildren<ParticleSystem>(true))
            ps.Play(withChildren: false);
    }

    private void StopWindVfx()
    {
        if (_windVfxGO == null) return;

        float maxLifetime = 0f;
        foreach (var ps in _windVfxGO.GetComponentsInChildren<ParticleSystem>(true))
        {
            ps.Stop(withChildren: false, stopBehavior: ParticleSystemStopBehavior.StopEmitting);
            maxLifetime = Mathf.Max(maxLifetime, ps.main.startLifetime.constantMax);
        }

        Object.Destroy(_windVfxGO, maxLifetime > 0f ? maxLifetime + 0.3f : 0f);
        _windVfxGO = null;
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 유틸
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private static void RestoreRootMotion(MonsterContext ctx)
    {
        if (ctx.Animator != null)
            ctx.Animator.applyRootMotion = true;
    }

    private static void RestoreAgent(MonsterContext ctx)
    {
        if (ctx.Agent == null) return;
        if (!ctx.Agent.enabled) ctx.Agent.enabled = true;
        if (ctx.Agent.isActiveAndEnabled && ctx.Agent.isOnNavMesh)
            ctx.Agent.Warp(ctx.Transform.position);
        else
            ctx.Monster.TrySnapAgentToNavMesh();
        if (ctx.Agent.isOnNavMesh) ctx.Agent.isStopped = false;
    }

    private static void PlayAnim(MonsterContext ctx, string stateName, float crossFade)
    {
        if (ctx.Animator == null) return;
        if (!ctx.Animator.HasState(0, Animator.StringToHash(stateName)))
        {
            Debug.LogWarning($"[FGCharge] Animator state '{stateName}' not found", ctx.Monster);
            return;
        }
        ctx.Animator.CrossFade(stateName, crossFade, 0, 0f);
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
}
}

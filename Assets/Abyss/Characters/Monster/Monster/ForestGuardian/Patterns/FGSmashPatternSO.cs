using PixPlays.ElementalVFX;
using UnityEngine;

namespace Abyss.Monster
{
/// <summary>
/// ForestGuardian 내려 찍기 (Smash) 패턴.
///
/// 조건  : 플레이어가 range 이내
/// 범위  : 360° 원형
/// 이동  : FullLock — 보스 고정, 중단 불가
/// 흐름  : 경고 장판 표시(warningDuration) → 손 선택 애니메이션
///         → 충격파 링 확산(shockwaveDuration, speed m/s) → 복귀
/// 손 선택: 플레이어가 보스 기준 왼쪽이면 PunchSmashLeft, 오른쪽이면 PunchSmashRight
/// </summary>
[CreateAssetMenu(menuName = "Abyss/Boss/ForestGuardian/FG_SmashPattern", fileName = "FG_SmashPattern")]
public class FGSmashPatternSO : BossPatternSO
{
    // ── 범위 ──────────────────────────────────────────────
    [Header("Smash — Range")]
    [Tooltip("최대 데미지 사정거리 (m)")]
    public float range = 5f;

    // ── 타이밍 ────────────────────────────────────────────
    [Header("Smash — Timing")]
    [Tooltip("충격파 발생 전 경고 장판 표시 시간 (초)")]
    public float warningDuration = 1.6f;

    [Tooltip("경고 장판 종료 후 실제 주먹이 바닥에 닿을 때까지 대기 시간 (초)")]
    public float impactDelay = 0.3f;

    [Tooltip("충격파 이펙트 지속 시간 (초)")]
    public float shockwaveDuration = 1.8f;

    [Tooltip("타격 후 자세 복귀 시간 (초)")]
    public float recoveryDuration = 0.3f;

    // ── 충격파 ────────────────────────────────────────────
    [Header("Smash — Shockwave")]
    [Tooltip("충격파 확산 속도 (m/s)")]
    public float shockwaveSpeed = 10f;

    // ── 데미지 ────────────────────────────────────────────
    [Header("Smash — Damage")]
    [Tooltip("기본 attackPower에 곱할 배율")]
    public float damageMultiplier = 1.5f;

    [Tooltip("넉백 힘 배율")]
    public float knockbackMultiplier = 2f;

    // ── 비주얼 ────────────────────────────────────────────
    [Header("Warning Zone")]
    [Tooltip("경고 장판 프리팹 (360° 원형 디스크). null이면 effectPrefab 사용.")]
    public GameObject warningZonePrefab;

    [Tooltip("경고 장판 이펙트 크기 배율 (기본 1, 파티클 자체 크기 기준)")]
    public float warningZoneScale = 1f;

    [Tooltip("충격파 임팩트 이펙트 프리팹 (EarthSlamSpikesAoeVFX 등).")]
    public GameObject shockwavePrefab;

    [Tooltip("충격파 이펙트 크기 배율 (기본 1)")]
    public float shockwaveScale = 1f;

    [Header("Smash — Impact Offset")]
    [Tooltip("왼손/오른손 좌우 오프셋 (m). 보스 right 기준, 오른손이면 +, 왼손이면 -로 자동 반전.")]
    public float handSideOffset = 0.6f;
    [Tooltip("보스 전방 추가 오프셋 (m). 손이 몸보다 앞으로 뻗는 거리.")]
    public float handForwardOffset = 0.5f;

    // ── 런타임 ───────────────────────────────────────────
    private FGSmashState _state;

    public override void Initialize(BossPatternContext ctx)
    {
        _state = new FGSmashState(this);
    }

    public override void OnRecycled()
    {
        _state = new FGSmashState(this);
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
// FGSmashState — FullLock (이동 + 중단 불가)
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class FGSmashState : FullLockState<FGSmashPatternSO>
{
    private const string AnimAttackReady = "AttackReady";
    private const string AnimLeft        = "PunchSmashLeft";
    private const string AnimRight       = "PunchSmashRight";

    private enum Phase { Warning, Impact, Shockwave, Recovery }

    private Phase      _phase;
    private float      _timer;
    private bool       _hasDamaged;
    private bool       _isLeftHand;
    private GameObject _warningGO;
    private GameObject _shockwaveGO;
    private Vector3    _impactPos;          // 손이 닿을 지점 — 경고장판·이펙트·데미지 공유
    private Vector3    _warningTargetScale;

    public FGSmashState(FGSmashPatternSO data) : base(data) { }

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

        FacePlayer(ctx);
        _isLeftHand = IsPlayerOnLeft(ctx);

        // 손이 닿을 지점 계산: 플레이어 위치 기준 + 손 방향 오프셋
        // (경고장판·이펙트·데미지가 모두 이 지점을 사용)
        _impactPos = CalcImpactPos(ctx);

        SpawnWarning(ctx);
        PlayAnim(ctx, AnimAttackReady);  // 경고 장판 채우기 동안 준비 자세
    }

    public override void Update(MonsterContext ctx)
    {
        _timer += Time.deltaTime * SpeedMult(ctx);

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
                    _phase = Phase.Impact;
                    DespawnWarning();
                    PlayAnim(ctx, _isLeftHand ? AnimLeft : AnimRight);  // 경고 종료 → 타격 애니메이션
                }
                break;

            case Phase.Impact:
                if (_timer >= Data.impactDelay)
                {
                    _timer = 0f;
                    _phase = Phase.Shockwave;
                    SpawnShockwave(ctx);
                }
                break;

            case Phase.Shockwave:
                float radius = _timer * Data.shockwaveSpeed;

                // 충격파 링이 플레이어 위치를 통과하는 순간 1회 데미지
                if (!_hasDamaged && ctx.Runtime.PlayerTarget != null)
                {
                    float playerDist = Vector3.Distance(_impactPos, ctx.Runtime.PlayerTarget.position);
                    if (playerDist <= Data.range && radius >= playerDist)
                    {
                        _hasDamaged = true;
                        DealDamage(ctx);
                    }
                }

                if (radius >= Data.range || _timer >= Data.shockwaveDuration)
                {
                    _timer = 0f;
                    _phase = Phase.Recovery;
                    DespawnShockwave();
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
        DespawnShockwave();

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

    // ── 임팩트 위치 계산 ──────────────────────────────────
    private Vector3 CalcImpactPos(MonsterContext ctx)
    {
        Vector3 origin = ctx.Transform.position;

        // 기준점: 플레이어 위치가 있으면 그쪽으로, 없으면 전방 오프셋
        Vector3 target = ctx.Runtime.PlayerTarget != null
            ? ctx.Runtime.PlayerTarget.position
            : origin + ctx.Transform.forward * Data.range;

        // 보스에서 너무 멀면 range 안으로 클램프
        Vector3 dir = (target - origin);
        dir.y = 0f;
        if (dir.magnitude > Data.range)
            target = origin + dir.normalized * Data.range;

        // 손 방향 오프셋 (왼손이면 오른쪽 반대)
        float side = _isLeftHand ? -Data.handSideOffset : Data.handSideOffset;
        target += ctx.Transform.right    * side;
        target += ctx.Transform.forward  * Data.handForwardOffset;
        target.y = origin.y;  // 지면 레벨 고정
        return target;
    }

    // ── 경고 장판 (임팩트 지점 원형 디스크) ──────────────
    private void SpawnWarning(MonsterContext ctx)
    {
        var prefab = Data.ResolveWarningPrefab();
        if (prefab == null) return;

        Vector3 pos  = _impactPos;
        pos.y       += 0.02f;
        float s      = Data.range;
        _warningTargetScale = new Vector3(s, 1f, s);
        _warningGO = Object.Instantiate(prefab, pos, Quaternion.identity);
        _warningGO.transform.localScale = Vector3.zero;
    }

    private void DespawnWarning()
    {
        if (_warningGO == null) return;
        Object.Destroy(_warningGO);
        _warningGO = null;
    }

    // ── 충격파 링 ─────────────────────────────────────────
    private void SpawnShockwave(MonsterContext ctx)
    {
        var prefab = Data.shockwavePrefab;
        if (prefab == null) return;

        Vector3 pos  = _impactPos;
        pos.y       += 0.02f;
        _shockwaveGO = Object.Instantiate(prefab, pos, Quaternion.identity);

        float s = Data.shockwaveScale;
        if (!Mathf.Approximately(s, 1f))
            _shockwaveGO.transform.localScale = new Vector3(s, s, s);

        if (_shockwaveGO.TryGetComponent<PlayableVfx>(out var vfx))
            vfx.Play();
    }

    private void DespawnShockwave()
    {
        if (_shockwaveGO == null) return;
        Object.Destroy(_shockwaveGO);
        _shockwaveGO = null;
    }

    // ── 데미지 판정 ───────────────────────────────────────
    private void DealDamage(MonsterContext ctx)
    {
        if (ctx.Config?.stat == null || ctx.Runtime.PlayerTarget == null) return;

        var player = ctx.Runtime.PlayerTarget.GetComponent<PlayerController>();
        if (player == null) return;

        int dmg = Mathf.Max(1, (int)(ctx.Config.stat.attackPower * Data.damageMultiplier));
        player.TakeDamage(dmg);

        Vector3 dir = ctx.Runtime.PlayerTarget.position - _impactPos;
        dir.y = 0.3f;
        if (dir.sqrMagnitude > 0.001f) dir.Normalize();
        player.ApplyKnockback(dir * ctx.Config.stat.knockbackForce * Data.knockbackMultiplier);
    }

    // ── 애니메이션 재생 ─────────────────────────────────────
    private static void PlayAnim(MonsterContext ctx, string stateName)
    {
        if (ctx.Animator == null) return;
        if (!ctx.Animator.HasState(0, Animator.StringToHash(stateName)))
        {
            Debug.LogWarning($"[FGSmash] Animator state not found: '{stateName}'", ctx.Monster);
            return;
        }
        ctx.Animator.speed = 1f;
        ctx.Animator.CrossFade(stateName, 0.1f, 0, 0f);
    }

    private static float SpeedMult(MonsterContext ctx)
    {
        var fg = ctx.Monster as ForestGuardianMonster;
        return fg?.FGBlackboard.AnimSpeedMult ?? 1f;
    }
}
}

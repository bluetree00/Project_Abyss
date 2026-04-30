using UnityEngine;

namespace Abyss.Monster
{
/// <summary>
/// ForestGuardian 잡아 던지기 (GrabThrow) 패턴.
///
/// 조건  : 플레이어가 range 이내, 전방 arcHalfAngle 부채꼴
/// 흐름  : Windup(경고+잡기판정) → Hold(RightHand→LeftHand 내리찍기×2) → Throw → Recovery
/// 경고  : Windup 중 45° 부채꼴 경고장판 표시, Grab 성공/실패 시 제거
/// 손    : slam1 이전 → RightHand 위치, slam1 이후 → LeftHand 위치로 이동
/// 던지기: throwMinDistance 위치로 순간이동 후 ApplyKnockback — 원거리 사정거리(10m) 밖 보장
/// </summary>
[CreateAssetMenu(menuName = "Abyss/Boss/ForestGuardian/FG_GrabThrowPattern", fileName = "FG_GrabThrowPattern")]
public class FGGrabThrowPatternSO : BossPatternSO
{
    // ── 범위 ──────────────────────────────────────────────
    [Header("GrabThrow — Range")]
    [Tooltip("잡기 사거리 (m)")]
    public float range = 2f;

    [Tooltip("잡기 판정 부채꼴 반각 (22.5 = 전방 45°)")]
    public float arcHalfAngle = 22.5f;

    // ── 타이밍 ────────────────────────────────────────────
    [Header("GrabThrow — Timing")]
    [Tooltip("잡기 판정까지 대기 시간 (초) — 손이 닿는 시점")]
    public float grabTime = 0.3f;

    [Tooltip("첫 번째 내리찍기 데미지 시점 (grabTime 기준 경과 초)")]
    public float slam1Offset = 1.7f;

    [Tooltip("두 번째 내리찍기 데미지 시점 (grabTime 기준 경과 초)")]
    public float slam2Offset = 3.7f;

    [Tooltip("던지기 시점 (grabTime 기준 경과 초)")]
    public float throwOffset = 4.5f;

    [Tooltip("자세 복귀 시간 (초)")]
    public float recoveryDuration = 0.5f;

    // ── 데미지 ────────────────────────────────────────────
    [Header("GrabThrow — Damage")]
    [Tooltip("내리찍기 1회당 attackPower 배율")]
    public float slamDamageMultiplier = 1.2f;

    [Tooltip("던지기 데미지 배율")]
    public float throwDamageMultiplier = 1.5f;

    [Tooltip("던지기 추가 넉백 힘")]
    public float throwKnockbackForce = 8f;

    [Tooltip("던지기 넉백 지속 시간 (초)")]
    public float throwKnockbackDuration = 0.4f;

    // ── 저글링 이동 ───────────────────────────────────────
    [Header("GrabThrow — Juggle")]
    [Tooltip("저글링 시 날아가는 속도 (m/s)")]
    public float juggleSpeed = 12f;

    [Tooltip("손 위치에 도달로 판정하는 반경 (m)")]
    public float juggleCatchRadius = 0.4f;

    [Tooltip("저글링 이동 방향의 위쪽 성분 — 높을수록 포물선이 큼")]
    public float juggleArcUp = 0.8f;

    // ── 비주얼 ────────────────────────────────────────────
    [Header("GrabThrow — Visual")]
    [Tooltip("잡기 경고장판 프리팹 (FanMeshWarning 포함, 45°). null이면 effectPrefab 사용.")]
    public GameObject warningZonePrefab;

    // ── 런타임 ────────────────────────────────────────────
    private FGGrabThrowState _state;

    public override void Initialize(BossPatternContext ctx)
    {
        _state = new FGGrabThrowState(this);
    }

    public override void OnRecycled()
    {
        _state = new FGGrabThrowState(this);
    }

    public override bool CanExecute(BossPatternContext ctx)
    {
        if (ctx.Ctx.Runtime.PlayerTarget == null) return false;
        Vector3 toPlayer = ctx.Ctx.Runtime.PlayerTarget.position - ctx.Ctx.Transform.position;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude > range * range) return false;
        if (toPlayer.sqrMagnitude > 0.001f &&
            Vector3.Angle(ctx.Ctx.Transform.forward, toPlayer) > arcHalfAngle) return false;
        return true;
    }

    public override SpecialStateBase GetRuntimeState() => _state;

    internal GameObject ResolveWarningPrefab()
        => warningZonePrefab != null ? warningZonePrefab : effectPrefab;
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// FGGrabThrowState — FullLock (이동 + 중단 불가)
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class FGGrabThrowState : FullLockState<FGGrabThrowPatternSO>
{
    private const string AnimGrab = "GrabBiteShakeSpit";

    private enum Phase { Windup, Hold, Recovery }

    private Phase            _phase;
    private float            _timer;
    private float            _holdTimer;
    private bool             _grabbed;
    private bool             _slam1Done;
    private bool             _slam2Done;
    private bool             _throwDone;
    private PlayerController _heldPlayer;
    private Vector3          _bossForward;
    private GameObject       _warningGO;
    private Vector3          _warningTargetScale;

    // 저글링 이동
    private bool    _isJuggling;
    private Vector3 _juggleStartPos;
    private float   _juggleProgress;
    private float   _juggleDuration;

    public FGGrabThrowState(FGGrabThrowPatternSO data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        _phase       = Phase.Windup;
        _timer       = 0f;
        _holdTimer   = 0f;
        _grabbed     = false;
        _slam1Done   = false;
        _slam2Done   = false;
        _throwDone   = false;
        _heldPlayer  = null;
        _isJuggling  = false;

        if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
        {
            ctx.Agent.isStopped = true;
            ctx.Agent.ResetPath();
        }

        FacePlayer(ctx);
        _bossForward = ctx.Transform.forward;

        SpawnWarning(ctx);
        PlayAnim(ctx, AnimGrab);
    }

    public override void Update(MonsterContext ctx)
    {
        float dt = Time.deltaTime * SpeedMult(ctx);
        _timer += dt;

        switch (_phase)
        {
            case Phase.Windup:
                // 경고 장판 서서히 커지기 (보스 기준 부채꼴 → 보스에서부터 서서히 확장)
                if (_warningGO != null && Data.grabTime > 0f)
                {
                    float t = Mathf.Clamp01(_timer / Data.grabTime);
                    _warningGO.transform.localScale = Vector3.Lerp(Vector3.zero, _warningTargetScale, t);
                }
                if (_timer >= Data.grabTime)
                {
                    DespawnWarning();
                    TryGrab(ctx);

                    // 잡기 실패 시 즉시 패턴 스킵
                    if (!_grabbed)
                    {
                        _phase = Phase.Recovery;
                        _timer = 0f;
                    }
                    else
                    {
                        _phase     = Phase.Hold;
                        _holdTimer = 0f;
                    }
                }
                break;

            case Phase.Hold:
                _holdTimer += dt;

                // 잡힌 플레이어 — slam1 전: RightHand, slam1 후: LeftHand에 고정
                if (_grabbed && _heldPlayer != null)
                    HoldPlayerAtHand(ctx);

                // 내리찍기 1 — 데미지 후 RightHand → LeftHand 저글링
                if (!_slam1Done && _holdTimer >= Data.slam1Offset)
                {
                    _slam1Done = true;
                    if (_grabbed)
                    {
                        DealSlamDamage(ctx);
                        StartJuggle(ctx, HumanBodyBones.LeftHand);
                    }
                }

                // 내리찍기 2 — 데미지 후 LeftHand → RightHand 저글링
                if (!_slam2Done && _holdTimer >= Data.slam2Offset)
                {
                    _slam2Done = true;
                    if (_grabbed)
                    {
                        DealSlamDamage(ctx);
                        StartJuggle(ctx, HumanBodyBones.RightHand);
                    }
                }

                // 던지기
                if (!_throwDone && _holdTimer >= Data.throwOffset)
                {
                    _throwDone = true;
                    if (_grabbed) Throw(ctx);
                }

                // 애니메이션 종료 후 Recovery
                if (_holdTimer >= Data.throwOffset + 0.8f)
                {
                    _phase = Phase.Recovery;
                    _timer = 0f;
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
        ReleasePlayer();

        if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
            ctx.Agent.isStopped = false;
    }

    // ── 잡기 판정 ─────────────────────────────────────────
    private void TryGrab(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null) return;

        Vector3 toPlayer = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        toPlayer.y = 0f;

        if (toPlayer.sqrMagnitude > Data.range * Data.range) return;
        if (toPlayer.sqrMagnitude > 0.001f &&
            Vector3.Angle(ctx.Transform.forward, toPlayer) > Data.arcHalfAngle) return;

        var player = ctx.Runtime.PlayerTarget.GetComponent<PlayerController>();
        if (player == null) return;

        _grabbed     = true;
        _heldPlayer  = player;
        _bossForward = ctx.Transform.forward;

        player.SetMoveScale(0f);
    }

    // ── RightHand → LeftHand → RightHand 순서로 손바닥 위치에 고정 ──
    private void HoldPlayerAtHand(MonsterContext ctx)
    {
        if (_heldPlayer == null || ctx.Animator == null) return;

        // slam1 전: RightHand / slam1~slam2: LeftHand / slam2 후: RightHand
        HumanBodyBones hand = !_slam1Done ? HumanBodyBones.RightHand
                            : !_slam2Done ? HumanBodyBones.LeftHand
                            : HumanBodyBones.RightHand;

        var bone = ctx.Animator.GetBoneTransform(hand);
        Vector3 targetPos = bone != null
            ? bone.position
            : ctx.Transform.position + _bossForward * 1.2f + Vector3.up * 1.0f;

        if (_isJuggling)
        {
            _juggleProgress += Time.deltaTime * SpeedMult(ctx) / Mathf.Max(0.01f, _juggleDuration);
            float t = Mathf.Clamp01(_juggleProgress);

            // 포물선 arc: XZ는 선형 보간, Y는 사인 커브로 솟아오름
            Vector3 pos = Vector3.Lerp(_juggleStartPos, targetPos, t);
            pos.y += Mathf.Sin(t * Mathf.PI) * Data.juggleArcUp;
            _heldPlayer.transform.position = pos;

            // Rigidbody가 물리로 간섭하지 않도록 velocity 초기화
            if (_heldPlayer.Rigid != null)
                _heldPlayer.Rigid.linearVelocity = Vector3.zero;

            if (t >= 1f)
            {
                _isJuggling = false;
                _heldPlayer.transform.position = targetPos;
            }
        }
        else
        {
            _heldPlayer.transform.position = targetPos;
        }
    }

    // ── 저글링 시작 — 현재 위치 → targetBone 으로 arc 이동 ─
    private void StartJuggle(MonsterContext ctx, HumanBodyBones targetBone)
    {
        if (_heldPlayer == null || ctx.Animator == null) return;

        var bone = ctx.Animator.GetBoneTransform(targetBone);
        Vector3 target = bone != null
            ? bone.position
            : ctx.Transform.position + _bossForward * 1.2f + Vector3.up;

        _juggleStartPos = _heldPlayer.transform.position;
        float dist      = Vector3.Distance(_juggleStartPos, target);
        _juggleDuration = dist / Mathf.Max(1f, Data.juggleSpeed);
        _juggleProgress = 0f;
        _isJuggling     = true;
    }

    // ── 던지기: Rigid.linearVelocity 직접 설정으로 날려보내기 ──
    private void Throw(MonsterContext ctx)
    {
        if (_heldPlayer == null) return;

        if (ctx.Config?.stat != null)
        {
            int dmg = Mathf.Max(1, (int)(ctx.Config.stat.attackPower * Data.throwDamageMultiplier));
            _heldPlayer.TakeDamage(dmg);
        }

        _heldPlayer.SetMoveScale(1f);

        // Rigidbody velocity 직접 설정 — ApplyKnockback의 _knockbackTimer도 함께 설정해
        // PlayerController 이동 로직이 velocity를 덮어쓰지 않도록 보장
        Vector3 throwDir = new Vector3(_bossForward.x, 0.3f, _bossForward.z).normalized;
        if (_heldPlayer.Rigid != null)
            _heldPlayer.Rigid.linearVelocity = throwDir * Data.throwKnockbackForce;

        // knockbackTimer 설정으로 PlayerController 이동 차단 유지
        _heldPlayer.ApplyKnockback(Vector3.zero, Data.throwKnockbackDuration);

        _heldPlayer = null;
        _grabbed    = false;
    }

    // ── 내리찍기 데미지 ───────────────────────────────────
    private void DealSlamDamage(MonsterContext ctx)
    {
        if (_heldPlayer == null || ctx.Config?.stat == null) return;

        int dmg = Mathf.Max(1, (int)(ctx.Config.stat.attackPower * Data.slamDamageMultiplier));
        _heldPlayer.TakeDamage(dmg);
    }

    // ── 플레이어 해제 ─────────────────────────────────────
    private void ReleasePlayer()
    {
        if (_heldPlayer == null) return;
        _heldPlayer.SetMoveScale(1f);
        _heldPlayer = null;
        _grabbed    = false;
    }

    // ── 경고장판 (45° 부채꼴) ─────────────────────────────
    private void SpawnWarning(MonsterContext ctx)
    {
        var prefab = Data.ResolveWarningPrefab();
        if (prefab == null) return;

        Vector3 pos = ctx.Transform.position;
        pos.y += 0.02f;
        _warningTargetScale = new Vector3(Data.range, 1f, Data.range);
        _warningGO = Object.Instantiate(prefab, pos, ctx.Transform.rotation);
        _warningGO.transform.localScale = Vector3.zero;  // 처음엔 0 → Update에서 서서히 확장
    }

    private void DespawnWarning()
    {
        if (_warningGO == null) return;
        Object.Destroy(_warningGO);
        _warningGO = null;
    }

    // ── 플레이어 방향 회전 ────────────────────────────────
    private static void FacePlayer(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null) return;
        Vector3 dir = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.001f)
            ctx.Transform.rotation = Quaternion.LookRotation(dir);
    }

    // ── 애니메이션 재생 ───────────────────────────────────
    private static void PlayAnim(MonsterContext ctx, string stateName)
    {
        if (ctx.Animator == null) return;
        if (!ctx.Animator.HasState(0, Animator.StringToHash(stateName)))
        {
            Debug.LogWarning($"[FGGrabThrow] Animator state not found: '{stateName}'", ctx.Monster);
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

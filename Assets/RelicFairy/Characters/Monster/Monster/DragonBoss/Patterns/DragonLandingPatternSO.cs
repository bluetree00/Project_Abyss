using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 착지 전이 패턴 — 공중 → 지상.
///
/// 실행 순서:
///   1. Landing_Descend 애니 루프 + Y 하강 (Spawn Y 까지)
///   2. 지면 도달 시 Landing_Touchdown 애니 (착지 충격)
///   3. Touchdown 애니 종료 시:
///      - NavMeshAgent 재활성 + Warp
///      - bb.IsAirborne = false (P7 에서 BodyState.Grounded 로 전환 예정)
///      - ChaseState / DragonRunChaseState / PatrolState 로 복귀 (거리 기반)
///
/// FullLock 제약 — 하강·착지 중 GetHit 전환 차단, 데미지는 적용.
/// L4 Executing 구간이므로 착지 도중 다른 패턴 평가는 발생하지 않는다.
///
/// BossConfigSO 연결: Body_Airborne 조건 엔트리의 patterns 에 포함.
/// </summary>
[CreateAssetMenu(fileName = "DragonLandingPattern",
    menuName = "RelicFairy/Boss/Dragon/LandingPattern")]
public class DragonLandingPatternSO : BossPatternSO
{
    [Header("선택 가중치")]
    [Tooltip("런타임 streak 배수의 기반값. 공통 weight 와 달리 코드로 절대 수정되지 않음.")]
    [SerializeField] private float _baseWeight = 1f;
    public float BaseWeight => _baseWeight;

    [Header("하강 파라미터")]
    [Tooltip("Y 하강 속도 (m/s).")]
    [SerializeField] private float _descentSpeed = 8f;
    [Tooltip("지면 도달 판정 허용 오차 (m). Spawn Y + epsilon 이하면 Touchdown 으로 전이.")]
    [SerializeField] private float _groundedEpsilon = 0.1f;

    [Tooltip("플레이어에게서 이 거리 앞에 내려앉는다 (m) — 0이면 예전처럼 제자리 수직 착지")]
    [SerializeField] private float _landDistance = 7f;

    [Header("애니메이션 상태 이름")]
    [SerializeField] private string _descendStateName   = "Landing_Descend";
    [SerializeField] private string _touchdownStateName = "Landing_Touchdown";

    public float  DescentSpeed       => _descentSpeed;
    public float  GroundedEpsilon    => _groundedEpsilon;
    public float  LandDistance       => _landDistance;
    public string DescendStateName   => _descendStateName;
    public string TouchdownStateName => _touchdownStateName;

    private DragonLandingState _runtimeState;

    public override void Initialize(BossPatternContext ctx)
        => _runtimeState = new DragonLandingState(this);

    public override void OnRecycled() => _runtimeState?.Reset();

    public override bool CanExecute(BossPatternContext ctx)
    {
        if (ctx.Blackboard is not DragonBossBlackboard bb) return false;
        return bb.BodyState == BodyState.Airborne;
    }

    public override SpecialStateBase GetRuntimeState() => _runtimeState;
}

// ────────────────────────────────────────────────────────────────────────────
// Runtime state
// ────────────────────────────────────────────────────────────────────────────

internal sealed class DragonLandingState : FullLockState<DragonLandingPatternSO>
{
    private enum Phase { Descend, Touchdown, Done }

    private Phase   _phase;
    private float   _targetY;
    private int     _touchdownHash;
    private Vector3 _landXZ;        // 내려앉을 수평 위치 — 플레이어 쪽(선회 궤도 제자리가 아니라)
    private float   _horizSpeed;

    internal DragonLandingState(DragonLandingPatternSO data) : base(data) { }

    internal void Reset()
    {
        _phase = Phase.Done;
    }

    public override void Enter(MonsterContext ctx)
    {
        GameCameraController.Instance?.DeactivateDragonTopDownView(0.8f);
        _phase   = Phase.Descend;
        // 착지 지점의 Y는 Spawn Y가 아닌 실제 바닥 높이를 사용 —
        // 그렇지 않으면 NavMeshAgent.Warp이 바닥과 어긋난 위치에서 실패해 착지 후 이동이 안 됨
        _landXZ        = ResolveLandPoint(ctx);
        _targetY       = DragonPatternFloorUtils.GetFloorY(_landXZ, ctx.Runtime.SpawnPosition.y);
        _touchdownHash = Animator.StringToHash(Data.TouchdownStateName);
        // 내려오는 동안 수평으로도 옮겨 가 같은 순간에 닿게 한다
        float descendTime = Mathf.Max(0.3f, (ctx.Transform.position.y - _targetY) / Mathf.Max(0.1f, Data.DescentSpeed));
        Vector3 flat = _landXZ - ctx.Transform.position; flat.y = 0f;
        _horizSpeed = flat.magnitude / descendTime;

        PlayAnim(ctx, Data.DescendStateName, 0.15f);
    }

    public override void Update(MonsterContext ctx)
    {
        switch (_phase)
        {
            case Phase.Descend:
                UpdateDescend(ctx);
                break;
            case Phase.Touchdown:
                UpdateTouchdown(ctx);
                break;
        }
    }

    public override void Exit(MonsterContext ctx) { }

    private void UpdateDescend(MonsterContext ctx)
    {
        Vector3 pos = ctx.Transform.position;
        pos.y = Mathf.MoveTowards(pos.y, _targetY, Data.DescentSpeed * Time.deltaTime);
        Vector3 xz = Vector3.MoveTowards(new Vector3(pos.x, 0f, pos.z), new Vector3(_landXZ.x, 0f, _landXZ.z), _horizSpeed * Time.deltaTime);
        pos.x = xz.x; pos.z = xz.z;
        ctx.Transform.position = pos;

        if (pos.y - _targetY <= Data.GroundedEpsilon)
        {
            pos.y = _targetY;
            ctx.Transform.position = pos;
            _phase = Phase.Touchdown;
            PlayAnim(ctx, Data.TouchdownStateName, 0.1f);
        }
    }

    /// <summary>플레이어에서 landDistance 앞(지금 선회 위치 쪽) — NavMesh 위로 스냅. 못 찾으면 제자리.</summary>
    private Vector3 ResolveLandPoint(MonsterContext ctx)
    {
        Vector3 here = ctx.Transform.position;
        if (Data.LandDistance <= 0f || ctx.Runtime.PlayerTarget == null) return here;
        Vector3 player = ctx.Runtime.PlayerTarget.position;
        Vector3 away = here - player; away.y = 0f;
        if (away.sqrMagnitude < 0.01f) away = -ctx.Transform.forward;
        Vector3 want = player + away.normalized * Data.LandDistance;
        want.y = player.y;
        return UnityEngine.AI.NavMesh.SamplePosition(want, out var hit, 6f, UnityEngine.AI.NavMesh.AllAreas)
            ? new Vector3(hit.position.x, here.y, hit.position.z)
            : here;
    }

    private void UpdateTouchdown(MonsterContext ctx)
    {
        if (!IsAnimNearEnd(ctx, _touchdownHash)) return;

        DragonPatternFloorUtils.EnsureAgentOnNavMesh(ctx);

        if ((ctx.Monster as IBoss)?.Blackboard is DragonBossBlackboard bb)
            bb.BodyState = BodyState.Grounded;

        _phase = Phase.Done;
        ReturnToGround(ctx);
    }

    private static void ReturnToGround(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null || ctx.Monster.IsPlayerDead())
        {
            ctx.Monster.ChangeState<PatrolState>();
            return;
        }

        if (ctx.Monster is DragonBossMonster dragon
            && ctx.Runtime.DistToPlayer > dragon.WalkToRunThreshold)
        {
            ctx.Monster.ChangeState<DragonRunChaseState>();
        }
        else
        {
            ctx.Monster.ChangeState<ChaseState>();
        }
    }

    private static void PlayAnim(MonsterContext ctx, string stateName, float fadeDuration)
    {
        if (ctx.Animator == null || string.IsNullOrEmpty(stateName)) return;
        int hash = Animator.StringToHash(stateName);
        if (!ctx.Animator.HasState(0, hash)) return;
        ctx.Animator.speed = 1f;
        ctx.Animator.CrossFade(stateName, fadeDuration, 0, 0f);
    }

    private static bool IsAnimNearEnd(MonsterContext ctx, int hash)
    {
        if (ctx.Animator == null) return true;
        if (!ctx.Animator.HasState(0, hash)) return true;
        if (ctx.Animator.IsInTransition(0)) return false;
        var info = ctx.Animator.GetCurrentAnimatorStateInfo(0);
        if (info.shortNameHash != hash) return true;
        return info.normalizedTime >= 0.9f;
    }
}
}

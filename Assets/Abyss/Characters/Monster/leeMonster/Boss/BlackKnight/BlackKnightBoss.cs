using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// BlackKnight 보스.
///
/// ━━ BT 구조 (매 프레임 루트부터 틱) ━━━━━━━━━━━━━━━━━━━━━━━━━━━
///   BTSelector (Root)
///     BTMemSequence  [전제 조건 + 패턴 선택]
///       BTConditionNode  : 생존 · 플레이어 생존 · 패턴 브레이크 쿨다운 · 사거리
///       BTSelector       [Priority 패턴 선택]
///         BTMemSequence  [SpinSlash]
///           BTConditionNode : SpinSlash.CanExecute
///           BTFSMActionNode : _spinState
///         BTMemSequence  [ChargeAttack]
///           BTConditionNode : ChargeAttack.CanExecute
///           BTFSMActionNode : _chargeState
///         BTMemSequence  [OverheadSlash]
///           BTConditionNode : OverheadSlash.CanExecute
///           BTFSMActionNode : _overheadState
///
/// BTMemSequence 가 Running 자식 인덱스를 기억하므로
/// 패턴 실행 중에는 조건을 재평가하지 않는다 → 패턴이 완료될 때까지 유지.
/// 패턴 종료 후 patternBreakDuration 동안 BT 전체 조건이 막힘 → 기본 평타 허용.
/// </summary>
public class BlackKnightBoss : LeeMonsterBase
{
    public const  string PrefabAddress   = "BlackKnight/BlackKnight";
    private const string AnimatorAddress = "BlackKnight/BlackKnightController";

    protected override string ConfigAddress   => "BlackKnight/BlackKnightConfig";
    protected override string DataAddress     => "";
    protected override string HeadBoneName    => "Head";
    protected override float  HPBarHeadOffset => 1.5f;

    // ── 공격 상태 (BT Action 노드가 래핑) ────────────────
    private BKSpinSlashState     _spinState;
    private BKChargeAttackState  _chargeState;
    private BKOverheadSlashState _overheadState;

    // ── 공유 블랙보드 (쿨다운) ────────────────────────────
    private readonly BossAttackBlackboard _bb = new();

    // ── BT ────────────────────────────────────────────────
    private LeeBTNode _bt;

    // ── 패턴 브레이크 쿨다운 ──────────────────────────────
    private float                _patternBreakCooldown;
    private bool                 _wasInPattern;
    private BlackKnightPatternData _patternData;

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 초기화
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    protected override async UniTask InitAsync()
    {
        await base.InitAsync();
        await LoadBossAnimatorAsync();
    }

    private async UniTask LoadBossAnimatorAsync()
    {
        var ctrl = await Managers.AddressableManager
            .LoadAssetAsync<RuntimeAnimatorController>(AnimatorAddress);

        if (ctrl == null)
        {
            Debug.LogWarning($"[BlackKnightBoss] AnimatorController 로드 실패: {AnimatorAddress}", this);
            return;
        }

        if (_animator != null)
            _animator.runtimeAnimatorController = ctrl;
    }

    protected override void OnInitialized()
    {
        _patternData = _config.specialState0 as BlackKnightPatternData;

        if (_patternData == null)
        {
            Debug.LogWarning("[BlackKnightBoss] specialState0 에 BlackKnightPatternData 가 없습니다.", this);
            return;
        }

        // 공격 상태 생성
        _spinState     = new BKSpinSlashState(_patternData, _bb);
        _chargeState   = new BKChargeAttackState(_patternData, _bb);
        _overheadState = new BKOverheadSlashState(_patternData, _bb);

        // BT 트리 빌드
        _bt = new LeeBTSelector(
            new LeeBTMemSequence(
                // 전제 조건
                new LeeBTConditionNode(ctx =>
                    !_runtime.IsDead
                    && !IsPlayerDead()
                    && _patternBreakCooldown <= 0f
                    && IsInEngagementRange(ctx)),
                // Priority 패턴 선택
                new LeeBTSelector(
                    new LeeBTMemSequence(
                        new LeeBTConditionNode(ctx => _spinState.CanExecute(ctx)),
                        new LeeBTFSMActionNode(_spinState)
                    ),
                    new LeeBTMemSequence(
                        new LeeBTConditionNode(ctx => _chargeState.CanExecute(ctx)),
                        new LeeBTFSMActionNode(_chargeState)
                    ),
                    new LeeBTMemSequence(
                        new LeeBTConditionNode(ctx => _overheadState.CanExecute(ctx)),
                        new LeeBTFSMActionNode(_overheadState)
                    )
                )
            )
        );
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // Update — BT 틱 + 쿨다운 관리
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    protected override void Update()
    {
        float dt = UnityEngine.Time.deltaTime;

        // 쿨다운: 항상 감소
        _bb.TickCooldowns(dt);
        if (_patternBreakCooldown > 0f) _patternBreakCooldown -= dt;

        // 패턴 종료 감지 → 브레이크 쿨다운 시작
        bool inPattern = IsInSpecialState;
        if (_wasInPattern && !inPattern && _patternData != null)
            _patternBreakCooldown = _patternData.patternBreakDuration;
        _wasInPattern = inPattern;

        // 평타 모드 타이머 갱신
        // 특수 상태(패턴) 중엔 0으로 리셋, 아니면 누적
        if (inPattern)
            _bb.NormalModeTimer = 0f;
        else
            _bb.NormalModeTimer += dt;

        // BT 틱 (패턴 선택 및 실행)
        _bt?.Tick(_ctx);

        base.Update();
    }

    // TryGetSpecialState 는 BT 가 직접 ChangeState 를 호출하므로 사용하지 않는다.
    public override ILeeMonsterState TryGetSpecialState(LeeMonsterContext ctx) => null;

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 풀 재사용
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    protected override void OnEnable()
    {
        _bb.Reset();
        _patternBreakCooldown = 0f;
        _wasInPattern         = false;
        _spinState?.ResetThresholds();
        _bt?.Reset();
        base.OnEnable();
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 헬퍼
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    // ChargeAttack(4~9m) 포함한 최대 사거리까지 커버
    private bool IsInEngagementRange(LeeMonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null) return false;
        float maxRange = _patternData != null
            ? UnityEngine.Mathf.Max(ctx.Stat.attackRange, _patternData.chargeMaxDist)
            : ctx.Stat.attackRange;
        return UnityEngine.Vector3.Distance(
            ctx.Transform.position, ctx.Runtime.PlayerTarget.position) <= maxRange;
    }
}

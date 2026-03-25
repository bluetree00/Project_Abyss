using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;


namespace Abyss.Monster
{
/// <summary>
/// BlackKnight 보스.
///
/// ━━ BT 구조 (매 프레임 루트부터 틱) ━━━━━━━━━━━━━━━━━━━━━━━━━━━
///   BTSelector (Root)
///     BTMemSequence  [전제 조건 + 패턴 선택]
///       BTConditionNode  : 생존 · 플레이어 생존 · 패턴 브레이크 쿨다운 · 사거리
///       BTSelector       [Priority 패턴 선택]
///         BTMemSequence  [SpinSlash]        ← HP 임계값 기반
///         BTMemSequence  [LeapSlam]         ← 우선순위 높음 (존재감 큰 패턴)
///         BTMemSequence  [RainAttack]       ← 중거리 이상 시 사용
///         BTMemSequence  [ScatterShot]
///         BTMemSequence  [ChargeAttack]
///         BTMemSequence  [OverheadSlash]    ← 최하 우선순위
///
/// BTMemSequence 가 Running 자식 인덱스를 기억하므로
/// 패턴 실행 중에는 조건을 재평가하지 않는다 → 패턴이 완료될 때까지 유지.
/// 패턴 종료 후 patternBreakDuration 동안 BT 전체 조건이 막힘 → 기본 평타 허용.
/// </summary>
public class BlackKnightBoss : MonsterBase
{
    public const  string PrefabAddress         = "BlackKnight/BlackKnight";
    private const string AnimatorAddress       = "BlackKnight/BlackKnightController";
    private const string PatternConfigAddress  = "BlackKnight/BlackKnightPatternConfig";

    protected override string ConfigAddress   => "BlackKnight/BlackKnightConfig";
    protected override string DataAddress     => "";
    protected override string HeadBoneName    => "Head";
    protected override float  HPBarHeadOffset => 1.5f;
    protected override bool   UseWorldHPBar   => false;  // HUD BossPanel에서 표시

    // ── 공격 상태 (BT Action 노드가 래핑) ────────────────
    private BKSpinSlashState     _spinState;
    private BKChargeAttackState  _chargeState;
    private BKOverheadSlashState _overheadState;
    private BKRainAttackState    _rainState;
    private BKScatterShotState   _scatterState;
    private BKLeapSlamState      _leapState;

    // ── 공유 블랙보드 (쿨다운) ────────────────────────────
    private readonly BossAttackBlackboard _bb = new();

    // ── 풀 ───────────────────────────────────────────────
    private BKProjectilePool   _projectilePool1; // 1차 발사 전용
    private BKProjectilePool   _projectilePool2; // 2차 발사 전용
    private BKEffectPool       _meteorPool;
    private BKEffectPool       _hitVfxPool;
    private BKEffectPool       _leapVfxPool;
    private BKGroundCirclePool _circlePool;
    private BKAudioPool        _audioPool;

    // ── BT ────────────────────────────────────────────────
    private BTNode                     _bt;
    private BKWeightedPatternSelector  _weightedSelector;

    // ── 패턴 브레이크 쿨다운 ──────────────────────────────
    private float                  _patternBreakCooldown;
    private bool                   _wasInPattern;
    private BlackKnightPatternData _patternData;

    // ── SpinSlash 페이즈 인터럽트 ─────────────────────────
    private bool _pendingSpinSlash;

    // ── JSON 설정 오버라이드 ──────────────────────────────
    private string _patternConfigJson;

    // ── HUD 바인딩 (최초 인식 시 1회) ────────────────────
    private bool _hudBound;

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 초기화
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    protected override async UniTask InitAsync()
    {
        await base.InitAsync();
        await LoadBossAnimatorAsync();
        await LoadPatternConfigAsync();
    }

    private async UniTask LoadPatternConfigAsync()
    {
        // LoadAssetAsync 는 키 없을 때 InvalidKeyException 을 throw 하므로
        // LoadResourceLocationsAsync 로 먼저 존재 여부를 확인한다 (예외 없음).
        var locHandle = Addressables.LoadResourceLocationsAsync(
            PatternConfigAddress, typeof(TextAsset));
        var locations = await locHandle.ToUniTask();
        Addressables.Release(locHandle);

        if (locations == null || locations.Count == 0) return; // 미등록 → SO 기본값 유지

        var ta = await Managers.AddressableManager
            .LoadAssetAsync<TextAsset>(PatternConfigAddress);
        if (ta != null)
            _patternConfigJson = ta.text;
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
        var soSource = _config.specialState0 as BlackKnightPatternData;
        if (soSource == null)
        {
            Debug.LogWarning("[BlackKnightBoss] specialState0 에 BlackKnightPatternData 가 없습니다.", this);
            return;
        }

        // SO 런타임 복제 → JSON 수치 오버라이드 (원본 에셋 보호)
        _patternData = Object.Instantiate(soSource);
        if (!string.IsNullOrEmpty(_patternConfigJson))
            JsonUtility.FromJsonOverwrite(_patternConfigJson, _patternData);

        // 공격 상태 생성
        _spinState     = new BKSpinSlashState(_patternData, _bb);
        _chargeState   = new BKChargeAttackState(_patternData, _bb);
        _overheadState = new BKOverheadSlashState(_patternData, _bb);
        _rainState     = new BKRainAttackState(_patternData, _bb);
        _scatterState  = new BKScatterShotState(_patternData, _bb);
        _leapState     = new BKLeapSlamState(_patternData, _bb);

        // 투사체 풀 — 1차/2차 발사가 서로 다른 풀을 사용해 TrailRenderer 잔상 방지
        if (_patternData.scatterProjectilePrefab != null)
        {
            var c1 = new UnityEngine.GameObject("[ProjectilePool1]");
            var c2 = new UnityEngine.GameObject("[ProjectilePool2]");
            c1.transform.SetParent(transform, false);
            c2.transform.SetParent(transform, false);
            _projectilePool1 = new BKProjectilePool(
                _patternData.scatterProjectilePrefab, 10, c1.transform);
            _projectilePool2 = new BKProjectilePool(
                _patternData.scatterProjectilePrefab, 10, c2.transform);
            _scatterState.SetPools(_projectilePool1, _projectilePool2);
        }

        // Rain 전용 VFX 풀 (운석 + 착지 이펙트 + 경고 원)
        var meteorCont = new UnityEngine.GameObject("[MeteorPool]");
        var hitVfxCont = new UnityEngine.GameObject("[HitVfxPool]");
        var circleCont = new UnityEngine.GameObject("[GroundCirclePool]");
        meteorCont.transform.SetParent(transform, false);
        hitVfxCont.transform.SetParent(transform, false);
        circleCont.transform.SetParent(transform, false);

        if (_patternData.rainMeteorVfxPrefab != null)
            _meteorPool = new BKEffectPool(_patternData.rainMeteorVfxPrefab, 16, meteorCont.transform);
        if (_patternData.rainHitVfxPrefab != null)
            _hitVfxPool = new BKEffectPool(_patternData.rainHitVfxPrefab, 16, hitVfxCont.transform);
        _circlePool = new BKGroundCirclePool(24, circleCont.transform);

        // 오디오 풀 (모든 패턴 사운드 공유)
        var audioCont = new UnityEngine.GameObject("[AudioPool]");
        audioCont.transform.SetParent(transform, false);
        _audioPool    = new BKAudioPool(12, audioCont.transform);
        _bb.AudioPool = _audioPool;

        // Leap 착지 VFX 풀
        if (_patternData.leapSlamVfxPrefab != null)
        {
            var leapVfxCont = new UnityEngine.GameObject("[LeapVfxPool]");
            leapVfxCont.transform.SetParent(transform, false);
            _leapVfxPool = new BKEffectPool(_patternData.leapSlamVfxPrefab, 4, leapVfxCont.transform);
            _leapState.SetVfxPool(_leapVfxPool);
        }

        var dropCtx = new BKDropContext(
            _meteorPool, _hitVfxPool, _circlePool,
            _patternData.rainMeteorVfxPrefab,
            _patternData.rainHitVfxPrefab,
            transform);
        _rainState.SetDropContext(dropCtx);

        // 스폰 직후 패턴 즉시 발동 방지 — 초기 쿨다운 부여
        _bb.LeapCooldown    = _patternData.leapCooldown;
        _bb.RainCooldown    = _patternData.rainCooldown    * 0.5f;
        _bb.ScatterCooldown = _patternData.scatterCooldown * 0.5f;

        // BT 트리 빌드
        _bt = new BTSelector(
            new BTMemSequence(
                // 전제 조건
                new BTConditionNode(ctx =>
                    !_runtime.IsDead
                    && !IsPlayerDead()
                    && _patternBreakCooldown <= 0f
                    && IsInEngagementRange(ctx)),
                // 가중치 랜덤 패턴 선택
                BuildWeightedPatternSelector()
            )
        );
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 가중치 패턴 선택기 빌드
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private BKWeightedPatternSelector BuildWeightedPatternSelector()
    {
        var sel = new BKWeightedPatternSelector(
            _patternData.patternRepeatPenaltyDuration,
            _patternData.patternRepeatPenaltyMult);

        // SpinSlash: HP 임계값 충족 + 근거리일 때 일반 후보로도 등장
        // 페이즈 인터럽트(HandleSpinPhaseInterrupt)가 거리 무관 강제 발동을 별도 처리
        sel.Add("Spin",     ctx => _spinState.CanExecute(ctx),    new BTFSMActionNode(_spinState),    _patternData.spinWeight);
        sel.Add("Leap",     ctx => _leapState.CanExecute(ctx),    new BTFSMActionNode(_leapState),    _patternData.leapWeight);
        sel.Add("Rain",     ctx => _rainState.CanExecute(ctx),    new BTFSMActionNode(_rainState),    _patternData.rainWeight);
        sel.Add("Scatter",  ctx => _scatterState.CanExecute(ctx), new BTFSMActionNode(_scatterState), _patternData.scatterWeight);
        sel.Add("Charge",   ctx => _chargeState.CanExecute(ctx),  new BTFSMActionNode(_chargeState),  _patternData.chargeWeight);
        sel.Add("Overhead", ctx => _overheadState.CanExecute(ctx),new BTFSMActionNode(_overheadState),_patternData.overheadWeight);

        _weightedSelector = sel;
        return sel;
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // Update — BT 틱 + 쿨다운 관리
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    protected override void Update()
    {
        float dt = UnityEngine.Time.deltaTime;

        // 오디오 풀 회수 체크
        _audioPool?.Tick();

        // 쿨다운: 항상 감소
        _bb.TickCooldowns(dt);
        if (_patternBreakCooldown > 0f) _patternBreakCooldown -= dt;

        // 패턴 종료 감지 → 브레이크 쿨다운 or 즉시 SpinSlash
        bool inPattern = IsInSpecialState;
        if (_wasInPattern && !inPattern && _patternData != null)
        {
            if (_pendingSpinSlash && _spinState != null && _spinState.IsHpThresholdMet(_ctx))
            {
                // 패턴 종료 즉시 SpinSlash — 브레이크 쿨다운 없음
                _pendingSpinSlash      = false;
                _patternBreakCooldown  = 0f;
                _weightedSelector?.ForceNext("Spin", skipCondition: true);
            }
            else
            {
                _pendingSpinSlash = false;
                _patternBreakCooldown = UnityEngine.Random.Range(
                    _patternData.patternBreakDurationMin,
                    _patternData.patternBreakDurationMax);
            }
        }
        _wasInPattern = inPattern;

        // SpinSlash 페이즈 인터럽트 — 공통 상태 즉시 발동 / 패턴 중 예약
        HandleSpinPhaseInterrupt(inPattern);

        // 평타 모드 타이머 갱신
        if (inPattern)
            _bb.NormalModeTimer = 0f;
        else
            _bb.NormalModeTimer += dt;

        // BT 틱 (패턴 선택 및 실행)
        _bt?.Tick(_ctx);

        // 플레이어 사망 → HUD 해제
        if (_hudBound && IsPlayerDead())
            UnbindHud();

        base.Update();
    }

    /// <summary>
    /// HP 임계값 도달 시 SpinSlash 를 가중치/브레이크 쿨다운과 무관하게 강제 발동한다.
    ///  • 공통 상태(추적/기본공격): 즉시 ForceNext 설정 → 이번 BT 틱에서 발동
    ///  • 특수 패턴 실행 중: _pendingSpinSlash 예약 → 패턴 종료 직후 발동
    /// </summary>
    private void HandleSpinPhaseInterrupt(bool inPattern)
    {
        if (_spinState == null || _weightedSelector == null) return;
        if (!_spinState.IsHpThresholdMet(_ctx)) return;

        if (inPattern)
        {
            // 이미 SpinSlash 자신이 실행 중이면 예약하지 않음
            if (!_pendingSpinSlash)
                _pendingSpinSlash = true;
        }
        else
        {
            // 공통 상태: 즉시 강제 발동
            _pendingSpinSlash     = false;
            _patternBreakCooldown = 0f;
            _weightedSelector.ForceNext("Spin", skipCondition: true);
        }
    }

    // TryGetSpecialState 는 BT 가 직접 ChangeState 를 호출하므로 사용하지 않는다.
    public override IMonsterState TryGetSpecialState(MonsterContext ctx) => null;

    /// <summary>플레이어를 최초 인식하는 순간 HUD Boss 패널을 활성화한다.</summary>
    public override bool ShouldStartChase(MonsterContext ctx)
    {
        bool result = base.ShouldStartChase(ctx);
        if (result && !_hudBound)
        {
            _hudBound = true;
            var hud = UnityEngine.Object.FindFirstObjectByType<HudPresenter>();
            hud?.BindBoss(this);
        }
        return result;
    }

    /// <summary>플레이어를 놓치면 HUD를 끄고 체력을 초기화한다.</summary>
    public override bool ShouldGiveUpChase(MonsterContext ctx)
    {
        bool result = base.ShouldGiveUpChase(ctx);
        if (result && _hudBound)
            UnbindHud();
        return result;
    }

    private void UnbindHud()
    {
        _hudBound = false;

        // 보스 체력 초기화
        if (_runtime != null && _config != null)
        {
            _runtime.CurrentHp = _config.stat.maxHp;
            _runtime.IsDead    = false;
        }

        // HUD 해제 (내부에서 Combat 모드로 복귀)
        var hud = UnityEngine.Object.FindFirstObjectByType<HudPresenter>();
        hud?.UnbindBoss();
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 풀 재사용
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    protected override void OnEnable()
    {
        _bb.Reset();
        _patternBreakCooldown = 0f;
        _wasInPattern         = false;
        _hudBound             = false;
        _spinState?.ResetThresholds();
        _bt?.Reset();

        // 보스 재사용 시 낙하 카운터 초기화 (잔여 코루틴 방지)
        BKConcurrentDrop.ResetActiveCount();

        // 풀 재사용 시에도 스폰 직후 도약 방지
        if (_patternData != null)
        {
            _bb.LeapCooldown    = _patternData.leapCooldown;
            _bb.RainCooldown    = _patternData.rainCooldown    * 0.5f;
            _bb.ScatterCooldown = _patternData.scatterCooldown * 0.5f;
        }

        // 풀 재사용 시 모든 활성 오브젝트 회수
        _projectilePool1?.RecycleAll();
        _projectilePool2?.RecycleAll();
        _meteorPool?.RecycleAll();
        _hitVfxPool?.RecycleAll();
        _leapVfxPool?.RecycleAll();
        _circlePool?.RecycleAll();
        _audioPool?.RecycleAll();

        base.OnEnable();
    }

    private void OnDestroy()
    {
        _projectilePool1?.Dispose();
        _projectilePool2?.Dispose();
        _meteorPool?.Dispose();
        _hitVfxPool?.Dispose();
        _leapVfxPool?.Dispose();
        _circlePool?.Dispose();
        _audioPool?.Dispose();
        _projectilePool1 = null;
        _projectilePool2 = null;
        _meteorPool      = null;
        _hitVfxPool     = null;
        _leapVfxPool    = null;
        _circlePool     = null;
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 헬퍼
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    // 모든 패턴의 최대 사거리까지 커버
    // 점프공격은 거리 무관 — 쿨다운이 0이면 사거리 체크를 면제한다
    private bool IsInEngagementRange(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null) return false;
        if (_bb.LeapCooldown <= 0f) return true; // 점프 가능 상태면 거리 무관 발동 허용
        float maxRange = ctx.Stat.attackRange;
        if (_patternData != null)
        {
            maxRange = UnityEngine.Mathf.Max(maxRange, _patternData.chargeMaxDist);
            maxRange = UnityEngine.Mathf.Max(maxRange, _patternData.scatterRange);
        }
        return UnityEngine.Vector3.Distance(
            ctx.Transform.position, ctx.Runtime.PlayerTarget.position) <= maxRange;
    }
}
}

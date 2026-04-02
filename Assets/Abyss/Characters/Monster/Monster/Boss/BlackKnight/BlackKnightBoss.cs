using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;


namespace Abyss.Monster
{
/// <summary>
/// BlackKnight 보스.
///
/// ━━ 패턴 실행 흐름 ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
///  BossConfigSO.patternEntries (Inspector 조립) 를 위→아래 순서로 평가한다.
///  실제 평가·선택·실행 로직은 BossPatternRunner 에 위임한다.
///
///  [강제 실행 엔트리 (forceExecute=true)]
///   • 패턴 브레이크 쿨다운 무관, 매 프레임 조건 체크
///   • 조건 충족 + 현재 패턴 실행 중 → _pendingForce 에 예약
///   • 조건 충족 + 패턴 없음 → 즉시 실행
///   • 패턴 종료 직후 _pendingForce 가 있으면 브레이크 쿨다운 없이 즉시 실행
///
///  [일반 엔트리 (forceExecute=false)]
///   • 패턴 브레이크 쿨다운 > 0 이면 스킵
///   • 조건 충족 시 patterns 목록에서 selectionMode 에 따라 패턴 선택
///   • 각 패턴의 CanExecute(ctx) 가 참인 패턴만 후보
///
/// ━━ BossConfigSO 로드 ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
///  ConfigAddress 가 가리키는 .asset 은 BossConfigSO 타입이어야 한다.
///  MonsterBase.InitAsync() 가 MonsterConfigSO 로 로드하므로,
///  BossConfigSO : MonsterConfigSO 상속 덕분에 그대로 동작한다.
///  OnInitialized() 에서 _config 를 BossConfigSO 로 캐스팅한다.
/// </summary>
public class BlackKnightBoss : MonsterBase, IBoss
{
    public const string PrefabAddress    = "BlackKnight/BlackKnight";
    private const string AnimatorAddress = "BlackKnight/BlackKnightController";

    protected override string ConfigAddress   => "BlackKnight/BlackKnightConfig";
    protected override string DataAddress     => "";
    protected override string HeadBoneName    => "Head";
    protected override float  HPBarHeadOffset => 1.5f;
    protected override bool   UseWorldHPBar   => false;  // HUD BossPanel에서 표시

    // ── BossConfigSO 참조 ─────────────────────────────────
    private BossConfigSO      _bossConfig;
    private BossPatternContext _patternCtx;

    // ── 공유 블랙보드 (쿨다운·페이즈) ────────────────────
    private readonly BossAttackBlackboard _bb = new();

    // ── 패턴 평가 러너 ────────────────────────────────────
    private BossPatternRunner _runner;

    // ── 오디오 풀 (모든 패턴이 공유) ─────────────────────
    private BKAudioPool _audioPool;

    // ── HP 이정표 (75%·50% 이동속도 증폭) ─────────────────
    private readonly bool[] _hpMilestones = new bool[2];

    // ── HUD 바인딩 ────────────────────────────────────────
    private bool _hudBound;

    // ── IBoss 구현 ────────────────────────────────────────
    /// <summary>현재 HP 비율 (0~1). BossHpConditionSO 에서 사용.</summary>
    public float HpRatio => (_config != null && _config.stat.maxHp > 0)
        ? (float)_runtime.CurrentHp / _config.stat.maxHp : 1f;

    /// <summary>보스 블랙보드. BossLastTagConditionSO / BossTimerConditionSO 에서 사용.</summary>
    public BossAttackBlackboard Blackboard => _bb;

    /// <summary>BKChaseStateSO.OrbitalChaseState 가 선회 모드 판정에 사용.</summary>
    public float PatternBreakCooldown => _runner?.PatternBreakCooldown ?? 0f;

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 초기화
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    protected override async UniTask InitAsync()
    {
        await base.InitAsync();
        await LoadBossAnimatorAsync();
        // 컨트롤러 로드 완료 후 PatrolState 재진입 — 초기 Enter 시점엔 컨트롤러가 없어
        // CrossFade가 무시됐으므로 여기서 다시 실행해 패트롤 애니메이션을 재생한다.
        ChangeState<PatrolState>();
    }

    private async UniTask LoadBossAnimatorAsync()
    {
        var ctrl = await Managers.AddressableManager
            .LoadAssetAsync<RuntimeAnimatorController>(AnimatorAddress);
        if (ctrl != null && _animator != null)
            _animator.runtimeAnimatorController = ctrl;
    }

    protected override void OnInitialized()
    {
        _bossConfig = _config as BossConfigSO;
        if (_bossConfig == null)
        {
            Debug.LogError(
                $"[BlackKnightBoss] ConfigSO '{ConfigAddress}' 가 BossConfigSO 타입이 아닙니다.", this);
            return;
        }

        // 오디오 풀 생성 (모든 패턴 공유)
        var audioCont = new GameObject("[AudioPool]");
        audioCont.transform.SetParent(transform, false);
        _audioPool    = new BKAudioPool(12, audioCont.transform);
        _bb.AudioPool = _audioPool;

        // 패턴 컨텍스트 조립 (IBoss = this 로 조건 SO가 HpRatio / Blackboard 에 접근 가능)
        _patternCtx = new BossPatternContext
        {
            Boss       = this,
            Ctx        = _ctx,
            Blackboard = _bb,
        };

        // 각 패턴 SO 초기화 (런타임 상태 및 풀 생성)
        // 같은 SO가 여러 엔트리에 중복 참조된 경우 Initialize가 한 번만 호출되도록 중복 제거
        if (_bossConfig.patternEntries != null)
        {
            var initialized = new HashSet<BossPatternSO>();
            foreach (var entry in _bossConfig.patternEntries)
            {
                if (entry.patterns == null) continue;
                foreach (var pattern in entry.patterns)
                {
                    if (pattern != null && initialized.Add(pattern))
                        pattern.Initialize(_patternCtx);
                }
            }
        }

        // 조건 키 → ICondition 인스턴스 변환
        BuildConditions(_bossConfig);

        // 패턴 평가 러너 생성
        _runner = new BossPatternRunner(
            _bossConfig,
            _patternCtx,
            isAlive:     () => !_runtime.IsDead && !IsPlayerDead(),
            isInRange:   () => IsInEngagementRange(),
            changeState: state => ChangeState(state),
            onExecuted:  pattern => _bb.LastPatternTag = pattern.patternTag);

        // 스폰 직후 패턴 즉시 발동 방지 — 초기 쿨다운 부여
        SetInitialCooldowns();
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 조건 빌드
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private void BuildConditions(BossConfigSO cfg)
    {
        if (cfg?.patternEntries == null) return;
        foreach (var entry in cfg.patternEntries)
        {
            if (entry.conditions == null || entry.conditions.Count == 0)
            {
                entry.BuiltConditions = System.Array.Empty<ICondition>();
                continue;
            }
            var list = new System.Collections.Generic.List<ICondition>(entry.conditions.Count);
            foreach (var key in entry.conditions)
                list.Add(KeyToCondition(key, cfg));
            entry.BuiltConditions = list.ToArray();
        }
    }

    private ICondition KeyToCondition(BossConditionKey key, BossConfigSO cfg) => key switch
    {
        BossConditionKey.Phase2        => new HpBelowCondition(cfg.condPhase2HpThreshold),
        BossConditionKey.Dist_Close    => new MaxRangeCondition(cfg.condDistClose),
        BossConditionKey.Dist_Far      => new MinRangeCondition(cfg.condDistFar),
        BossConditionKey.AfterBackstep => new LastTagCondition("backstep"),
        BossConditionKey.AfterSidestep => new LastTagCondition("sidestep"),
        BossConditionKey.TimePressure  => new NormalModeTimerCondition(cfg.condTimePressureSecs),
        _                              => new AlwaysTrue(),
    };

    private void SetInitialCooldowns()
    {
        if (_bossConfig?.patternEntries == null) return;

        foreach (var entry in _bossConfig.patternEntries)
        {
            if (entry.patterns == null) continue;
            foreach (var pattern in entry.patterns)
            {
                switch (pattern)
                {
                    case BKLeapSlamPatternSO leap:
                        _bb.LeapCooldown      = Mathf.Max(_bb.LeapCooldown,      leap.leapCooldown);
                        break;
                    case BKRainAttackPatternSO rain:
                        _bb.RainCooldown      = Mathf.Max(_bb.RainCooldown,      rain.rainCooldown      * 0.5f);
                        break;
                    case BKScatterShotPatternSO scatter:
                        _bb.ScatterCooldown   = Mathf.Max(_bb.ScatterCooldown,   scatter.scatterCooldown * 0.5f);
                        break;
                    case BKDashSlashPatternSO dash:
                        _bb.DashSlashCooldown = Mathf.Max(_bb.DashSlashCooldown, dash.dashCooldown       * 0.5f);
                        break;
                }
            }
        }
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // Update
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    protected override void Update()
    {
        float dt = Time.deltaTime;

        _audioPool?.Tick();
        _bb.TickCooldowns(dt);

        _runner?.Tick(dt);

        // NormalModeTimer: 패턴 실행 중이면 0으로 유지, 아니면 누적
        if (_runner != null)
        {
            if (_runner.IsPatternActive) _bb.NormalModeTimer  = 0f;
            else                         _bb.NormalModeTimer += dt;
        }

        // ── HUD ─────────────────────────────────────────
        // BossPatternRunner가 PatrolState를 우회해 직접 공격 상태로 전환해도
        // 플레이어가 감지 범위 안에 있거나 패턴이 실행 중이면 HUD를 바인딩한다.
        if (!_hudBound && _runtime?.PlayerTarget != null)
        {
            float dist = Vector3.Distance(transform.position, _runtime.PlayerTarget.position);
            if (_runner?.IsPatternActive == true
                || dist <= (_config?.detection.detectionRange ?? float.MaxValue))
            {
                _hudBound = true;
                var hud = Object.FindFirstObjectByType<HudPresenter>();
                hud?.BindBoss(this);
            }
        }
        if (_hudBound && IsPlayerDead())
            UnbindHud();

        // 기본 FSM (Patrol/Chase/AttackReady/Attack) 업데이트
        base.Update();
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // MonsterBase 훅 오버라이드
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    public override bool ShouldStartChase(MonsterContext ctx)
    {
        return base.ShouldStartChase(ctx);
    }

    public override bool ShouldGiveUpChase(MonsterContext ctx)
    {
        // 한 번 조우(HUD 바인딩) 후에는 플레이어 사망 시에만 어그로 해제
        // → Update()의 IsPlayerDead() 체크가 UnbindHud를 담당
        if (_hudBound) return false;
        return base.ShouldGiveUpChase(ctx);
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // HP 이정표 반응
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    protected override void OnDamageTaken()
    {
        base.OnDamageTaken(); // specialStates 조건 자동 처리 (보스 Config에 설정된 경우)

        if (_bossConfig == null || _runtime == null || _config == null) return;

        float ratio = _config.stat.maxHp > 0
            ? (float)_runtime.CurrentHp / _config.stat.maxHp
            : 1f;

        // 75% 이하 — 이동속도 +12%
        if (!_hpMilestones[0] && ratio <= 0.75f)
        {
            _hpMilestones[0]   = true;
            _bb.ChaseSpeedMult = Mathf.Min(_bb.ChaseSpeedMult + 0.12f, 1.5f);
        }
        // 50% 이하 — 이동속도 추가 +15%
        if (!_hpMilestones[1] && ratio <= 0.50f)
        {
            _hpMilestones[1]   = true;
            _bb.ChaseSpeedMult = Mathf.Min(_bb.ChaseSpeedMult + 0.15f, 1.5f);
        }
    }

    private void UnbindHud()
    {
        _hudBound = false;

        if (_runtime != null && _config != null)
        {
            _runtime.CurrentHp = _config.stat.maxHp;
            _runtime.IsDead    = false;
        }

        var hud = Object.FindFirstObjectByType<HudPresenter>();
        hud?.UnbindBoss();
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 헬퍼
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    /// <summary>
    /// 패턴 발동 유효 사거리 판정.
    /// Leap 쿨다운이 0이면 거리 무관 허용 (점프는 어디서든 가능).
    /// </summary>
    private bool IsInEngagementRange()
    {
        if (_runtime?.PlayerTarget == null) return false;
        if (_bb.LeapCooldown <= 0f) return true;

        float maxRange = _config?.stat.attackRange ?? 0f;
        if (_bossConfig?.patternEntries != null)
        {
            foreach (var entry in _bossConfig.patternEntries)
            {
                if (entry.patterns == null) continue;
                foreach (var p in entry.patterns)
                {
                    if (p is BKChargeAttackPatternSO c)
                        maxRange = Mathf.Max(maxRange, c.chargeMaxDist);
                    else if (p is BKScatterShotPatternSO s)
                        maxRange = Mathf.Max(maxRange, s.scatterRange);
                    else if (p is BKDashSlashPatternSO d)
                        maxRange = Mathf.Max(maxRange, d.dashMaxDist);
                }
            }
        }
        return Vector3.Distance(transform.position, _runtime.PlayerTarget.position) <= maxRange;
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 풀 재사용
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    protected override void OnEnable()
    {
        _bb.Reset();
        _runner?.Reset();
        _hudBound = false;
        System.Array.Clear(_hpMilestones, 0, _hpMilestones.Length);

        BKConcurrentDrop.ResetActiveCount();

        // 패턴 SO 재사용 초기화 (중복 SO는 한 번만 호출)
        if (_bossConfig?.patternEntries != null)
        {
            var recycled = new HashSet<BossPatternSO>();
            foreach (var entry in _bossConfig.patternEntries)
            {
                if (entry.patterns == null) continue;
                foreach (var pattern in entry.patterns)
                {
                    if (pattern != null && recycled.Add(pattern))
                        pattern.OnRecycled();
                }
            }
        }

        // 초기 쿨다운 재부여
        SetInitialCooldowns();

        _audioPool?.RecycleAll();

        base.OnEnable();
    }

    protected override void OnDisable()
    {
        _audioPool?.RecycleAll();
        base.OnDisable();
    }

    private void OnDestroy()
    {
        if (_bossConfig?.patternEntries != null)
        {
            var disposed = new HashSet<BossPatternSO>();
            foreach (var entry in _bossConfig.patternEntries)
            {
                if (entry.patterns == null) continue;
                foreach (var pattern in entry.patterns)
                {
                    if (pattern != null && disposed.Add(pattern))
                        pattern.Dispose();
                }
            }
        }
        _audioPool?.Dispose();
        _audioPool = null;
    }
}
}

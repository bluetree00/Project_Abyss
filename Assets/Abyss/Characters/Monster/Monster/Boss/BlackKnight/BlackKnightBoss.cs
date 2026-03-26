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
public class BlackKnightBoss : MonsterBase
{
    public const string PrefabAddress        = "BlackKnight/BlackKnight";
    private const string AnimatorAddress     = "BlackKnight/BlackKnightController";

    protected override string ConfigAddress   => "BlackKnight/BlackKnightConfig";
    protected override string DataAddress     => "";
    protected override string HeadBoneName    => "Head";
    protected override float  HPBarHeadOffset => 1.5f;
    protected override bool   UseWorldHPBar   => false;  // HUD BossPanel에서 표시

    // ── BossConfigSO 참조 ─────────────────────────────────
    private BossConfigSO          _bossConfig;
    private BossPatternContext     _patternCtx;

    // ── 공유 블랙보드 (쿨다운·페이즈) ────────────────────
    private readonly BossAttackBlackboard _bb = new();

    // ── 오디오 풀 (모든 패턴이 공유) ─────────────────────
    private BKAudioPool _audioPool;

    // ── 패턴 브레이크 쿨다운 ──────────────────────────────
    private float _patternBreakCooldown;
    private bool  _wasInPattern;

    // ── 강제 실행 예약 ────────────────────────────────────
    private (BossPatternEntry entry, BossPatternSO pattern) _pendingForce;

    // ── 반복 패널티 추적 ──────────────────────────────────
    private BossPatternSO _lastPatternSO;
    private float         _lastPatternTime;

    // ── Sequential 모드 인덱스 추적 ──────────────────────
    private readonly Dictionary<BossPatternEntry, int> _seqIndex = new();

    // ── HUD 바인딩 ────────────────────────────────────────
    private bool _hudBound;

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

        // 패턴 컨텍스트 조립
        _patternCtx = new BossPatternContext
        {
            Ctx       = _ctx,
            Blackboard = _bb,
        };

        // 각 패턴 SO 초기화 (런타임 상태 및 풀 생성)
        if (_bossConfig.patternEntries != null)
        {
            foreach (var entry in _bossConfig.patternEntries)
            {
                if (entry.patterns == null) continue;
                foreach (var pattern in entry.patterns)
                    pattern?.Initialize(_patternCtx);
            }
        }

        // 스폰 직후 패턴 즉시 발동 방지 — 초기 쿨다운 부여
        SetInitialCooldowns();
    }

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
                        _bb.LeapCooldown    = Mathf.Max(_bb.LeapCooldown,    leap.leapCooldown);
                        break;
                    case BKRainAttackPatternSO rain:
                        _bb.RainCooldown    = Mathf.Max(_bb.RainCooldown,    rain.rainCooldown    * 0.5f);
                        break;
                    case BKScatterShotPatternSO scatter:
                        _bb.ScatterCooldown = Mathf.Max(_bb.ScatterCooldown, scatter.scatterCooldown * 0.5f);
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
        if (_patternBreakCooldown > 0f) _patternBreakCooldown -= dt;

        bool inPattern = IsInSpecialState;

        // ── 패턴 종료 감지 ─────────────────────────────
        if (_wasInPattern && !inPattern && _bossConfig != null)
        {
            if (_pendingForce.pattern != null)
            {
                // 예약된 강제 패턴 즉시 실행
                var pending = _pendingForce;
                _pendingForce = default;
                _patternBreakCooldown = 0f;
                ExecutePattern(pending.pattern);
            }
            else
            {
                _patternBreakCooldown = UnityEngine.Random.Range(
                    _bossConfig.patternBreakDurationMin,
                    _bossConfig.patternBreakDurationMax);
            }
        }
        _wasInPattern = inPattern;

        // 방금 패턴을 실행했을 수 있으므로 inPattern 갱신
        inPattern = IsInSpecialState;

        // ── 강제 인터럽트 체크 ──────────────────────────
        HandleForceInterrupts(inPattern);
        inPattern = IsInSpecialState;

        // ── NormalModeTimer ─────────────────────────────
        if (inPattern) _bb.NormalModeTimer = 0f;
        else           _bb.NormalModeTimer += dt;

        // ── 일반 패턴 평가 ──────────────────────────────
        if (!inPattern && _patternBreakCooldown <= 0f)
            EvaluateNormalPatterns();

        // ── HUD ─────────────────────────────────────────
        if (_hudBound && IsPlayerDead())
            UnbindHud();

        // 기본 FSM (Patrol/Chase/AttackReady/Attack) 업데이트
        base.Update();
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 패턴 평가
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private void HandleForceInterrupts(bool inPattern)
    {
        if (_pendingForce.pattern != null) return; // 이미 예약됨
        if (_bossConfig?.patternEntries == null) return;
        if (_runtime.IsDead || IsPlayerDead()) return;

        foreach (var entry in _bossConfig.patternEntries)
        {
            if (!entry.forceExecute) continue;
            if (!AllConditionsMet(entry)) continue;

            var pattern = SelectPatternSkipCanExecute(entry);
            if (pattern == null) continue;

            if (inPattern)
            {
                _pendingForce = (entry, pattern);
            }
            else
            {
                _patternBreakCooldown = 0f;
                ExecutePattern(pattern);
            }
            return;
        }
    }

    private void EvaluateNormalPatterns()
    {
        if (_bossConfig?.patternEntries == null) return;
        if (_runtime.IsDead || IsPlayerDead()) return;
        if (!IsInEngagementRange()) return;

        foreach (var entry in _bossConfig.patternEntries)
        {
            if (entry.forceExecute) continue;
            if (!AllConditionsMet(entry)) continue;

            var pattern = SelectPattern(entry);
            if (pattern == null) continue;

            ExecutePattern(pattern);
            return;
        }
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 조건 평가
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private bool AllConditionsMet(BossPatternEntry entry)
    {
        if (entry.conditions == null || entry.conditions.Count == 0) return true;
        foreach (var cond in entry.conditions)
            if (cond == null || !cond.Evaluate(_patternCtx)) return false;
        return true;
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 패턴 선택
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    /// <summary>CanExecute 를 체크하며 패턴 선택 (일반 엔트리용).</summary>
    private BossPatternSO SelectPattern(BossPatternEntry entry)
    {
        if (entry.patterns == null || entry.patterns.Count == 0) return null;
        return entry.selectionMode switch
        {
            PatternSelectionMode.Sequential => SelectSequential(entry, checkCanExecute: true),
            PatternSelectionMode.Random     => SelectRandom(entry,     checkCanExecute: true),
            _                               => SelectWeightedRandom(entry),
        };
    }

    /// <summary>CanExecute 를 무시하고 패턴 선택 (강제 엔트리용).</summary>
    private BossPatternSO SelectPatternSkipCanExecute(BossPatternEntry entry)
    {
        if (entry.patterns == null || entry.patterns.Count == 0) return null;
        return entry.selectionMode switch
        {
            PatternSelectionMode.Sequential => SelectSequential(entry, checkCanExecute: false),
            PatternSelectionMode.Random     => SelectRandom(entry,     checkCanExecute: false),
            _                               => SelectWeightedRandomSkip(entry),
        };
    }

    private BossPatternSO SelectWeightedRandom(BossPatternEntry entry)
    {
        float totalWeight = 0f;
        foreach (var p in entry.patterns)
        {
            if (p == null) continue;
            if (!p.CanExecute(_patternCtx)) continue;
            totalWeight += ApplyRepeatPenalty(p);
        }
        if (totalWeight <= 0f) return null;

        float roll = UnityEngine.Random.Range(0f, totalWeight);
        float acc  = 0f;
        foreach (var p in entry.patterns)
        {
            if (p == null) continue;
            if (!p.CanExecute(_patternCtx)) continue;
            acc += ApplyRepeatPenalty(p);
            if (roll <= acc) return p;
        }
        return null;
    }

    private BossPatternSO SelectWeightedRandomSkip(BossPatternEntry entry)
    {
        float total = 0f;
        foreach (var p in entry.patterns)
        {
            if (p == null) continue;
            total += Mathf.Max(0f, p.weight);
        }
        if (total <= 0f) return entry.patterns[0];

        float roll = UnityEngine.Random.Range(0f, total);
        float acc  = 0f;
        foreach (var p in entry.patterns)
        {
            if (p == null) continue;
            acc += Mathf.Max(0f, p.weight);
            if (roll <= acc) return p;
        }
        return entry.patterns[entry.patterns.Count - 1];
    }

    private BossPatternSO SelectSequential(BossPatternEntry entry, bool checkCanExecute)
    {
        if (!_seqIndex.TryGetValue(entry, out int idx)) idx = 0;
        int count = entry.patterns.Count;
        for (int i = 0; i < count; i++)
        {
            int    realIdx = (idx + i) % count;
            var p = entry.patterns[realIdx];
            if (p == null) continue;
            if (checkCanExecute && !p.CanExecute(_patternCtx)) continue;
            _seqIndex[entry] = (realIdx + 1) % count;
            return p;
        }
        return null;
    }

    private BossPatternSO SelectRandom(BossPatternEntry entry, bool checkCanExecute)
    {
        var candidates = new List<BossPatternSO>();
        foreach (var p in entry.patterns)
        {
            if (p == null) continue;
            if (checkCanExecute && !p.CanExecute(_patternCtx)) continue;
            candidates.Add(p);
        }
        if (candidates.Count == 0) return null;
        return candidates[UnityEngine.Random.Range(0, candidates.Count)];
    }

    private float ApplyRepeatPenalty(BossPatternSO pattern)
    {
        float w = pattern.weight;
        if (_bossConfig != null && _lastPatternSO == pattern)
        {
            float elapsed = Time.time - _lastPatternTime;
            if (elapsed < _bossConfig.patternRepeatPenaltyDuration)
                w *= _bossConfig.patternRepeatPenaltyMult;
        }
        return Mathf.Max(0f, w);
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 패턴 실행
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private void ExecutePattern(BossPatternSO pattern)
    {
        var state = pattern.GetRuntimeState();
        if (state == null) return;
        ChangeState(state);
        _lastPatternSO   = pattern;
        _lastPatternTime = Time.time;
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // MonsterBase 훅 오버라이드
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    public override bool ShouldStartChase(MonsterContext ctx)
    {
        bool result = base.ShouldStartChase(ctx);
        if (result && !_hudBound)
        {
            _hudBound = true;
            var hud = Object.FindFirstObjectByType<HudPresenter>();
            hud?.BindBoss(this);
        }
        return result;
    }

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
        _patternBreakCooldown = 0f;
        _wasInPattern         = false;
        _hudBound             = false;
        _pendingForce         = default;
        _lastPatternSO        = null;
        _seqIndex.Clear();

        BKConcurrentDrop.ResetActiveCount();

        // 패턴 SO 재사용 초기화
        if (_bossConfig?.patternEntries != null)
        {
            foreach (var entry in _bossConfig.patternEntries)
            {
                if (entry.patterns == null) continue;
                foreach (var pattern in entry.patterns)
                    pattern?.OnRecycled();
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
            foreach (var entry in _bossConfig.patternEntries)
            {
                if (entry.patterns == null) continue;
                foreach (var pattern in entry.patterns)
                    pattern?.Dispose();
            }
        }
        _audioPool?.Dispose();
        _audioPool = null;
    }
}
}

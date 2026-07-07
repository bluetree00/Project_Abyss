using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Random = UnityEngine.Random;

namespace RelicFairy.Monster
{
/// <summary>
/// ForestGuardian 보스 MonoBehaviour.
///
/// ━━ 페이즈 ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
///  1페이즈 (HP 100~50%) : TreantE 머티리얼
///  2페이즈 (HP 50~0%)   : TreantD 머티리얼 (속도/딜레이 변화 없음)
///
/// ━━ 그로기 ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
///  일반 피격 시 강인도 8 감소.
///  BigAttackWindow 중 피격 시 추가 40 감소.
///  강인도 0 → GroggyState 진입, 3초 경직 후 강인도 복원.
///
/// ━━ 패턴 가중치 회복 ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
///  패턴 실행 직후 weight=0, 5~10초에 걸쳐 원래값으로 복원.
/// </summary>
public class ForestGuardianMonster : MonsterBase, IBoss, IBossEntrance
{
    // ── 상수 ─────────────────────────────────────────────────
    private const string Phase2BodyMatAddress  = "ForestGuardian/Materials/TreantD";
    private const string Phase2LimbMatAddress  = "ForestGuardian/Materials/TreantDLimbs";
    private const float  Phase2HpThreshold     = 0.5f;
    private const float  WeightRecoveryMin     = 5f;
    private const float  WeightRecoveryMax     = 10f;

    // ── Inspector ────────────────────────────────────────────
    [Header("ForestGuardian — 렌더러")]
    [Tooltip("body 머티리얼(index 0)을 가진 Renderer 배열")]
    [SerializeField] private Renderer[] _bodyRenderers;
    [Tooltip("limbs 머티리얼(index 1)을 가진 Renderer 배열 (body와 동일 오브젝트여도 무방)")]
    [SerializeField] private Renderer[] _limbRenderers;

    [Header("ForestGuardian — 목소리 사운드 (Voice)")]
    [Tooltip("랜덤 간격으로 재생할 목소리 클립 (Voice1~3)")]
    [SerializeField] private AudioClip[] _voiceClips;
    [Tooltip("패턴 비활성 상태에서 목소리 사운드를 재생하는 최소 간격 (초)")]
    [SerializeField] private float _voiceSfxIntervalMin = 8f;
    [Tooltip("패턴 비활성 상태에서 목소리 사운드를 재생하는 최대 간격 (초)")]
    [SerializeField] private float _voiceSfxIntervalMax = 15f;

    [Header("ForestGuardian — 등장 연출 카메라")]
    [Tooltip("카메라 오프셋 (보스 기준). 우측아래에서 올려다보는 구도. Ch2/Ch3 참고: DK=(1.5,1,-1.5), Dragon=(7,0.5,-2)")]
    [SerializeField] private Vector3 _entranceCamOffset     = new Vector3(4f, 1f, -3f);
    [Tooltip("카메라가 바라볼 지점 = 보스 위치 + 이 오프셋. Y를 높이면 얼굴 방향을 바라본다")]
    [SerializeField] private Vector3 _entranceCamLookOffset = new Vector3(0f, 3f, 0f);
    [Tooltip("플레이어→보스 대각선 이동 시간 (초)")]
    [SerializeField] private float   _entranceCamMoveDuration   = 1.0f;
    [Tooltip("대각선 구도에서 보스를 보여주는 홀드 시간 (초)")]
    [SerializeField] private float   _entranceCamHoldDuration   = 1.5f;
    [Tooltip("보스 대각선→플레이어 복귀 이동 시간 (초)")]
    [SerializeField] private float   _entranceCamReturnDuration = 1.2f;

    [Header("ForestGuardian — 발걸음 사운드 (Walk)")]
    [Tooltip("발이 땅에 닿을 때 랜덤 재생할 발걸음 클립 (Walk1~3)")]
    [SerializeField] private AudioClip[] _footstepClips;
    [Tooltip("걷기 애니메이션 상태 이름 (Animator 상태명과 일치)")]
    [SerializeField] private string _walkStateName = "Walk";
    [Tooltip("걷기 클립 내 발이 땅에 닿는 시점 (normalizedTime). 108프레임 기준 20·48·75·102프레임")]
    [SerializeField] private float[] _footstepPhases = { 0.185f, 0.444f, 0.694f, 0.944f };

    // ── MonsterBase 추상 멤버 ─────────────────────────────────
    protected override string ConfigAddress  => "ForestGuardian/ForestGuardianConfig";
    protected override string DataAddress    => string.Empty;
    protected override string HeadBoneName   => null;
    protected override float  HPBarHeadOffset=> 0.3f;
    protected override bool   UseWorldHPBar  => false;

    // ── IBoss ─────────────────────────────────────────────────
    public float HpRatio =>
        (_runtime != null && _config != null && _config.stat.maxHp > 0)
        ? (float)_runtime.CurrentHp / _config.stat.maxHp
        : 1f;

    public BossAttackBlackboard Blackboard => _coreBB;

    // ── ForestGuardian 공개 접근 ──────────────────────────────
    public ForestGuardianBlackboard FGBlackboard => _fgBB;

    public Vector3 EntranceCamOffset       => _entranceCamOffset;
    public Vector3 EntranceCamLookOffset   => _entranceCamLookOffset;
    public float   EntranceCamMoveDuration   => _entranceCamMoveDuration;
    public float   EntranceCamHoldDuration   => _entranceCamHoldDuration;
    public float   EntranceCamReturnDuration => _entranceCamReturnDuration;

    // ── 내부 필드 ─────────────────────────────────────────────
    private ForestGuardianBlackboard _fgBB;
    private BossAttackBlackboard     _coreBB;
    private BossPatternRunner        _runner;
    private BossPatternContext       _patternCtx;
    private FGDormantState _dormantState;

    // 목소리 사운드
    private float _voiceSfxTimer;
    private float _voiceSfxNextInterval;

    // 발걸음 사운드
    private int   _walkStateHash;
    private int   _footstepStateHash;
    private float _footstepPrevTime;

    // ── 패턴 가중치 회복 ──────────────────────────────────────
    private struct WeightRecoveryEntry
    {
        public BossPatternSO Pattern;
        public float         OriginalWeight;
        public float         Timer;
        public float         Duration;
    }

    private readonly List<WeightRecoveryEntry> _weightRecoveries = new();

    // ── 커스텀 ICondition ─────────────────────────────────────

    private sealed class FGPhase2Condition : ICondition
    {
        private readonly ForestGuardianBlackboard _bb;
        public FGPhase2Condition(ForestGuardianBlackboard bb) => _bb = bb;
        public bool Evaluate(BossPatternContext ctx) => _bb.IsPhase2;
    }

    private sealed class FGGroggyCondition : ICondition
    {
        private readonly ForestGuardianBlackboard _bb;
        public FGGroggyCondition(ForestGuardianBlackboard bb) => _bb = bb;
        public bool Evaluate(BossPatternContext ctx) => _bb.IsGroggy;
    }

    private sealed class FGPhaseChangePendingCondition : ICondition
    {
        private readonly ForestGuardianBlackboard _bb;
        public FGPhaseChangePendingCondition(ForestGuardianBlackboard bb) => _bb = bb;
        public bool Evaluate(BossPatternContext ctx)
        {
            var fg = ctx.Boss as ForestGuardianMonster;
            return fg != null && fg.HpRatio <= 0.5f && !_bb.IsPhase2;
        }
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 레이어드 FSM 상태 등록
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    protected override void RegisterStates()
    {
        base.RegisterStates();
        _fsm.RegisterAs<PatrolState>     (new FGIdleState());
        _fsm.RegisterAs<ChaseState>      (new FGChaseState());
        _fsm.RegisterAs<AttackReadyState>(new FGAttackReadyState());
        _fsm.RegisterAs<AttackState>     (new FGAttackState());
        _fsm.RegisterAs<GetHitState>     (new FGGetHitState());
        _fsm.RegisterAs<DieState>        (new FGDieState());
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 초기화
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    protected override void OnInitialized()
    {
        var bossConfig = _config as BossConfigSO;
        if (bossConfig == null)
        {
            Debug.LogError("[ForestGuardianMonster] Config 이 BossConfigSO 가 아닙니다!", this);
            return;
        }

        _fgBB   = new ForestGuardianBlackboard();
        _coreBB = new BossAttackBlackboard();

        _patternCtx = new BossPatternContext
        {
            Boss       = this,
            Ctx        = _ctx,
            Blackboard = _coreBB,
        };

        // Inspector 미할당 시 SkinnedMeshRenderer 자동 탐색
        if (_bodyRenderers == null || _bodyRenderers.Length == 0)
            _bodyRenderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        if (_limbRenderers == null || _limbRenderers.Length == 0)
            _limbRenderers = _bodyRenderers;

        BuildConditions(bossConfig);
        InitializePatterns(bossConfig);

        _runner = new BossPatternRunner(
            bossConfig,
            _patternCtx,
            isAlive:     () => _runtime != null && !_runtime.IsDead && !IsPlayerDead() && !(_fgBB?.IsGroggy ?? false),
            isInRange:   () => _runtime?.PlayerTarget != null,
            changeState: s  => ChangeState(s),
            onExecuted:  OnPatternExecuted);

        _walkStateHash         = Animator.StringToHash(_walkStateName);
        _voiceSfxNextInterval  = Random.Range(_voiceSfxIntervalMin, _voiceSfxIntervalMax);

        BindBossHud();

        _dormantState = new FGDormantState();
        ChangeState(_dormantState);
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 매 프레임
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    protected override void Update()
    {
        base.Update();

        if (_fgBB == null || _coreBB == null) return;
        if (_dormantState != null && _dormantState.IsActive) return;

        float dt = Time.deltaTime;

        // 블랙보드 틱 (빅어택 윈도우, 그로기 타이머)
        _fgBB.Tick(dt);
        _coreBB.TickCooldowns(dt);

        // 패턴 가중치 회복
        TickWeightRecoveries(dt);

        // 패턴 러너 NormalModeTimer
        if (_runner != null)
        {
            if (_runner.IsPatternActive)
                _coreBB.NormalModeTimer = 0f;
            else
                _coreBB.NormalModeTimer += dt;
        }

        _runner?.Tick(dt);

        UpdateVoiceSfx();
        UpdateFootstepSound();

        // 페이즈 전환 체크 — 패턴 실행 중일 땐 패턴이 직접 트리거하도록 위임
        if (!_fgBB.IsPhase2 && HpRatio < Phase2HpThreshold && !IsInSpecialState)
            EnterPhase2Async().Forget();

        // 그로기 상태 진입
        if (_fgBB.IsGroggy && !IsInSpecialState)
            ChangeState<GetHitState>();
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 피격 처리 (강인도)
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    public override void TakeDamage(float amount, UnityEngine.GameObject instigator, float knockbackMultiplier = 1f, bool isCrit = false)
    {
        if (_fgBB != null && instigator != null)
        {
            bool isHeavy = _fgBB.IsBigWindowOpen;
            Vector3 dir  = instigator.transform.position - transform.position;
            _fgBB.SetHitDirection(dir, transform.forward, isHeavy);
        }

        if (_fgBB != null)
        {
            if (_fgBB.IsTransitioning)
                amount *= 0.01f;          // 변신 연출 중: 99% 감소
            else if (_fgBB.IsPhase2)
                amount *= 0.7f;           // 2페이즈: 30% 감소
        }

        base.TakeDamage(amount, instigator, knockbackMultiplier, isCrit);
    }

    protected override void OnDamageTaken()
    {
        if (_fgBB == null) return;

        bool isHeavy = _fgBB.IsBigWindowOpen;

        // 그로기는 2페이즈에서만 발동
        if (_fgBB.IsPhase2)
            _fgBB.ApplyToughnessDamage(ForestGuardianBlackboard.NormalHitDamage, isHeavy);

        if (_fgBB.IsGroggy)
        {
            // 그로기 우선 — 방어도 억제 없이 base의 ChangeState<GetHitState>() 에 위임
            return;
        }

        // 방어도 데미지 — 파괴되면 GetHit 방향 경직 발동, 그렇지 않으면 추적 유지
        bool poiseBroke = _fgBB.ApplyPoiseDamage(isHeavy);
        if (!poiseBroke)
            _suppressGetHitThisHit = true;   // base의 ChangeState<GetHitState>() 스킵
        // poiseBroke=true 이면 _suppressGetHitThisHit = false 유지 → base가 GetHitState 진입
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 풀 재사용
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    protected override void OnEnable()
    {
        base.OnEnable();
        _runner?.Reset();
        _coreBB?.Reset();
        _fgBB?.Reset();
        _weightRecoveries.Clear();
        _phase2Transitioning    = false;
        _voiceSfxTimer          = 0f;
        _voiceSfxNextInterval = Random.Range(_voiceSfxIntervalMin, _voiceSfxIntervalMax);
        if (_dormantState != null)
            ChangeState(_dormantState);
        BindBossHud();
    }

    protected override void OnDisable()
    {
        UnbindBossHudIfBound();
        base.OnDisable();
    }

    public void UnbindBossHudIfBoundPublic() => UnbindBossHudIfBound();

    /// <summary>2페이즈 전환을 패턴에서 직접 트리거할 때 사용. 머터리얼·속도 교체 포함.</summary>
    public void TriggerPhase2() => EnterPhase2Async().Forget();

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 보스룸 전용: 플레이어가 있으면 항상 추적
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    public override bool ShouldStartChase(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null) return false;
        return !IsPlayerDead();
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 내부 헬퍼
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private void InitializePatterns(BossConfigSO config)
    {
        if (config.patternEntries == null) return;
        foreach (var entry in config.patternEntries)
        {
            if (entry?.patterns == null) continue;
            foreach (var p in entry.patterns)
                p?.Initialize(_patternCtx);
        }
    }

    private void BuildConditions(BossConfigSO config)
    {
        if (config.patternEntries == null) return;
        foreach (var entry in config.patternEntries)
        {
            if (entry == null) continue;
            if (entry.conditions == null || entry.conditions.Count == 0)
            {
                entry.BuiltConditions = null;
                continue;
            }

            var built = new ICondition[entry.conditions.Count];
            for (int i = 0; i < entry.conditions.Count; i++)
                built[i] = BuildSingleCondition(entry.conditions[i], config);
            entry.BuiltConditions = built;
        }
    }

    private ICondition BuildSingleCondition(BossConditionKey key, BossConfigSO config)
    {
        return key switch
        {
            BossConditionKey.Phase2          => new HpBelowCondition(config.condPhase2HpThreshold),
            BossConditionKey.Dist_Close      => new MaxRangeCondition(config.condDistClose),
            BossConditionKey.Dist_Far        => new MinRangeCondition(config.condDistFar),
            BossConditionKey.TimePressure    => new NormalModeTimerCondition(config.condTimePressureSecs),
            BossConditionKey.FG_Phase1             => new HpAboveCondition(config.condPhase2HpThreshold),
            BossConditionKey.FG_Phase2             => new FGPhase2Condition(_fgBB),
            BossConditionKey.FG_PhaseChangePending => new FGPhaseChangePendingCondition(_fgBB),
            BossConditionKey.FG_IsPhase2           => new FGPhase2Condition(_fgBB),
            BossConditionKey.FG_IsGroggy           => new FGGroggyCondition(_fgBB),
            _                                      => new AlwaysTrue(),
        };
    }

    // ── 패턴 실행 콜백 ─────────────────────────────────────────
    private void OnPatternExecuted(BossPatternSO pattern)
    {
        _coreBB.LastPatternTag  = pattern.patternTag;
        _coreBB.NormalModeTimer = 0f;

        // 가중치 즉시 0, 5~10초에 걸쳐 복원 예약
        float original = pattern.weight;
        if (original <= 0f) return;

        pattern.weight = 0f;

        // 기존 회복 항목 제거 — 부분 회복 중 재발동 시 OriginalWeight가 계속 감소하는 버그 방지
        // 이전 항목에 더 높은 원래 값이 있으면 그 값으로 복원
        for (int i = _weightRecoveries.Count - 1; i >= 0; i--)
        {
            if (!ReferenceEquals(_weightRecoveries[i].Pattern, pattern)) continue;
            if (_weightRecoveries[i].OriginalWeight > original)
                original = _weightRecoveries[i].OriginalWeight;
            _weightRecoveries.RemoveAt(i);
        }

        _weightRecoveries.Add(new WeightRecoveryEntry
        {
            Pattern        = pattern,
            OriginalWeight = original,
            Timer          = 0f,
            Duration       = Random.Range(WeightRecoveryMin, WeightRecoveryMax),
        });
    }

    private void TickWeightRecoveries(float dt)
    {
        for (int i = _weightRecoveries.Count - 1; i >= 0; i--)
        {
            var e = _weightRecoveries[i];
            e.Timer += dt;
            float t = Mathf.Clamp01(e.Timer / e.Duration);
            e.Pattern.weight = Mathf.Lerp(0f, e.OriginalWeight, t);

            if (t >= 1f)
            {
                e.Pattern.weight = e.OriginalWeight;
                _weightRecoveries.RemoveAt(i);
            }
            else
            {
                _weightRecoveries[i] = e;
            }
        }
    }

    // ── 페이즈 전환 ────────────────────────────────────────────
    private bool _phase2Transitioning;

    private async UniTaskVoid EnterPhase2Async()
    {
        if (_phase2Transitioning) return;
        _phase2Transitioning = true;

        Debug.Log($"[FG] Phase2 시작 — HP={_runtime?.CurrentHp}/{EffectiveMaxHp} ratio={HpRatio:F2}", this);
        _fgBB.SetPhase2();

        // 머티리얼 교체 (Addressable 로드)
        try
        {
            var matBody = await Managers.AddressableManager.LoadAssetAsync<Material>(Phase2BodyMatAddress);
            var matLimb = await Managers.AddressableManager.LoadAssetAsync<Material>(Phase2LimbMatAddress);

            Debug.Log($"[FG] 머티리얼 로드 — body={matBody?.name ?? "NULL"}, limb={matLimb?.name ?? "NULL"}, bodyRenderers={_bodyRenderers?.Length ?? 0}", this);

            if (!destroyCancellationToken.IsCancellationRequested)
            {
                ApplyMaterials(_bodyRenderers, matBody, 0);
                ApplyMaterials(_limbRenderers, matLimb, 1);
                Debug.Log("[FG] 머티리얼 교체 완료", this);
            }
            else
            {
                Debug.LogWarning("[FG] 머티리얼 교체 취소됨 (destroyCancellationToken)", this);
            }
        }
        catch (System.OperationCanceledException)
        {
            // 씬 전환 등으로 취소됨 — 정상 흐름
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[ForestGuardianMonster] 2페이즈 머티리얼 로드 실패: {e.Message}", this);
        }
    }

    /// <summary>패턴 비활성 상태에서 랜덤 간격으로 Voice1~3 중 하나를 재생해 "살아있는 보스" 느낌을 준다.</summary>
    private void UpdateVoiceSfx()
    {
        if (_runtime == null || _runtime.IsDead) return;
        if (_voiceClips == null || _voiceClips.Length == 0) return;

        if (_runner != null && _runner.IsPatternActive)
        {
            _voiceSfxTimer = 0f;
            return;
        }

        _voiceSfxTimer += Time.deltaTime;
        if (_voiceSfxTimer < _voiceSfxNextInterval) return;

        _voiceSfxTimer        = 0f;
        _voiceSfxNextInterval = Random.Range(_voiceSfxIntervalMin, _voiceSfxIntervalMax);
        var clip = _voiceClips[Random.Range(0, _voiceClips.Length)];
        Managers.Sound?.PlayEffectAt(clip, transform.position);
    }

    /// <summary>Walk 애니메이션 재생 중 발이 땅에 닿는 시점(normalizedTime)을 지날 때마다 Walk1~3 중 하나를 랜덤 재생한다.</summary>
    private void UpdateFootstepSound()
    {
        if (_footstepClips == null || _footstepClips.Length == 0
            || _footstepPhases == null || _footstepPhases.Length == 0
            || _animator == null) return;

        var info = _animator.GetCurrentAnimatorStateInfo(0);
        int hash = info.shortNameHash;

        if (hash != _walkStateHash)
        {
            _footstepStateHash = 0;
            return;
        }

        if (_footstepStateHash != hash)
        {
            _footstepStateHash = hash;
            _footstepPrevTime  = info.normalizedTime;
            return;
        }

        float currentTime = info.normalizedTime;
        foreach (float phase in _footstepPhases)
        {
            int prevCycle = Mathf.FloorToInt(_footstepPrevTime - phase);
            int curCycle  = Mathf.FloorToInt(currentTime - phase);
            if (curCycle != prevCycle)
            {
                var clip = _footstepClips[Random.Range(0, _footstepClips.Length)];
                Managers.Sound?.PlayEffectAt(clip, transform.position);
                break;
            }
        }
        _footstepPrevTime = currentTime;
    }

    private static void ApplyMaterials(Renderer[] renderers, Material mat, int matIndex = 0)
    {
        if (mat == null || renderers == null) return;
        foreach (var r in renderers)
        {
            if (r == null) continue;
            var mats = r.materials;
            if (matIndex < mats.Length)
            {
                mats[matIndex] = mat;
                r.materials = mats;
            }
        }
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // IBossEntrance
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    public override bool HasEntranceAnimation => true;

    // OnEntranceRequested 는 발행하지 않는다 — BRC 카메라 팬 개입 방지
    public event Action OnEntranceRequested;
    public event Action OnCombatReady;

    internal void FireCombatReady()
    {
        _runner?.EnsureMinBreakCooldown(3f);
        OnCombatReady?.Invoke();
        RaiseBossCombatReady();
    }

    // BRC 호출용 — 이 보스는 Enter 에서 직접 시작하므로 실질적으로 호출되지 않음
    public void TriggerEntrance()
    {
        _dormantState?.TriggerEntrance(_ctx);
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 등장 연출 전용 메서드 (FGDormantState 에서 호출)
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    internal void PlayEntranceVoice()
    {
        if (_voiceClips == null || _voiceClips.Length == 0) return;
        var clip = _voiceClips[Random.Range(0, _voiceClips.Length)];
        Managers.Sound?.PlayEffectAt(clip, transform.position);
    }
}
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace RelicFairy.Monster
{
/// <summary>
/// 훈련용 허수아비 — <b>실제 몬스터</b>(MonsterBase)로 동작한다.
///
/// 기존 TrainingDummy는 MonsterBase가 아닌 독립 IDamageable이라 상태이상 수신기가 없었고,
/// 받는피해 증폭(ApplyDamageTakenAmp)·방어력·6속성 상태가 전부 적용되지 않아
/// 측정값이 실전과 달랐다(호출처가 모두 MonsterBase 강타입이라 더미를 건너뛴다).
/// 이 클래스는 MonsterBase를 그대로 상속하므로 <b>데미지 파이프라인 전체가 자동으로 동일</b>하다.
///
/// 허수아비로 만들기 위해 더한 것은 세 가지뿐이다.
///  · 불사      : HpFloorMin1 — HP가 1 미만으로 안 떨어져 처치되지 않는다(MonsterBase 기본 기능).
///  · 위치 고정 : NavMeshAgent를 끄고 매 프레임 스폰 위치로 되돌린다. 넉백에도 밀리지 않는다.
///  · 자동 회복 : 마지막 피격 후 regenDelay가 지나면 초당 regenPerSecond만큼 회복.
///
/// 흉내낼 몬스터는 <see cref="configAddress"/>로 고른다 — 몬스터가 바뀌어도 코드 수정 없이 최신화된다.
/// </summary>
[RequireComponent(typeof(Collider))]
public class TrainingDummyMonster : MonsterBase
{
    // ── [SerializeField] ────────────────────────────────────────────
    [Header("흉내낼 몬스터")]
    [Tooltip("이 허수아비가 사용할 몬스터 Config의 Addressable 키(예: Orc/OrcConfig). " +
             "스탯·애니메이터·상태가 이 몬스터 기준이 된다.")]
    [SerializeField] private string configAddress = "Orc/OrcConfig";

    [Tooltip("차트 데이터 주소. 비우면 Config 값만 사용(대부분 비워둔다).")]
    [SerializeField] private string dataAddress = string.Empty;

    [Header("허수아비 동작")]
    [Tooltip("마지막 피격 후 이 시간(초)이 지나면 회복을 시작한다.")]
    [SerializeField, Min(0f)] private float regenDelay = 3f;

    [Tooltip("초당 회복량. 0이면 회복하지 않는다.")]
    [SerializeField, Min(0f)] private float regenPerSecond = 500f;

    [Tooltip("끄면 넉백·이동을 허용한다. 기본은 제자리 고정.")]
    [SerializeField] private bool lockPosition = true;

    // ── 상수 ────────────────────────────────────────────────────────
    private const float DpsWindow          = 3f;   // DPS 집계 구간(초)
    private const float AnalysisResetIdle  = 4f;   // 이 시간 이상 무타격이면 다음 타격에 집계 리셋(교전 단위)
    private const float LabelRefreshInterval = 0.1f;

    // ── Private ─────────────────────────────────────────────────────
    private Vector3 _anchorPos;
    private Quaternion _anchorRot;
    private bool _anchorCaptured;
    private NavMeshAgent _navAgent;
    private float _lastHitTime = -999f;
    private float _regenCarry;   // 정수 HP라 소수 회복분을 누적해 흘리지 않게 한다

    // 실시간 데미지 분석(교전 단위 집계)
    private readonly Queue<(float time, float damage)> _damageLog = new();
    private float _damageInWindow;
    private float _lastHit;
    private bool  _lastHitCrit;
    private float _maxHit;
    private int   _hitCount;
    private int   _critCount;
    private float _labelTimer;

    // HP 변화 추적 — 평타·시너지·DoT 등 <b>모든</b> 경로의 실제 적용 피해를 잡기 위해 프레임 델타로 측정한다.
    // (TakeDamage만 후킹하면 TakeSynergyDamage·화상 틱이 집계에서 빠진다.)
    private int  _lastKnownHp = -1;
    private bool _pendingCrit;

    // ── MonsterBase 필수 구현 ───────────────────────────────────────
    protected override string ConfigAddress => configAddress;
    protected override string DataAddress   => dataAddress;

    // ── Lifecycle ───────────────────────────────────────────────────

    /// <summary>
    /// 고정 기준점은 <b>여기서</b> 잡는다. OnInitialized는 config 비동기 로드가 끝난 뒤라
    /// 그 사이 NavMeshAgent 스냅·중력으로 이미 밀린 위치를 기준으로 삼아 배치가 어긋났다.
    /// (MonsterBase.Awake는 private async라 서브클래스에서 가로채면 초기화가 깨진다 — OnEnable이 가장 이른 안전 훅.)
    /// </summary>
    protected override void OnEnable()
    {
        base.OnEnable();

        if (!_anchorCaptured)
        {
            _anchorPos      = transform.position;
            _anchorRot      = transform.rotation;
            _anchorCaptured = true;
        }
    }

    /// <summary>
    /// 허수아비는 <b>AI를 돌리지 않는다</b>. 기반 구현을 부르지 않고 직접 등록해
    /// 순찰·추격·공격을 전부 무동작 상태로 대체하고, config의 stateOverrides(PatternAttackOverrideSO 등)도
    /// 붙이지 않는다. 기반을 그대로 쓰면 더미가 플레이어를 쫓아와 때리고,
    /// 고정된 Agent가 NavMesh 밖이라 ResetPath에서 예외 로그가 매 프레임 쏟아진다.
    ///
    /// 피격·사망 상태는 <b>진짜 구현을 그대로</b> 등록한다 — 피격 반응과 처치 처리는 실제 몬스터와 같아야 한다.
    /// (MonsterBase.Update의 상태이상 틱·디버프 UI는 FSM보다 앞에서 돌므로 이 교체와 무관하게 정상 동작한다.)
    /// </summary>
    protected override void RegisterStates()
    {
        var idle = new DummyIdleState();

        _fsm.RegisterAs<PatrolState>     (idle);
        _fsm.RegisterAs<ChaseState>      (idle);
        _fsm.RegisterAs<AttackReadyState>(idle);
        _fsm.RegisterAs<AttackState>     (idle);

        _fsm.RegisterAs<GetHitState>(new GetHitState());
        _fsm.RegisterAs<DieState>   (new DieState());
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();

        // 처치 불가 — MonsterBase가 HP를 1 미만으로 내리지 않는다.
        HpFloorMin1 = true;

        if (lockPosition) FreezeMovement();
    }

    protected override void Update()
    {
        // 기반 Update를 그대로 태운다 — 상태이상 틱·피격 반응·디버프 UI가 실제 몬스터와 동일하게 동작해야 한다.
        base.Update();

        TrackDamage();   // 회복보다 먼저 — 회복이 감소분을 덮으면 피해가 집계에서 사라진다
        Regenerate();
        RefreshAnalysisLabel();
    }

    /// <summary>이동 제어(넉백·Agent·애니메이션 루트모션)가 모두 끝난 뒤 되돌려야 확실히 고정된다.</summary>
    private void LateUpdate()
    {
        if (!lockPosition || !_anchorCaptured) return;
        transform.SetPositionAndRotation(_anchorPos, _anchorRot);
    }

    // ── Private Methods ─────────────────────────────────────────────

    /// <summary>NavMeshAgent·Rigidbody가 위치를 흔들지 않도록 정지시킨다.</summary>
    private void FreezeMovement()
    {
        if (_navAgent == null) TryGetComponent(out _navAgent);
        if (_navAgent != null && _navAgent.isActiveAndEnabled && _navAgent.isOnNavMesh)
        {
            _navAgent.isStopped = true;
            _navAgent.velocity  = Vector3.zero;
        }

        if (TryGetComponent<Rigidbody>(out var rb))
        {
            rb.linearVelocity  = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
    }

    /// <summary>이번 프레임 HP 감소분을 실제 적용 피해로 집계한다(방어력·배율이 모두 반영된 값).</summary>
    private void TrackDamage()
    {
        if (_runtime == null) return;

        int hp = CurrentHp;
        if (_lastKnownHp < 0) { _lastKnownHp = hp; return; }

        int drop = _lastKnownHp - hp;
        _lastKnownHp = hp;
        if (drop <= 0) return;

        float now = Time.time;
        // 오래 쉬었다가 다시 때리면 새 교전으로 보고 리셋 → 매 세션 깨끗한 분석.
        if (now - _lastHitTime > AnalysisResetIdle) ResetAnalysis();
        _lastHitTime = now;
        _regenCarry  = 0f;

        _lastHit     = drop;
        _lastHitCrit = _pendingCrit;
        _pendingCrit = false;
        if (drop > _maxHit) _maxHit = drop;
        _hitCount++;
        if (_lastHitCrit) _critCount++;

        _damageLog.Enqueue((now, drop));
        _damageInWindow += drop;
    }

    private void PruneDpsWindow()
    {
        float now = Time.time;
        while (_damageLog.Count > 0 && now - _damageLog.Peek().time > DpsWindow)
            _damageInWindow -= _damageLog.Dequeue().damage;
    }

    private void ResetAnalysis()
    {
        _lastHit = 0f; _maxHit = 0f;
        _hitCount = 0; _critCount = 0;
        _damageLog.Clear(); _damageInWindow = 0f;
    }

    /// <summary>HP바 서브라벨에 DPS(3초)/최근타(크리)/최대타/타수·크리율을 표기. 문자열 조립은 저주기로만.</summary>
    private void RefreshAnalysisLabel()
    {
        PruneDpsWindow();

        _labelTimer -= Time.deltaTime;
        if (_labelTimer > 0f) return;
        _labelTimer = LabelRefreshInterval;

        var bar = HpBar;
        if (bar == null) return;

        if (_hitCount == 0) { bar.SetSubLabel(string.Empty); return; }

        float dps      = _damageInWindow / DpsWindow;
        float critRate = 100f * _critCount / _hitCount;

        bar.SetSubLabel(
            $"DPS {dps:F0}\n" +
            $"최근 {_lastHit:F0}{(_lastHitCrit ? " CRIT" : string.Empty)}\n" +
            $"최대 {_maxHit:F0}\n" +
            $"타수 {_hitCount}  크리 {critRate:F0}%");
    }

    private void Regenerate()
    {
        if (regenPerSecond <= 0f || _runtime == null || _runtime.IsDead) return;
        if (Time.time - _lastHitTime < regenDelay) return;

        int max = EffectiveMaxHp;
        if (max <= 0 || _runtime.CurrentHp >= max) { _regenCarry = 0f; return; }

        _regenCarry += regenPerSecond * Time.deltaTime;
        int heal = Mathf.FloorToInt(_regenCarry);
        if (heal <= 0) return;

        _regenCarry -= heal;
        _runtime.CurrentHp = Mathf.Min(max, _runtime.CurrentHp + heal);
    }

    // ── Event Handlers ──────────────────────────────────────────────

    /// <summary>크리 여부는 이 경로로만 전달되므로 여기서 받아 두고, 실제 피해량은 TrackDamage가 HP 델타로 집계한다.</summary>
    public override void TakeDamage(float amount, GameObject instigator, float knockbackMultiplier = 1f, bool isCrit = false)
    {
        if (isCrit) _pendingCrit = true;
        base.TakeDamage(amount, instigator, knockbackMultiplier, isCrit);
    }

    /// <summary>피격 시 고정 유지. 상태 전환 자체는 기반 로직에 맡긴다(피격 반응을 실제 몬스터와 동일하게).</summary>
    protected override void OnDamageTaken()
    {
        base.OnDamageTaken();

        if (lockPosition) FreezeMovement();
    }
}

/// <summary>허수아비 전용 무동작 상태 — 순찰·추격·공격 자리를 대신한다. 아무것도 하지 않는다.</summary>
internal sealed class DummyIdleState : IMonsterState
{
    public void Enter (MonsterContext ctx) { }
    public void Update(MonsterContext ctx) { }
    public void Exit  (MonsterContext ctx) { }
}
}

using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// FSM 방식 몬스터 공통 추상 베이스.
///
/// 기존 BatFSMController / SlimeFSMController의 중복 코드를 통합한 버전.
/// 새 몬스터는 이 클래스를 상속하고 아래 3가지만 구현하면 된다.
///   1. Type       → Define.MonsterType 반환
///   2. MonsterId  → Define.GetMonsterId(Type)
///   3. InitializeFSM() → RegisterState 호출로 상태 등록
///
/// [개선된 점]
///   - FSM 보일러플레이트(Dictionary, RequestStateChange, HandleAI) 중복 제거
///   - 등록되지 않은 상태 전환 시 Warning 로그
///   - StateUpdate() 반환값 무시 문제 해소 (상태 전환은 RequestStateChange 단일 경로로 통일)
/// </summary>
public abstract class LeeMonsterFSMBase : MonsterController, IMonsterStateChanger, IDamageable
{
    private readonly Dictionary<MonsterState, IMonsterState> _states = new();
    private MonsterState _currentStateKey;
    private IMonsterState _currentState;

    [SerializeField]
    private MonsterState _debugState;

    // ── HP ────────────────────────────────────────────────────────────
    public float CurrentHp  { get; private set; }
    public float MaxHp      => MyStat != null ? MyStat.max_hp : 1f;
    private bool _isDead;

    // ── 애니메이션 State 이름 (Animator Controller와 일치해야 함) ─────────
    // 파생 클래스에서 override하여 해당 몬스터의 Animator Controller 이름에 맞게 설정한다.
    public virtual string AnimIdle        => "MoveBlend";
    public virtual string AnimMove        => "MoveBlend";
    public virtual string AnimAttackReady => "AttackReady";
    public virtual string AnimAttack      => "Attack";
    public virtual string AnimGetHit      => "GetHit";
    public virtual string AnimDie         => "Die";

    public IMonsterState CurrentState => _currentState;

    // ── 초기화 ────────────────────────────────────────────────────────

    protected override async UniTask InitAsync()
    {
        await base.InitAsync();
        _isDead   = false;
        CurrentHp = MaxHp;
        EnsureCollider();
        InitializeFSM();
        RequestStateChange(MonsterState.Idle);
    }

    /// <summary>
    /// Collider가 없으면 CapsuleCollider를 자동으로 추가한다.
    /// 플레이어 무기 레이캐스트가 히트하려면 루트에 Collider가 필요하다.
    /// Inspector에서 직접 추가한 경우에는 호출되지 않는다.
    /// </summary>
    protected virtual void EnsureCollider()
    {
        if (GetComponent<Collider>() != null) return;

        var col    = gameObject.AddComponent<CapsuleCollider>();
        col.height = 1.5f;
        col.radius = 0.5f;
        col.center = new Vector3(0f, 0.75f, 0f);
    }

    // ── IDamageable ───────────────────────────────────────────────────

    public void TakeDamage(float amount, UnityEngine.GameObject instigator, float knockbackMultiplier = 1f)
    {
        if (_isDead) return;

        CurrentHp -= amount;
        Debug.Log($"[{name}] 피해 {amount} 받음. 남은 HP: {CurrentHp}/{MaxHp}");

        if (CurrentHp <= 0f)
        {
            _isDead = true;
            CurrentHp = 0f;
            RequestStateChange(MonsterState.Die);
            return;
        }

        // 공격/대기 중에 피해를 받으면 피격 애니메이션 재생
        if (_currentStateKey != MonsterState.GetHit)
        {
            RequestStateChange(MonsterState.GetHit);
        }
    }

    /// <summary>
    /// 상속 클래스에서 RegisterState를 호출해 사용할 상태들을 등록한다.
    /// InitAsync 완료 후 자동 호출된다.
    /// </summary>
    protected abstract void InitializeFSM();

    /// <summary>
    /// 상태를 딕셔너리에 등록하고 Init을 실행한다.
    /// InitializeFSM() 내부에서만 호출한다.
    /// </summary>
    protected void RegisterState(MonsterState key, IMonsterState state)
    {
        state.Init(this, this);
        _states[key] = state;
    }

    // ── IMonsterStateChanger ──────────────────────────────────────────

    public void RequestStateChange(MonsterState newState)
    {
        if (_currentStateKey == newState) return;

        Debug.Log($"[{name}] {_currentStateKey} → {newState}  (t={Time.time:F2}s)");

        _currentState?.Exit();
        _currentStateKey = newState;
        _debugState      = newState;

        if (!_states.TryGetValue(newState, out _currentState))
        {
            Debug.LogWarning($"[{GetType().Name}] 등록되지 않은 상태: {newState}. RegisterState 확인 필요.");
            return;
        }

        _currentState.Enter();
    }

    // ── MonsterController 추상 구현 ───────────────────────────────────

    public override void HandleAI()
    {
        _currentState?.StateUpdate();
    }
}

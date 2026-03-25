using System;
using System.Collections.Generic;
using UnityEngine;


namespace Abyss.Monster
{
/// <summary>
/// Type 키 기반 FSM.
///
/// ━━ 공용 상태 ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
///  RegisterStates() 에서 Register(instance) 로 등록.
///  ChangeState{T}() 로 타입 조회 후 전환 → 외부(TakeDamage 등)에서 사용.
///
/// ━━ 특수 상태 ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
///  몬스터가 인스턴스를 직접 보유 → ChangeState(instance) 로 전환.
///  딕셔너리 등록 없이 직접 참조만으로 동작.
///  SpecialStateBase 상속 시 Constraints 가 자동 바인딩됨.
/// </summary>
public class MonsterFSM
{
    private readonly Dictionary<Type, IMonsterState> _states = new();
    private IMonsterState   _current;
    private readonly MonsterContext _ctx;

    /// <summary>현재 상태 타입 (디버그용).</summary>
    public Type CurrentType { get; private set; }

    /// <summary>
    /// 현재 특수 상태의 제약 플래그.
    /// 공용 상태이거나 특수 상태가 아닐 경우 None 반환.
    /// TakeDamage / 이동 제어 등에서 중앙 처리에 사용.
    /// </summary>
    public SpecialStateConstraint CurrentConstraints
        => (_current as SpecialStateBase)?.Constraints ?? SpecialStateConstraint.None;

    public MonsterFSM(MonsterContext ctx) { _ctx = ctx; }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 등록
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    /// <summary>공용 상태 등록. 타입이 키가 된다.</summary>
    public void Register<T>(T state) where T : IMonsterState
        => _states[typeof(T)] = state;

    /// <summary>
    /// 오버라이드 등록 — TKey 타입으로 조회되지만 실제 인스턴스는 파생 클래스.
    /// 파생 몬스터에서 공용 상태를 교체할 때 사용.
    /// </summary>
    public void RegisterAs<TKey>(IMonsterState state) where TKey : IMonsterState
        => _states[typeof(TKey)] = state;

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 상태 전환
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    /// <summary>공용 상태 전환 — 타입으로 조회. 외부에서 사용.</summary>
    public void ChangeState<T>() where T : IMonsterState
    {
        if (!_states.TryGetValue(typeof(T), out var next))
        {
            Debug.LogError($"[MonsterFSM] 등록되지 않은 상태: {typeof(T).Name}");
            return;
        }
        Transition(next);
    }

    /// <summary>특수 상태 전환 — 인스턴스 직접 전달. 몬스터 내부에서 사용.</summary>
    public void ChangeState(IMonsterState next)
    {
        if (next == null) return;
        Transition(next);
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 업데이트
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    public void Update() => _current?.Update(_ctx);

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 내부
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private void Transition(IMonsterState next)
    {
        if (_current == next) return;
        _current?.Exit(_ctx);
        _current    = next;
        CurrentType = next.GetType();
        _current.Enter(_ctx);
    }
}
}

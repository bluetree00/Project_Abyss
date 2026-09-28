using System;
using System.Collections.Generic;

/// <summary>
/// 단일 레이어 상태머신
/// </summary>
public sealed class LayerStateMachine<TId> where TId : struct, Enum
{
    private readonly Dictionary<TId, ILayerState<TId>> _map = new();
    private ILayerState<TId> _current;
    private readonly PlayerController _controller;
    private readonly Changer _changer;

    public TId CurrentId { get; private set; }

    private sealed class Changer : ILayerStateChanger<TId>
    {
        private readonly LayerStateMachine<TId> _sm;
        public Changer(LayerStateMachine<TId> sm) => _sm = sm;
        public void Change(TId next) => _sm.Change(next);
    }

    public LayerStateMachine(PlayerController controller)
    {
        _controller = controller;
        _changer = new Changer(this);
    }

    public void Register(TId id, ILayerState<TId> state)
    {
        _map[id] = state;
        state.Init(_controller, _changer);
    }

    /// <summary>
    /// 상태 변경
    /// 동일 상태 전이는 무시 (Enter 재호출 방지)
    /// </summary>
    public void Change(TId next)
    {
        // 동일 상태는 Enter 재호출 방지 — 단 아직 아무 상태도 없으면 첫 전환은 받는다.
        // CurrentId는 enum 기본값(Idle/None)으로 시작해, 첫 Change(Idle)가 '같은 상태'로 무시되면
        // 상태 객체가 비어 Update가 돌지 않았다(땅에 닿은 채 스폰되면 이동 입력이 안 먹는다).
        if (_current != null && CurrentId.Equals(next)) return;
        if (!_map.TryGetValue(next, out var s)) return;

        _current?.Exit();
        _current = s;
        CurrentId = next;
        _current.Enter();
    }

    public void Update() => _current?.Update();
}

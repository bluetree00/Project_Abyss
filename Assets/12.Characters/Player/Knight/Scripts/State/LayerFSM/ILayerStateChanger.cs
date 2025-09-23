// /Scripts/FSM/LayerStateMachine.cs
using System;
using System.Collections.Generic;

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

    public void Change(TId next)
    {
        if (!_map.TryGetValue(next, out var s)) return;
        _current?.Exit();
        _current = s;
        CurrentId = next;
        _current.Enter();
    }

    public void Update() => _current?.Update();
}

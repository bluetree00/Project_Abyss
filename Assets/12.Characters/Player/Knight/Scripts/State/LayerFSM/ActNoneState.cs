public class ActNoneState : ILayerState<ActState> //act 상태의 idle의 역활을 수행해야함 
{
    private PlayerController _controller;
    private ILayerStateChanger<ActState> _stateChanger;

    public void Init(PlayerController controller, ILayerStateChanger<ActState> stateChanger)
    {
        _controller = controller;
        _stateChanger = stateChanger;
    }

    public void Enter()
    {

        // 1) 공격 관련 플래그 초기화
        _controller.isAttacking = false;

        // 2) Locomotion FSM을 Idle로 변경(이미 Idle이면 내부에서 무시될 것)
        if (_controller.LocoSM.CurrentId != LocoState.Idle)
            _controller.LocoSM.Change(LocoState.Idle);

        // 3) Animator를 LocoIdleState가 기대하는 상태로 강제 정렬
        var anim = _controller.Anim;
        if (anim != null)
        {
            int layer = 0;

            // MoveBlend로 덮어쓰기 — LocoIdleState.Enter와 일치
            // CrossFade를 사용하면 transition 문제를 어느 정도 덮어쓸 수 있음.
            const string moveBlendStateName = "MoveBlend";
            if (!anim.IsInTransition(layer))
            {
                anim.CrossFade(moveBlendStateName, 0.08f, layer, 0f);
            }
            else
            {
                // 이미 transition 중이라면 즉시 상태 값을 보정
                anim.Play(moveBlendStateName, layer, 0f);
            }

        }
    }

    public void Update() { }
    public void Exit() { }
}

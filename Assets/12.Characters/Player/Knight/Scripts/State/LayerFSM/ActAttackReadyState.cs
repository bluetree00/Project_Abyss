// ActAttackReadyState.cs
public class ActAttackReadyState : ILayerState<ActState>
{
    private PlayerController _controller;
    private ILayerStateChanger<ActState> _stateChanger;

    public void Init(PlayerController controller, ILayerStateChanger<ActState> stateChanger)
    { _controller = controller; _stateChanger = stateChanger; }

    public void Enter()
    {
        // 컨텍스트별(지상/공중) 애니 세트 교체
        var isAir = !_controller.IsGrounded();


        // 이동 제어(풀보디 공격 기준; 상체공격이면 0.2f 같은 감속으로)
        _controller.AcquireMoveLock();
        _controller.SetMoveScale(0f);

        // 현재 콤보 스텝으로 첫 타 실행
        int step = _controller.currentComboStep;
        _controller.Anim.CrossFade($"NormalAttack_{step + 1}", 0.05f);

        // 어빌리티 호출(히트박스/이펙트 스폰 등은 애니 이벤트에 배치 권장)
        // _controller.LightAttackAbility?.LightAttack(_controller, step);

        // 즉시 진행 상태로
        _stateChanger.Change(ActState.Attack);
    }

    public void Update() { }
    public void Exit() { }
}

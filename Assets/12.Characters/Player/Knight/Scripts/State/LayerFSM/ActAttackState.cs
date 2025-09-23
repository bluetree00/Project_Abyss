// ActAttackState.cs
using Game.Inputs;

public class ActAttackState : ILayerState<ActState>
{
    private PlayerController _controller;
    private ILayerStateChanger<ActState> _stateChanger;

    public void Init(PlayerController controller, ILayerStateChanger<ActState> stateChanger)
    { _controller = controller; _stateChanger = stateChanger; }

    public void Enter()
    {
        _controller.isAttacking = true;
        _controller.nextComboQueued = false;
        _controller.comboWindowOpen = false;
    }

    public void Update()
    {
        // 콤보 창이 열려 있을 때만 라이트 입력을 소비해 다음 타 예약
        if (_controller.comboWindowOpen && _controller.InputBuffer.TryConsume(Command.Light))
            _controller.nextComboQueued = true;
    }

    public void Exit()
    {
        _controller.isAttacking = false;
        _controller.comboWindowOpen = false;
        _controller.nextComboQueued = false;
        _controller.ReleaseMoveLock();
        _controller.SetMoveScale(1f);
    }

    // ===== 애니메이션 이벤트 전달용 메서드 =====
    // PlayerController 쪽에서 현재 상태를 캐스팅하여 호출하면 됨.

    public void AE_OpenCombo()  => _controller.OpenComboWindow();
    public void AE_CloseCombo() => _controller.CloseComboWindow();

    public void AE_AttackEnd()
    {
        if (_controller.nextComboQueued)
        {
            _controller.nextComboQueued = false;
            _controller.currentComboStep++;

            // var max = _controller.weaponManagerSO.CurrentWeapon?.lightComboCount ?? 1;
            // if (_controller.currentComboStep >= max)
            //     _controller.currentComboStep = 0;

            _stateChanger.Change(ActState.AttackReady); // 다음 타
        }
        else
        {
            _controller.currentComboStep = 0;
            _stateChanger.Change(ActState.None);        // 종료
        }
    }
}

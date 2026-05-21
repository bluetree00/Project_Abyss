/// <summary>
/// 콤보 상태를 관리하는 클래스.
/// PlayerController가 직접 콤보 필드를 소유하지 않도록 분리.
/// </summary>
public class ComboController
{
    public bool IsAttacking      { get; private set; }
    public bool ComboWindowOpen  { get; private set; }
    public int  CurrentComboStep { get; private set; }
    public bool NextComboQueued  { get; private set; }

    /// <summary>콤보 창 유지 시간 (초). WeaponData 로드 후 설정.</summary>
    public float ResetTime { get; set; } = 2f;

    private float _timeRemaining;

    // ---------------------------
    // Mutation API
    // ---------------------------
    public void SetAttacking(bool value)       { IsAttacking = value; }
    public void SetNextComboQueued(bool value) { NextComboQueued = value; }
    public void IncrementStep()                { CurrentComboStep++; }
    public void ResetStep()                    { CurrentComboStep = 0; }

    public void OpenWindow()
    {
        ComboWindowOpen  = true;
        _timeRemaining   = ResetTime;
    }

    public void CloseWindow()
    {
        ComboWindowOpen = false;
        _timeRemaining  = 0f;
    }

    /// <summary>
    /// 매 프레임 호출. 콤보 창 타임아웃을 처리한다.
    /// 타임아웃 시 Reset()을 호출하지 않고 창만 닫는다 —
    /// 상태 초기화는 각 State의 Exit()에서 담당.
    /// </summary>
    public void Tick(float dt)
    {
        if (!ComboWindowOpen) return;
        _timeRemaining -= dt;
        if (_timeRemaining <= 0f)
            CloseWindow();
    }

    /// <summary>공격 종료 / 상태 전환 시 전체 초기화.</summary>
    public void Reset()
    {
        IsAttacking     = false;
        ComboWindowOpen = false;
        NextComboQueued = false;
        CurrentComboStep = 0;
        _timeRemaining  = 0f;
    }
}

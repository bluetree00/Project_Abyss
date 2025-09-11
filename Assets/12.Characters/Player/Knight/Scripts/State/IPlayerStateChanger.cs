
/// <summary>
    ///상태 변경용 인터페이스
/// </summary>
public interface IPlayerStateChanger 
{
    void RequestStateChange(PlayerController.PlayerState newState);
}
/// <summary>
/// Wisp가 F키로 상호작용할 수 있는 오브젝트 인터페이스.
/// CharacterDisplayStand가 구현한다. (WeaponDisplayStand는 PlayerController 자동 트리거 방식으로 전환됨)
/// </summary>
public interface IWispInteractable
{
    void TrySelect(WispController wisp);
    void ClearInteraction(WispController wisp);
}

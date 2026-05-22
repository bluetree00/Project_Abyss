/// <summary>
/// 기본 회피 어빌리티.
/// 이동/쿨다운 처리는 LocoDodgeState가 담당하므로 이 클래스는 확장 훅용으로만 남깁니다.
/// </summary>
public class DefaultDodgeAbility : IDodgeAbility<PlayerController>
{
    public void Dodge(PlayerController controller) { }
}

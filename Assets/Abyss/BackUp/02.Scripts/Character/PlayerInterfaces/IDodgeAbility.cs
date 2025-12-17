public interface IDodgeAbility<T> where T : PlayerController
{
    void Dodge(PlayerController controller);
}

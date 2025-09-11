public interface IDodgeAbility<T> where T : PlayerCharacter
{
    void Dodge(PlayerCharacter controller);
}

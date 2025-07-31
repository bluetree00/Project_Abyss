public interface IJumpAbility<T> where T : PlayerCharacter
{
    void Jump(T controller);
}

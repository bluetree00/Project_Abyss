public interface IJumpAbility<T> where T : CharacterBase
{
    void Jump(T controller);
}

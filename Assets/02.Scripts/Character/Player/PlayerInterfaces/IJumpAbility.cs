public interface IJumpAbility<T> where T : CharacterController
{
    void Jump(T controller);
}

public interface IJumpAbility<T> where T : PlayerController
{
    void Jump(T controller);
}

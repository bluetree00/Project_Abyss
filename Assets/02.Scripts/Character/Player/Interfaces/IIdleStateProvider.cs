using Game.CharacterStates;

namespace Game.Interfaces
{
    public interface IIdleStateProvider<T> where T : CharacterController
    {
        State<T> GetIdleState();
    }
}

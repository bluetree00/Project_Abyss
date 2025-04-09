using Game.CharacterStates;
using Game.CharacterStates.StateMachine;

namespace Game.Interfaces
{
    
    public interface IDodgeProvider<T> where T : CharacterController
    {
        StateMachine<T> CreateDodgeStateMachine(T owner);
    }

}

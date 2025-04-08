using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Game.CharacterStates;

public interface IComboAttackProvider
{
    StateMachine<CharacterController> CreateAttackStateMachine(CharacterController owner);
}
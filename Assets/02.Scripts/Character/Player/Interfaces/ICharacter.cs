using System.Collections;
using System.Collections.Generic;
using Game.CharacterStates;
using UnityEngine;

public interface ICharacter
{
    CharacterData CharacterData { get; }
    WeaponManagerSO weaponManagerSO { get; }
    StateMachine<CharacterController> StateMachine { get; }
}

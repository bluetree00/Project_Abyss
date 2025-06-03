using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class JumpAbilitySO : ScriptableObject , IJumpAbility<CharacterController>
{
    public abstract void Jump(CharacterController controller);
}

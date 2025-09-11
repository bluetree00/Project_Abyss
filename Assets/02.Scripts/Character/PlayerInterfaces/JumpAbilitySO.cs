using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class JumpAbilitySO : ScriptableObject , IJumpAbility<PlayerCharacter>
{
    public abstract void Jump(PlayerCharacter controller);
}

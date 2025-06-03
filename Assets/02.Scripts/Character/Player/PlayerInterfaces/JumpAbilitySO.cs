using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class JumpAbilitySO : ScriptableObject , IJumpAbility<CharacterBase>
{
    public abstract void Jump(CharacterBase controller);
}

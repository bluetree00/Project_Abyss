using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class JumpAbilitySO : ScriptableObject , IJumpAbility<PlayerController>
{
    public abstract void Jump(PlayerController controller);
}

using UnityEngine;

[CreateAssetMenu(menuName = "Monster/Abilities/Detect")]
public class DetectAbilitySO : MonsterAbilitySO
{
    
    [SerializeField] private float range;

    public void SetRange(float r)
    {
        range = r;
    }

    public override IMonsterAbility CreateAbilityInstance()
    {
        return new DetectAbility(range);
    }


}

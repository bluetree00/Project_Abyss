using UnityEngine;

[CreateAssetMenu(fileName = "HealthAugment", menuName = "Augments/HealthAugment")]
public class HealthAugment : AugmentData
{
    public int healthBonus;

    public HealthAugment()
    {
        Name = "Health Augment";
        Description = "Increases the target's health.";
        Grade = AugmentGrade.Common;
        healthBonus = 50;  // Example health bonus
    }

    public override void UseAugment(GameObject target)
    {
        //필요한 로직 사용
       Debug.Log("증강 효과 발동!");

       

    }
}

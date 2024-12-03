using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "NewAugment", menuName = "Augments/AugmentData")]
public class AugmentData : ScriptableObject
{
    public string Name;
    public Sprite Icon;
    public string Description;
    public AugmentGrade Grade; // 일반, 레어, 유니크 등급

    public enum AugmentGrade
    {
        Common,    // 80%
        Rare,      // 15%
        Unique     // 5%
    }

    public virtual void Use()
    {
        Debug.Log($"{Name} 증강을 사용했습니다!");
    }
}

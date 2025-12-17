using UnityEngine;

public abstract class AugmentData : ScriptableObject
{
    public string Name;          // 증강 이름
    public Sprite Icon;          // 아이콘 이미지
    public string Description;   // 설명
    public AugmentGrade Grade;   // 등급 (Common, Rare, Unique 등)

    public enum AugmentGrade
    {
        Common,
        Rare,
        Unique
    }

    /// <summary>
    /// 증강 사용 시 호출되는 메서드. 개별 증강마다 구현 필요.
    /// </summary>
    public abstract void UseAugment(GameObject target);
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "NewItem", menuName = "Items/Item")]
public class ScriptableItemData : ScriptableObject
{
    public string Name;
    public Sprite Icon;

    public virtual void Use()
    {
        Debug.Log($"{Name}을(를) 사용했습니다.");
    }
}
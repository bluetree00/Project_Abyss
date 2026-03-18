using UnityEngine;

public abstract class SkillData : ScriptableObject
{
    public string skillName;
    [TextArea] public string skillDescription;
    public Sprite icon;
    public float cooldown;

    public abstract void Activate(GameObject user);
}

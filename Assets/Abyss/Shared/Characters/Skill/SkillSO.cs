using UnityEngine;

/// <summary>
/// 스킬 정의 SO — UI 표시 + 쿨다운 정보
/// 추후 ability(실행 데이터), 애니메이션 매핑 추가 예정
/// </summary>
[CreateAssetMenu(fileName = "NewSkill", menuName = "Game/SkillSO")]
public class SkillSO : ScriptableObject
{
    [Header("기본 정보")]
    public string skillName;
    [TextArea] public string description;
    public Sprite icon;

    [Header("쿨다운")]
    public float cooldown = 5f;
}

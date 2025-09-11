using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(menuName = "Weapon/Animation/LightAttackAnimationSet")]
public class LightAttackAnimationSetSO : ScriptableObject
{
    [Header("최대 공격 수")]
    public int maxAttackCount = 3;

    [Header("콤보 입력 초기화 시간")]
    public float comboResetTime = 1.5f;
    
    [Header("무기 기본 공격 애니메이션")]
    public List<AnimationClip> attackAnimations;

    [Header("각 기본공격이 끝나는 타이밍")]
    public List<float> comboEndTimes;

    [Header("변경할 기존 노말 공격 애니메이션 이름")]
    public string[] normalAttackAnimations;

    
}

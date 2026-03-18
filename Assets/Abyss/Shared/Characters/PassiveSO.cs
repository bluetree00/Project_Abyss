using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 캐릭터 고유 패시브 정의 ScriptableObject
/// - CharacterData.passive 필드로 참조
/// - 초기 스탯 보정값(기본 패시브)을 StatModifier 목록으로 표현
/// - 향후 특수 효과(OnKill, OnHit 등)는 하위 클래스로 확장
/// </summary>
[CreateAssetMenu(fileName = "NewPassive", menuName = "Characters/Passive")]
public class PassiveSO : ScriptableObject
{
    [Header("패시브 기본 정보")]
    public string passiveName;
    [TextArea] public string description;

    [Header("기본 스탯 보정 (런 시작 시 1회 적용)")]
    public List<StatModifier> baseModifiers = new List<StatModifier>();
    // 예시:
    //   AttackPower +10  (전사 패시브: 기본 공격력 강화)
    //   MaxHp       -20  (유리대포 캐릭터: HP 패널티)
}

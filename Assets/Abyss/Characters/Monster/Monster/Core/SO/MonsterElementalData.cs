using System;
using Abyss.Monster;
using UnityEngine;

/// <summary>
/// 원소 속성 하나의 설정 데이터.
/// resistance / overrideState 는 원소별로 개별 설정.
/// </summary>
[Serializable]
public class ElementEntry
{
    [Range(0f, 3f)]
    [Tooltip("이 원소 피해에 대한 저항/약점 배율.\n0.0 = 면역  /  0.5 = 반감  /  1.0 = 보통  /  2.0 = 약점")]
    public float resistance = 1f;

    [Tooltip("null = 공통 원소 상태(DefaultElementalState) 사용.\n" +
             "지정 시 해당 SO 가 생성하는 커스텀 상태로 교체.\n" +
             "예: 불 면역 몬스터 → FireImmuneStateSO 할당")]
    public ElementalStateSO overrideState;
}

/// <summary>
/// MonsterConfigSO 에 인라인으로 포함되는 원소 설정 블록.
/// 누적치 임계값은 전체 원소 공통 1개 값 사용.
/// 5종 원소(번개·물·불·풀·땅) 각각의 저항·오버라이드 상태를 보유한다.
/// </summary>
[Serializable]
public class MonsterElementalData
{
    [Tooltip("전체 원소 공통 누적치 임계값. 이 값 이상 누적되면 원소 효과 발동.")]
    public float accumulationThreshold = 100f;

    public ElementEntry lightning = new();
    public ElementEntry water     = new();
    public ElementEntry fire      = new();
    public ElementEntry grass     = new();
    public ElementEntry earth     = new();

    /// <summary>ElementType 으로 해당 엔트리를 가져온다.</summary>
    public ElementEntry Get(ElementType type) => type switch
    {
        ElementType.Lightning => lightning,
        ElementType.Water     => water,
        ElementType.Fire      => fire,
        ElementType.Grass     => grass,
        ElementType.Earth     => earth,
        _                     => null,
    };
}

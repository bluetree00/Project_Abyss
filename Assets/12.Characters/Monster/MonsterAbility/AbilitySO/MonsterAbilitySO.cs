using UnityEngine;

public abstract class MonsterAbilitySO : ScriptableObject //NOTE추후 레벨 디자인 과정에서 몬스터 기능 수치를 조절할때 기존 수치 + 제이슨 값으로 넣어 줘야함.
{                                                         //NOTE 스테이지 기반 난이도 증가는 스테이지 클리어 기록에 접근해서 클리어 마다 +를 주는 방식을 사용.
    public abstract Define.MonsterAbilityType MonsterAbilityType { get; }

    // 능력 인스턴스 생성 추상 메서드
    public abstract IMonsterAbility ReturnAbilityInstance();
}

using UnityEngine;

namespace Abyss.Monster
{
/// <summary>
/// 몬스터 공용 상태 오버라이드 SO 추상 베이스.
///
/// ━━━ 설계 원칙 ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
///  • 이 SO 하나가 "어느 공용 상태를 교체할지" + "특수 상태 진입 조건/로직"을 모두 담는다.
///  • MonsterConfigSO.stateOverrides 리스트에 추가하면 자동 적용된다.
///  • 쿨다운 등 런타임 상태는 RegisterOverrides() 에서 생성한 인스턴스가 보유한다.
///    풀 재사용 시 리셋이 필요하면 monster.RegisterOnEnabledCallback() 을 이용한다.
/// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
///
/// 구현 방법:
///   1) 이 클래스를 상속한 ScriptableObject 작성
///   2) RegisterOverrides() 에서 fsm.RegisterAs<T>(new MyState(...)) 호출
///   3) .asset 파일 생성 후 MonsterConfigSO.stateOverrides 에 추가
/// </summary>
public abstract class MonsterStateOverrideSO : ScriptableObject
{
    /// <summary>
    /// MonsterBase.RegisterStates() 에서 호출.
    /// 이 SO가 담당하는 공용 상태 오버라이드를 FSM에 등록한다.
    /// 쿨다운 등 런타임 객체를 생성하고 monster.RegisterOnEnabledCallback() 으로 리셋 훅을 등록할 수 있다.
    /// </summary>
    public abstract void RegisterOverrides(MonsterFSM fsm, MonsterBase monster);
}
}

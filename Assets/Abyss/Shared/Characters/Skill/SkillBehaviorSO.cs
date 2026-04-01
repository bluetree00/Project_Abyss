using UnityEngine;

/// <summary>
/// 스킬 실행 로직의 추상 SO.
/// 무기 SO의 SkillSO.behavior 필드에 연결.
/// SO는 공유 데이터(Inspector), Runtime은 실행 시 가변 상태.
/// </summary>
public abstract class SkillBehaviorSO : ScriptableObject
{
    /// <summary>매 스킬 사용 시 새 Runtime 인스턴스 생성</summary>
    public abstract ISkillRuntime CreateRuntime();
}

/// <summary>
/// 스킬 실행 시 가변 상태를 관리하는 인터페이스.
/// ActSkillState가 Enter/Update/Exit 생명주기를 호출.
/// </summary>
public interface ISkillRuntime
{
    void OnEnter(SkillExecutionContext ctx);
    void OnUpdate(SkillExecutionContext ctx);
    void OnExit(SkillExecutionContext ctx);
}

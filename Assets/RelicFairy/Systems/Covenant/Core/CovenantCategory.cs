/// <summary>
/// 서약 카테고리 (서약 기획서 4분류). 선택 UI 태그/색·향후 추첨 가중치 메타데이터로 사용.
/// </summary>
public enum CovenantCategory
{
    /// <summary>행동 조건형 — 특정 행동(무기 교체·이동·연속 공격) 시 발동.</summary>
    ActionConditional,
    /// <summary>런 구조형 — 선택지·드롭·그리드 등 런 규칙 변경.</summary>
    RunStructure,
    /// <summary>전투 리듬형 — 공격 순서·쿨타임·스킬 타이밍 변경.</summary>
    CombatRhythm,
    /// <summary>트레이드오프형 — 무언가를 포기하고 구조적 이점 획득.</summary>
    Tradeoff,
}

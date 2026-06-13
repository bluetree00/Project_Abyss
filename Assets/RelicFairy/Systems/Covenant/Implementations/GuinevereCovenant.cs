/// <summary>
/// 기네비어의 서약 — 잘못된 선택을 되돌리는 힘 (런 구조형: 재편성)
///
/// 기획: 아이템·그리드·스킬 선택을 재선택(reroll)·재구성.
///
/// ⚠️ [임시 스텁] 아이템/그리드/스킬 선택 UI + 시너지 완성 후 구현 예정. 현재 무동작.
/// </summary>
public sealed class GuinevereCovenant : CovenantBase
{
    public override string CovenantId => CovenantFactory.Guinevere;
    public override CovenantCategory Category => CovenantCategory.RunStructure;

    public override string DisplayName         => "기네비어의 서약";
    public override string LoreText            => "기네비어 — 잘못된 선택을 되돌리는 힘을 서약으로 남겼다";
    public override string BasicDescription    => "런 중 선택(아이템·그리드·스킬)을 1회 재선택할 수 있다. (준비 중)";
    public override string EnhancedDescription => "재선택 횟수 증가 + 한 단계 높은 등급 구성. (준비 중)";
    public override string EvolvedDescription  => "재선택 횟수 추가 + 고른 아이템 복제 획득. (준비 중)";

    // TODO: 재선택 시스템 완성 후 IReselectionService 브리지로 reroll 구현.
}

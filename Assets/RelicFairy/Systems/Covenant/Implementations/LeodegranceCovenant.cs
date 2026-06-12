using UnityEngine;

/// <summary>
/// 레오데그란스의 서약 — 왕의 보급 (런 구조형)
///
/// 기획: 일정 주기마다 보급품(골드 또는 아이템) 낙하.
///
/// ⚠️ [임시 스텁] 아이템 드롭/인벤토리 구조 + 시너지 완성 후 구현 예정. 현재 무동작.
/// </summary>
public sealed class LeodegranceCovenant : CovenantBase
{
    public override string CovenantId => CovenantFactory.Leodegrance;
    public override CovenantCategory Category => CovenantCategory.RunStructure;

    public override string DisplayName         => "레오데그란스의 서약";
    public override string LoreText            => "레오데그란스 — 필요한 순간마다 보급품을 보내는 서약을 전달했다";
    public override string BasicDescription    => "일정 주기마다 골드 또는 아이템이 보급된다. (준비 중)";
    public override string EnhancedDescription => "보급 주기 단축 + 품질 상향. (준비 중)";
    public override string EvolvedDescription  => "보급 주기 추가 단축 + 보스방 직전 보급 보장. (준비 중)";

    // TODO: 아이템 구조 완성 후 ISupplyDropService 브리지로 주기 보급(골드/아이템) 구현.
}

/// <summary>
/// 니무에의 서약 — 가능성을 꺼내는 힘 (런 구조형: 그리드 특화)
///
/// 기획: 그리드 블록 추가 획득 + 속성 발동 임계값 인하.
///
/// ⚠️ [임시 스텁] 멀린 룬 그리드 시스템 + 시너지 완성 후 구현 예정. 현재 무동작.
/// </summary>
public sealed class NimueCovenant : CovenantBase
{
    public override string CovenantId => CovenantFactory.Nimue;
    public override CovenantCategory Category => CovenantCategory.RunStructure;

    public override string DisplayName         => "니무에의 서약";
    public override string LoreText            => "니무에 — 가능성을 꺼내는 힘을 서약으로 전달했다";
    public override string BasicDescription    => "그리드 블록 추가 획득 + 속성 발동 임계값 인하. (준비 중)";
    public override string EnhancedDescription => "블록 추가량 증가 + 임계값 추가 인하. (준비 중)";
    public override string EvolvedDescription  => "그리드가 채워질 때마다 랜덤 속성 채움률 상승. (준비 중)";

    // TODO: 룬 시스템 완성 후 IRuneCovenantBridge로 블록 부여·임계값 인하 구현.
}

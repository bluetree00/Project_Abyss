/// <summary>
/// 상점 「정비소」 서비스 종류 — 성장을 파는 게 아니라 <b>빌드를 다듬는</b> 행위를 판다.
/// 리서치(StS 카드제거 / Hades 교환 / Gungeon 독점)에 기반. 상세: 바탕화면 상점_정비소 기획서.
///
/// 구현 상태:
///   Impl  — 인프라 완비, 실동작
///   UiTodo— 코어 로직은 있으나 선택 UI/런플로우 연결이 남음(연결점 열어둠)
///   Stub  — 정제소 등 <b>미구현 시스템 의존</b>. 객체만 미리 만들어 두고 연결은 이후.
/// </summary>
public enum ShopServiceKind
{
    EssenceExchange,   // Impl   : 골드 → 심연의 정수(각성 메타재화). Hades 다크니스 판매.
    HpForGold,         // Impl   : HP 일부 → 골드. Hades 미다스. 보수적 환율.
    RuneExtraction,    // UiTodo : 보드에서 룬 1개 제거. StS 카드제거. 누진 가격. 선택 UI 필요.
    CovenantReforge,   // UiTodo : 서약 부품 재조립(기존 4개 상한 고려). UI_CovenantAssemble 재사용.
    RuneReroll,        // Stub   : 정제 리롤권. 정제소 구현 후 연결.
    ExclusiveRune,     // Stub   : 상점 전용 룬. 정제 확정 후 연결.
}

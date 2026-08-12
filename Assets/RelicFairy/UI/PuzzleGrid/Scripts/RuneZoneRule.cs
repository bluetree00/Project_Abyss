/// <summary>
/// 룬 속성 ↔ 판 속성존 배치 제약의 <b>단일 판정 지점</b>.
///
/// 멀린 룬판은 6속성 존(F/I/T/P/L/D)과 중앙(CENTER)으로 나뉘어 있는데, 예전에는 판정이 없어
/// 어떤 룬이든 아무 칸에나 놓을 수 있었다 — 속성 존이 사실상 색깔만 다른 칸이었다.
/// 룬은 <b>자기 속성 존</b>에만 놓인다.
///
/// 예외 두 가지는 의도적이다.
///  · <b>CENTER</b>: 자체 효과 없이 모든 속성 시너지를 증폭하는 중립 허브다. 채우는 만큼 속성 존을
///    못 채우는 순수 기회비용이므로, 어떤 속성이든 받아 "증폭 vs 단계"의 선택지를 만든다.
///  · <b>무속성 룬</b>(element 비어 있음): 속성이 없으니 제약할 근거가 없다. 어디든 놓인다.
///
/// 배치 확정(GridManager.TryPlaceShape) · 드래그 프리뷰 · 배치 가능 조회(선택 팝업)가 모두 여기를 통과한다.
/// </summary>
public static class RuneZoneRule
{
    /// <summary>존 코드 <paramref name="zoneCode"/> 칸에 속성 <paramref name="elementId"/> 룬을 놓을 수 있는가.</summary>
    /// <param name="isLegendary">레전드리 등급 룬이면 true. 레전드리는 중앙(CENTER)에 배치 불가.</param>
    public static bool Accepts(char zoneCode, string elementId, bool isLegendary = false)
    {
        if (zoneCode == '\0') return true;                    // 속성 존이 없는 판(레거시 그리드) — 제약 없음
        if (string.IsNullOrEmpty(elementId)) return true;      // 무속성 룬
        if (zoneCode == ElementDef.CenterCode)
            return !isLegendary;                              // 중앙 공명 — 레전드리는 자기 속성 존에만

        char want = ElementDef.IdToCode(elementId);
        // ElementDef에 없는 속성값(데이터 오타·신규 속성 미등록 등)이면 제약하지 않는다.
        // 여기서 막으면 그 룬은 중앙 말고는 어디에도 못 놓는 <b>죽은 보상</b>이 된다.
        if (want == '\0') return true;

        return zoneCode == want;
    }

    /// <summary>칸 단위 판정. square가 null이면 판정 대상이 아니므로 통과.</summary>
    public static bool Accepts(GridSquare square, string elementId, bool isLegendary = false)
        => square == null || Accepts(square.zoneCode, elementId, isLegendary);

    /// <summary>룬 아이템의 속성 zone_id("FIRE" 등). 아이템이 없거나 무속성이면 null.</summary>
    public static string ElementOf(RuntimeItemData item)
        => item != null && !string.IsNullOrEmpty(item.element) ? item.element : null;
}

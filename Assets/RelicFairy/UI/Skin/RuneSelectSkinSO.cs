using UnityEngine;

/// <summary>
/// 룬 획득 팝업(<see cref="UI_RuneSelectPopup"/>) 아트 슬롯. 디자이너 납품(바탕화면 0_룬 획득_260723)과 1:1.
///
/// 미할당 슬롯은 <see cref="ShopUIStyle.Skin"/>에서 무시되어 코드로 그린 색 박스가 그대로 남는다 —
/// <b>아트가 0장이어도 화면이 지금과 동일하게 보인다.</b> 납품이 오는 대로 한 장씩 꽂으면 된다.
///
/// Addressable 키 "UI/RuneSelectSkin"으로 1회 로드해 캐싱한다(<see cref="UISkin"/>).
/// 기존 <see cref="RefinerySkinSO"/>와 동일한 관례 — 세 번째 패턴을 만들지 않는다.
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/UI/RuneSelect Skin", fileName = "RuneSelectSkin")]
public sealed class RuneSelectSkinSO : ScriptableObject
{
    [Header("배경 / 타이틀")]
    [Tooltip("룬 획득 바탕 — 전면 일러스트(9-slice 아님). 창 전체를 덮는다.")]
    public Sprite background;
    public Sprite titleBar;

    [Header("카드")]
    [Tooltip("룬 획득 카드 — 테두리 라인아트(9-slice).")]
    public Sprite cardFrame;
    [Tooltip("룬 획득 카드 바탕 — 채움.")]
    public Sprite cardFill;
    [Tooltip("상단 속성 리본. ElementDef.Order 순서 — 불/얼음/전기/풀/빛/어둠")]
    public Sprite[] elementRibbon = new Sprite[6];

    [Header("모양 미리보기")]
    [Tooltip("룬 타일 — 도형을 이루는 한 칸. 속성색으로 곱해 쓴다.")]
    public Sprite runeTile;
    [Tooltip("룬조각 속성 엠블럼 — 카드 속성 배지. ElementDef.Order(불/얼음/전기/풀/빛/어둠).")]
    public Sprite[] elementPiece = new Sprite[6];

    /// <summary>속성 엠블럼(범위 밖·미할당은 null → 표시 안 함).</summary>
    public Sprite ElementPiece(int elementIndex)
        => (elementPiece == null || elementIndex < 0 || elementIndex >= elementPiece.Length) ? null : elementPiece[elementIndex];

    [Header("효과 / 배치 상태")]
    [Tooltip("효과 칸 — 효과 목록 박스 배경(9-slice).")]
    public Sprite effectBox;
    [Tooltip("놓을 자리 있음 — 초록 상태 바.")]
    public Sprite placeOk;
    [Tooltip("놓을 자리 없음 — 빨강 상태 바.")]
    public Sprite placeNo;

    [Header("버튼")]
    public Sprite confirmButton;   // 선택
    public Sprite skipButton;      // 넘기기

    // ── 조회 (범위 밖·미할당은 null → 색 폴백) ──

    public Sprite ElementRibbon(int elementIndex) => Pick(elementRibbon, elementIndex);

    private static Sprite Pick(Sprite[] arr, int idx)
        => (arr == null || arr.Length == 0 || idx < 0 || idx >= arr.Length) ? null : arr[idx];
}

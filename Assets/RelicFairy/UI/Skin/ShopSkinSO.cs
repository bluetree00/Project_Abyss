using UnityEngine;

/// <summary>
/// 상점(심연의 행상) 화면(<see cref="UI_ShopPanel"/>) 아트 슬롯. 디자이너 납품(바탕화면 0_상점_260724)과 1:1.
///
/// 미할당 슬롯은 <see cref="ShopUIStyle.Skin"/>에서 무시되어 코드로 그린 색 박스가 그대로 남는다.
/// Addressable 키 "UI/ShopSkin"으로 1회 로드해 캐싱한다(<see cref="UISkin"/>). <see cref="RefinerySkinSO"/> 관례.
///
/// 카테고리 배열 순서: 0=버프 1=룬 2=재료 3=포션 (색: 초록/보라/주황/빨강).
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/UI/Shop Skin", fileName = "ShopSkin")]
public sealed class ShopSkinSO : ScriptableObject
{
    [Header("배경 / 틀")]
    [Tooltip("상점 배경 — 전체화면 나무결 일러스트.")]
    public Sprite background;
    [Tooltip("상점 테두리 — 전체화면 외곽 프레임(9-slice).")]
    public Sprite windowBorder;

    [Header("상단바")]
    [Tooltip("심연의행상 초상 및 대사칸 — 좌상단 밴드.")]
    public Sprite portraitBand;
    public Sprite lanternIcon;   // 등불
    public Sprite rerollButton;  // 상품 리롤버튼

    [Header("오늘의 특가")]
    public Sprite dealTab;         // 특가 표시("오늘의 특가")
    public Sprite dealBorder;      // 특가 테두리(가로 히어로)
    public Sprite dealContentBg;   // 특가 내용 바탕
    public Sprite dealItemBg;      // 특가 아이템 바탕(아이콘)
    public Sprite dealItemBorder;  // 특가 아이템 테두리

    [Header("상품 카드")]
    public Sprite cardBg;          // 상품카드 배경
    [Tooltip("상품 카테고리 세로 악센트. 0=버프 1=룬 2=재료 3=포션")]
    public Sprite[] cardAccent = new Sprite[4];
    [Tooltip("카테고리 배지. 0=버프 1=룬 2=재료 3=포션")]
    public Sprite[] categoryBadge = new Sprite[4];
    public Sprite buyButton;       // 구매버튼

    [Header("고른 물건")]
    public Sprite selectedWindow;  // 고른 물건 창(우측 컬럼)
    public Sprite selectedBg;      // 고른물건 바탕(미리보기)
    [Tooltip("고른물건 카테고리 테두리. 0=버프 1=룬 2=재료 3=포션")]
    public Sprite[] selectedBorder = new Sprite[4];

    // ── 조회 (범위 밖·미할당은 null → 색 폴백) ──
    public Sprite CardAccent(int cat)     => Pick(cardAccent, cat);
    public Sprite CategoryBadge(int cat)  => Pick(categoryBadge, cat);
    public Sprite SelectedBorder(int cat) => Pick(selectedBorder, cat);

    private static Sprite Pick(Sprite[] arr, int idx)
        => (arr == null || idx < 0 || idx >= arr.Length) ? null : arr[idx];
}

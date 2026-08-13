using UnityEngine;

/// <summary>
/// 상점(심연의 행상) 화면(<see cref="UI_ShopPanel"/>) 아트 슬롯. 디자이너 완성본(바탕화면 "상점 UI 리소스")과 1:1.
///
/// 미할당 슬롯은 <see cref="ShopUIStyle.Skin"/>에서 무시되어 코드로 그린 색 박스가 그대로 남는다.
/// Addressable 키 "UI/ShopSkin"으로 1회 로드해 캐싱한다(<see cref="UISkin"/>). <see cref="RefinerySkinSO"/> 관례.
///
/// ※ 슬롯 구성이 초기 선화 시안과 다르다 — 완성본이 조각을 다르게 나눴다.
///   테두리: 9-slice 1장 → 코너 4장 / 상단바: 밴드+등불 2장 → 간판 1장 / 특가: 탭+틀+내용 3장 → 1장.
///
/// 카테고리 배열 순서는 <see cref="ShopProductCategory"/>와 같다 — 0=버프 1=룬 2=재료 3=포션.
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/UI/Shop Skin", fileName = "ShopSkin")]
public sealed class ShopSkinSO : ScriptableObject
{
    [Header("창")]
    [Tooltip("창 전체 배경 — 나무 좌판. 창과 같은 비율(1178:837)이라 늘리지 않는다.")]
    public Sprite background;
    [Tooltip("상단 띠 — 간판/골드가 얹히는 어두운 가로 밴드. 좌우 9-slice.")]
    public Sprite topBand;
    [Tooltip("간판 — '심연의 행상' 나무판 + 사슬 + 등불이 한 장에 들어있다(등불 별도 슬롯 없음).")]
    public Sprite titleBoard;
    [Tooltip("우상단 문장 깃발.")]
    public Sprite crest;
    [Tooltip("골드 코인 아이콘 — 상단 잔액과 카드 가격에 공용.")]
    public Sprite goldCoin;

    [Header("창 모서리 장식 (좌상/우상/좌하/우하)")]
    public Sprite cornerTopLeft;
    public Sprite cornerTopRight;
    public Sprite cornerBottomLeft;
    public Sprite cornerBottomRight;

    [Header("오늘의 특가")]
    [Tooltip("특가 배너 — '오늘의 특가' 리본이 아트에 포함돼 있다. 좌우 9-slice.")]
    public Sprite dealPanel;

    [Header("상품 카드")]
    [Tooltip("상품 카드 양피지 바탕.")]
    public Sprite cardBg;
    [Tooltip("품절 카드 바탕 — 바랜 양피지. 미할당이면 cardBg를 어둡게 틴트한다.")]
    public Sprite cardBgSold;
    [Tooltip("SOLD OUT 도장 — 품절 카드 위에 겹친다.")]
    public Sprite soldStamp;
    [Tooltip("아이콘 칸 채움(어두운 판).")]
    public Sprite iconFill;
    [Tooltip("아이콘 칸 테두리.")]
    public Sprite iconFrame;
    [Tooltip("카테고리 배지 바탕. 0=버프 1=룬 2=재료 3=포션 — 룬·재료는 미납품(색 폴백).")]
    public Sprite[] categoryBadgeBg = new Sprite[4];
    [Tooltip("카테고리 배지 글자. 0=버프 1=룬 2=재료 3=포션 — 룬·재료는 미납품(코드 라벨 폴백).")]
    public Sprite[] categoryBadge = new Sprite[4];

    [Header("고른 물건")]
    [Tooltip("우측 양피지 패널 — 겹친 종이 가장자리 포함. 상하 9-slice.")]
    public Sprite selectedPanel;

    [Header("조작")]
    public Sprite buyButton;      // 구매버튼("사겠네"가 아트에 구워져 있다)
    public Sprite rerollButton;   // 새로고침 버튼

    // ── 조회 (범위 밖·미할당은 null → 색 폴백) ──

    public Sprite CategoryBadge(int cat)   => Pick(categoryBadge, cat);
    public Sprite CategoryBadgeBg(int cat) => Pick(categoryBadgeBg, cat);

    /// <summary>카드 바탕. 품절 전용 아트가 없으면 null을 돌려 호출측이 틴트로 처리하게 한다.</summary>
    public Sprite CardBackground(bool sold) => sold && cardBgSold != null ? cardBgSold : cardBg;

    private static Sprite Pick(Sprite[] arr, int idx)
        => (arr == null || idx < 0 || idx >= arr.Length) ? null : arr[idx];
}

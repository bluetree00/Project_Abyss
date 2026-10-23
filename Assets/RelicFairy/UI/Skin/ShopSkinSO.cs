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
/// 10-13: 룬 · 재료 배지 바탕은 납품 버프바탕(초록)을 보라 · 주황으로 재색칠한 리본(룬바탕 · 재료바탕) — 넷이 같은 모양.
///        나가기 · 새로고침 폴백 · 이름 명판 · 가격 알약은 Artsystack 부품(아래 「Artsystack 부품」) — 비면 코드 판.
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
    [Tooltip("카테고리 배지 바탕. 0=버프 1=룬 2=재료 3=포션 — 룬·재료는 버프바탕 재색칠 리본(10-13). 비면 색 폴백.")]
    public Sprite[] categoryBadgeBg = new Sprite[4];
    [Tooltip("카테고리 배지 글자. 0=버프 1=룬 2=재료 3=포션 — 룬·재료는 미납품(코드 라벨 폴백).")]
    public Sprite[] categoryBadge = new Sprite[4];

    [Header("고른 물건")]
    [Tooltip("우측 양피지 패널 — 겹친 종이 가장자리 포함. 상하 9-slice.")]
    public Sprite selectedPanel;

    [Header("조작")]
    public Sprite buyButton;      // 구매버튼("사겠네"가 아트에 구워져 있다)
    public Sprite rerollButton;   // 새로고침 버튼

    [Header("Artsystack 부품 (10-13) — 비면 코드 판")]
    [Tooltip("나가기 판 — components/slot_short_inactive(원색 · 먹갈 = 나무 톤). 9-slice 22/12/22/12.")]
    [SerializeField] private Sprite exitPanel;
    [Tooltip("새로고침 폴백 판(비용이 아트의 10과 다름 · 골드 모자람 · 이미 돌림) — components/slot_short_active(원색 · 먹 + 금 테). " +
             "비활성은 같은 판을 버튼 틴트(알파 0.45)가 누른다. 9-slice 24/20/24/20.")]
    [SerializeField] private Sprite rerollPanel;
    [Tooltip("고른 물건 이름 명판 — components/placeholder(양 끝 뾰족 + 금 테 · 원색). 9-slice 150/0/150/0.")]
    [SerializeField] private Sprite namePlate;
    [Tooltip("가격 알약 · 룬 태그 판 — components/Rectangle_locked. 코드가 무채로 바꿔 고동(60,44,28)으로 칠한다. 9-slice 22/0/22/0.")]
    [SerializeField] private Sprite pricePill;

    [Header("흑요석 새 레이아웃 (10-21) — 힉스필드 · Artsystack 흰 아이콘. 비면 코드 판")]
    [Tooltip("상점 창 틀 — 흑요석 + 호박색 등불 상감. 9칸 72(모서리 쐐기돌 ≈ 60 · 안쪽 빛 선 ≈ 48).")]
    [SerializeField] private Sprite windowFrame;
    [Tooltip("행상 초상(웃는 버섯 떠돌이 상인) — 가장자리가 투명으로 풀린 2:3.")]
    [SerializeField] private Sprite peddlerPortrait;
    [Tooltip("가호 문장 판 — 흑요석 원판 + 금 테. 가운데에 가호 아이콘(흰)을 얹는다.")]
    [SerializeField] private Sprite blessingEmblem;
    [Tooltip("가호 아이콘(흰) — StatType 순서(공격력 · 방어 · 최대 체력 · 이속 · 공속 · 스킬 쿨 · 근접 · 원거리 · 행운 · 액티브 쿨 · 투사체 · 즉시 · 치명 확률 · 치명 피해).")]
    [SerializeField] private Sprite[] blessingIcons = new Sprite[14];
    [Tooltip("잠금(닫힘 · 열림) · 새로고침 아이콘(흰).")]
    [SerializeField] private Sprite lockClosed, lockOpen, rerollIcon;
    [Tooltip("「내 상태」 띠 아이콘(흰) — 체력 · 포션 · 룬 보관함 · 룬판.")]
    [SerializeField] private Sprite hpIcon, potionIcon, storageIcon, boardIcon;
    [Tooltip("원석 환전소(10-22) — 저울 문장(힉스필드 · 둥근 메달) · 원석 아이콘(HUD 재화와 같은 그림).")]
    [SerializeField] private Sprite exchangeEmblem, oreIcon;

    public Sprite ExitPanel   => exitPanel;
    public Sprite RerollPanel => rerollPanel;
    public Sprite NamePlate   => namePlate;
    public Sprite PricePill   => pricePill;

    public Sprite WindowFrame     => windowFrame;
    public Sprite PeddlerPortrait => peddlerPortrait;
    public Sprite BlessingEmblem  => blessingEmblem;
    public Sprite LockClosed      => lockClosed;
    public Sprite LockOpen        => lockOpen;
    public Sprite RerollIcon      => rerollIcon;
    public Sprite HpIcon          => hpIcon;
    public Sprite PotionIcon      => potionIcon;
    public Sprite StorageIcon     => storageIcon;
    public Sprite BoardIcon       => boardIcon;
    public Sprite ExchangeEmblem  => exchangeEmblem;
    public Sprite OreIcon         => oreIcon;
    public Sprite BlessingIcon(StatType stat) => Pick(blessingIcons, (int)stat);

    // ── 조회 (범위 밖·미할당은 null → 색 폴백) ──

    public Sprite CategoryBadge(int cat)   => Pick(categoryBadge, cat);
    public Sprite CategoryBadgeBg(int cat) => Pick(categoryBadgeBg, cat);

    /// <summary>카드 바탕. 품절 전용 아트가 없으면 null을 돌려 호출측이 틴트로 처리하게 한다.</summary>
    public Sprite CardBackground(bool sold) => sold && cardBgSold != null ? cardBgSold : cardBg;

    private static Sprite Pick(Sprite[] arr, int idx)
        => (arr == null || idx < 0 || idx >= arr.Length) ? null : arr[idx];
}

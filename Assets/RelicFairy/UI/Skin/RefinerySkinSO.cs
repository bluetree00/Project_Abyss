using UnityEngine;

/// <summary>
/// 정제소 화면(<see cref="UI_RefineryPanel"/>) 아트 슬롯. 의뢰서 R1~R12와 1:1로 대응한다
/// (바탕화면 의뢰서/RelicFairy_정제소_룬획득_디자인의뢰.pptx).
///
/// 미할당 슬롯은 <see cref="ShopUIStyle.Skin"/>에서 무시되어 기존 색 박스가 그대로 남는다 —
/// <b>아트가 0장이어도 화면이 지금과 동일하게 보인다.</b> 그래서 납품이 오는 대로 한 장씩 꽂으면 된다.
///
/// Addressable 키 "UI/RefinerySkin"으로 1회 로드해 캐싱한다(<see cref="UISkin"/>).
/// 기존 <see cref="EffectIconSetSO"/> / <see cref="RuneArtLibrarySO"/>와 동일한 관례.
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/UI/Refinery Skin", fileName = "RefinerySkin")]
public sealed class RefinerySkinSO : ScriptableObject
{
    [Header("R1 창 / R2 제목바")]
    [Tooltip("창 테두리 9-slice. 룬 획득 팝업과 공용.")]
    public Sprite windowFrame;
    public Sprite titleBar;
    [Tooltip("원석 아이콘 — 제목바 우측 잔량 표시용.")]
    public Sprite oreIcon;

    [Header("R3 제단 무대")]
    public Sprite altarBackground;

    [Header("R4 속성 선택 노드 (회색조 — 속성색은 코드가 곱한다)")]
    [Tooltip("0=기본 1=호버 2=선택됨 3=보유중")]
    public Sprite[] nodeBase = new Sprite[4];
    [Tooltip("6속성 각인. ElementDef.Order 순서 — 불/얼음/전기/풀/빛/어둠")]
    public Sprite[] nodeGlyph = new Sprite[6];

    [Header("R5 결과 슬롯")]
    public Sprite slotFrame;
    // [비움] 중앙 원 바탕 — 2026-09-03.
    // 「정제소 중앙 원 바탕@2x」가 들어 있었는데 <b>순수 검정 원판</b>(RGB 0,0,0 · 불투명 78%)이라,
    // 속이 비치는 「중앙 최종룬」 액자 뒤에 깔리면서 결과가 비었을 때 중앙이 검은 구멍으로 보였다.
    // 그 검은 원은 @2x 세트의 「정제소 중앙 원 테두리@2x」와 한 쌍인데 짝이 미배선이라 혼자 남아 있었다.
    // 완성본(정제소 UI 리소스)의 중앙은 액자 한 장뿐이고 뒤로 배경 무늬가 비친다 → 비워 둔다.
    [Tooltip("[비움] 중앙 원 바탕 — 제단 코어 채움(결과 뒤). 넣으면 액자 뒤에 깔린다.")]
    public Sprite altarCore;
    [Tooltip("등급 테두리. 0=Common 1=Rare 2=Epic 3=Legendary")]
    public Sprite[] gradeBorder = new Sprite[4];
    public Sprite[] gradeGlow   = new Sprite[4];

    [Header("R6 피버 게이지")]
    public Sprite feverTrack;
    [Tooltip("단계별 칸. 인덱스가 곧 열기 단계(0~7).")]
    public Sprite[] feverCell = new Sprite[8];

    [Header("R7 확률 막대")]
    public Sprite barTrack;
    [Tooltip("확률막대 바탕 — 확률 패널 전체 배경.")]
    public Sprite oddsPanel;
    [Tooltip("확률 막대 테두리 — 칸별 프레임 오버레이.")]
    public Sprite barFrame;
    [Tooltip("0=Rare 1=Epic 2=Legendary")]
    public Sprite[] barFill       = new Sprite[3];
    [Tooltip("과열(다음 회 확률 2배) 상태 채움. 비우면 barFill을 그대로 쓴다.")]
    public Sprite[] barFillHeated = new Sprite[3];

    [Header("좌측 상주 판")]
    [Tooltip("완성본 좌측 세로 패널(세부지표 3, 589×822). 돌발 이벤트 배너가 이 위에 뜬다. 의뢰서 \"좌 = 무대\".")]
    public Sprite eventPanel;

    [Header("R8 돌발 이벤트")]
    [Tooltip("재점화 — 460×96. 나머지 셋보다 확실히 크다.")]
    public Sprite bannerReignite;
    [Tooltip("340×60. 0=과열 1=불티 2=쌍생")]
    public Sprite[] bannerSmall = new Sprite[3];
    [Tooltip("과열·불티가 '다음 회에 예약됨'을 알리는 작은 배지.")]
    public Sprite reservedBadge;

    [Header("R9~R12 조작부")]
    [Tooltip("0=충분 1=부족 2=무료")]
    public Sprite[] costPlate = new Sprite[3];
    [Tooltip("0=기본 1=호버 2=눌림 3=비활성")]
    public Sprite[] spinButton = new Sprite[4];
    [Tooltip("0=기본 1=호버 2=비활성")]
    public Sprite[] reforgeButton = new Sprite[3];
    [Tooltip("0=할인 1=전설2배 2=첫회무료")]
    public Sprite[] perkBadge = new Sprite[3];

    [Header("연출 — 응축 3박자 (회색조, 등급별 시트)")]
    [Tooltip("Rare 15프레임 (응축5·균열4·개봉6)")]
    public Sprite[] condenseRare = new Sprite[0];
    public Sprite[] condenseEpic = new Sprite[0];
    public Sprite[] condenseLegendary = new Sprite[0];

    // ── 조회 (범위 밖·미할당은 null → 색 폴백) ──

    public Sprite Node(int stateIndex)      => Pick(nodeBase, stateIndex);
    public Sprite Glyph(int elementIndex)   => Pick(nodeGlyph, elementIndex);
    public Sprite GradeBorder(ItemRarity r) => Pick(gradeBorder, (int)r);
    public Sprite GradeGlow(ItemRarity r)   => Pick(gradeGlow, (int)r);
    public Sprite FeverCell(int level)      => Pick(feverCell, level);

    /// <summary>등급 인덱스(0=Rare 1=Epic 2=Legendary)별 확률 막대 채움. 과열이면 heated 우선.</summary>
    public Sprite BarFill(int tierIndex, bool heated)
    {
        if (heated)
        {
            var h = Pick(barFillHeated, tierIndex);
            if (h != null) return h;
        }
        return Pick(barFill, tierIndex);
    }

    public Sprite[] CondenseFrames(ItemRarity r) => r switch
    {
        ItemRarity.Legendary => condenseLegendary,
        ItemRarity.Epic      => condenseEpic,
        _                    => condenseRare,
    };

    private static Sprite Pick(Sprite[] arr, int idx)
        => (arr == null || arr.Length == 0 || idx < 0 || idx >= arr.Length) ? null : arr[idx];
}

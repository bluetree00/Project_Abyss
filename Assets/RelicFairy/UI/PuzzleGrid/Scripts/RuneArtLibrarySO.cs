using UnityEngine;

/// <summary>
/// 등급별 룬 한칸 아트 라이브러리. 룬 표시(선택 카드·보관함·판)에서 공통으로 참조한다.
/// 아트는 등급으로 고르고, <b>속성 색으로 틴트</b>해 룬의 랜덤 속성을 나타낸다(효과=조각 / 속성=랜덤 배정).
///
/// Addressable 키 "RuneArtLibrary"로 1회 로드해 캐싱한다(<see cref="RuneArt"/>).
/// gradeArt/borderArt 인덱스: 0=Common, 1=Rare, 2=Epic, 3=Legendary, 4=예비(최고급/특수).
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/Rune/Art Library", fileName = "RuneArtLibrary")]
public sealed class RuneArtLibrarySO : ScriptableObject
{
    /// <summary>기능 하나에 문양 하나. <c>effectType</c>은 ITEM_DATA의 <c>effect_type</c>과 같은 글자다.</summary>
    [System.Serializable]
    public struct EffectIcon
    {
        [Tooltip("ITEM_DATA의 effect_type (예: AllDamage · Freeze · FireLegendAoe)")]
        public string effectType;
        public Sprite icon;
    }

    [Header("기능별 문양 — 룬이 무엇을 하는지가 아이콘으로 읽혀야 한다")]
    [SerializeField, Tooltip("효과 종류마다 다른 문양. 비어 있으면 아래 등급/속성 아트로 폴백한다.\n" +
                             "룬 120종은 효과 54종을 나눠 쓰므로, 여기 54칸이면 모든 룬이 제 문양을 갖는다.")]
    private EffectIcon[] effectIcons = new EffectIcon[0];

    [SerializeField, Tooltip("등급별 룬 아트. 0=Common,1=Rare,2=Epic,3=Legendary,4=예비")]
    private Sprite[] gradeArt = new Sprite[5];

    // [미배선] 등급 테두리 — <see cref="GetBorder"/>를 호출하는 코드가 프로젝트에 하나도 없다.
    // git 이력상 <b>한 번도 연결된 적이 없는</b> 예비 API다(끊긴 게 아니라 처음부터 안 썼다).
    // 룬 아이콘에 등급 액자를 두르려던 흔적으로 보이며, 지금 등급은 아트 자체(룬1~5)로만 읽힌다.
    // 배열과 아트는 남긴다 — 07-22 납품본이고 바탕화면 원본 폴더가 이미 정리돼 <b>백업이 없다</b>.
    // 등급 액자를 살릴 거면 여기가 제자리고, 정말 버릴 거면 파일까지 같이 결정해야 한다.
    [SerializeField, Tooltip("[미배선] 등급별 룬 테두리. gradeArt와 동일 인덱스. 호출처 0 — 예비 슬롯.")]
    private Sprite[] borderArt = new Sprite[5];

    // [비움] 속성별 룬 아트 — 2026-09-03 정리.
    // 여기 있던 룬1~5는 <b>gradeArt와 똑같은 파일</b>을 속성 순서로 다시 담은 중복이었다.
    // 같은 돌 한 장이 "불 속성"이자 "Common 등급"일 수는 없어 뜻이 갈렸고, 5장뿐이라 빛이 비어
    // 빛 룬만 무늬 없는 등급석으로 떨어졌다. 지금은 08-28 납품 blockTile(6/6 완비)이
    // 네 경로(판 블록·선택 팝업·대기열·보관함 정보판)를 모두 덮어 여긴 도달하지 않는다.
    // 배열은 남겨 둔다 — 속성 전용 아트가 새로 납품되면 여기 채우는 것이 제자리다.
    [SerializeField, Tooltip("[미사용] 속성별 룬 아트. 지금은 blockTile이 전 경로를 덮는다. 속성 전용 아트 납품 시 사용.")]
    private Sprite[] elementArt = new Sprite[6];

    [SerializeField, Tooltip("[미사용] 속성별 룬 테두리. GetBorderByElement를 읽는 곳이 없다.")]
    private Sprite[] elementBorder = new Sprite[6];

    [SerializeField, Tooltip("판 위 블록 칸 타일. ElementDef.Order(불/얼음/전기/풀/빛/어둠).\n" +
                             "룬 아이콘(elementArt)과 분리된 슬롯이다 — 룬 자체는 룬 아트로, " +
                             "그 룬이 차지한 칸은 속성 타일로 그린다.")]
    private Sprite[] blockTile = new Sprite[6];

    [Header("판 외곽 액자 — 조각 조립(상·하·좌·우 + 코너 4)")]
    [SerializeField, Tooltip("가로 변. 좌우 코너 사이를 늘려 채운다.")]
    private Sprite frameTop, frameBottom;
    [SerializeField, Tooltip("세로 변. 상하 코너 사이를 늘려 채운다.")]
    private Sprite frameLeft, frameRight;
    [SerializeField, Tooltip("네 귀퉁이. 늘리지 않고 원본 비율로 둔다.")]
    private Sprite frameTL, frameTR, frameBL, frameBR;

    /// <summary>
    /// 기능에 대응하는 문양. 없으면 null → 호출측이 등급/속성 아트로 폴백한다.
    ///
    /// <para>표가 작아(≤54) 선형 탐색으로 충분하다 — 사전을 만들면 <c>OnValidate</c> 이후
    /// 갱신 시점을 따로 관리해야 해서 오히려 깨지기 쉽다.</para>
    /// </summary>
    public Sprite GetIconByEffect(string effectType)
    {
        if (string.IsNullOrEmpty(effectType) || effectIcons == null) return null;
        for (int i = 0; i < effectIcons.Length; i++)
            if (effectIcons[i].icon != null &&
                string.Equals(effectIcons[i].effectType, effectType, System.StringComparison.OrdinalIgnoreCase))
                return effectIcons[i].icon;
        return null;
    }

    public Sprite FrameTop    => frameTop;
    public Sprite FrameBottom => frameBottom;
    public Sprite FrameLeft   => frameLeft;
    public Sprite FrameRight  => frameRight;
    public Sprite FrameTL     => frameTL;
    public Sprite FrameTR     => frameTR;
    public Sprite FrameBL     => frameBL;
    public Sprite FrameBR     => frameBR;

    /// <summary>판 외곽 액자 조각이 갖춰졌나. 하나라도 없으면 액자를 그리지 않는다.</summary>
    public bool HasBoardFrame => frameTop && frameBottom && frameLeft && frameRight
                              && frameTL && frameTR && frameBL && frameBR;

    /// <summary>
    /// 판에 놓인 룬이 차지하는 <b>칸</b>에 깔 속성 타일.
    /// 미할당이면 null → 호출측이 기존 룬 아트로 폴백한다.
    /// </summary>
    public Sprite GetBlockTile(int elementIndex)
        => (blockTile == null || elementIndex < 0 || elementIndex >= blockTile.Length) ? null : blockTile[elementIndex];

    /// <summary>등급에 해당하는 룬 아트. 범위를 벗어나면 마지막(또는 첫) 유효 스프라이트로 폴백.</summary>
    public Sprite GetArt(ItemRarity rarity) => Pick(gradeArt, (int)rarity);

    /// <summary>등급에 해당하는 룬 테두리.</summary>
    public Sprite GetBorder(ItemRarity rarity) => Pick(borderArt, (int)rarity);

    /// <summary>속성 인덱스별 룬 아트(ElementDef.Order). 미할당은 null → 호출측이 색 틴트로 폴백.</summary>
    public Sprite GetArtByElement(int elementIndex)
        => (elementArt == null || elementIndex < 0 || elementIndex >= elementArt.Length) ? null : elementArt[elementIndex];

    /// <summary>속성 인덱스별 룬 테두리. 미할당은 null.</summary>
    public Sprite GetBorderByElement(int elementIndex)
        => (elementBorder == null || elementIndex < 0 || elementIndex >= elementBorder.Length) ? null : elementBorder[elementIndex];

    private static Sprite Pick(Sprite[] arr, int idx)
    {
        if (arr == null || arr.Length == 0) return null;
        idx = Mathf.Clamp(idx, 0, arr.Length - 1);
        return arr[idx] != null ? arr[idx] : arr[0];
    }
}

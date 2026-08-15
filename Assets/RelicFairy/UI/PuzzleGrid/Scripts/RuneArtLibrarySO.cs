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
    [SerializeField, Tooltip("등급별 룬 아트. 0=Common,1=Rare,2=Epic,3=Legendary,4=예비")]
    private Sprite[] gradeArt = new Sprite[5];

    [SerializeField, Tooltip("등급별 룬 테두리. gradeArt와 동일 인덱스")]
    private Sprite[] borderArt = new Sprite[5];

    [SerializeField, Tooltip("속성별 룬 아트. ElementDef.Order(불/얼음/전기/풀/빛/어둠). 빈 칸은 null → 색 틴트 폴백.")]
    private Sprite[] elementArt = new Sprite[6];

    [SerializeField, Tooltip("속성별 룬 테두리. elementArt와 동일 인덱스.")]
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

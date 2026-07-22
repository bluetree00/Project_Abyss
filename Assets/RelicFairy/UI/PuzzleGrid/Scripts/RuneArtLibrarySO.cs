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

    /// <summary>등급에 해당하는 룬 아트. 범위를 벗어나면 마지막(또는 첫) 유효 스프라이트로 폴백.</summary>
    public Sprite GetArt(ItemRarity rarity) => Pick(gradeArt, (int)rarity);

    /// <summary>등급에 해당하는 룬 테두리.</summary>
    public Sprite GetBorder(ItemRarity rarity) => Pick(borderArt, (int)rarity);

    private static Sprite Pick(Sprite[] arr, int idx)
    {
        if (arr == null || arr.Length == 0) return null;
        idx = Mathf.Clamp(idx, 0, arr.Length - 1);
        return arr[idx] != null ? arr[idx] : arr[0];
    }
}

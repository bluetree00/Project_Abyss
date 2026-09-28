using UnityEngine;

/// <summary>
/// 유물 개화(파츠 드래프트) 팝업 <see cref="UI_RelicPartDraftPopup"/> 아트 슬롯.
///
/// <b>아트는 아직 없다.</b> 레이아웃만 먼저 굳히고(09-21 결정), 그림이 나오면 여기에 꽂는다.
/// 미할당 슬롯은 <see cref="ShopUIStyle.Skin"/>이 무시하므로 코드로 그린 색 원반·기호가 그대로 남는다 —
/// <b>0장이어도 화면은 지금과 똑같이 동작한다.</b>
///
/// 칸 크기는 팝업의 좌표 상수와 같은 값이다(창 1280×760 · 카드 380×520 · 문양 168²).
/// 납품 명세는 구현설계/RelicFairy_개화UI_리소스명세_20260921.md.
///
/// Addressable 키 "UI/RelicPartDraftSkin"으로 1회 로드해 캐싱한다(<see cref="UISkin"/>).
/// 기존 <see cref="RuneSelectSkinSO"/>와 동일한 관례 — 새 패턴을 만들지 않는다.
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/UI/RelicPartDraft Skin", fileName = "RelicPartDraftSkin")]
public sealed class RelicPartDraftSkinSO : ScriptableObject
{
    /// <summary>파츠 하나에만 쓰는 전용 문양. 비우면 계열 문양으로 내려간다.</summary>
    [System.Serializable]
    public struct PartArt
    {
        [Tooltip("RelicPartEntry.part_id — CSV의 고유 식별자와 철자까지 같아야 한다.")]
        public string  partId;
        public Sprite  symbol;
    }

    [Header("창 — 1280×760")]
    [Tooltip("개화 배경 — 전면 일러스트(9-slice 아님). 창 전체를 덮는다.")]
    public Sprite background;
    [Tooltip("타이틀 띠 — 900×72. 「유물 개화」 글자는 코드가 위에 얹으므로 아트에 굽지 않는다.")]
    public Sprite titleBar;

    [Header("카드 — 380×520")]
    [Tooltip("카드 액자 — 테두리 라인아트(9-slice).")]
    public Sprite cardFrame;
    [Tooltip("카드 바탕 — 채움(9-slice).")]
    public Sprite cardFill;

    [Header("계열 — core · effect · behavior · trigger 순서")]
    [SerializeField, Tooltip("계열 칩 바탕 — 150×36(9-slice).")]
    private Sprite[] kindChip = new Sprite[4];
    [SerializeField, Tooltip("계열 문양 — 168×168. 파츠 전용 문양이 없을 때 쓰는 기본 그림.")]
    private Sprite[] kindEmblem = new Sprite[4];

    [Header("파츠 전용 문양(선택) — 없으면 계열 문양")]
    [SerializeField] private PartArt[] partArt = new PartArt[0];

    [Header("버튼")]
    [Tooltip("장착 버튼 — 220×56.")]
    public Sprite confirmButton;

    public Sprite KindChip(int kindIndex)   => Pick(kindChip, kindIndex);
    public Sprite KindEmblem(int kindIndex) => Pick(kindEmblem, kindIndex);

    /// <summary>파츠 전용 문양(없으면 null → 호출측이 계열 문양으로 내려간다).</summary>
    public Sprite PartSymbol(string partId)
    {
        if (string.IsNullOrEmpty(partId) || partArt == null) return null;
        for (int i = 0; i < partArt.Length; i++)
            if (partArt[i].partId == partId) return partArt[i].symbol;
        return null;
    }

    private static Sprite Pick(Sprite[] arr, int idx)
        => (arr == null || idx < 0 || idx >= arr.Length) ? null : arr[idx];
}

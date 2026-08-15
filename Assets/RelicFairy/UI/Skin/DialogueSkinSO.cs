using UnityEngine;

/// <summary>
/// 대화 팝업(<see cref="UI_DialoguePopup"/>) 아트 슬롯. 디자이너 납품(바탕화면 "대화창")과 1:1.
///
/// 미할당 슬롯은 <see cref="ShopUIStyle.Skin"/>에서 무시되어 프리팹 기존 모습이 그대로 남는다.
/// Addressable 키 "UI/DialogueSkin"으로 1회 로드해 캐싱한다(<see cref="UISkin"/>). <see cref="RefinerySkinSO"/> 관례.
///
/// ※ 이 화면에서 <b>스킨 대상은 대사 상자와 그 장식뿐</b>이다.
///   AdvanceButton은 1390×240짜리 투명 클릭 캐처라 판을 입히면 대사를 덮어버린다 — 건드리지 않는다.
///   납품본의 버튼 6종은 대응하는 자리가 없어 슬롯을 두지 않았다(선택지 UI가 생기면 그때).
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/UI/Dialogue Skin", fileName = "DialogueSkin")]
public sealed class DialogueSkinSO : ScriptableObject
{
    [Header("대사 상자")]
    [Tooltip("대사 상자 바탕. 상자가 화면 폭을 따라 늘어나므로 9-slice 필수.")]
    public Sprite plate;

    [Header("모서리 장식 (상자 네 귀퉁이)")]
    public Sprite cornerTopLeft;
    public Sprite cornerTopRight;
    public Sprite cornerBottomLeft;
    public Sprite cornerBottomRight;

    [Header("가로 장식 (상자 위·아래 모서리 중앙)")]
    [Tooltip("대부분이 투명한 넓은 캔버스에 얇은 장식이 들어있다 — 통짜로 놓아야 위치가 맞는다.")]
    public Sprite flourishTop;
    public Sprite flourishBottom;
}

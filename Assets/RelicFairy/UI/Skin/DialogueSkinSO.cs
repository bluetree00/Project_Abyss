using UnityEngine;

/// <summary>
/// 대화 팝업(<see cref="UI_DialoguePopup"/>) 아트 슬롯. 디자이너 납품(바탕화면 "대화창")과 1:1.
///
/// 미할당 슬롯은 <see cref="ShopUIStyle.Skin"/>에서 무시되어 프리팹 기존 모습이 그대로 남는다.
/// Addressable 키 "UI/DialogueSkin"으로 1회 로드해 캐싱한다(<see cref="UISkin"/>). <see cref="RefinerySkinSO"/> 관례.
///
/// ※ 이미지형 개편(2026-09-09)으로 액자·모서리·장식·이름 판 슬롯은 걷었다 — 남은 스킨 대상은 띠 한 장이다.
///   AdvanceButton은 1390×240짜리 투명 클릭 캐처라 판을 입히면 대사를 덮어버린다 — 건드리지 않는다.
///   납품본의 버튼 6종은 대응하는 자리가 없어 슬롯을 두지 않았다(선택지 UI가 생기면 그때).
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/UI/Dialogue Skin", fileName = "DialogueSkin")]
public sealed class DialogueSkinSO : ScriptableObject
{
    [Header("대사 띠 (이미지형 · 2026-09-09)")]
    [Tooltip("하단 반투명 띠(1160×168) 바탕. 비어 있으면 코드가 만든 둥근 소프트 판(검정 α0.55)을 쓴다. 9-slice 필수.")]
    public Sprite band;
}

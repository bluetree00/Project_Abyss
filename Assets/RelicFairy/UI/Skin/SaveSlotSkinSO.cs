using UnityEngine;

/// <summary>
/// 로비 세이브 창(<see cref="UI_SaveSlotPanel"/>) 아트 슬롯 — 10-23 힉스필드 그림(배경 · 카드 틀).
/// Addressable 키 "UI/SaveSlotSkin"으로 1회 로드해 캐싱한다(<see cref="UISkin"/>). 비어 있으면 창은 코드 색 판으로 그대로 동작한다.
/// 반입 · 연결은 메뉴 「RelicFairy/Tools/10-23 로비 세이브 창 그림 반입 — 배경 · 틀」(<c>HiggsfieldArtImportEditor</c>).
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/UI/Save Slot Skin", fileName = "SaveSlotSkin")]
public sealed class SaveSlotSkinSO : ScriptableObject
{
    [Header("창")]
    [Tooltip("화면 전체 배경 — 달빛 드는 옛 성소(16:9). 화면을 덮도록 비율을 지켜 키운다.")]
    [SerializeField] private Sprite background;

    [Header("카드")]
    [Tooltip("슬롯 카드 틀 — 흑요석 테 + 금 상감 + 모서리 쐐기돌. 9칸(경계 64).")]
    [SerializeField] private Sprite cardFrame;

    public Sprite Background => background;
    public Sprite CardFrame  => cardFrame;
}

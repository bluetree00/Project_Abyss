using UnityEngine;

/// <summary>
/// 유물 선택 팝업(<see cref="UI_RelicInfoPopup"/>) 아트 슬롯. 디자이너 납품(바탕화면 "유물 선택 팝업")과 1:1.
///
/// 미할당 슬롯은 <see cref="ShopUIStyle.Skin"/>에서 무시되어 코드로 그린 색 박스가 그대로 남는다 —
/// 아트가 0장이어도 화면이 지금과 똑같이 동작한다.
/// Addressable 키 "UI/RelicInfoSkin"으로 1회 로드해 캐싱한다(<see cref="UISkin"/>). <see cref="RefinerySkinSO"/> 관례.
///
/// ※ 이 팝업은 <b>세로 레이아웃 + ContentSizeFitter</b>로 높이가 내용에 따라 늘어난다.
///   그래서 폭이 내용에 따라 변하는 조각(태그 칩)은 반드시 9-slice로 넣어야 한다.
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/UI/RelicInfo Skin", fileName = "RelicInfoSkin")]
public sealed class RelicInfoSkinSO : ScriptableObject
{
    [Header("창")]
    [Tooltip("팝업 외곽 — 얇은 선 프레임 + 상·하단 중앙 문장. 문장이 중앙에 있어 9-slice가 아니라 통짜로 늘린다.")]
    public Sprite panelFrame;

    [Header("초상화")]
    [Tooltip("일러스트 액자 — 이중 사각 + 상단 장식. 원본 비율 697:907을 지켜야 액자가 안 찌그러진다.")]
    public Sprite portraitFrame;

    [Header("태그")]
    [Tooltip("태그 칩 바탕. 칩 폭이 글자 길이에 따라 변하므로 좌우 9-slice 필수.")]
    public Sprite tagChip;

    [Header("능력 카드")]
    [Tooltip("고유(스킬) 카드 바탕 — 팝업에서 가장 큰 강조 칸.")]
    public Sprite uniqueCard;
    [Tooltip("상시 능력 칸. 하단 2열에 인덱스 순서대로 들어간다. 부족하면 마지막 것을 재사용한다.\n" +
             "⚠ 반드시 '어두운 판 + 테두리' 형태여야 한다 — 납품본의 정오(형광주황)·하루의 순환(순백)은 " +
             "단색 채움이라 그 위 글자를 못 읽는다. 그래서 현재 비워 뒀다(색 폴백이 더 읽힌다).")]
    public Sprite[] passivePlate = new Sprite[2];

    [Header("버튼 (글자가 아트에 구워져 있다 — 코드 라벨을 겹치면 안 된다)")]
    public Sprite selectButton;
    public Sprite cancelButton;

    /// <summary>상시 능력 칸 아트. 범위를 넘으면 마지막 것을 돌려준다(칸 수가 유물마다 다르다).</summary>
    public Sprite PassivePlate(int index)
    {
        if (passivePlate == null || passivePlate.Length == 0) return null;
        if (index < 0) index = 0;
        return passivePlate[Mathf.Min(index, passivePlate.Length - 1)];
    }
}

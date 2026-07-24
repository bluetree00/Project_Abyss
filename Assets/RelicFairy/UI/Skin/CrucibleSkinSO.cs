using UnityEngine;

/// <summary>
/// 재련소 화면(<see cref="UI_CruciblePanel"/>) 아트 슬롯. 디자이너 납품(바탕화면 0_강화 게이지…_260723)과 1:1.
///
/// 완성본은 <b>전체화면 + 상단 탭(무기강화/원거리 파츠)</b> 구성이다. 미할당 슬롯은 <see cref="ShopUIStyle.Skin"/>에서
/// 무시되어 코드로 그린 색이 남는다 — 아트가 0장이어도 화면이 동일하게 보인다.
///
/// Addressable 키 "UI/CrucibleSkin"으로 1회 로드해 캐싱한다(<see cref="UISkin"/>). <see cref="RefinerySkinSO"/> 관례.
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/UI/Crucible Skin", fileName = "CrucibleSkin")]
public sealed class CrucibleSkinSO : ScriptableObject
{
    [Header("배경 / 무대 / 타이틀")]
    [Tooltip("재련소 배경 — 전체화면 마법진 일러스트.")]
    public Sprite background;
    [Tooltip("재련소 바탕 — 좌측 대형 스테이지 패널(9-slice).")]
    public Sprite stagePanel;
    [Tooltip("재련소 모루 — 타이틀 좌측 아이콘.")]
    public Sprite anvilIcon;
    public Sprite decor1;        // 재련소 장식_1
    public Sprite diamondDecor;  // 마름모 장식
    public Sprite arrow;         // 재련소 화살표

    [Header("탭 (선택 / 미선택)")]
    public Sprite tabWeaponOn;   // 무기강화 선택
    public Sprite tabWeaponOff;  // 무기강화 미선택
    public Sprite tabRangedOn;   // 원거리 선택
    public Sprite tabRangedOff;  // 원거리 미선택

    [Header("무기 슬롯 카드")]
    [Tooltip("무기 넣는칸 테두리(9-slice).")]
    public Sprite slotFrame;
    public Sprite slotFill;      // 무기 넣는칸 바탕
    public Sprite slotFill2;     // 무기 넣는칸 바탕2
    public Sprite slotFill2Alt;  // 무기 넣는칸 바탕2_1
    public Sprite weaponExample; // 무기 예시

    [Header("강화 게이지 (무기강화)")]
    public Sprite gaugeTrack;    // 강화 게이지 검은 바탕
    public Sprite gaugeFill;     // 강화 게이지 빨강
    public Sprite gaugeFrame;    // 강화 게이지 테두리

    [Header("강화 게이지 (원거리 파츠)")]
    public Sprite rangedGaugeFill;   // 원거리 강화 바탕
    public Sprite rangedGaugeFrame;  // 원거리 강화 테두리
    public Sprite rangedGaugeMark;   // 원거리 강화 게이지 표시(마일스톤)

    [Header("정보 / 배지")]
    public Sprite infoPanel;     // 강화정보창
    public Sprite riskBadge;     // 위험 하락

    [Header("버튼")]
    public Sprite enhanceButton;      // 강화하기
    public Sprite partEnhanceButton;  // 원거리 파츠 강화하기
    public Sprite switchButton;       // 전환
    public Sprite exitButton;         // 나가기

    [Header("재화")]
    public Sprite currencySlot;  // 재화 칸
    public Sprite currencyGem;   // 재화 보석
}

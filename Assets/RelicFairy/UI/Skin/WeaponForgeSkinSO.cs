using UnityEngine;

/// <summary>
/// 무기 선택(모루) 팝업(<see cref="UI_WeaponForgePopup"/>) 아트 슬롯. 디자이너 납품(바탕화면 0_무기_260723)과 1:1.
///
/// 이 팝업은 프로시저럴이 아니라 <b>프리팹 기반</b>이라, 코드가 직렬화된 Image 참조에 스프라이트만 얹는다
/// (<see cref="UISkin.WeaponForge"/> · <see cref="ShopUIStyle.Skin"/>). 미할당/미로드면 프리팹의 기존 색이 남는다.
///
/// Addressable 키 "UI/WeaponForgeSkin"으로 1회 로드해 캐싱한다. <see cref="RefinerySkinSO"/>와 동일 관례.
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/UI/WeaponForge Skin", fileName = "WeaponForgeSkin")]
public sealed class WeaponForgeSkinSO : ScriptableObject
{
    [Header("패널")]
    [Tooltip("근/원거리 테두리 — 팝업 외곽 프레임(9-slice).")]
    public Sprite panelFrame;
    [Tooltip("테두리 추가버전 — 대체 외곽(선택).")]
    public Sprite panelFrameAlt;
    [Tooltip("근/원거리 바탕 — 카드 채움(9-slice).")]
    public Sprite cardFill;

    [Header("카드 프레임 (카테고리별)")]
    [Tooltip("근거리 — 근접 카드 테두리 라인아트.")]
    public Sprite meleeFrame;
    [Tooltip("원거리 — 원거리 카드 테두리 라인아트.")]
    public Sprite rangedFrame;

    [Header("아이콘 홀더")]
    [Tooltip("중앙 원 — 무기 아이콘 뒤 원형 받침.")]
    public Sprite iconHolder;
}

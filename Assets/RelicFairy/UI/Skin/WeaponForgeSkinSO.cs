using UnityEngine;

/// <summary>
/// 보조무기(원거리) 선택 팝업 <see cref="UI_RangedForgePopup"/> 아트 슬롯.
/// 디자이너 완성본(바탕화면 "원거리 장비 선택 팝업")과 1:1.
///
/// 미할당 슬롯은 <see cref="ShopUIStyle.Skin"/>에서 무시되어 기존 색 박스가 그대로 남는다 —
/// 아트가 0장이어도 팝업이 지금과 똑같이 동작한다.
/// Addressable 키 "UI/WeaponForgeSkin"으로 1회 로드해 캐싱한다. <see cref="RefinerySkinSO"/> 관례.
///
/// ※ 초기 시안의 "근거리/원거리 2카드 택1" 슬롯은 전부 걷어냈다 — 기획이 캐러셀로 피벗해
///   주무기는 무형검 고정 지급이 됐고, 그 시안 아트를 읽는 코드가 하나도 없었다.
/// ※ 납품본의 "전환 버튼"·"전환표시"는 기획상 불필요로 판단돼 슬롯을 두지 않는다(파일은 폴더에 남아 있다).
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/UI/WeaponForge Skin", fileName = "WeaponForgeSkin")]
public sealed class WeaponForgeSkinSO : ScriptableObject
{
    [Header("패널")]
    [Tooltip("팝업 전체 배경 — 테두리와 상·하단 문장 장식이 한 장에 들어있다. 비율(0.963)을 지켜야 한다.")]
    public Sprite panelBackground;
    [Tooltip("이름과 버튼 사이 가로 구분 장식.")]
    public Sprite divider;

    [Header("무기 아이콘 홀더 (3겹: 채움 → 아이콘 → 테두리 → 외곽선)")]
    public Sprite holderFill;
    public Sprite holderFrame;
    [Tooltip("홀더보다 살짝 큰 외곽선.")]
    public Sprite holderOutline;

    [Header("캐러셀 화살표")]
    public Sprite arrowLeft;
    public Sprite arrowRight;

    [Header("장착 버튼")]
    [Tooltip("'장착하기' 글자가 아트에 구워져 있다 — 코드 라벨을 겹치면 안 된다.")]
    public Sprite equipButton;
    [Tooltip("장착 버튼 좌측 체크 글리프.")]
    public Sprite equipGlyph;
}

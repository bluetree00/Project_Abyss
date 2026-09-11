using UnityEngine;

/// <summary>
/// 환경설정 화면(<see cref="UI_Settings"/>) 아트 슬롯. 디자이너 납품(바탕화면 「환경설정 UI」)과 1:1이다.
///
/// <para>미할당 슬롯은 호출측에서 무시되어 코드가 그린 색 박스가 그대로 남는다 —
/// 아트가 0장이어도 화면이 지금과 똑같이 동작한다.</para>
///
/// <para>⚠ 이 화면은 드롭다운 클리핑 때문에 <b>ScrollRect를 쓰지 않는다</b>.
/// 판이 커지면 내용이 넘치는 게 아니라 판 자체를 키워야 한다.</para>
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/UI/Settings Skin", fileName = "SettingsSkin")]
public sealed class SettingsSkinSO : ScriptableObject
{
    [Header("판")]
    [Tooltip("전체 배경판(1498×869). 모서리 장식이 있어 9-slice가 아니라 통짜로 늘린다.")]
    public Sprite panel;
    [Tooltip("상단 장식 — 제목 위 문양.")]
    public Sprite topOrnament;
    [Tooltip("구분 작대기 — 구역 사이 가로선.")]
    public Sprite divider;
    [Tooltip("잔장식.")]
    public Sprite ornament;

    [Header("게이지(슬라이더)")]
    public Sprite gaugeTrack;   // 게이지 바탕
    public Sprite gaugeFill;    // 게이지
    public Sprite gaugeKnob;    // 조절 박스

    [Header("드롭다운")]
    [Tooltip("접힌 상태 박스.")]
    public Sprite dropdownBox;
    [Tooltip("접힌 상태 화살표.")]
    public Sprite dropdownArrow;
    [Tooltip("펼친 목록 바탕.")]
    public Sprite dropdownListBg;
    [Tooltip("펼친 목록 안의 항목 칸.")]
    public Sprite dropdownItem;
    [Tooltip("펼친 상태 화살표.")]
    public Sprite dropdownArrowOpen;

    [Header("버튼")]
    public Sprite applyButton;    // 적용
    public Sprite cancelButton;   // 취소
    public Sprite offButton;      // 끄기(토글 OFF)
    public Sprite resetButton;    // 기본값 복원
}

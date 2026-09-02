using UnityEngine;

/// <summary>
/// 업적 목록(<see cref="AchievementListView"/> · <see cref="AchievementRowView"/>) 아트 슬롯.
/// 디자이너 납품(바탕화면 「업적UI」)과 1:1이다.
///
/// <para>미할당 슬롯은 호출측에서 무시되어 기존 색 박스가 그대로 남는다 —
/// 아트가 0장이어도 화면이 지금과 똑같이 동작한다.</para>
///
/// <para><b>행은 세 상태가 각각 다른 판</b>이다(수령 가능 / 진행 중 / 받음).
/// 색만 바꾸던 시절엔 "받을 수 있다"가 눈에 안 띄어 수령을 놓쳤다.</para>
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/UI/Achievement Skin", fileName = "AchievementSkin")]
public sealed class AchievementSkinSO : ScriptableObject
{
    [Header("행 배경 — 상태별 (모두 853×60)")]
    [Tooltip("수령 가능 — 주황 테두리. 가장 눈에 띄어야 한다.")]
    public Sprite rowClaimable;
    [Tooltip("진행 중 — 차분한 회색 판.")]
    public Sprite rowProgress;
    [Tooltip("받음 — 더 가라앉은 판. 목록에서 뒤로 물러난다.")]
    public Sprite rowClaimed;

    [Header("행 좌측 표식")]
    [Tooltip("수령 가능 ✦ (37×37)")]
    public Sprite markClaimable;
    [Tooltip("진행 중 ▶ (23×21)")]
    public Sprite markProgress;
    [Tooltip("받음 ✔ (27×20)")]
    public Sprite markClaimed;

    [Header("진척 게이지")]
    public Sprite gaugeFrame;   // 264×18
    public Sprite gaugeTrack;   // 261×14
    public Sprite gaugeFill;    // 130×14 — fillAmount로 늘어난다(가로 9-slice 필요)

    [Header("버튼")]
    [Tooltip("행의 [받기] (102×38)")]
    public Sprite claimButton;
    [Tooltip("하단 [모두 받기] (141×56)")]
    public Sprite claimAllButton;

    [Header("하단 정산 바 / 분류 칩")]
    [Tooltip("「받아갈 것이 N개 있다」 바 (853×106)")]
    public Sprite bottomBar;
    [Tooltip("분류 칩 (83×35). 선택/미선택 두 장.")]
    public Sprite chipOn;
    public Sprite chipOff;

    /// <summary>상태에 맞는 행 배경. 없으면 null → 호출측이 색 폴백을 쓴다.</summary>
    public Sprite Row(bool claimable, bool claimed)
        => claimable ? rowClaimable : claimed ? rowClaimed : rowProgress;

    /// <summary>상태에 맞는 좌측 표식.</summary>
    public Sprite Mark(bool claimable, bool claimed)
        => claimable ? markClaimable : claimed ? markClaimed : markProgress;
}

using System;
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
///
/// <para><b>제단(10-13 Artsystack)</b> — 이 스킨은 기억의 제단 창 전체가 쓴다. 판 · 해금 탭 · 해금 배너 칸과
/// 같은 그림을 쓰는 잠긴 칸(<see cref="UILockedSlot"/>) · 구역 이름 밑줄(<see cref="ZoneTitleBanner"/>)도 여기 둔다.
/// 그림은 편집 도구 「10-13 Artsystack 기억의 제단 스킨 걸기」가 건다. 비면 지금 코드 모양 그대로.</para>
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

    [Header("업적 아이콘 고리(10-13 Artsystack)")]
    [Tooltip("행 기호 열의 아이콘 고리(icon_slot_active). 무채 · 얇게 깎은 뒤 코드가 금선으로 칠한다. 비면 아이콘만 그린다.")]
    [SerializeField] private Sprite iconRing;

    [Header("하단 정산 바 / 분류 칩")]
    [Tooltip("「받아갈 것이 N개 있다」 바 (853×106)")]
    public Sprite bottomBar;
    [Tooltip("분류 칩 (83×35). 선택/미선택 두 장.")]
    public Sprite chipOn;
    public Sprite chipOff;

    [Header("제단(판 · 해금 탭) — 10-13 Artsystack")]
    [Tooltip("창 판(InfoBox 남색 재색칠 · 9-slice). 코드 단색 판 위에 덮고 금 가는 테는 그대로 둔다.")]
    [SerializeField] private Sprite altarWindow;
    [Tooltip("머리 구분선(splitt_divider) — 무채 + 잉크 30%. 오른쪽 끝 마름모 = 9-slice 오른쪽 경계.")]
    [SerializeField] private Sprite altarHeaderRule;
    [Tooltip("탭 선택 밑줄(selected_line) — 무채 + 금. Simple(가운데 마름모가 늘지 않게).")]
    [SerializeField] private Sprite altarTabUnderline;
    [Tooltip("탭 새 소식 점(slider_scroller) — 원색 금 마름모.")]
    [SerializeField] private Sprite altarNewsDot;
    [Tooltip("닫기 버튼 판(slot_short_inactive 남색 재색칠 · 9-slice).")]
    [SerializeField] private Sprite altarCloseButton;
    [Tooltip("노드 고리 작음 · 보통(icon_slot_active) — 무채 뒤 코드가 상태색으로 칠한다. 비면 코드 고리.")]
    [SerializeField] private Sprite altarNodeRing;
    [Tooltip("노드 고리 열쇠(skill_slot · 눈금 4) — 안이 차 있어 코드 판 아래에 깔고 판을 줄인다. 비면 코드 고리.")]
    [SerializeField] private Sprite altarKeyRing;
    [Tooltip("잠김(lock_1) — 조건 잠긴 노드 오른쪽 아래 배지 · 잠긴 칸 자물쇠. 잉크.")]
    [SerializeField] private Sprite altarLock;
    [Tooltip("열림(lock_3) — 해금 배너 배지. 금.")]
    [SerializeField] private Sprite altarUnlock;
    [Tooltip("다음 목표(star_3) — 노드 오른쪽 위 배지. 금.")]
    [SerializeField] private Sprite altarNext;
    [Tooltip("상세 판(description_box_05 남색 재색칠 · 9-slice).")]
    [SerializeField] private Sprite altarDetailPanel;
    [Tooltip("상세 판 구분선(Line 1) — 무채 + 잉크 35%.")]
    [SerializeField] private Sprite altarDetailRule;
    [Tooltip("해금 배너 띠(message_slot_1) — 무채 + 금 45%. 제단 화면의 후광은 이 한 곳.")]
    [SerializeField] private Sprite altarBannerStrip;
    [Tooltip("구역 이름(ZoneTitleBanner) 밑줄(direction_line) — 무채 + 잉크 50%.")]
    [SerializeField] private Sprite zoneTitleRule;

    [Header("제단 갈래 문장 — 갈래 색으로 칠한다")]
    [SerializeField] private Sprite emblemRune;
    [SerializeField] private Sprite emblemCovenant;
    [SerializeField] private Sprite emblemWeapon;
    [SerializeField] private Sprite emblemJourney;
    [SerializeField] private Sprite emblemMemory;

    [Header("제단 노드 아이콘 — 노드 정의가 코드라 id로 찾는다(편집 도구가 채운다)")]
    [SerializeField] private NodeIconEntry[] altarNodeIcons;

    /// <summary>업적 아이콘 고리. 없으면 null.</summary>
    public Sprite IconRing => iconRing;

    // 제단 — 모두 없으면 null(호출측이 지금 코드 모양으로 둔다)
    public Sprite AltarWindow       => altarWindow;
    public Sprite AltarHeaderRule   => altarHeaderRule;
    public Sprite AltarTabUnderline => altarTabUnderline;
    public Sprite AltarNewsDot      => altarNewsDot;
    public Sprite AltarCloseButton  => altarCloseButton;
    public Sprite AltarNodeRing     => altarNodeRing;
    public Sprite AltarKeyRing      => altarKeyRing;
    public Sprite AltarLock         => altarLock;
    public Sprite AltarUnlock       => altarUnlock;
    public Sprite AltarNext         => altarNext;
    public Sprite AltarDetailPanel  => altarDetailPanel;
    public Sprite AltarDetailRule   => altarDetailRule;
    public Sprite AltarBannerStrip  => altarBannerStrip;
    public Sprite ZoneTitleRule     => zoneTitleRule;

    /// <summary>상태에 맞는 행 배경. 없으면 null → 호출측이 색 폴백을 쓴다.</summary>
    public Sprite Row(bool claimable, bool claimed)
        => claimable ? rowClaimable : claimed ? rowClaimed : rowProgress;

    /// <summary>상태에 맞는 좌측 표식.</summary>
    public Sprite Mark(bool claimable, bool claimed)
        => claimable ? markClaimable : claimed ? markClaimed : markProgress;

    /// <summary>제단 갈래 문장. 없으면 null.</summary>
    public Sprite BranchEmblem(AltarBranch branch) => branch switch
    {
        AltarBranch.Rune     => emblemRune,
        AltarBranch.Covenant => emblemCovenant,
        AltarBranch.Weapon   => emblemWeapon,
        AltarBranch.Journey  => emblemJourney,
        _                    => emblemMemory,
    };

    /// <summary>제단 노드 아이콘(노드 id). 없으면 null. 30줄이라 그냥 훑는다.</summary>
    public Sprite NodeIcon(string nodeId)
    {
        if (altarNodeIcons == null || string.IsNullOrEmpty(nodeId)) return null;
        foreach (var e in altarNodeIcons)
            if (e.Id == nodeId) return e.Icon;
        return null;
    }

    /// <summary>제단 노드 아이콘 한 줄 — 노드 id · 흰 아이콘.</summary>
    [Serializable]
    private struct NodeIconEntry
    {
        [SerializeField] private string id;
        [SerializeField] private Sprite icon;

        public string Id   => id;
        public Sprite Icon => icon;
    }
}

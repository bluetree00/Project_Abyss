using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 저장 슬롯 카드 한 장.
/// 세이브 데이터가 있으면 채워진 상태(filledRoot), 없으면 빈 상태(emptyRoot) 표시.
/// </summary>
public class UI_SaveSlotCard : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────
    // Constants
    // ─────────────────────────────────────────────────────────

    // 카드 글 두 줄(10-11 리서치 C §6) — DNF 36 + 가짜 굵게(잉크 32) → Noto 400 · 24. 줄 간격 ≈ 1.4em:
    // 세로 그룹이 각 줄을 글자 높이(Noto 줄높이 ≈ 1.45em)로 잡으므로 간격 -1이면 줄과 줄 사이가 24 × 1.4 남짓이다.
    private const float InfoSize    = 24f;
    private const float InfoSpacing = -1f;

    // ─────────────────────────────────────────────────────────
    // SerializeField
    // ─────────────────────────────────────────────────────────

    [Header("상태 루트")]
    [SerializeField] private GameObject filledRoot;
    [SerializeField] private GameObject emptyRoot;

    [Header("채워진 상태 — 텍스트")]
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text progressText;
    [SerializeField] private TMP_Text retryText;
    [SerializeField] private TMP_Text savedAtText;

    [Header("버튼")]
    [SerializeField] private Button   startButton;
    [SerializeField] private TMP_Text startButtonText;
    [SerializeField] private Button   deleteButton;

    // ─────────────────────────────────────────────────────────
    // Private
    // ─────────────────────────────────────────────────────────

    private int  _slotIndex;
    private bool _hasSave;
    private Action<int, bool> _onStart;
    private Action<int>       _onDelete;

    // ─────────────────────────────────────────────────────────
    // Lifecycle
    // ─────────────────────────────────────────────────────────

    private void Awake()
    {
        startButton.onClick.AddListener(OnClickStart);
        if (deleteButton != null)
            deleteButton.onClick.AddListener(OnClickDelete);
    }

    // ─────────────────────────────────────────────────────────
    // Public Methods
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 카드를 초기화한다.
    /// onStart(slotIndex, hasSave) — 게임 시작 클릭 시 호출.
    /// onDelete(slotIndex) — 삭제 클릭 시 호출 (hasSave=true 일 때만 버튼 활성).
    /// </summary>
    public void Setup(RunSaveData data, int slotIndex, Action<int, bool> onStart, Action<int> onDelete = null)
    {
        _slotIndex = slotIndex;
        _onStart   = onStart;
        _onDelete  = onDelete;
        _hasSave   = data?.hasActiveRun == true;

        filledRoot.SetActive(_hasSave);
        emptyRoot.SetActive(!_hasSave);
        if (deleteButton != null) deleteButton.gameObject.SetActive(_hasSave);
        if (startButtonText != null) startButtonText.text = _hasSave ? "이어하기" : "새 게임";

        // 빈 슬롯 가운데 글이 버튼과 같은 「새 게임」이라 두 번 읽혔다(09-28 UI 전수).
        if (!_hasSave && emptyRoot != null && emptyRoot.transform.Find("Txt_Empty") is Transform empty
            && empty.TryGetComponent<TMP_Text>(out var emptyText))
            emptyText.text = "빈 슬롯";

        if (!_hasSave) return;

        // 슬롯에 필요한 정보는 "얼마나 오래" "몇 번 시도했나" 둘뿐이다.
        // 챕터·HP·아이템·시너지·골드·저장시각은 인게임에서 다 보이므로 카드에선 뺀다(정보 과밀 제거).
        //
        // 예외 — 허브(베이스캠프) 세이브는 아직 플레이 시간이 안 쌓였을 수 있어 "--"만 뜨면
        // 빈 슬롯처럼 보인다. 어디서 이어지는지를 대신 알려준다.
        if (progressText != null)
            progressText.text = (data.isInStartRoom && data.playSeconds <= 0)
                ? "베이스캠프"
                : FormatPlayTime(data.playSeconds);
        if (retryText    != null) retryText.text    = $"시도 {Mathf.Max(1, data.retryCount)}회";
        UITheme.StyleThinText(progressText, UIWeight.Regular, InfoSize, progressText != null ? progressText.color : Color.white);
        UITheme.StyleThinText(retryText,    UIWeight.Regular, InfoSize, retryText != null ? retryText.color : Color.white);

        // 남는 슬롯은 숨겨 카드가 비대해지지 않게 한다.
        if (nameText    != null) nameText.gameObject.SetActive(false);
        if (savedAtText != null) savedAtText.gameObject.SetActive(false);

        // 두 줄만 남으면 세로 그룹의 「늘려 채우기」가 카드 높이를 반씩 나눠 두 줄이 멀리 떨어졌다 — 가운데로 모은다(09-28 UI 전수).
        // 간격은 18 → 줄 간격 1.4em(10-11) — 한 덩어리 정보로 읽히게.
        if (filledRoot.TryGetComponent<VerticalLayoutGroup>(out var vlg))
        {
            vlg.childForceExpandHeight = false;
            vlg.childAlignment         = TextAnchor.MiddleCenter;
            vlg.spacing                = InfoSpacing;
        }
    }

    // ─────────────────────────────────────────────────────────
    // Private Methods — Format
    // ─────────────────────────────────────────────────────────

    /// <summary>누적 플레이 시간 — 1시간 미만은 분:초, 넘으면 시간:분.</summary>
    private static string FormatPlayTime(int seconds)
    {
        if (seconds <= 0) return "플레이 시간 --";

        var t = TimeSpan.FromSeconds(seconds);
        return t.TotalHours >= 1d
            ? $"플레이 시간 {(int)t.TotalHours}시간 {t.Minutes}분"
            : $"플레이 시간 {t.Minutes}분 {t.Seconds}초";
    }

    // ─────────────────────────────────────────────────────────
    // Event Handlers
    // ─────────────────────────────────────────────────────────

    private void OnClickStart()  => _onStart?.Invoke(_slotIndex, _hasSave);
    private void OnClickDelete() => _onDelete?.Invoke(_slotIndex);
}

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

        // 남는 슬롯은 숨겨 카드가 비대해지지 않게 한다.
        if (nameText    != null) nameText.gameObject.SetActive(false);
        if (savedAtText != null) savedAtText.gameObject.SetActive(false);
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

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

        if (nameText     != null) nameText.text     = FormatName(data);
        if (progressText != null) progressText.text = FormatProgress(data);
        if (retryText    != null) retryText.text    = FormatStats(data);
        if (savedAtText  != null) savedAtText.text  = FormatSavedAt(data.savedAt);
    }

    // ─────────────────────────────────────────────────────────
    // Private Methods — Format
    // ─────────────────────────────────────────────────────────

    private static string FormatName(RunSaveData d)
    {
        return string.IsNullOrEmpty(d.characterName) ? "???" : d.characterName;
    }

    private static string FormatProgress(RunSaveData d)
    {
        // "챕터 N | HP X / Y"
        int chapter = Mathf.Max(1, d.chapter);
        int hp      = Mathf.Clamp(d.currentHp, 0, d.maxHp);
        int maxHp   = Mathf.Max(1, d.maxHp);
        return $"챕터 {chapter}  |  HP {hp} / {maxHp}";
    }

    private static string FormatStats(RunSaveData d)
    {
        // "클리어 N방  아이템 N  시너지 N  골드 N"
        return $"클리어 {d.roomClearCount}방   아이템 {d.itemCount}   시너지 {d.synergyCount}   골드 {d.runGold}";
    }

    private static string FormatSavedAt(string iso8601)
    {
        if (string.IsNullOrEmpty(iso8601)) return string.Empty;

        if (DateTime.TryParse(iso8601, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
        {
            var local = dt.ToLocalTime();
            return local.ToString("yyyy-MM-dd HH:mm");
        }

        return iso8601;
    }

    // ─────────────────────────────────────────────────────────
    // Event Handlers
    // ─────────────────────────────────────────────────────────

    private void OnClickStart()  => _onStart?.Invoke(_slotIndex, _hasSave);
    private void OnClickDelete() => _onDelete?.Invoke(_slotIndex);
}

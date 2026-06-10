using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 저장 슬롯 선택 패널.
/// UI_Lobby에 SerializeField로 참조되며, Btn_StartRun 클릭 시 Open/Close.
/// 슬롯 선택 결과:
///   hasSave=true  → 이어하기 (RequestRestoreRun, 슬롯 인덱스 전달)
///   hasSave=false → 새로하기 (RequestStartRun, 스타트 방에서 인게임 선택)
/// </summary>
public class UI_SaveSlotPanel : UI_Base
{
    // ─────────────────────────────────────────────────────────
    // SerializeField
    // ─────────────────────────────────────────────────────────

    [Header("슬롯 카드 (0, 1, 2)")]
    [SerializeField] private UI_SaveSlotCard[] slotCards;

    [Header("뒤로 가기")]
    [SerializeField] private Button btnBack;

    [Header("삭제 확인 다이얼로그")]
    [SerializeField] private GameObject confirmRoot;
    [SerializeField] private Button     btnConfirmDelete;
    [SerializeField] private Button     btnCancelDelete;

    // ─────────────────────────────────────────────────────────
    // Events
    // ─────────────────────────────────────────────────────────

    public event System.Action OnClosed;

    // ─────────────────────────────────────────────────────────
    // Private
    // ─────────────────────────────────────────────────────────

    private int _pendingDeleteSlot = -1;

    // ─────────────────────────────────────────────────────────
    // Lifecycle
    // ─────────────────────────────────────────────────────────

    public override void Init()
    {
        base.Init();

        if (btnBack != null)
            btnBack.onClick.AddListener(Close);

        if (btnConfirmDelete != null)
            btnConfirmDelete.onClick.AddListener(OnClickConfirmDelete);

        if (btnCancelDelete != null)
            btnCancelDelete.onClick.AddListener(OnClickCancelDelete);

        if (confirmRoot != null)
            confirmRoot.SetActive(false);
    }

    // ─────────────────────────────────────────────────────────
    // Public Methods
    // ─────────────────────────────────────────────────────────

    /// <summary>패널을 열고 슬롯 카드를 최신 세이브 데이터로 갱신한다.</summary>
    public void Open()
    {
        HideConfirm();
        RefreshAllCards();
        gameObject.SetActive(true);
    }

    public override void Close()
    {
        HideConfirm();
        gameObject.SetActive(false);
        OnClosed?.Invoke();
    }

    // ─────────────────────────────────────────────────────────
    // Private Methods — Cards
    // ─────────────────────────────────────────────────────────

    private void RefreshAllCards()
    {
        var rpm = RunProgressManager.Instance;
        for (int i = 0; i < slotCards.Length; i++)
        {
            var data = (rpm != null && i < RunProgressManager.SlotCount)
                ? rpm.Saves[i]
                : null;
            slotCards[i].Setup(data, i, OnSlotStartClicked, OnSlotDeleteClicked);
        }
    }

    private void RefreshCard(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= slotCards.Length) return;
        var rpm  = RunProgressManager.Instance;
        var data = (rpm != null && slotIndex < RunProgressManager.SlotCount)
            ? rpm.Saves[slotIndex]
            : null;
        slotCards[slotIndex].Setup(data, slotIndex, OnSlotStartClicked, OnSlotDeleteClicked);
    }

    // ─────────────────────────────────────────────────────────
    // Private Methods — Confirm Dialog
    // ─────────────────────────────────────────────────────────

    private void HideConfirm()
    {
        _pendingDeleteSlot = -1;
        if (confirmRoot != null) confirmRoot.SetActive(false);
    }

    // ─────────────────────────────────────────────────────────
    // Event Handlers
    // ─────────────────────────────────────────────────────────

    private void OnSlotStartClicked(int slotIndex, bool hasSave)
    {
        var rpm = RunProgressManager.Instance;
        if (rpm != null) rpm.ActiveSlotIndex = slotIndex;

        Close();

        if (hasSave)
            AppBootstrapper.Instance?.RequestRestoreRun(onFailed: Open);
        else
            AppBootstrapper.Instance?.RequestStartRun();
    }

    private void OnSlotDeleteClicked(int slotIndex)
    {
        _pendingDeleteSlot = slotIndex;
        if (confirmRoot != null) confirmRoot.SetActive(true);
    }

    private void OnClickConfirmDelete()
    {
        int slot = _pendingDeleteSlot;
        HideConfirm();
        DeleteSlotAsync(slot).Forget();
    }

    private void OnClickCancelDelete() => HideConfirm();

    private async UniTaskVoid DeleteSlotAsync(int slotIndex)
    {
        var rpm = RunProgressManager.Instance;
        if (rpm == null || slotIndex < 0) return;

        await rpm.ClearAsync(slotIndex);
        RefreshCard(slotIndex);
    }
}

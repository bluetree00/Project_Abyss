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
    }

    // ─────────────────────────────────────────────────────────
    // Private Methods — Cards
    // ─────────────────────────────────────────────────────────

    private void RefreshAllCards()
    {
        var rpm   = RunProgressManager.Instance;
        var local = GetLocalSave(rpm);
        for (int i = 0; i < slotCards.Length; i++)
            slotCards[i].Setup(ResolveSlotData(rpm, local, i), i, OnSlotStartClicked, OnSlotDeleteClicked);
    }

    private void RefreshCard(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= slotCards.Length) return;
        var rpm   = RunProgressManager.Instance;
        var local = GetLocalSave(rpm);
        slotCards[slotIndex].Setup(ResolveSlotData(rpm, local, slotIndex), slotIndex, OnSlotStartClicked, OnSlotDeleteClicked);
    }

    /// <summary>PR1: 진행 중 런은 로컬(단일 슬롯)이 권위. 해당 슬롯은 로컬 세이브로 표시한다.</summary>
    private static RunSaveData GetLocalSave(RunProgressManager rpm)
    {
        var local = rpm != null ? rpm.LoadLocalRun() : null;
        return local != null && local.hasActiveRun ? local : null;
    }

    private static RunSaveData ResolveSlotData(RunProgressManager rpm, RunSaveData local, int i)
    {
        if (local != null && local.slotIndex == i) return local;
        return (rpm != null && i < RunProgressManager.SlotCount) ? rpm.Saves[i] : null;
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

        // 로컬 런 세이브가 이 슬롯이면 함께 폐기 (PR1: 로컬이 진행 중 런의 권위)
        var local = GetLocalSave(rpm);
        if (local != null && local.slotIndex == slotIndex)
            rpm.ClearLocalRun();

        await rpm.ClearAsync(slotIndex);
        RefreshCard(slotIndex);
    }
}

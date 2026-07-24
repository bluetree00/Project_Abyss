using Cysharp.Threading.Tasks;
using TMPro;
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
    // Constants — 확인 다이얼로그 규격
    // ─────────────────────────────────────────────────────────

    // 판 세로 300 기준 — 메시지는 위 절반(-10~+86), 버튼은 아래(-119~-37)에 앉아 서로 닿지 않는다.
    private const float DialogW    = 640f;
    private const float DialogH    = 300f;
    private const float DialogMsgY = 38f;    // 판 중앙 기준 메시지 중심
    private const float BtnW       = 152f;   // Yes/No 아트 비율 ≈1.85 유지
    private const float BtnH       = 82f;
    private const float BtnGapHalf = 92f;    // 판 중앙에서 각 버튼 중심까지
    private const float BtnY       = 72f;    // 판 하단에서 버튼 중심까지

    private static readonly Color VeilColor   = new(0f, 0f, 0f, 0.82f);
    private static readonly Color DialogFill  = new(0.06f, 0.05f, 0.09f, 0.985f);
    private static readonly Color DialogEdge  = new(0.52f, 0.40f, 0.20f, 1f);

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

        NormalizeConfirmLayout();

        if (confirmRoot != null)
            confirmRoot.SetActive(false);
    }

    // ─────────────────────────────────────────────────────────
    // Public Methods
    // ─────────────────────────────────────────────────────────

    /// <summary>패널을 열고 슬롯 카드를 최신 세이브 데이터로 갱신한다.</summary>
    public override void Open()
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
            slotCards[i].Setup(ResolveSlotData(rpm, i), i, OnSlotStartClicked, OnSlotDeleteClicked);
    }

    private void RefreshCard(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= slotCards.Length) return;
        var rpm = RunProgressManager.Instance;
        slotCards[slotIndex].Setup(ResolveSlotData(rpm, slotIndex), slotIndex, OnSlotStartClicked, OnSlotDeleteClicked);
    }

    /// <summary>진행 중 런은 슬롯별 로컬 파일이 단독 권위. 활성 로컬 런이 있으면 그것을, 없으면 빈 슬롯(null).</summary>
    private static RunSaveData ResolveSlotData(RunProgressManager rpm, int i)
    {
        if (rpm == null || i < 0 || i >= RunProgressManager.SlotCount) return null;
        var local = rpm.LoadLocalRun(i);
        return local != null && local.hasActiveRun ? local : null;
    }

    // ─────────────────────────────────────────────────────────
    // Private Methods — Confirm Dialog
    // ─────────────────────────────────────────────────────────

    private void HideConfirm()
    {
        _pendingDeleteSlot = -1;
        if (confirmRoot != null) confirmRoot.SetActive(false);
    }

    /// <summary>
    /// 삭제 확인 다이얼로그를 "팝업답게" 잡는다.
    ///
    /// 프리팹에는 판(배경 상자) 없이 78pt 제목과 원본 크기 그대로의 Yes/No 아트만 화면 한복판에
    /// 흩어져 있었다 — 슬롯 카드가 그대로 비쳐 보여 무엇을 묻는 창인지 읽히지 않았다.
    /// 크기·위치가 코드에서 오는 이상 제약도 코드가 쥐고 있어야 다시 어긋나지 않으므로,
    /// 여기서 어두운 막 + 가운데 판 + 메시지 + 버튼 2개로 매번 재구성한다.
    /// 판은 없으면 만들고(1회), 이미 있으면 그대로 쓴다.
    /// </summary>
    private void NormalizeConfirmLayout()
    {
        if (confirmRoot == null) return;

        // 1) 루트 = 전체를 덮는 어두운 막(뒤쪽 클릭 차단)
        if (confirmRoot.transform is RectTransform rootRT)
        {
            rootRT.anchorMin = Vector2.zero;
            rootRT.anchorMax = Vector2.one;
            rootRT.offsetMin = rootRT.offsetMax = Vector2.zero;
        }
        var veil = confirmRoot.GetComponent<Image>();
        if (veil == null) veil = confirmRoot.AddComponent<Image>();
        veil.sprite        = null;
        veil.color         = VeilColor;
        veil.raycastTarget = true;

        // 2) 판 — 메시지·버튼이 앉을 상자. 이미 만들어 뒀으면 재사용.
        var panelTf = confirmRoot.transform.Find("Panel");
        RectTransform panel;
        if (panelTf != null)
        {
            panel = (RectTransform)panelTf;
        }
        else
        {
            var go = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            panel = (RectTransform)go.transform;
            panel.SetParent(confirmRoot.transform, false);
            var edge = go.GetComponent<Image>();
            edge.color = DialogEdge;

            var innerGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            var inner = (RectTransform)innerGo.transform;
            inner.SetParent(panel, false);
            inner.anchorMin = Vector2.zero;
            inner.anchorMax = Vector2.one;
            inner.offsetMin = new Vector2(2f, 2f);
            inner.offsetMax = new Vector2(-2f, -2f);
            innerGo.GetComponent<Image>().color = DialogFill;
        }
        Center(panel, Vector2.zero, new Vector2(DialogW, DialogH));
        panel.SetAsFirstSibling();   // 메시지·버튼보다 뒤에 깔린다

        // 3) 메시지 — 판 위쪽. 78pt는 화면을 가로지르므로 판에 맞춰 줄인다.
        var msg = confirmRoot.GetComponentInChildren<TMP_Text>(true);
        if (msg != null)
        {
            msg.fontSize          = 26f;
            msg.alignment         = TextAlignmentOptions.Center;
            msg.textWrappingMode  = TextWrappingModes.Normal;
            msg.overflowMode      = TextOverflowModes.Truncate;
            Center(msg.rectTransform, new Vector2(0f, DialogMsgY), new Vector2(DialogW - 60f, 96f));
        }

        // 4) 버튼 — 아트 원본(539×291 / 487×269)이 그대로 깔려 있어 화면을 뒤덮었다.
        //    비율을 지키면서 판 안에 들어오는 크기로 고정한다.
        PlaceConfirmButton(btnConfirmDelete, -BtnGapHalf);
        PlaceConfirmButton(btnCancelDelete,   BtnGapHalf);
    }

    private void PlaceConfirmButton(Button btn, float x)
    {
        if (btn == null) return;
        Center((RectTransform)btn.transform, new Vector2(x, -DialogH * 0.5f + BtnY), new Vector2(BtnW, BtnH));
        if (btn.TryGetComponent<Image>(out var img)) img.preserveAspect = true;
    }

    /// <summary>부모 중앙 기준으로 위치·크기를 못 박는다(앵커가 어떻게 authoring 돼 있든 동일 결과).</summary>
    private static void Center(RectTransform rt, Vector2 pos, Vector2 size)
    {
        var half = new Vector2(0.5f, 0.5f);
        rt.anchorMin = rt.anchorMax = rt.pivot = half;
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
    }

    // ─────────────────────────────────────────────────────────
    // Event Handlers
    // ─────────────────────────────────────────────────────────

    private void OnSlotStartClicked(int slotIndex, bool hasSave)
    {
        var rpm = RunProgressManager.Instance;
        if (rpm != null) rpm.ActiveSlotIndex = slotIndex;

        Close();

        Managers.Sound.FadeOutBgmAsync().Forget();

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
        DeleteSlot(slot);
    }

    private void OnClickCancelDelete() => HideConfirm();

    private void DeleteSlot(int slotIndex)
    {
        var rpm = RunProgressManager.Instance;
        if (rpm == null || slotIndex < 0) return;

        // 이 슬롯의 로컬 런 세이브만 폐기(다른 슬롯 무영향). 로컬이 단독 권위.
        rpm.ResetSlot(slotIndex);   // 슬롯 삭제 = 이 슬롯으로 다시 시작하면 초회부터
        RefreshCard(slotIndex);
    }
}

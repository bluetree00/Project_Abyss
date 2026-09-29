using System;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;

/// <summary>
/// BaseCamp 배치용 유물 각성 제단.
/// 범위에 들어와 F 키를 누르면 UI_AwakeningPanel 팝업을 연다.
/// SpawnAt()으로 코드 스폰하거나 씬에 직접 배치 가능.
/// </summary>
[RequireComponent(typeof(Collider))]
public class WorldAwakeningAltar : MonoBehaviour
{
    // ── 직렬화 필드 ──────────────────────────────────────────────────────
    [Header("월드 텍스트")]
    [SerializeField] private TMP_FontAsset worldTextFont;
    [SerializeField] private float textHeight = 1.2f;
    [SerializeField] private float textSize   = 3f;
    [Tooltip("이름판·[F] 안내를 옮긴다(이 오브젝트 로컬) — 베이스캠프 기억의 제단은 받침 위 글자가 제단 기둥 모형에 가렸다(ae·98 09-28)")]
    [SerializeField] private Vector3 labelOffset;

    // ── 상수 ─────────────────────────────────────────────────────────────
    private const float PromptOffsetY = 1.8f;
    private const float LabelRefreshInterval = 1f;   // 이름표가 보이는 동안 정수·열 수 있는 수를 다시 읽는 간격(실시간 초)
    private const string LabelTitle = "기억의 제단";   // 여는 창이 기억의 제단(UI_AwakeningPanel) — 옛 이름 「유물 각성」이 베이스캠프 구역 이름과 어긋났다(09-28)

    // ── 비공개 필드 ──────────────────────────────────────────────────────
    private bool         _playerInRange;
    private bool         _opening;
    private Transform    _camTransform;
    private TextMeshPro  _worldText;
    private GameObject   _promptGo;
    private TextMeshPro  _promptText;
    private int          _labelEssence    = -1;
    private int          _labelAffordable = -1;
    private float        _nextLabelCheck;

    // ── Lifecycle ─────────────────────────────────────────────────────────

    private void Start()
    {
        _camTransform = Camera.main != null ? Camera.main.transform : null;
        CreateWorldText();
        CreatePrompt();
        BaseCampLabelRule.Register(transform);   // 이름표는 가까이 간 곳 하나만(2차 개편 09-29)
    }

    private void Update()
    {
        BillboardTexts();

        if (_opening || !_playerInRange) return;
        if (UIInputGate.Blocked) return;
        if (Input.GetKeyDown(KeyCode.F))
            OpenAsync(this.GetCancellationTokenOnDestroy()).Forget();
    }

    private void OnDestroy() => BaseCampLabelRule.Unregister(transform);

    // ── Public 팩토리 ─────────────────────────────────────────────────────

    public static WorldAwakeningAltar SpawnAt(Vector3 position)
    {
        var go = new GameObject("AwakeningAltar");
        go.transform.position = position;

        var col = go.AddComponent<SphereCollider>();
        col.isTrigger = true;
        col.radius = 2f;

        return go.AddComponent<WorldAwakeningAltar>();
    }

    // ── 내부 ──────────────────────────────────────────────────────────────

    private async UniTaskVoid OpenAsync(System.Threading.CancellationToken ct)
    {
        _opening = true;
        ShowPrompt(false);

        try
        {
            var panel = await Managers.UI.ShowPopupUIAndGetAsync<UI_AwakeningPanel>();
            if (panel != null)
            {
                // 가드는 팝업이 <b>닫힐 때까지</b> 유지한다. 열리자마자 풀면 F 연타에
                // 같은 팝업이 스택에 겹쳐 쌓여 한 번 닫아도 잔재가 남는다.
                panel.OnClosed += HandlePanelClosed;   // 팝업은 닫힐 때 파괴되므로 해제는 불필요
                return;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Debug.LogWarning($"[WorldAwakeningAltar] 팝업 로드 실패: {e.Message}");
        }

        HandlePanelClosed();
    }

    private void HandlePanelClosed()
    {
        // 팝업의 OnDestroy가 이 이벤트를 쏘므로 씬 언로드/종료 때도 불린다.
        // 그때는 제단이 이미 파괴돼 있을 수 있어, 그대로 진행하면 MissingReferenceException 이 난다.
        if (this == null) return;

        _opening = false;
        if (_playerInRange) ShowPrompt(true);
    }

    private void BillboardTexts()
    {
        if (_camTransform == null)
        {
            // 시작 때 카메라가 아직 없었으면(테스트 허브 → 베이스캠프 등) 여기서 다시 잡는다 — Start 한 번만 잡으면 라벨이 영영 안 돈다.
            var cam = Camera.main;
            if (cam == null) return;
            _camTransform = cam.transform;
        }
        if (_worldText != null)
        {
            _worldText.transform.rotation = _camTransform.rotation;
            bool show = BaseCampLabelRule.IsShown(transform);
            if (_worldText.enabled != show) _worldText.enabled = show;
            if (show && Time.unscaledTime >= _nextLabelCheck) RefreshLabel();
        }
        if (_promptGo != null && _promptGo.activeSelf)
            _promptGo.transform.rotation = _camTransform.rotation;
    }

    /// <summary>
    /// 이름 + 지금 할 수 있는 것 한 줄 — 이름만 말하던 이름표가 「무엇을 할 수 있나」를 말한다(2차 개편, 유도설계 A-2).
    /// 값이 바뀔 때만 글자를 다시 만든다.
    /// </summary>
    private void RefreshLabel()
    {
        _nextLabelCheck = Time.unscaledTime + LabelRefreshInterval;
        int essence    = MemoryAltarService.Essence;
        int affordable = MemoryAltarService.AffordableCount();
        if (essence == _labelEssence && affordable == _labelAffordable) return;

        _labelEssence    = essence;
        _labelAffordable = affordable;
        _worldText.text = affordable > 0
            ? $"{LabelTitle}\n<size=55%><color={UIPalette.GoldHex}>열 수 있는 것 {affordable}</color> · 정수 {essence:N0}</size>"
            : $"{LabelTitle}\n<size=55%>정수 {essence:N0}</size>";
    }

    private void CreateWorldText()
    {
        var go = new GameObject("AltarLabel");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.up * textHeight + labelOffset;

        _worldText = go.AddComponent<TextMeshPro>();
        if (worldTextFont != null) _worldText.font = worldTextFont;
        _worldText.text = LabelTitle;
        _worldText.fontSize = textSize;
        _worldText.alignment = TextAlignmentOptions.Center;
        _worldText.color = new Color(0.9f, 0.7f, 0.2f);
        _worldText.textWrappingMode = TextWrappingModes.NoWrap;
        _worldText.sortingOrder = UISortingOrder.WorldLabel;
        TMPOutlineHelper.ApplySoftShadow(_worldText);
    }

    private void CreatePrompt()
    {
        _promptGo = new GameObject("InteractPrompt");
        _promptGo.transform.SetParent(transform, false);
        _promptGo.transform.localPosition = Vector3.up * PromptOffsetY + labelOffset;   // 이름판과 같이 — 「[F]」도 제단 모형에 가렸다(98 09-28)

        _promptText = _promptGo.AddComponent<TextMeshPro>();
        if (worldTextFont != null) _promptText.font = worldTextFont;
        _promptText.fontSize = 4f;
        _promptText.alignment = TextAlignmentOptions.Center;
        _promptText.color = Color.white;
        _promptText.textWrappingMode = TextWrappingModes.NoWrap;
        _promptText.sortingOrder = UISortingOrder.WorldPrompt;
        TMPOutlineHelper.ApplySoftShadow(_promptText);
        _promptText.text = $"<color={UIPalette.GoldHex}>[F]</color> 정수로 해금";   // 「각성 관리」는 옛 이름 — 무엇을 하는 곳인지 동사로(2차 개편 09-29)

        _promptGo.SetActive(false);
    }

    private void ShowPrompt(bool show) => _promptGo?.SetActive(show);

    private void OnTriggerEnter(Collider other)
    {
        if (!IsPlayer(other)) return;
        _playerInRange = true;
        ShowPrompt(true);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!IsPlayer(other)) return;
        _playerInRange = false;
        ShowPrompt(false);
    }

    private static bool IsPlayer(Collider col)
        => col.GetComponentInParent<PlayerController>() != null;
}

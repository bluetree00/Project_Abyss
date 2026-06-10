using Cysharp.Threading.Tasks;
using System.Threading;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 스타트 방 캐릭터 진열대.
/// WispController가 트리거에 진입하면 상호작용 프롬프트를 표시하고
/// F키 입력 시 캐릭터 정보 팝업을 표시한다. 팝업에서 선택 확정 시 캐릭터를 등록한다.
/// </summary>
[RequireComponent(typeof(Collider))]
public class CharacterDisplayStand : MonoBehaviour, IWispInteractable
{
    [Header("Character Data")]
    [SerializeField] private CharacterData characterData;
    [SerializeField] private string characterPrefabKey = "Knight";

    [Header("Relic")]
    [SerializeField, Tooltip("유물 클래스. 할당 시 선택 확정 후 Loadout.Relic으로 전달된다(없으면 기존 캐릭터 경로 유지).")]
    private RelicClassSO relicClass;

    [Header("Display")]
    [SerializeField, Tooltip("진열 캐릭터가 바라볼 Y 회전")]
    private float displayRotationY = 180f;
    [SerializeField, Tooltip("전시 캐릭터 Y 오프셋")]
    private float displayOffsetY = 0f;

    [Header("Interaction")]
    [SerializeField, Tooltip("근접 시 활성화할 [F] 상호작용 프롬프트 오브젝트 (선택)")]
    private GameObject interactPrompt;

    private bool _selected;
    private GameObject _displayInstance;
    private WispController _nearbyWisp;

    private bool _popupOpen;
    private GameObject _popupGO;
    private WispController _pendingWisp;

    // ── Lifecycle ───────────────────────────────────────────────

    private void Awake()
    {
        var col = GetComponent<Collider>();
        col.isTrigger = true;
        if (col is BoxCollider box)
            box.size = new Vector3(3f, 2f, 3f);

        if (interactPrompt != null) interactPrompt.SetActive(false);
    }

    private void Start()
    {
        SpawnDisplayAsync(this.GetCancellationTokenOnDestroy()).Forget();
    }

    private void OnDestroy()
    {
        if (_displayInstance != null) Destroy(_displayInstance);
        if (_popupGO != null)         Destroy(_popupGO);
    }

    // ── Display Spawn ────────────────────────────────────────────

    private async UniTaskVoid SpawnDisplayAsync(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(characterPrefabKey)) return;

        var addr = Managers.AddressableManager;
        if (addr == null) return;

        GameObject prefab;
        try
        {
            prefab = await addr.LoadAssetAsync<GameObject>(characterPrefabKey);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[CharacterDisplayStand] 프리팹 로드 실패: {characterPrefabKey}\n{e.Message}");
            return;
        }

        if (prefab == null || ct.IsCancellationRequested) return;

        var tempHost = new GameObject("_CharDisplayTemp");
        tempHost.SetActive(false);

        _displayInstance = Instantiate(
            prefab,
            transform.position + Vector3.up * displayOffsetY,
            Quaternion.Euler(0f, displayRotationY, 0f),
            tempHost.transform);

        DisableGameplayComponents(_displayInstance);

        _displayInstance.transform.SetParent(transform, true);
        Destroy(tempHost);
        _displayInstance.SetActive(true);

        Debug.Log($"[CharacterDisplayStand] 전시 생성: {characterData?.characterName} ({characterPrefabKey})");
    }

    private static void DisableGameplayComponents(GameObject go)
    {
        foreach (var behaviour in go.GetComponentsInChildren<Behaviour>(true))
        {
            if (behaviour is Animator) continue;
            behaviour.enabled = false;
        }

        var rb = go.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.constraints = RigidbodyConstraints.FreezeAll;
        }

        foreach (var col in go.GetComponentsInChildren<Collider>(true))
            col.enabled = false;
    }

    // ── Trigger / Interaction ────────────────────────────────────

    private void OnTriggerEnter(Collider other)
    {
        if (_selected) return;
        var wisp = other.GetComponentInParent<WispController>();
        if (wisp == null) return;

        _nearbyWisp = wisp;
        wisp.SetNearbyStand(this);
        if (interactPrompt != null) interactPrompt.SetActive(true);
    }

    private void OnTriggerExit(Collider other)
    {
        var wisp = other.GetComponentInParent<WispController>();
        if (wisp == null) return;
        if (_nearbyWisp != wisp) return;

        wisp.ClearNearbyStand(this);
        _nearbyWisp = null;
        if (interactPrompt != null) interactPrompt.SetActive(false);
    }

    public void ClearInteraction(WispController wisp)
    {
        if (_nearbyWisp != wisp) return;
        wisp.ClearNearbyStand(this);
        _nearbyWisp = null;
        if (interactPrompt != null) interactPrompt.SetActive(false);
        ClosePopup();
    }

    /// <summary>WispController.Update()에서 F키 감지 시 호출. 캐릭터 정보 팝업을 표시한다.</summary>
    public void TrySelect(WispController wisp)
    {
        if (_selected || wisp == null) return;
        if (_popupOpen) return;

        if (characterData == null && relicClass == null)
        {
            Debug.LogError("[CharacterDisplayStand] characterData/relicClass 둘 다 null — 하나는 할당해야 합니다.");
            return;
        }

        OpenInfoPopup(wisp);
    }

    // ── Character Info Popup ──────────────────────────────────────

    private void OpenInfoPopup(WispController wisp)
    {
        _pendingWisp = wisp;
        _popupOpen   = true;
        _popupGO     = BuildInfoPopup();
    }

    private void ClosePopup()
    {
        _popupOpen   = false;
        _pendingWisp = null;
        if (_popupGO != null) { Destroy(_popupGO); _popupGO = null; }
    }

    private void ConfirmSelect() => ConfirmSelectAsync().Forget();

    private async UniTaskVoid ConfirmSelectAsync()
    {
        if (_selected || _pendingWisp == null) return;

        var wisp = _pendingWisp;
        ClosePopup();

        _selected = true;
        if (interactPrompt != null) interactPrompt.SetActive(false);
        if (_displayInstance != null) { Destroy(_displayInstance); _displayInstance = null; }

        var loadout = AppBootstrapper.Instance?.Loadout;
        if (loadout == null) return;

        loadout.SetCharacter(characterData, characterPrefabKey);
        loadout.SetRelic(relicClass); // null이면 기존 캐릭터 경로 유지 (회귀 0)
        // 유물 경로(characterData null)면 몸이 prefabKey로 베이스 데이터를 자체 로드하므로 스킵
        if (characterData != null)
            Managers.CharacterData?.SetCharacterData(characterData, characterPrefabKey);

        // 캐릭터별 획득 대사 (정보 팝업 닫힌 후, 캐릭터 스폰 전)
        await ShowAcquisitionDialogueAsync();

        // 획득 대사 종료 후 HUD 복원
        UIRootBootstrapper.Instance?.SetHudStartRoomSuppressed(false);

        GameRunBootstrapper.Instance?.SpawnCharacterInStartRoomAsync(
            characterPrefabKey,
            wisp.transform.position,
            wisp.transform.rotation,
            wisp).Forget();

        wisp.ClearNearbyStand(this);
        _nearbyWisp = null;

        var allStands = Object.FindObjectsByType<CharacterDisplayStand>(FindObjectsSortMode.None);
        foreach (var stand in allStands)
        {
            if (stand == this) continue;
            stand.DismissStand();
        }

        Debug.Log($"[CharacterDisplayStand] 선택: {(relicClass != null ? relicClass.DisplayName : characterData?.characterName)} ({characterPrefabKey})");
    }

    /// <summary>
    /// CSV 우선 ({characterPrefabKey}_Pickup 시퀀스), 없으면 CharacterData.AcquisitionDialogue SO 폴백.
    /// 둘 다 없으면 즉시 반환.
    /// </summary>
    private async UniTask ShowAcquisitionDialogueAsync()
    {
        var dlgMgr = Managers.DialogueData;
        if (dlgMgr != null && !dlgMgr.IsInitialized)
            await dlgMgr.InitializeAsync();

        var lines = dlgMgr?.GetLines($"{characterPrefabKey}_Pickup")
                    ?? characterData?.AcquisitionDialogue?.Lines;

        if (lines == null || lines.Length == 0) return;

        var popup = await Managers.UI.ShowPopupUIAndGetAsync<UI_DialoguePopup>();
        if (popup == null) return;

        try { await popup.ShowAsync(lines); }
        catch (System.OperationCanceledException) { }
    }

    /// <summary>다른 캐릭터가 선택됐을 때 이 진열대를 디졸브로 퇴장시킨다.</summary>
    public void DismissStand()
    {
        if (_selected) return;
        _selected = true;
        if (interactPrompt != null) interactPrompt.SetActive(false);
        ClosePopup();
        DissolveEffect.PlayDisappear(gameObject, 0.5f, () => { if (this != null) Destroy(gameObject); });
    }

    private GameObject BuildInfoPopup()
    {
        var rootGO = new GameObject("CharacterSelectPopup");

        var canvas = rootGO.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 15;
        rootGO.AddComponent<CanvasScaler>();
        rootGO.AddComponent<GraphicRaycaster>();

        // 배경 딤
        var dimGO  = new GameObject("Dim");
        dimGO.transform.SetParent(rootGO.transform, false);
        dimGO.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);
        var dimRT  = dimGO.GetComponent<RectTransform>();
        dimRT.anchorMin = Vector2.zero;
        dimRT.anchorMax = Vector2.one;
        dimRT.offsetMin = Vector2.zero;
        dimRT.offsetMax = Vector2.zero;

        // 메인 패널
        var panelGO  = new GameObject("Panel");
        panelGO.transform.SetParent(rootGO.transform, false);
        panelGO.AddComponent<Image>().color = new Color(0.07f, 0.07f, 0.12f, 0.97f);
        var panelRT  = panelGO.GetComponent<RectTransform>();
        panelRT.anchorMin        = new Vector2(0.5f, 0.5f);
        panelRT.anchorMax        = new Vector2(0.5f, 0.5f);
        panelRT.pivot            = new Vector2(0.5f, 0.5f);
        panelRT.sizeDelta        = new Vector2(700f, 460f);
        panelRT.anchoredPosition = Vector2.zero;

        // ── 왼쪽 영역: 초상화 프레임 + 캐릭터 일러스트 ──────────────

        var leftGO = new GameObject("Left");
        leftGO.transform.SetParent(panelGO.transform, false);
        leftGO.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
        var leftRT = leftGO.GetComponent<RectTransform>();
        leftRT.anchorMin = new Vector2(0f, 0f);
        leftRT.anchorMax = new Vector2(0.4f, 1f);
        leftRT.offsetMin = new Vector2(12f, 60f);
        leftRT.offsetMax = new Vector2(-6f, -12f);

        // 초상화 프레임 (금색 테두리)
        var frameGO  = new GameObject("PortraitFrame");
        frameGO.transform.SetParent(leftGO.transform, false);
        frameGO.AddComponent<Image>().color = new Color(0.45f, 0.35f, 0.1f, 1f);
        var frameRT  = frameGO.GetComponent<RectTransform>();
        frameRT.anchorMin = new Vector2(0f, 0.12f);
        frameRT.anchorMax = Vector2.one;
        frameRT.offsetMin = Vector2.zero;
        frameRT.offsetMax = Vector2.zero;

        // 로스터 일러스트 — 유물 우선, 없으면 캐릭터 데이터 폴백
        var illust = relicClass?.RosterIllust ?? relicClass?.Portrait
                     ?? characterData?.rosterIllust ?? characterData?.portrait;
        var innerGO  = new GameObject("PortraitInner");
        innerGO.transform.SetParent(frameGO.transform, false);
        var innerImg = innerGO.AddComponent<Image>();
        innerImg.sprite         = illust;
        innerImg.preserveAspect = illust != null;
        innerImg.color          = illust != null ? Color.white : new Color(0.15f, 0.15f, 0.2f, 1f);
        var innerRT  = innerGO.GetComponent<RectTransform>();
        innerRT.anchorMin = new Vector2(0.03f, 0.03f);
        innerRT.anchorMax = new Vector2(0.97f, 0.97f);
        innerRT.offsetMin = Vector2.zero;
        innerRT.offsetMax = Vector2.zero;

        // 캐릭터 이름 (프레임 아래)
        var charNameGO  = new GameObject("CharName");
        charNameGO.transform.SetParent(leftGO.transform, false);
        var charNameTmp = charNameGO.AddComponent<TextMeshProUGUI>();
        charNameTmp.text      = !string.IsNullOrEmpty(relicClass?.DisplayName)
            ? relicClass.DisplayName
            : (characterData?.characterName ?? "???");
        charNameTmp.fontSize  = 20f;
        charNameTmp.fontStyle = FontStyles.Bold;
        charNameTmp.alignment = TextAlignmentOptions.Center;
        charNameTmp.color     = new Color(1f, 0.84f, 0f);
        var charNameRT  = charNameGO.GetComponent<RectTransform>();
        charNameRT.anchorMin = new Vector2(0f, 0f);
        charNameRT.anchorMax = new Vector2(1f, 0.12f);
        charNameRT.offsetMin = Vector2.zero;
        charNameRT.offsetMax = Vector2.zero;

        // ── 오른쪽 영역: 스탯 + 패시브 ────────────────────────────

        var rightGO = new GameObject("Right");
        rightGO.transform.SetParent(panelGO.transform, false);
        rightGO.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
        var rightRT = rightGO.GetComponent<RectTransform>();
        rightRT.anchorMin = new Vector2(0.4f, 0f);
        rightRT.anchorMax = new Vector2(1f, 1f);
        rightRT.offsetMin = new Vector2(6f, 60f);
        rightRT.offsetMax = new Vector2(-12f, -12f);

        // 스탯 섹션 제목
        float y = 0f;
        AddSectionTitle(rightGO.transform, "스탯", new Color(0.6f, 0.7f, 1f), ref y);

        if (characterData != null)
        {
            AddStatRow(rightGO.transform, "최대 HP",   $"{characterData.maxHealth}", ref y);
            AddStatRow(rightGO.transform, "근거리",    $"{characterData.baseMeleeAttack}", ref y);
            AddStatRow(rightGO.transform, "원거리",    $"{characterData.baseRangedAttack}", ref y);
            AddStatRow(rightGO.transform, "방어력",    $"{characterData.baseDefense}", ref y);
            AddStatRow(rightGO.transform, "행운",      $"{characterData.baseLuck}", ref y);
            AddStatRow(rightGO.transform, "이동속도",  $"{characterData.baseMoveSpeed:F1}", ref y);
        }

        // 구분선
        y -= 8f;
        var divGO = new GameObject("Divider");
        divGO.transform.SetParent(rightGO.transform, false);
        divGO.AddComponent<Image>().color = new Color(0.3f, 0.3f, 0.3f, 0.7f);
        var divRT = divGO.GetComponent<RectTransform>();
        divRT.anchorMin        = new Vector2(0f, 1f);
        divRT.anchorMax        = new Vector2(1f, 1f);
        divRT.pivot            = new Vector2(0.5f, 1f);
        divRT.sizeDelta        = new Vector2(0f, 1f);
        divRT.anchoredPosition = new Vector2(0f, y);
        y -= 6f;

        // 패시브 섹션 — 유물 우선(첫 패시브), 없으면 캐릭터 데이터 폴백
        var passive = (relicClass != null && relicClass.Passives != null && relicClass.Passives.Length > 0)
            ? relicClass.Passives[0]
            : characterData?.passive;
        AddSectionTitle(rightGO.transform, "패시브", new Color(0.5f, 1f, 0.6f), ref y);

        if (passive != null)
        {
            var passiveNameGO  = new GameObject("PassiveName");
            passiveNameGO.transform.SetParent(rightGO.transform, false);
            var passiveNameTmp = passiveNameGO.AddComponent<TextMeshProUGUI>();
            passiveNameTmp.text      = passive.passiveName;
            passiveNameTmp.fontSize  = 14f;
            passiveNameTmp.fontStyle = FontStyles.Bold;
            passiveNameTmp.color     = Color.white;
            var passiveNameRT  = passiveNameGO.GetComponent<RectTransform>();
            passiveNameRT.anchorMin        = new Vector2(0f, 1f);
            passiveNameRT.anchorMax        = new Vector2(1f, 1f);
            passiveNameRT.pivot            = new Vector2(0.5f, 1f);
            passiveNameRT.sizeDelta        = new Vector2(0f, 22f);
            passiveNameRT.anchoredPosition = new Vector2(0f, y);
            y -= 24f;

            var passiveDescGO  = new GameObject("PassiveDesc");
            passiveDescGO.transform.SetParent(rightGO.transform, false);
            var passiveDescTmp = passiveDescGO.AddComponent<TextMeshProUGUI>();
            passiveDescTmp.text               = passive.description;
            passiveDescTmp.fontSize           = 12f;
            passiveDescTmp.color              = new Color(0.75f, 0.75f, 0.75f);
            passiveDescTmp.enableWordWrapping = true;
            var passiveDescRT  = passiveDescGO.GetComponent<RectTransform>();
            passiveDescRT.anchorMin        = new Vector2(0f, 1f);
            passiveDescRT.anchorMax        = new Vector2(1f, 1f);
            passiveDescRT.pivot            = new Vector2(0.5f, 1f);
            passiveDescRT.sizeDelta        = new Vector2(0f, 60f);
            passiveDescRT.anchoredPosition = new Vector2(0f, y);
        }
        else
        {
            var noPassiveGO  = new GameObject("NoPassive");
            noPassiveGO.transform.SetParent(rightGO.transform, false);
            var noPassiveTmp = noPassiveGO.AddComponent<TextMeshProUGUI>();
            noPassiveTmp.text      = "—";
            noPassiveTmp.fontSize  = 13f;
            noPassiveTmp.color     = new Color(0.5f, 0.5f, 0.5f);
            var noPassiveRT  = noPassiveGO.GetComponent<RectTransform>();
            noPassiveRT.anchorMin        = new Vector2(0f, 1f);
            noPassiveRT.anchorMax        = new Vector2(1f, 1f);
            noPassiveRT.pivot            = new Vector2(0.5f, 1f);
            noPassiveRT.sizeDelta        = new Vector2(0f, 22f);
            noPassiveRT.anchoredPosition = new Vector2(0f, y);
        }

        // ── 버튼 영역 (패널 하단) ────────────────────────────────────

        var selectBtnGO  = new GameObject("SelectBtn");
        selectBtnGO.transform.SetParent(panelGO.transform, false);
        var selectBtnImg = selectBtnGO.AddComponent<Image>();
        selectBtnImg.color = new Color(0.15f, 0.42f, 0.72f);
        var selectBtn    = selectBtnGO.AddComponent<Button>();
        selectBtn.targetGraphic = selectBtnImg;
        selectBtn.onClick.AddListener(ConfirmSelect);
        var selectBtnRT  = selectBtnGO.GetComponent<RectTransform>();
        selectBtnRT.anchorMin        = new Vector2(0.5f, 0f);
        selectBtnRT.anchorMax        = new Vector2(0.5f, 0f);
        selectBtnRT.pivot            = new Vector2(1f, 0f);
        selectBtnRT.sizeDelta        = new Vector2(160f, 44f);
        selectBtnRT.anchoredPosition = new Vector2(-8f, 12f);

        var selectTextGO  = new GameObject("Text");
        selectTextGO.transform.SetParent(selectBtnGO.transform, false);
        var selectTmp = selectTextGO.AddComponent<TextMeshProUGUI>();
        selectTmp.text      = "선택";
        selectTmp.fontSize  = 18f;
        selectTmp.fontStyle = FontStyles.Bold;
        selectTmp.alignment = TextAlignmentOptions.Center;
        selectTmp.color     = Color.white;
        var selectTextRT    = selectTextGO.GetComponent<RectTransform>();
        selectTextRT.anchorMin = Vector2.zero;
        selectTextRT.anchorMax = Vector2.one;
        selectTextRT.offsetMin = Vector2.zero;
        selectTextRT.offsetMax = Vector2.zero;

        var closeBtnGO  = new GameObject("CloseBtn");
        closeBtnGO.transform.SetParent(panelGO.transform, false);
        var closeBtnImg = closeBtnGO.AddComponent<Image>();
        closeBtnImg.color = new Color(0.25f, 0.25f, 0.28f);
        var closeBtn    = closeBtnGO.AddComponent<Button>();
        closeBtn.targetGraphic = closeBtnImg;
        closeBtn.onClick.AddListener(ClosePopup);
        var closeBtnRT  = closeBtnGO.GetComponent<RectTransform>();
        closeBtnRT.anchorMin        = new Vector2(0.5f, 0f);
        closeBtnRT.anchorMax        = new Vector2(0.5f, 0f);
        closeBtnRT.pivot            = new Vector2(0f, 0f);
        closeBtnRT.sizeDelta        = new Vector2(160f, 44f);
        closeBtnRT.anchoredPosition = new Vector2(8f, 12f);

        var closeTextGO  = new GameObject("Text");
        closeTextGO.transform.SetParent(closeBtnGO.transform, false);
        var closeTmp = closeTextGO.AddComponent<TextMeshProUGUI>();
        closeTmp.text      = "닫기";
        closeTmp.fontSize  = 18f;
        closeTmp.alignment = TextAlignmentOptions.Center;
        closeTmp.color     = Color.white;
        var closeTextRT    = closeTextGO.GetComponent<RectTransform>();
        closeTextRT.anchorMin = Vector2.zero;
        closeTextRT.anchorMax = Vector2.one;
        closeTextRT.offsetMin = Vector2.zero;
        closeTextRT.offsetMax = Vector2.zero;

        return rootGO;
    }

    private static void AddSectionTitle(Transform parent, string title, Color color, ref float y)
    {
        var go  = new GameObject($"Title_{title}");
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text      = title;
        tmp.fontSize  = 15f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color     = color;
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin        = new Vector2(0f, 1f);
        rt.anchorMax        = new Vector2(1f, 1f);
        rt.pivot            = new Vector2(0.5f, 1f);
        rt.sizeDelta        = new Vector2(0f, 24f);
        rt.anchoredPosition = new Vector2(0f, y);
        y -= 26f;
    }

    private static void AddStatRow(Transform parent, string label, string value, ref float y)
    {
        var rowGO = new GameObject($"Row_{label}");
        rowGO.transform.SetParent(parent, false);
        rowGO.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
        var rowRT = rowGO.GetComponent<RectTransform>();
        rowRT.anchorMin        = new Vector2(0f, 1f);
        rowRT.anchorMax        = new Vector2(1f, 1f);
        rowRT.pivot            = new Vector2(0.5f, 1f);
        rowRT.sizeDelta        = new Vector2(0f, 22f);
        rowRT.anchoredPosition = new Vector2(0f, y);
        y -= 24f;

        var labelGO  = new GameObject("Label");
        labelGO.transform.SetParent(rowGO.transform, false);
        var labelTmp = labelGO.AddComponent<TextMeshProUGUI>();
        labelTmp.text     = label;
        labelTmp.fontSize = 13f;
        labelTmp.color    = new Color(0.65f, 0.65f, 0.65f);
        var labelRT = labelGO.GetComponent<RectTransform>();
        labelRT.anchorMin = Vector2.zero;
        labelRT.anchorMax = new Vector2(0.55f, 1f);
        labelRT.offsetMin = Vector2.zero;
        labelRT.offsetMax = Vector2.zero;

        var valueGO  = new GameObject("Value");
        valueGO.transform.SetParent(rowGO.transform, false);
        var valueTmp = valueGO.AddComponent<TextMeshProUGUI>();
        valueTmp.text      = value;
        valueTmp.fontSize  = 13f;
        valueTmp.alignment = TextAlignmentOptions.Right;
        valueTmp.color     = Color.white;
        var valueRT = valueGO.GetComponent<RectTransform>();
        valueRT.anchorMin = new Vector2(0.55f, 0f);
        valueRT.anchorMax = Vector2.one;
        valueRT.offsetMin = Vector2.zero;
        valueRT.offsetMax = Vector2.zero;
    }
}
